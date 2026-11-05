# Synchronizacja dwukierunkowa — stan wdrożenia

2026-11-05. Baza prac: `dfb5926`, wersja 1.9.0. To kod roboczy, nie gotowe wydanie 1.10.0.

## Dostępne w kodzie roboczym

- Silnik Core/Application/Infrastructure: trzy wersje danych, scalanie niezależnych pól, konflikty edycji/usunięcia, grupy czasu i cykliczności, kolekcje po ID/e-mailu, trwała kolejka, rewizje, kopie odzyskiwania i rozpoznawanie niepewnego wyniku zapisu.
- SQLite 7: atomowy dokument powiązania wraz z outboxem, CAS, wspólny cache zakresów kalendarza i kopia przed migracją. Dawne migawki Microsoft są zachowane; lokalne importy nie są publikowane automatycznie. Migracja obejmuje oba wcześniejsze robocze warianty schematu 4 oraz schematy 5/6, z zachowaniem niewysłanych operacji. Format synchronizacji 2 rozróżnia zadania, checklisty i powiązania; migracja podnosi także rewizję CAS, aby starszy otwarty zapis nie zastąpił nowego formatu. Starsza aplikacja odrzuci nowszy schemat bazy. Dokument JSON upraszcza transakcję, lecz wymaga pomiarów dla dużych zbiorów i ewentualnej normalizacji.
- Podłączony harmonogram hosta, także podczas pracy w zasobniku. Operacje jednego konta są serializowane. Blokady Retry-After obowiązują dla konta, również przy ręcznym odświeżaniu; stan błędu połączenia jest trwały.
- Powiązana karta To Do i panel Google Calendar udostępniają przycisk **Synchronizacja dwukierunkowa**. Pierwsza aktywacja osobno prosi o zgodę dostawcy na zapis; dotychczasowy import i odczyt nadal działają.
- Edytor zapisuje najpierw lokalnie, pokazuje kolejkę, błędy, konflikty, zatwierdzanie powiadomień, ponowienie, wstrzymanie i odnowienie zgody. Kontrola wersji blokuje nadpisanie elementu zmienionego podczas otwartego formularza.
- Zadania: tworzenie, tytuł, opis, dokładny status Microsoft, ważność, przypisanie istniejących kategorii, daty rozpoczęcia/terminu/wykonania, przypomnienie i usuwanie. Opis tekstowy jest kodowany do HTML wymaganego przez endpoint; opcjonalnie można edytować źródło HTML. Niezmienione dane, w tym bogaty opis, dokładność dat i nieznane przyszłe wartości, pozostają zachowane.
- Powtarzanie zadań: sześć wzorców Graph (dzienny, tygodniowy, bezwzględny/względny miesięczny i roczny), trzy zakresy (bez końca, do daty, liczba powtórzeń), dni, odstęp i strefa. Formularz pokazuje pola właściwe dla wzorca. Nie stanowi to potwierdzenia akceptacji każdego wariantu przez rzeczywiste konto To Do.
- Daty zadań zachowują strefę usługi zamiast przeliczać ją przy edycji tytułu na strefę komputera. Można wybrać identyfikator Windows lub IANA; nieprawidłowe oraz pomijane/niejednoznaczne godziny DST są odrzucane. Osobny wybór przesunięcia dla niejednoznacznej godziny nie został jeszcze dodany.
- Checklista i powiązane zasoby Microsoft mają osobne edytory, CRUD i trwałe identyfikatory w tym samym dokumencie listy. Kroki obejmują nazwę, stan wykonania i opcjonalne znaczniki czasu z jawnym przesunięciem strefy. Powiązania obejmują nazwę, aplikację źródłową, URL i externalId. Odczyty pełnych relacji są niezależne od delta zadań i sprawdzają wszystkie strony oraz pochodzenie linków kontynuacji.
- Element zależny czeka na potwierdzone utworzenie zadania. ID checklist i powiązań są rozróżniane według rodzaju i zadania nadrzędnego. Lokalnego zadania nie można usunąć, gdy jego elementy mają niewysłane zmiany: trzeba najpierw sprawdzić ich kolejkę. Zdalne usunięcie zadania kontra lokalna edycja jego elementu tworzy konflikt. Jawny wybór odtworzenia tworzy nowe tożsamości zdalne; przyjęcie usunięcia archiwizuje lokalną zawartość. Przyciski w oknie elementów wstrzymują lub wznawiają całą powiązaną listę. Zamknięcie kontekstu zamyka okna i zatrzymuje ich lokalne timery.
- Dla niepewnego tworzenia w Microsoft bez ID kolejka udostępnia **Wskaż istniejący element**. Użytkownik porównuje szkic wysłanej operacji, aktualną wersję lokalną i konkretny element usługi z ID. Mechanizm wykonuje ponowny GET, respektuje Retry-After, sprawdza aktualność obu podglądów i zapisuje mapowanie lokalnie. Nie ponawia POST ani nie dopasowuje po tytule. Nowsze edycje są scalane lub zatrzymywane jako konflikt; kopie odczytu i ich relacje przechodzą pod dotychczasowe lokalne ID. Przy braku wskazanego elementu stan pozostaje nieznany i wstrzymany; brak kandydata nie dowodzi niepowodzenia wcześniejszego POST.
- Wydarzenia: tworzenie, tytuł, opis, miejsce, terminy, cały dzień i usuwanie. Nieedytowane pola są zachowywane. Zmiana istniejącej serii wymaga potwierdzenia i dotyczy całej serii. Operacje na wydarzeniach z uczestnikami wymagają osobnego zatwierdzenia skutków przed wysłaniem.
- Zwykły widok kalendarza zapisuje w SQLite kompletne, rozwinięte zakresy wraz z ID wystąpień i serii. Kopia jest wspólna dla tego samego konta, klienta, kalendarza i strefy wyświetlania; pokrywa także mniejszy widok. Błąd kolejnej strony nie zastępuje pełnej kopii, a starsze żądanie nie zastępuje nowszego. Widok pokazuje datę kopii i błąd połączenia; niepobrany zakres jest jawnie niedostępny offline. Stare sesje bez tożsamości OpenID wymagają ponownego połączenia, aby używać wspólnego cache. To projekcja ostatniego odczytu usługi: niewysłane edycje i serie w edytorze nie są jeszcze nakładane na siatkę kalendarza.
- Potwierdzenie starszej wysyłki scala jej odpowiedź z nowszą edycją lokalną. Niezależne pola serwera nie są cofane przez kolejny PATCH; sprzeczne zmiany tworzą konflikt. Cofnięcie usuwania wydarzenia podczas wysyłki przygotowuje nowy identyfikator Google przed ponownym tworzeniem.
- **Ustawienia → Integracje → Centrum synchronizacji** daje dostęp do trwałych powiązań również po usunięciu panelu. Usunięcie widoku nie wysyła DELETE do usług.
- Microsoft: kontekstowe Tasks.ReadWrite, domyślny Client ID podany przez właściciela, MSAL/cache Windows, sprawdzanie konta i przyznanych zakresów.
- Google: systemowa przeglądarka, PKCE przez SDK, OpenID Connect `sub`, odrębna zgoda calendar.events i calendar.calendarlist.readonly, odczyt rzeczywiście przyznanych zakresów, DPAPI i kontrola tożsamości przed użyciem tokenu. Sesja nowego konta nie zastępuje poprzedniej, jeśli dodatkowa zgoda nie została poprawnie zakończona; stary refresh nie może odtworzyć odłączonej sesji lub zastąpić nowego konta.
- Zapis plików paneli korzysta z pliku tymczasowego i podmiany, aby przerwany zapis sesji nie niszczył poprzedniej wersji.
- Strona projektu i polityka prywatności są dostępne z ustawień. Ograniczono informacyjne logowanie HTTP; nie potwierdzono zapisu pełnej lokalizacji w logach domyślnego .NET.

