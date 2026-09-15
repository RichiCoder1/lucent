using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Lucent.Lui.Compiler;

/// <summary>Outcome of a source-preserving formatting operation.</summary>
public enum LuiFormattingStatus
{
    /// <summary>The operation succeeded and the source already matches its output.</summary>
    Clean,

    /// <summary>The operation succeeded and has a safe replacement.</summary>
    Changed,

    /// <summary>Malformed or unsupported source cannot safely be formatted.</summary>
    Unavailable,

    /// <summary>The formatter could not complete the operation.</summary>
    Failed,
}

/// <summary>A replacement in the original UTF-16 source coordinate space.</summary>
public sealed class LuiSourceEdit
{
    /// <summary>Creates an authored source replacement.</summary>
    public LuiSourceEdit(LuiSpan span, string newText)
    {
        Span = span;
        NewText = newText;
    }

    /// <summary>Original source span to replace.</summary>
    public LuiSpan Span { get; }

    /// <summary>Replacement source text.</summary>
    public string NewText { get; }
}

/// <summary>Formatting output with honest availability and safe edits.</summary>
public sealed class LuiFormattingResult
{
    internal LuiFormattingResult(
        LuiFormattingStatus status,
        string source,
        string text,
        IReadOnlyList<LuiDiagnostic> diagnostics
    )
    {
        Status = status;
        Text = text;
        Diagnostics = diagnostics;
        Edits =
            status == LuiFormattingStatus.Changed
                ? new[] { new LuiSourceEdit(new LuiSpan(0, source.Length), text) }
                : Array.Empty<LuiSourceEdit>();
    }

    /// <summary>Whether formatting succeeded, changed source, or was unavailable/failed.</summary>
    public LuiFormattingStatus Status { get; }

    /// <summary>Safe output, or the unchanged input when formatting cannot complete.</summary>
    public string Text { get; }

    /// <summary>Source diagnostics explaining unavailable or failed formatting.</summary>
    public IReadOnlyList<LuiDiagnostic> Diagnostics { get; }

    /// <summary>Safe original-coordinate edits; empty for every non-changed outcome.</summary>
    public IReadOnlyList<LuiSourceEdit> Edits { get; }
}

public static partial class LuiFormatter
{
    /// <summary>Formats a document only when syntax, comments, tokens and meaningful text are preserved.</summary>
    public static LuiFormattingResult FormatDocument(
        string source,
        LuiLineEnding lineEnding = LuiLineEnding.Preserve,
        CancellationToken cancellationToken = default
    ) =>
        FormatSafely(
            source,
            null,
            new LuiFormattingOptions(lineEnding: lineEnding),
            cancellationToken
        );

    /// <summary>Formats with the shared project's indentation, width and line-ending policy.</summary>
    public static LuiFormattingResult FormatDocument(
        string source,
        LuiFormattingOptions options,
        CancellationToken cancellationToken = default
    ) => FormatSafely(source, null, options, cancellationToken);

    /// <summary>Formats a safe complete syntax selection, returning explicit unavailability when none applies.</summary>
    public static LuiFormattingResult FormatSelection(
        string source,
        LuiSpan range,
        LuiLineEnding lineEnding = LuiLineEnding.Preserve,
        CancellationToken cancellationToken = default
    ) =>
        FormatSafely(
            source,
            range,
            new LuiFormattingOptions(lineEnding: lineEnding),
            cancellationToken
        );

    /// <summary>Formats complete selected boundaries with the shared project policy.</summary>
    public static LuiFormattingResult FormatSelection(
        string source,
        LuiSpan range,
        LuiFormattingOptions options,
        CancellationToken cancellationToken = default
    ) => FormatSafely(source, range, options, cancellationToken);

