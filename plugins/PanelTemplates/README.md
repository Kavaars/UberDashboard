# Plugin panel templates

[Wersja polska](../../docs/pl/PanelTemplates.md)

The project contains two starting points: `simple` (empty content) and `modular` (two sample items to delete or replace). To create your own plugin, copy the folder, then change the namespace and DLL name, the unique ID in `plugin.json`, both `TypeId` values and the names. You can remove an unneeded descriptor from the module and the matching manifest entry.

```powershell
dotnet build plugins/PanelTemplates/PanelTemplates.Plugin.csproj -c Release
```

The project uses only the public `Contracts` and the transitive `Core`; it does not reference `App`. The manifest requires API 1.3.0. A `.udplugin` package contains `plugin.json` and the plugin DLL directly in the ZIP, with no parent folder and no copy of the host DLLs. The sample requires a host that supports API 1.3.0.

## What both templates share

`TemplateModule` registers descriptors in the background; a descriptor has a stable TypeId and a factory, and `TemplateInstance` keeps the ID from `PanelContext`. Custom content is supplied through `IPanelContent`; the host provides the card, tabs, outer layout and appearance. `IPanelStateNotifications` reports persistent changes and a version for autosave. State has a schema version; an unknown schema is rejected. Every instance has its own view and data. When you add I/O, timers and options, complete the lifecycle, cancellation and `IPanelOptions` as described in [`docs/PLUGINS.md`](../../docs/PLUGINS.md).

## Modular panel

`IModularPanel` gives the host a transient edit mode, item identifiers and positions; do not persist the edit mode itself. The host exposes its toggle in the panel menu. `TemplateModel` stores positions in its own JSON and reports the change through the instance. `ModuleGrid.Restore` restores old or incomplete positions, and `ModuleGrid.Move` shifts colliding items. `ModularView` and `ModularItemsPanel` are the plugin's view code: they provide the grid, dragging and Ctrl+Shift+arrow keys, and can be adapted in your own copy. Item IDs must be stable and unique within an instance. Empty cells are preserved; the number of visual columns changes with the width without changing the stored coordinates.

Before distributing, check the independence of instances and tabs, saving and loading, invalid and older state, dragging, keyboard use, themes, small sizes, DPI and screen readers. The template has two demonstration items, fetches no data and provides no domain functionality to the application. Plugins run in the host process and are not isolated.
