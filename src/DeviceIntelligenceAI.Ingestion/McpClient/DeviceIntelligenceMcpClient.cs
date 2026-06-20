using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace DeviceIntelligenceAI.Ingestion.McpClient;

/// <summary>
/// JSON-RPC client that communicates with the Device Intelligence MCP server over stdio.
/// </summary>
public sealed class DeviceIntelligenceMcpClient : IDisposable
{
    private readonly Process _process;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _requestId;

    public DeviceIntelligenceMcpClient(string mcpServerPath)
    {
        if (!File.Exists(mcpServerPath))
            throw new FileNotFoundException($"MCP server not found at: {mcpServerPath}");

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = mcpServerPath,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        _process.Start();
    }

    /// <summary>
    /// Initialize the MCP connection with the protocol handshake.
    /// </summary>
    public async Task<JsonDocument> InitializeAsync(CancellationToken ct = default)
    {
        var response = await SendRequestAsync("initialize", new
        {
            protocolVersion = "2024-11-05",
            capabilities = new { },
            clientInfo = new { name = "DeviceIntelligenceAI", version = "1.0.0" }
        }, ct);

        // Send initialized notification
        await SendNotificationAsync("notifications/initialized", null, ct);
        return response;
    }

    /// <summary>
    /// Call an MCP tool and return the result.
    /// </summary>
    public async Task<JsonDocument> CallToolAsync(string toolName, object? arguments = null, CancellationToken ct = default)
    {
        return await SendRequestAsync("tools/call", new
        {
            name = toolName,
            arguments = arguments ?? new { }
        }, ct);
    }

    /// <summary>
    /// Read an MCP resource by URI.
    /// </summary>
    public async Task<JsonDocument> ReadResourceAsync(string uri, CancellationToken ct = default)
    {
        return await SendRequestAsync("resources/read", new { uri }, ct);
    }

    /// <summary>
    /// Build a device twin via MCP.
    /// </summary>
    public async Task<JsonDocument> BuildDeviceTwinAsync(bool saveSnapshot = false, CancellationToken ct = default)
    {
        return await CallToolAsync("build_device_twin", new { saveSnapshot }, ct);
    }

    /// <summary>
    /// Get device health summary.
    /// </summary>
    public async Task<JsonDocument> GetDeviceHealthAsync(CancellationToken ct = default)
    {
        return await CallToolAsync("get_device_health", null, ct);
    }

    /// <summary>
    /// Investigate update errors.
    /// </summary>
    public async Task<JsonDocument> InvestigateUpdateErrorsAsync(CancellationToken ct = default)
    {
        return await CallToolAsync("investigate_update_errors", null, ct);
    }

    /// <summary>
    /// Compare recent changes.
    /// </summary>
    public async Task<JsonDocument> CompareRecentChangesAsync(CancellationToken ct = default)
    {
        return await CallToolAsync("compare_recent_changes", null, ct);
    }

    private async Task<JsonDocument> SendRequestAsync(string method, object? @params, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _requestId);
        var request = new
        {
            jsonrpc = "2.0",
            id,
            method,
            @params
        };

        var json = JsonSerializer.Serialize(request);
        await _sendLock.WaitAsync(ct);
        try
        {
            await _process.StandardInput.WriteLineAsync(json);
            await _process.StandardInput.FlushAsync();
        }
        finally
        {
            _sendLock.Release();
        }

        // Read response line
        var responseLine = await _process.StandardOutput.ReadLineAsync(ct)
            ?? throw new InvalidOperationException("MCP server closed connection");

        return JsonDocument.Parse(responseLine);
    }

    private async Task SendNotificationAsync(string method, object? @params, CancellationToken ct)
    {
        var notification = new
        {
            jsonrpc = "2.0",
            method,
            @params
        };

        var json = JsonSerializer.Serialize(notification);
        await _sendLock.WaitAsync(ct);
        try
        {
            await _process.StandardInput.WriteLineAsync(json);
            await _process.StandardInput.FlushAsync();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public void Dispose()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.StandardInput.Close();
                _process.WaitForExit(5000);
                if (!_process.HasExited) _process.Kill();
            }
        }
        catch { }
        _process.Dispose();
        _sendLock.Dispose();
    }
}
