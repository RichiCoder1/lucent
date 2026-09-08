using Lucent.Core;

namespace Lucent.Testing;

/// <summary>An immutable semantic and renderer-facing snapshot from one settled application state.</summary>
public sealed class HeadlessSnapshot
{
    internal HeadlessSnapshot(RetainedScene scene, SemanticSnapshot? semantics)
    {
        Scene = scene;
        Semantics = semantics;
    }

    /// <summary>Gets the immutable retained scene.</summary>
    public RetainedScene Scene { get; }

    /// <summary>Gets the immutable semantic tree, when the application emits one.</summary>
    public SemanticSnapshot? Semantics { get; }

    /// <summary>Returns semantic nodes in stable depth-first order.</summary>
    public IReadOnlyList<SemanticSnapshot> FindAll(Func<SemanticSnapshot, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return Semantics is null ? [] : Descendants(Semantics).Where(predicate).ToArray();
    }

    /// <summary>Returns all semantic nodes with the supplied role and optional exact name.</summary>
    public IReadOnlyList<SemanticSnapshot> FindAll(SemanticRole role, string? name = null) =>
        FindAll(node => node.Role == role && (name is null || node.Name == name));

    /// <summary>Requires exactly one semantic node matching the predicate.</summary>
    public SemanticSnapshot Require(Func<SemanticSnapshot, bool> predicate)
    {
        var matches = FindAll(predicate);
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                "No semantic node matched the query.\n" + SemanticDump()
            ),
            _ => throw new InvalidOperationException(
                matches.Count + " semantic nodes matched the query.\n" + SemanticDump()
            ),
        };
    }

    /// <summary>Requires exactly one semantic node with the supplied role and optional exact name.</summary>
    public SemanticSnapshot Require(SemanticRole role, string? name = null) =>
        Require(node => node.Role == role && (name is null || node.Name == name));

    /// <summary>Gets the layout box associated with a semantic node.</summary>
    public LayoutBox RequireBox(SemanticSnapshot semantic)
    {
        ArgumentNullException.ThrowIfNull(semantic);
        var matches = Scene
            .Boxes.Where(box =>
                box.Identity.CompositionEpoch == semantic.Identity.CompositionEpoch
                && box.Identity.ElementId == semantic.Identity.ElementId
            )
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException(
                "The semantic node has no layout box in scene generation " + Scene.Generation + "."
            ),
            _ => throw new InvalidOperationException(
                "The semantic node has multiple layout boxes."
            ),
        };
    }

    /// <summary>Returns flattened scene operations belonging to a semantic node.</summary>
    public IReadOnlyList<SceneNode> SceneNodes(SemanticSnapshot semantic)
    {
        ArgumentNullException.ThrowIfNull(semantic);
        return Flatten(Scene.Nodes)
            .Where(node =>
                node.Identity.Element.CompositionEpoch == semantic.Identity.CompositionEpoch
                && node.Identity.Element.ElementId == semantic.Identity.ElementId
            )
            .ToArray();
    }

    private string SemanticDump() => Semantics is null ? "semantics: <empty>" : Dump(Semantics, 0);

    private static string Dump(SemanticSnapshot node, int depth) =>
        new string(' ', depth * 2)
        + node.Role
        + " name=\""
        + node.Name
        + "\"\n"
        + string.Concat(node.Children.Select(child => Dump(child, depth + 1)));

    private static IEnumerable<SemanticSnapshot> Descendants(SemanticSnapshot node)
    {
        yield return node;
        foreach (var child in node.Children)
        foreach (var descendant in Descendants(child))
            yield return descendant;
    }

    private static IEnumerable<SceneNode> Flatten(IEnumerable<SceneNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            var children = node switch
            {
                ClipSceneNode clip => clip.Children,
                OpacitySceneNode opacity => opacity.Children,
                _ => null,
            };
            if (children is null)
                continue;
            foreach (var child in Flatten(children))
                yield return child;
        }
    }
}
