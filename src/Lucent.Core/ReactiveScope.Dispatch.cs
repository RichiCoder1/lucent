namespace Lucent.Core;

public sealed partial class ReactiveScope
{
    private readonly object _postGate = new();
    private readonly HashSet<ScopePost> _pendingPosts = [];
    private bool _postsDisposed;

    /// <summary>Queues a callback from any thread for execution during the owning graph's next drain.</summary>
    /// <remarks>The returned handle cancels the callback. Cancellation and scope disposal release queued closures immediately. Posting to a disposed scope is a no-op.</remarks>
    public IDisposable Post(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ScopePost post;
        lock (_postGate)
        {
            post = new ScopePost(this, callback);
            if (_postsDisposed)
            {
                post.Release();
                return post;
            }
            _pendingPosts.Add(post);
        }
        // Disposal may cancel the payload before it reaches the graph queue.
        _graph.Post(post);
        return post;
    }

    private void CancelPosted()
    {
        lock (_postGate)
        {
            _postsDisposed = true;
            foreach (var post in _pendingPosts)
                post.Release();
            _pendingPosts.Clear();
        }
    }

    private bool CommitPosted(ScopePost post)
    {
        Action? callback;
        lock (_postGate)
        {
            if (!_pendingPosts.Remove(post))
                return false;
            callback = post.Release();
        }
        if (callback is null)
            return false;
        CheckMutationGuard();
        callback();
        return true;
    }

    private void CancelPost(ScopePost post)
    {
        lock (_postGate)
        {
            _pendingPosts.Remove(post);
            post.Release();
        }
    }

    private sealed class ScopePost(ReactiveScope owner, Action callback) : IDisposable, IPosted
    {
        private ReactiveScope? _owner = owner;
        private Action? _callback = callback;

        public bool Commit() => Volatile.Read(ref _owner)?.CommitPosted(this) ?? false;

        public void Dispose() => Volatile.Read(ref _owner)?.CancelPost(this);

        // All payload changes happen under the owner's post gate.
        internal Action? Release()
        {
            Volatile.Write(ref _owner, null);
            var callback = _callback;
            _callback = null;
            return callback;
        }
    }
}
