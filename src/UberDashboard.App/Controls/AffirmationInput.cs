using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UberDashboard.App.Features.Affirmations;

namespace UberDashboard.App.Controls;

/// <summary>Pole ćwiczenia, które przekazuje wpisywane znaki do modelu panelu.</summary>
public sealed class AffirmationInput : System.Windows.Controls.TextBox
{
    public AffirmationInput()
    {
        PreviewTextInput += (_, e) =>
        {
            if (DataContext is AffirmationPanelViewModel vm) vm.Accept(e.Text);
            e.Handled = true;
        };
        System.Windows.DataObject.AddPastingHandler(this, (_, e) => e.CancelCommand());
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Space) return;
            if (DataContext is AffirmationPanelViewModel vm) vm.Accept(" ");
            e.Handled = true;
        };
    }

    public static readonly DependencyProperty FocusTargetProperty = DependencyProperty.RegisterAttached(
        "FocusTarget", typeof(AffirmationInput), typeof(AffirmationInput), new PropertyMetadata(null, OnFocusTargetChanged));

    public static AffirmationInput? GetFocusTarget(DependencyObject element) => (AffirmationInput?)element.GetValue(FocusTargetProperty);
    public static void SetFocusTarget(DependencyObject element, AffirmationInput? value) => element.SetValue(FocusTargetProperty, value);

    private static void OnFocusTargetChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not UIElement target) return;
        target.PreviewMouseLeftButtonDown -= FocusFromPrompt;
        if (e.NewValue is not null) target.PreviewMouseLeftButtonDown += FocusFromPrompt;
    }

    private static void FocusFromPrompt(object sender, MouseButtonEventArgs e)
    {
        if (sender is DependencyObject element && GetFocusTarget(element) is { IsEnabled: true } input)
        {
            input.Focus();
            e.Handled = true;
        }
    }

    public static readonly DependencyProperty KeepVisibleProperty = DependencyProperty.RegisterAttached(
        "KeepVisible", typeof(bool), typeof(AffirmationInput), new PropertyMetadata(false, OnKeepVisibleChanged));

    public static bool GetKeepVisible(DependencyObject element) => (bool)element.GetValue(KeepVisibleProperty);
    public static void SetKeepVisible(DependencyObject element, bool value) => element.SetValue(KeepVisibleProperty, value);

    /// <summary>Przewija otaczający ScrollViewer tak, by bieżąca afirmacja była zawsze widoczna.</summary>
    private static void OnKeepVisibleChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not FrameworkElement fe || e.NewValue is not true) return;
        System.ComponentModel.PropertyChangedEventHandler handler = (_, a) =>
        {
            if (a.PropertyName == nameof(AffirmationPanelViewModel.Typed))
                fe.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(fe.BringIntoView));
        };
        fe.Loaded += (_, _) => { if (fe.DataContext is AffirmationPanelViewModel vm) { vm.PropertyChanged -= handler; vm.PropertyChanged += handler; fe.BringIntoView(); } };
        fe.Unloaded += (_, _) => { if (fe.DataContext is AffirmationPanelViewModel vm) vm.PropertyChanged -= handler; };
    }
}
