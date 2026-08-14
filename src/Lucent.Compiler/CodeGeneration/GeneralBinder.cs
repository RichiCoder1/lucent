using Lucent.Compiler.Parsing;
using Lucent.Compiler.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Lucent.Compiler.CodeGeneration;

internal sealed class GeneralBinder(DiagnosticBag diagnostics)
{
    private static readonly IReadOnlyDictionary<string, (string TypeName, BoundControlKind Kind)> LegacyControls =
        new Dictionary<string, (string, BoundControlKind)>(StringComparer.Ordinal)
        {
            ["Column"] = ("StackPanel", BoundControlKind.Panel),
            ["Text"] = ("TextBlock", BoundControlKind.Text),
            ["Button"] = ("Button", BoundControlKind.ContentControl),
        };

    private static readonly IReadOnlySet<string> NativeEvents = new HashSet<string>(StringComparer.Ordinal)
    {
        "Click", "TextChanged", "KeyDown", "KeyUp", "PointerPressed", "PointerReleased",
        "PointerMoved", "PointerEntered", "PointerExited", "PointerWheelChanged", "GotFocus",
        "LostFocus", "GotKeyboardFocus", "LostKeyboardFocus", "Loaded", "Unloaded",
        "AttachedToVisualTree", "DetachedFromVisualTree", "Tapped", "DoubleTapped", "RightTapped",
        "Holding", "DragEnter", "DragLeave", "DragOver", "Drop", "SelectionChanged", "Checked",
        "Unchecked", "Indeterminate", "IsCheckedChanged", "ValueChanged", "ScrollChanged", "Opened",
        "Closed", "Closing", "ContextMenuOpening", "ContextMenuClosing", "TemplateApplied",
        "EffectiveViewportChanged", "LayoutUpdated", "SizeChanged", "DataContextChanged",
        "PropertyChanged", "ItemsChanged",
    };

