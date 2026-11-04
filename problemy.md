# Rejestr problemów

## 2026-11-04T11:17:17+01:00 | 1.8.0 / 9e0866e + zmiany robocze | wygląd kontrolek i Microsoft To Do

- Moduły: motyw WPF, okna i dialogi, zasobnik Windows, zakładki ToDo. Środowisko: Windows, WPF/.NET 10. Wersja uruchomionej aplikacji użytkownika i rejestracja Microsoft Entra nieznane.
- Zgłoszenie: systemowe paski i okna odstają od motywu, a wybór list Microsoft To Do powinien być dostępny w ustawieniach. Odtworzenie w źródłach: otworzyć ustawienia i wybór pliku, przewinąć zawartość oraz sprawdzić menu zakładki ToDo.
- Przyczyna: okna i przewijanie korzystały z domyślnych szablonów Windows/WPF. Roboczy import Microsoft To Do był dostępny z menu zakładki i obsługiwał jedną listę.
- Zmiana: wspólne szablony motywu, dialog wyboru plików i kreator w Integracjach. Zapisane powiązania odświeżają się w kierunku Microsoft To Do → UberDashboard. Edytowalne kopie importuje się osobno.
- Regresje: testy szablonu nagłówka i obu kierunków przewijania, zapisu ustawień, odświeżenia tej samej karty i zachowania lokalnych zadań po ponownym wczytaniu. Kompilacja Release bez ostrzeżeń, 350 testów, 104 rendery WPF oraz kontrola wtyczek przeszły. Rzeczywiste konto Microsoft i interakcje powłoki Windows przy różnych DPI wymagają odbioru.

## 2026-11-04T08:12:10+01:00 | źródła 1.7.0 / 876a2d7 + zmiany robocze | kolor tekstu paneli

- Moduły: wygląd paneli, notatnik i afirmacje; Windows/WPF, .NET 10. Wersja uruchomionej aplikacji i konkretny dokument użytkownika nieznane.
- Zgłoszenie: tekst ma być biały w ciemnym motywie i czarny w jasnym, z indywidualnym nadpisaniem koloru w opcjach panelu. Powiązane wcześniejsze wpisy opisują ciemny tekst notatnika.
- Zmiana: oddzielne zasoby kolorów paneli, trwała opcja nadpisania i wybór koloru, notatnik stosuje kolor panelu do tekstu, afirmacje korzystają z tego samego zasobu. Dotychczasowe kolory fragmentów notatki nie są wyświetlane i przy dalszej edycji mogą zostać usunięte z zapisu; pozostałe formatowanie pozostaje.
- Weryfikacja: 303 testy automatyczne i kompilacja przeszły; rendery WPF jasnego i ciemnego motywu przeszły przed ostatnią zmianą tekstu ostrzeżenia. Nie zweryfikowano oryginalnej notatki użytkownika w uruchomionej aplikacji.


## 2026-11-04T07:34:00+01:00 | źródła 1.7.0 / 876a2d7 + zmiany robocze | zakładki, kolor notatek i tło pogody

- Moduły: Notes, BindableRichTextBox, Theming/PanelTemplates. Windows/WPF, .NET 10; wersja uruchomionej aplikacji i dane notatki użytkownika nieznane.
- Zgłoszenie: niewygodne zakładki, ciemny tekst w ciemnym motywie oraz odmienne tło niskiego i wysokiego panelu pogody; odtworzenie: porównać motywy i zmienić wysokość panelu.
- Powiązanie: wpisy z 2026-11-03 dotyczą wcześniejszych, niewystarczających zmian koloru. Przyczyna konkretnej notatki ze zrzutu pozostaje niepotwierdzona. W kodzie większy widok pogody miał dodatkowe tło, a kompaktowy go nie miał.
- Zmiany: pasek z przyciskiem dodawania, edycją nazwy, usuwaniem i przenoszeniem; wspólna powierzchnia pogody. Poprawka koloru pozostaje otwarta: format nie pozwala pewnie odróżnić świadomie ustawionej szarości od koloru motywu, a rzeczywisty dokument nie był dostępny.
- Weryfikacja: kompilacja bez ostrzeżeń, 302 testy i 96 renderów WPF. Nie wykonano odbioru interakcji myszy i klawiatury w działającej aplikacji ani porównania tapety; rzeczywista notatka ze zrzutu nie była dostępna.


