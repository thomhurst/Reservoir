namespace Reservoir.Docs.Samples.Configuration;

internal static class ConfigurationExamples
{
    internal static int GetDefaultCapacity()
    {
        int defaultCapacity = Math.Max(32, 2 * Environment.ProcessorCount);
        return defaultCapacity;
    }
}
