# Shortcuts panel (1.9.0)

[Wersja polska](pl/PANEL_SKROTY.md)

Add the **Shortcuts** panel from the panel catalog. The **Add shortcut** button opens an editor for the name, target, icon and colors. The target can be a full path to an application, folder, document or Windows shortcut (.lnk/.url), or an http:// or https:// address. File and folder pickers are available, and environment variables such as %SystemRoot% are expanded.

Views: **Icons**, **List**, **Buttons**, **Tiles**. Enter opens the selected target. In the panel menu you can choose single or double click; older settings keep their previous way of opening. You can also use the **Open** button. Windows chooses the application associated with the document or the default browser.

The **⋯** menu offers editing, deleting, moving items, and choosing or clearing this panel's wallpaper. Deleting a shortcut does not delete the target file or folder. Editing works on a copy: cancelling leaves the saved shortcut unchanged.

By default the panel reads the icon of a local EXE/LNK/ICO file. You can replace it with a symbol, an emoji or your own PNG/JPG/BMP/ICO image. Background and name colors are independent for each item; **Theme colors** restores the look defined by the theme. Tiles act as small shortcut cards.

Shortcuts, order, view, colors and the wallpaper path are saved in the existing panel state store. Image reading, icon fetching and launching targets happen off the UI thread. Invalid targets can be fixed in the editor and opened again; the panel shows an error and a global toast.

Images are referenced by a local path and are not copied into the profile. After a file is moved or deleted you must point to it again. UNC images are not loaded; shortcuts to UNC folders and files can be opened by Windows. Command-line arguments and arbitrary URI schemes are not supported. An application that needs arguments can be pointed to through a prepared .lnk shortcut. The panel does not reproduce free placement of icons by coordinates or nested panels.

Verification: the `ShortcutTests` tests cover target validation, editing, order, save/load, corrupted data and open errors. `ShortcutsChecks` exercises the editor without user input and renders the four views at three sizes in both themes; the whole VisualChecks suite also runs with the `--english` option.

The icon and image gallery in the editor contains 59 Lucide 0.468.0 icons, available offline; search uses English names. The choice is stored in the shortcut as an icon identifier and does not depend on a local path. The Lucide library is distributed under the ISC license; the full text and author information are in `src/UberDashboard.App/Features/Shortcuts/Assets/Lucide/LICENSE.txt`. Custom images are still referenced by path.