## 2026-11-03T22:00:45+01:00 | źródła 1.7.0 / 876a2d7 + zmiany robocze | kolor notatnika i zakładki

- Moduł: App/Controls/BindableRichTextBox i Theming/PanelTemplates. Windows, WPF/.NET 10; dokładna konfiguracja użytkownika i wersja uruchomionego pliku nieznane.
- Zgłoszenie: po wcześniejszej poprawce tekst notatnika nadal jest ciemny na ciemnym tle; zrzut pokazuje także nazwę klasy w obszarze zakładek. Oczekiwany czytelny tekst zgodny z motywem i poprawny wygląd zakładek. Odtwarzanie: ciemny motyw i widoczna zawartość notatnika; dokładne pochodzenie tekstu nieznane.
- Powiązanie: poprzedni wpis 21:39 opisuje niepełną poprawkę, która normalizowała kolory tylko przy wczytywaniu. Test ujawnił ponadto możliwość zastąpienia dokumentu w trakcie jego zmiany; przyczyny konkretnego zrzutu nie można potwierdzić bez danych użytkownika.
- Poprawka: normalizacja neutralnych kolorów także podczas edycji, ochrona przed rekurencyjną podmianą dokumentu oraz szablon nagłówków i pustej zawartości zakładek. Istniejące inne kolory i pogrubienie pozostają zachowane.
- Regresja: przed poprawką test zakończył proces WPF błędem przy zmianie dokumentu; po poprawce test przechodzi, podobnie jak komplet 301 testów. Kompilacja w ramach testów zakończona powodzeniem; odbiór rzeczywistego okna i notatki ze zrzutu pozostaje otwarty.


## 2026-11-03T21:39:36+01:00 | źródła 1.7.0 / 876a2d7 + zmiany robocze | notatnik, menu, afirmacje i pasek

- Moduły: Notes, Theming, Affirmations, AppBar. Windows 11, WPF/.NET 10; dokładna konfiguracja monitorów i DPI nieznana.
- Zgłoszenie: brak zakładek, przezroczyste menu, brak polskich znaków, nieczytelny tekst notatnika w ciemnym motywie i przeskakiwanie paska przy zmianie grubości. Oczekiwane: wiele notatek, czytelne menu i tekst oraz stabilna dolna krawędź. Odtwarzanie: zmienić motyw lub poruszyć suwakiem grubości w trybie paska; pozostałe szczegóły nieznane.
- Powiązany wpis 09:34 opisuje wcześniejszą poprawkę paska. Aktualny kod przy każdej zmianie wyrejestrowywał pasek i przywracał poprzedni prostokąt okna, co mogło zmienić monitor automatyczny. Uszkodzone znaki występowały w bieżącym roboczym szablonie afirmacji.
- Zmiany: zakładki z zapisem zgodnym ze starszą notatką; zasób tła menu; poprawa znaków i dziedziczenia kolorów; zmiana konfiguracji zarejestrowanego paska bez przywracania okna. Potrzebna weryfikacja rzeczywistego pulpitu i dwóch monitorów; przyczyna skakania nie została potwierdzona na sprzęcie.
- Kontrole: kompilacja rozwiązania bez ostrzeżeń i błędów; 300 testów przeszło po uruchomieniu poza środowiskiem ograniczającym testhost. Dodano testy zapisu zakładek i aktualizacji AppBar.


## 2026-11-03T11:14:15+01:00 | źródła v1.6.0 / 876a2d7 + zmiany robocze | Surowy wygląd, dzielniki i rozbudowane ustawienia

