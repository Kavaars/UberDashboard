# Shell, Tryby Okna, Pogoda i Ustawienia - Design

**Data:** 2026-11-02

## Cel

Domknąć brakujący zakres v1 aplikacji UberDashboard tak, aby produkt zawierał:

- trzeci wbudowany panel `Weather`
- 3 tryby działania okna: zwykłe okno, tryb desktop-widget `best effort` i tryb dokowanego paska rezerwującego obszar ekranu
- odświeżony, nowocześniejszy interfejs zgodny stylistycznie z referencją użytkownika
- pełne `Opcje/Ustawienia` z zakładką `Personalizacja`, która pozwala zmieniać motyw, kolory, fonty i parametry wizualne bez rozwalania spójności UI

## Zakres

- warstwa powłoki WPF:
  - `MainWindow.xaml`
  - `MainWindow.xaml.cs`
  - `ShellViewModel`
  - zasoby `Theming/*`
- nowa warstwa koordynacji trybów okna oparta o istniejące fundamenty AppBar
- nowy feature `Weather`
- nowe okno lub panel `Opcje/Ustawienia`
- trwałe ustawienia aplikacji dla trybów okna i personalizacji
- aktualizacja testów aplikacyjnych i integracyjnych dla nowego zachowania
- aktualizacja `listaZmian.md`, `wersje.md`, `changelog.txt` po wdrożeniu

Poza tym etapem:

- pełny, gwarantowany shell-level desktop embedding bez fallbacku
- automatyczne wykrywanie lokalizacji użytkownika
- kompletnie dowolne, nieograniczone kolorowanie każdego elementu UI
- osobny marketplace wtyczek lub zaawansowany system presetów motywów

## Podejście

Wybrany wariant: najpierw dokończyć brakujące fundamenty v1, potem osadzić na nich pogodę i nowy shell UI.

Powód:

- repo ma już zalążki architektury dla `AppBarController`, hosta paneli i themingu
- panel `Weather` powinien wejść do tego samego systemu co `Notes` i `Todo`, bez obejść
- redesign UI ma sens dopiero wtedy, gdy w topbarze i ustawieniach istnieją już realne funkcje trybów okna i personalizacji
- minimalizujemy przepisywanie `MainWindow.xaml` dwa razy

Kolejność logiczna:

1. `WindowModes`
2. `Weather`
3. `Shell/UI Refresh`
4. `Options/Settings`

## Architektura

### 1. WindowModes

Dodana zostanie jawna warstwa koordynująca tryb okna, zamiast rozsypywania logiki Win32 po `MainWindow.xaml.cs`.

#### Model

- `WindowMode`
  - `Normal`
  - `DesktopWidget`
  - `DockedBar`
- `WindowModeOptions`
  - `MonitorId`
  - `DockEdge`
  - `DockThickness`
  - `WidgetOpacity`
  - `LaunchMode`
  - flagi zachowania fallbacku

#### Usługi

- `IWindowModeCoordinator`
- `NormalWindowModeHandler`
- `DockedBarWindowModeHandler`
- `DesktopWidgetModeHandler`

`ShellViewModel` tylko zgłasza żądanie zmiany trybu. Całe wejście/wyjście z trybów, manipulacja uchwytem okna, AppBar i ewentualna integracja z pulpitem żyją w osobnej warstwie usług.

#### Zachowanie

- `Normal`
  - zwykłe okno WPF
  - tryb bazowy i awaryjny
- `DockedBar`
  - używa istniejącego `AppBarController`
  - rezerwuje obszar wybranego monitora i krawędzi
  - stosuje tylko prostokąt zwrócony przez system po `ABM_SETPOS`
- `DesktopWidget`
  - działa jako tryb `best effort`
  - próbuje osadzić powierzchnię pod zwykłymi oknami, ale bez agresywnego wymuszania `Topmost`
  - przy błędzie lub utracie stabilnej powierzchni wraca do `Normal`

