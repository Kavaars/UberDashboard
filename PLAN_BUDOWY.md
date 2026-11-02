# Plan realizacji UberDashboard

## 1. Cel i założenia

UberDashboard ma być lekką, lokalną aplikacją dla Windows 11, która pozwala użytkownikowi tworzyć własne pulpity z paneli: dodawać wiele instancji, ukrywać je, przywracać, usuwać, zamieniać zawartość, zamieniać miejscami, dzielić obszar poziomo lub pionowo i zmieniać proporcje. Wersja 1 zawiera panele ToDo, Notatnik i Pogoda, a kolejne funkcje mogą być dostarczane jako wtyczki.

**Decyzje bazowe:**

- C# i serwisowana linia .NET 10 LTS; przed każdym wydaniem sprawdzić aktualny harmonogram wsparcia i wdrażać dostępne poprawki serwisowe.[2]
- WPF/XAML, MVVM oraz ograniczona warstwa Win32 P/Invoke. WPF odpowiada za interfejs i wiązania, a kod Win32 tylko za funkcje powłoki, których wymaga produkt.[10]
- Modularny monolit. Panele wbudowane i wtyczki korzystają z jednego, wersjonowanego kontraktu. Wtyczki są zaufanym kodem uruchamianym w procesie aplikacji; nie są sandboxem.
- SQLite jako lokalny magazyn. `Microsoft.Data.Sqlite` nie daje rzeczywistego asynchronicznego I/O: metody `Async` wykonują operacje synchronicznie. Wszystkie operacje DB muszą więc trafiać do kolejki wykonywanej poza wątkiem UI; samo `await` nie wystarcza.[12]
- Własny, prosty, binarny silnik drzewa podziałów. Nie wprowadzać zależności od frameworka layoutowego, dopóki testy nie wykażą konkretnej potrzeby. W zwykłym oknie i na pasku panele tworzą przylegające kafle, a nie dowolnie nakładające się okna; w trybie pulpitu powierzchnia ma osobne współrzędne swobodne.
- Tryb paska rezerwującego przestrzeń korzysta z Win32 AppBar API. Tryb pulpitu „jak Rainmeter” jest funkcją najlepszych starań i prototypem z jawnym fallbackiem, nie obietnicą gwarantowanej warstwy pulpitu.
- Dane ToDo i Notatnika działają offline. Pogoda wymaga osobnej zgody na połączenie HTTPS i ręcznego wyboru lokalizacji.
- Nie ustalać wersji pakietów z góry. W fazie startowej wybrać najnowsze stabilne wersje zgodne z `net10.0-windows`, zablokować je w plikach zależności i aktualizować przez jawny przegląd zmian oraz testy.

Wszystkie wartości wydajnościowe i czasowe w tym dokumencie są **celami i estymatami projektowymi, a nie wynikami pomiarów ani obietnicą terminu**. Na tym etapie nie wykonano pomiarów aplikacji.

### Uzasadnienie wyboru technologii

| Wariant | Ocena dla tego projektu |
|---|---|
| **C#/.NET + WPF** | Wybrany: Windows jest jedyną platformą docelową, a WPF udostępnia rozbudowany system układów, wiązań i stylów; osobna warstwa Win32 pozwala integrować okna z powłoką.[10] Wymaga dopracowania nowoczesnego wyglądu i testów przezroczystości oraz DPI. |
| **C# + WinUI 3** | Możliwa alternatywa dla bardziej natywnego, współczesnego wyglądu, ale nie eliminuje ryzyk AppBar ani integracji z pulpitem. Nie ma dostatecznego uzasadnienia, by kosztem zmiany stosu rezygnować z WPF przed prototypem. |
| **Electron lub inna powłoka webowa** | Zaletą jest ekosystem HTML/CSS; ceną jest dodatkowy runtime przeglądarki oraz nadal konieczna integracja z Win32. Dla lokalnego dashboardu, w którym priorytetem są zasoby i integracja z Windows, nie jest to pierwszy wybór. Nie zastępuje to pomiarów wydajności wybranej implementacji. |
| **C++/Win32** | Daje bezpośrednią kontrolę nad platformą, ale zwiększa koszt tworzenia UI i bezpiecznego SDK rozszerzeń; używać bezpośrednich wywołań Win32 tylko w wydzielonym adapterze C#. |

## 2. Zakres wersji 1

### Wersja 1 obejmuje

1. Jedno aktywne środowisko robocze z możliwością tworzenia i wybierania nazwanych pulpitów. W v1 każdy pulpit ma własne instancje paneli i ich dane; zmiana pulpitu nie przenosi danych między nimi.
2. Zwykłe okno pulpitu aplikacji: przenoszone, skalowane, maksymalizowane i minimalizowane standardowymi zachowaniami Windows.
3. Edytor układu: dodawanie, ukrywanie i przywracanie paneli, usuwanie z potwierdzeniem, zamiana miejscami i zamiana zawartości, poziomy/pionowy podział, przeciąganie, klawiaturowa alternatywa dla przeciągania, przeciągane dzielniki, minimum rozmiaru i walidacja drzewa.
4. Tryb paska przy krawędzi ekranu rezerwującego przestrzeń roboczą przez AppBar API. W pierwszym wydaniu jedna konfiguracja paska dla wybranego monitora i krawędzi; wiele monitorów musi być przetestowane i wspierane tylko tam, gdzie powłoka zwraca prawidłową geometrię.
5. Eksperymentalny tryb pulpitu z pozycjonowaniem swobodnym, ikonami paneli i opcjonalnym obrazem tła w powierzchni aplikacji; testy Win+D/Pokaż pulpit, wielu monitorów i restartu Explorera. W razie braku stabilnej obsługi powierzchnia wraca do zwykłego okna.
6. Wbudowane panele ToDo, Notatnik i Pogoda, każde tworzone w wielu instancjach i identyfikowane przez `instanceId`.
7. Motyw jasny, ciemny, systemowy i personalizacja: kolory, czcionka i jej rozmiar, gęstość, tło, przezroczystość oraz nadpisania wyglądu poszczególnych paneli; wybór obrazu lokalnego przez systemowy dialog plików.
8. Lokalną bazę SQLite, transakcyjne zapisy, migracje, tworzenie i odtwarzanie kopii bezpieczeństwa, cache pogody i odporny na błąd odczyt przy starcie.
9. Host wtyczek in-process, manifest, katalog wtyczek, jawne zaufanie, tryb awaryjny oraz dokumentowany SDK i przykładową wtyczkę deweloperską.
10. Diagnostykę do plików `LOGS/*.txt`, globalne toasty i obsługę błędów bez blokowania podstawowego pulpitu.
11. Instalator/aktualizację, które nie umieszczają danych użytkownika w katalogu programu i nie usuwają ich bez osobnej, jawnej decyzji.

### Poza wersją 1

- Wtyczki nieznanych autorów jako bezpieczny kod, sandbox, wymuszane uprawnienia albo gwarantowana izolacja awarii. `AssemblyLoadContext` i `AssemblyDependencyResolver` rozwiązują problemy ładowania zależności, ale nie ograniczają uprawnień kodu; kod wtyczki ma uprawnienia procesu.[3]
- Aktualizacja wtyczki „na gorąco”; zmiana wersji wymaga zamknięcia i ponownego uruchomienia aplikacji.
- Automatyczne wykrywanie lokalizacji, śledzenie GPS, geolokalizacja na podstawie IP, synchronizacja chmurowa, konta użytkownika i współdzielenie pulpitu.
- Komercyjne użycie bez licencji Open-Meteo właściwej dla komercyjnego API. Bezpłatna warstwa Open-Meteo jest przeznaczona do użytku niekomercyjnego.[5]
- Gwarantowane osadzanie okien w warstwie pulpitu Windows, ingerowanie w Explorer, podmiana systemowej tapety, zarządzanie systemowymi ikonami pulpitu lub obejście Pokaż pulpit.
- Automatyczne ukrywanie paska AppBar, zaawansowana synchronizacja konfiguracji między monitorami, ARM64 i rozbudowany marketplace.
- Osobny proces dla wtyczek. Jeżeli będzie wymagany dla niezaufanych rozszerzeń, należy zaprojektować wersjonowane IPC i granice uprawnień jako osobny etap.

## 3. UX i zachowanie interfejsu

### Główne okno

- Pasek górny: nazwa/wybór pulpitu, przełącznik trybu, przycisk `Edytuj układ`, `Dodaj panel`, ustawienia oraz dostęp do diagnostyki.
- Obszar roboczy: aktywne liście drzewa w trybie podziałów albo pozycje swobodne w prototypie pulpitu. To te same instancje i dane: pulpit przechowuje równolegle drzewo podziałów oraz prostokąty dla trybu swobodnego. Przełączenie trybu nie odtwarza paneli z nowych ID ani nie nadpisuje niewidocznego układu.
- Karta panelu: uchwyt tytułu, ikona typu, nazwa, menu działań i czytelny stan ładowania/błędu. Tytuł i ikona są dostępne w UI Automation; dekoracyjne ikony nie dublują nazwy dla czytnika ekranu.
- Dzielniki mają uchwyt, fokus klawiatury, widoczne stany hover/focus oraz możliwość precyzyjnego zmieniania rozmiaru klawiszami. Wartości rozmiarów paneli są przechowywane w DIP, a na granicy interop przeliczane na współrzędne Win32.
- Tryb edycji jest jawny. Poza nim przeciąganie panelu nie zmienia układu; menu panelu pozostaje dostępne. Wyjście z edycji zapisuje finalny, zwalidowany stan albo pokazuje błąd zapisu i umożliwia ponowienie.