- Moduły: Theming/PanelTemplates, Settings, MainWindow. Środowisko: Windows, WPF/.NET 10; dokładna wersja używana przez użytkownika, DPI i wymiary paska nieznane.
- Zgłoszenie: twardy, toporny interfejs, zbyt grube podziały paneli, rozlazłe menu i ustawienia. Oczekiwane: miękki, minimalistyczny pasek oraz zwarte formularze przy zachowaniu wszystkich ustawień, obu motywów i rozbudowanej personalizacji.
- Odtwarzanie znanego fragmentu: otworzyć personalizację — cztery pełne edytory kolorów wyświetlane są kolejno. Konkretne menu ocenione przez użytkownika i pozostałe kroki nieznane.
- Korelacja: dzielniki 6 DIP i rozbudowana personalizacja występują już w 876a2d7; roboczy diff SettingsWindow zmienia jedynie opis suwaka treści, więc sam nie wyjaśnia rozmiaru formularza. Powiązane wcześniejsze wpisy z 2026-11-03 dotyczą motywów, ColorWheel i opcji paneli.
- Potwierdzone w kodzie/renderach: pełne wypełnienie dzielników 6 DIP; stale rozwinięte edytory ColorWheel znacząco wydłużają formularz. Ocena estetyczna pozostaje preferencją użytkownika.
- Audyt i projekt: docs/UI_KIERUNEK_2026-11-03.md.
- Weryfikacja bazy: build VisualChecks PASS, 0 ostrzeżeń; 57 renderów i kontrole fontów/wejścia PASS po ponowieniu z dostępem do rejestru. Nie jest to potwierdzenie naprawy wyglądu ani odbiór AppBar.
- Wdrożenie 2026-11-03T21:20:00+01:00, v1.7.0: linia 1 DIP i uchwyt 6 DIP, suwaki odstępu i linii, krótszy pasek, menu i sześć kategorii ustawień. Pełny edytor koloru otwiera się z próbki. Zmiana motywu zachowuje paletę.
- Regresja: zachowanie ustawień po ponownym odczycie, normalizacja nowych suwaków, ustawienia bez pola odstępu i linii, domyślny tryb paska, podgląd fontu i wygląd pluginu. Przeszły build Release, 298 testów, 96 renderów oraz PluginChecks. Natywnie sprawdzono start na dolnej krawędzi, menu, podmenu i autozapis akcentu na osobnym profilu. Odbiór wszystkich krawędzi, konfiguracji monitorów i skalowania pozostaje otwarty.

## 2026-11-03T09:34:03+01:00 | v1.4.0, stan roboczy 4910420 | pogoda, menu, tło, pasek

- Moduły: Theming, Settings, Shell, okno główne. Środowisko: Windows, WPF, .NET SDK 10.0.403; rozmiary i konfiguracja użytkownika nieznane.
- Zgłoszenie: znikające ikony pogody po zmniejszeniu poziomego panelu, nieczytelne menu w ciemnym motywie, brak opcji przezroczystego tła do pulpitu oraz brak dynamicznej zmiany grubości paska.
- Oczekiwane: ikony dostępne w poziomym panelu, czytelne menu, widoczny pulpit bez ukrywania treści oraz zmiana grubości w aktywnym pasku. Odtwarzanie: przełączyć ciemny motyw i otworzyć menu paska; zmniejszyć poziomy panel pogody; zmieniać grubość w aktywnym pasku. Pozostałe szczegóły nieznane.
- Korelacja: ostatnie commity dotyczą dokumentacji; wcześniejsze niezacommitowane zmiany wprowadziły kompaktowy szablon pogody, autozapis i tapetę. Wpis z 08:27 dotyczy tych samych obszarów.
- Przyczyny w kodzie: kompaktowy szablon pomijał glify; menu kontekstowe miało systemowe tło z tekstem motywu; zapis ustawień uruchamiał tryb startowy zamiast aktywnego. Dodano glify, styl menu i zachowanie aktywnego trybu; test regresyjny obejmuje ustawienie grubości.
- Ograniczenia: samodzielna przezroczystość tła do pulpitu bez zanikania tekstu nie została wdrożona; bieżące krycie obejmuje całe okno. Weryfikacja efektu menu i paska w rzeczywistym pulpicie Windows pozostaje wymagana.

## 2026-11-03T08:27:43+01:00 | v1.3.0, stan roboczy 4910420 | orientacja paneli i wygląd okna

