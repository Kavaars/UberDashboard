# Rozwój UberDashboard

Dokumentacja techniczna jest oddzielona od [instrukcji dla użytkowników](../README.md). Zasady pracy: [AGENTS.md](../AGENTS.md), [zasady projektu](../ZASADY_PROJEKTU.md) i [wytyczne paneli](WYTYCZNE_PANELI.md). Publiczne kontrakty i pakiety wtyczek opisuje [dokumentacja pluginów](PLUGINY.md).

## Środowisko i architektura

Windows 11, .NET SDK 10, PowerShell 7; do instalatora opcjonalnie Inno Setup 6. Rozwiązanie WPF ma warstwy Core (domena), Application (przypadki użycia), Infrastructure (SQLite, pliki i usługi), Platform.Windows (integracja Windows), App (host i UI) oraz Contracts (publiczne API paneli).

```powershell
dotnet restore UberDashboard.slnx
dotnet build UberDashboard.slnx -c Release
dotnet test UberDashboard.slnx -c Release --no-build -v q
```

Każdy krok wymaga sukcesu poprzedniego. `testuj.bat` uruchamia kontrole rozwiązania oraz integrację pluginów. Rendery WPF wykonuje `tests/UberDashboard.VisualChecks`; nie zastępują prób prawdziwego OAuth, DPI, Narratora i powłoki Windows.

## Skrypty

| Skrypt | Działanie |
| --- | --- |
| `budujdev.bat` | Weryfikacja, publikacja Debug do Release/Dev i uruchomienie. |
| `budujprod.bat` | Weryfikacja, publikacja Release win-x64 self-contained do Release/Prod i uruchomienie. |
| `budujfast.bat` | Nieweryfikowana publikacja bez testów do Release/Dev. |
| `budujinst.bat` | Weryfikacja i instalator Inno Setup w Release/Instal; brak ISCC kończy pracę błędem. |
| `usun.bat -DryRun` | Podgląd czyszczenia wygenerowanych starszych kompilacji. |
| `folder.bat` | Otwarcie katalogu Release. |

Przed wykonaniem skryptu sprawdź scripts/BuildTools.ps1. Dane użytkownika pozostają w profilu `%LOCALAPPDATA%\UberDashboard`, poza katalogami kompilacji.

## Wersje i Git

Źródłem wersji jest Directory.Build.props. Build nie podbija wersji; tworzy osobny katalog `v<wersja>_<YYYYMMDD-HHMMSS-fff>` (szybki build z sufiksem `-fast`). Kompatybilna funkcja zmienia minor, poprawka patch, zmiana łamiąca major. Tag `v<MAJOR>.<MINOR>.<PATCH>` należy tworzyć dopiero dla sprawdzonego stanu wydania.

Format commita: `typ: krotki opis po polsku bez ogonkow` (Conventional Commits). Typy: feat, fix, chore, docs, test, refactor. Projekt pracuje na main, zdalne repozytorium: https://github.com/Kavaars/UberDashboard. Commit obejmuje przejrzane zmiany oraz właściwe wpisy w listaZmian.md, wersje.md i changelog.txt; nie nadpisuj zdalnej historii.
