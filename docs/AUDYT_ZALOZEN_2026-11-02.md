# Audyt założeń i UI UberDashboard

Data: 2026-11-02T21:00:59.6868795+01:00
Wersja po poprawkach: **1.0.8**
Punkt odniesienia: czysty stan `4910420` (pierwszy kod aplikacji w `8164ac5`).
Build do przeglądu: `Release/Dev/v1.0.8_20261102-210401-650-audit`.

## Ocena

**Nie wszystkie założenia PLAN_BUDOWY.md i sześciu punktów użytkownika są spełnione.** Jest działający fundament WPF/.NET, trzy rodzaje paneli i niezależny zapis instancji. Największe niedokończone elementy to rzeczywista warstwa widgetów pulpitu, kompletny system instalowanych wtyczek, tapety/personalizacja per panel, pełne odzyskiwanie danych oraz pomiary wydajności. Przycisk trybu ani obecność interfejsu C# nie są dowodem implementacji funkcji.

Plan potraktowano jako specyfikację do porównania, bez wykonywania zawartych w nim przykładów czy poleceń jako dodatkowych instrukcji użytkownika. Oryginalnego planu i jego kryteriów wydania nie oznaczono jako ukończonych.

## Sześć wymagań użytkownika

| Wymaganie | Stan po tej iteracji | Dowód i ograniczenie |
|---|---|---|
| Dowolne zarządzanie poziomymi/pionowymi panelami | Zasadnicza obsługa układu kafelkowego wdrożona, odbiór interakcji na żywo otwarty | Dodawanie obok/poniżej, hide/restore, delete z potwierdzeniem, swap, replace, move z menu i przeciąganiem tytułu, zapis dzielników. Testy powłoki z prawdziwym SQLite. Brak swobodnych prostokątów na pulpicie; układ kafelkowy nie nakłada paneli na siebie. |
| Zwykłe okno, widget, pasek rezerwujący ekran | Częściowo | Okno jest zaimplementowane. AppBar dostał faktyczne ustawianie okna i komunikaty powłoki. Widget obecnie jawnie wraca do zwykłego okna: wcześniejszy pełnoekranowy widok bez ramki nie był warstwą pulpitu. AppBar nadal wymaga odbioru na żywej powłoce. |
| Minimalny koszt CPU/RAM i szybkie reakcje | Niepotwierdzone pomiarami | Baza ma osobny worker, edycje mają debounce, zapis nie robi I/O SQLite na Dispatcherze. Nie wykonano protokołu KPI: 5 minut idle, 30 zimnych startów, 50 paneli, p50/p95 input-to-render i miks DPI. Nie ma podstaw do deklaracji „super szybka”. |
| Łatwe dodawanie instalowanych wtyczek | Architektura przygotowana, produktowo brak | Są Contracts, manifest i PanelCatalog. Nie ma kompletnego loadera AssemblyLoadContext/AssemblyDependencyResolver, instalatora ZIP, zaufania/podpisów, safe mode ani przykładowego SDK. Naprawiono zachowywanie miejsca/danych nieobecnej wtyczki. |
| Bogata personalizacja | Częściowo | Są motywy, kolory powierzchni/akcentu, font, rozmiar, gęstość, narożniki. Brak importu tapet, kaskady ustawień per panel i pełnej kontroli kolorów tekstu. Dowolna mieszanka własnych kolorów nie ma potwierdzonego kontrastu. Przezroczystość widgetu nie oznacza wdrożenia samego widgetu. |
| ToDo, Notatnik i Pogoda od początku | Trzy typy dostępne; pełne szczegóły planu częściowe | Rejestracja wszystkich typów istnieje. ToDo nie udostępnia jeszcze całego zakresu edycji/kolejności/terminów w UI. Notatnik obsługuje bogaty tekst i obrazy. Pogoda ma jawny wybór lokalizacji, zgodę, dane/cache; deduplikacja i backoff nie są kompletne. |

## Macierz pozostałych obszarów planu

