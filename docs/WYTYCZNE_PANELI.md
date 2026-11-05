# Wytyczne tworzenia paneli UberDashboard

Data: 2026-11-05, Europe/Warsaw. Zakres: nowe i rozbudowywane panele wbudowane oraz panele wtyczek.

**AI przed tworzeniem panelu musi przeczytać cały ten dokument, [zasady projektu](../ZASADY_PROJEKTU.md) i [AGENTS.md](../AGENTS.md), a następnie przestrzegać ich w trakcie pracy.**

To wspólny standard funkcji i wyglądu, zebrany z architektury, kontraktów, dokumentacji pluginów i uzgodnionego kierunku UI. Określa wymagania dla nowej pracy, a nie potwierdzenie, że każdy historyczny panel już je spełnia. Funkcje specyficzne dla danego typu wymagają własnej specyfikacji. Techniczny panel pustej przestrzeni może nie mieć treści i zakładek; takiego wyjątku nie przenoś automatycznie na panele treści.

## 1. Podstawowe funkcje i podział odpowiedzialności

| Obszar | Wymaganie |
| --- | --- |
| Dodawanie | Typ ma czytelną nazwę i reprezentację w katalogu hosta. Można utworzyć wiele niezależnych instancji na różnych pulpitach. Nowe ID nadaje host. |
| Wspólna karta | Używaj nagłówka, menu, opcji i obramowania hosta. Nie buduj drugiej ramki aplikacji ani konkurencyjnego menu układu wewnątrz treści. |
| Układ | Współpracuj z dodawaniem, dzieleniem poziomo/pionowo, zagnieżdżonymi podziałami, zmianą proporcji, przenoszeniem i zamianą miejsc. Nie zapisuj własnych współrzędnych w miejsce układu hosta. |
| Ukrywanie | Ukrycie i przywrócenie zachowują dane oraz tożsamość. Ukrycie, przełączenie zakładki i zmiana pulpitu nie oznaczają usunięcia. |
| Zamiana zawartości | Używaj istniejącej operacji hosta; poprzednie dane nie mogą zniknąć wskutek zmiany typu zawartości. |
| Usuwanie | Usuwanie obsługuje host wraz z właściwym potwierdzeniem zakresu. Panel zwalnia własne zasoby; nie usuwa zewnętrznych dokumentów, zdjęć ani celów skrótów. |
| Opcje | Ustawienia działania dotyczą aktywnej instancji/zakładki. Wspólny wygląd należy do grupy. Opcje muszą mieścić się w dostępnym obszarze lub mieć przewijanie. |
| Trwałość | Treść i konfiguracja zapisują się automatycznie, bez wymogu ręcznego „Zapisz”. Błąd zapisu pozostaje widoczny i umożliwia ponowienie. |
| Stany | Pokaż sensowny stan pusty, ładowanie, działanie, brak zasobu/dostępu, błąd i ponowienie; anulowanie, jeśli operacja go wymaga. Nie udawaj sukcesu ani pomiaru. |

Nie każda funkcja domenowa dotyczy każdego panelu: import, eksport, odświeżanie, logowanie, wykresy i alternatywne widoki dodawaj wtedy, gdy wynikają z zadania. Nie kopiuj integracji sieciowej ani funkcji edycji z innego panelu bez potrzeby.

## 2. Zakładki

- Panele treści korzystają ze wspólnego systemu zakładek hosta, także wtyczki. Nie implementuj osobnego paska zakładek zastępującego ten system.
- Każda zakładka ma własny `PanelInstanceId`, treść, konfigurację i magazyn plików. Nie przechowuj stanu użytkownika w singletonie lub deskryptorze.
- Położenie i personalizacja są wspólne dla grupy. Zmiana zakładki nie może podmieniać tła, fontu ani geometrii grupy na ustawienia innej instancji.
- Zachowaj działania hosta: „+” dla nowej zakładki tego samego typu, nazwa przez dwuklik/F2, kolejność przez przeciąganie lub Ctrl+Shift+strzałkę, usuwanie z menu i ochronę ostatniej zakładki.
- Przy przełączaniu poprzednia instancja jest zapisywana i wstrzymywana. Zatrzymaj jej odpytywanie, timery i kosztowne działania, a po powrocie bezpiecznie je wznów.
- Weryfikuj szybkie przełączenia, równoległy zapis i zamknięcie. Spóźniona odpowiedź nie może nadpisać danych nowej aktywnej zakładki.
- Brak lub awaria pluginu nie uzasadnia kasowania grupy i zapisanej treści. Zachowuj możliwość odzyskania po przywróceniu zgodnego pluginu.