- Moduły: App/Theming, Settings, Shell, panele i wtyczki.
- Zgłoszenie: zawartość paneli powinna dynamicznie dostosowywać się do poziomej i pionowej orientacji; potrzebne są również tapeta aplikacji z rozmyciem oraz przezroczystość okna z rozmyciem pulpitu.
- Oczekiwane: dane w poziomym panelu układają się od lewej i pozostają dostępne podczas zmiany rozmiaru; ustawienia tła działają w całej aplikacji.
- Stan obecny: kod poziomego widoku pogody umieszcza część danych w rozwijanej sekcji; suwak przezroczystości widgetu jest nieaktywny. Pozostałe przypadki wymagają sprawdzenia w uruchomionej aplikacji.
- Odtwarzanie: szczegółowe rozmiary i konfiguracja zgłoszenia nieznane. Dla pogody kod wybiera widok kompaktowy przy szerokości co najmniej 480 DIP i wysokości poniżej 260 DIP.
- Środowisko: Windows, WPF, .NET SDK 10.0.403. W repozytorium istnieją liczne wcześniejsze niezacommitowane zmiany; ostatnie commity dotyczą dokumentacji i konfiguracji Git. Powiązany wpis v1.0.7 dotyczy czytelności pogody.
- Potwierdzona przyczyna widoku pogody: kompaktowy szablon umieszczał świt, zmierzch i prognozę pod zwiniętym elementem Expander. Zastąpiono go poziomą listą z przewijaniem; pozostałe panele i wtyczki nie zostały jeszcze dostosowane.
- Częściowa realizacja: dodano tapetę okna, jej rozmycie oraz krycie powierzchni i okna. Rozmycie pulpitu za oknem nie zostało wdrożone. Wygląd wymaga sprawdzenia w działającym oknie Windows.
- Dalsze zmiany 2026-11-03: dodano opcję systemowego materiału Acrylic, układy poziome notatek i zadań oraz kontrakt rozmiaru dla wtyczek i jego obsługę w ramce zdjęć. Wtyczki bez tego opcjonalnego kontraktu zachowują własny układ.
- Weryfikacja: 287 testów automatycznych PASS, w tym test ograniczania wartości ustawień i przełączenia układu tam i z powrotem; integracja wtyczki 37 kontroli PASS. Rendery WPF paneli w różnych rozmiarach PASS; efekt Acrylic i tapeta wymagają odbioru w rzeczywistym oknie na pulpicie.

## 2026-11-02T21:05:54.3677930+01:00 | v1.0.8 | audyt planu, trwałość paneli, tryby i UI

- Moduły: App/Shell, Panels, Theming, Settings, Weather, Infrastructure/Stores/Logging, Platform.Windows, BuildTools.
- Zgłoszenie: sprawdzić zgodność z planem i wymaganiami paneli/trybów/wtyczek/personalizacji, czytelność UI i zasoby; od razu poprawić potwierdzone usterki.
- Oczekiwane: trwałe operacje paneli, rzetelne stany i zgody, czytelne motywy i tryby zgodne z faktycznymi możliwościami aplikacji.
- Rzeczywiste: m.in. usuwanie bez potwierdzenia, nadpisywanie IsHidden autozapisem, resize wyłącznie wizualny, brak pozycjonowania okna AppBar i callbacku, fullscreen udający widget, utrata tokenów motywu, kwadraty ikon, czarne etykiety opcji w ciemnym motywie, techniczna nazwa monitora, pogoda bez zgody, brak części operacji układu w UI.
- Odtwarzanie: dodać ToDo i ukryć/reload; zmienić splitter i reload; usunąć panel; uruchomić legacy pogodę z lokalizacją bez zgody; przełączyć motyw i wejść w Opcje; przejrzeć host widgetu i połączenie AppBar z oknem. Scenariusze z testów używają odrębnych profili i fikcyjnych danych.
- Środowisko: lokalny Windows, .NET SDK 10.0.403, WPF/net10.0-windows. Stan początkowy git 4910420, czysty. Ostatnie commity 4910420/998f560 dotyczą dokumentacji/ignorowania narzędzi, nie wprowadzają tych usterek. Kod bazowy pochodzi z 8164ac5; dokładnego wcześniejszego momentu powstania każdej usterki nie ustalono. Starsze wpisy o braku .git opisują poprzedni stan; w tej iteracji repo jest dostępne.
- Powiązania: wcześniejsze wpisy v1.0.4, v1.0.5 i v1.0.7 o trybach, pogodzie, glifach i themingu. Poprzednie automatyczne PASS nie pokrywały wszystkich zachowań WPF.
- Potwierdzone przyczyny i naprawy: szczegóły w docs/AUDYT_ZALOZEN_2026-11-02.md. Najważniejsze: zapis stanu aktualizuje wyłącznie StateJson/UpdatedAtUtc; układ jest utrwalany po resize; dodano potwierdzenie usuwania, kontrolkę ruchu/menu, jawny fallback widgetu, adapter pozycjonowania AppBar, poprawne zasoby/font glifów/kontrast opcji, utrwalaną zgodę z anulowaniem pogody i ponawianie zapisu układu.
- Regresje przed poprawką: Cancelled_removal FAIL (0 wywołań potwierdzenia), Hide_survives FAIL (IsHidden=false), Legacy_location FAIL (2 żądania bez zgody). Po poprawkach wszystkie PASS.
- Weryfikacja: 225 testów PASS; build Release 0 ostrzeżeń/błędów; 22 rendery WPF PASS; publish win-x64 self-contained PASS. Build Release/Dev/v1.0.8_20261102-210401-650-audit.
- Ograniczenia: brak prawdziwej warstwy widgetu i kompletnego hosta wtyczek, tapet/per-panel personalization, pełnego restore/rollback i KPI. Nie wykonano odbioru AppBar/Explorera/monitorów/Narratora ani instalatora. Narzędzia pulpitu blokuje MXC os error 1005 przy enumeracji dysku D:; ustawień dysku nie zmieniano.
- Status: potwierdzone usterki z tej iteracji naprawione i pokryte opisanymi testami; cała specyfikacja v1 nie jest ukończona. Lista otwartych wymagań i granice dowodów w raporcie audytu.

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