| Sekcje planu | Ocena |
|---|---|
| 1–2: technologia, zakres v1 | C#/WPF/.NET 10 i sześć warstw istnieje. Zakres wydania v1 pozostaje niekompletny. |
| 3: UX, polecenia i dostępność | Naprawiono ikony, minima/przewijanie, toolbar, menu paska, dostępne nazwy i polskie etykiety. Menu Układ daje alternatywę dla przeciągania. Pełny przebieg keyboard-only/Narrator, odzyskiwanie fokusu po przebudowie i popupy na różnych DPI pozostają otwarte. |
| 4: silnik podziałów | Core ma operacje i walidację z testami właściwości. UI jest teraz podłączony do zapisu resize i do move/replace/split w obu osiach. Operacje instancja + układ nadal obejmują wiele wywołań repozytoriów; nie jest to jedna wspólna transakcja odporna na awarię procesu w każdym punkcie. |
| 5: Windows | AppBar używa zwróconego RECT, callbacku, aktywacji/dezaktywacji, pozycji, fullscreen, display/DPI i TaskbarCreated; zwalnia rezerwację przy wyjściu/trayu. Te ścieżki mają testy kontrolera i koordynatora, lecz brak odbioru natywnego z innym maksymalizowanym oknem. DesktopPlacement i prototyp warstwy pulpitu nie są gotowe. |
| 6–7: architektura i wtyczki | Podział warstw i kontrakty istnieją; kompletny cykl instalacji, aktywacji, aktualizacji i odzyskiwania zewnętrznych wtyczek pozostaje do wdrożenia. Nie obiecywać izolacji in-process. |
| 8: ToDo | Dodawanie, ukończenie, kasowanie i zapis działają. Brak pełnej edycji, zmiany kolejności i terminu w interfejsie. |
| 8: Notatnik | Bogaty tekst i obrazy istnieją. Render weryfikuje round-trip obrazu przez XAML package. Dodano ostrzeżenie przed zastąpieniem notatki importem oraz filtr .md/.txt. Brak zweryfikowanego limitu rozmiaru dokumentu i pomiarów kosztu serializacji dużych obrazów przy pisaniu. Nie wykonano testu rzeczywistego schowka. |
| 8: Pogoda | Zero forecastów przed zgodą potwierdzone regresją, cofnięcie zgody anuluje pobieranie. Nowe/legacy konfiguracje bez znacznika zgody nie łączą się automatycznie. Dodano źródło, linki, opis modelowy i domyślny interwał 30 min dla nowych paneli. Nadal brak wspólnej deduplikacji identycznych żądań, kompletnego Retry-After/backoff, pełnego offline-first i potwierdzenia integracji globalnego interwału z każdą instancją. |
| 9: trwałość i odzyskiwanie | SQLite, worker, WAL, migrator, transakcje repozytoriów i tworzenie kopii online istnieją. SqliteBackupService nie stanowi kompletnego restore ze stagingiem, awaryjną kopią i rollbackiem; brakuje całego przepływu odzyskiwania w UI i pełnych testów awaryjnych. PendingCleanup nie stanowi działającego cleanupu plików wtyczek. |
| 10: personalizacja i zasoby | Globalne tokeny działają po naprawieniu utraty VisualTokens przy zmianie motywu. Brak systemu assetów tapet i nadpisań per panel. Błędne kolory oraz NaN/Infinity mają bezpieczne wartości zastępcze. |
| 11: logi i instalacja | Nowe logi .txt mają strukturalny JSON, timestamp z offsetem i retencję. Toasty nie utrwalają już treści komunikatów mogącej zawierać lokalizację/nazwę pliku. Nie oznacza to pełnego audytu redakcji każdego wyjątku ani kompletności operationId we wszystkich modułach. Brak okna Live System Log. Instalator per-user istnieje, lecz upgrade/rollback/uninstall nie były w tej iteracji wykonywane. |
| 12–13: etapy i testy | Rozbudowano testy powłoki + SQLite, koordynatora, zgody sieciowej, logów i awarii zapisu oraz osobny renderer WPF. Nie ma kompletnej macierzy testów Windows/FlaUI, podpisów wtyczek, awarii restore ani testów każdego scenariusza planu. |
| 14: KPI | Niezmierzone. Czas testu/renderowania nie jest czasem startu aplikacji ani p95 interakcji. Nie zmierzono pokrycia kodu; nie deklarujemy 75%. |
| 15–16: ryzyka i wydanie | Bramki wydania pozostają otwarte. v1.0.8 jest buildem do przeglądu poprawek, a nie potwierdzeniem ukończenia produktu według planu. |

