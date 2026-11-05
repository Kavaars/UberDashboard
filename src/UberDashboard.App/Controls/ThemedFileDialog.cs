using UberDashboard.Core.Localization;
using System.IO;
using System.IO.Enumeration;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using Control = System.Windows.Controls.Control;
using SelectionMode = System.Windows.Controls.SelectionMode;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using ListBox = System.Windows.Controls.ListBox;
using TextBox = System.Windows.Controls.TextBox;

namespace UberDashboard.App.Controls;

/// <summary>Wybór plików w oknie korzystającym z motywu aplikacji.</summary>
internal sealed class ThemedFileDialog
{
    public required string Title { get; init; }
    public required string Filter { get; init; }
    public bool Multiselect { get; init; }
    public bool SaveFile { get; init; }
    public string DefaultExt { get; init; } = "";
    public string FileName { get; set; } = "";
    public string[] FileNames { get; private set; } = [];

    public bool? ShowDialog(Window? owner = null)
    {
        owner ??= System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive);
        var window = WindowFrame.Create(owner, Title, 650, 520);
        window.MinWidth = 410;
        window.MinHeight = 320;
        using var cancel = new CancellationTokenSource();
        var token = cancel.Token;
        window.Closed += (_, _) => cancel.Cancel();

        var folder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrWhiteSpace(folder)) folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = new TextBox { Text = folder };
        var filters = Filter.Split('|').Chunk(2).Select(parts => new FileFilter(parts[0], parts[1].Split(';'))).ToArray();
        var filter = new ComboBox { ItemsSource = filters, DisplayMemberPath = nameof(FileFilter.Name), SelectedIndex = 0 };
        var list = new ListBox
        {
            DisplayMemberPath = nameof(FileEntry.Label),
            SelectionMode = Multiselect ? SelectionMode.Extended : SelectionMode.Single,
        };
        list.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "ThemedListBoxItem");
        list.SetResourceReference(Control.BackgroundProperty, "Brush.Surface");
        list.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        var filename = new TextBox { Text = FileName };
        path.SetResourceReference(FrameworkElement.StyleProperty, "ThemedTextBox");
        filename.SetResourceReference(FrameworkElement.StyleProperty, "ThemedTextBox");
        filter.SetResourceReference(FrameworkElement.StyleProperty, "ThemedComboBox");
        AutomationProperties.SetName(path, UiText.Get("FolderPath"));
        AutomationProperties.SetName(filename, UiText.Get("FileNameOrFullPath"));
        AutomationProperties.SetName(filter, UiText.Get("FileType"));
        AutomationProperties.SetName(list, UiText.Get("FoldersAndFiles"));

        var root = new DockPanel { Margin = new Thickness(14) };
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        var navigation = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var go = MakeButton(UiText.Get("Go"));
        var up = MakeButton(UiText.Get("Up"));
        var drives = MakeButton(UiText.Get("Drives"));
        foreach (var button in new[] { go, up, drives })
        {
            DockPanel.SetDock(button, Dock.Right);
            navigation.Children.Add(button);
        }
        navigation.Children.Add(path);
        top.Children.Add(navigation);
        top.Children.Add(filter);
        root.Children.Add(top);

        var bottom = new StackPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        var status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 4) };
        var buttons = new WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 6, 0, 0) };
        var accept = MakeButton(SaveFile ? UiText.Get("Save") : UiText.Get("Open"));
        var close = MakeButton(UiText.Get("Cancel"));
        close.IsCancel = true;
        buttons.Children.Add(accept);
        buttons.Children.Add(close);
        bottom.Children.Add(status);
        bottom.Children.Add(filename);
        bottom.Children.Add(buttons);
        root.Children.Add(bottom);
        root.Children.Add(list);
        window.Content = root;
        var revision = 0;
        var confirming = false;

        async Task NavigateAsync(string? requested)
        {
            var current = ++revision;
            list.ItemsSource = null;
            accept.IsEnabled = false;
            status.Text = UiText.Get("ReadingFolder");
            var pattern = (FileFilter)filter.SelectedItem;
            try
            {
                var resolved = requested is null ? null : Path.GetFullPath(requested, folder);
                var entries = await Task.Run(() => ReadEntries(resolved, pattern, token), token);
                if (token.IsCancellationRequested || current != revision) return;
                if (resolved is not null) folder = resolved;
                path.Text = resolved ?? "";
                list.ItemsSource = entries;
                accept.IsEnabled = true;
                status.Text = resolved is null ? UiText.Get("ChooseADrive") : UiText.Format("ItemsYouCanEnterAFullFile", entries.Length);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                if (token.IsCancellationRequested || current != revision) return;
                accept.IsEnabled = true;
                status.Text = UiText.Get("CouldNotOpenTheFolderCheckIts");
            }
        }

        async Task AcceptAsync()
        {
            if (confirming || !accept.IsEnabled) return;
            if (list.SelectedItem is FileEntry { IsDirectory: true } directory && (!SaveFile || string.IsNullOrWhiteSpace(filename.Text)))
            {
                await NavigateAsync(directory.Path);
                return;
            }
            confirming = true;
            try
            {
                var selection = list.SelectedItems.OfType<FileEntry>().Where(entry => !entry.IsDirectory).Select(entry => entry.Path).ToArray();
                if (SaveFile || selection.Length == 0)
                {
                    var name = filename.Text.Trim();
                    if (name.Length == 0) { status.Text = UiText.Get("ChooseAFileOrEnterItsName"); return; }
                    if (SaveFile && !Path.HasExtension(name)) name += DefaultExt;
                    selection = [Path.GetFullPath(name, folder)];
                }
                var exists = await Task.Run(() => selection.All(File.Exists), token);
                if (token.IsCancellationRequested) return;
                if (!SaveFile && !exists) { status.Text = UiText.Get("TheSelectedFileIsUnavailable"); return; }
                if (SaveFile && exists && ThemedMessageBox.Show(window, UiText.Get("ReplaceTheExistingFile"), UiText.Get("SaveFile"),
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
                FileNames = selection;
                FileName = selection[0];
                window.DialogResult = true;
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error) when (error is IOException or ArgumentException or NotSupportedException)
            { status.Text = UiText.Get("TheFilePathIsInvalid"); }
            finally { confirming = false; }
        }

        filename.TextChanged += (_, _) =>
        {
            if (!SaveFile && list.SelectedItems.Count > 0) list.UnselectAll();
        };
        list.SelectionChanged += (_, _) =>
        {
            if (SaveFile && list.SelectedItem is FileEntry { IsDirectory: false } file) filename.Text = file.Label;
        };
        go.Click += async (_, _) => await NavigateAsync(path.Text);
        path.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await NavigateAsync(path.Text); } };
        up.Click += async (_, _) => await NavigateAsync(Path.GetDirectoryName(folder));
        drives.Click += async (_, _) => await NavigateAsync(null);
        filter.SelectionChanged += async (_, _) => await NavigateAsync(string.IsNullOrEmpty(path.Text) ? null : folder);
        list.MouseDoubleClick += async (_, e) =>
        {
            if ((e.OriginalSource as DependencyObject) is not { } source ||
                ItemsControl.ContainerFromElement(list, source) is not ListBoxItem { Content: FileEntry entry }) return;
            if (entry.IsDirectory) await NavigateAsync(entry.Path);
            else await AcceptAsync();
        };
        list.KeyDown += async (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            if (list.SelectedItem is FileEntry { IsDirectory: true } entry) await NavigateAsync(entry.Path);
            else await AcceptAsync();
        };
        filename.KeyDown += async (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; await AcceptAsync(); } };
        accept.Click += async (_, _) => await AcceptAsync();
        window.Loaded += async (_, _) => await NavigateAsync(folder);
        return window.ShowDialog();
    }

    private static Button MakeButton(string text)
    {
        var button = new Button { Content = text, Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(9, 4, 9, 4) };
        button.SetResourceReference(FrameworkElement.StyleProperty, "ToolbarButton");
        return button;
    }

    private static FileEntry[] ReadEntries(string? folder, FileFilter filter, CancellationToken token)
    {
        if (folder is null) return Directory.GetLogicalDrives().Select(path => new FileEntry(path, path, true)).ToArray();
        var entries = new List<FileEntry>();
        foreach (var entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
        {
            token.ThrowIfCancellationRequested();
            var directory = entry.Attributes.HasFlag(FileAttributes.Directory);
            if (directory || filter.Patterns.Any(pattern => pattern == "*.*" || FileSystemName.MatchesSimpleExpression(pattern, entry.Name)))
                entries.Add(new(entry.FullName, directory ? entry.Name + Path.DirectorySeparatorChar : entry.Name, directory));
        }
        return entries.OrderByDescending(entry => entry.IsDirectory).ThenBy(entry => entry.Label, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private sealed record FileFilter(string Name, string[] Patterns);
    private sealed record FileEntry(string Path, string Label, bool IsDirectory);
}