## 3. Kontrakty, rejestracja i cykl życia

Źródło prawdy API: `src/UberDashboard.Contracts/Panels/` i `src/UberDashboard.Contracts/Host/`. Przed implementacją sprawdź aktualne sygnatury; opis nie zastępuje kodu kontraktów.

1. `IPanelDescriptor`: stabilne, unikalne `TypeId`, lokalizowane `DisplayName`, opcjonalne `IconKey`, realne `MinSize` w DIP, `StateSchemaVersion` i `Factory`. Deskryptor nie przechowuje stanu instancji.
2. `IPanelFactory.CreateAsync`: przyjmij tożsamość i usługi z `PanelContext`. Nigdy nie generuj nowego ID podczas odtwarzania.
3. `IPanelInstance`: zapewnij `ViewModel`, `InitializeAsync`, `RestoreStateAsync`, `CaptureStateAsync`, `SuspendAsync`, `ResumeAsync`, `PrepareForRemovalAsync` i `DisposeAsync`. Inicjalizacja, wstrzymanie, wznowienie i sprzątanie nie mogą mnożyć subskrypcji ani timerów.
4. Trwałe zmiany zgłaszaj przez `IPanelStateNotifications`: zwiększaj `ChangeVersion` i zgłaszaj `StateChanged` na UI. `OnStatePersisted` oznacza potwierdzony zapis konkretnej wersji; nie ustawiaj samodzielnie sukcesu przed zatwierdzeniem magazynu.
5. Host obecnie stosuje debounce 600 ms. Nie kopiuj własnego równoległego mechanizmu zapisu całego stanu. Odczyty czujników, animacja i zmiana slajdu nie są automatycznie zmianą trwałej konfiguracji.
6. Gdy panel ma opcje, wykorzystaj `IPanelOptions` i istniejącą prezentację opcji. Wbudowany panel podepnij do właściwego szablonu/selektora opcji. Wtyczka odpowiada za pokazanie treści swoich opcji.
7. Własny widok może udostępniać `IPanelContent`; każda instancja musi mieć własny element WPF. Przy potrzebie adaptacji użyj `IResponsivePanel.OnPanelSizeChanged` albo istniejących mechanizmów układu WPF.
8. Panel wbudowany umieść w `src/UberDashboard.App/Features/<Nazwa>`, zarejestruj przez `BuiltInPanelRegistration` w `Hosting/AppServiceCollectionExtensions.cs`, dodaj widok/`DataTemplate` w istniejącym systemie i właściwe zależności DI.
9. Panel pluginu rejestruje `IPluginModule`. Używaj własnego reverse-DNS ID i unikalnego prefiksowanego TypeId, aktualnej zgodności API i manifestu. Nie odwołuj się do wnętrza App, Application lub Infrastructure; szczegóły paczki określa [dokumentacja pluginów](PLUGINY.md).

Fabryka i cykl życia panelu są wywoływane przez host WPF na UI. Metody mają szybko oddawać sterowanie. Inicjalizacja modułu pluginu działa w tle i nie może tworzyć obiektów WPF ani zmieniać zasobów `Application`.

## 4. Zapis, migracje i bezpieczeństwo danych

