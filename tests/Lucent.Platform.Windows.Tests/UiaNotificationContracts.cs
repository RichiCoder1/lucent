using Lucent.Core;
using Lucent.Renderer.Skia;
using SDL3;
using MAssert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Lucent.Platform.Windows.Tests;

public sealed unsafe partial class UiaLifecycleContracts
{
    [TestMethod]
    public void PoliteStatusPlansOneMostRecentNativeNotificationPerOwnerRefresh()
    {
        Assert(SDL.Init(SDL.InitFlags.Video), "SDL_Init(UIA notification) failed.");
        var window = CreateWindow("Lucent UIA notification");
        try
        {
            var graph = new ReactiveGraph();
            using var composition = new Composition(graph, "uia-notification");
            using var theme = new ThemeContext(composition.Root.Scope, ControlThemes.Light);
            var ordinary = graph.Signal("Quiet", "ordinary-status");
            var polite = graph.Signal("Waiting", "polite-status");
            composition.Mount(
                composition.Root,
                theme,
                Components.Column(
                    ComponentContent.Create([
                        Components.Status(() => ordinary.Value),
                        Components.Status(
                            () => polite.Value,
                            announcement: SemanticAnnouncement.Polite
                        ),
                    ])
                )
            );
            composition.Flush();
            using var renderer = new SkiaSceneRenderer();
            using var dispatcher = new WindowsUiaDispatcher();
            using var provider = new WindowsUiaProvider(
                Hwnd(window),
                composition,
                dispatcher,
                "Notification"
            );
            var planned = new List<(string Text, int Kind, int Processing, string Activity)>();
            provider.NotificationEventPlanned = (text, kind, processing, activity) =>
                planned.Add((text, kind, processing, activity));

            using (var initial = SceneLayout.Project(composition, new(320, 120, 1), renderer))
                provider.Refresh(initial);
            MAssert.AreEqual(0L, provider.DetectedNotificationChanges, "Initial mount announced.");

            ordinary.Value = "Still quiet";
            composition.Flush();
            using (var defaultUpdate = SceneLayout.Project(composition, new(320, 120, 1), renderer))
                provider.Refresh(defaultUpdate);
            MAssert.AreEqual(0L, provider.DetectedNotificationChanges, "Default status announced.");

            polite.Value = "Working";
            polite.Value = "Almost done";
            polite.Value = "Done";
            composition.Flush();
            using (var coalesced = SceneLayout.Project(composition, new(320, 120, 1), renderer))
            {
                provider.Refresh(coalesced);
                provider.Refresh(coalesced);
            }

            MAssert.AreEqual(1L, provider.DetectedNotificationChanges);
            MAssert.AreEqual(1, planned.Count);
            MAssert.AreEqual("Done", planned[0].Text);
            MAssert.AreEqual(4, planned[0].Kind, "NotificationKind.Other ABI value changed.");
            MAssert.AreEqual(
                3,
                planned[0].Processing,
                "NotificationProcessing.MostRecent ABI value changed."
            );
            StringAssert.StartsWith(planned[0].Activity, "lucent:");

            polite.Value = "Done";
            composition.Flush();
            using (var equal = SceneLayout.Project(composition, new(320, 120, 1), renderer))
                provider.Refresh(equal);
            MAssert.AreEqual(1L, provider.DetectedNotificationChanges, "Equal text was repeated.");
        }
        finally
        {
            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }
}