#### Zasady bezpieczeństwa

- brak kradzieży fokusu
- brak agresywnego `Topmost`
- brak nieskończonych pętli ponawiania przy problemach Explorera
- przełączenia trybów są idempotentne
- każdy błąd trybu kończy się toastem, wpisem do logu i powrotem do `Normal`

### 2. Weather

`Weather` będzie trzecim built-in panelem hostowanym dokładnie tak samo jak `Notes` i `Todo`.

#### Feature folder

Nowe pliki trafią do:

- `src/UberDashboard.App/Features/Weather`

Wzorzec:

- `WeatherPanelDescriptor`
- `WeatherPanelFactory`
- `WeatherPanelInstance`
- `WeatherPanelViewModel`

#### Usługi

Po stronie application/infrastructure:

- `IWeatherService`
- `IWeatherLocationSearchService`
- `IWeatherCacheStore`
- klient Open-Meteo
- persystencja konfiguracji i cache poza wątkiem UI

#### Zakres funkcjonalny v1

- ręczny wybór lokalizacji
- wyszukiwanie lokalizacji po wpisanej nazwie
- bieżące warunki:
  - temperatura
  - opis warunków
  - kod pogody i ikona
  - wiatr
  - czas danych
- ręczne odświeżanie
- lokalny cache
- stany:
  - brak konfiguracji
  - ładowanie
  - sukces
  - błąd
  - dane nieświeże z cache

#### Zasady działania

- brak requestów pogodowych przed wyborem lokalizacji
- wyszukiwanie lokalizacji z debounce
- pobieranie i odświeżanie poza UI
- każda instancja panelu ma własną konfigurację i własny stan
- ukryty panel nie inicjuje aktywnego odświeżania
- przy błędzie sieci panel pokazuje ostatni poprawny cache, jeśli istnieje

### 3. Shell/UI Refresh

Powłoka zostanie przebudowana z prostego paska narzędzi w bardziej dopracowany shell desktopowy.

#### Topbar

- lewa część:
  - marka `UberDashboard`
  - wybór pulpitu
- środek:
  - segmentowy przełącznik trybu `Okno | Widget | Pasek`
- prawa część:
  - `Dodaj panel`
  - `Ukryte`
  - `Nowy pulpit`
  - `Opcje`
  - `Motyw`
  - `Odśwież`

#### Język wizualny

- większe promienie rogów
- miękkie powierzchnie i delikatne obrysy
- lepszy spacing
- subtelne stany `hover`, `focus`, `selected`
- lekkie ikony wektorowe
- bardziej dopracowane karty paneli, menu i toasty

#### Karty paneli

- wspólny system nagłówka karty:
  - ikona typu panelu
  - nazwa
  - akcje po prawej
- czytelniejszy wizualnie tryb edycji
- `Weather`, `Notes` i `Todo` korzystają z jednego zestawu styli kart

#### Zasoby

Rozbudowa themingu będzie oparta o resource dictionaries, nie o pojedyncze, ręczne override'y.

Zakładane kierunki:

- rozszerzenie `Theming/Controls.xaml`
- rozszerzenie `Theming/PanelTemplates.xaml`
- możliwe wydzielenie dodatkowego słownika dla visual tokenów lub shella, jeśli obecne pliki zaczną puchnąć

### 4. Opcje/Ustawienia

Dodany zostanie pełny moduł ustawień aplikacji dostępny z topbara.

#### Forma UI

- osobne okno lub panel ustawień z zakładkami
- lewa kolumna nawigacyjna
- prawa część z grupami ustawień
- podgląd zmian na żywo tam, gdzie to możliwe

#### Zakładki v1

- `Personalizacja`
  - motyw `jasny / ciemny / systemowy`
  - kolor akcentu
  - kolory powierzchni i tekstu w ramach kontrolowanych tokenów
  - font
  - rozmiar fontu
  - promień narożników
  - gęstość interfejsu
  - poziom przezroczystości UI
