namespace Lucent.Core;

internal static class ControlLabelBinding
{
    internal static void Configure(
        Element element,
        Func<string> read,
        string parameterName,
        string diagnosticKind,
        Action<string> configure,
        Action<string> update
    )
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentNullException.ThrowIfNull(update);
        ReactiveGraph.ValidateName(diagnosticKind, nameof(diagnosticKind));
        var label = element.Scope.Derived(
            () => ControlState.Required(read(), parameterName),
            element.Name + "." + diagnosticKind + "-label"
        );
        configure(label.Value);
        _ = element.Scope.Effect(() => update(label.Value), element.Name + "." + diagnosticKind);
    }
}
