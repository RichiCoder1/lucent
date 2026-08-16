using Avalonia.Controls;
using Lucent.Runtime;

namespace Lucent.Runtime.Tests;

[TestClass]
public sealed class FragmentTests
{
    [TestMethod]
    public void Empty_default_and_concat_preserve_order()
    {
        var first = new Border();
        var second = new TextBlock();

        Assert.AreEqual(0, default(Fragment).Count);
        Assert.AreSame(Fragment.Empty.Roots, default(Fragment).Roots);
        var fragment = Fragment.Concat(default, Fragment.From(first), Fragment.Empty,
            Fragment.From(second));

        CollectionAssert.AreEqual(new Control[] { first, second }, fragment.Roots.ToArray());
        Assert.AreSame(first, fragment[0]);
        Assert.IsFalse(fragment.Roots is Control[]);
        Assert.ThrowsExactly<InvalidCastException>(() =>
            _ = (Control[])fragment.Roots);
    }

    [TestMethod]
    public void From_snapshots_and_rejects_invalid_roots()
    {
        var first = new Border();
        var second = new TextBlock();
        var roots = new Control[] { first };
        var fragment = Fragment.From(roots);
        roots[0] = second;

        Assert.AreSame(first, fragment[0]);
        Assert.ThrowsExactly<ArgumentNullException>(() => Fragment.From(null!));
        Assert.ThrowsExactly<ArgumentException>(() => Fragment.From(first, null!));
        Assert.ThrowsExactly<ArgumentException>(() => Fragment.From(first, first));
        Assert.ThrowsExactly<ArgumentException>(() =>
            Fragment.Concat(Fragment.From(first), Fragment.From(first)));
    }
}