### Polecenia panelu

| Polecenie | Skutek | Dane |
|---|---|---|
| Dodaj | Tworzy nową instancję wskazanego typu z nowym `instanceId` i dodaje ją do wybranego miejsca | Nowa, niezależna konfiguracja i stan |
| Ukryj | Usuwa liść z aktywnego drzewa, kompaktuje podziały i zapamiętuje wskazówkę przywrócenia | Zachowuje konfigurację, notatki, zadania i cache |
| Przywróć | Wstawia ukryty panel z powrotem w zapamiętane miejsce, jeżeli nadal jest poprawne | Zachowuje `instanceId` i dane |
| Zamień miejscami | Wymienia dwa liście w układzie bez przenoszenia ich stanu | Każdy stan nadal należy do pierwotnego `instanceId` |
| Zamień zawartość | Wstawia nową instancję w to samo miejsce; dotychczasową instancję ukrywa | Nie kopiuje danych. Poprzednią można przywrócić albo osobno usunąć |
| Usuń instancję | Usuwa instancję, jej dane zarządzane przez aplikację i jej wpis układu | Wymaga osobnego potwierdzenia z podaniem zakresu; nieodwracalne bez kopii |

Potwierdzenie usunięcia ma wymienić wszystkie rodzaje danych, które znikną (np. notatka, zadania, konfiguracja i cache tej instancji). Anulowanie nie zmienia bazy ani układu. Usunięcie nie może być skutkiem samego przeciągnięcia, ukrycia ani zamiany zawartości.

### Dostępność i sprzężenie zwrotne

- Wszystkie akcje edytora dostępne przez klawiaturę: wybór panelu, menu kontekstowe, zmiana kolejności, split horizontal/vertical, zmiana proporcji i przywracanie ukrytych.
- WPF UI Automation z nazwami dostępności, logiczną kolejnością fokusu, widocznym focusem, kontrastem w obu motywach i tekstem błędów niezależnym od koloru.
- Toasty globalne dla nieblokujących zdarzeń: zapisano, pogoda nieaktualna, wtyczka wyłączona, aktualizacja wymaga restartu. Dialogi modalne tylko dla potwierdzenia nieodwracalnej operacji i krytycznego wyboru.
- Operacje trwające dłużej od zwykłej interakcji wykonują się poza UI, pokazują prawdziwy stan/progres, sukces, błąd, ponowienie i anulowanie, jeśli operację można bezpiecznie przerwać. Bez sztucznego progresu i fikcyjnych opóźnień.

## 4. Silnik układu i stan paneli

### Model

- Każdy zapisany pulpit ma wersjonowany dokument układu. Węzeł jest jednym z: `Leaf(instanceId)` albo `Split(axis, ratio, first, second)`, gdzie `axis` jest poziomy lub pionowy. W układzie poziomym pierwszy liść znajduje się po lewej, w pionowym u góry.
- Węzły liści wskazują `instanceId`, nigdy sam typ panelu. W ten sposób dwie instancje tego samego pluginu mogą mieć inne treści i układ.
- `ratio` jest proporcją poddrzew w zakresie wewnętrznym; wyliczony podział jest dodatkowo ograniczany minimalnym rozmiarem wymaganym przez potomków. Układ nie może zapisać NaN, nieskończoności, zduplikowanego węzła ani cyklu. Minima grup obliczać rekurencyjnie: w osi dzielenia zsumować minima dzieci i szerokość dzielnika, w drugiej osi wybrać większe minimum.
- Ukryty panel nie jest aktywnym liściem, ale pozostaje w `PanelInstance` wraz z `RestoreHintJson` (ostatnia ścieżka, rodzic, sąsiad, kolejność i proporcja). Jeśli wskazówka stała się nieaktualna, przywracanie stosuje określony fallback: najbliższy poprawny sąsiad, a następnie wolny liść/root. UI wskazuje, gdzie panel został przywrócony.
- Zamiana miejscami podmienia dwa odwołania do `instanceId`; zamiana zawartości tworzy nowe `instanceId` i nie przenosi danych. Układ zawsze jest walidowany przed zapisem.

### Operacje i niezmienniki

1. `Split(instanceId, axis, newInstanceId, insertSide)` dzieli wybrany liść na dwa i ustawia początkową proporcję; nowa instancja zachowuje własne minimum rozmiaru.
2. `Move(instanceId, target, dropZone)` przenosi liść i buduje nowy split wskazanej orientacji. Samoprzeniesienie i próba umieszczenia w swoim potomku są odrzucane.
3. `Swap(a, b)` zamienia liście, nie rekordy instancji ani ich dane.
4. `Resize(splitId, ratio)` zaciska proporcję na przedziale, który spełnia minima obu poddrzew. Jeżeli obszar roboczy jest mniejszy niż suma minimów, nie obcina po cichu treści: włącza przewijanie/stan przepełnienia i pokazuje wskazówkę; edytor nadal nie zapisuje niewykonalnej proporcji.
5. `Hide(instanceId)` usuwa liść, zapisuje `RestoreHintJson`, a rodzica z jednym dzieckiem zastępuje tym dzieckiem. Pusty pulpit ma jawny stan bez korzenia.
6. `Restore(instanceId)` sprawdza wskazówkę i minima; w razie konfliktu stosuje fallback, nie nadpisuje innej instancji.
7. `Delete(instanceId)` po potwierdzeniu usuwa aktywny liść, kompaktuje drzewo i w jednej transakcji oznacza lub usuwa rekordy należące do instancji; sprzątanie plików instancji następuje osobno i jest ponawiane.
8. `ToggleAxis(splitId)` zmienia oś wybranego splitu, zachowując kolejność dzieci i walidując minima.

### Walidacja i serializacja

- Niepusty układ ma dokładnie jeden korzeń; każdy liść wskazuje istniejący, widoczny rekord panelu; każda widoczna instancja występuje dokładnie raz; ukryte instancje nie występują w drzewie.
- Każdy split ma dokładnie dwoje dzieci, znaną oś, prawidłowy identyfikator i skończoną proporcję. Każdy węzeł jest osiągalny z korzenia.
- Po odczycie dokument przechodzi walidację wersji, rozmiaru, głębokości i spójności ID przed użyciem. Nieznana wersja powoduje zachowanie kopii oryginalnego dokumentu, tryb naprawy/odtworzenia i czytelny komunikat, nie bezwarunkowy nadpis.
- `LayoutSerializer` ma wersję schematu, testy round-trip i migracje. Migracja wykonuje się na kopii w pamięci, waliduje nowy kształt i zapisuje dopiero po poprawnej walidacji.
- Silnik domenowy jest niezależny od WPF i Win32, aby operacje drzewa testować automatycznie jako czyste funkcje.
- W modelu WPF rekursywny widok splitu renderuje `Grid` z dwiema definicjami wierszy albo kolumn i `GridSplitter`; liść renderuje host panelu. Podczas przeciągania pokazywać obszar docelowy i stosować operację domenową dopiero po upuszczeniu; anulowanie nie zapisuje zmian.
- Swobodny tryb pulpitu trzyma dla każdego widocznego `instanceId` prostokąt w DIP względem wybranego monitora oraz kolejność wewnętrzną. Przy pierwszym przejściu wylicza prostokąty z obecnego drzewa, a później zachowuje ostatnie pozycje; po dodaniu nowego panelu nadaje mu niezajętą pozycję lub informuje o braku miejsca. Ukrycie i usunięcie aktualizują obie reprezentacje w jednej transakcji, a przywrócenie używa zachowanego prostokąta albo wskazanego fallbacku. Nie przenosić WPF `UIElement` jednocześnie do dwóch rodziców: najpierw odłączyć widok od starej powierzchni, następnie dołączyć do nowej.
- Dodanie w trybie swobodnym jednocześnie wstawia liść do drzewa: obok wskazanego panelu albo przy korzeniu, gdy cel nie został wskazany. Przesuwanie i zamiana miejsc w trybie swobodnym zmieniają prostokąty, ale nie drzewo; w trybie podziałów zmieniają drzewo, ale nie ostatnie prostokąty. Dane instancji nie są przenoszone między panelami. Nowy lub przywrócony panel dostaje brakującą pozycję w drugiej reprezentacji przed zapisem.

## 5. Trzy tryby wyświetlania

### 5.1. Zwykłe okno

- Standardowe okno WPF z paskiem tytułu i poprawnym zachowaniem minimalizacji, maksymalizacji, przywracania, Alt+Tab i zamknięcia.
- Zamykanie okna może minimalizować do obszaru powiadomień tylko wtedy, gdy taka opcja została włączona; dostępne jest polecenie `Zakończ`, które zamyka host, kończy pracę paneli i zapisuje bazę.
- Bez wymuszania aktywacji/fokusu. Zwykłe okno pozostaje podstawowym, przewidywalnym fallbackiem dla pozostałych trybów.

