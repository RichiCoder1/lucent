using Avalonia.Controls;

namespace Lucent.Runtime;

public readonly struct Fragment
{
    private static readonly IReadOnlyList<Control> EmptyRoots = Array.AsReadOnly(Array.Empty<Control>());
    private readonly IReadOnlyList<Control>? _roots;

    private Fragment(IReadOnlyList<Control> roots) => _roots = roots;

    public static Fragment Empty => default;
    public int Count => Roots.Count;
    public Control this[int index] => Roots[index];
    public IReadOnlyList<Control> Roots => _roots ?? EmptyRoots;

    public static Fragment From(params Control[] roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (roots.Any(root => root is null))
        {
            throw new ArgumentException("A fragment cannot contain a null control.", nameof(roots));
        }

        if (roots.Distinct(ReferenceEqualityComparer.Instance).Count() != roots.Length)
        {
            throw new ArgumentException("A fragment cannot contain the same control more than once.", nameof(roots));
        }

        return roots.Length == 0
            ? Empty
            : new Fragment(Array.AsReadOnly(roots.ToArray()));
    }

    public static Fragment Concat(params Fragment[] fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        var roots = fragments.SelectMany(fragment => fragment.Roots).ToArray();
        return From(roots);
    }
}
