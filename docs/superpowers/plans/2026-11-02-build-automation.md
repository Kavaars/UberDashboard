# Build Automation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dodać powtarzalne skrypty build/test/installer, automatyczne wersjonowanie oraz dokumentację uruchomieniową projektu UberDashboard.

**Architecture:** Logika wykonawcza ma być skupiona w jednym helperze PowerShell, a rootowe pliki `.bat` mają pozostać cienkimi wrapperami dla wygodnego uruchamiania w Windows. Wersja aplikacji jest czytana i zapisywana w `Directory.Build.props`, a każdy build trafia do osobnego katalogu pod `Release/`.

**Tech Stack:** PowerShell 7+, Windows `.bat`, .NET 10 CLI, Inno Setup, Markdown

---

### Task 1: Dodać wspólny helper buildów

**Files:**
- Create: `f:\MyApps\UberDashboard\scripts\BuildTools.ps1`
- Modify: `f:\MyApps\UberDashboard\.gitignore`

- [ ] Step 1: Utworzyć helper PowerShell z funkcjami wersjonowania, katalogów i uruchamiania komend.
- [ ] Step 2: Dodać ignorowanie katalogów pośrednich i staging, jeśli nowe ścieżki tego wymagają.
- [ ] Step 3: Zweryfikować składnię helpera przez uruchomienie z parametrem testowym lub przez realne wywołanie jednego ze skryptów wrappera.

### Task 2: Dodać skrypty rootowe build/test/cleanup

**Files:**
- Create: `f:\MyApps\UberDashboard\budujdev.bat`
- Create: `f:\MyApps\UberDashboard\budujprod.bat`
- Create: `f:\MyApps\UberDashboard\budujfast.bat`
- Create: `f:\MyApps\UberDashboard\budujinst.bat`
- Create: `f:\MyApps\UberDashboard\testuj.bat`
- Create: `f:\MyApps\UberDashboard\usun.bat`
- Create: `f:\MyApps\UberDashboard\folder.bat`

- [ ] Step 1: Dodać wrappery `.bat` delegujące do `scripts/BuildTools.ps1`.
- [ ] Step 2: Zapewnić fail-fast przez sprawdzanie `%ERRORLEVEL%` po każdym wywołaniu PowerShell.
- [ ] Step 3: Uruchomić `testuj.bat`, następnie co najmniej `budujfast.bat` i `budujprod.bat`.

### Task 3: Dodać konfigurację instalatora

**Files:**
- Create: `f:\MyApps\UberDashboard\installer\UberDashboard.iss`
- Modify: `f:\MyApps\UberDashboard\scripts\BuildTools.ps1`

- [ ] Step 1: Utworzyć skrypt Inno Setup korzystający z katalogu staging i instalacji per-user.
- [ ] Step 2: Dodać w helperze obsługę wykrywania `ISCC.exe` i budowy instalatora.
- [ ] Step 3: Uruchomić `budujinst.bat`; jeśli Inno Setup nie jest zainstalowany, potwierdzić kontrolowany błąd.

### Task 4: Dodać dokumentację użytkową i pliki śledzące

**Files:**
- Create: `f:\MyApps\UberDashboard\README.md`
- Create: `f:\MyApps\UberDashboard\listaZmian.md`
- Create: `f:\MyApps\UberDashboard\wersje.md`
- Create: `f:\MyApps\UberDashboard\changelog.txt`

- [ ] Step 1: Opisać architekturę, wymagania i obsługę skryptów w `README.md`.
- [ ] Step 2: Dodać aktualne wpisy zmian, wersji i changelogu na początek odpowiednich plików.
- [ ] Step 3: Sprawdzić zgodność treści z faktycznie wdrożonymi skryptami i katalogami.

### Task 5: Końcowa weryfikacja

**Files:**
- Verify: `f:\MyApps\UberDashboard\Directory.Build.props`
- Verify: `f:\MyApps\UberDashboard\Release\`

- [ ] Step 1: Uruchomić `dotnet build UberDashboard.slnx`.
- [ ] Step 2: Uruchomić `dotnet test UberDashboard.slnx`.
- [ ] Step 3: Sprawdzić, że katalogi `Release/Dev`, `Release/Prod` i `Release/Instal` są tworzone zgodnie z trybem.
- [ ] Step 4: Zanotować ograniczenia końcowe, jeśli środowisko nie ma Inno Setup albo repo git.