- Korzystaj z istniejącego magazynu stanu hosta/SQLite i `PanelContext.Storage` dla plików instancji. Nie pobieraj surowego połączenia do bazy z wnętrza panelu wtyczki.
- `CaptureStateAsync` zwraca poprawny JSON UTF-8; model zapisu ma jawny schemat. Oddziel stan trwały od tymczasowego fokusu, otwartego menu czy trwającej animacji.
- Normalizuj odczytane wartości, weryfikuj zakresy, rozmiary i formaty. Obsłuż pusty stan, starsze wersje i uszkodzony zapis bez utraty oryginału. Nieznany nowszy schemat nie może zostać zastąpiony pustym dokumentem.
- Przy zmianie formatu dodaj migrację i testy zgodności. Zachowuj dane i kopię potrzebną do odzyskania zgodnie z istniejącą polityką projektu.
- Szybkie edycje podczas zapisu nie mogą być oznaczone jako zapisane przez potwierdzenie starszej wersji. Sprawdź zapis przed wstrzymaniem/zamknięciem i obsługę błędów.
- Przy importach waliduj format, rozmiar i kodowanie przed zatwierdzeniem; anulowanie nie może pozostawiać częściowo dodanych danych. Import nie jest obowiązkową funkcją każdego panelu.
- Operuj wyłącznie na plikach należących do instancji. Usuwanie wpisu odwołującego się do pliku nie oznacza usunięcia źródła z dysku.
- Nie zapisuj haseł i tokenów w zwykłym stanie JSON, logach, repozytorium ani paczce. W integracji korzystaj z istniejącego bezpiecznego magazynu poświadczeń i minimalnych uprawnień.
- Dla obrazów i tapet ustal, czy są kopiowane do profilu, czy wskazywane ścieżką. Wyjaśnij użytkownikowi skutki przeniesienia pliku; brak obrazu ma dać bezpieczny wygląd zastępczy.

## 5. Responsywność i praca w tle

- Panel musi działać w zwykłym oknie, poziomym i pionowym pasku oraz obsługiwanym trybie pulpitu. Nie zakładaj, że szerokość zawsze przewyższa wysokość.
- Deklaruj minimum na podstawie realnej treści. Przy małej wysokości stosuj układ poziomy, zawijanie lub przewijanie; przy wąskiej szerokości układ pionowy/kompaktowy. Nie obcinaj podstawowych akcji i komunikatów.
- Rozmiary projektuj w DIP. Uwzględnij powiększony tekst, skalowanie DPI, długie etykiety PL/EN i skrajne ustawienia gęstości.
- Nie wykonuj sieci, pracy SQLite, odczytu katalogów, dekodowania obrazów, pobierania ikon ani odpytywania sprzętu na UI. Stosuj asynchroniczne I/O i pracę w tle dla operacji blokujących/CPU.
- Właściwości związane z WPF i kolekcje powiązane z widokiem aktualizuj na Dispatcherze. Obrazy przygotowane poza UI zamrażaj przez `Freeze`, gdy wymaga tego przekazanie między wątkami.
- Obsługuj `CancellationToken`, w tym token kontekstu i token konkretnej operacji. Anuluj własne działania również przy `SuspendAsync` i `DisposeAsync`; nie polegaj na wymuszonym przerwaniu przez hosta.
- Nie uruchamiaj nakładających się odświeżeń. Stosuj blokadę, anulowanie poprzedniej operacji lub numer rewizji; odrzuć wynik nieaktualnego żądania.
- Kosztowne odświeżanie ma mieć sensowny interwał i przerwę przy ukryciu. Listy o dużej liczbie elementów wymagają ograniczenia pracy i odpowiedniej wirtualizacji.
- Postęp procentowy pokazuj tylko wtedy, gdy istnieje wiarygodna liczba wykonanych i wszystkich jednostek. W innym przypadku pokaż stan nieokreślony.

## 6. Wygląd wspólny dla paneli

