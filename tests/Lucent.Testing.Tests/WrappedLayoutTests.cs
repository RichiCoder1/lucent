using System.Buffers.Binary;
using Lucent.Core;
using Lucent.Testing.Skia;

namespace Lucent.Testing.Tests;

[TestClass]
public sealed class WrappedLayoutTests
{
    [TestMethod]
    [DataRow(1f)]
    [DataRow(1.5f)]
    [DataRow(2f)]
    public async Task CompiledWrappingToolbarKeepsFollowingContentBelowItsLastLine(float scale)
    {
        await using var app = await SkiaHeadlessApplication.StartAsync(
            HeadlessFixtures.Components.WrappedToolbar(),
            new HeadlessApplicationOptions { Viewport = new(520, 260, scale) }
        );
        var wide = await app.SnapshotAsync();
        var first = wide.Require(SemanticRole.Button, "Open issues");
        var clear = wide.Require(SemanticRole.Button, "Clear filters");
        var following = wide.Require(SemanticRole.Text, "Following content");
        var originalY = wide.RequireBox(following).Bounds.Y;
        Assert.AreEqual(wide.RequireBox(first).Bounds.Y, wide.RequireBox(clear).Bounds.Y);

        await app.ResizeAsync(new(340, 260, scale));
        var narrow = await app.SnapshotAsync();
        var lastLine = narrow
            .RequireBox(narrow.Require(SemanticRole.Button, "Clear filters"))
            .Bounds;
        var followingBounds = narrow
            .RequireBox(narrow.Require(SemanticRole.Text, "Following content"))
            .Bounds;
        Assert.IsTrue(
            lastLine.Y
                > narrow.RequireBox(narrow.Require(SemanticRole.Button, "Open issues")).Bounds.Y
        );
        Assert.IsTrue(
            followingBounds.Y >= lastLine.Y + lastLine.Height + 5,
            $"Following content overlaps the wrapped toolbar: last={lastLine}; following={followingBounds}."
        );
        var png = await app.CapturePngAsync();
        Assert.AreEqual((int)(340 * scale), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        var captureDirectory = Environment.GetEnvironmentVariable("LUCENT_LAYOUT_CAPTURES");
        if (!string.IsNullOrEmpty(captureDirectory))
        {
            Directory.CreateDirectory(captureDirectory);
            await File.WriteAllBytesAsync(
                Path.Combine(captureDirectory, $"wrapped-toolbar-{scale:0.0}.png"),
                png
            );
        }

        await app.ResizeAsync(new(520, 260, scale));
        var restored = await app.SnapshotAsync();
        Assert.AreEqual(
            originalY,
            restored.RequireBox(restored.Require(SemanticRole.Text, "Following content")).Bounds.Y
        );
        Assert.AreEqual(
            clear.Identity,
            restored.Require(SemanticRole.Button, "Clear filters").Identity
        );
        Assert.AreEqual(wide.Scene.Boxes.Count, restored.Scene.Boxes.Count);
    }
}