### 5.2. Pulpit typu Rainmeter — eksperymentalny

- Niezależny adapter `IDesktopSurfaceHost` zarządza jednym obszarem nakładkowym na monitor; pozycje paneli są przechowywane względem wybranego monitora i konwertowane przy zmianie DPI.
- Z-order jest wybierany jawnie: zwykłe okno/poniżej innych/nad zwykłymi oknami tylko w zakresie, na jaki pozwala tryb. Nie utrzymujemy agresywnego `Topmost`, nie walczymy z decyzjami użytkownika i nie przechwytujemy fokusu.
- `Win+D`/Pokaż pulpit, przełączenie zadań, Explorer restart, odłączenie monitora i zmiana tapety są testami zgodności, nie obietnicą widoczności we wszystkich stanach. Dokumentacja Rainmeter pokazuje, że jego skóry mają różne pozycje Z i zachowanie Win+D, ale nie ustanawia to kontraktu Windows dla dowolnej aplikacji.[4]
- `WorkerW` nie jest traktowany jako oficjalne/stabilne API ani wymóg architektury. Jeśli spike pokaże zależność od nieudokumentowanego zachowania Explorera, implementacja pozostaje flagowanym prototypem i po utracie powierzchni wraca do zwykłego okna.
- Restart lub awaria Explorera: wykryć utratę okna/uchwytu, odtworzyć powierzchnię po dostępności powłoki, bez pętli agresywnego tworzenia. Po przekroczeniu limitu prób włączyć fallback i wyświetlić komunikat.
- Przezroczystość wizualna nie oznacza automatycznie click-through. Klikalność tła i obszarów nieinteraktywnych jest osobną opcją, testowaną per region. W edycji okno nigdy nie jest click-through. Stan wejściowy/z-order jest przywracany po wyjściu z trybu; w trybie pulpitu ikona zasobnika udostępnia akcję przywrócenia zwykłego okna. Ponowne uruchomienie skrótu aplikacji przekazuje tę akcję działającej instancji, dzięki czemu ukryta ikona zasobnika nie odcina dostępu.
- Obraz tła jest obrazem powierzchni UberDashboard, nie zmianą tapety Windows. Kafle ikon wskazują typy paneli; w wersji 1 nie ma menedżera skrótów ani uruchamiania dowolnych plików.

### 5.3. Pasek rezerwujący przestrzeń

- Warstwa `IAppBarNativeApi` zawiera wyłącznie potrzebne P/Invoke: `SHAppBarMessage`, rejestrację callbacku i wybrane wywołania `user32` dla okna/monitorów. `AppBarController` jest jedynym właścicielem rejestracji.
- Cykl: `ABM_NEW` z unikalnym callbackiem → wyznaczenie krawędzi i RECT → `ABM_QUERYPOS` → ponowne dopasowanie żądanej grubości do zwróconego RECT → `ABM_SETPOS` → ustawienie rozmiaru/pozycji okna z RECT zwróconego przez system → przy zamknięciu lub zmianie trybu `ABM_REMOVE`. System może zmienić RECT, dlatego aplikacja zawsze stosuje wartość zwróconą po `ABM_SETPOS`, nie samą wartość żądaną.[1]
- Callback obsługuje `ABN_POSCHANGED` (ponowne query/set), `ABN_FULLSCREENAPP` i inne jawnie obsługiwane powiadomienia. Dla `WM_ACTIVATE` wysyła `ABM_ACTIVATE`, a dla `WM_WINDOWPOSCHANGED` — `ABM_WINDOWPOSCHANGED`. Przy `WM_DPICHANGED` i `WM_DISPLAYCHANGE` odświeża monitor i geometrię.[1][13]
- W v1 autohide jest wyłączone/odłożone. Nie implementować częściowej obsługi rejestracji autohide ani nie obiecywać jej współpracy z paskiem systemowym.
- Po powiadomieniu o uruchomieniu pełnego ekranu opuścić AppBar na dół kolejności Z, po jego zakończeniu przywrócić wcześniejszą politykę i ponownie zweryfikować pozycję. Nie podbijać paska nad grę ani nie kraść fokusu. Rejestracja i rezerwacja obszaru pozostają aktywne, dopóki użytkownik nie wyłączy trybu; nie przedstawiać obszaru jako zwolnionego. Aplikacje pełnoekranowe i te, które ignorują standardowy obszar roboczy Windows, nie mają gwarancji pozostawienia miejsca dla paska.[1]
- Monitor wybierany przez użytkownika. Wyliczyć monitory przez `EnumDisplayMonitors`, a dla wybranego `GetMonitorInfo` pobrać `rcMonitor` i `rcWork`; uwzględnić ujemne współrzędne ekranu wirtualnego. Nie używać rozmiaru monitora głównego jako geometrii każdego paska. Po odłączeniu monitora konfiguracja wskazuje fallback i wraca do aktywnego monitora lub zwykłego okna; nie zakładać identycznego działania AppBar na każdym monitorze.[19][20]
- Zadeklarować w manifeście aplikacji `PerMonitorV2`, a zachowanie WPF sprawdzić na wybranej wersji .NET. WPF przechowuje rozmiary w DIP, zaś granica Win32/AppBar pracuje na współrzędnych pikselowych właściwych dla kontekstu DPI. Konwersję i zaokrąglenia scentralizować w adapterze; nie stosować dodatkowego skalowania bez potwierdzenia, że WPF go nie wykonał.
- Konkurencja z paskiem zadań i innymi AppBar jest normalnym przypadkiem. Zmniejszenie obszaru przez powłokę jest jawnie pokazane, a jeżeli nie można otrzymać poprawnego RECT, tryb kończy się bez zajmowania przypadkowego obszaru.
- Obsłużyć zarejestrowany komunikat `TaskbarCreated`: odtworzyć ikonę zasobnika, odświeżyć uchwyty powłoki i defensywnie ponowić rejestrację AppBar, z ograniczonymi próbami. Komunikat jest sygnałem utworzenia paska, nie dowodem, że rejestracja AppBar została automatycznie przywrócona.[21] Operacje wejścia i wyjścia z trybu muszą być idempotentne; nie wykonywać nieskończonej rejestracji w odpowiedzi na własne `ABN_POSCHANGED`. Automatyczny test uruchamia inne zwykłe okno, maksymalizuje je i porównuje jego granice z rzeczywiście zarezerwowanym obszarem, również po ponownej rejestracji.

## 6. Architektura rozwiązania

### Projekty i warstwy

```text
src/
  UberDashboard.App/                 WPF bootstrap, App.xaml, DI, okna, zasobnik
  UberDashboard.Contracts/           publiczne typy i abstrakcje host/plugin
  UberDashboard.Core/                domena, layout tree, walidacja, zdarzenia
  UberDashboard.Application/         use-case'y, koordynacja, interfejsy usług
  UberDashboard.Infrastructure/      SQLite, kolejka DB, pliki, HTTP, logowanie
  UberDashboard.Platform.Windows/   AppBar, DPI, monitor, shell lifecycle, P/Invoke
  Features/
    Todo/                             hostowany moduł i WPF viewmodely
    Notes/
    Weather/
  plugins/                            lokalne katalogi z manifestami i zależnościami
  tests/
    UberDashboard.Core.Tests/
    UberDashboard.Application.Tests/
    UberDashboard.Infrastructure.Tests/
    UberDashboard.Windows.Tests/
    UberDashboard.PluginContract.Tests/
```

- `Core` nie zależy od WPF, SQLite, HTTP ani Win32.
- `Application` zależy od abstrakcji Core/Contracts; Infrastructure i Windows implementują te abstrakcje.
- `App` tworzy Generic Host i kontroluje integrację jego cyklu życia z WPF. Nie uruchamia długiego I/O w `Startup`, zdarzeniach okna ani konwerterach bindingu.
- Feature moduły nie importują prywatnych wnętrz innych feature'ów. Komunikacja jest przez publiczne kontrakty, use-case'y, zdarzenia domenowe lub wspólny interfejs hosta.
- Każdy panel dostaje osobny scope DI i identyfikator instancji. Współdzielone są jedynie usługi hosta takie jak kolejka DB, logowanie i koordynator pogody.

### Biblioteki i komponenty