## 2026-11-02T21:27:17.5905229+01:00 | v1.1.0 | Układ, personalizacja i ustawienia na żywo

- Moduły: Shell, Theming, Settings, Weather; baza Git 4910420 oraz zastane niezacommitowane zmiany v1.0.8.
- Zgłoszenie: nieczytelny motyw ciemny/systemowy, konieczność używania Zastosuj, informacje dostawcy pogody widoczne na karcie.
- Oczekiwane: czytelne motywy i zmiany wyglądu widoczne natychmiast; zgoda, opisy i źródła pogody wyłącznie w opcjach.
- Rzeczywiste przed zmianą: SettingsViewModel stosował i zapisywał ustawienia dopiero na komendę; formularz materializował kolory jasnego motywu jako stałe nadpisania. Jawne style edytorów pomijały dynamiczne tokeny fontu. Blok informacji pogodowych znajdował się poza popupem opcji.
- Reprodukcja: otworzyć Opcje, zmienić font/kolor bez Zastosuj; zapisać jasną paletę i przełączyć ciemny motyw; sprawdzić główną kartę pogody. Dokładna konfiguracja profilu użytkownika i moment powstania problemu: nieznane.
- Środowisko: Windows, .NET SDK 10.0.403, WPF/net10.0-windows. Powiązany wpis audytu v1.0.8; ostatnie commity dotyczą dokumentacji, brak dowodu na regresję po nich.
- Naprawa: natychmiastowy podgląd, grupowany autozapis i ponawianie; migracja dawnych domyślnych jasnych nadpisań w ciemnym motywie; dynamiczne fonty także w dokumencie notatki; reakcja na motyw Windows; przewijane opcje pogody zawierają zgody i źródła. Dodatkowo personalizacja osobnych instancji oraz wskazanie strefy upuszczenia i skrót trzech paneli w kolumnie.
- Regresje: AppearanceRegressionTests, LiveSettingsTests, Three_panels_in_one_column_keep_original_content_and_reload; rozszerzone VisualChecks sprawdzają też podgląd fontu bez zapisu i font dokumentu per panel.
- Granice: testy używają osobnych profili i danych przykładowych; odbiór prawdziwą myszą, rzeczywiste przełączenie motywu Windows i Explorer/AppBar nie są potwierdzone renderowaniem WPF. Tapeta jest odwołaniem do lokalnego pliku.
- Weryfikacja końcowa: build Release PASS (0 ostrzeżeń/błędów), 233 testy PASS, 33 rendery WPF trzech motywów PASS, podgląd fontu i font dokumentu per panel PASS, publish win-x64 self-contained PASS. Build: Release/Dev/v1.1.0_20261102-213028-157.

## 2026-11-02T21:51:51.5007905+01:00 | v1.2.0 | Panels — równoległe zamykanie instancji