## Konfiguracja i uruchomienie

Strona projektu: https://github.com/Kavaars/UberDashboard. Polityka: https://github.com/Kavaars/UberDashboard/blob/main/PRIVACY.md. Kontakt: profil GitHub Kavaars.

Microsoft Client ID jest publiczną konfiguracją klienta i ma wartość domyślną; nadal można użyć własnej rejestracji. Google korzysta z lokalnie wybranego JSON klienta typu Desktop; plik nie jest dodawany do repozytorium ani paczki źródeł. Przekazany lokalny plik sprawdzono bez wypisywania jego treści: typ Desktop i Client ID są zgodne. Samo posiadanie ID nie potwierdza poprawności ekranu zgody, obsługi kont osobistych ani statusu weryfikacji wydawcy.

Rejestracje i rozszerzone zgody trzeba zatwierdzić w rzeczywistej przeglądarce. Dotychczasowy login tylko do odczytu nie jest zgodą na zapis. W razie wygaśnięcia sesji użyj **Połącz ponownie** w edytorze. Konto musi odpowiadać tożsamości powiązania; kolejki nie są przenoszone między kontami.

## Pozostały zakres pełnego planu

**Pełny plan nie jest ukończony.** W szczególności pozostają:

1. Pliki i katalog kategorii; kompletne zarządzanie listami/kalendarzami, subskrypcjami oraz ACL. Podstawowe pomocnicze transporty nie oznaczają ukończenia tych funkcji. Checklisty i powiązania są już podłączone, lecz nadal wymagają odbioru na rzeczywistych kontach.
2. Edycja uczestników/odpowiedzi, konferencje i Drive; pojedyncze wystąpienia, trwałe wyjątki anulowane oraz podział serii jako operacja wieloetapowa. Edytor wydarzeń Google nie oferuje jeszcze tworzenia reguł cykliczności.
3. Nakładanie lokalnych niewysłanych edycji i wyjątków serii na rozwinięty widok kalendarza oraz konsolidacja pobierania projekcji z harmonogramem kont. Zapisany zakres działa już offline, ale jest oznaczoną datą kopią ostatniego odczytu usługi.
4. Ręczna edycja połączonego rozwiązania konfliktu, dodatkowe decyzje dla niepewnej edycji/usunięcia, kompletne odłączenie/usuwanie kont wraz z wyborem kopii i UI historii odzyskiwania. Obecny wybór konfliktu dotyczy wersji lokalnej lub wersji usługi, z ponownym sprawdzeniem serwera. Niepewne tworzenie Microsoft można już uzgodnić przez jawny wybór istniejącego ID; bez takiej identyfikacji operacja nie jest ponawiana.
5. Zarządzanie widocznością zasobów dla interwałów 60/300 sekund; nowo aktywowane powiązania są obecnie traktowane jako widoczne. Synchronizacja kont działa niezależnie od timerów lokalnego odczytu widoków; odczyty projekcji nie zostały jeszcze w pełni skonsolidowane.
6. Próby na rzeczywistych kontach osobistych/organizacyjnych Microsoft i Gmail/Workspace; testy ETag/If-Match Microsoft, realnych odmów ról i limitów. Bez potwierdzonego wsparcia API nie deklarujemy atomowej ochrony równoczesnego zapisu Microsoft.
7. Pełny odbiór DPI/Narratora/klawiatury, fontu 24 DIP, dużych zbiorów, wielu kont, zakładek, zamykania i awarii w operacjach wieloetapowych. Automatyczne testy nie zastępują tych prób. Pobieranie checklist i powiązań wymaga obecnie dwóch pełnych odczytów relacji na znane zadanie w cyklu; duże listy wymagają pomiarów i optymalizacji liczby żądań, a limity dostawcy mogą wydłużyć cykl.
8. Weryfikacje wydawcy i publicznego OAuth. Właściciel wybrał GitHub jako stronę projektu; publiczne repo i polityka nie oznaczają spełnienia wymagań Google dotyczących własności domeny. Wydanie 1.10.0 nie zostało przygotowane.

