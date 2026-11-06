using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Threading;
using UberDashboard.App.Features.Integrations;
using UberDashboard.App.Theming;
using UberDashboard.Core.Localization;
using UberDashboard.Core.Synchronization;

internal static class GoogleEventEditorChecks
{
    internal static async Task RunAsync(Window owner, ThemeService theme, string output)
    {
        foreach (var themeKind in new[] { ThemeKind.Light, ThemeKind.Dark })
        {
            await theme.ApplyAsync(themeKind, default);
            foreach (var scenario in new[] { "organizer", "guest", "special" })
            {
                var original = JsonNode.Parse("""{"summary":"Spotkanie / Meeting","start":{"dateTime":"2030-05-12T10:00:00+09:00","timeZone":"Asia/Tokyo"},"end":{"dateTime":"2030-05-12T11:00:00+09:00","timeZone":"Asia/Tokyo"},"attendees":[{"email":"me@example.invalid","self":true,"responseStatus":"accepted"},{"email":"host@example.invalid","organizer":true,"optional":true,"responseStatus":"tentative","comment":"Keep"}],"reminders":{"useDefault":true}}""")!.AsObject();
                if (scenario == "special") original["eventType"] = "birthday";
                var metadata = new JsonObject { ["organizer"] = new JsonObject { ["self"] = scenario == "organizer" } };
                Exception? failure = null;
                var result = SyncEditorWindow.Edit(owner, SyncProviderKind.GoogleCalendar, original, window =>
                    window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                    {
                        try
                        {
                            foreach (var expander in SyncEditorChecks.Descendants(window).OfType<Expander>()) expander.IsExpanded = true;
                            window.UpdateLayout();
                            var save = SyncEditorChecks.Descendants(window).OfType<Button>().Single(b => Equals(b.Content, UiText.Get("SyncSaveLocal")));
                            T Control<T>(string key) where T : FrameworkElement => SyncEditorChecks.Descendants(window).OfType<T>().Single(c => AutomationProperties.GetName(c) == UiText.Get(key));
                            var emails = Control<TextBox>("SyncGoogleEmails");
                            if (emails.IsReadOnly != (scenario != "organizer")) throw new InvalidOperationException("Attendee edit rights were not applied.");
                            if (Control<TextBox>("SyncTitle").IsEnabled != (scenario == "organizer")) throw new InvalidOperationException("Shared event fields were enabled for an unprivileged copy.");
                            if (scenario == "organizer")
                            {
                                emails.Text += "\ninvalid"; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                if (!window.IsVisible) throw new InvalidOperationException("Invalid attendee was saved.");
                                emails.Text = GoogleEventOptions.Read(original).AttendeeEmails + "\nnew@example.invalid";
                                Control<ComboBox>("SyncGoogleReminderMode").SelectedIndex = 2;
                                var reminderKey = UiText.Format("SyncGoogleReminderNumber", 1);
                                SyncEditorChecks.Descendants(window).OfType<CheckBox>().Single(c => AutomationProperties.GetName(c) == reminderKey).IsChecked = true;
                                var minutes = SyncEditorChecks.Descendants(window).OfType<TextBox>().Single(c => AutomationProperties.GetName(c) == reminderKey + " · " + UiText.Get("SyncGoogleReminderMinutes"));
                                minutes.Text = "40321"; save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                if (!window.IsVisible) throw new InvalidOperationException("Invalid reminder was saved.");
                                minutes.Text = "30";
                                if (SyncEditorChecks.Descendants(window).OfType<TextBlock>().Any(t => t.Text == UiText.Get("SyncGoogleInvalidReminders")))
                                    throw new InvalidOperationException("A corrected reminder retained a stale validation message.");
                                Control<ComboBox>("SyncGoogleVisibility").SelectedIndex = 2;
                                Control<ComboBox>("SyncGoogleAvailability").SelectedIndex = 1;
                            }
                            else if (scenario == "guest") Control<ComboBox>("SyncGoogleResponse").SelectedIndex = 3;
                            else if (Control<ComboBox>("SyncGoogleReminderMode").IsEnabled || Control<ComboBox>("SyncGoogleResponse").IsEnabled)
                                throw new InvalidOperationException("Special-event operations were exposed through the default form.");
                            foreach (var size in new[] { new Size(620, 640), new Size(420, 360) })
                            {
                                window.Width = size.Width; window.Height = size.Height;
                                emails.BringIntoView(); window.UpdateLayout();
                                SyncEditorChecks.Capture(window, Path.Combine(output, $"{themeKind}-google-{scenario}-{size.Width}x{size.Height}.png"));
                                var position = save.TransformToAncestor(window).Transform(new Point());
                                if (position.Y < 0 || position.Y + save.ActualHeight > window.ActualHeight) throw new InvalidOperationException("Google save action is clipped.");
                            }
                            window.Width = 620; window.Height = 640;
                            foreach (var element in SyncEditorChecks.Descendants(window))
                            {
                                if (element is System.Windows.Controls.Control control) control.FontSize = 24;
                                else if (element is TextBlock text) text.FontSize = 24;
                            }
                            Control<ComboBox>("SyncGoogleReminderMode").BringIntoView(); window.UpdateLayout();
                            SyncEditorChecks.Capture(window, Path.Combine(output, $"{themeKind}-google-{scenario}-font24.png"));
                            save.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        }
                        catch (Exception error) { failure = error; window.Close(); }
                    })), metadata);
                if (failure is not null || result is null) throw new InvalidOperationException("Google event editor failed: " + scenario, failure);
                if (!JsonNode.DeepEquals(original["start"], result["start"]) || !JsonNode.DeepEquals(original["end"], result["end"]))
                    throw new InvalidOperationException("Google options changed an unrelated time zone.");
                if (scenario == "organizer" && (result["attendees"]!.AsArray().Count != 3 || result["reminders"]!["overrides"]![0]!["minutes"]!.GetValue<int>() != 30 ||
                    result["visibility"]!.GetValue<string>() != "private" || result["transparency"]!.GetValue<string>() != "transparent"))
                    throw new InvalidOperationException("Google options were not saved locally.");
                if (scenario == "guest" && (result["attendees"]![0]!["responseStatus"]!.GetValue<string>() != "tentative" || !JsonNode.DeepEquals(result["attendees"]![1], original["attendees"]![1])))
                    throw new InvalidOperationException("RSVP altered someone else's attendance.");
                if (scenario == "special" && !JsonNode.DeepEquals(original, result)) throw new InvalidOperationException("The read-only special event was modified.");
            }
        }
        Console.WriteLine("GOOGLE OPTIONS: attendee preservation / RSVP / reminders / roles / special types / 18 renders PASS");
    }
}
