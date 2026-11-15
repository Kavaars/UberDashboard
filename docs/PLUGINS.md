# UberDashboard panel plugins

[Wersja polska](pl/PLUGINY.md)

Applies to the current development version **1.9.0**, panel contract **1.3.0** and the **Photo Frame 1.1.0** plugin.

## Panel tabs since 1.8.0

The host provides tabs for every panel type, including existing compatible plugins; a plugin does not need to implement a tab bar. Each tab has its own instance identifier, state and file folder. Appearance and position are shared by the whole group. Plugin-specific settings apply to the active tab.

The "+" button adds a tab of the same type. Double-click or F2 renames a tab; drag it or press Ctrl+Shift+arrow to reorder. The right-click menu removes the selected tab after confirmation; the last tab is protected. When you switch tabs, the host saves and suspends the previous instance. When a tab is removed, the host calls the existing lifecycle contract on the instances being removed.

The state of an old panel stays in the first tab. Existing notepad tabs are migrated transactionally, and a copy of the original state stays in the database for recovery. A missing plugin does not delete the group or its data; reinstalling a compatible plugin restores the panel. Back up your profile before updating; going back to an older version requires restoring a compatible database copy.

## Installing and removing

1. Run UberDashboard 1.2.0 or newer.
2. Drag **one `.udplugin` file** from Explorer onto the main window or the settings window. Alternatively open **Options and Settings → Plugins → Install from file…**.
3. Wait for confirmation. The real state of the operation is shown while it runs; **Cancel operation** aborts it before it is committed. Do not close the program while installing.
4. Choose the new panel type from the **Add panel** menu. Installation does not add an instance to every dashboard automatically.
5. The **Plugins** tab lists names, versions, identifiers and installation state. **Remove** disables the code and deletes its package without deleting panel data.

No restart is needed. Plugin panels use the same layout, dividers, moving, hiding, per-panel appearance and autosave as built-in panels. You can create many independently configured instances.

After a plugin is removed, its active panels show an "unavailable" notice. Layout, identifiers and state in SQLite, and files in PluginData, are kept. Reinstalling a compatible plugin restores the panels. Installing a second version of the same plugin is blocked: remove the previous one first. The new version must be able to read the previous state. Removing a single panel is a separate application operation.

Failed installations remain visible on the list after a restart and can be removed. When an operation fails, the message describes the validation problem or points to the logs in `%LOCALAPPDATA%\UberDashboard\LOGS`. Choose the file again after fixing the cause. Normal image drops and panel dragging are not intercepted by the plugin installer. Dropping several files at once does not install them.

## Photo Frame

A ready-made package is generated at `Release/Plugins/PhotoFrame-1.0.0-<timestamp>/PhotoFrame-1.0.0.udplugin`.

After installing, add **Photo Frame**. In its options (the gear in the header):

- **Choose image…** shows a single local file.
- **Choose folder…** starts a slideshow of the images directly in the folder (no subfolders), in name order.
- **Change image every** sets the interval in seconds, from 1 to 86400. Confirm the value with Tab or by leaving the field. The field is active when the folder contains more than one supported file.
- **Refresh source** re-reads the folder, for example after adding or deleting photos, or after fixing a damaged file.

Supported formats: JPEG, PNG, BMP, GIF and TIFF. GIF and TIFF show their first frame. SVG and WebP are not part of the plugin's first version. The image keeps its aspect ratio and fits the panel. Files stay where they are; the plugin does not copy or modify them. The path and the slideshow interval are stored separately for each instance.

Folder scanning and image decoding run off the UI thread. Only the current scaled image is kept in memory, not the whole collection. The longer side is decoded to at most 1920 px. Safety limits: 10,000 images per folder, 50 MiB and 100 megapixels per image. Damaged or unavailable images are skipped. When there are no valid images, the panel shows a message. Changing the source cancels the previous load. Hiding, switching dashboards and removing the panel stop the timer and release the image. New files in the folder are picked up after you use Refresh; the plugin does not watch the disk continuously.

## Trust and execution limits

A plugin is native .NET code running inside the application process with the current user's permissions. **Install only packages from a source you trust.** The publisher name is a declaration, not a signature. The current format neither verifies signatures nor isolates code in a separate process. `capabilities` describe behavior; they do not enforce permissions.