## Prywatność i kontrola

Polityka odpowiada stwierdzonym przepływom: lokalny zapis nie wyklucza komunikacji z Microsoft/Google/Open-Meteo. Nie znaleziono automatycznej analityki autora lub wysyłania raportów awarii. Baza i zwykłe backupy nie są w całości szyfrowane. Szczegóły zakresu przeglądu i ograniczenia: [audyt prywatności](AUDYT_PRYWATNOSCI_2026-11-05.md).

Testy transportów używają kontrolowanego HTTP, testy DB izolowanych profili, a testy edytorów danych przykładowych. Nie wykonano prawdziwego logowania, modyfikacji kont użytkownika ani weryfikacji konsol dostawców.

Kontrola `sync-privacy-20261105-1010`: Release bez ostrzeżeń i błędów; 438 testów PASS (185 App, 87 Application, 95 Core, 71 Infrastructure); VisualChecks PL/EN po 161 PNG, w tym po 8 renderów nowych edytorów; PluginChecks PASS. Obejrzano także formularz w małym oknie i poprawiono stały dolny pasek akcji. Nie mierzono procentowego pokrycia nowych linii; nie pakowano instalatora ani wydania produkcyjnego.

Kontrola `sync-extended-20261105-2050`: Release 0 ostrzeżeń/błędów; 489 testów PASS (191 App, 90 Application, 130 Core, 78 Infrastructure); VisualChecks PL/EN po 165 PNG, w tym po 12 renderów edytorów; PluginChecks PASS. Rendery obejmują 620×640, 420×360 oraz tekst 24 DIP, etykiety wyboru, zachowanie HTML/statusu i zapis rozszerzonych pól. Sprawdzono automatycznie wybór daty i blokadę zapisu po błędnym wpisie w nowym szablonie; ręczny Narrator, DPI i pełny odbiór rozwijanego kalendarza pozostają otwarte. Zmierzono pokrycie linii: MicrosoftTaskDraft.cs 98,1%, TaskRecurrenceInput.cs 98,1%, CalendarViewCache.cs 96,1%, SyncCoordinator.cs 84,2%. To wyniki tych plików, a nie całej aplikacji; projekt testowy App nie ma kolektora pokrycia. Początkowe testy trzech nowych kontraktów poprzedzały implementację; dwa testy regresji współbieżności najpierw odtworzyły błąd, a po poprawce przeszły. Nie pakowano wydania.

