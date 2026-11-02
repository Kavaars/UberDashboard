# Rejestr problemów

## 2026-11-02T11:15:16+01:00 | v1.0.7 | panel pogody, tooltipy i glify akcji

- Moduł: `UberDashboard.App` / `Features.Weather` / `Theming` / `MainWindow`
- Zgłoszenie użytkownika:
  - napisy nakładają się w panelu pogody
  - panel pogody ma pokazywać duże ikony oraz więcej danych: prognozę godzinową, temperaturę max/min, świt i zmierzch
  - tekst nie mieści się w tooltipach
  - ikony opcji i usuwania wyświetlają się niepoprawnie
  - w polu wyboru pulpitu zamiast `Mój pulpit` pojawia się techniczny tekst `dashboardSummary[...]`
  - `Dodaj panel` wizualnie odstaje od reszty przycisków
- Oczekiwane zachowanie:
  - karta pogody ma czytelny układ bez nakładania tekstów i pokazuje rozszerzone dane pogodowe
  - tooltipy zawijają dłuższy tekst
  - glify akcji są czytelne i spójne
  - selektor pulpitów pokazuje nazwę, a nie domyślne `ToString()` rekordu
  - `Dodaj panel` wygląda jak reszta sterowania w topbarze
- Faktyczne zachowanie:
  - placeholder w karcie pogody pozostawał widoczny także po załadowaniu danych i nachodził na odczyt temperatury
  - model pogody ograniczał się do warunków bieżących, bez danych dobowych i godzinowych
  - styl tooltipa ograniczał szerokość, ale nie wymuszał zawijania tekstu
  - część glifów była renderowana fontem, który w praktyce nie pokazywał poprawnie użytych kodów
  - kontrolki potrafiły pokazać pełne `ToString()` dla `DashboardSummary`
- Kroki odtworzenia:
  1. Uruchomić `UberDashboard`.
  2. Dodać panel `Pogoda`, wybrać lokalizację i poczekać na pobranie danych.
  3. Najechać na ikonki akcji panelu i dłuższe tooltipy w górnym pasku.
  4. Sprawdzić tekst aktywnego pulpitu i wygląd przycisku `Dodaj panel`.
- Środowisko:
  - Windows 11
  - workspace `f:\MyApps\UberDashboard`
- Powiązanie z ostatnimi zmianami:
  - problem koreluje z ostatnim redesignem UI i pierwszą implementacją panelu pogody z `v1.0.4` / `v1.0.5`
  - brak `.git` w workspace, więc analiza regresji ponownie opierała się na bieżącym kodzie i wcześniejszych wpisach w `problemy.md`
- Potwierdzona przyczyna:
  - w `PanelTemplates.xaml` placeholder dla pustego panelu pogody miał lokalne wiązanie `Visibility`, które utrzymywało go widocznym mimo aktywnego snapshotu
  - kontrakt `WeatherSnapshot` i klient Open-Meteo zwracały tylko dane `current`, bez sekcji `daily` i `hourly`
  - styl `ToolTip` nie ustawiał `TextBlock.TextWrapping`
  - glify akcji korzystały z `Segoe Fluent Icons`, a użyte kody lepiej odpowiadają `Segoe MDL2 Assets`
  - `DashboardSummary` nie nadpisywał `ToString()`, więc część kontrolek mogła pokazywać techniczny dump rekordu
- Fix:
  - rozszerzono `WeatherSnapshot` i `OpenMeteoWeatherService` o dane dobowe oraz prognozę godzinową
  - przebudowano kartę pogody: duża ikona warunków, zakres temperatur, świt/zmierzch, metadane i prognoza godzinowa
  - usunięto konflikt widoczności placeholdera, który powodował nakładanie napisów
  - wymuszono zawijanie treści w tooltipach
  - przełączono glify UI na `Segoe MDL2 Assets`
  - `DashboardSummary.ToString()` zwraca teraz samą nazwę pulpitu, a selektor dostał jawny `ItemTemplate`
  - `Dodaj panel` i `Ukryte` dostały host z obramowaniem zgodnym z resztą topbara
- Test regresji:
  - `dotnet test tests/UberDashboard.Application.Tests/UberDashboard.Application.Tests.csproj --filter "FullyQualifiedName~DashboardSummaryTests" -m:1` PASS
  - `dotnet test tests/UberDashboard.Infrastructure.Tests/UberDashboard.Infrastructure.Tests.csproj --filter "FullyQualifiedName~OpenMeteoWeatherServiceTests" -m:1` PASS
  - `dotnet test tests/UberDashboard.App.Tests/UberDashboard.App.Tests.csproj --filter "FullyQualifiedName~WeatherPanelViewModelTests" -m:1` PASS
  - `dotnet build UberDashboard.slnx -m:1` PASS
  - `dotnet test UberDashboard.slnx -m:1` PASS
