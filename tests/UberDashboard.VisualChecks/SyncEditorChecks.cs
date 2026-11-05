using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using UberDashboard.App.Features.Integrations;
using UberDashboard.App.Theming;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;

internal static class SyncEditorChecks
{
    internal static async Task RunAsync(Window owner, ThemeService theme, string output)
    {
        _ = new System.Windows.Interop.WindowInteropHelper(owner).EnsureHandle();
        foreach (var kind in new[] { ThemeKind.Light, ThemeKind.Dark })
        {
            await theme.ApplyAsync(kind, CancellationToken.None);
            foreach (var provider in Enum.GetValues<SyncProviderKind>())
            {
                var original = provider == SyncProviderKind.MicrosoftTodo
                    ? JsonNode.Parse("""{"title":"Próba","status":"waitingOnOthers","body":{"contentType":"html","content":"<p>Zachowaj opis</p>"},"importance":"high"}""")!.AsObject()
                    : JsonNode.Parse("""{"summary":"Spotkanie","description":"Opis","start":{"dateTime":"2030-05-12T10:00:00+09:00","timeZone":"Asia/Tokyo"},"end":{"dateTime":"2030-05-12T11:00:00+09:00","timeZone":"Asia/Tokyo"},"visibility":"private"}""")!.AsObject();
                Exception? failure = null;
                var result = SyncEditorWindow.Edit(owner, provider, original, window =>
                {
                    _ = window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                    try
                    {
                        var title = Descendants(window).OfType<TextBox>().Single(f => AutomationProperties.GetName(f) == UiText.Get("SyncTitle"));
                        var save = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, UiText.Get("SyncSaveLocal")));
                        title.Text = ""; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        if (!window.IsVisible) throw new InvalidOperationException("An empty title closed the synchronization editor.");
                        title.Text = "Zmieniony tytuł / Changed title";
                        if (provider == SyncProviderKind.MicrosoftTodo)
                        {
                            var elements = Descendants(window).ToArray();
                            var dates = elements.OfType<Expander>().Single(e => Equals(e.Header, UiText.Get("SyncTaskDates")));
                            dates.IsExpanded = true;
                            var recurrence = elements.OfType<Expander>().Single(e => Equals(e.Header, UiText.Get("SyncTaskRecurrence")));
                            recurrence.IsExpanded = true; window.UpdateLayout();
                            elements = Descendants(window).ToArray();
                            elements.OfType<ComboBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncTaskImportance")).SelectedIndex = 0;
                            elements.OfType<TextBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncTaskCategories")).Text = "Work\nHome";
                            elements.OfType<CheckBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncTaskDueDate")).IsChecked = true;
                            elements.OfType<DatePicker>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncTaskDueDate") + " · " + UiText.Get("SyncDate")).SelectedDate = new DateTime(2030, 5, 12);
                            elements.OfType<TextBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncTaskDueDate") + " · " + UiText.Get("SyncTimeInZone")).Text = "13:14:15";
                            elements.OfType<TextBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncTaskDueDate") + " · " + UiText.Get("SyncTimeZone")).Text = "Europe/Warsaw";
                            elements.OfType<CheckBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncRecurrenceEnabled")).IsChecked = true;
                            elements.OfType<ComboBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncRecurrencePattern")).SelectedIndex = 1;
                            elements.OfType<CheckBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncWeekday_monday")).IsChecked = true;
                            elements.OfType<CheckBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncWeekday_friday")).IsChecked = true;
                            elements.OfType<TextBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncRecurrenceInterval")).Text = "2";
                            var datePicker = elements.OfType<DatePicker>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncTaskDueDate") + " · " + UiText.Get("SyncDate"));
                            datePicker.ApplyTemplate(); datePicker.IsDropDownOpen = true; window.UpdateLayout();
                            if (datePicker.Template.FindName("PART_Popup", datePicker) is not System.Windows.Controls.Primitives.Popup { Child: Calendar popupCalendar })
                                throw new InvalidOperationException("The themed date picker lost its native calendar.");
                            popupCalendar.SelectedDate = new DateTime(2030, 5, 12); datePicker.IsDropDownOpen = false;
                            if (datePicker.SelectedDate != new DateTime(2030, 5, 12)) throw new InvalidOperationException("Date selection did not update the editor.");
                            datePicker.Text = "invalid date";
                            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            if (!window.IsVisible) throw new InvalidOperationException("Invalid typed date silently saved the previous date.");
                            datePicker.SelectedDate = new DateTime(2030, 5, 13); datePicker.SelectedDate = new DateTime(2030, 5, 12);
                            elements.OfType<DatePicker>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncRecurrenceStart")).SelectedDate = new DateTime(2030, 1, 1);
                            elements.OfType<ComboBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncRecurrenceRange")).SelectedIndex = 2;
                            elements.OfType<TextBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncRecurrenceCount")).Text = "5";
                            // Invalid recurrence stays in the form without discarding its draft.
                            elements.OfType<TextBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncRecurrenceInterval")).Text = "0";
                            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            if (!window.IsVisible) throw new InvalidOperationException("Invalid recurrence closed the editor.");
                            elements.OfType<TextBox>().Single(e => AutomationProperties.GetName(e) == UiText.Get("SyncRecurrenceInterval")).Text = "2";
                        }
                        foreach (var size in new[] { new Size(620, 640), new Size(420, 360) })
                        {
                            window.Width = size.Width; window.Height = size.Height; window.UpdateLayout();
                            Capture(window, Path.Combine(output, $"{kind}-sync-{provider}-{size.Width}x{size.Height}.png"));
                            var position = save.TransformToAncestor(window).Transform(new Point());
                            if (position.Y < 0 || position.Y + save.ActualHeight > window.ActualHeight)
                                throw new InvalidOperationException("Save action is clipped in the small editor.");
                        }
                        if (provider == SyncProviderKind.MicrosoftTodo)
                        {
                            window.Width = 620; window.Height = 640; window.FontSize = 24;
                            foreach (var element in Descendants(window))
                            {
                                if (element is Control control) control.FontSize = 24;
                                else if (element is TextBlock text) text.FontSize = 24;
                            }
                            window.UpdateLayout();
                            foreach (var key in new[] { "SyncTaskDueDate", "SyncRecurrencePattern" })
                            {
                                var element = Descendants(window).OfType<FrameworkElement>().First(e => AutomationProperties.GetName(e) == UiText.Get(key));
                                element.BringIntoView(); window.UpdateLayout();
                                Capture(window, Path.Combine(output, $"{kind}-sync-task-{key}-font24.png"));
                            }
                        }
                        if (Descendants(window).OfType<TextBlock>().Any(t => t.Text.Contains("Option {", StringComparison.Ordinal)))
                            throw new InvalidOperationException("A technical option description leaked into the editor.");
                        save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    }
                    catch (Exception error) { failure = error; window.Close(); }
                    }));
                });
                if (failure is not null || result is null) throw new InvalidOperationException("Synchronization editor failed.", failure);
                var preserved = provider == SyncProviderKind.MicrosoftTodo ? "body" : "start";
                if (!JsonNode.DeepEquals(original[preserved], result[preserved]))
                    throw new InvalidOperationException("Editing the title changed an unrelated field.");
                if (provider == SyncProviderKind.MicrosoftTodo && result["status"]!.GetValue<string>() != "waitingOnOthers")
                    throw new InvalidOperationException("Editing the title flattened the task's status.");
                if (provider == SyncProviderKind.MicrosoftTodo && (result["importance"]!.GetValue<string>() != "low" ||
                    result["dueDateTime"]!["timeZone"]!.GetValue<string>() != "Europe/Warsaw" ||
                    result["recurrence"]!["pattern"]!["type"]!.GetValue<string>() != "weekly" ||
                    result["recurrence"]!["range"]!["numberOfOccurrences"]!.GetValue<int>() != 5))
                    throw new InvalidOperationException("Advanced task fields were not persisted by the form.");
            }
        }
        Console.WriteLine("SYNC EDITORS: validation / advanced task fields / localized controls / preservation / 12 renders PASS");
    }

    private static void Capture(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, index))) yield return child;
    }
}
