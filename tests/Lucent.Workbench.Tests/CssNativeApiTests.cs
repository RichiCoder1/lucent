using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit;

namespace Lucent.Workbench.Tests;

[TestClass]
public sealed class CssNativeApiTests
{
    [TestMethod]
    public async Task Coded_dynamic_resource_setter_tracks_application_resource_changes()
    {
        await HeadlessTestHarness.RunWindowAsync(window =>
        {
            var border = new Border();
            window.Content = border;
            Application.Current!.Resources.ThemeDictionaries[ThemeVariant.Light] =
                new ResourceDictionary { ["Lucent.Canvas"] = new SolidColorBrush(Colors.Red) };
            Application.Current.Resources.ThemeDictionaries[ThemeVariant.Dark] =
                new ResourceDictionary { ["Lucent.Canvas"] = new SolidColorBrush(Colors.Blue) };
            window.Styles.Add(new Style(selector => selector.OfType<Border>())
            {
                Setters =
                {
                    new Setter(Border.BackgroundProperty,
                        new DynamicResourceExtension("Lucent.Canvas")),
                },
            });
            window.RequestedThemeVariant = ThemeVariant.Light;
            window.UpdateLayout();
            Assert.AreEqual(Colors.Red, ((SolidColorBrush)border.Background!).Color);

            window.RequestedThemeVariant = ThemeVariant.Dark;
            window.UpdateLayout();
            Assert.AreEqual(Colors.Blue, ((SolidColorBrush)border.Background!).Color);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Avalonia_1211_exposes_the_required_transition_families()
    {
        Assert.IsNotNull(typeof(BrushTransition));
        Assert.IsNotNull(typeof(DoubleTransition));
        Assert.IsNotNull(typeof(ThicknessTransition));
        Assert.IsNotNull(typeof(CornerRadiusTransition));
        Assert.IsNotNull(typeof(BoxShadowsTransition));
    }

    [TestMethod]
    public async Task Catalog_transition_pairs_bind_to_the_public_native_properties()
    {
        await HeadlessTestHarness.RunWindowAsync(_ =>
        {
            var transitions = new Transitions
            {
                new BrushTransition { Property = Border.BackgroundProperty },
                new DoubleTransition { Property = Border.OpacityProperty },
                new ThicknessTransition { Property = Border.BorderThicknessProperty },
                new CornerRadiusTransition { Property = Border.CornerRadiusProperty },
                new BoxShadowsTransition { Property = Border.BoxShadowProperty },
            };

            Assert.HasCount(5, transitions);
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task Fluent_focus_resources_cover_native_controls_without_template_replacement()
    {
        await HeadlessTestHarness.RunWindowAsync(window =>
        {
            var resources = Application.Current!.Resources;
            resources["SystemControlFocusVisualPrimaryBrush"] = new SolidColorBrush(Colors.Teal);
            resources["SystemControlFocusVisualSecondaryBrush"] = new SolidColorBrush(Colors.Teal);
            resources["SystemControlFocusVisualMargin"] = new Thickness(2);
            resources["SystemControlFocusVisualPrimaryThickness"] = new Thickness(2);
            resources["SystemControlFocusVisualSecondaryThickness"] = new Thickness(0);

            var button = new Button { Content = "Button" };
            var textBox = new TextBox { Text = "TextBox" };
            var list = new ListBox { Focusable = true, ItemsSource = new[] { "ListBox" } };
            var editor = new TextEditor { Focusable = true, Text = "AvaloniaEdit" };
            window.Content = new StackPanel { Children = { button, textBox, list, editor } };
            window.UpdateLayout();

            Assert.AreEqual(2d, ((Thickness)resources["SystemControlFocusVisualMargin"]!).Left);
            Assert.AreEqual(2d, ((Thickness)resources["SystemControlFocusVisualPrimaryThickness"]!).Left);
            Assert.AreEqual(0d, ((Thickness)resources["SystemControlFocusVisualSecondaryThickness"]!).Left);
            foreach (var control in new Control[] { button, textBox, list })
            {
                Assert.IsTrue(control.Focus(), control.GetType().Name);
                Assert.AreSame(control, window.FocusManager?.GetFocusedElement(), control.GetType().Name);
            }
            Assert.IsTrue(editor.Focusable);
            Assert.IsTrue(editor.TextArea.Focus());
            Assert.AreSame(editor.TextArea, window.FocusManager?.GetFocusedElement());
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public async Task Untyped_class_ancestor_matches_a_different_typed_descendant()
    {
        await HeadlessTestHarness.RunWindowAsync(window =>
        {
            var editor = new TextBox();
            var row = new Border { Child = editor };
            row.Classes.Add("todo-row");
            row.Classes.Add("completed");
            window.Content = row;
            row.Styles.Add(new Style(selector => selector
                .Class("todo-row").Class("completed")
                .Descendant().OfType<TextBox>())
            {
                Setters = { new Setter(TextBox.OpacityProperty, 0.5d) },
            });

            window.UpdateLayout();
            Assert.AreEqual(0.5d, editor.Opacity);
            return Task.CompletedTask;
        });
    }
}
