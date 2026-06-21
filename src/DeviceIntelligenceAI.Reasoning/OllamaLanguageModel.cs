using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace DeviceIntelligenceAI.Reasoning;

/// <summary>
/// Language model implementation using Ollama's local HTTP API.
/// Provides real SLM inference for devices without Windows AI (Phi Silica) support.
/// Fallback chain: Phi Silica → Ollama → Mock.
/// </summary>
public sealed class OllamaLanguageModel : ILanguageModel
{
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _baseUrl;

    public OllamaLanguageModel(string model = "phi3:mini", string baseUrl = "http://localhost:11434")
    {
        _model = model;
        _baseUrl = baseUrl;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
    }

    public async Task<string> GenerateAsync(string prompt, CancellationToken ct = default)
    {
        var request = new
        {
            model = _model,
            prompt,
            stream = false,
            options = new { temperature = 0.3, num_predict = 512 }
        };

        var response = await SendRequestAsync("/api/generate", request, ct);
        return response.GetProperty("response").GetString() ?? "";
    }

    public async Task<string> GenerateAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        var request = new
        {
            model = _model,
            prompt = userPrompt,
            system = systemPrompt,
            stream = false,
            options = new { temperature = 0.3, num_predict = 512 }
        };

        var response = await SendRequestAsync("/api/generate", request, ct);
        return response.GetProperty("response").GetString() ?? "";
    }

    public async IAsyncEnumerable<string> GenerateStreamAsync(string prompt, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new
        {
            model = _model,
            prompt,
            stream = true,
            options = new { temperature = 0.3, num_predict = 512 }
        };

        var json = JsonSerializer.Serialize(request);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/generate") { Content = content };

        using var response = await _httpClient.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            ct.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(ct);
            if (string.IsNullOrWhiteSpace(line)) continue;

            string? chunk = null;
            bool done = false;
            try
            {
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("response", out var responseProp))
                    chunk = responseProp.GetString();
                if (doc.RootElement.TryGetProperty("done", out var doneProp) && doneProp.ValueKind == JsonValueKind.True)
                    done = true;
            }
            catch (JsonException)
            {
                continue; // Skip malformed lines defensively.
            }

            if (!string.IsNullOrEmpty(chunk))
                yield return chunk!;
            if (done)
                yield break;
        }
    }

    public async Task<bool> IsReadyAsync(CancellationToken ct = default)    {
        try
        {
            var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags", ct);
            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync(ct);
            var doc = JsonDocument.Parse(json);
            var models = doc.RootElement.GetProperty("models");

            foreach (var model in models.EnumerateArray())
            {
                var name = model.GetProperty("name").GetString() ?? "";
                if (name == _model || name.StartsWith(_model.Split(':')[0]))
                    return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check if Ollama server is reachable and has a model available.
    /// </summary>
    public static async Task<bool> IsAvailableAsync(string baseUrl = "http://localhost:11434")
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = await client.GetAsync($"{baseUrl}/api/tags");
            if (!response.IsSuccessStatusCode) return false;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("models").GetArrayLength() > 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Check if Ollama is reachable (synchronous, for startup).
    /// </summary>
    public static bool IsAvailable(string baseUrl = "http://localhost:11434")
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
            var response = client.GetAsync($"{baseUrl}/api/tags").GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode) return false;

            var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var doc = JsonDocument.Parse(json);
            return doc.RootElement.GetProperty("models").GetArrayLength() > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<JsonElement> SendRequestAsync(string endpoint, object payload, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync($"{_baseUrl}{endpoint}", content, ct);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