- Źródło: scenariusz integracyjny nowego hosta pluginów; nie jest to osobne zgłoszenie użytkownika.
- Moduł: App/Panels/PanelHostManager, PluginManager, Shell.
- Oczekiwane: usunięcie pluginu podczas przełączania pulpitu bez podwójnego zwalniania panelu i bez utraty stanu.
- Rzeczywiste przed poprawką: ObjectDisposedException: The CancellationTokenSource has been disposed w SuspendAsync, gdy dwie operacje oczekiwały na zapis tej samej instancji.
- Odtwarzanie: PluginChecks — dodać ramkę, ukryć/przywrócić, uruchomić tworzenie/przełączenie pulpitu i równocześnie usunąć plugin.
- Środowisko: Windows/WPF, .NET SDK 10.0.403, baza Git 4910420 z istniejącymi zmianami roboczymi; wykryte podczas implementacji 1.2.0, nie przypisano do wcześniejszego commita.
- Potwierdzona przyczyna: odczyt HostedPanel przed await i brak osobnej serializacji Suspend/Remove umożliwiał dwóm operacjom zwolnienie tego samego CTS.
- Naprawa: LifecycleGate per instancja i ponowna kontrola tożsamości po uzyskaniu blokady; zapis nadal musi zakończyć się przed usunięciem aktywnej instancji.
- Regresja: scenariusz PluginChecks nie przechodził przed poprawką; po poprawce przechodzi, łącznie z ponowną instalacją i odtworzeniem stanu SQLite.

## 2026-11-03T07:20:30+01:00 | v1.2.0 / 4910420 + zmiany robocze | Lista zgłoszeń UI

Środowisko: Windows, WPF/net10.0-windows. Wersja uruchomiona przez użytkownika i dokładny profil: nieznane. Ostatnie commity dotyczą dokumentacji; istotne wcześniejsze implementacje są w zmianach roboczych. Powiązane zgłoszenie: personalizacja i układ z 2026-11-02.

- [x] 1. Motyw z górnego paska: zmieniają się ramki; oczekiwane pełne przełączenie jak w Opcjach. Reprodukcja: użyć przycisku motywu na pasku.
- [x] 2. Wygląd panelu: osobny przycisk w nagłówku; oczekiwane ustawienia wyglądu wewnątrz opcji panelu.
- [x] 3. Kolory: oczekiwane koło kolorów jako podstawowy wybór, wpis wartości schowany opcjonalnie; obecnie systemowy dialog i widoczne pola tekstowe.
- [x] 4. Notatnik: po włączeniu ciemnego motywu tekst pozostaje czarny; oczekiwana czytelność istniejących i nowych notatek. Dokładny payload reprodukujący: nieznany.
- [x] 5. Przenoszenie paneli: drag and drop nie działa; oczekiwane przenoszenie w trybie edycji. Dokładne miejsce rozpoczęcia i upuszczenia: nieznane.
- [x] 6. Wysokość i układ: oczekiwane chwytanie górnej/dolnej krawędzi i wolne miejsce nad/pod panelem, możliwość dodania kolejnego panelu; wszystkie panele użyteczne w proporcjach pionowych i poziomych. Obecny model to pełny binarny podział bez pustych obszarów.

Diagnoza potwierdzona w kodzie dla 1: SettingsViewModel czyści paletę przy zmianie motywu, ThemeService.ApplyAsync zachowuje nadpisania. Pozostałe przyczyny wymagają weryfikacji.

### Wynik punktu 1 — 2026-11-03T07:24:00+01:00
- Zmieniono ThemeService.ApplyAsync: jawny wybór motywu usuwa globalne nadpisania kolorów jak formularz Opcji; nie resetuje fontu ani pozostałych preferencji. Skutek: własna paleta globalna jest resetowana przy takim wyborze, zgodnie z istniejącym zachowaniem Opcji.
- Dodano ToolbarThemeTests sprawdzający reset palety i zachowanie fontu. Test po poprawce PASS; próba przed poprawką nie zakończyła się w ograniczonym środowisku, więc nie deklaruje się potwierdzenia red/green.
- Odbiór w uruchomionym UI i punkty 2–6 nadal niepotwierdzone.

## 2026-11-03T07:41:20.1690202+01:00 | v1.3.0 | Zakończenie zgłoszeń 1–6

