# UberDashboard

A personal desktop dashboard for Windows 11: notes, to-do lists, calendar, weather, shortcuts, a system monitor and more — side by side in one customizable window. Run it as a normal window or dock it as a bar to a screen edge. Your layout and content are stored locally.

![UberDashboard – dark theme with wallpaper](docs/screenshots/main-current-dark.png)

## What you can do with it

- **Build your own layout** — add panels, resize, move, hide and restore them. Multiple dashboards and per-panel tabs are supported.
- **Notes** — rich-text notepad with TXT and Markdown import.
- **To-do lists** — local lists with TXT/Markdown/CSV import and optional sync with Microsoft To Do.
- **Calendar** — Google Calendar views: month, week, day, custom range and agenda.
- **Weather** — forecast for a chosen city from Open-Meteo, with several icon sets.
- **Shortcuts** — launch apps, folders, documents and web pages from icons, lists, buttons or tiles.
- **Home Assistant** — show sensor values and trigger actions on your local Home Assistant.
- **Resource monitor** — CPU, GPU, RAM and VRAM usage; temperatures and fans when sensors are available.
- **Affirmations** — practice your own texts and track progress.
- **Plugins** — extra panels shipped as `.udplugin` packages (for example a photo frame).
- **Personalization** — light, dark or system theme, accent and surface colors, 10 ready-made color themes, fonts, wallpapers, corner radius, spacing and per-panel appearance. The UI is available in English and Polish.

In a panel's options, use **When tabs overflow** to fit all tabs on one row, scroll with arrows, or wrap to more rows. Each panel saves its own choice. On short panels, scroll vertically within the tab strip to reach additional rows.

## Screenshots

| Light theme | Blue dark theme |
|---|---|
| ![Light theme](docs/screenshots/main-light.png) | ![Blue dark theme](docs/screenshots/main-ocean-dark.png) |

| Warm light theme |
|---|
| ![Warm light theme](docs/screenshots/main-warm-light.png) |

All screenshots are generated from mock data: `dotnet run --project tests/UberDashboard.Screenshots -- docs/screenshots`.

## Install

Download the latest installer from the [Releases](https://github.com/Kavaars/UberDashboard/releases) page and run it. Windows 11 (x64) is the supported platform.

## Build from source

Requires Windows and the .NET SDK pinned in [`global.json`](global.json).

```bash
dotnet build UberDashboard.slnx
dotnet test UberDashboard.slnx
```

`budujdev.bat`, `budujfast.bat`, `budujprod.bat` and `budujinst.bat` wrap `scripts/BuildTools.ps1` (development, fast, production and installer builds). The installer build needs [Inno Setup 6](https://jrsoftware.org/isinfo.php).

## Integrations and privacy

Your data stays on your computer. The app contacts the network only for features you enable (weather, Microsoft To Do, Google Calendar, Home Assistant). Sign-in tokens are stored encrypted with Windows DPAPI. Google Calendar requires your own OAuth client file. See the [Privacy Policy](PRIVACY.md).

## Plugins

Panel plugins are described in [`docs/PLUGINS.md`](docs/PLUGINS.md), with a sample project in [`plugins/PanelTemplates`](plugins/PanelTemplates).

## Database backups

Open **Settings → Application → Create database backup** to save a verified copy of the local SQLite database in the profile's `Backups` folder. Keep a separate copy if you need it beyond the ten retained by the app. Microsoft and Google sessions, plugin files and referenced files are stored separately.

To restore a database copy, close UberDashboard, move the existing `Data/dashboard.db` and any matching `-wal` and `-shm` files to a safe folder, then copy the chosen backup to `Data/dashboard.db`. Reopen offline with the same or a compatible newer version. Review pending cloud sync changes before reconnecting.

## Documentation

- [Plugins](docs/PLUGINS.md)
- [Microsoft To Do](docs/MICROSOFT_TODO.md)
- [Shortcuts panel](docs/PANEL_SHORTCUTS.md)
- [Calendar and resource monitor](docs/PANELS_CALENDAR_AND_RESOURCES.md)

Polish translations of the documentation are in [`docs/pl`](docs/pl).

## Unsigned installer

The installer is currently **not code-signed**. Windows SmartScreen will likely show an "unknown publisher" / "Windows protected your PC" warning when you run it; choose **More info → Run anyway** if you trust the download. You can compare the file with the one attached to the [release](https://github.com/Kavaars/UberDashboard/releases) or build it yourself from source.

## Built with AI assistance

This application was developed with the help of AI coding assistants (Claude by Anthropic). The code is reviewed and tested by the maintainer, but as with any software, use it at your own risk (see the [License](#license)).

## Feedback

Found a bug or have an idea? [Open an issue](https://github.com/Kavaars/UberDashboard/issues).

## License

UberDashboard is released under the [MIT License](LICENSE). Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