A separate AssemblyLoadContext separates private managed dependencies, but it does not protect against faulty or malicious code. Cancellation is cooperative: the author must honor the tokens. The host cannot safely abort arbitrary plugin code. Errors in the constructor, module initialization and panel lifecycle are handled by the host; the author is still responsible for their own UI events and background tasks. There is no guarantee about the runtime of any third-party plugin.

## The `.udplugin` package format

A ZIP with `plugin.json` directly in the root, plus the entry DLL and optional private managed dependencies. Do not package a parent folder, the host application, Core/Contracts, the .NET runtime, user data or secrets.

```text
PhotoFrame-1.0.0.udplugin
  plugin.json
  PhotoFrame.Plugin.dll
```

A complete example manifest:

```json
{
  "id": "pl.uberdashboard.photoframe",
  "name": "Photo Frame",
  "publisher": "UberDashboard",
  "version": "1.0.0",
  "apiVersion": "1.1.0",
  "minHostVersion": "1.2.0",
  "targetFramework": "net10.0-windows",
  "entryAssembly": "PhotoFrame.Plugin.dll",
  "entryType": "PhotoFrame.Plugin.PhotoFrameModule",
  "dataSchemaVersion": 1,
  "dependencies": [],
  "capabilities": ["Read a local image or folder chosen by the user"],
  "panels": [
    { "typeId": "pl.uberdashboard.photoframe.frame", "name": "Photo Frame" }
  ]
}
```

`id` is a stable, lowercase reverse-DNS identifier. Do not use `pl.uberdashboard.builtin`. Write `version`, `apiVersion` and `minHostVersion` as `major.minor.patch`. The API major version must match the host, and the minor version must not be higher than the host's. A plugin built for 1.0 can run with contract 1.1. The framework must currently be `net10.0-windows`. `entryAssembly` is a single DLL name; `entryType` is the full name of a public type with a parameterless constructor. All panels must be declared and must match exactly what the module registers. `TypeId` must be unique across the whole application; use the plugin id as a prefix.

Put private managed libraries next to the entry DLL. Core and Contracts are shared with the host. Do not reference the App, Infrastructure or Application implementations. `dependencies` is metadata, not a download instruction: the host downloads nothing. API 1.1 does not support private native libraries or searching runtime folders for them. A plugin must be a library that runs on the host's desktop framework, with no separate EXE or runtime.

Installer limits: ZIP up to 64 MiB, extracted content up to 128 MiB, at most 512 entries, manifest up to 64 KiB and at most 32 panel types. Absolute paths, `..`, NTFS alternate data streams, links, Windows device names, `.enabled` marker files, duplicate names and TypeId conflicts are blocked. Extraction and module startup run in the background. Only a complete, valid registration publishes the package; a failure removes the staging area. Active packages live in `%LOCALAPPDATA%\UberDashboard\Plugins`; do not edit them while the application is running.

## Building your own plugin

A full working example is in `plugins/PhotoFrame`. Copy the project to a new folder and change the assembly name, namespace, manifest, plugin id and TypeId. The project targets `net10.0-windows`, enables `UseWPF` and references only `UberDashboard.Contracts.csproj` (Core is a transitive dependency). Windows and the .NET 10 SDK are required.

```powershell
dotnet build plugins/PhotoFrame/PhotoFrame.Plugin.csproj -c Release
pwsh -NoProfile -File scripts/BuildPhotoFramePlugin.ps1
```

The second command builds the sample and creates a ready `.udplugin` in a unique folder. It checks the build exit code; it does not package after a failure and does not overwrite an existing output folder. For your own plugin, adjust paths and names in a copy of this script and add its private dependencies to the ZIP.

### Contracts and threading

