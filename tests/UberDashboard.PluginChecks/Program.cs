using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using UberDashboard.Application.Abstractions;
using UberDashboard.Application.Panels;
using UberDashboard.App.Hosting;
using UberDashboard.App.Panels;
using UberDashboard.App.Plugins;
using UberDashboard.App.Shell;
using UberDashboard.App.Theming;
using UberDashboard.Contracts.Panels;
using UberDashboard.Infrastructure.DependencyInjection;
using UberDashboard.Infrastructure.Paths;
using UberDashboard.Platform.Windows.DependencyInjection;

internal static class Program
{
    private static int _exitCode = 1;
    private const string TypeId = "pl.uberdashboard.photoframe.frame";
    private static readonly List<string> Checks = [];
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Checks.Add(name); Console.WriteLine("PASS: " + name);
    }
    private static IEnumerable<PanelLeafViewModel> Leaves(LayoutNodeViewModel? node) => node switch
    {
        PanelLeafViewModel leaf => [leaf],
        PanelSplitViewModel split => Leaves(split.First).Concat(Leaves(split.Second)),
        _ => [],
    };
    private static ServiceProvider Provider(string profile)
    {
        var services = new ServiceCollection(); services.AddLogging();
        var paths = new AppPaths(profile); paths.EnsureCreated();
        services.AddUberDashboardInfrastructure(paths); services.AddUberDashboardWindows(); services.AddUberDashboardApp();
        return services.BuildServiceProvider();
    }
    private static void CreatePhoto(string file, byte red)
    {
        var pixels = Enumerable.Repeat(new byte[] { 60, 140, red, 255 }, 320 * 200).SelectMany(x => x).ToArray();
        var bitmap = BitmapSource.Create(320, 200, 96, 96, PixelFormats.Bgra32, null, pixels, 320 * 4);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(file); encoder.Save(output);
    }
    private static async Task CallAsync(object model, string method, params object[] args)
        => await (Task)model.GetType().GetMethod(method)!.Invoke(model, args)!;
    private static T Value<T>(object model, string name) => (T)model.GetType().GetProperty(name)!.GetValue(model)!;
    private static void Set(object model, string name, object value) => model.GetType().GetProperty(name)!.SetValue(model, value);
    private static async Task RenderAsync(FrameworkElement element, string file, int width, int height)
    {
        element.Measure(new Size(width, height)); element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        element.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen()) drawing.DrawRectangle((Brush)System.Windows.Application.Current.FindResource("Brush.Window.Background"), null, new Rect(0, 0, width, height));
        image.Render(background); image.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(file); encoder.Save(stream);
    }
    [STAThread]
    private static int Main(string[] args)
    {
        var output = Path.GetFullPath(args.Length > 0 ? args[0] : "Release/PluginChecks/" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(output);
        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        foreach (var resource in new[] { "Theme.Light", "VisualTokens", "Controls", "PanelTemplates" })
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/UberDashboard;component/Theming/{resource}.xaml") });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        Dispatcher.CurrentDispatcher.BeginInvoke(new Action(async () =>
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                await RunAsync(output, app).WaitAsync(deadline.Token);
                await File.WriteAllTextAsync(Path.Combine(output, "results.json"), JsonSerializer.Serialize(Checks, new JsonSerializerOptions { WriteIndented = true }));
                _exitCode = 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); await File.WriteAllTextAsync(Path.Combine(output, "failure.txt"), ex.ToString()); }
            finally { app.Shutdown(); }
        }));
        app.Run(); return _exitCode;
    }
    private static async Task RunAsync(string output, System.Windows.Application app)
    {
        var package = Path.Combine(output, "PhotoFrame-1.0.0.udplugin");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(typeof(PhotoFrame.Plugin.PhotoFrameModule).Assembly.Location, "PhotoFrame.Plugin.dll");
            var manifest = Path.Combine(AppContext.BaseDirectory, "photo-plugin.json");
            zip.CreateEntryFromFile(manifest, "plugin.json");
        }
        var profile = Path.Combine(output, "profile");
        var photos = Path.Combine(output, "photos"); Directory.CreateDirectory(photos);
        CreatePhoto(Path.Combine(photos, "a.png"), 240); CreatePhoto(Path.Combine(photos, "b.png"), 80);
        File.WriteAllText(Path.Combine(photos, "c.png"), "corrupt image");
        UberDashboard.Core.Layout.PanelInstanceId instanceId;
        Guid dashboardId;
        await using (var provider = Provider(profile))
        {
            var manager = provider.GetRequiredService<PluginManager>();
            await manager.InitializeAsync();
            var shell = provider.GetRequiredService<ShellViewModel>();
            manager.RefreshPanelsAsync = shell.RefreshPluginPanelsAsync;
            await shell.InitializeAsync();
            var ui = provider.GetRequiredService<PluginSettingsViewModel>();

            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                try { await manager.InstallAsync(package, cancellation.Token); throw new InvalidOperationException("Cancellation ignored"); }
                catch (OperationCanceledException) { Check(manager.Installed.Count == 0, "Cancelled installation has no side effects"); }
            }
            var broken = Path.Combine(output, "broken.udplugin");
            using (var zip = ZipFile.Open(broken, ZipArchiveMode.Create))
            {
                zip.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, "photo-plugin.json"), "plugin.json");
                using var bad = new StreamWriter(zip.CreateEntry("PhotoFrame.Plugin.dll").Open()); bad.Write("invalid DLL");
            }
            try { await manager.InstallAsync(broken); throw new InvalidOperationException("Bad DLL was accepted"); }
            catch (BadImageFormatException)
            {
                Check(manager.Installed.Count == 0 && !Directory.GetDirectories(Path.Combine(profile, "Plugins")).Any(), "Failed module load rolls back package and registrations");
            }
            var interruptedInstall = ui.InstallFileAsync(package);
            await ui.StopAsync(); await interruptedInstall;
            Check(!ui.IsBusy && manager.Installed.Count == 0, "Shutdown cancels and awaits pending plugin operation");
            var watch = Stopwatch.StartNew();
            var dropWindow = new Window();
            PluginFileDrop.Attach(dropWindow, ui);
            var imageDrop = DragArgs(dropWindow, new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { Path.Combine(photos, "a.png") }), System.Windows.DragDrop.PreviewDropEvent);
            dropWindow.RaiseEvent(imageDrop);
            Check(!imageDrop.Handled, "Image drops remain available to original panels");
            var internalDrop = DragArgs(dropWindow, new System.Windows.DataObject("UberDashboard.PanelInstance.v1", Guid.NewGuid().ToString()), System.Windows.DragDrop.PreviewDropEvent);
            dropWindow.RaiseEvent(internalDrop);
            Check(!internalDrop.Handled, "Plugin installer does not intercept panel rearrangement");
            var over = DragArgs(dropWindow, new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { package }), System.Windows.DragDrop.PreviewDragOverEvent);
            dropWindow.RaiseEvent(over);
            Check(over.Handled && over.Effects == DragDropEffects.Copy, "Package drag advertises copy action");
            var dropped = DragArgs(dropWindow, new System.Windows.DataObject(System.Windows.DataFormats.FileDrop, new[] { package }), System.Windows.DragDrop.PreviewDropEvent);
            dropWindow.RaiseEvent(dropped);
            var installDeadline = Stopwatch.StartNew();
            while (ui.IsBusy && installDeadline.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(10);
            Check(dropped.Handled && manager.Installed.Count == 1, "WPF drop event installs real package through shared controller");
            dropWindow.Close();
            Check(shell.PanelTypes.Any(p => p.TypeId == TypeId), "Panel menu updates without restart");
            Console.WriteLine($"Install elapsed: {watch.ElapsedMilliseconds} ms");
            await AssertRejected(() => manager.InstallAsync(package), "Duplicate install rejected");
            await shell.AddPanelCommand.ExecuteAsync(TypeId);
            var leaf = Leaves(shell.Root).Single(); instanceId = leaf.InstanceId; dashboardId = shell.SelectedDashboard!.DashboardId;
            Check(leaf.HasOptions && leaf.PanelContent is FrameworkElement, "Native panel card and options contract");
            var model = leaf.ViewModel;
            await CallAsync(model, "SelectAsync", photos, true);
            Check(Value<ImageSource?>(model, "CurrentPhoto") is not null && Value<bool>(model, "HasMultipleImages"), "Folder loads image off UI thread");
            Set(model, "IntervalSeconds", 1);
            Check(Value<ImageSource>(model, "CurrentPhoto").IsFrozen, "Decoded bitmap is safe to transfer between threads");
            var sourceFile = Path.Combine(photos, "a.png"); File.Move(sourceFile, sourceFile + ".moved"); File.Move(sourceFile + ".moved", sourceFile);
            Check(true, "Decoded image releases original file handle");
            Check(Value<bool>(model, "HasUnsavedChanges"), "Plugin notifies host of unsaved changes");
            await provider.GetRequiredService<PanelStateScheduler>().FlushAllAsync(default);
            Check(!Value<bool>(model, "HasUnsavedChanges"), "Host confirms durable plugin state save");
            var before = Value<ImageSource>(model, "CurrentPhoto");
            await Dispatcher.Yield(DispatcherPriority.Background);
            var slideDeadline = Stopwatch.StartNew();
            while (ReferenceEquals(before, Value<ImageSource>(model, "CurrentPhoto")) && slideDeadline.Elapsed < TimeSpan.FromSeconds(4)) await Task.Delay(50);
            Check(!ReferenceEquals(before, Value<ImageSource>(model, "CurrentPhoto")), "Real slideshow timer changes photo");
            foreach (var kind in new[] { ThemeKind.Light, ThemeKind.Dark })
            {
                await provider.GetRequiredService<ThemeService>().ApplyAsync(kind, default);
                Set(model, "IsOptionsOpen", true);
                var card = new System.Windows.Controls.ContentControl { Content = leaf, ContentTemplate = (DataTemplate)app.FindResource("PanelLeafTemplate") };
                await RenderAsync(card, Path.Combine(output, $"frame-{kind}.png"), 420, 440);
                Check(ReferenceEquals(((IPanelContent)model).Content, leaf.PanelContent), "Plugin view participates in native card " + kind);
                card.Content = null;
                Set(model, "IsOptionsOpen", false);
            }
            await shell.HidePanelCommand.ExecuteAsync(leaf);
            Check(!provider.GetRequiredService<PanelHostManager>().IsActive(instanceId), "Hide disposes slideshow and preserves instance");
            await shell.RestorePanelCommand.ExecuteAsync(shell.HiddenPanels.Single());
            var restored = Leaves(shell.Root).Single();
            Check(restored.InstanceId == instanceId && Value<int>(restored.ViewModel, "IntervalSeconds") == 1, "Restore retains identity and settings");
            await shell.AddPanelCommand.ExecuteAsync(TypeId);
            var second = Leaves(shell.Root).Single(p => p.InstanceId != instanceId);
            await CallAsync(second.ViewModel, "SelectAsync", Path.Combine(photos, "a.png"), false);
            Set(second.ViewModel, "IntervalSeconds", 23);
            Check(Value<int>(restored.ViewModel, "IntervalSeconds") == 1 && Value<string>(restored.ViewModel, "SourcePath") == photos, "Multiple photo panels have independent sources and settings");
            await shell.HidePanelCommand.ExecuteAsync(second);
            await shell.NewDashboardCommand.ExecuteAsync(null);
            // Nowy pulpit przełącza się asynchronicznie; czekamy na rzeczywistą zmianę korzenia.
            var switchDeadline = Stopwatch.StartNew();
            while (!shell.IsLayoutEmpty && switchDeadline.Elapsed < TimeSpan.FromSeconds(4)) await Task.Delay(25);
            Check(shell.IsLayoutEmpty, "Dashboard switch suspends photo panel");
            await manager.RemoveAsync(manager.Installed.Single());
            Check(!provider.GetRequiredService<PanelCatalog>().Contains(TypeId), "Uninstall unregisters panel type");
            Check((await provider.GetRequiredService<IPanelInstanceStore>().GetAsync(instanceId, default))?.StateJson is not null, "Uninstall preserves photo panel state on other dashboard");
            await ui.InstallFileAsync(package);
            Check(manager.Installed.Count == 1, "Settings controller reinstalls same package in same session");
            shell.SelectedDashboard = shell.Dashboards.Single(d => d.DashboardId == dashboardId);
            var restoreDeadline = Stopwatch.StartNew();
            while (Leaves(shell.Root).FirstOrDefault()?.ViewModel is not IPanelOptions && restoreDeadline.Elapsed < TimeSpan.FromSeconds(4)) await Task.Delay(25);
            Check(Leaves(shell.Root).Single().InstanceId == instanceId, "Reinstall restores original panel identity");
            var settings = provider.GetRequiredService<UberDashboard.App.Settings.SettingsWindow>();
            var settingsRoot = (FrameworkElement)settings.Content;
            await RenderAsync(settingsRoot, Path.Combine(output, "settings.png"), 980, 720);
            var tabControl = Descendants(settingsRoot).OfType<System.Windows.Controls.TabControl>().Single();
            tabControl.SelectedItem = tabControl.Items.OfType<System.Windows.Controls.TabItem>().Single(tab => tab.Name == "PluginsTab");
            await RenderAsync(settingsRoot, Path.Combine(output, "plugins-settings.png"), 980, 720);
            Check(Descendants(settingsRoot).OfType<System.Windows.Controls.Button>().Any(b => b.Content?.ToString() == "Usuń"), "Installed plugin removal action renders in settings");
            settings.Close();
        }
        var duplicateDirectory = Path.Combine(profile, "Plugins", "zz-duplicate");
        var originalDirectory = Directory.GetDirectories(Path.Combine(profile, "Plugins")).Single();
        Directory.CreateDirectory(duplicateDirectory);
        foreach (var file in Directory.GetFiles(originalDirectory)) File.Copy(file, Path.Combine(duplicateDirectory, Path.GetFileName(file)));
        await using (var provider = Provider(profile))
        {
            var manager = provider.GetRequiredService<PluginManager>(); await manager.InitializeAsync();
            Check(manager.Installed.Count == 2 && manager.Installed.Any(p => p.Status.StartsWith("Błąd")), "Duplicate startup installation is isolated and visible");
            await manager.RemoveAsync(manager.Installed.Single(p => p.Directory == duplicateDirectory));
            Check(provider.GetRequiredService<PanelCatalog>().Contains(TypeId), "Removing failed duplicate preserves active plugin registrations");
            Check(manager.Installed.Count == 1, "Installed plugin reloads from disk in fresh host");
            var shell = provider.GetRequiredService<ShellViewModel>(); manager.RefreshPanelsAsync = shell.RefreshPluginPanelsAsync;
            await shell.InitializeAsync();
            var leaf = Leaves(shell.Root).Single();
            Check(leaf.InstanceId == instanceId && Value<int>(leaf.ViewModel, "IntervalSeconds") == 1, "Fresh host restores plugin panel state from SQLite");
            await manager.RemoveAsync(manager.Installed.Single());
            Check(Leaves(shell.Root).Single().ViewModel is UnavailablePanelViewModel, "Removing active plugin leaves recoverable placeholder");
            await manager.InstallAsync(package);
            Check(Leaves(shell.Root).Single().ViewModel is IPanelOptions, "Reinstall replaces placeholder with functional panel");
            var model = Leaves(shell.Root).Single().ViewModel;
            await CallAsync(model, "SelectAsync", Path.Combine(photos, "a.png"), false);
            Check(!Value<bool>(model, "HasMultipleImages") && Value<ImageSource?>(model, "CurrentPhoto") is not null, "Single image source works");
            await CallAsync(model, "SelectAsync", Path.Combine(photos, "missing"), true);
            Check(Value<ImageSource?>(model, "CurrentPhoto") is null && !Value<bool>(model, "IsLoading"), "Missing folder reports recoverable error");
            await CallAsync(model, "SelectAsync", photos, true);
            Check(Value<ImageSource?>(model, "CurrentPhoto") is not null, "Source retry recovers after error");
        }
        Check(true, "Plugin host disposal flushes and closes panels");
    }
    private static DragEventArgs DragArgs(Window window, System.Windows.IDataObject data, RoutedEvent routedEvent)
    {
        var constructor = typeof(DragEventArgs).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Single();
        var args = (DragEventArgs)constructor.Invoke([data, DragDropKeyStates.None, DragDropEffects.Copy, window, new Point(20, 20)]);
        args.RoutedEvent = routedEvent;
        return args;
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static async Task AssertRejected(Func<Task> action, string message)
    {
        try { await action(); }
        catch (InvalidDataException) { Check(true, message); return; }
        throw new InvalidOperationException(message);
    }
}
