namespace DeviceIntelligenceAI.Ingestion.SemanticIndex;

/// <summary>
/// Factory for creating the appropriate semantic index implementation.
/// Uses WindowsSemanticIndex on Copilot+ PCs, falls back to LocalSemanticIndex elsewhere.
/// </summary>
public static class SemanticIndexFactory
{
    /// <summary>
    /// Create the best available semantic index for the current environment.
    /// </summary>
    /// <param name="indexName">Name for the Windows AppContentIndexer index.</param>
    /// <param name="forceLocal">Force the local keyword-based fallback.</param>
    public static ISemanticIndex Create(string indexName = "device-intelligence-facts", bool forceLocal = false)
    {
        if (forceLocal)
        {
            return new LocalSemanticIndex();
        }

        try
        {
            if (WindowsSemanticIndex.IsAvailable())
            {
                return WindowsSemanticIndex.Create(indexName);
            }
        }
        catch (Exception)
        {
            // Fall through to local
        }

        return new LocalSemanticIndex();
    }
}