- Pozostałe ryzyko:
  - tryby `DockedBar` i `DesktopWidget` nadal wymagają osobnej ręcznej weryfikacji runtime na aktywnej powłoce Windows
  - część wcześniejszych uwag o ogólnym UI została już pokryta kodem, ale pełny audyt wizualny nadal warto wykonać na żywo
- Status:
  - naprawione w kodzie i zweryfikowane automatycznie

## 2026-11-02T10:05:00+01:00 | v1.0.5 | pakiet UX, paneli i zachowania okna

- Moduł: `UberDashboard.App` / shell WPF / theming / panel templates / system integration
- Zgłoszenie użytkownika:
  - ciemny motyw ma błędy kontrastu i niespójne kontrolki
  - wybór kolorów ma działać przez systemowy picker z opcją wpisania kodu
  - każdy panel ma mieć ikonę ustawień i pływające okno opcji
  - akcje typu `Usuń` mają używać ikon zamiast tekstu
  - panel notatnika ma dostać formatowanie tekstu i wklejanie obrazów
  - tryby zamykania, tray i autostart mają działać jednoznacznie
  - tryb paska ma respektować zakres `200..1200 px`
- Oczekiwane zachowanie:
  - poprawny kontrast kontrolek w jasnym i ciemnym motywie
  - opcje paneli dostępne z nagłówka, bez zajmowania stałego miejsca w treści
  - notatnik obsługuje bogatą treść, listy, wyrównanie, pogrubienie i obrazy ze schowka
  - zamknięcie aplikacji wykonuje wybrany tryb: pełne wyjście albo minimalizację do traya
  - ustawienia okna i paneli zapisują się i odtwarzają bez utraty nowych pól
- Faktyczne zachowanie:
  - stare szablony paneli nadal używały tekstowych przycisków i zwykłego `TextBox` dla notatnika
  - część nowych modeli/settings była już przygotowana, ale UI nie było do nich podpięte
  - po włączeniu `UseWindowsForms` pojawiły się konflikty typów WPF/WinForms blokujące build
  - kontrakt testowy trybu paska wymagał zakresu `200..1200`, ale domyślna wartość nadal wynosiła `56`
- Kroki odtworzenia:
  1. Uruchomić `UberDashboard`.
  2. Przełączyć motyw na ciemny i otworzyć kontrolki wyboru / opcje paneli.
  3. Otworzyć panel notatnika i spróbować użyć formatowania lub wkleić obraz.
  4. Zamknąć okno aplikacji krzyżykiem i sprawdzić zachowanie procesu oraz traya.
- Środowisko:
  - Windows 11
  - workspace `f:\MyApps\UberDashboard`
- Powiązanie z ostatnimi zmianami:
  - problem koreluje bezpośrednio z wcześniejszym redesignem shella i niedomkniętym wdrożeniem v1.x
  - brak `.git` w workspace, więc analiza regresji opiera się na `problemy.md`, `listaZmian.md`, `wersje.md` i bieżącym stanie plików
- Potwierdzona przyczyna:
  - warstwa ViewModel była częściowo gotowa, ale `PanelTemplates.xaml` i style kontrolek nadal korzystały z poprzednich szablonów
  - po aktywacji WinForms dla traya i pickera kolorów nie wszystkie odwołania WPF zostały jawnie zaliasowane
  - kontrakt `WindowModeSettings` został zaostrzony testami, ale domyślna wartość i jeden test trwałości pozostały w starej skali
- Fix:
  - podpięto nowe popupy opcji paneli, ikonowe akcje i bogaty edytor notatek w `PanelTemplates.xaml`
  - dodano theming dla tooltipów, checkboxów, list items, RichTextBox i własny template `ComboBox`
  - rozszerzono `BindableRichTextBox` o realne przełączanie zawijania dokumentu
  - naprawiono konflikty WPF/WinForms (`RichTextBox`, `MessageBox`, `DataFormats`, dialogi, `FontFamily`, `ColorConverter`)
  - ustawiono bezpieczną domyślną grubość paska i zaktualizowano test trwałości ustawień
  - ToDo zapisuje zmianę opcji `ShowSummary`, więc ustawienie panelu nie ginie po odświeżeniu