| Komponent | Rola |
|---|---|
| .NET 10 LTS / WPF | Runtime Windows oraz interfejs XAML/MVVM; używać aktualnych serwisowanych poprawek.[2][10] |
| `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Configuration`, `Microsoft.Extensions.Logging` | Composition root, cykl życia, DI, konfiguracja i abstrakcja logowania |
| `CommunityToolkit.Mvvm` | Observable ViewModel, polecenia i ograniczenie ręcznego kodu wiązań |
| `Microsoft.Data.Sqlite` | Lokalny SQLite, prepared statements, transakcje, WAL i mechanizm online backupu |
| `Serilog` oraz sink plikowy | Strukturalne logi do rotowanych `LOGS/*.txt`, zachowanie pól i retencji |
| `System.Text.Json` | Manifesty, konfiguracje i wersjonowane dokumenty drzewa |
| `HttpClient` / `IHttpClientFactory` | Klient HTTPS pogody, timeout, cancellation i współdzielenie połączeń; cache odpowiedzi i deduplikacja są implementowane w usłudze aplikacji |
| `AssemblyLoadContext` + `AssemblyDependencyResolver` | Ładowanie prywatnych zależności wtyczki i ograniczanie konfliktów wersji; nie izolacja bezpieczeństwa.[3] |
| Win32 P/Invoke | AppBar, monitor, DPI i cykl okien wymagany przez tryby Windows |
| `xunit`, `FsCheck` | Testy jednostkowe, kontraktowe i generowanie przypadków brzegowych drzewa.[18] |
| `FlaUI.Core`, `FlaUI.UIA3` | Automatyzacja interfejsu WPF przez Windows UI Automation; używać w interaktywnej sesji testowej Windows.[17] |
| Inno Setup | Instalator per-user EXE oraz odinstalowanie bez automatycznego usuwania danych.[15] |

Pakiety są zależnościami zewnętrznymi, a ich stabilne wersje będą zapisane i audytowane w repozytorium w momencie wdrożenia. Nie wprowadzać pakietów do samego layoutu, jeśli nie przynoszą konkretnej, przetestowanej korzyści. Dla kompatybilności z dynamicznymi rozszerzeniami publikować v1 jako self-contained `win-x64`, bez trimming i bez Native AOT; runtime wymaga aktualizacji razem z aplikacją. Trimming może usuwać kod potrzebny dynamicznie.[14]

## 7. Kontrakt i bezpieczeństwo wtyczek

### Manifest i dystrybucja

Manifest JSON zawiera: `id` w formacie reverse-DNS, nazwę, wersję wtyczki, wersję kontraktu, minimalną wersję hosta, framework docelowy, assembly/type wejściowy, zależności, nazwę deklarowanego wydawcy, wersję schematu danych, listę obsługiwanych paneli i opisane funkcje. Dokument podpisu wraz z hashami plików jest osobną częścią pakietu, jeśli pakiet jest podpisany; sama nazwa wydawcy w JSON nie dowodzi tożsamości.

Poprawny podpis od zaufanego wydawcy wraz z hashami pozwala sprawdzić pochodzenie i niezmienność pakietu, ale nie bezpieczeństwo jego kodu; sam hash nie potwierdza tożsamości. Deklarowane capability są informacją i elementem zgody użytkownika, a nie mechanizmem wymuszania uprawnień. Instalacja/aktywacja wtyczki wymaga jawnego potwierdzenia z nazwą wydawcy, lokalizacją i ostrzeżeniem, że kod działa z uprawnieniami użytkownika.

### API hosta

- `IPluginModule.InitializeAsync(IPluginHostContext, CancellationToken)` rejestruje metadane bez blokowania Dispatcher.
- `IPanelDescriptor` deklaruje typ, nazwę, ikonę, minimalny rozmiar, wersję stanu i dostępną fabrykę.
- `IPanelFactory.CreateAsync(PanelContext, CancellationToken)` tworzy obiekt panelu dla przekazanego `instanceId`. Host nadaje nowy ID tylko przy dodawaniu instancji; przy restarcie lub ponownym wyświetleniu przekazuje istniejący ID.
- `IPanelInstance` zapewnia inicjalizację, odtworzenie i zapis stanu, pauzowanie/wznowienie, przygotowanie do usunięcia oraz `DisposeAsync`. Widok WPF i elementy UI tworzone są na STA/Dispatcherze; operacje sieciowe/pliki/liczenia odbywają się poza nim.
- `PanelContext` udostępnia tylko kontrakty hosta: `instanceId`, token zamknięcia, logowanie, powiadomienia, lokalny, namespacowany magazyn instancji i potrzebne use-case'y. Nie udostępnia surowego połączenia DB.
- Host opakowuje wywołania asynchroniczne w cancellation token; przy zamknięciu, ukryciu lub usunięciu sygnalizuje anulowanie, czeka na bezpieczne zakończenie w ograniczonym czasie, a wynik raportuje.
- Dane instancji są przypisane do `instanceId`; nie ma globalnego stanu panelu, który przypadkowo scala dwie instancje.

### Ładowanie, awarie i aktualizacje