## Naprawione i potwierdzone problemy

1. **Usuwanie bez potwierdzenia** — dodano wstrzykiwane potwierdzenie z bezpiecznym domyślnym anulowaniem. Test najpierw FAIL, następnie PASS; anulowanie nie zmienia bazy ani drzewa.
2. **Ukryty panel wracał po restarcie** — finalny autozapis robił Upsert całego starego rekordu i cofał IsHidden. Zapis zmienia teraz wyłącznie StateJson/UpdatedAtUtc, bez odtwarzania usuniętego rekordu. Test odtworzył błąd przed poprawką i potwierdza hide/reload/restore z treścią zadania.
3. **Proporcje dzielników były tylko wizualne** — PersistedGridSplitter zapisuje rezultat gestu i klawiszy przez LayoutEditor, a minima poddrzew są używane przez WPF. Test potwierdza proporcje po ponownym odczycie SQLite.
4. **Brak podłączenia części operacji layoutu do UI** — dodano menu Układ, pozycjonowanie w dwóch osiach, zamianę zawartości bez kasowania poprzednika i przeciąganie w trybie edycji. Testy potwierdzają tożsamość danych i zachowanie po reload.
5. **Brakujący plugin usuwał miejsce z drzewa** — widok zastępczy zachowuje identyfikator, stan i liść; regresja sprawdza zapisane dane.
6. **AppBar rezerwował RECT bez pozycjonowania okna i callbacku** — dodano adapter geometrii, przywracanie normalnych granic, callback, rejestrację po restarcie powłoki, obsługę geometrii/DPI i zwalnianie obszaru. Testy kontrolują dokładny zwrócony RECT oraz fallback przy błędzie ustawienia okna.
7. **Widget pozorował warstwę pulpitu** — wycofano pełnoekranową imitację. Obecnie jest komunikat o niedostępnej eksperymentalnej integracji i bezpieczny powrót do okna. To uczciwy fallback, nie implementacja docelowego trybu.
8. **Zmiana motywu usuwała czcionki/odstępy** — wydzielono pozycję słownika kolorów przed VisualTokens i użyto jawnego URI zasobu assembly. Renderer sprawdza tokeny po zmianie motywu.
9. **Kwadraty zamiast ikon i kontrast opcji** — implicit TextBlock narzucał font tekstowy wewnątrz ContentPresenter przycisku. Osobny szablon glifu ustawia właściwą czcionkę lokalnie. Renderer sprawdza obecność kodów w rzeczywistym GlyphTypeface. W ustawieniach naprawiono czarne etykiety na ciemnym tle, wygląd zakładek i techniczny opis monitora.
10. **Pogoda wysyłała żądania bez zgody** — regresja legacy konfiguracji wykazała 2 żądania. Dodano utrwalaną zgodę, bramkę wyszukiwania/forecastu, anulowanie i test cofnięcia zgody. Dane testowe nie wymagają usług internetowych.
11. **Przepełnianie UI** — zawijany toolbar okna, kompaktowe menu w pasku, przewijanie przy minimach, przewijany panel pogody i zawijane kafle szczegółów. Tekst/panele nie są ściskane poniżej zadeklarowanego minimum; w pasku 200 px część treści wymaga przewinięcia.
12. **Niepotwierdzony zapis przy zamykaniu** — flush błędu nie ukrywa, a zwykłe zamknięcie czeka asynchronicznie przed zniknięciem Dispatchera. Błąd pozostawia okno otwarte. Błąd zapisu układu nie generuje komunikatu sukcesu; polecenie Zapisz układ umożliwia ponowienie bez przeładowania danych. Stany WPF są przechwytywane na Dispatcherze, zapis DB na workerze; zapisy jednej instancji są serializowane i nowsza edycja nie jest oznaczana jako już zapisana.
13. **Build sam podbijał wersję** — wersję zmieniono jawnie raz do 1.0.8 dla poprawek; skrypty tworzą odrębne identyfikatory buildów bez kolejnego podbijania aplikacji.