- Zachowaj miękki, ciepły, kompaktowy charakter uzgodniony w [kierunku UI](UI_KIERUNEK_2026-11-03.md), z inspiracjami Umail i GlassWire. Treść ma być czytelna na niskim pasku; unikaj pustych toolbarów, nadmiaru ramek, dużych marginesów i ozdobników.
- Korzystaj z `DynamicResource` i istniejących tokenów/stylów: m.in. `Brush.Text`, `Brush.Text.Muted`, `Brush.Surface`, `Brush.Border`, `Brush.Accent`, `Token.Padding.Panel`, `ToolbarButton`, `ThemedTextBox`. Przed użyciem sprawdź dostępne klucze w zasobach hosta.
- Nie wpisuj na sztywno globalnych kolorów, fontów, odstępów i zaokrągleń. Style charakterystyczne dla funkcji ogranicz do jej widoku; nie nadpisuj globalnych stylów kontrolek.
- Zachowaj motywy systemowy, jasny i ciemny oraz odświeżanie zasobów przy zmianie motywu. Własna paleta użytkownika nie może znikać przy przełączeniu motywu.
- Treść ma respektować wspólną personalizację panelu: tło, własny tekst i jego kolor, krój i rozmiar, tapetę i jej krycie oraz reset wyglądu. Nie zasłaniaj tapety niepotrzebnym nieprzezroczystym tłem własnego widoku.
- Wygląd grupy i stan treści pozostają oddzielne. Reset wyglądu nie usuwa treści ani nie resetuje ustawień działania.
- Wspólne ustawienia tekstu obejmują rozmiar 11–24 DIP, gęstość 0,85–1,35 i systemowe kroje. Nie wprowadzaj mniejszych limitów lub własnej listy kilku fontów bez uzasadnienia.
- Dzielniki i promienie są odpowiedzialnością hosta: zachowuj regulowany odstęp, cienką linię i większy obszar chwytania. Nie odtwarzaj ich wewnątrz nowej karty.
- Tekst i kontrolki muszą pozostać czytelne na jasnym/ciemnym tle, przy tapecie i przezroczystości. Nie nakładaj dodatkowego krycia na całą treść, omijając wspólne ustawienia.
- Przyciski ikonowe korzystają z istniejącego systemu ikon/wektorów; unikaj zależności od przypadkowej dostępności glifów fontu. Stosuj wspólne okna i kontrolki motywu, m.in. `ThemedFileDialog` i `ThemedMessageBox`, gdy pasują do operacji.

## 7. Dostępność, języki i komunikaty

- Podstawowe akcje mają być dostępne klawiaturą i bez najechania myszą. Zachowaj widoczny fokus, logiczny Tab, obsługę Enter/Escape odpowiednią do edytora i brak konfliktów z przeciąganiem karty.
- Dodaj dostępne nazwy `AutomationProperties.Name`, także przyciskom ikonowym, oraz etykiety pól. Tooltip wyjaśnia efekt lub konsekwencje akcji, a nie tylko powtarza nazwę.
- Błędy, ostrzeżenia i brak danych wymagają tekstu; sam kolor/ikona nie wystarcza. Przy zmianach statusu używaj właściwego mechanizmu dostępności bez ciągłego zalewania czytnika komunikatami.
- Korzystaj ze wspólnej lokalizacji `UiText`/rozszerzenia XAML `loc:Text` i katalogu tłumaczeń. Dodaj klucze PL i EN także dla nazw typu, opcji, dialogów, toastów, tooltipów i dostępności. Nie tłumacz samodzielnie treści użytkownika.
- Daty, liczby i jednostki formatuj zgodnie z właściwą kulturą; stan zapisywany musi być odtwarzalny niezależnie od języka UI.
- Używaj `PanelContext.Notifier`/globalnych toastów do nieblokujących komunikatów i `PanelContext.Logger` do diagnostyki instancji. Nie pokazuj toasta przy każdym cyklu czujnika ani każdym znaku automatycznego zapisu.
- Nie loguj tokenów, pełnej prywatnej treści ani danych niezwiązanych z diagnostyką. Zachowaj moduł, instancję, poziom, czas i użyteczne szczegóły błędu przez wspólną infrastrukturę.

## 8. Integracje i funkcje specyficzne dla typu

- Opisz główną funkcję, stan pusty, model danych, opcje i ograniczenia, zanim zaczniesz kodować. Nie wnioskuj z wyglądu makiety, że dane lub integracja już działają.
- Lokalna treść powinna działać offline. Panel wymagający sieci rozróżnia brak konfiguracji, brak zgody, logowanie, pobieranie, brak wyników i błąd. Po błędzie nie udaje aktualnych danych.
- Ujawniaj ograniczenia: odczyt a edycja/synchronizacja, dostępność czujników i uprawnień, wymagany Client ID, lokalne ścieżki. Brak pomiaru nie jest wartością zero.
- Integracja ma mieć ponowienie i, jeśli dotyczy, odłączenie konta oraz usunięcie poświadczeń w zakresie tej instancji. Nie rozszerzaj uprawnień lub zapisu w zewnętrznej usłudze bez potrzeby wynikającej z zadania.
- Otwieranie zewnętrznych celów wymaga walidacji i świadomej akcji użytkownika. Nie wykonuj treści pobranej z sieci ani poleceń zapisanych w importowanym dokumencie.
- Funkcje istniejących paneli nie są automatycznym wymaganiem dla wszystkich nowych. Notatnik ma edycję tekstu, ToDo zadania, Pogoda dane pogodowe, a ramka zdjęcia; wspólnym wymaganiem pozostaje integracja z hostem opisana wyżej.

