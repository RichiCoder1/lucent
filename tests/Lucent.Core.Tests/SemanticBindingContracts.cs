using Lucent.Core;

namespace Lucent.Core.Tests;

[TestClass]
public sealed class SemanticBindingContracts
{
    [TestMethod]
    public void PublicBindingUpdatesOneSemanticIdentityAndStopsWithItsElement()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "semantic-binding");
        var label = composition.Root.Scope.Signal("first", "semantic-binding-label");
        var reads = 0;
        var element = composition.Child(composition.Root, "semantic-binding-target");
        element.Present(new ThemeContext(element.Scope, ControlThemes.Light));
        element.AttachBehaviors(
            new BindingBehavior(() =>
            {
                reads++;
                return SemanticDeclaration.Create(SemanticRole.Group, label.Value).Build();
            })
        );

        graph.Drain();
        var first = Find(composition.SemanticSnapshot()!, element.Id);
        Assert.AreEqual("first", first.Name);

        label.Value = "second";
        graph.Drain();
        var second = Find(composition.SemanticSnapshot()!, element.Id);
        Assert.AreEqual(first.Identity.CompositionEpoch, second.Identity.CompositionEpoch);
        Assert.AreEqual(first.Identity.ElementId, second.Identity.ElementId);
        Assert.AreEqual("second", second.Name);

        var readsBeforeDispose = reads;
        element.Dispose();
        label.Value = "after disposal";
        graph.Drain();
        Assert.AreEqual(readsBeforeDispose, reads);
    }

    [TestMethod]
    public void BindingRequiresSemanticOwnershipAndTheActiveAttachCall()
    {
        var graph = new ReactiveGraph();
        using var composition = new Composition(graph, "semantic-binding-guards");
        var unowned = composition.Child(composition.Root, "unowned-binding");
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            unowned.AttachBehaviors(new NonSemanticBindingBehavior())
        );

        var retained = composition.Child(composition.Root, "retained-binding");
        var behavior = new RetainedContextBehavior();
        retained.AttachBehaviors(behavior);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            behavior.Context!.BindSemantics(() =>
                SemanticDeclaration.Create(SemanticRole.Group, "too late").Build()
            )
        );
    }

    private static SemanticSnapshot Find(SemanticSnapshot node, long elementId)
    {
        if (node.Identity.ElementId == elementId)
            return node;
        foreach (var child in node.Children)
        {
            var found = FindOrNull(child, elementId);
            if (found is not null)
                return found;
        }
        throw new AssertFailedException($"Semantic element {elementId} was not found.");
    }

    private static SemanticSnapshot? FindOrNull(SemanticSnapshot node, long elementId)
    {
        if (node.Identity.ElementId == elementId)
            return node;
        foreach (var child in node.Children)
        {
            var found = FindOrNull(child, elementId);
            if (found is not null)
                return found;
        }
        return null;
    }

    private sealed class BindingBehavior(Func<SemanticDeclaration> read) : Behavior
    {
        public override string Name => "semantic-binding";

        public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context) => context.BindSemantics(read);
    }

    private sealed class NonSemanticBindingBehavior : Behavior
    {
        public override string Name => "non-semantic-binding";

        public override void Attach(BehaviorContext context) =>
            context.BindSemantics(() =>
                SemanticDeclaration.Create(SemanticRole.Group, "not owned").Build()
            );
    }

    private sealed class RetainedContextBehavior : Behavior
    {
        internal BehaviorContext? Context { get; private set; }

        public override string Name => "retained-semantic-context";

        public override BehaviorOwnership Ownership => BehaviorOwnership.Semantics;

        public override void Attach(BehaviorContext context)
        {
            Context = context;
            context.SetSemantics(SemanticDeclaration.Create(SemanticRole.Group, "initial").Build());
        }
    }
}