## Weryfikacja i jej granice

- Bazowo: 201 testów PASS. Nowe regresje usuwania, ukrywania i zgody odtworzono jako FAIL przed odpowiednimi poprawkami.
- Końcowy wynik kompilacji, testów, renderera, formatowania i publish: **Release build PASS (0 ostrzeżeń, 0 błędów); 225 testów PASS (App 35, Application 64, Core 84, Infrastructure 42); renderer PASS (22 obrazy, obecne glify i obraz w notatce); publish self-contained win-x64 PASS. Formatowanie zmienionego C# wykonane; końcowa kontrola formatu jest zapisana obok raportu walidacji.**.
- Artefakty: `Release/Dev/v1.0.8_20261102-210401-650-audit/verification`, `Release/Dev/v1.0.8_20261102-210401-650-audit/visual`, `Release/Dev/v1.0.8_20261102-210401-650-audit/app`.
- Renderer używa rzeczywistych słowników i szablonów WPF, dwóch motywów, rozmiarów 1280×820, 860×560, 1920×200, 200×900, edycji włączonej/wyłączonej, notatki z syntetycznym obrazem, zadań i fikcyjnej pogody. Osobno renderuje trzy zakładki ustawień w obu motywach. Testowy dostawca pogody zastępuje HTTP.
- Render geometryczny paska **nie uruchamia integracji AppBar**. Nie stanowi dowodu działania z-order, maksymalizacji innego okna, Win+D, DPI 150–200%, Narratora, systemowych popupów ani restartu Explorera.
- Kontrola na żywym pulpicie przez computer-use i cua nie uruchomiła się: środowisko narzędzi zgłasza `MXC launcher: enumerate MXC volume D:\ ... os error 1005`. Odczyt/kod/build/test wykonano działającym narzędziem poleceń; nie zmieniano konfiguracji dysku D:.
- Nie wykonano instalacji, aktualizacji ani kasowania danych profilu. Pakiet testowy nie jest instalatorem.

## Kolejność dalszego domykania zakresu

1. Odbiór AppBar na Windows 11: cztery krawędzie, drugie maksymalizowane okno, restart Explorera, odłączenie monitora, miks DPI i fullscreen.
2. Prawdziwy eksperymentalny host pulpitu wraz z odzyskiwaniem, osobnymi pozycjami i bezpiecznym powrotem do okna.
3. Kompletny host/instalacja wtyczek, zaufanie/podpisy/safe mode i przykład SDK.
4. Restore/rollback oraz atomowe operacje metadanych i layoutu, komplet testów awaryjnych i cleanupu.
5. Tapety, wygląd per panel, gwarancje kontrastu własnych palet, pełne funkcje ToDo i brakujące mechanizmy pogody.
6. Dopiero po pomiarach: optymalizacje CPU/RAM/startu i odbiór KPI, pełna macierz dostępności oraz instalatora.

## Źródła techniczne sprawdzone podczas audytu

- [Microsoft: Application Desktop Toolbars](https://learn.microsoft.com/en-us/windows/win32/shell/application-desktop-toolbars) — rejestracja, zwrócony RECT, przesunięcie okna, callbacki i zwalnianie AppBar.
- [Microsoft: GridSplitter](https://learn.microsoft.com/en-us/dotnet/api/system.windows.controls.gridsplitter) — sterowanie myszą i klawiaturą.
- [Open-Meteo: Terms](https://open-meteo.com/en/terms) i [Licence](https://open-meteo.com/en/licence) — komunikat zgody i atrybucja. Nie oznacza to zamknięcia bramki dystrybucji komercyjnej/geokodowania z planu.
## Aktualizacja 1.2.0 — pluginy

Host pluginów opisany powyżej jako brakujący został wdrożony: instalacja i usuwanie paczek .udplugin, integracja natywnych kart i zapisu oraz zewnętrzny plugin ramki na zdjęcia. Szczegóły, ograniczenia API i granice weryfikacji: [PLUGINY.md](PLUGINY.md). Powyższe wyniki audytu pozostają historycznym opisem wersji 1.0.8.
