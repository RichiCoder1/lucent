using Lucent.Core;

namespace ContextInjection.Library;

public readonly record struct ProbeContext(int Value);

public interface IProbeService
{
    int Value { get; }
}

public static class RequiredComponent
{
    public static ComponentRecipe Create(List<string> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        var context = new ComponentRequirementSource(
            "probeContext",
            "ContextInjection.Library.ProbeContext",
            "RequiredComponent.lui",
            2,
            5
        );
        var service = new ComponentRequirementSource(
            "probeService",
            "ContextInjection.Library.IProbeService",
            "RequiredComponent.lui",
            3,
            5
        );
        var requirements = ComponentRequirements
            .Context<ProbeContext>(context)
            .AndService<IProbeService>(service)
            .Select(values => new Values(values.Previous, values.Value));
        return ComponentRecipe.Defer(
            "package-requirement-probe",
            requirements,
            (owner, values) =>
            {
                events.Add($"setup:{values.Context.Value}:{values.Service.Value}");
                owner.OnDispose(() => events.Add("component-dispose"));
                return ComponentRecipe.Create("package-probe-root", static (_, _) => { });
            }
        );
    }

    private readonly record struct Values(ProbeContext Context, IProbeService Service);
}