    private static readonly IReadOnlySet<string> DetachableNativeEvents =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Click",
            "TextChanged",
        };

    public BoundComponentModel? Bind(Lucent.Compiler.Syntax.CompilationUnitSyntax syntax)
    {
        foreach (var additional in syntax.AllComponents.Skip(1))
        {
            AddUnsupported(
                additional.Span,
                "The initial compiler emits one component per source file; split additional components into separate files.");
        }

        var component = syntax.Component;
        var states = component.AllStateMembers
            .Select(BindState)
            .Where(state => state is not null)
            .Cast<BoundStateModel>()
            .ToArray();

        if (component.RenderMethod.Root.Name == "Missing")
        {
            return null;
        }

        var root = BindControl(component.RenderMethod.Root);
        if (root is null)
        {
            return null;
        }

        return HasErrors
            ? null
            : new BoundComponentModel(
                syntax.NamespaceName,
                component.Name,
                syntax.AllUsings.Select(directive => directive.Text).ToArray(),
                states,
                root);
    }

    private BoundStateModel? BindState(StateMemberSyntax state)
    {
        return new BoundStateModel(
            state.TypeName,
            state.Name,
            state.InitializerText ?? state.InitialValue.ToString(),
            state.Span);
    }

    private BoundControlModel? BindControl(
        UiElementSyntax element,
        bool insideLoop = false)
    {
        if (!TryGetControlKind(element.Name, out var kind) &&
            (element.Name.Length == 0 || !char.IsUpper(element.Name[0])))
        {
            AddUnsupported(
                element.Span,
                $"Control '{element.Name}' is not a native Avalonia control name (use PascalCase).");
            return null;
        }

        var members = new List<BoundControlMember>();
        var seenMembers = new HashSet<string>(StringComparer.Ordinal);
        var childCount = 0;
        var hasStructuralLoop = false;

        foreach (var member in element.Members)
        {
            switch (member)
            {
                case UiContentSyntax content:
                    var contentTarget = ImplicitContentTarget(kind);
                    if (contentTarget is null)
                    {
                        AddUnsupported(
                            content.Span,
                            $"Control '{element.Name}' does not have an implicit scalar content slot.");
                        continue;
                    }

                    if (childCount > 0)
                    {
                        AddUnsupported(
                            content.Span,
                            $"Control '{element.Name}' cannot combine implicit content with a nested child.");
                        continue;
                    }

                    if (!seenMembers.Add(contentTarget))
                    {
                        AddUnsupported(
                            content.Span,
                            $"{element.Name} may contain only one '{contentTarget}' member.");
                        continue;
                    }

                    var contentValue = (StringValueSyntax)content.Value;
                    members.Add(
                        new BoundContentMember(
                            contentValue.Text,
                            contentValue.Span,
                            contentValue.IsInterpolated,
                            content.Span));
                    break;

                case UiPropertySyntax property:
                    var eventName = GetEventName(property.Name);
                    var memberName = CanonicalMemberName(element.Name, property.Name, eventName);

                    if (eventName is null &&
                        childCount > 0 &&
                        string.Equals(memberName, ImplicitContentTarget(kind), StringComparison.Ordinal))
                    {
                        AddUnsupported(
                            property.Span,
                            $"Control '{element.Name}' cannot combine explicit '{property.Name}' with a nested child.");
                        continue;
                    }

                    if (!seenMembers.Add(memberName))
                    {
                        AddUnsupported(
                            property.Span,
                            $"{element.Name} may contain only one '{property.Name}' member.");
                        continue;
                    }

                    if (eventName is not null)
                    {
                        if (property.Value is EventBlockValueSyntax eventBlock)
                        {
                            members.Add(
                                new BoundEventMember(
                                    property.Name,
                                    eventName,
                                    eventBlock.Text,
                                    eventBlock.Span,
                                    IsExpression: false,
                                    property.Span));
                            continue;
                        }

                        if (property.Value is not CSharpExpressionValueSyntax expression ||
                            !expression.Text.Contains("=>", StringComparison.Ordinal))
                        {
                            AddUnsupported(
                                property.Span,
                                $"Event '{property.Name}' must use a lambda expression or a statement block.");
                            continue;
                        }

                        if (SyntaxFactory.ParseExpression(expression.Text) is not
                            ParenthesizedLambdaExpressionSyntax
                            {
                                ParameterList.Parameters.Count: 0,
                            })
                        {
                            AddUnsupported(
                                property.Span,
                                $"Expression event '{property.Name}' must use a parameterless lambda; use a statement block for the generated 'sender' and 'e' values.");
                            continue;
                        }

                        members.Add(
                            new BoundEventMember(
                                property.Name,
                                eventName,
                                property.Value.Text,
                                property.Value.Span,
                                IsExpression: true,
                                property.Span));
                        continue;
                    }

                    if (property.Value is EventBlockValueSyntax)
                    {
                        AddUnsupported(
                            property.Span,
                            $"Block-valued property '{property.Name}' is not a native event.");
                        continue;
                    }

                    if (!IsAllowedProperty(element.Name, property.Name))
                    {
                        AddUnsupported(
                            property.Span,
                            $"Property '{property.Name}' is not a native PascalCase property on {element.Name}.");
                        continue;
                    }

                    members.Add(
                        new BoundPropertyMember(
                            property.Name,
                            property.Value.Text,
                            property.Value.Span,
                            property.Value is StringValueSyntax,
                            property.Value is StringValueSyntax { IsInterpolated: true },
                            property.Span,
                            GetNativeValueKind(element.Name, memberName)));
                    break;

                case UiChildSyntax child:
                    if (hasStructuralLoop)
                    {
                        AddUnsupported(
                            child.Span,
                            $"Control '{element.Name}' cannot mix a keyed foreach with ordinary child controls in the initial compiler.");
                        continue;
                    }

                    if (kind == BoundControlKind.Text)
                    {
                        AddUnsupported(
                            child.Span,
                            $"Control '{element.Name}' does not accept nested controls as content.");
                        continue;
                    }

                    if (kind is BoundControlKind.Decorator or BoundControlKind.ContentControl && childCount > 0)
                    {
                        AddUnsupported(
                            child.Span,
                            $"Control '{element.Name}' can contain only one child.");
                        continue;
                    }

                    if (kind is BoundControlKind.Decorator or BoundControlKind.ContentControl &&
                        seenMembers.Contains("Content"))
                    {
                        AddUnsupported(
                            child.Span,
                            $"Control '{element.Name}' can contain only one content child.");
                        continue;
                    }

                    var boundChild = BindControl(child.Element, insideLoop);
                    if (boundChild is not null)
                    {
                        members.Add(new BoundChildMember(boundChild, child.Span));
                        childCount++;
                    }

                    break;

                case UiForEachSyntax loop:
                    if (insideLoop)
                    {
                        AddUnsupported(
                            loop.Span,
                            "Nested keyed foreach regions are not supported in the initial compiler.");
                        continue;
                    }

                    if (kind is not BoundControlKind.Panel and not BoundControlKind.ItemsControl)
                    {
                        AddUnsupported(
                            loop.Span,
                            $"Control '{element.Name}' cannot host a keyed foreach; use a Panel or ItemsControl.");
                        continue;
                    }

                    if (hasStructuralLoop || childCount > 0)
                    {
                        AddUnsupported(
                            loop.Span,
                            $"Control '{element.Name}' must dedicate its child region to one keyed foreach in the initial compiler.");
                        continue;
                    }

                    var boundBody = BindControl(loop.Body, insideLoop: true);
                    if (boundBody is not null)
                    {
                        members.Add(
                            new BoundForEachMember(
                                loop.ItemName,
                                loop.SourceExpression,
                                loop.SourceExpressionSpan,
                                loop.KeyExpression,
                                loop.KeyExpressionSpan,
                                boundBody,
                                loop.Span));
                        hasStructuralLoop = true;
                    }

                    break;
            }
        }

        return new BoundControlModel(element.Name, members, element.Span, kind);
    }

    private static bool TryGetControlKind(string name, out BoundControlKind kind)
    {
        if (LegacyControls.TryGetValue(name, out var legacy))
        {
            kind = legacy.Kind;
            return true;
        }

        kind = name switch
        {
            "Panel" or "StackPanel" or "DockPanel" or "Canvas" or "Grid" or "WrapPanel" or
                "UniformGrid" or "RelativePanel" => BoundControlKind.Panel,
            "Decorator" or "Border" or "AdornerDecorator" or "Viewbox" => BoundControlKind.Decorator,
            "ContentControl" or "Button" or "CheckBox" or "RadioButton" or "ToggleButton" or
                "RepeatButton" or "HyperlinkButton" or "Label" or "ListBoxItem" or "TreeViewItem" or
                "TabItem" or "Expander" or "GroupBox" or "UserControl" or "Window" or "ScrollViewer" =>
                BoundControlKind.ContentControl,
            "ItemsControl" or "ListBox" or "ComboBox" or "TreeView" or "TabControl" or "DataGrid" or
                "Menu" or "ContextMenu" or "SelectingItemsControl" or "HeaderedItemsControl" or
                "HeaderedSelectingItemsControl" => BoundControlKind.ItemsControl,
            "TextBlock" or "TextBox" or "PasswordBox" or "MaskedTextBox" or "AccessText" =>
                BoundControlKind.Text,
            _ => BoundControlKind.Unknown,
        };

        return kind != BoundControlKind.Unknown;
    }

    private static string? ImplicitContentTarget(BoundControlKind kind) =>
        kind switch
        {
            BoundControlKind.Text => "Text",
            BoundControlKind.ContentControl => "Content",
            BoundControlKind.ItemsControl => "Items",
            _ => null,
        };

    private static BoundNativeValueKind GetNativeValueKind(
        string controlName,
        string propertyName) =>
        (controlName, propertyName) switch
        {
            ("Border", "Padding") => BoundNativeValueKind.Thickness,
            ("Border", "CornerRadius") => BoundNativeValueKind.CornerRadius,
            _ => BoundNativeValueKind.None,
        };

    private static bool IsAllowedProperty(string controlName, string propertyName)
    {
        if (propertyName.Length > 0 && char.IsUpper(propertyName[0]))
        {
            return true;
        }

        return propertyName == "class" ||
            (controlName, propertyName) switch
            {
                ("Text", "text") => true,
                ("Button", "text") => true,
                _ => false,
            };
    }

    private static string CanonicalMemberName(string controlName, string propertyName, string? eventName)
    {
        if (eventName is not null)
        {
            return eventName;
        }

        return (controlName, propertyName) switch
        {
            ("Text", "text") => "Text",
            ("Button", "text") => "Content",
            _ => propertyName,
        };
    }

    private static string? GetEventName(string memberName)
    {
        if (NativeEvents.Contains(memberName) &&
            DetachableNativeEvents.Contains(memberName))
        {
            return memberName;
        }

        if (memberName.StartsWith("on", StringComparison.Ordinal) &&
            memberName.Length > 2 &&
            NativeEvents.Contains(memberName[2..]) &&
            DetachableNativeEvents.Contains(memberName[2..]))
        {
            return memberName[2..];
        }

        return null;
    }

    private bool HasErrors =>
        diagnostics.Items.Any(diagnostic =>
            diagnostic.Severity == LucentDiagnosticSeverity.Error);

    private void AddUnsupported(SourceSpan span, string message) =>
        diagnostics.Add("LUC2001", message, span);
}

internal sealed record BoundChildMember(
    BoundControlModel Child,
    SourceSpan Span) : BoundControlMember(Span);