- `Wyświetlanie`
  - domyślny tryb startu
  - ustawienia `DockedBar`: monitor, krawędź, grubość
  - ustawienia `DesktopWidget`: przezroczystość i polityka fallbacku
- `Aplikacja`
  - podstawowe zachowanie przy starcie
  - odświeżanie danych pogody
  - miejsce na ustawienia toastów i diagnostyki, jeśli istniejące usługi pozwolą na sensowną ekspozycję

#### Model danych

- `ApplicationSettings`
- `IApplicationSettingsService`
- zapis do istniejącego store ustawień

#### Ograniczenia personalizacji

Personalizacja będzie oparta o design tokens i kontrolowany zakres wartości, nie o całkowicie dowolne ustawianie wszystkiego. Celem jest:

- zachowanie czytelności
- utrzymanie kontrastu
- brak rozjazdu wizualnego między panelami
- możliwość natychmiastowego zastosowania zmian w shellu

## File Map

Przewidywane główne miejsca zmian:

- `src/UberDashboard.App/MainWindow.xaml`
- `src/UberDashboard.App/MainWindow.xaml.cs`
- `src/UberDashboard.App/Shell/ShellViewModel.cs`
- `src/UberDashboard.App/Theming/Controls.xaml`
- `src/UberDashboard.App/Theming/PanelTemplates.xaml`
- `src/UberDashboard.App/Hosting/AppServiceCollectionExtensions.cs`
- `src/UberDashboard.App/Features/Weather/*`
- nowe pliki ustawień w warstwie `App` i/lub `Application`
- pliki usług platformowych dla koordynacji trybów okna
- testy w `tests/UberDashboard.Application.Tests` i tam, gdzie wymagają tego nowe usługi

## Edge Cases

- brak możliwości stabilnego osadzenia `DesktopWidget`
  - fallback do `Normal`
- restart Explorera w trybie `DesktopWidget`
  - jedna kontrolowana próba odtworzenia, potem fallback
- brak poprawnego prostokąta AppBar
  - tryb `DockedBar` kończy się fallbackiem
- zmiana monitorów lub DPI w `DockedBar`
  - ponowne przeliczenie geometrii albo fallback
- brak sieci dla `Weather`
  - pokazanie cache albo czytelny stan błędu
- brak wyników geokodowania
  - stan pusty bez psucia konfiguracji panelu
- skrajne ustawienia personalizacji
  - wartości ograniczone do bezpiecznych zakresów
- zmiana fontu lub kolorów na żywo
  - aktualizacja zasobów bez restartu aplikacji, jeśli technicznie możliwa

## Weryfikacja

- testy jednostkowe dla logiki koordynatora trybów okna
- testy jednostkowe dla AppBar orchestration tam, gdzie obecna architektura już rozdziela logikę od Win32
- testy aplikacyjne dla ustawień i mapowania `ApplicationSettings`
- testy `Weather`:
  - brak requestów bez lokalizacji
  - poprawne stany `loading/error/stale/success`
  - cache fallback
- testy regresyjne rejestracji built-in paneli
- `dotnet build UberDashboard.slnx`
- `dotnet test UberDashboard.slnx`

Przy wdrożeniu trzeba też zweryfikować ręcznie:

- przełączanie 3 trybów okna
- zachowanie po maksymalizacji innej aplikacji nad paskiem `DockedBar`
- działanie `DesktopWidget` z fallbackiem
- odświeżanie personalizacji na żywo

## Ograniczenia

- katalog roboczy nie jest aktualnie repozytorium git, więc commit specyfikacji nie może zostać wykonany
- `DesktopWidget` w v1 pozostaje funkcją `best effort`, zgodnie z planem projektu
- zakres UI personalization musi pozostać kontrolowany przez tokeny, nie przez dowolne override'y
- finalne zachowanie `DesktopWidget` zależy od realnej współpracy z Explorerem i może wymagać ograniczenia funkcji przy zachowaniu fallbacku
