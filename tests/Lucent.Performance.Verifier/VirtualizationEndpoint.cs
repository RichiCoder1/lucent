internal static class VirtualizationEndpoint
{
    internal static void Verify(string[] expectedVisible, string[] visible, int realizedCount)
    {
        if (realizedCount == 0)
            throw new InvalidOperationException("Virtualization endpoint has no realized rows.");
        if (!visible.SequenceEqual(expectedVisible))
            throw new InvalidOperationException(
                $"Virtualization endpoint visible rows were [{string.Join(", ", visible)}], expected [{string.Join(", ", expectedVisible)}]."
            );
    }
}