    private static LuiFormattingResult FormatSafely(
        string source,
        LuiSpan? range,
        LuiFormattingOptions options,
        CancellationToken cancellationToken
    )
    {
        if (source is null)
            throw new ArgumentNullException(nameof(source));
        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (
            range is { } requested
            && (
                requested.Start < 0
                || requested.Length < 0
                || requested.Start > source.Length - requested.Length
            )
        )
            throw new ArgumentOutOfRangeException(nameof(range));
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var document = LuiParser.Parse(source);
            if (document.Diagnostics.Count != 0)
            {
                var projection = LuiAuthoredSourceProjection.Project(source);
                if (projection.Success)
                    return FormatProjected(source, projection, range, options, cancellationToken);
                return new LuiFormattingResult(
                    LuiFormattingStatus.Unavailable,
                    source,
                    source,
                    document.Diagnostics
                );
            }
            if (
                range is { } selected
                && !Nodes(document)
                    .Any(node => node.Span.Start >= selected.Start && node.Span.End <= selected.End)
            )
                return Unavailable(
                    "LUI6002",
                    "The selected range contains no complete supported formatting boundary."
                );
            var directives = new LuiFormattingDirectives(document);
            if (directives.Diagnostics.Count != 0)
                return new LuiFormattingResult(
                    LuiFormattingStatus.Unavailable,
                    source,
                    source,
                    directives.Diagnostics
                );
            var layout = new LuiDocumentLayout(document, options, directives);
            var formatted = range is { } selection
                ? FormatSelectedBoundaries(source, document, selection, layout, options)
                : layout.Format();
            cancellationToken.ThrowIfCancellationRequested();
            if (
                LuiSourceComparison.StructuralKey(document) is not { } before
                || before != LuiSourceComparison.StructuralKey(formatted)
            )
                return Unavailable(
                    "LUI6001",
                    "Formatting is unavailable because this source cannot yet be rewritten while preserving its tokens, comments and meaningful text."
                );
            return new LuiFormattingResult(
                formatted == source ? LuiFormattingStatus.Clean : LuiFormattingStatus.Changed,
                source,
                formatted,
                Array.Empty<LuiDiagnostic>()
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            return new LuiFormattingResult(
                LuiFormattingStatus.Failed,
                source,
                source,
                new[]
                {
                    new LuiDiagnostic(
                        "LUI6000",
                        "Formatting failed: " + error.Message,
                        new LuiSpan(0, 0)
                    ),
                }
            );
        }

        LuiFormattingResult Unavailable(string id, string message) =>
            new(
                LuiFormattingStatus.Unavailable,
                source,
                source,
                new[] { new LuiDiagnostic(id, message, range ?? new LuiSpan(0, source.Length)) }
            );
    }

