# UberDashboard

Lokalna aplikacja desktopowa dla Windows 11 do budowy własnych pulpitów z paneli. Projekt jest rozwijany w .NET 10, WPF i architekturze modularnego monolitu z wyraźnym podziałem na warstwy domeny, aplikacji, infrastruktury, integracji Windows i hosta UI.

## Zakres v1

- wiele nazwanych pulpitów
- układ paneli oparty o drzewo podziałów
- panele wbudowane: Notatnik i ToDo
- host paneli i przygotowanie pod wtyczki
- lokalny zapis do SQLite poza wątkiem UI
- motywy jasny/ciemny
- globalne toasty
- skrypty build/test/installer dla Windows 11

## Architektura

```text
src/
  UberDashboard.App                 WPF, DI, okno główne, host aplikacji
  UberDashboard.Application         use-case'y i abstrakcje
  UberDashboard.Contracts           kontrakty hosta i paneli
  UberDashboard.Core                logika domenowa i silnik layoutu
  UberDashboard.Infrastructure      SQLite, logowanie, ścieżki, kolejka DB
  UberDashboard.Platform.Windows    AppBar, monitory, DPI, interop

tests/
  UberDashboard.Core.Tests
  UberDashboard.Application.Tests
  UberDashboard.Infrastructure.Tests
```

## Najważniejsze decyzje techniczne

- `Directory.Build.props` jest źródłem wersji aplikacji.
- dane użytkownika są trzymane poza katalogiem programu, domyślnie pod `%LOCALAPPDATA%\UberDashboard`
- logi aplikacji trafiają do katalogu `LOGS` w profilu użytkownika
- buildy trafiają do `Release/Dev`, `Release/Prod`, `Release/Instal`
- każdy build wykonywany przez skrypty `buduj*.bat` automatycznie podbija patch version i tworzy nowy katalog wydania

## Wymagania

- Windows 11
- .NET SDK 10
- PowerShell 7 (`pwsh`)
- opcjonalnie Inno Setup 6 do budowy instalatora

## Szybki start

1. Przywróć zależności:

```powershell
dotnet restore UberDashboard.slnx
```

2. Zbuduj rozwiązanie:

```powershell
dotnet build UberDashboard.slnx
```

3. Uruchom testy:

```powershell
dotnet test UberDashboard.slnx -v q
```

4. Zbuduj gotową paczkę:

```powershell
.\budujprod.bat
```

## Skrypty w katalogu głównym

### `testuj.bat`

Pełna walidacja projektu:

- `dotnet build UberDashboard.slnx -c Release`
- `dotnet test UberDashboard.slnx -c Release --no-build`

Ten skrypt **nie podbija wersji**.

### `budujdev.bat`

Build developerski:

- podbija wersję
- uruchamia pełną walidację
- publikuje aplikację w trybie `Debug`
- zapisuje wynik do `Release/Dev/<build-id>`
- uruchamia zbudowaną aplikację

### `budujfast.bat`

Szybki build bez testów:

- podbija wersję
- pomija build/test rozwiązania
- publikuje aplikację jako build nieweryfikowany
- zapisuje wynik do `Release/Dev/<build-id>-fast`
- uruchamia zbudowaną aplikację

### `budujprod.bat`

Build produkcyjny:

- podbija wersję
- uruchamia pełną walidację
- publikuje aplikację w `Release`, `win-x64`, `self-contained`
- zapisuje wynik do `Release/Prod/<build-id>`
- uruchamia zbudowaną aplikację

### `budujinst.bat`

Budowa instalatora:

- podbija wersję
- uruchamia pełną walidację
- publikuje staging aplikacji
- buduje instalator Inno Setup
- zapisuje wynik do `Release/Instal/<build-id>`

Jeżeli `ISCC.exe` nie jest dostępne, skrypt kończy się kontrolowanym błędem.

### `usun.bat`

Czyszczenie starych buildów:

- zachowuje najnowszy katalog w `Release/Dev`
- zachowuje najnowszy katalog w `Release/Prod`
- zachowuje najnowszy katalog w `Release/Instal`

Tryb podglądu:

```powershell
.\usun.bat -DryRun
```

### `folder.bat`

Otwiera katalog `Release` w Eksploratorze Windows.

## Wersjonowanie

- wersja jest zapisywana w `Directory.Build.props`
- skrypty `budujdev.bat`, `budujfast.bat`, `budujprod.bat`, `budujinst.bat` podbijają patch version
- każdy build dostaje katalog w formacie:

```text
v<wersja>_<YYYYMMDD-HHMMSS>
```

lub dla szybkiego builda:

```text
v<wersja>_<YYYYMMDD-HHMMSS>-fast
```

## Instalator

Konfiguracja instalatora znajduje się w:

```text
installer/UberDashboard.iss
```

Instalacja jest wykonywana per-user i nie powinna naruszać danych użytkownika zapisanych poza katalogiem programu.

## Dane użytkownika i logi

Domyślne lokalizacje:

- profil aplikacji: `%LOCALAPPDATA%\UberDashboard`
- baza danych: `%LOCALAPPDATA%\UberDashboard\Data\dashboard.db`
- logi: `%LOCALAPPDATA%\UberDashboard\LOGS`
- backupy: `%LOCALAPPDATA%\UberDashboard\Backups`

## Testy

Aktualny zestaw testów obejmuje:

- `UberDashboard.Core.Tests`
- `UberDashboard.Application.Tests`
- `UberDashboard.Infrastructure.Tests`

Uruchomienie:

```powershell
dotnet test UberDashboard.slnx -v q
```

## Repozytorium git

- repo zdalne: `https://github.com/Kavaars/UberDashboard` (prywatne)
- gałąź główna: `main`, śledzi `origin/main`
- remote `origin`: `https://github.com/Kavaars/UberDashboard.git`

### Konwencja commitów

Format: `typ: krótki opis` (Conventional Commits, opis po polsku, bez ogonków w nagłówku).

Najczęściej używane typy:

- `feat` — nowa funkcja
- `fix` — poprawka błędu
- `chore` — porządki, konfiguracja, zależności
- `docs` — dokumentacja i pliki rejestrów
- `test` — testy
- `refactor` — zmiany bez wpływu na zachowanie

### Workflow

1. Praca odbywa się na `main` (projekt jednoosobowy).
2. Po zamknięciu zadania: `git add <pliki>` → `git commit`.
3. Wypchnięcie: `git push`.
4. Rejestry projektu (`listaZmian.md`, `wersje.md`, `changelog.txt`) aktualizuje się razem ze zmianą kodu w tym samym commicie.

### Tagi wersji

- tag tworzy się z wersji w `Directory.Build.props`, w formacie `v<MAJOR>.<MINOR>.<PATCH>`
- tag jest dołączony do commita reprezentującego dany stan wersji

```powershell
git tag -a v1.0.7 -m "UberDashboard 1.0.7 - opis"
git push origin v1.0.7
```

- poprawka błędu podbija `PATCH`, nowa funkcja `MINOR`, zmiana łamiąca `MAJOR`
- skrypty `buduj*.bat` podbijają wersję w `Directory.Build.props`, ale tag zakłada się ręcznie po weryfikacji builda