- Katalog każdej wtyczki jest ograniczony do własnego katalogu; `AssemblyDependencyResolver` rozwiązuje jej zależności. Dzielone kontrakty/API hosta są ładowane z kontekstu domyślnego.
- Wtyczki są ładowane dopiero po walidacji manifestu i zgodności API, sprawdzeniu podpisu, jeżeli jest obecny, oraz jawnej akceptacji zaufania. Odrzucać nieprawidłowy podpis; paczka bez podpisu może zostać włączona dopiero po wyraźnym ostrzeżeniu „niezweryfikowany wydawca”. Sam hash dostarczony wewnątrz tej samej paczki nie potwierdza tożsamości. Weryfikować podpis całej paczki albo podpisany manifest zawierający hashe każdego pliku oraz łańcuch zaufania wydawcy; pokazać wynik przed uruchomieniem kodu.
- Błąd manifestu, inicjalizacji, viewmodelu lub cyklu życia powoduje izolowane wyłączenie panelu/wtyczki, log bez sekretów i możliwość kontynuowania pulpitu. Nie obiecywać izolacji awarii procesu: wadliwa wtyczka in-process może zawiesić albo zakończyć host.
- Tryb awaryjny uruchamia wyłącznie host i moduły wbudowane; wtyczki zewnętrzne są wyłączone do czasu ręcznego wznowienia. Po nieczystym poprzednim starcie podczas ładowania rozszerzeń aplikacja proponuje safe mode.
- W v1 brak hot-update. Nowe pliki są przygotowywane obok aktywnej wersji; aktualizacja wchodzi po zamknięciu hosta i restarcie. Nie polegać na unload `AssemblyLoadContext` jako gwarancji zwolnienia WPF view, eventów ani wszystkich uchwytów.
- Jeśli plugin jest nieobecny, wyłączony albo niezgodny, zachować `instanceId`, stan i miejsce w układzie; zamiast panelu pokazać kartę zastępczą z identyfikatorem rozszerzenia i akcjami instalacji, włączenia lub ukrycia. Po przywróceniu zgodnej wtyczki spróbować odtworzyć tę samą instancję. Brak pluginu nie może kasować danych ani automatycznie uruchamiać obcego kodu.
- Usługa plikowa hosta przechowuje dane instancji w `%LOCALAPPDATA%\UberDashboard\PluginData\<pluginId>\<instanceId>\`, nie zwraca API do usuwania dowolnej ścieżki i udostępnia pliki użytkownika przez dialog. To ograniczenie kontraktu hosta, a nie zabezpieczenie: wtyczka in-process może ominąć usługę i korzystać bezpośrednio z plików oraz sieci z uprawnieniami użytkownika.
- Przy usuwaniu instancji: transakcja usuwa stan hostowany w bazie; cleanup namespacowanych plików jest osobnym zadaniem. Jeśli wtyczka/plik jest niedostępny, operacja zostaje oznaczona do ponowienia i użytkownik dostaje informację o pozostałych danych. Nie kasować ścieżek poza katalogiem instancji.

### Format pakietu i zgodność

- Pakiet `.zip` zawiera `manifest.json`, bibliotekę wejściową `.dll`, plik `.deps.json`, prywatne zależności i opcjonalne zasoby. Przykład manifestu:

```json
{
  "id": "pl.example.clock",
  "name": "Zegar",
  "publisher": "Example",
  "version": "1.0.0",
  "apiVersion": "1.0.0",
  "minHostVersion": "1.0.0",
  "targetFramework": "net10.0-windows",
  "entryAssembly": "Clock.Plugin.dll",
  "entryType": "Example.Clock.ClockModule",
  "dataSchemaVersion": 1,
  "dependencies": [],
  "panels": [{ "typeId": "clock", "name": "Zegar" }],
  "capabilities": ["local-storage"]
}
```

- Paczka ma ograniczenie liczby i łącznego rozmiaru plików; przed rozpakowaniem odrzucić ścieżki bezwzględne, `..`, linki, powtórzone nazwy i pliki wychodzące poza staging. Po sprawdzeniu i zgodzie przenieść do katalogu wersji wtyczki. Właściwy plik wejściowy musi być zwykłym plikiem wewnątrz paczki.
- `id` jest unikalne i niezmienne, `typeId` jest unikalny w obrębie pluginu. Zgodność: taki sam numer major API, deklarowany minor nie większy niż minor hosta oraz host nie starszy niż `minHostVersion`; zmiana łamiąca kontrakt zwiększa major. Niezgodny pakiet nie uruchamia kodu. Testowe paczki SDK muszą przechodzić macierz wersji i migrację stanu z poprzedniej obsługiwanej wersji.

## 8. Funkcje wbudowane

### ToDo

- Każda instancja ToDo ma własną listę i rekordy. Funkcje v1: dodaj, edytuj, ukończ/cofnij ukończenie, zmień kolejność, usuń pojedynczy element oraz ustaw opcjonalny termin.
- Automatyczny zapis zmian jest debounced, ale nie opiera się tylko na zamykaniu okna. Każdy batch zapisu jest krótką transakcją kolejki DB; UI pokazuje stan zapisu/błąd i opcję ponowienia.
- Brak sieci i logowanie do usługi zewnętrznej nie są wymagane. Usunięcie panelu ToDo po potwierdzeniu usuwa jego listę i zadania; ukrycie zachowuje je.

### Notatnik

- Każda instancja ma niezależny dokument prostego tekstu z tytułem, zawartością i wersją treści; v1 nie używa zdalnego edytora ani synchronizacji.
- Zapis jest transakcyjny i wykonywany przez kolejkę DB; zmiany w UI mają oznaczenie „niezapisane” do czasu potwierdzenia zapisu. Debounce nie może powodować cichej utraty treści przy zamknięciu.
- Eksport/import `.txt`/`.md` odbywa się wyłącznie przez jawny dialog plików. Eksport do istniejącego pliku wymaga potwierdzenia nadpisania; import pokazuje podgląd/ostrzeżenie o zastąpieniu dokumentu. Nie zapisujemy ścieżek spoza zatwierdzonego dialogu ani nie otwieramy ich automatycznie.
- Rozmiar pojedynczej notatki ma projektowy limit walidowany przed zapisem; dokładna wartość jest zatwierdzana w spike, a UI pokazuje błąd bez obcinania treści.

### Pogoda

- Użytkownik wybiera lokalizację ręcznie: wpisuje nazwę/kod, wybiera wynik geokodowania i zatwierdza lokalizację. Geokodowanie korzysta z Open-Meteo Geocoding API; bez zgody nie wysyłać zapytania ani nie pobierać lokalizacji systemowej.[8]
- Po zgodzie wyszukiwanie wysyła `GET https://geocoding-api.open-meteo.com/v1/search` z parametrami `name`, `language=pl` i `count`; UI prezentuje miejscowość, region i kraj do świadomego wyboru. Prognoza korzysta z `GET https://api.open-meteo.com/v1/forecast` z wybraną parą `latitude`/`longitude` i `current=temperature_2m,weather_code,wind_speed_10m,is_day`. Parametry budować przez bezpieczne kodowanie URL, a jednostki i czas odczytać z odpowiedzi.[7][8]
- Po osobnej, jawnej zgodzie klient wykonuje żądania HTTPS do endpointów forecast/geocoding. Wyjaśnić, że dostawca otrzymuje wyszukiwaną nazwę lub wybrane współrzędne i adres IP. Na darmowej warstwie Open-Meteo obsługiwane jest wyłącznie zastosowanie niekomercyjne; przed dystrybucją komercyjną wymagane są odpowiednia licencja oraz osobna weryfikacja uprawnień dla geokodowania.[5]
- Pobierane są tylko używane zmienne. API rozróżnia zmienne bieżące, godzinowe i dobowe; kody zjawisk dekoduje się wg tabeli WMO API, a nie przez nieudokumentowane mapowanie tekstu.[7]
- Interfejs opisuje dane jako **bieżące warunki według modelu**, nie jako oficjalny pomiar stacji; aktualne warunki Open-Meteo są oparte o dane/model pogodowy.[7]
- Każdy wynik pokazuje lokalizację, jednostki, czas danych, czas pobrania, stan świeżości, źródło oraz link do Open-Meteo obok danych. Dla danych pogodowych ogólna strona licencji wskazuje CC BY 4.0: podać link do licencji i informację o zmianach.[9] Geokoder wykorzystuje dane GeoNames; w selektorze lokalizacji wskazać GeoNames i link do źródła.[22][23]
- **Bramka licencyjna geokodowania:** README projektu geokodera wskazuje CC BY-NC 4.0, natomiast ogólna strona Open-Meteo opisuje CC BY 4.0. Nie rozstrzygać tej rozbieżności na podstawie założenia, że płatny pakiet pogodowy wystarcza; przed komercyjną dystrybucją uzyskać potwierdzenie dla konkretnego endpointu i danych albo wybrać innego dostawcę geokodowania.[9][22]
- Cache jest lokalny, współdzielony dla tych samych współrzędnych/zmiennych, a żądania identyczne są łączone. Projektowy interwał automatycznego odświeżania domyślnie 30 minut i nie częściej niż po ręcznym wywołaniu zgodnie z limitem aplikacji; nie pobierać dla ukrytego panelu, jeśli użytkownik nie włączył tej opcji.
- Przy braku sieci pokazuje ostatni cache i jego wiek; przy braku cache pokazuje stan pusty z ponowieniem. Obsłużyć timeout, DNS, TLS, HTTP 4xx/5xx, 429 z respektowaniem `Retry-After` (jeśli jest) i ograniczonym exponential backoff, anulowanie oraz odpowiedź o niepoprawnej lokalizacji.
- Zapytanie nie zapisuje pełnego adresu URL z parametrami lokalizacji do logów. Dane lokalizacji nie są wysyłane do innych usług ani używane do telemetrii.

## 9. SQLite, transakcje i kopie bezpieczeństwa

### Schemat logiczny

Wszystkie identyfikatory instancji są stabilnymi UUID/GUID jako tekst. Daty zapisujemy w UTC w jednoznacznym formacie. Klucze obce i ograniczenia walidują spójność; usuwanie instancji i należących danych jest jawnie kontrolowane.

| Tabela | Kluczowe pola i relacje |
|---|---|
| `SchemaMigration` | `Version` PK, `AppliedAtUtc`, identyfikator migracji |
| `Dashboard` | `DashboardId` PK, nazwa, `CreatedAtUtc`, `UpdatedAtUtc`, aktywność |
| `DashboardLayout` | `DashboardId` PK/FK, `SchemaVersion`, `TreeJson`, ustawienia trybu/monitora, `UpdatedAtUtc` |
| `DesktopPlacement` | `DashboardId` i `InstanceId` PK/FK, `MonitorId`, `X`, `Y`, `Width`, `Height` w DIP, `ZOrder`; pozycja zachowywana także po ukryciu |
| `PanelInstance` | `InstanceId` PK, `DashboardId` FK, `PluginId`, nazwa, `IsHidden`, `ConfigJson`, `StateJson`, `RestoreHintJson`, wersja danych, znaczniki czasu |
| `TodoItem` | `TodoId` PK, `InstanceId` FK, tytuł, szczegóły, ukończenie, kolejność, termin nullable, utworzenie/aktualizacja/ukończenie |
| `NoteDocument` | `InstanceId` PK/FK, tytuł, tekst, revision, znaczniki czasu |
| `WeatherConfig` | `InstanceId` PK/FK, nazwa, szerokość/długość, strefa, jednostki, czas zgody, interwał |
| `WeatherCache` | `CacheKey` PK, lokalizacja/zmienne, `PayloadJson`, `FetchedAtUtc`, `ValidUntilUtc`, dane źródła/modelu |
| `AppSetting` | klucz PK, `ValueJson`, `UpdatedAtUtc`; motyw, preferencje i zachowanie hosta |
| `PendingCleanup` | identyfikator zadania, plugin/instance, bezpieczny katalog względny, stan/ostatni błąd/ponowienia |

Wersjonowany JSON layoutu służy wyłącznie strukturze drzewa i trybu. Stan właściwy panelu jest przechowywany niezależnie i kluczowany po `InstanceId`, aby migrować oraz usuwać dane per instancja. Nie umieszczać treści notatek/zadań w jednej nieindeksowanej kolumnie JSON całego pulpitu.

### Kolejka DB i wątki

- Jedna usługa `DatabaseWorkQueue` ma dedykowany wątek/worker poza UI i serializuje operacje zapisujące. Repozytoria udostępniają `Task` jako kontrakt dla UI, ale wywołania `Microsoft.Data.Sqlite` wykonuje worker synchronicznie. Nie wywoływać `ExecuteNonQueryAsync`, `OpenAsync` itd. bezpośrednio z UI i nie uznawać `await` za przeniesienie pracy w tło.[12]
- Polecenia kolejki są krótkie i atomowe: otwarcie/ponowne użycie kontrolowanego połączenia, prepared statement, transakcja, commit/rollback. Nie trzymać transakcji podczas interakcji użytkownika, HTTP, dekodowania obrazu ani callbacku pluginu.
- Wszystkie zapisy (autozapis, layout, migracje, preferencje, cache) używają jednej ścieżki zapisu. Dłuższe odczyty również trafiają do pracy poza UI; ewentualne odczyty współbieżne wprowadzamy dopiero po testach i z osobnymi połączeniami.
- Włączenie WAL, `foreign_keys`, timeout/busy timeout i strategia checkpointu są jawne na połączeniu. Sprawdzić zachowanie checkpointu i backupu w testach; nie interpretować WAL jako pełnej asynchroniczności SQLite.
- Kolejka zwraca wynik/błąd poprzez `TaskCompletionSource` z `RunContinuationsAsynchronously`. Cancellation może usunąć nieuruchomione zadanie; nie przerywa natywnej operacji DB w połowie. Błędy są mapowane do stanu UI z retry tylko dla operacji idempotentnych.
- Nie wykonuje się dyskowego zapisu, synchronizacji folderu ani długiego parsowania na Dispatcherze. UI może pokazywać „zapisywanie”; zamknięcie czeka ograniczony czas na flush i informuje o niepowodzeniu zamiast po cichu zamykać z utratą zmian.

