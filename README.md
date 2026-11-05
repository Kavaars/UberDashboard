# UberDashboard

Lokalna aplikacja desktopowa dla Windows 11 do budowy własnych pulpitów z paneli. Projekt jest rozwijany w .NET 10, WPF i architekturze modularnego monolitu z wyraźnym podziałem na warstwy domeny, aplikacji, infrastruktury, integracji Windows i hosta UI.

## Zasady tworzenia paneli

Przed projektowaniem lub tworzeniem nowego panelu AI musi przeczytać w całości [zasady projektu](ZASADY_PROJEKTU.md) oraz [wytyczne paneli](docs/WYTYCZNE_PANELI.md) i przestrzegać ich podczas implementacji i weryfikacji. Wytyczne obejmują wspólne funkcje, zakładki, zapis, wygląd, dostępność i listę odbioru. Obowiązek zapisano również w [AGENTS.md](AGENTS.md).

## Stan zgodności

Aktualny audyt i lista otwartych wymagań: [raport z 2026-11-02](docs/AUDYT_ZALOZEN_2026-11-02.md). Panele wbudowane i host instalowanych pluginów są dostępne. Wersja 1.2.0 dodaje paczki .udplugin i ramkę na zdjęcia; instrukcja i API: [Pluginy](docs/PLUGINY.md). Rzeczywista warstwa widgetów i pełny restore pozostają do wdrożenia. AppBar wymaga odbioru na żywej powłoce Windows. Nie wykonano protokołu KPI.

Osobny projekt `tests/UberDashboard.VisualChecks` renderuje szablony WPF na profilu testowym z fikcyjną pogodą, sprawdza glify i obraz w notatce oraz zapisuje zrzuty obu motywów, układów i zakładek ustawień. Nie zastępuje testów natywnego AppBar, schowka, DPI ani Narratora:

```powershell
dotnet run --project tests/UberDashboard.VisualChecks -c Release -- "Release/Dev/visual-$(Get-Date -Format yyyyMMdd-HHmmss)"
```

## Zakres v1

- wiele nazwanych pulpitów
- układ paneli oparty o drzewo podziałów
- panele wbudowane: Notatnik, ToDo i Pogoda
- host paneli oraz instalowanie/usuwanie pluginów .udplugin z ustawień i przez upuszczenie pliku
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
- skrypty `buduj*.bat` zachowują wersję aplikacji i tworzą unikalny katalog builda

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
- integracja WPF/SQLite/pluginów przez `tests/UberDashboard.PluginChecks`

Ten skrypt **nie podbija wersji**.

### `budujdev.bat`

Build developerski:

- zachowuje wersję aplikacji
- uruchamia pełną walidację
- publikuje aplikację w trybie `Debug`
- zapisuje wynik do `Release/Dev/<build-id>`
- uruchamia zbudowaną aplikację

### `budujfast.bat`

Szybki build bez testów:

- zachowuje wersję aplikacji
- pomija build/test rozwiązania
- publikuje aplikację jako build nieweryfikowany
- zapisuje wynik do `Release/Dev/<build-id>-fast`
- uruchamia zbudowaną aplikację

### `budujprod.bat`

Build produkcyjny:

- zachowuje wersję aplikacji
- uruchamia pełną walidację
- publikuje aplikację w `Release`, `win-x64`, `self-contained`
- zapisuje wynik do `Release/Prod/<build-id>`
- uruchamia zbudowaną aplikację

### `budujinst.bat`

Budowa instalatora:

- zachowuje wersję aplikacji
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
- wersję aplikacji zmieniamy jawnie po zmianie zachowania; skrypty buildów jej nie podbijają
- każdy build dostaje katalog w formacie:

```text
v<wersja>_<YYYYMMDD-HHMMSS-fff>
```

lub dla szybkiego builda:

```text
v<wersja>_<YYYYMMDD-HHMMSS-fff>-fast
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
- skrypty `buduj*.bat` odczytują wersję z `Directory.Build.props`; tag zakłada się ręcznie po weryfikacji builda

### Układ i personalizacja od 1.1.0

- Włącz **Edytuj układ** i przeciągnij tytuł panelu ku wybranej stronie drugiej karty. Etykieta wskazuje miejsce upuszczenia. Menu **Układ** udostępnia alternatywę bez przeciągania.
- **Dodaj obok** tworzy podział na kolumny, **Dodaj poniżej** na wiersze. Podziały można zagnieżdżać, a dzielniki regulują proporcje. **Trzy panele w kolumnie** zachowuje oryginalny panel i dodaje dwa mniejsze.
- **Wygląd** dotyczy jednej instancji panelu, niezależnie od pozycji i ukrywania. Pusty kolor oznacza motyw. Własny font jest opcjonalny; bez niego panel korzysta z globalnego. Tapeta wskazuje lokalny plik PNG/JPEG/BMP, który musi pozostać dostępny. Domyślny wygląd usuwa także tapetę.
- Zgoda na połączenia, opisy i odnośniki pogodowe są w opcjach pogody pod ikoną koła zębatego.
- Ustawienia aplikacji pokazują zmiany od razu, zapisują je po krótkiej pauzie i udostępniają **Ponów zapis** w razie błędu. Wybór motywu w formularzu przywraca jego globalną paletę; osobny wygląd paneli pozostaje zachowany.
- Walidacja WPF obejmuje osobne profile i przykładową pogodę. Nie zastępuje ręcznego odbioru przeciągania, integracji z Windows ani AppBar.

## Pluginy od 1.2.0

Pełna instrukcja instalacji, usuwania, budowy i kontraktów: [docs/PLUGINY.md](docs/PLUGINY.md). Pierwszy plugin jest osobnym projektem w plugins/PhotoFrame; paczkę tworzy scripts/BuildPhotoFramePlugin.ps1. Panele pluginów korzystają ze wspólnego układu i autozapisu. Usunięcie kodu zachowuje dane paneli do ponownej instalacji.

### Wysokość i wygląd paneli (1.3.0)

Włącz **Edytuj układ**. Przeciągnij dolną krawędź panelu w górę albo górną w dół, aby wydzielić wolne miejsce. Kliknij **Dodaj panel tutaj** lub przeciągnij tam nagłówek istniejącego panelu. Granice istniejących obszarów można przesuwać ponownie; zapis układu zachowuje pozycje i wolne miejsca. Ustawienia wyglądu są pod kołem zębatym panelu, w sekcji **Wygląd**. Wybór koloru pokazuje koło, jasność i krycie; pole wartości rozwija się przez **Wpisz wartość koloru**.
