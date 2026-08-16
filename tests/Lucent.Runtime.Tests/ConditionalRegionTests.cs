using Avalonia.Controls;
using Lucent.Runtime;

namespace Lucent.Runtime.Tests;

[TestClass]
public sealed class ConditionalRegionTests
{
    [TestMethod]
    public void Branches_publish_fixed_multi_root_fragments_in_order()
    {
        using var owner = new ComponentOwner(new TestUiDispatcher());
        Fragment published = default;
        var region = new ConditionalRegion(owner, roots => published = roots);
        var first = new Border();
        var second = new TextBlock();

        region.Show(1, _ => Fragment.From(first, second));

        Assert.AreEqual(2, published.Count);
        Assert.AreSame(first, published[0]);
        Assert.AreSame(second, published[1]);
    }

    [TestMethod]
    public void Show_switch_clear_and_parent_dispose_own_branches()
    {
        using var owner = new ComponentOwner(new TestUiDispatcher());
        Fragment published = default;
        var disposed = new List<int>();
        var region = new ConditionalRegion(owner, root => published = root);

        region.Show(1, branchOwner =>
        {
            branchOwner.OnDispose(() => disposed.Add(1));
            return Fragment.From(new Border());
        });
        var first = published[0];
        region.Show(1, _ => throw new AssertFailedException("same branch mounted twice"));
        region.Show(2, branchOwner =>
        {
            branchOwner.OnDispose(() => disposed.Add(2));
            return Fragment.From(new TextBlock());
        });

        Assert.IsNotNull(first);
        Assert.IsInstanceOfType<TextBlock>(published[0]);
        CollectionAssert.AreEqual(new[] { 1 }, disposed);
        region.Clear();
        Assert.AreEqual(0, published.Count);
        Assert.IsNull(region.ActiveBranch);
        CollectionAssert.AreEqual(new[] { 1, 2 }, disposed);

        region.Show(3, branchOwner =>
        {
            branchOwner.OnDispose(() => disposed.Add(3));
            return Fragment.From(new Border());
        });
        owner.Dispose();
        Assert.IsNull(region.ActiveBranch);
        Assert.AreEqual(1, published.Count, "Parent teardown must not publish empty roots.");
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, disposed);
    }

    [TestMethod]
    public void Failures_restore_or_clear_transactionally()
    {
        using var owner = new ComponentOwner(new TestUiDispatcher());
        Fragment published = default;
        var calls = new List<Fragment>();
        var region = new ConditionalRegion(owner, root =>
        {
            calls.Add(root);
            published = root;
        });
        region.Show(1, _ => Fragment.From(new Border()));
        var old = published[0];

        var mountDisposed = 0;
        Assert.Throws<InvalidOperationException>(() => region.Show(2, branchOwner =>
        {
            branchOwner.OnDispose(() => mountDisposed++);
            throw new InvalidOperationException("mount");
        }));
        Assert.AreEqual(1, mountDisposed);
        Assert.AreSame(old, published[0]);
        Assert.AreEqual(1, region.ActiveBranch);

        var publishDisposed = 0;
        var failNext = true;
        var transactional = new ConditionalRegion(owner, root =>
        {
            calls.Add(root);
            published = root;
            if (failNext && root.Count == 1 && root[0] is TextBlock)
            {
                failNext = false;
                throw new InvalidOperationException("publish");
            }
        });
        transactional.Show(1, _ => Fragment.From(new Border()));
        old = published[0];
        Assert.Throws<InvalidOperationException>(() => transactional.Show(2, branchOwner =>
        {
            branchOwner.OnDispose(() => publishDisposed++);
            return Fragment.From(new TextBlock());
        }));
        Assert.AreSame(old, published[0]);
        Assert.AreEqual(1, publishDisposed);
        Assert.AreEqual(1, transactional.ActiveBranch);
        Assert.AreSame(old, calls[^1][0]);
    }

    [TestMethod]
    public void Mount_failure_never_publishes_and_disposes_only_the_new_owner()
    {
        using var owner = new ComponentOwner(new TestUiDispatcher());
        var publications = 0;
        var oldDisposed = 0;
        var nextDisposed = 0;
        var region = new ConditionalRegion(owner, _ => publications++);
        region.Show(1, branchOwner =>
        {
            branchOwner.OnDispose(() => oldDisposed++);
            return Fragment.From(new Border());
        });

        Assert.Throws<InvalidOperationException>(() => region.Show(2, branchOwner =>
        {
            branchOwner.OnDispose(() => nextDisposed++);
            throw new InvalidOperationException("mount");
        }));

        Assert.AreEqual(1, publications);
        Assert.AreEqual(0, oldDisposed);
        Assert.AreEqual(1, nextDisposed);
        Assert.AreEqual(1, region.ActiveBranch);
    }

    [TestMethod]
    public void Publication_failure_restores_before_new_cleanup_for_pre_and_post_mutation_adapters()
    {
        foreach (var mutateBeforeThrow in new[] { false, true })
        {
            using var owner = new ComponentOwner(new TestUiDispatcher());
            Fragment published = default;
            var events = new List<string>();
            var fail = false;
            var region = new ConditionalRegion(owner, root =>
            {
                events.Add(root.Count == 1 && root[0] is TextBlock ? "publish-new" : "publish-old");
                if (fail && root.Count == 1 && root[0] is TextBlock)
                {
                    if (mutateBeforeThrow)
                    {
                        published = root;
                    }
                    fail = false;
                    throw new InvalidOperationException("publish");
                }
                published = root;
            });
            region.Show(1, _ => Fragment.From(new Border()));
            var old = published[0];
            fail = true;

            Assert.Throws<InvalidOperationException>(() => region.Show(2, branchOwner =>
            {
                branchOwner.OnDispose(() => events.Add("dispose-new"));
                return Fragment.From(new TextBlock());
            }));

            Assert.AreSame(old, published[0]);
            CollectionAssert.AreEqual(
                new[] { "publish-old", "publish-new", "publish-old", "dispose-new" },
                events);
            Assert.AreEqual(1, region.ActiveBranch);
        }
    }

    [TestMethod]
    public void Restoration_failure_clears_state_disposes_both_owners_and_aggregates_failures()
    {
        using var owner = new ComponentOwner(new TestUiDispatcher());
        Control? oldRoot = null;
        var restoreShouldFail = false;
        var oldDisposed = 0;
        var nextDisposed = 0;
        var region = new ConditionalRegion(owner, root =>
        {
            if (root.Count == 1 && root[0] is TextBlock)
            {
                restoreShouldFail = true;
                throw new InvalidOperationException("publish");
            }
            if (restoreShouldFail && root.Count == 1 && ReferenceEquals(root[0], oldRoot))
            {
                throw new ApplicationException("restore");
            }
            oldRoot = root.Count == 0 ? null : root[0];
        });
        region.Show(1, branchOwner =>
        {
            branchOwner.OnDispose(() => oldDisposed++);
            return Fragment.From(new Border());
        });

        var failure = Assert.Throws<AggregateException>(() => region.Show(2, branchOwner =>
        {
            branchOwner.OnDispose(() => nextDisposed++);
            return Fragment.From(new TextBlock());
        }));

        var messages = failure.Flatten().InnerExceptions.Select(exception => exception.Message).ToArray();
        CollectionAssert.Contains(messages, "publish");
        CollectionAssert.Contains(messages, "restore");
        Assert.AreEqual(1, oldDisposed);
        Assert.AreEqual(1, nextDisposed);
        Assert.IsNull(region.ActiveBranch);
    }

    [TestMethod]
    public void Switch_and_clear_publish_before_disposing_the_old_branch()
    {
        using var owner = new ComponentOwner(new TestUiDispatcher());
        var events = new List<string>();
        var region = new ConditionalRegion(owner, root =>
            events.Add(root.Count == 0 ? "publish-null" : "publish-root"));
        region.Show(1, branchOwner =>
        {
            branchOwner.OnDispose(() => events.Add("dispose-one"));
            return Fragment.From(new Border());
        });
        region.Show(2, branchOwner =>
        {
            branchOwner.OnDispose(() => events.Add("dispose-two"));
            return Fragment.From(new TextBlock());
        });
        region.Clear();

        CollectionAssert.AreEqual(
            new[] { "publish-root", "publish-root", "dispose-one", "publish-null", "dispose-two" },
            events);
    }

    [TestMethod]
    public void Clear_failure_restores_the_active_root_before_rethrowing()
    {
        using var owner = new ComponentOwner(new TestUiDispatcher());
        Fragment published = default;
        var disposed = 0;
        var failClear = false;
        var calls = new List<Fragment>();
        var region = new ConditionalRegion(owner, root =>
        {
            calls.Add(root);
            published = root;
            if (failClear && root.Count == 0)
            {
                failClear = false;
                throw new InvalidOperationException("clear");
            }
        });
        region.Show(1, branchOwner =>
        {
            branchOwner.OnDispose(() => disposed++);
            return Fragment.From(new Border());
        });
        var old = published[0];
        failClear = true;

        Assert.Throws<InvalidOperationException>(region.Clear);

        Assert.AreSame(old, published[0]);
        Assert.AreSame(old, calls[^1][0]);
        Assert.AreEqual(0, disposed);
        Assert.AreEqual(1, region.ActiveBranch);
    }

    [TestMethod]
    public void Clear_restoration_failure_clears_state_and_disposes_the_old_owner()
    {
        using var owner = new ComponentOwner(new TestUiDispatcher());
        Control? old = null;
        var restoring = false;
        var disposed = 0;
        var region = new ConditionalRegion(owner, root =>
        {
            if (root.Count == 0)
            {
                restoring = true;
                throw new InvalidOperationException("clear");
            }
            if (restoring && root.Count == 1 && ReferenceEquals(root[0], old))
            {
                throw new ApplicationException("restore");
            }
            old = root.Count == 0 ? null : root[0];
        });
        region.Show(1, branchOwner =>
        {
            branchOwner.OnDispose(() => disposed++);
            return Fragment.From(new Border());
        });

        var failure = Assert.Throws<AggregateException>(region.Clear);

        CollectionAssert.Contains(
            failure.Flatten().InnerExceptions.Select(exception => exception.Message).ToArray(),
            "clear");
        CollectionAssert.Contains(
            failure.Flatten().InnerExceptions.Select(exception => exception.Message).ToArray(),
            "restore");
        Assert.AreEqual(1, disposed);
        Assert.IsNull(region.ActiveBranch);
    }
}