- Punkt 1: poprawka z poprzedniego etapu; pasek i Opcje resetują globalną paletę przy jawnym wyborze motywu.
- Punkt 2: wspólne opcje instancji zawierają rozwijaną sekcję Wygląd. Opcje wbudowanych paneli przeniesiono do szablonów wspólnego okna; pluginy zachowują własną komendę opcji.
- Punkt 3: ColorWheel (HSV, jasność, alfa, klawiatura i dostępna nazwa); pole HEX domyślnie schowane. Globalne opcje, wygląd instancji i dialog IColorPickerService używają tej samej kontrolki.
- Punkt 4: XamlPackage materializował neutralny kolor na akapitach i fragmentach. Przy odczycie edytor przywraca dziedziczenie kolorów motywu dla neutralnych kolorów; kolorowe fragmenty, formatowanie i obrazy pozostają. Nie da się rozróżnić ręcznie wybranego czarnego/białego od utrwalonego domyślnego — neutralne kolory podlegają dostosowaniu do motywu.
- Punkt 5: rozszerzono uchwyt na tło nagłówka z wyłączeniem przycisków i edytorów, dodano przechwycenie myszy i obsługę PreviewDragOver/PreviewDrop, aby edytory nie przejmowały przenoszenia paneli. Wklejanie/przeciąganie obrazów oraz instalowanie pluginów pozostaje obsługiwane.
- Punkt 6: uchwyty góra/dół wydzielają puste obszary; istniejąca krawędź zmienia proporcję bez dodawania kolejnego obszaru. Wolne miejsce można wypełnić z menu lub przez przeniesienie panelu. Zapis wykorzystuje wewnętrzny typ builtin.layout-space i istniejący schemat liści, bez migracji danych. Minimum wysokości wbudowanych paneli: 150 DIP; szerokie nagłówki mają jeden wiersz akcji, niska pogoda ma kompaktowy odczyt z rozwijanymi szczegółami.
- Regresje: ToolbarThemeTests, PanelSpaceTests (obie krawędzie, ponowny odczyt, wypełnianie, przenoszenie, niepoprawne rozmiary, ponowienie błędu zapisu), PanelControlsTests (HSV, warunki kompaktowego widoku, kolor i formatowanie notatki).
- Weryfikacja końcowa: build Release PASS, 0 ostrzeżeń/błędów; 285 testów PASS; VisualChecks PASS — 51 renderów, kontrola tekstu notatek i fontów; PluginChecks PASS; publish win-x64 self-contained PASS. Wcześniejsze błędy kompilacji i blokada plików przez własne okno testowe zostały usunięte przed końcową weryfikacją.
- Próby prawdziwą myszą (Computer Use, osobny profil): dolna/górna krawędź notatnika, przeniesienie ToDo w wolne miejsce, dodanie poziomego notatnika, otwarcie opcji/Wyglądu i wybór na kole — wykonane. Nie testowano pełnej bazy użytkownika ani wszystkich zewnętrznych pluginów.
- Build: F:\MyApps\UberDashboard\Release\Dev\v1.3.0_20261103-074019-175


## 2026-11-03T09:39:23+01:00 | v1.4.0, stan roboczy 4910420 | przezroczyste tło

- Moduł: App/Settings, MainWindow, SystemIntegration. Windows, WPF; wersja kompozytora i ustawienia systemu użytkownika nieznane.
- Zgłoszenie: brak możliwości odsłonięcia pulpitu przez tło przy zachowaniu krycia tekstu. Oczekiwane: osobne przełączenie tła bez zmiany krycia treści. Odtwarzanie: otworzyć ustawienia wyglądu i włączyć opcję pokazywania pulpitu.
- Powiązanie: poprzedni wpis z 2026-11-03 i niezacommitowane zmiany tapety; ostatnie commity dotyczą dokumentacji Git.
- Zmiana: kompozycja DWM i przezroczyste tło obszaru okna; osobny przełącznik i krycie samych powierzchni. W razie błędu przewidziano powrót do zwykłego tła; wcześniej suwak zmieniał krycie całego okna.
- Dodano testy zgodności zapisu ustawień i autozapisu. Faktyczna kompozycja DWM, czytelność i działanie w pasku wymagają sprawdzenia na pulpicie Windows.

## 2026-11-03T10:15:00+01:00 | v1.4.0 / 876a2d7 | Widoczność treści przy przezroczystym tle

