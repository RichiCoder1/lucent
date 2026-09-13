using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace Lucent.Lui.Compiler;

/// <summary>Capabilities to which a generated authoring property may be applied.</summary>
[Flags]
public enum LuiAuthoringCapabilities
{
    /// <summary>No authoring target.</summary>
    None = 0,

    /// <summary>The retained presentation target accepts the property.</summary>
    Styled = 1,

    /// <summary>The retained semantic target accepts the property.</summary>
    Accessible = 2,
}

/// <summary>Input shapes emitted for a generated authoring property.</summary>
[Flags]
public enum LuiAuthoringInputForms
{
    /// <summary>No input shape.</summary>
    None = 0,

    /// <summary>A snapshot value overload.</summary>
    Value = 1,

    /// <summary>A live reader overload.</summary>
    Reader = 2,

    /// <summary>A theme token overload.</summary>
    Token = 4,
}

/// <summary>
/// Immutable metadata for one authorable Core property. The descriptor is produced from the
/// Roslyn field symbol and is shared by compiler, generator, and editor tooling.
/// </summary>
public sealed class LuiAuthorPropertyDescriptor : IEquatable<LuiAuthorPropertyDescriptor>
{
    private readonly IReadOnlyList<string> _names;

    internal LuiAuthorPropertyDescriptor(
        string declaringTypeName,
        string fieldName,
        string symbolIdentity,
        string valueTypeName,
        string authorName,
        IEnumerable<string> aliases,
        LuiAuthoringCapabilities capabilities,
        LuiAuthoringInputForms inputForms,
        string styleTarget,
        string? semanticTarget,
        bool transitionEligible,
        bool paintInvalidating,
        bool declaredInCompilation
    )
    {
        DeclaringTypeName = declaringTypeName;
        FieldName = fieldName;
        SymbolIdentity = symbolIdentity;
        ValueTypeName = valueTypeName;
        AuthorName = authorName;
        Aliases = new ReadOnlyCollection<string>(
            aliases
                .Where(alias => !String.IsNullOrWhiteSpace(alias))
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        );
        _names = new ReadOnlyCollection<string>(
            new[] { AuthorName, FieldName }
                .Concat(Aliases)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
        );
        Capabilities = capabilities;
        InputForms = inputForms;
        StyleTarget = styleTarget;
        SemanticTarget = semanticTarget;
        TransitionEligible = transitionEligible;
        PaintInvalidating = paintInvalidating;
        DeclaredInCompilation = declaredInCompilation;
    }

    /// <summary>Gets the metadata name of the static property group declaring the field.</summary>
    public string DeclaringTypeName { get; }

    /// <summary>Gets the static field name.</summary>
    public string FieldName { get; }

    /// <summary>Gets the fully qualified field symbol identity.</summary>
    public string SymbolIdentity { get; }

    /// <summary>Gets the stable property identity used by lowering and transition diagnostics.</summary>
    public string PropertyIdentity => SymbolIdentity;

    /// <summary>Gets the fully qualified property value type.</summary>
    public string ValueTypeName { get; }

    /// <summary>Gets the fully qualified property value type (compatibility alias).</summary>
    public string ValueType => ValueTypeName;

    /// <summary>Gets the canonical C# authoring name.</summary>
    public string AuthorName { get; }

    /// <summary>Gets source-compatible aliases for the canonical authoring name.</summary>
    public IReadOnlyList<string> Aliases { get; }

    /// <summary>Gets the permitted authoring targets.</summary>
    public LuiAuthoringCapabilities Capabilities { get; }

    /// <summary>Gets the permitted authoring targets (compatibility alias).</summary>
    public LuiAuthoringCapabilities TargetCapabilities => Capabilities;

    /// <summary>Gets the permitted same-name input overloads.</summary>
    public LuiAuthoringInputForms InputForms { get; }

    /// <summary>Gets the permitted same-name input overloads (compatibility alias).</summary>
    public LuiAuthoringInputForms SupportedInputForms => InputForms;

    /// <summary>Gets the explicit presentation target identity.</summary>
    public string StyleTarget { get; }

    /// <summary>Gets the explicit semantic target identity, when the property has one.</summary>
    public string? SemanticTarget { get; }

    /// <summary>Gets whether the property is eligible for a presentation transition.</summary>
    public bool TransitionEligible { get; }

    /// <summary>Gets whether changing the property invalidates painting.</summary>
    public bool PaintInvalidating { get; }

    /// <summary>Gets whether the declaring property group belongs to the current compilation.</summary>
    public bool DeclaredInCompilation { get; }

    /// <summary>Gets the canonical name followed by the field and declared aliases.</summary>
    public IEnumerable<string> Names => _names;

    /// <summary>Gets the transition value type expected by the compiler when eligible.</summary>
    public string? TransitionValueType => TransitionEligible ? ValueTypeName : null;

    /// <inheritdoc />
    public bool Equals(LuiAuthorPropertyDescriptor? other) =>
        other is not null
        && StringComparer.Ordinal.Equals(SymbolIdentity, other.SymbolIdentity)
        && StringComparer.Ordinal.Equals(AuthorName, other.AuthorName)
        && StringComparer.Ordinal.Equals(ValueTypeName, other.ValueTypeName)
        && Aliases.SequenceEqual(other.Aliases, StringComparer.Ordinal)
        && Capabilities == other.Capabilities
        && InputForms == other.InputForms
        && StringComparer.Ordinal.Equals(StyleTarget, other.StyleTarget)
        && StringComparer.Ordinal.Equals(SemanticTarget, other.SemanticTarget)
        && TransitionEligible == other.TransitionEligible
        && PaintInvalidating == other.PaintInvalidating
        && DeclaredInCompilation == other.DeclaredInCompilation;

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as LuiAuthorPropertyDescriptor);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = StringComparer.Ordinal.GetHashCode(SymbolIdentity);
        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(AuthorName);
        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(ValueTypeName);
        foreach (var alias in Aliases)
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(alias);
        hash = hash * 31 + Capabilities.GetHashCode();
        hash = hash * 31 + InputForms.GetHashCode();
        hash = hash * 31 + StringComparer.Ordinal.GetHashCode(StyleTarget);
        hash =
            hash * 31
            + (SemanticTarget is null ? 0 : StringComparer.Ordinal.GetHashCode(SemanticTarget));
        hash = hash * 31 + TransitionEligible.GetHashCode();
        hash = hash * 31 + PaintInvalidating.GetHashCode();
        return hash * 31 + DeclaredInCompilation.GetHashCode();
    }
}