## 9. Testy i lista odbioru

Przed implementacją wybierz odpowiednie przypadki testowe. Dla błędu zacznij od testu regresji, gdy to praktyczne. Testy nie mogą wymagać ręcznej odpowiedzi na dialog.

- [ ] Stabilne TypeId, poprawna rejestracja i minimum rozmiaru; dwie instancje bez współdzielenia treści i zasobów.
- [ ] Utworzenie, inicjalizacja, odczyt/zapis JSON, ponowne uruchomienie hosta; pusty, stary, uszkodzony i nieobsługiwany schemat.
- [ ] Zakładki: dodawanie, nazwa, kolejność, przełączanie i usuwanie, zapis poprzedniej zakładki i wspólny wygląd grupy.
- [ ] Ukrycie/przywrócenie, zmiana pulpitu, suspend/resume, usunięcie i dispose: brak wycieków, podwójnych timerów i utraty danych.
- [ ] Szybkie edycje podczas zapisu, błąd zapisu i ponowienie, anulowanie, spóźnione odpowiedzi i zamknięcie podczas operacji.
- [ ] Testy funkcji domenowych: normalny wynik, dane graniczne, brak pliku/sieci/uprawnień i błędne wejście — odpowiednio do typu panelu.
- [ ] Oba motywy, systemowy wybór motywu, własne kolory, font 24 DIP, gęstość, tapeta i reset bez utraty treści.
- [ ] Render WPF dla niskiego szerokiego paska, wąskiego wysokiego paska oraz zwykłego/minimalnego okna; brak obciętych akcji i nieczytelnego tekstu.
- [ ] PL/EN: kompletność nowych kluczy, długie etykiety, formatowanie i brak surowych identyfikatorów w UI.
- [ ] Klawiatura, fokus i nazwy UI Automation; ręczny odbiór DPI, Narratora oraz integracji Windows, gdy zmiana ich dotyczy.
- [ ] Dla pluginu: zgodny manifest/API, instalacja, błąd instalacji, przywrócenie zapisanej instancji i zachowanie danych przy braku pluginu.
- [ ] Dokumentacja użytkownika, istotne ograniczenia, wymagane wpisy historii i przegląd końcowego diffu.

Typowe kontrole kodu, po sprawdzeniu aktualnych skryptów:

```powershell
dotnet build UberDashboard.slnx -c Release
dotnet test UberDashboard.slnx -c Release --no-build -v q
dotnet run --project tests/UberDashboard.VisualChecks -c Release --no-build -- "Release/Dev/panele-$(Get-Date -Format yyyyMMdd-HHmmss-fff)"
dotnet run --project tests/UberDashboard.PluginChecks -c Release --no-build -- "Release/PluginChecks/panele-$(Get-Date -Format yyyyMMdd-HHmmss-fff)"
```

Każde polecenie uruchamiaj dopiero po sprawdzeniu sukcesu poprzedniego; nie stosuj pakowania po błędzie. `testuj.bat` jest wspólnym wejściem istniejącej automatyzacji. Dobierz zakres do zmiany; sama zmiana dokumentacji nie wymaga kompilowania aplikacji.

Cel pokrycia zmienianej logiki to co najmniej 75% znaczących testów, gdy jest to praktyczne; liczba nie zastępuje właściwych przypadków. Raportuj dokładnie uruchomione kontrole i ich wyniki. Automatyczny render nie potwierdza rzeczywistego logowania Microsoft/Google, zachowania tray/AppBar, DWM, wszystkich monitorów ani DPI. Wymień niewykonane kontrole oraz odstępstwa od tej listy wraz z powodem.