    private static LuiFormattingResult FormatProjected(
        string source,
        LuiAuthoredSourceProjection projection,
        LuiSpan? range,
        LuiFormattingOptions options,
        CancellationToken cancellationToken
    )
    {
        var prefix = projection.DeclarationsSource;
        var unsupportedDirective = Microsoft
            .CodeAnalysis.CSharp.SyntaxFactory.ParseTokens(prefix)
            .SelectMany(token => token.LeadingTrivia.Concat(token.TrailingTrivia))
            .FirstOrDefault(trivia =>
                trivia.ToString().StartsWith("// lui-format-", StringComparison.Ordinal)
            );
        if (unsupportedDirective.RawKind != 0)
            return new LuiFormattingResult(
                LuiFormattingStatus.Unavailable,
                source,
                source,
                new[]
                {
                    new LuiDiagnostic(
                        "LUI6003",
                        "Formatter directives on ordinary support declarations are not supported yet; source has been preserved.",
                        new LuiSpan(
                            unsupportedDirective.Span.Start,
                            unsupportedDirective.Span.Length
                        )
                    ),
                }
            );
        var componentSource = source.Substring(prefix.Length);
        string formatted;
        if (range is { } selected)
        {
            if (selected.Start < prefix.Length)
                return new LuiFormattingResult(
                    LuiFormattingStatus.Unavailable,
                    source,
                    source,
                    new[]
                    {
                        new LuiDiagnostic(
                            "LUI6002",
                            "Select a complete component boundary, or format the whole file to include ordinary declarations.",
                            selected
                        ),
                    }
                );
            var component = FormatSafely(
                componentSource,
                new LuiSpan(selected.Start - prefix.Length, selected.Length),
                options,
                cancellationToken
            );
            if (component.Status is LuiFormattingStatus.Failed or LuiFormattingStatus.Unavailable)
                return new LuiFormattingResult(
                    component.Status,
                    source,
                    source,
                    component
                        .Diagnostics.Select(diagnostic => new LuiDiagnostic(
                            diagnostic.Id,
                            diagnostic.Message,
                            new LuiSpan(
                                diagnostic.Span.Start + prefix.Length,
                                diagnostic.Span.Length
                            ),
                            diagnostic.Severity
                        ))
                        .ToArray()
                );
            formatted = prefix + component.Text;
        }
        else
        {
            var declarations = new LuiCSharpLayout(options)
                .Declarations(prefix)
                .Render(options, options.Newline(source))
                .TrimEnd();
            if (projection.Document.Component is null)
                formatted = declarations + options.Newline(source);
            else
            {
                var component = FormatSafely(componentSource, null, options, cancellationToken);
                if (
                    component.Status
                    is LuiFormattingStatus.Failed
                        or LuiFormattingStatus.Unavailable
                )
                    return new LuiFormattingResult(
                        component.Status,
                        source,
                        source,
                        component
                            .Diagnostics.Select(diagnostic => new LuiDiagnostic(
                                diagnostic.Id,
                                diagnostic.Message,
                                new LuiSpan(
                                    diagnostic.Span.Start + prefix.Length,
                                    diagnostic.Span.Length
                                ),
                                diagnostic.Severity
                            ))
                            .ToArray()
                    );
                formatted =
                    declarations
                    + options.Newline(source)
                    + options.Newline(source)
                    + component.Text;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (
            LuiSourceComparison.StructuralKey(source) is not { } before
            || before != LuiSourceComparison.StructuralKey(formatted)
        )
            return new LuiFormattingResult(
                LuiFormattingStatus.Unavailable,
                source,
                source,
                new[]
                {
                    new LuiDiagnostic(
                        "LUI6001",
                        "Formatting ordinary declarations could not preserve every token, comment and meaningful text value.",
                        new LuiSpan(0, source.Length)
                    ),
                }
            );
        return new LuiFormattingResult(
            formatted == source ? LuiFormattingStatus.Clean : LuiFormattingStatus.Changed,
            source,
            formatted,
            Array.Empty<LuiDiagnostic>()
        );
    }

    private static string FormatSelectedBoundaries(
        string source,
        LuiDocumentSyntax document,
        LuiSpan selection,
        LuiDocumentLayout layout,
        LuiFormattingOptions options
    )
    {
        var selected = new List<LuiSyntaxNode>();
        foreach (
            var node in Nodes(document)
                .Where(node => node.Span.Start >= selection.Start && node.Span.End <= selection.End)
                .OrderBy(node => node.Span.Start)
                .ThenByDescending(node => node.Span.Length)
        )
            if (
                !selected.Any(parent =>
                    parent.Span.Start <= node.Span.Start && parent.Span.End >= node.Span.End
                )
            )
                selected.Add(node);
        var output = source;
        foreach (var node in selected.AsEnumerable().Reverse())
        {
            var start = node.Span.Start;
            while (start > 0 && source[start - 1] is not '\n' and not '\r')
                start--;
            var column = 0;
            for (var index = start; index < node.Span.Start; index++)
                column =
                    source[index] == '\t' ? column + options.TabWidth - column % options.TabWidth
                    : source[index] == ' ' ? column + 1
                    : 0;
            output =
                output.Substring(0, node.Span.Start)
                + layout.FormatNode(node, column / options.IndentSize)
                + output.Substring(node.Span.End);
        }
        return output;
    }
}
