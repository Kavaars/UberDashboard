using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using UberDashboard.App.Features.Todo;

namespace UberDashboard.App.Controls;

/// <summary>Pole edycji wpisu na liście: Enter lub utrata fokusu zatwierdza, Esc anuluje.</summary>
public sealed class InlineEditTextBox : System.Windows.Controls.TextBox
{
    public InlineEditTextBox()
    {
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true) Dispatcher.BeginInvoke(() => { Focus(); SelectAll(); }, DispatcherPriority.Input);
        };
    }

    protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Enter) { e.Handled = true; Finish(commit: true); }
        else if (e.Key == Key.Escape) { e.Handled = true; Finish(commit: false); }
    }

    protected override void OnLostKeyboardFocus(System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        Finish(commit: true);
    }

    private void Finish(bool commit)
    {
        if (DataContext is not TodoItemViewModel { IsEditing: true } item) return;
        var binding = GetBindingExpression(TextProperty);
        if (commit && !string.IsNullOrWhiteSpace(Text)) binding?.UpdateSource(); else binding?.UpdateTarget();
        item.IsEditing = false;
    }
}