- Test regresji:
  - `dotnet build src/UberDashboard.App/UberDashboard.App.csproj` PASS
  - `dotnet test tests/UberDashboard.App.Tests/UberDashboard.App.Tests.csproj -m:1 --no-build` PASS
  - `dotnet test tests/UberDashboard.Application.Tests/UberDashboard.Application.Tests.csproj -m:1 --no-build` PASS
  - `dotnet test tests/UberDashboard.Infrastructure.Tests/UberDashboard.Infrastructure.Tests.csproj -m:1 --filter "FullyQualifiedName~ApplicationSettingsServiceTests|FullyQualifiedName~WindowModeSettingsServiceTests"` PASS
- Pozostałe ryzyko:
  - rzeczywiste zachowanie `DockedBar` i `DesktopWidget` nadal wymaga pełnego testu runtime na aktywnej powłoce Windows
  - nie wszystkie globalne przyciski powłoki zostały jeszcze sprowadzone do wariantu ikonowego
- Status:
  - częściowo naprawione i zweryfikowane automatycznie; dalsza iteracja UX/runtime pozostaje otwarta

## 2026-11-02T09:02:00+01:00 | v1.0.4 | aktualizacja po fixie

- Moduł: `UberDashboard.App` / `UberDashboard.Infrastructure` / `UberDashboard.Application`
- Potwierdzona przyczyna:
  - zakres v1 był niekompletny: brakowało implementacji panelu `Weather`, koordynacji trybów `Normal/DockedBar/DesktopWidget`, modułu `Opcje/Ustawienia` oraz docelowego odświeżenia shella
- Fix:
  - dodano panel `Weather` z wyszukiwaniem lokalizacji Open-Meteo, cache i rejestracją jako trzeci built-in panel
  - dodano `WindowMode`, trwałe ustawienia trybów okna i `MainWindowModeCoordinator`
  - dodano `DesktopWidget` w modelu `best effort` z fallbackiem do `Normal`
  - dodano moduł `Opcje/Ustawienia` z zakładką `Personalizacja`
  - przebudowano shell i style na bardziej dopracowany, ikonowy układ z większymi promieniami narożników
- Test regresji:
  - `WindowModeSettingsContractTests`
  - `WindowModeSettingsServiceTests`
  - `MainWindowModeCoordinatorTests`
  - `WeatherPanelRegistrationTests`
  - `WeatherPanelViewModelTests`
  - `OpenMeteoWeatherServiceTests`
  - `OpenMeteoLocationSearchServiceTests`
  - `WeatherCacheStoreTests`
  - pełny `dotnet test UberDashboard.slnx`

## 2026-11-02T08:18:00.7503800+01:00 | v1.0.4

- Moduł: `UberDashboard.App` / powłoka WPF / funkcje v1
- Zgłoszenie użytkownika:
  - aplikacja uruchamia się i działa poprawnie, ale brakuje panelu pogody przewidzianego dla pierwszej wersji
  - nie są spełnione 3 tryby działania okna:
    - zwykłe okno Windows
    - tryb widgetu tła pulpitu w stylu Rainmeter
    - stały pasek pulpitu rezerwujący obszar ekranu
  - wygląd aplikacji wymaga dopracowania w kierunku nowocześniejszego stylu z ikonami, zaokrągleniami i bardziej dopracowaną kompozycją
- Oczekiwane zachowanie:
  - v1 zawiera panel pogody
  - aplikacja pozwala przełączyć 3 tryby działania okna zgodnie z planem projektu
  - interfejs ma spójniejszy, nowocześniejszy styl
- Faktyczne zachowanie:
  - w aktualnym UI dostępne są tylko panele `Notatnik` i `ToDo`
  - brak widocznej obsługi przełączania trybów okna
  - UI działa, ale jest jeszcze w wersji bazowej
- Kroki odtworzenia:
  1. Uruchomić aplikację `UberDashboard`.
  2. Otworzyć menu `Dodaj panel`.
  3. Sprawdzić brak panelu pogody.
  4. Sprawdzić brak przełącznika trybów okna w głównym pasku narzędzi i brak zachowania widget/AppBar.
- Środowisko:
  - Windows 11
  - lokalny workspace `f:\MyApps\UberDashboard`
- Powiązanie z ostatnimi zmianami:
  - sprawdzono `listaZmian.md`, `wersje.md`, `changelog.txt`
  - ostatnie udokumentowane zmiany dotyczyły automatyzacji buildów, instalatora i README
  - brak potwierdzenia, że problem jest regresją po ostatniej zmianie; obecnie wygląda na niedomknięty zakres funkcjonalny v1
- Status diagnozy:
  - przyczyna techniczna: nieustalona
  - test regresji: jeszcze nie dodany
