using UberDashboard.Core.Localization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using UberDashboard.App.Features.Notes;
using UberDashboard.App.Features.Todo;
using UberDashboard.App.Panels;
using UberDashboard.Application.Import;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using CheckBox = System.Windows.Controls.CheckBox;
using Control = System.Windows.Controls.Control;
using Orientation = System.Windows.Controls.Orientation;

namespace UberDashboard.App.Controls;

internal static class PanelFileImport
{
    private sealed record Draft(ImportedPanelTab Tab, string Preview);
    private sealed record CsvOptions(char Separator, bool Header, int TextColumn, int? StatusColumn);

    public static async Task RunAsync(PanelGroupSession session, Window? owner)
    {
        var picker = new UberDashboard.App.Controls.ThemedFileDialog
        {
            Title = UiText.Get("ChooseFilesToImport"),
            Multiselect = true,
            Filter = session.ImportsTasks ? UiText.Get("ListsTxtMdCsvTxtMdCsv") :
                UiText.Get("NotesTxtMdTxtMd"),
        };
        if (picker.ShowDialog(owner) != true) return;
        CsvOptions? csv = null;
        if (picker.FileNames.Any(path => Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase)))
        {
            csv = GetCsvOptions(owner);
            if (csv is null) return;
        }

        using var cancellation = new CancellationTokenSource();
        var progress = WindowFrame.Create(owner, UiText.Get("ReadingFiles"), 410, 150);
        var cancel = new Button { Content = UiText.Get("CancelReading"), Margin = new Thickness(16) };
        cancel.Click += (_, _) => cancellation.Cancel();
        progress.Content = cancel;
        var progressClosed = false;
        progress.Closed += (_, _) => { progressClosed = true; cancellation.Cancel(); };
        progress.Show();
        Draft[] drafts;
        try
        {
            try
            {
                drafts = await ReadDraftsAsync(picker.FileNames, session.ImportsTasks, csv, Encoding.UTF8, cancellation.Token);
            }
            catch (DecoderFallbackException)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                progress.Hide();
                if (ThemedMessageBox.Show(UiText.Get("AtLeastOneFileIsNotValid"),
                        UiText.Get("FileEncoding"), MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                    return;
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                progress.Show();
                drafts = await ReadDraftsAsync(picker.FileNames, session.ImportsTasks, csv,
                    Encoding.GetEncoding(1250), cancellation.Token);
            }
            cancellation.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return; }
        finally { if (!progressClosed) progress.Close(); }

        var preview = WindowFrame.Create(owner, UiText.Get("ImportPreview"), 650, 490);
        var container = new DockPanel { Margin = new Thickness(16) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var confirm = new Button { Content = UiText.Get("CreateSelectedTabs"), IsDefault = true, Margin = new Thickness(0, 8, 0, 0) };
        confirm.Click += (_, _) => preview.DialogResult = true;
        actions.Children.Add(new Button { Content = UiText.Get("Cancel"), IsCancel = true, Margin = new Thickness(0, 8, 8, 0) });
        actions.Children.Add(confirm);
        DockPanel.SetDock(actions, Dock.Bottom);
        container.Children.Add(actions);
        var list = new StackPanel();
        var choices = new List<(CheckBox Selection, Draft Draft)>();
        foreach (var draft in drafts)
        {
            var selection = new CheckBox { Content = draft.Tab.Name, IsChecked = true, Margin = new Thickness(0, 8, 0, 2) };
            list.Children.Add(selection);
            list.Children.Add(new TextBlock { Text = draft.Preview, TextWrapping = TextWrapping.Wrap });
            choices.Add((selection, draft));
            selection.Checked += (_, _) => confirm.IsEnabled = true;
            selection.Unchecked += (_, _) => confirm.IsEnabled = choices.Any(item => item.Selection.IsChecked == true);
        }
        container.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        preview.Content = container;
        if (preview.ShowDialog() != true) return;
        var tabs = choices.Where(item => item.Selection.IsChecked == true).Select(item => item.Draft.Tab).ToArray();
        if (tabs.Length > 0)
            await session.ImportTabsAsync(tabs, CancellationToken.None);
    }

    private static Task<Draft[]> ReadDraftsAsync(string[] paths, bool tasks, CsvOptions? csv, Encoding encoding,
        CancellationToken token) => Task.Run(async () =>
    {
        var sources = await ImportFileReader.ReadAsync(paths, encoding, token);
        var drafts = new List<Draft>();
        var count = 0;
        foreach (var source in sources)
        {
            token.ThrowIfCancellationRequested();
            var title = Path.GetFileNameWithoutExtension(source.Name).Trim();
            if (title.Length == 0) title = source.Name;
            var extension = Path.GetExtension(source.Name).ToLowerInvariant();
            if (!tasks)
            {
                if (extension is not (".txt" or ".md")) throw new InvalidDataException(UiText.Format("UnsupportedFile", source.Name));
                var document = new NotesDocument { Title = title, PlainText = source.Text };
                drafts.Add(new(new(title, JsonSerializer.Serialize(document), NotesDocument.SchemaVersion),
                    (extension == ".md" ? UiText.Get("MarkdownWillBeSavedAsPlainText") : "") + Shorten(source.Text)));
                continue;
            }

            var candidates = extension switch
            {
                ".csv" when csv is not null => TodoCsvImport.Parse(source.Text, csv.Separator, csv.Header, csv.TextColumn, csv.StatusColumn),
                ".md" => TodoTextImport.Parse(source.Text, true),
                ".txt" => TodoTextImport.Parse(source.Text, false),
                _ => throw new InvalidDataException(UiText.Format("UnsupportedFile", source.Name)),
            };
            var items = new List<ImportedTask>();
            foreach (var item in candidates)
            {
                token.ThrowIfCancellationRequested();
                if (++count > ImportFileReader.MaxTasks) throw new InvalidDataException(UiText.Get("TheLimitOf10000TasksWas"));
                items.Add(item);
            }
            var todo = new TodoDocument { Items = items.Select(item => new TodoItemState(item.Text, item.IsDone == true)).ToArray() };
            var text = UiText.Format("TasksIncludingCompleted", items.Count, items.Count(item => item.IsDone == true));
            if (items.Any(item => item.IsDone is null)) text += UiText.Get("UnrecognizedStatusesWillBeTreatedAsIncomplete");
            if (items.Any(item => item.IsNested)) text += UiText.Get("NestedTasksWillBecomeSeparateItems");
            text += "\n" + string.Join("\n", items.Take(6).Select(item => Shorten(item.Text, 120)));
            drafts.Add(new(new(title, JsonSerializer.Serialize(todo), TodoDocument.SchemaVersion), text));
        }
        return drafts.ToArray();
    }, token);

    private static string Shorten(string text, int max = 400) => text.Length <= max ? text : text[..max] + "…";

    private static CsvOptions? GetCsvOptions(Window? owner)
    {
        var dialog = WindowFrame.Create(owner, UiText.Get("CsvSettingsForSelectedFiles"), 425, 330);
        var form = new StackPanel { Margin = new Thickness(16) };
        var separator = new ComboBox { ItemsSource = new[] { UiText.Get("Semicolon"), UiText.Get("Comma"), UiText.Get("Tab") }, SelectedIndex = 0 };
        var header = new CheckBox { Content = UiText.Get("FirstRowContainsHeaders"), IsChecked = true, Margin = new Thickness(0, 8, 0, 8) };
        var text = new TextBox { Text = "1" };
        var status = new TextBox();
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var next = new Button { Content = UiText.Get("Preview"), IsDefault = true, Margin = new Thickness(0, 8, 0, 0) };
        CsvOptions? options = null;
        next.Click += (_, _) =>
        {
            int statusIndex = 0;
            if (!int.TryParse(text.Text, out var textIndex) || textIndex < 1 ||
                (!string.IsNullOrWhiteSpace(status.Text) && (!int.TryParse(status.Text, out statusIndex) || statusIndex < 1)))
            {
                error.Text = UiText.Get("ColumnNumbersMustBePositive");
                return;
            }
            options = new(";,\t"[separator.SelectedIndex], header.IsChecked == true, textIndex - 1,
                statusIndex > 0 ? statusIndex - 1 : null);
            dialog.DialogResult = true;
        };
        form.Children.Add(new TextBlock { Text = UiText.Get("Delimiter") });
        form.Children.Add(separator);
        form.Children.Add(header);
        form.Children.Add(new TextBlock { Text = UiText.Get("TextColumnNumber") });
        form.Children.Add(text);
        form.Children.Add(new TextBlock { Text = UiText.Get("StatusColumnNumberOptional") });
        form.Children.Add(status);
        form.Children.Add(next);
        form.Children.Add(error);
        dialog.Content = form;
        return dialog.ShowDialog() == true ? options : null;
    }
}