- Moduły: MainWindow, Settings, DWM; Windows/WPF. Wersja systemu i ustawienia użytkownika: nieznane.
- Zgłoszenie: po pokazaniu pulpitu treści mają pozostać widoczne; ich widoczność ma być regulowana osobno.
- Oczekiwane: przełącznik wpływa na tło, widoczność treści domyślnie 100%. Rzeczywiste zachowanie zgłoszone przez użytkownika: szczegółowa konfiguracja i kroki nieznane.
- Powiązanie: implementacja opcji w 876a2d7, wpis z 09:39; odbiór DWM nie był potwierdzony. W kodzie suwak WindowOpacity wpływał na całe okno, w tym tapetę.
- Zmiana: krycie okna stałe, suwak stosowany do drzewa treści. Ograniczenie: drzewo treści zawiera również powierzchnie paneli, więc zmniejszenie tego suwaka wpływa też na nie; niezależność od tła nie jest pełna. Kompozycja DWM wymaga odbioru w Windows.
- Weryfikacja: 291 testów i 57 renderów WPF PASS; test DWM i pełna niezależność suwaka treści od powierzchni pozostają otwarte.

## 2026-11-03T10:27:45+01:00 | źródła v1.5.0 / 876a2d7 + zmiany robocze | Afirmacje — wpisywanie

- Moduł: App/Features/Affirmations, Controls/AffirmationInput. Środowisko: Windows/WPF; dokładna wersja uruchomionej aplikacji i sposób ustawienia fokusu nieznane.
- Zgłoszenie: po dodaniu afirmacji pierwsza jest widoczna, ale wpisywane znaki nie zmieniają jej wyglądu.
- Oczekiwane: prawidłowo wpisywane znaki stają się widoczne; błędne są ignorowane.
- Odtwarzanie: dodać afirmację w opcjach, wrócić do panelu i pisać wyświetlony tekst. Konkretny tekst i miejsce kliknięcia przed pisaniem nieznane.
- Powiązanie: panel jest nowy w niezacommitowanych źródłach v1.5.0. Poprzednie testy sprawdzały model bezpośrednio i render, bez rzeczywistych naciśnięć klawiatury. Przyczyna do ustalenia.
- Ustalenie: tekst afirmacji nie ustawiał fokusu; pole wejściowe było puste i bez widocznego opisu. Pisanie działało wyłącznie po aktywowaniu tego pola. Nie można potwierdzić, gdzie kliknął użytkownik.
- Poprawka: kliknięcie treści ustawia fokus wejścia, dodano widoczną wskazówkę. Próba klawiaturą w osobnym profilu potwierdziła wpisanie pierwszej afirmacji i przejście do następnej.
- Regresja: VisualChecks sprawdza przypięcie celu fokusu, obsługę kliknięcia oraz zdarzenie wpisania prawidłowego i nieprawidłowego znaku; osobno wpisano pierwszą afirmację klawiszami w uruchomionym widoku testowym. Nie testowano danych użytkownika.
- Weryfikacja końcowa: 291 testów Release PASS, 57 renderów i test wejścia WPF PASS, integracja pluginów PASS, publikacja win-x64 PASS; uruchomiona instalacja użytkownika nie została zastąpiona.

## 2026-11-04T09:50:10+01:00 | źródła 1.8.0 / 876a2d7 + zmiany robocze | afirmacje, zakładki, dialogi i pulpity
- Zgłoszenie: osobny wygląd niewpisanego tekstu afirmacji; usuwanie pustych notatek bez potwierdzenia; komunikaty zgodne z motywem; nazwa panelu i zakładki w jednym wierszu; usuwanie pulpitów z selektora.
- Oczekiwane: niezależne ustawienia, ochrona zawartości i ostatniego pulpitu, spójne komunikaty oraz zachowanie zapisanych danych. Środowisko: Windows, WPF; szczegółowe kroki i komunikaty błędów nie podano.
- Ustalenia: niewpisany tekst korzystał ze wspólnego koloru; oba paski zakładek zawsze żądały potwierdzenia; komunikaty używały systemowego MessageBox, a pasek zakładek był pod nagłówkiem. Selektor nie oferował usuwania pulpitu.
- Zmiany: niezależny kolor i krycie afirmacji, sprawdzanie treści notatek, wspólny komunikat WPF, zakładki w nagłówku i usuwanie pulpitów z ochroną ostatniego.
- Kontrole: 339 testów przeszło po końcowej zmianie; kontrola wizualna motywów i rozmiarów przeszła przed końcową korektą przeciągania dialogu oraz walidacji danych notatki. Interakcje komunikatów i usuwania pulpitu w działającym oknie wymagają odbioru.
