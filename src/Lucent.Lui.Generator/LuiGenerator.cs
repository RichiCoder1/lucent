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
    private static readonly DiagnosticDescriptor InvalidInput = new DiagnosticDescriptor("LUI4001", "Unreadable .lui input", "LUI input '{0}' is unreadable", "Lucent.Lui", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor DuplicateInput = new DiagnosticDescriptor("LUI4002", "Duplicate .lui probe input", "LUI probe input '{0}' has duplicate logical path '{1}'", "Lucent.Lui", DiagnosticSeverity.Error, true);
    private static readonly DiagnosticDescriptor InvalidLogicalPath = new DiagnosticDescriptor("LUI4003", "Invalid .lui logical path", "LUI probe input '{0}' has invalid logical path '{1}'", "Lucent.Lui", DiagnosticSeverity.Error, true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var inputs = context.AdditionalTextsProvider
            .Where(static text => text.Path.EndsWith(".lui", StringComparison.OrdinalIgnoreCase))
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Select(static (input, cancellationToken) => ParseInput.Read(input.Left, input.Right.GetOptions(input.Left), cancellationToken));
        context.RegisterSourceOutput(inputs.Collect(), static (production, inputs) => Emit(production, inputs));
    }

    private static void Emit(SourceProductionContext production, ImmutableArray<ParseInput> inputs)
    {
        foreach (var input in inputs.Where(input => !input.IsReadable)) production.ReportDiagnostic(Diagnostic.Create(InvalidInput, Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan()), input.Path));
        foreach (var input in inputs.Where(input => !input.IsLogicalPathValid)) production.ReportDiagnostic(Diagnostic.Create(InvalidLogicalPath, Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan()), input.Path, input.LogicalPath));
        foreach (var input in inputs.Where(input => input.IsReadable && input.IsLogicalPathValid)) foreach (var diagnostic in input.Document!.Diagnostics) production.ReportDiagnostic(Diagnostic.Create(ParseDescriptor(diagnostic), input.Location(diagnostic.Span), diagnostic.Message));
        var duplicates = inputs.Where(input => input.IsReadable && input.IsLogicalPathValid).GroupBy(input => input.LogicalPath, StringComparer.Ordinal).Where(group => group.Count() > 1).SelectMany(group => group);
        var duplicatePaths = new System.Collections.Generic.HashSet<string>(duplicates.Select(input => input.Path), StringComparer.Ordinal);
        foreach (var input in inputs.Where(input => duplicatePaths.Contains(input.Path))) production.ReportDiagnostic(Diagnostic.Create(DuplicateInput, Location.Create(input.Path, new TextSpan(0, 0), new LinePositionSpan()), input.Path, input.LogicalPath));
        foreach (var input in inputs.Where(input => input.IsReadable && input.IsLogicalPathValid && input.Document!.Diagnostics.Count == 0 && !duplicatePaths.Contains(input.Path))) production.AddSource(LuiProbeEmitter.HintName(new LuiDocumentIdentity(input.LogicalPath)), LuiProbeEmitter.Emit(new LuiDocumentIdentity(input.LogicalPath), input.Source));
    }

    private static DiagnosticDescriptor ParseDescriptor(LuiDiagnostic diagnostic) => new DiagnosticDescriptor(diagnostic.Id, "Invalid .lui syntax", "{0}", "Lucent.Lui", DiagnosticSeverity.Error, true);

    private sealed class ParseInput : IEquatable<ParseInput>
    {
        private ParseInput(string path, string logicalPath, string source, SourceText? sourceText, bool readable, bool logicalPathValid) { Path = path; LogicalPath = logicalPath; Source = source; SourceText = sourceText; IsReadable = readable; IsLogicalPathValid = logicalPathValid; Document = readable && logicalPathValid ? LuiParser.Parse(source) : null; }
        public string Path { get; } public string LogicalPath { get; } public string Source { get; } public SourceText? SourceText { get; } public bool IsReadable { get; } public bool IsLogicalPathValid { get; } public LuiDocumentSyntax? Document { get; }
        public static ParseInput Read(AdditionalText text, AnalyzerConfigOptions options, System.Threading.CancellationToken cancellationToken)
        {
            var source = text.GetText(cancellationToken);
            var hasLogicalPath = options.TryGetValue("build_metadata.AdditionalFiles.LucentLuiLogicalPath", out var logicalPath);
            var path = hasLogicalPath ? logicalPath! : System.IO.Path.GetFileName(text.Path);
            try { path = new LuiDocumentIdentity(path).LogicalPath; return new ParseInput(text.Path, path, source?.ToString() ?? "", source, source != null, true); }
            catch (ArgumentException) { return new ParseInput(text.Path, path, source?.ToString() ?? "", source, source != null, false); }
        }
        public Microsoft.CodeAnalysis.Location Location(LuiSpan span) { var source = SourceText!; var bounded = new TextSpan(Math.Min(span.Start, source.Length), Math.Min(span.Length, source.Length - Math.Min(span.Start, source.Length))); return Microsoft.CodeAnalysis.Location.Create(Path, bounded, source.Lines.GetLinePositionSpan(bounded)); }
        public bool Equals(ParseInput? other) => other != null && Path == other.Path && LogicalPath == other.LogicalPath && Source == other.Source && IsReadable == other.IsReadable && IsLogicalPathValid == other.IsLogicalPathValid;
        public override bool Equals(object? obj) => Equals(obj as ParseInput);
        public override int GetHashCode() => (Path + "\0" + LogicalPath + "\0" + Source + "\0" + IsReadable + "\0" + IsLogicalPathValid).GetHashCode();
    }
}
