using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Automation;
using AvaloniaEdit;

namespace Lucent.Examples.Workbench;

internal sealed class AccessibleTextEditor : TextEditor
{
    protected override Type StyleKeyOverride => typeof(TextEditor);

    protected override AutomationPeer OnCreateAutomationPeer() => new EditorPeer(this);

    private sealed class EditorPeer : ControlAutomationPeer, IValueProvider
    {
        public EditorPeer(AccessibleTextEditor owner) : base(owner)
        {
            owner.TextChanged += OnTextChanged;
        }

        private void OnTextChanged(object? sender, EventArgs args) =>
            RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, null, Editor.Text);

        private AccessibleTextEditor Editor => (AccessibleTextEditor)Owner;
        bool IValueProvider.IsReadOnly => Editor.IsReadOnly;
        string IValueProvider.Value => Editor.Text;
        void IValueProvider.SetValue(string? value)
        {
            if (Editor.IsReadOnly) throw new InvalidOperationException("The editor is read-only.");
            Editor.Text = value ?? string.Empty;
        }

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
    }
}
