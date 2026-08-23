namespace Lucent.LanguageServer;

internal sealed record LanguageServerRequestMetric(
    string Method,
    TimeSpan Elapsed,
    long AllocatedBytes,
    int ProjectGenerationCount);
