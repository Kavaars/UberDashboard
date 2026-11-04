# Import danych i synchronizacja Microsoft To Do

Punkt odniesienia: `9e0866e`, wersja 1.8.0. Aktualizuj checklistę po każdej zweryfikowanej zmianie, podając datę, commit i wynik kontroli. Obsidian pozostaje poza zakresem wdrożenia. Kontrakt dostawcy powinien umożliwiać integracje bez OAuth i bez obsługi odczytu przyrostowego.

## Fundament

- [x] Zapisać plan i checklistę.
- [x] Potwierdzić kompilację bazową. 2026-11-04, `9e0866e`: Release z dostępnymi zależnościami, 0 błędów i 0 ostrzeżeń.
- [ ] Przygotować izolowany profil.
- [x] Przenieść zadania z JSON paneli do istniejącej tabeli SQLite, z trwałymi identyfikatorami, zachowaniem kolejności, duplikatów i ustawień. Schemat 3 zapisuje kopię i oryginalny JSON.
- [ ] Rozdzielić magazyn zadań i ustawienia paneli. Zapis zadania oraz operacji synchronizacji wykonywać atomowo poza wątkiem UI.
- [ ] Sprawdzić migrację nieaktywnych kart, ponowne uruchomienie i odtworzenie kopii.

## Import plików

W toku (2026-11-04): w menu zakładki ToDo i Notatnika dostępny jest import wielu plików z podglądem i wyborem kart. Nowe karty są zapisywane razem. Notatki Markdown zachowują składnię jako tekst. Formatowanie Markdown i pełny podgląd poszczególnych wierszy pozostają otwarte.

- [x] Sprawdzić podstawowe przypadki parserów. 2026-11-04, zmiany robocze: 3 testy aplikacyjne przeszły po uzyskaniu dostępu do lokalnego procesu testowego.

- [x] Udostępnić wybór plików, mapowanie CSV, podgląd plików i zatwierdzenie. Nowa karta na plik, bez automatycznego usuwania duplikatów. Zapis całej partii w transakcji.
- [ ] Rozszerzyć podgląd o wybór wierszy, indywidualną decyzję dla nieznanych statusów i ostrzeżenie o ponownym imporcie.
- [ ] Obsłużyć TXT (pozycja na linię), Markdown (checkboxy poza kodem, ostrzeżenie przy spłaszczaniu) i CSV (cytowanie, pola wielowierszowe, separator, nagłówek, mapowanie treści i statusu).
- [ ] Obsłużyć UTF-8, BOM, LF i CRLF. Przy błędnym UTF-8 zaoferować świadomy wybór Windows-1250. Limity 100 plików, 10 MiB na plik, 50 MiB łącznie i 10 000 zadań.
- [ ] Importować wiele notatek TXT/MD do nowych kart. Podstawowe formatowanie Markdown, tytuł z H1 lub nazwy pliku. Nieobsługiwane elementy pozostają tekstem źródłowym. Bez pobierania obrazów i wykonywania HTML.
- [ ] Zweryfikować anulowanie, kodowania, błędne dane, limity, rollback i oba motywy WPF.

## Microsoft To Do — odczyt

- [x] Dodać klienta MSAL.NET z chronionym cache i instrukcją rejestracji. Używa przeglądarki systemowej. Identyfikator klienta można zapisać w ustawieniach integracji. Zmienna środowiska podpowiada dotychczasową konfigurację.
- [x] Dodać odczyt Graph v1.0 przez `Tasks.Read`, stronicowanie i import lokalnych kopii. Kreator w Ustawieniach → Integracje umożliwia wybór wielu list i tworzenie powiązanych kart do odczytu.
- [x] Odświeżać powiązane karty przy starcie, na żądanie i co 60 sekund podczas pracy. Zachowywać ostatnią kopię i datę odświeżenia. Powiązania są oddzielne dla każdego klienta, konta i listy.
- [ ] Sprawdzić konto osobiste i organizacyjne, anulowanie zgody, ponowny start, wylogowanie i brak sieci.

