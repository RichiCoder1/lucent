using Lucent.Lui.Compiler;

namespace Lucent.Lui.LanguageServer;

/// <summary>Owns the authored source and its same-offset named-component projection.</summary>
internal sealed class LuiNamedDocument
{
    private LuiNamedDocument(LuiProjectDocument authored, LuiAuthoredSourceProjection projection)
    {
        Authored = authored;
        Projection = projection;
    }

    internal LuiProjectDocument Authored { get; }

    internal LuiAuthoredSourceProjection Projection { get; }

    internal bool Success => Projection.Success;

    internal static LuiNamedDocument Create(
        LuiProjectDocument authored,
        LuiAuthoredSourceProjection projection
    )
    {
        ArgumentNullException.ThrowIfNull(authored);
        ArgumentNullException.ThrowIfNull(projection);
        return new LuiNamedDocument(authored, projection);
    }
}
