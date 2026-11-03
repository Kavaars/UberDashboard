# Wersje

- 2026-11-03T08:36:48+01:00 | wersja źródła `1.4.0`, API paneli `1.2.0`, ramka zdjęć `1.1.0` | 287 testów i 37 kontroli integracyjnych wtyczki PASS; rendery WPF PASS; pakiet wtyczki `Release/Plugins/PhotoFrame-1.1.0-20261103-083648-566` | Acrylic wymaga odbioru na pulpicie

- 2026-11-03T08:30:38+01:00 | wersja źródła: `1.3.0` | 286 testów PASS; bez nowego pakietu | częściowa personalizacja tła i poziomy widok pogody; pozostałe wymagania otwarte

- 2026-11-02T21:05:54.3677930+01:00 | wersja: `1.0.8` | build: `Release/Dev/v1.0.8_20261102-210401-650-audit` | Release/test: 225 PASS, 0 ostrzeżeń/błędów; 22 rendery WPF PASS; publish win-x64 PASS | poprawki z audytu; pełne v1, runtime AppBar i KPI pozostają otwarte

- 2026-11-02T11:31:57+01:00 | wersja źródła: `1.0.7` | build/test status: bez zmian kodu — brak nowego przebiegu build/test | zainicjalizowano repo git, prywatne zdalne repo `Kavaars/UberDashboard`, tag `v1.0.7` i workflow commitów w `README.md`
- 2026-11-02T11:15:16+01:00 | wersja źródła: `1.0.7` | build/test status: `dotnet build UberDashboard.slnx -m:1` PASS, `dotnet test UberDashboard.slnx -m:1` PASS | poprawiono panel pogody (duże ikony, max/min, świt/zmierzch, prognoza godzinowa), napisy/tooltipy, glify ikon, selektor pulpitu i wizualne wyrównanie `Dodaj panel`
- 2026-11-02T10:05:00+01:00 | wersja źródła: `1.0.5` | build/test status: `dotnet build src/UberDashboard.App/UberDashboard.App.csproj` PASS, `dotnet test tests/UberDashboard.App.Tests/UberDashboard.App.Tests.csproj -m:1 --no-build` PASS, `dotnet test tests/UberDashboard.Application.Tests/UberDashboard.Application.Tests.csproj -m:1 --no-build` PASS, `dotnet test tests/UberDashboard.Infrastructure.Tests/UberDashboard.Infrastructure.Tests.csproj -m:1 --filter "FullyQualifiedName~ApplicationSettingsServiceTests|FullyQualifiedName~WindowModeSettingsServiceTests"` PASS | dopięto popupy opcji paneli, bogaty notatnik, kontrakt paska 200..1200 i poprawki themingu / tray-WinForms
- 2026-11-02T09:02:00+01:00 | wersja źródła: `1.0.4` | build/test status: `dotnet build UberDashboard.slnx` PASS, `dotnet test UberDashboard.slnx` PASS | dodano pogodę, tryby okna, opcje/personalizację i redesign shella
- 2026-11-02T07:28:10+01:00 | wersja źródła: `1.0.4` | buildy zweryfikowane: `v1.0.4_20261102-072622` (Dev), `v1.0.2_20261102-072444` (Prod), `v1.0.3_20261102-072522` (Instal) | dodano automatyzację buildów, instalator, retencję katalogów i dokumentację startową

- 2026-11-02T21:29:46.2040944+01:00 | wersja: 1.1.0 | build: Release/Dev/v1.1.0_20261102-213028-157 | Personalizacja per panel i ustawienia na żywo; 233 testy PASS, build Release 0 ostrzeżeń/błędów, 33 rendery WPF i podgląd fontu PASS, publish win-x64 PASS; ręczny odbiór myszy/Windows otwarty.

- 2026-11-02T21:56:33.4954056+01:00 | wersja: 1.2.0 | build: Release/Dev/v1.2.0_20261102-215527-467-plugins | Host pluginów API 1.1.0 i PhotoFrame 1.0.0; build Release 0 ostrzeżeń/błędów, 270 testów i 37 sprawdzeń integracyjnych PASS, 33 rendery WPF PASS, format/diff PASS, publish win-x64 self-contained PASS. Ręczny odbiór Eksploratora i dialogów Windows pozostaje otwarty.

## 2026-11-03T07:24:00+01:00 | v1.2.0 / zmiana robocza, bez wydania
- Ujednolicenie wyboru motywu z paska i Opcji. Nie przygotowano paczki wydania.

## 2026-11-03T07:41:20.1690202+01:00 | v1.3.0 | UI i elastyczny układ
- Zakończono sześć zgłoszeń motywu, ustawień i układu. Build: F:\MyApps\UberDashboard\Release\Dev\v1.3.0_20261103-074019-175
- Build Release bez ostrzeżeń; 285 testów, 51 renderów i integracja pluginów PASS. Samodzielna publikacja win-x64 PASS; bez instalatora.
- 2026-11-03T09:35:38+01:00 | źródło 1.4.0, bez nowego wydania | build rozwiązania bez błędów; testy nie zakończyły się w środowisku; przezroczyste tło pulpitu pozostaje otwarte.
- 2026-11-03T09:39:23+01:00 | źródło 1.4.0, bez nowego wydania | opcja przezroczystego tła; odbiór kompozycji Windows wymagany.

- 2026-11-03T09:54:37+01:00 | źródła 1.4.0 / synchronizacja Git | Release build: 0 błędów i ostrzeżeń; 289 testów, 38 sprawdzeń pluginów i 51 renderów WPF PASS. dotnet format --verify-no-changes: FAIL (odstępy/importy, również starsze pliki). Bez nowej paczki wydania; ręczny odbiór kompozycji Windows pozostaje otwarty.