## Microsoft To Do — synchronizacja

- [ ] Sprawdzić na koncie testowym rzeczywistą obsługę ETag i If-Match dla PATCH i DELETE oraz rozpoznawanie niepewnego wyniku POST. Stabilny zapis zależy od wyniku tych prób.
- [ ] Żądać `Tasks.ReadWrite` dopiero przy aktywacji. Jedno powiązanie na listę, osobna karta, bez scalania list i zdalnego zarządzania listami.
- [ ] Zapisać powiązania, bazowy stan pól, rewizje, operacje offline, konflikty i kursory. Pobierać delta przed wysłaniem operacji. Zapisywać kursor atomowo ze zmianami. Po utracie kursora zachować kolejkę i odświeżyć całą listę.
- [ ] Synchronizować tylko tytuł, wykonanie, tworzenie i potwierdzone usuwanie zadań. W PATCH wysyłać wyłącznie zmienione pola. Zachowywać dokładny status Microsoft, gdy nie zmieniono wykonania.
- [ ] Scalać różne pola. Konflikt tego samego pola lub edycji z usunięciem wymaga wyboru. Blokować tylko konfliktowe zadanie. Nie identyfikować zadań po tytule.
- [ ] Wznawiać kolejkę po restarcie. Stosować Retry-After i opóźnienie przy przejściowych błędach. Nie powtarzać niejednoznacznego POST automatycznie.
- [ ] Usuwanie panelu, karty lub pulpitu nie wysyła DELETE. Przy odłączeniu zaoferować zachowanie lokalnej kopii wraz z niewysłanymi zmianami.
- [ ] Zweryfikować konflikty, wyścigi, przeniesienie zadań, cykliczność, awarie sieci i ochronę pól poza zakresem.

## Odbiór

Kompilacja kontrolna `v1.8.0-20261104-111642-383` (2026-11-04): Release bez ostrzeżeń, 350 testów, 104 rendery WPF i kontrola wtyczek przeszły. Test z izolowanym SQLite potwierdził aktualizację tej samej karty, ponowne wczytanie powiązania i zachowanie zadań lokalnych. Odbiór połączenia, automatycznego odświeżania i zmiany konta na rzeczywistej rejestracji Microsoft Entra pozostaje otwarty.

Kontrola migracji i importu Microsoft To Do (2026-11-04, zmiany robocze 1.8.0): kompilacja Release bez ostrzeżeń, 346 testów i kontrole wtyczek przeszły. Testy objęły migrację rzeczywistej struktury schematu 2 w izolowanej bazie, trwałość identyfikatorów i stronicowanie Graph z kontrolowanym transportem. Odbiór na rzeczywistym koncie, przywrócenie kopii i próby warunkowego zapisu pozostają otwarte.

Kontrola importu wielu plików (2026-11-04, zmiany robocze 1.8.0): kompilacja Release bez ostrzeżeń, 343 testy i kontrole integracji wtyczek przeszły. Test SQLite potwierdza wycofanie całej partii po błędzie zapisu jednej karty. Okna kreatora w obu motywach nadal wymagają ręcznego odbioru.

Kontrola zmian roboczych z 2026-11-04: skrypt projektu zakończył się kodem 0. Kompilacja Release bez ostrzeżeń, 342 testy i kontrole integracji wtyczek zakończone powodzeniem. Nie potwierdza to gotowości kreatora ani działania na rzeczywistym koncie Microsoft.

- [ ] Uruchomić testy projektu, kontrole WPF i git diff --check. Oddzielić wynik prób na koncie od testów automatycznych.
- [ ] Aktualizować listaZmian.md, wersje.md i changelog.txt. Dokumentować błędy w problemy.md i nie zapisywać tokenów ani treści użytkownika w logach.
- [ ] Przygotować odrębne wydania funkcjonalne dla importu, odczytu i synchronizacji. Nadać wersję przy przygotowaniu wydania.