### Migracje, odzyskiwanie i backup

- Migracje mają numer, test z poprzedniego schematu i atomowy zapis numeru dopiero po udanym wykonaniu. Przed niekompatybilną migracją tworzyć kopię przez SQLite Backup API, a nie przez kopiowanie plików DB.
- `SqliteConnection.BackupDatabase` działa synchronicznie, może blokować inne połączenia przed zapisem i powinien wykonywać się poza UI. Backup wchodzi jako zadanie ekskluzywne kolejki w spokojnym momencie; nowe zapisy są na ten czas kolejkowane. UI pokazuje „tworzenie kopii — zapisy chwilowo w kolejce”, możliwość odłożenia lub anulowania przed startem oraz komunikat po zakończeniu.[11]
- Nie kopiować aktywnego pliku SQLite ani plików `-wal`/`-shm` przez `File.Copy` jako kopii bezpieczeństwa. Backup używa online backupu SQLite do pliku tymczasowego, zamyka/flushuje połączenie docelowe, wykonuje kontrolę integralności i dopiero wtedy promuje plik do nazwy z datą.
- Retencja kopii jest konfigurowalna; usuwane są tylko kopie utworzone przez aplikację, po sprawdzeniu wieku/liczby i bez naruszania danych źródłowych.
- Odtwarzanie: potwierdzenie destrukcyjnego zastąpienia aktualnego stanu, wstrzymanie operacji, zachowanie bieżącej kopii awaryjnej, odtworzenie wybranego backupu do stagingu za pomocą SQLite API, `integrity_check`, zamknięcie wszystkich połączeń, kontrolowana podmiana bazy i ponowne otwarcie. Przy błędzie zostaje zachowana baza bieżąca i możliwy rollback.
- Przy niepoprawnym pliku nie nadpisywać go startową pustą bazą. Wyświetlić opcje: ponów odczyt, przywróć kopię, eksport diagnostyczny bez treści notatek lub uruchom pusty profil zachowując oryginalne pliki do ręcznego odzyskania.

## 10. Personalizacja, pliki i zasoby

- Tokeny stylu (kolory, typografia, promienie, odstępy, stan focus) zdefiniowane w resource dictionaries; jasny/ciemny/systemowy i motyw użytkownika bazujący na dozwolonych tokenach, nie na arbitralnym XAML.
- Ustawienia transparentności wpływają na wygląd, nie automatycznie na hit testing. Click-through wyłącznie jako jawna funkcja powierzchni pulpitu; w trybie edycji wyłączone. Przy wyjściu przywrócić style i hit testing, nawet po błędzie.
- Użytkownik wybiera rodzinę dostępnej czcionki, rozmiar, kolory tekstu i tła, akcent, obramowanie oraz przezroczystość globalnie lub dla konkretnego panelu. Kolejność rozstrzygania: ustawienie panelu → motyw pulpitu → motyw systemowy/domyślny. Brakująca czcionka lub obraz zastępowane są czytelnym wariantem domyślnym bez utraty zapisanego wyboru; przywrócenie domyślnego stylu nie usuwa danych panelu.
- Obraz wybierany przez dialog plików, filtrowany do obsługiwanych typów. Import kopiuje zasób do katalogu aplikacji (po walidacji ścieżki, rozmiaru i typu) albo zapisuje zatwierdzoną ścieżkę w sposób jawny; preferowany wariant to kopia do `Assets` z nowym GUID, by uniknąć zniknięcia pliku źródłowego. Ograniczyć maksymalny rozmiar i wymiary według jawnych limitów projektowych, dekodować poza UI i używać cache bitmap.
- Usuwanie assetu tylko wtedy, gdy nie wskazuje go żaden profil; błędne/nieobsługiwane obrazy pokazują błąd i zachowują poprzednie tło.
- Wszystkie eksporty odbywają się do ścieżki wybranej przez użytkownika. Walidować rozszerzenie, długość i znaki ścieżek; nie używać treści importu jako nazwy pliku bez sanitizacji. Nie uruchamiać importowanych plików.
- Użytkownik może włączyć ograniczoną przezroczystość i wybrać akcent; testy kontrastu obejmują przezroczyste tło. Przywrócenie UI po click-through, awarii hosta albo restartowaniu Explorera jest kryterium akceptacyjnym.

## 11. Pliki, logowanie i instalacja

### Ścieżki

