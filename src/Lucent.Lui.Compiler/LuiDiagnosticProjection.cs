using Microsoft.CodeAnalysis;

namespace Lucent.Lui.Compiler;

/// <summary>Canonical diagnostic projection shared by build and editor hosts.</summary>
public static class LuiDiagnosticProjection
{
    /// <summary>Projects one project-index diagnostic onto its authored document.</summary>
    public static LuiDiagnostic Index(LuiProjectComponentIndex.Diagnostic diagnostic)
    {
        var (id, message) = diagnostic.Kind switch
        {
            LuiProjectComponentIndex.DiagnosticKind.DuplicateLogicalPath => (
                "LUI4002",
                "LUI input '"
                    + diagnostic.Document.Path
                    + "' has duplicate logical path '"
                    + diagnostic.Value
                    + "'"
            ),
            LuiProjectComponentIndex.DiagnosticKind.DuplicateComponent => (
                "LUI4004",
                "LUI component '" + diagnostic.Value + "' is declared more than once"
            ),
            _ => (
                "LUI4005",
                "LUI component '" + diagnostic.Value + "' has an unresolved signature"
            ),
        };
        return new LuiDiagnostic(id, message, diagnostic.Span);
    }

    /// <summary>Projects an unreadable additional-file input.</summary>
    public static LuiDiagnostic Unreadable(string path) =>
        new("LUI4001", "LUI input '" + path + "' is unreadable", new LuiSpan(0, 0));

    /// <summary>Projects an invalid additional-file logical path.</summary>
    public static LuiDiagnostic InvalidLogicalPath(string path, string logicalPath) =>
        new(
            "LUI4003",
            "LUI input '" + path + "' has invalid logical path '" + logicalPath + "'",
            new LuiSpan(0, 0)
        );
}
