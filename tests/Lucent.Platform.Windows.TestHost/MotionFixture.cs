using Lucent.Platform.Windows;

namespace Lucent.Platform.Windows.TestHost;

/// <summary>Published NativeAOT proof for scheduled presentation frames and bounded motion lifetime.</summary>
internal static class MotionFixture
{
    internal static int Run(bool reducedMotion)
    {
        try
        {
            return LucentApplication
                .CreateBuilder()
                .UseWindows()
                .SetTitle(reducedMotion ? "Lucent Reduced Motion Fixture" : "Lucent Motion Fixture")
                .SetTheme(_ => ControlThemes.Light)
                .Build()
                .Run(new MotionLifecycle(reducedMotion));
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("Lucent motion fixture: " + error.Message);
            return 1;
        }
    }

    private sealed class MotionLifecycle(bool reducedMotion) : IApplicationLifecycle
    {
        private readonly CancellationTokenSource _lifetime = new();
        private Task? _change;

        public ValueTask<ComponentRecipe> StartAsync(ApplicationSession session)
        {
            session.Theme.ReducedMotion = reducedMotion;
            var active = session.Scope.Signal(false, "motion-fixture.active");
            var owner =
                SynchronizationContext.Current
                ?? throw new InvalidOperationException("The motion fixture has no owner context.");
            _change = Task.Run(
                async () =>
                {
                    await Task.Delay(750, _lifetime.Token).ConfigureAwait(false);
                    owner.Post(_ => active.Value = true, null);
                },
                _lifetime.Token
            );
            return ValueTask.FromResult(
                ComponentRecipe.Create(
                    "motion-fixture",
                    (context, root) =>
                        root.Present(
                            context.Theme,
                            author: Style
                                .Empty.Width(800)
                                .Height(500)
                                .Bind(
                                    VisualProperties.Background,
                                    () =>
                                        Brush.Solid(
                                            Color.Parse(active.Value ? "#E33B32" : "#246BCE")
                                        )
                                )
                                .Transition(
                                    VisualProperties.Background,
                                    Motion.Duration(1200, Easing.Linear)
                                )
                        )
                )
            );
        }

        public ValueTask<bool> PrepareCloseAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(true);

        public async ValueTask StopAsync()
        {
            _lifetime.Cancel();
            if (_change is not null)
                try
                {
                    await _change.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        }

        public ValueTask DisposeAsync()
        {
            _lifetime.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