- `IPluginModule.InitializeAsync(IPluginHostContext, CancellationToken)` runs in the background. Register only `IPanelDescriptor` metadata here, through `RegisterPanel`. Do not create WPF objects or change Application resources. Registration after InitializeAsync completes is blocked. The module should not start its own unbounded lifecycle: tasks and resources belong to the panel instance.
- `IPanelDescriptor` exposes TypeId, DisplayName, an optional IconKey, MinSize in DIPs, StateSchemaVersion and Factory. The descriptor is immutable and holds no per-instance data.
- `IPanelFactory.CreateAsync(PanelContext, token)` and the instance's Initialize/Restore/Capture/Suspend/Resume/PrepareForRemoval/Dispose are called by the WPF host on the UI thread. Return quickly; move file/network operations and decoding to async I/O or Task.Run. After an await, return to the Dispatcher before changing WPF objects and view-bound properties.
- `IPanelInstance.InstanceId` must be exactly the one passed by PanelContext. Do not generate a new identity when restoring. `ViewModel` is the model bound to the card.
- `IPanelContent.Content` on the ViewModel lets you supply your own WPF view. The host embeds it in a regular card. Each instance must have its own view. In the sample it is a UserControl with its DataContext set to the model. Do not attach the same WPF element to several parents.
- `IPanelOptions` on the ViewModel exposes HasOptions, IsOptionsOpen and an ICommand ToggleOptionsCommand. Together with INotifyPropertyChanged it gives the standard gear icon and open state in the header. The plugin's view handles the visibility of the options themselves.
- `IPanelStateNotifications` on the instance exposes StateChanged, ChangeVersion and OnStatePersisted. Raise StateChanged and increment ChangeVersion on the UI thread when persistent settings change. The host debounces for 600 ms and saves the state to SQLite. OnStatePersisted fires only after success, if the version did not change during the save. Merely advancing a slide should not save the configuration.
- `CaptureStateAsync` returns UTF-8 bytes containing valid JSON. `RestoreStateAsync` restores the state and should normalize older values or migrate your own format. Never replace an unknown schema with empty data. The photo frame plugin rejects an unsupported schema and leaves the original record for recovery.
- `PanelContext.CloseToken` is cancelled on close, hide and removal. The host currently saves and removes the active instance when hiding or switching dashboards, and recreates it on restore. Implement both a new instance and Restore, as well as Suspend/Resume, correctly.
- `DisposeAsync` must stop timers, cancel I/O, detach events and release resources. Do not block the Dispatcher or use `.Wait()`/`.Result` on tasks that need the UI.
- `PanelContext.Logger`, `Notifier` and `Storage` provide logs, global toasts and a dedicated per-instance store. Do not reach into the host database directly. Do not log secrets or private photo paths. Do not call the synchronous Storage.ListFiles on the UI thread for large folders.

The view should use the host's dynamic resources: `Brush.Text`, `Brush.Text.Muted`, `Brush.Surface`, `Brush.Border`, `Brush.Accent` and the existing `ToolbarButton` and `ThemedTextBox` styles. Do not override global styles. Preserve keyboard access, AutomationProperties labels, contrast and scrolling at the panel's minimum size. Bitmaps prepared in the background must be frozen (`Freeze`) before being handed to the UI.

### Adapting to the panel size

A plugin may implement the optional `UberDashboard.Contracts.Panels.IResponsivePanel` on its own view (`IPanelContent.Content`) or view model. The host calls `OnPanelSizeChanged(double width, double height)` on the UI thread after the available content size changes; dimensions are in DIPs. The plugin should re-lay-out quickly, keep its state and allow scrolling at small sizes. Older plugins work without implementing this interface; the host cannot automatically rebuild their internal controls. An example is `plugins/PhotoFrame/PhotoFrameView.cs`.

### Panel kinds and templates

[`plugins/PanelTemplates`](../plugins/PanelTemplates/README.md) contains compilable templates for a regular and a modular panel. Its documentation describes the duties common to both kinds, the separate module contract, state saving, package preparation and an acceptance checklist. A regular panel provides its own content; a modular panel also exposes its item layout and edit mode through `IModularPanel`. Both use the same card, tabs and host lifecycle. The current modular API requires contract **1.3.0**.

## Verification

```powershell
dotnet build UberDashboard.slnx -c Release
dotnet test UberDashboard.slnx -c Release --no-build -v q
dotnet run --project tests/UberDashboard.PluginChecks -c Release --no-build -- Release/PluginChecks/my-unique-run
```

`testuj.bat` also runs PluginChecks. This scenario uses a separate profile and a real DLL package: installation through a synthetic WPF Drop event and the settings controller, cancellation, rollback of a faulty DLL, duplicates, a panel in a native card, the timer, both themes, SQLite persistence, hiding, switching dashboards, removal and reinstallation, a new host container, a single image and an unavailable folder. It writes `results.json` and images to the given directory.

This is automated WPF and SQLite integration. Manual drag-and-drop from Explorer, system file dialogs, touch, DPI and screen readers remain a separate check on the target desktop. The time of a single run of a local package is not a guarantee of any plugin's speed.