Kontrola `sync-relations-20261105-2145`: Release 0 ostrzeżeń/błędów; 522 testy PASS (191 App, 107 Application, 137 Core, 87 Infrastructure); VisualChecks PL/EN po 183 PNG oraz PluginChecks PASS. W nowych renderach sprawdzono formularze relacji w obu motywach, 420×340, duży tekst 24 DIP i okno ręcznego powiązania 440×360. Automatyczny odbiór obejmuje osobne checklisty dwóch zadań z tym samym zdalnym ID kroku, oznaczenie wykonania, wstrzymanie listy, zamknięcie kontekstu oraz ręczne powiązanie bez POST. Pokrycie linii wybranych plików: MicrosoftRelatedDraft 100%, MicrosoftTodoRelations 93,8%, SyncManualReconciliation 95,9%, SyncEdits 98,5%, SyncCoordinator 92,6%, SyncStore 98,6%, MicrosoftTodoProvider 100%. Nie jest to pokrycie całego App ani dowód zgodności prawdziwego konta. Pierwsza próba WPF wykryła brak tłumaczenia błędnego URL; poprawiono i ponowiono oba języki. Obejrzano render powiązań z dużym tekstem i małe okno uzgodnienia. Pełny ręczny odbiór klawiatury, DPI, Narratora, prawdziwych kont i dużych list nadal nie został wykonany. Nie pakowano wydania 1.10.0.

## Źródła wymagań

Endpointy nowej części Microsoft używają Graph v1.0. Delegowane `Tasks.Read` wystarcza do odczytu, a `Tasks.ReadWrite` jest wymagane do zapisu na koncie osobistym lub organizacyjnym. Testy kontrolowanego HTTP nie zastępują zgody i uprawnień rzeczywistego konta.

| Funkcja | Kolekcja / operacje | Stan prób |
| --- | --- | --- |
| Checklista | `/me/todo/lists/{list}/tasks/{task}/checklistItems`; GET/POST, GET/PATCH/DELETE konkretnego ID | Testy kolejki, HTTP i WPF; realne konta otwarte |
| Powiązania | `/me/todo/lists/{list}/tasks/{task}/linkedResources`; GET/POST, GET/PATCH/DELETE konkretnego ID | Testy kolejki, HTTP i WPF; realne konta otwarte |
| Uzgodnienie tworzenia | GET konkretnego ID wybranego przez użytkownika; lokalny CAS, bez ponowienia POST | Testy starego podglądu, nowszej edycji, relacji i Retry-After; realne konta otwarte |

- [Microsoft: uprawnienia i aktualizacja](https://learn.microsoft.com/en-us/graph/api/todotask-update?view=graph-rest-1.0)
- [Microsoft: checklistItem](https://learn.microsoft.com/en-us/graph/api/resources/checklistitem?view=graph-rest-1.0), [utworzenie](https://learn.microsoft.com/en-us/graph/api/todotask-post-checklistitems?view=graph-rest-1.0) i [aktualizacja](https://learn.microsoft.com/en-us/graph/api/checklistitem-update?view=graph-rest-1.0)
- [Microsoft: linkedResource](https://learn.microsoft.com/en-us/graph/api/resources/linkedresource?view=graph-rest-1.0) i [utworzenie](https://learn.microsoft.com/en-us/graph/api/todotask-post-linkedresources?view=graph-rest-1.0)
- [Microsoft: wzorce cykliczności](https://learn.microsoft.com/en-us/graph/api/resources/recurrencepattern?view=graph-rest-1.0) i [zakresy](https://learn.microsoft.com/en-us/graph/api/resources/recurrencerange?view=graph-rest-1.0)
- [Microsoft: delta](https://learn.microsoft.com/en-us/graph/api/todotask-delta?view=graph-rest-1.0)
- [Microsoft: throttling](https://learn.microsoft.com/en-us/graph/throttling)
- [Google: syncToken](https://developers.google.com/workspace/calendar/api/guides/sync)
- [Google: ETag](https://developers.google.com/workspace/calendar/api/guides/version-resources)
- [Google: zakresy](https://developers.google.com/workspace/calendar/api/auth)
- [Google: OAuth desktop](https://developers.google.com/identity/protocols/oauth2/native-app)
- [Google: wymagania publikacji](https://developers.google.com/identity/protocols/oauth2/production-readiness/sensitive-scope-verification)
