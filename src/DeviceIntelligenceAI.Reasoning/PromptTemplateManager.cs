using System.Reflection;

namespace DeviceIntelligenceAI.Reasoning;

/// <summary>
/// Manages prompt templates for different reasoning scenarios.
/// Templates are embedded resources with {{CONTEXT}} placeholders.
/// </summary>
public sealed class PromptTemplateManager
{
    private readonly Dictionary<string, string> _templates = new();

    public PromptTemplateManager()
    {
        LoadEmbeddedTemplates();
    }

    /// <summary>
    /// Get a rendered prompt with context substituted.
    /// </summary>
    public string Render(string templateName, string context)
    {
        if (!_templates.TryGetValue(templateName, out var template))
            throw new ArgumentException($"Unknown template: '{templateName}'. Available: {string.Join(", ", _templates.Keys)}");

        return template.Replace("{{CONTEXT}}", context);
    }

    /// <summary>
    /// Get available template names.
    /// </summary>
    public IReadOnlyList<string> AvailableTemplates => _templates.Keys.ToList();

    private void LoadEmbeddedTemplates()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = "DeviceIntelligenceAI.Reasoning.PromptTemplates.";

        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.StartsWith(prefix) || !resourceName.EndsWith(".md"))
                continue;

            var templateName = resourceName[prefix.Length..^3]; // Remove prefix and .md suffix
            using var stream = assembly.GetManifestResourceStream(resourceName)!;
            using var reader = new StreamReader(stream);
            _templates[templateName] = reader.ReadToEnd();
        }
    }
}