- Program: katalog instalacyjny per użytkownik, np. `%LOCALAPPDATA%\Programs\UberDashboard\` (alternatywnie podpisany instalator dla wszystkich użytkowników, jeżeli będzie wymagany). Katalog programu jest tylko do odczytu dla aplikacji po instalacji.
- Dane: `%LOCALAPPDATA%\UberDashboard\Data\dashboard.db`.
- Logi: `%LOCALAPPDATA%\UberDashboard\LOGS\app-YYYYMMDD.txt` wraz z rotacją.
- Kopie: `%LOCALAPPDATA%\UberDashboard\Backups\`.
- Pakiety wtyczek: `%LOCALAPPDATA%\UberDashboard\Plugins\<pluginId>\<version>\`; dane wtyczek: `%LOCALAPPDATA%\UberDashboard\PluginData\<pluginId>\<instanceId>\`.
- Assety: `%LOCALAPPDATA%\UberDashboard\Assets\`.
- Ustawienia pierwszego uruchomienia/aktualizacji przechowywane oddzielnie od plików binarnych, aby aktualizacja i reinstalacja nie usuwały profili.

### Logi i diagnostyka

- Każdy wpis ma timestamp ISO 8601, `module`, poziom `DEBUG/INFO/WARN/ERROR`, `operationId` i właściwości strukturalne. Pliki mają rozszerzenie `.txt`; format wiersza jest parsowalny i zawiera pola, a nie tylko niestrukturalny tekst.
- Redagować tokeny, nagłówki, pełne URI, treści notatek, zadania, lokalizację użytkownika i inne dane wrażliwe. Logować identyfikatory operacji/typy błędów zamiast treści użytkownika.
- Konfigurowalna retencja i rotacja; sprzątanie ograniczone do aplikacyjnego katalogu `LOGS`, bez kasowania kopii, danych lub plików użytkownika. Udostępnić eksport diagnostyczny bez danych użytkownika.
- Globalny toast dla zwykłego feedbacku; okno logów na żywo nie jest warunkiem v1. Jeżeli zostanie dodane, ma być niemodalne, przenoszone, skalowalne, filtrowane poziomem/modułem i regexem, z auto-scroll, wyczyść i eksport JSON.

### Instalacja i aktualizacje

- Wariant dystrybucji v1: podpisany instalator EXE budowany przez Inno Setup w trybie per-user, bez wymogu administratora. Binaria i dane użytkownika są rozdzielone; wynik spike instalacji/aktualizacji może uzasadnić zmianę tej decyzji. Autorzy Inno Setup proszą użytkowników komercyjnych o zakup licencji, zaznaczając, że nie jest to bezwzględnie wymagane; przed wydaniem sprawdzić aktualne warunki.[15][16]
- Pierwsza instalacja tworzy katalogi użytkownika z ograniczeniami systemu plików; aplikacja nie zapisuje bazy do katalogu instalacyjnego.
- Aktualizacja weryfikuje podpis/paczki, pobiera do stagingu, zamyka host, tworzy kopię przed migracją, stosuje update, wykonuje kontrolę startową i umożliwia rollback. Jeśli po migracji baza nie przechodzi kontroli, zachowuje kopię i nie usuwa oryginału.
- Aktualizacja hosta lub wtyczki wymaga restartu; użytkownik ma jasny komunikat. Odinstalowanie nie usuwa bazy, kopii ani wtyczek bez osobnego, wyraźnego wyboru.

## 12. Etapy realizacji i bramki

Estymaty w roboczotygodniach jednej osoby są wstępnym budżetem planistycznym do aktualizacji po spike'ach; nie stanowią deklaracji terminu. Testy wydajnościowe nie są wykonane.

| Etap | Zakres i wynik | Bramka akceptacyjna | Estymata projektowa |
|---|---|---|---:|
| 0. Spike i decyzje techniczne | Prototyp WPF/.NET 10, test przeliczeń DPI, AppBar na różnych krawędziach, Win+D/Explorer, kolejka SQLite, backup/restore; ADR dla niepewnych zachowań | Wyniki zapisane wraz z ograniczeniami; desktop mode pozostaje opt-in/fallback, a ryzyko AppBar na monitorach dodatkowych jest znane | 1–2 RW |
| 1. Fundament | Repo/projekty, DI, MVVM, błędy globalne, ścieżki danych, logi, safe mode, CI i build unattended | Build czysty na Windows 11; dane/logi pod właściwymi ścieżkami; testy CI bez interakcji | 2–3 RW |
| 2. Domena układu | Drzewo, walidacja, podziały, DnD/Move/Swap/Hide/Restore/Resize, serializer i migracje | Testy jednostkowe i property-based potwierdzają niezmienniki, round-trip i kompaktowanie | 3–4 RW |
| 3. Powłoka i UX | Główne okno, tryb edycji, accessibility, theme, selektor trybu, toast, odzyskiwanie okna | Klawiatura, Narrator/UIA i rozmiary okna; tryb zwykły jako działający fallback | 3–5 RW |
| 4. Windows modes | AppBar, monitor/DPI, pełny ekran, lifecycle i prototyp pulpitu, restarty Explorera | Testy rzeczywiste Windows 11; poprawny zwrócony RECT albo jawny fallback; brak kradzieży fokusu | 2–3 RW |
| 5. SDK i katalog wtyczek | Kontrakt, manifest, resolver, trust flow, katalog, obsługa błędów, przykładowy plugin i safe mode | Plugin zgodny tworzy osobne instancje; błędny manifest/wyjątek nie niszczy innych paneli; brak obietnicy sandboxa | 3–4 RW |
| 6. Panele i dane | ToDo, Notatnik, consent/cache pogody, schemat DB, kolejka, WAL, migracje, backup/restore | Offline ToDo/Notatnik; brak requestu pogody przed zgodą; baza odzyskiwalna i backup/restore zweryfikowany | 4–6 RW |
| 7. Stabilizacja i dystrybucja | Testy integracyjne/akceptacyjne, log rotation, accessibility, installer/update/rollback, dokumentacja SDK | Wszystkie kryteria v1 poniżej spełnione, pakiet podpisany, upgrade nie narusza danych | 2–3 RW |

### Kolejność i zależności

- Etap 0 poprzedza decyzję, czy tryb pulpitu jest włączany w buildzie produkcyjnym, oraz ustala obsługiwane kombinacje AppBar/monitor/DPI.
- Etap 2 można rozwijać przed pełnym UI; jego czysty model ułatwia testy.
- Etap 5 musi udostępnić kontrakt przed ukończeniem ToDo/Notatnika/Pogody, ponieważ wszystkie trzy panele mają korzystać z tych samych abstrakcji i `instanceId`.
- Etap 6 nie może zakładać asynchronicznego SQLite na podstawie nazw metod; testy kolejki i blokującego backupu są wymagane przed UI paneli.
- Etap 7 jest bramką wydania, nie opcjonalną listą późniejszych usprawnień.

## 13. Testy i weryfikacja

| Obszar | Testy wymagane | Kryterium akceptacji |
|---|---|---|
| Silnik drzewa | Jednostkowe operacje; losowe poprawne drzewa; split/resize ze skrajnymi proporcjami; przeniesienie do potomka; duplikaty; pusty układ; kompaktowanie po hide/delete; serializacja/migracja | Brak cykli, duplikatów liści i referencji do nieistniejących instancji; minimum rozmiarów respektowane albo stan overflow komunikowany; round-trip zachowuje kolejność i proporcje |
| Minima i layout | Różne minima pluginów, zbyt małe okno, DPI mix, dynamiczna zmiana rozmiaru, brak monitora | Brak nakładania i cichego ucinania; przewijanie/komunikat; niepoprawny split nie jest zapisany |
| Wieloinstancyjność | Dwie instancje każdego typu; identyczny typ w różnych miejscach; zamiana; hide/restore; usunięcie jednej instancji | Stan zawsze kluczowany po `instanceId`; operacja na jednej instancji nie zmienia danych drugiej |
| SQLite | Testy transakcji commit/rollback, kolejki zapisów, konkurencyjnych UI events, WAL, timeout, migracji z każdej wersji wspieranej, przerwanego zapisu | Operacje synchroniczne odbywają się poza Dispatcherem; brak utraconych zapisów przy kolejce; każda migracja albo przechodzi w całości, albo zostawia poprzednią bazę |
| Backup/restore | Backup przy obciążeniu, zapis oczekujący w kolejce, nieprawidłowy staging, uszkodzona kopia, odtworzenie i rollback | UI informuje o czasowym kolejkowaniu zapisów; brak raw copy aktywnej bazy/WAL; integrity check przechodzi przed promocją; błąd nie niszczy bieżącej bazy |
| Pliki danych | Brak uprawnień, brak miejsca, usunięcie assetu używanego, złośliwe ścieżki, eksport nadpisujący, awaria cleanup | Tylko aplikacyjne ścieżki sprzątane automatycznie; plik użytkownika nie jest nadpisany bez potwierdzenia; błędy bez cichej utraty |
| Wtyczki | Wersja kontraktu niezgodna, brak zależności, konflikty prywatnych bibliotek, plugin throwing/blocking, anulowanie, safe mode, zaufanie, ścieżka traversal | Nieznany plugin nie uruchamia się przed zgodą; safe mode wyłącza pluginy zewnętrzne; odmowa lub przechwycony wyjątek nie niszczy bazowych paneli. Zawieszenie albo awaria procesu wskutek kodu in-process mogą uniemożliwić pracę; zaufanie nie jest sandboxem |
| AppBar | Krawędzie góra/dół/lewo/prawo; maksymalizacja innego zwykłego okna; konflikt z taskbarem i innym paskiem; ABN_POSCHANGED; pełny ekran; restart Explorera; aktywacja; WM_WINDOWPOSCHANGED; odłączony monitor; DPI 100–200%; ujemne współrzędne | Drugie, standardowo maksymalizowane okno respektuje zarezerwowany obszar w testowanym środowisku. Stosowany jest RECT zwrócony po `ABM_SETPOS`; `ABM_REMOVE` przy wyjściu; brak kradzieży fokusu; fallback, jeśli system nie przyznaje poprawnej pozycji |
| Pulpit | Win+D, Pokaż pulpit, Alt+Tab, Explorer restart, Windows restart, 1/2/3 monitorów, pozycja względem DPI, click-through włącz/wyłącz, odzyskanie fokusu | Tryb jest jawnie oznaczony jako eksperymentalny; w razie utraty powierzchni użytkownik może odzyskać zwykłe okno; tapeta systemu nie jest zmieniana; brak agresywnego topmost/focus-steal |
| ToDo/Notatnik | Offline, autosave, szybkie edycje, zamknięcie natychmiast po edycji, restart awaryjny, usunięcie i przywrócenie panelu | Brak utraty treści potwierdzonych jako zapisane; ukrycie zachowuje dane; usunięcie wymaga potwierdzenia i usuwa tylko wskazaną instancję |
| Pogoda | Mock HTTP: poprawna odpowiedź, błąd JSON, timeout, TLS, 429, 5xx, brak sieci, stale cache, brak zgody, błędne współrzędne, wielokrotne instancje | Brak żądań przed zgodą; HTTPS; cache i czas źródłowy pokazane; atrybucja i link obecne; brak fałszywej etykiety pomiaru stacyjnego; request dedup/backoff działa |
| UI/accessibility | Keyboard-only, UI Automation/Narrator, focus order, kontrast jasny/ciemny/przezroczysty, skalowanie tekstu, menu/toasty | Każda akcja ma nazwę dostępną i klawiaturowy odpowiednik; brak utraconego fokusu; komunikaty nie opierają się wyłącznie na kolorze |
| Instalacja/aktualizacja | Instalacja czysta per-user, upgrade danych, nieudana aktualizacja, rollback, odinstalowanie z danymi i bez nich | Update nie zapisuje w program files ani nie usuwa danych; awaria rollback zachowuje backup; usunięcie danych wymaga wyraźnego wyboru |
| Logowanie | Rotacja, retencja, redakcja, błąd zapisu logu, eksport | ISO timestamp/module/severity/payload; `.txt`; brak treści notatek/lokalizacji/tokenów; rotacja nie usuwa niczego poza LOGS |

- Testy jednostkowe Core i logiki biznesowej powinny dawać co najmniej 75% sensownego pokrycia nowego/zmienionego kodu, gdy praktyczne; kryterium nie zastępuje testów brzegowych i integracyjnych.
- `xunit` uruchamia testy domeny, migracji i infrastruktury; `FsCheck` generuje drzewa oraz sekwencje operacji z zapisywanym seedem w razie błędu. `FlaUI` sprawdza scenariusze okien WPF w interaktywnej sesji Windows. Testy zależne od Explorera i monitorów uruchamiać na dedykowanym runnerze z pulpitem, nie zakładać, że zwykła bezgłowa maszyna CI oferuje rzeczywisty shell.
- Testy muszą działać unattended. Dialogi w testach są stubowane albo testowane jako kontrolowana warstwa UI; test nie oczekuje kliknięcia człowieka.
- Windows/Win32 integration testy wykonują się na Windows 11; testy AppBar, DPI i Explorer nie są zastępowane wyłącznie mockami.
- W CI uruchamiać build, format/lint zgodny z wybranym toolchainem, unit, integration, testy migracji i paczkowanie próbne. Każda komenda sprawdza exit code; testy uruchomić ponownie po finalnych zmianach.

## 14. Cele KPI i metoda pomiaru

Poniższe progi są wstępnymi **celami projektowymi**, bez dotychczasowego pomiaru. Przed raportowaniem wyników należy najpierw określić konfigurację sprzętową i zebrać rzeczywiste wartości.

| KPI | Cel projektowy | Metoda i warunki pomiaru |
|---|---|---|
| Responsywność gestów layoutu | Reakcja UI poniżej 50 ms tam, gdzie praktyczne; osobno raportować p50/p95 | Instrumentacja Dispatcher input-to-render dla split/drag/resize, log bez treści; Windows 11, zdefiniowany CPU/RAM, 100/150/200% DPI |
| Zwykła operacja layoutu | p95 < 50 ms przy 50 aktywnych panelach jako cel do walidacji | Powtarzalny scenariusz z testowym pluginem i 50 liśćmi; osobno czas domeny i render WPF; nie raportować go jako zmierzonego przed wykonaniem |
| Zimny start bez sieci | cel <= 3 s na uzgodnionym sprzęcie do pulpitu gotowego do edycji; pogoda może pozostać w stanie cache/loading | 30 uruchomień po restarcie/wyczyszczeniu cache procesu, raport p50/p95, urządzenie i stan dysku zapisane w raporcie |
| Pamięć bezczynnego procesu | wstępny budżet <= 150 MiB working set po 5 min, bez dużych obrazów i wtyczek | Pomiar Windows Performance Counter/Process Explorer na referencyjnym zestawie; z osobnym pomiarem dla trybów i cache bitmap |
| Procesor podczas bezczynności | wstępny cel średnio < 1% jednego logicznego rdzenia przez 5 min, bez aktywnego pobierania pogody i z trzema panelami | Zmierzyć czas CPU procesu podzielony przez czas obserwacji, policzyć osobno w każdym trybie i podać sprzęt; uwzględnić okresowe zapisy oraz wpływ tła systemu |
| Integralność zapisu | 0 utraconych zapisów po potwierdzonym „zapisano” w testach awaryjnego zamknięcia | Iteracyjne edycje + kill test process po sygnale commit, restart i porównanie ID/treści; dane testowe, nie produkcyjne |
| Żądania pogody | zero przed zgodą; domyślnie nie częściej niż co 30 min dla aktywnego cache/lokalizacji | Test HTTP mock i raport liczby requestów, 429/backoff/dedup; nie zakładać SLA dostawcy |
| Diagnostyka | 100% testowanych operacji DB/plugin/weather ma operationId i redakcję w logach | Testy schematu logów i testy z fikcyjnymi tokenami/treściami, sprawdzające brak wycieku |

Każdy raport KPI zapisuje build, commit, wersję serwisową .NET, OS/build, CPU/RAM, rozdzielczość, DPI, liczbę monitorów, liczbę paneli, warm/cold cache, liczbę powtórzeń i metodę. Wartości niespełnione prowadzą do decyzji o zmianie celu albo optymalizacji; nie ukrywać percentyli ani nie nazywać targetów „wydajnością aplikacji”.

## 15. Ryzyka i decyzje wymagające bramki

| Ryzyko | Reakcja planowana |
|---|---|
| Shell nie utrzymuje nakładki pulpitu w oczekiwanym stanie | Opt-in prototyp, osobny adapter, testy Win+D/Explorer, jawne fallback do okna; bez WorkerW jako gwarantowanego API |
| AppBar i wielomonitorowe DPI zachowują się różnie | Pozycja zawsze oparta na zwróconym RECT, testy per-monitor, jeden wybrany monitor w v1, fallback bez rezerwacji błędnego obszaru |
| Wtyczka in-process zawiesza/kończy host | Jawne zaufanie i safe mode, timeouty tylko jako ochrona UI, nie udawana izolacja; sandbox/proces poza v1 |
| SQLite blokuje UI mimo API `Async` | Wszystkie wywołania na workerze kolejki; krótkie transakcje; testy opóźnień i stanu kolejki; nie używać `await` jako dowodu asynchroniczności[12] |
| Backup blokuje zapisy | Ekskluzywne zadanie kolejki, okres spokojnej aktywności, widoczna informacja, odłożenie/anulowanie przed startem; nigdy raw copy aktywnej bazy[11] |
| Niekompatybilna migracja lub uszkodzony plik | Kopia przez SQLite online backup, integrity check, zachowanie pliku źródłowego, rollback i tryb odzyskiwania |
| Open-Meteo ogranicza/zmienia warunki użycia | Sprawdzenie terms przed dystrybucją; darmowa warstwa tylko niekomercyjnie; attribution/CC BY; plan licencji komercyjnej przed publikacją komercyjną[5][9] |
| Wygląd przezroczysty powoduje click-through lub brak dostępności | Rozdzielić opacity i hit testing, tylko jawny tryb click-through, kontrola odzyskiwania, testy focus/Narrator |
| Inspiracja implementacją Rainmeter narusza licencję | Czytać zachowanie jako materiał poglądowy, nie kopiować kodu `System.cpp`, fragmentów ani zasobów; odrębna implementacja i review licencji.[6] |
| Wtyczka deklaruje uprawnienia, których host nie potrafi wymusić | Komunikat przy zgodzie mówi wprost o uprawnieniach procesu; capabilities nie przedstawiać jako restrykcji bezpieczeństwa[3] |

## 16. Kryteria wydania v1

Wydanie jest gotowe dopiero, gdy wszystkie punkty są potwierdzone testem lub dokumentem decyzji:

- [ ] Zbudowany Windows 11 WPF host na .NET 10 LTS i działające aktualizowanie serwisowanej wersji runtime.
- [ ] Możliwe jest dodanie wielu instancji każdego z trzech wbudowanych paneli, a każda ma odrębne `instanceId` i dane.
- [ ] Użytkownik potrafi dzielić układ poziomo/pionowo, przeciągać, zmieniać proporcje, zamieniać miejscami, ukrywać/przywracać, zamieniać zawartość i usuwać panel po wyraźnym potwierdzeniu.
- [ ] Drzewo jest walidowane i wersjonowane; minima, kompaktyzacja, round-trip, uszkodzony dokument i fallback przy odtwarzaniu są przetestowane.
- [ ] Zwykłe okno zawsze pozostaje osiągalnym fallbackiem. Tryb pulpitu jest jawnie najlepszych starań. AppBar stosuje systemowy RECT, odbiera powiadomienia i rejestruje usunięcie.
- [ ] ToDo i Notatnik działają offline, zapisują transakcyjnie na kolejce poza UI, a kopie są wykonywane bez kopiowania aktywnego pliku/WAL.
- [ ] Backup/restore, migracje, integralność, awaria i rollback przeszły testy. UI komunikuje okresowe kolejkowanie zapisów podczas backupu.
- [ ] Pogoda nie wykonuje połączeń przed zgodą, używa HTTPS, pokazuje czas i źródło, atrybucję i link, obsługuje cache/stale/429/offline i nie przedstawia danych modelowych jako stacji pomiarowej.
- [ ] Manifest, trust flow, lifecycle, `AssemblyLoadContext`/`AssemblyDependencyResolver`, per-instance state i safe mode działają dla testowej wtyczki; użytkownik widzi, że nie ma sandboxa.
- [ ] Klawiatura, UI Automation, czytnik ekranu, kontrast, z-order, DPI i odzyskiwanie UI są zweryfikowane na Windows 11.
- [ ] Logi są strukturalne, w `LOGS/*.txt`, rotowane, redagowane i nie ujawniają treści paneli ani lokalizacji.
- [ ] Installer/update/rollback nie zapisują do katalogu programu danych użytkownika i nie kasują ich przy standardowej aktualizacji/odinstalowaniu.
- [ ] Kryteria KPI są raportowane jako rzeczywiste wyniki dopiero po wykonaniu opisanych pomiarów; nieużyte wyniki są jawnie oznaczone jako niezmierzone.

## Sources

[1] https://learn.microsoft.com/en-us/windows/win32/shell/application-desktop-toolbars
[2] https://learn.microsoft.com/en-us/dotnet/core/releases-and-support
[3] https://learn.microsoft.com/dotnet/core/tutorials/creating-app-with-plugin-support
[4] https://docs.rainmeter.net/manual/skins/rainmeter-section/defaults
[5] https://open-meteo.com/en/terms
[6] https://github.com/rainmeter/rainmeter/blob/6802bae9/Library/System.cpp
[7] https://open-meteo.com/en/docs
[8] https://open-meteo.com/en/docs/geocoding-api
[9] https://open-meteo.com/en/licence
[10] https://learn.microsoft.com/en-us/dotnet/desktop/wpf/overview
[11] https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/backup
[12] https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/async
[13] https://learn.microsoft.com/en-us/windows/win32/hidpi/wm-dpichanged
[14] https://learn.microsoft.com/en-us/dotnet/core/deploying/trimming/trim-self-contained
[15] https://jrsoftware.org/isinfo.php
[16] https://jrsoftware.org/isorder.php
[17] https://github.com/FlaUI/FlaUI
[18] https://github.com/fscheck/FsCheck
[19] https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaymonitors
[20] https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-monitorinfo
[21] https://learn.microsoft.com/en-us/windows/win32/shell/taskbar
[22] https://github.com/open-meteo/geocoding-api
[23] https://www.geonames.org/export
