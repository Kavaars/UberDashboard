# Automatyzacja Buildów i Dokumentacji - Design

**Data:** 2026-11-02

## Cel

Domknąć warstwę developersko-wydaniową projektu UberDashboard tak, aby aplikacja miała powtarzalne skrypty Windows do testów, buildów developerskich i produkcyjnych, budowy instalatora oraz porządkowania katalogów wyjściowych. Każdy build ma automatycznie podbijać wersję aplikacji i trafiać do nowego katalogu wydania.

## Zakres

- rootowe skrypty: `budujdev.bat`, `budujprod.bat`, `budujfast.bat`, `budujinst.bat`, `testuj.bat`, `usun.bat`, `folder.bat`
- wspólna logika PowerShell dla wersjonowania, katalogów buildów i fail-fast
- katalogi wyjściowe `Release/Dev`, `Release/Prod`, `Release/Instal`
- szablon instalatora Inno Setup
- pliki śledzące: `listaZmian.md`, `wersje.md`, `changelog.txt`
- pełny `README.md`

## Podejście

Wybrany wariant: wspólny helper PowerShell plus cienkie wrappery `.bat`.

Powód:

- jedna implementacja reguł wersjonowania i katalogów wyjściowych
- mniejsza duplikacja niż w pełnych skryptach `.bat`
- prostsze utrzymanie fail-fast i spójnych logów na Windows 11

## Architektura

### Warstwa skryptów

- `scripts/BuildTools.ps1`
  - odczyt wersji z `Directory.Build.props`
  - podbicie patch version
  - zapis nowej wersji do `Directory.Build.props`
  - tworzenie identyfikatora builda `v<wersja>_<yyyyMMdd-HHmmss>`
  - tworzenie katalogów wynikowych
  - wspólne uruchamianie poleceń z walidacją exit code
  - retencja buildów

- rootowe `.bat`
  - tylko ustawiają tryb i delegują do PowerShell

### Dystrybucja

- `budujdev.bat`
  - pełna walidacja
  - `dotnet publish` w trybie developerskim do `Release/Dev/<build-id>`
  - uruchomienie zbudowanej aplikacji

- `budujprod.bat`
  - pełna walidacja
  - `dotnet publish -c Release -r win-x64 --self-contained true`
  - wynik w `Release/Prod/<build-id>`
  - uruchomienie aplikacji

- `budujfast.bat`
  - bez testów
  - oznaczenie builda jako nieweryfikowany
  - unpacked output do `Release/Dev/<build-id>-fast`

- `budujinst.bat`
  - pełna walidacja
  - najpierw build produkcyjny do katalogu staging
  - potem `ISCC.exe` dla `installer/UberDashboard.iss`
  - wynik do `Release/Instal/<build-id>`

### Dokumentacja

- `README.md`
  - architektura
  - wymagania
  - szybki start
  - opis skryptów
  - lokalizacja danych użytkownika

- pliki historii
  - aktualne wpisy na początku pliku

## Edge Cases

- brak `git` nie blokuje buildów
- brak Inno Setup kończy `budujinst.bat` kontrolowanym błędem z instrukcją
- brak katalogów `Release/*` nie jest błędem; są tworzone automatycznie
- `usun.bat` wspiera tryb podglądu i usuwa tylko stare katalogi buildów
- wersja jest podbijana tylko przez skrypty buildowe, nie przez `testuj.bat`

## Weryfikacja

- `testuj.bat`
- `budujfast.bat`
- `budujdev.bat`
- `budujprod.bat`
- jeśli Inno Setup dostępny: `budujinst.bat`
- końcowo: `dotnet build UberDashboard.slnx` i `dotnet test UberDashboard.slnx`

## Ograniczenia

- repo nie jest obecnie zainicjalizowane jako git, więc commit designu nie może zostać wykonany
- instalator zależy od lokalnej instalacji Inno Setup
