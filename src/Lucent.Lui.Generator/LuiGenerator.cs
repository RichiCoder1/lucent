using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using Lucent.Lui.Compiler;

namespace Lucent.Lui.Generator;

[Generator]
public sealed class LuiGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor InvalidInput = new DiagnosticDescriptor("LUI4001", "Unreadable or empty .lui probe input", "LUI probe input '{0}' is unreadable or empty", "Lucent.Lui", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor DuplicateInput = new DiagnosticDescriptor("LUI4002", "Duplicate .lui probe input", "LUI probe input '{0}' has duplicate logical path '{1}'", "Lucent.Lui", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor InvalidLogicalPath = new DiagnosticDescriptor("LUI4003", "Invalid .lui logical path", "LUI probe input '{0}' has invalid logical path '{1}'", "Lucent.Lui", DiagnosticSeverity.Error, true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var inputs = context.AdditionalTextsProvider
            .Where(static text => text.Path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (input, cancellationToken) => ProbeInput.Read(input.Left, input.Right.GetOptions(input.Left), cancellationToken));
        context.RegisterSourceOutput(inputs.Collect(), static (production, inputs) => Emit(production, inputs));
    }

    private static void Emit(SourceProductionContext production, ImmutableArray<ProbeInput> inputs)
    {
        foreach (var input in inputs.Where(input => !input.IsReadable)) production.ReportDiagnostic(Diagnostic.Create(InvalidInput, Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan()), input.Path));
        foreach (var input in inputs.Where(input => input.IsReadable && input.IsEmpty)) production.ReportDiagnostic(Diagnostic.Create(InvalidInput, Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan()), input.Path));
        foreach (var input in inputs.Where(input => !input.IsLogicalPathValid)) production.ReportDiagnostic(Diagnostic.Create(InvalidLogicalPath, Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan()), input.Path, input.LogicalPath));
        var duplicates = inputs.Where(input => input.IsReadable && !input.IsEmpty && input.IsLogicalPathValid).GroupBy(input => input.LogicalPath, StringComparer.Ordinal).Where(group => group.Count() > 1).SelectMany(group => group);
        var duplicatePaths = new System.Collections.Generic.HashSet<string>(duplicates.Select(input => input.Path), StringComparer.Ordinal);
        foreach (var input in inputs.Where(input => duplicatePaths.Contains(input.Path))) production.ReportDiagnostic(Diagnostic.Create(DuplicateInput, Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan()), input.Path, input.LogicalPath));
        foreach (var input in inputs.Where(input => input.IsReadable && !input.IsEmpty && input.IsLogicalPathValid && !duplicatePaths.Contains(input.Path))) production.AddSource(LuiProbeEmitter.HintName(new LuiDocumentIdentity(input.LogicalPath)), LuiProbeEmitter.Emit(new LuiDocumentIdentity(input.LogicalPath), input.Source));
    }

    private sealed class ProbeInput : IEquatable<ProbeInput>
    {
        private ProbeInput(string path, string logicalPath, string source, bool readable, bool empty, bool logicalPathValid) { Path = path; LogicalPath = logicalPath; Source = source; IsReadable = readable; IsEmpty = empty; IsLogicalPathValid = logicalPathValid; }
        public string Path { get; } public string LogicalPath { get; } public string Source { get; } public bool IsReadable { get; } public bool IsEmpty { get; } public bool IsLogicalPathValid { get; }
        public static ProbeInput Read(AdditionalText text, AnalyzerConfigOptions options, System.Threading.CancellationToken cancellationToken)
        {
            var source = text.GetText(cancellationToken);
            var hasLogicalPath = options.TryGetValue("build_metadata.AdditionalFiles.LucentLuiLogicalPath", out var logicalPath);
            var path = hasLogicalPath ? logicalPath! : System.IO.Path.GetFileName(text.Path);
            try { path = new LuiDocumentIdentity(path).LogicalPath; return new ProbeInput(text.Path, path, source?.ToString() ?? "", source != null, source == null || source.Length == 0, true); }
            catch (ArgumentException) { return new ProbeInput(text.Path, path, source?.ToString() ?? "", source != null, source == null || source.Length == 0, false); }
        }
        public bool Equals(ProbeInput? other) => other != null && Path == other.Path && LogicalPath == other.LogicalPath && Source == other.Source && IsReadable == other.IsReadable && IsEmpty == other.IsEmpty && IsLogicalPathValid == other.IsLogicalPathValid;
        public override bool Equals(object? obj) => Equals(obj as ProbeInput);
        public override int GetHashCode() => (Path + "\0" + LogicalPath + "\0" + Source + "\0" + IsReadable + "\0" + IsEmpty + "\0" + IsLogicalPathValid).GetHashCode();
    }
}
