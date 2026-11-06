# Rejestr problemów

## 2026-11-06T13:00:00+01:00 | 1.9.0 / 4f35dbf + zmiany robocze | załączniki i serie

- Zgłoszenie: wdrożyć załączniki i pełne przebiegi obsługi serii. Przed zmianą brakowało transferów plików, edytora reguł i podziału. Windows, .NET 10/WPF; rzeczywistych kont nie używano.
- Dodano trwałe migawki plików i transfery MS/Drive, osobną zgodę drive.file, bezpieczne niepewne wyniki, pobieranie i usuwanie/odpinanie. SQLite 9 / format 6 tworzy kopię i wymusza ponowny pełny odczyt Google przed zapisem załączników.
- Serie: reguły iCalendar, IANA/DST, cache wystąpień, trwałe anulowane wyjątki i etapy podziału z ID przed POST. Testy awarii po odpowiedzi i przed zapisem DB oraz timeoutu POST przechodzą bez powtarzania potwierdzonych etapów. Regresja najpierw wykazała pozostawienie wygenerowanych wystąpień po usunięciu mastera; propagacja usunięcia po zbudowaniu pełnego snapshotu naprawia ten przypadek.
- Oględziny PNG wykazały puste obrazy ze starszego narzędzia WPF. Udokumentowany przełącznik ShouldRenderEvenWhenNoDisplayDevicesAreAvailable w projekcie testowym i kontrola pikseli zapobiegają fałszywemu potwierdzaniu renderów. Kontrole interakcji przechodziły niezależnie; starsze puste PNG nie stanowią dowodu wizualnego.
- Nadal wymagane: rzeczywiste konta obu dostawców, obsługa polityk organizacyjnych, pełny ręczny DPI/Narrator oraz bramki wydawcy. Siatka kalendarza nadal pokazuje ostatni odczyt usługi; lokalne szkice są w edytorach.

## 2026-11-06T11:23:30+01:00 | 1.9.0 / 1a15764 + zmiany robocze | niepewne edycje i usuwanie

- Zgłoszenie: kontynuować pełny plan MS/Google. Środowisko: Windows, .NET 10/WPF, kontrolowane adaptery i izolowane SQLite. Oczekiwane: po niepewnym zapisie sprawdzić aktualny stan i świadomie zdecydować bez automatycznego powtórzenia żądania. Stan wyjściowy potwierdzony kodem: ReconcileUnknownAsync potwierdzał tylko rozpoznany skutek, a kolejka nie miała ręcznej ścieżki dla pozostałych edycji/usunięć. Nie wykonano reprodukcji na prawdziwym koncie.
- Implementacja: świeży GET podglądu i drugi GET przed atomowym lokalnym konfliktem; weryfikacja operacji, pełnej wersji elementu, ID/konta/zasobu/rodzica, ETag i metadanych. Wszystkie późniejsze operacje pozostają w kolejce. Wysłany szkic, bieżąca treść i odczyt usługi trafiają do historii po decyzji. Dalsze rozstrzygnięcie korzysta ze standardowego świeżego GET i ponownego zatwierdzania skutków. Sam przebieg podglądu/konfliktu nie wywołuje provider.WriteAsync.
- Test roboczego scalania najpierw wykazał utratę celowego cofnięcia pola do dawnej bazy podczas niepewnej wysyłki. Przyczyną było traktowanie zgodności z pierwotną bazą jako braku późniejszej edycji. Dodatkowe porównanie względem faktycznie wysłanego szkicu zachowuje ten zamiar. Regresja po poprawce przechodzi. Osobne testy najpierw wykazały przyjmowanie tworzenia, zmienionego mapowania i nieobsługiwanego rodzaju operacji przez nową ścieżkę; zawężono ją do UnknownOutcome Update/Delete o zgodnym ID, a tworzenie zachowuje wcześniejsze zabezpieczenia przed duplikatami.
- Regresje obejmują stare snapshoty, zmianę pól/ETag/metadanych, lokalną edycję podczas odczytu, tożsamość konta i rodzica, brak elementu/tombstone, Retry-After, anulowanie oraz błąd zapisu SQLite bez częściowego zatwierdzenia. Test trwałości ponownie otwiera bazę z konfliktem i historią, po czym rozstrzyga bez powtórzenia zapisu do dostawcy.
- WPF: rzeczywista ścieżka kolejka → podgląd → konflikt → wybór usługi, oba API i Update/Delete, błąd IO/timeout oraz ponowienie, nieaktualny podgląd i zamknięcie kontekstu. Skrócono opis dla małego okna. Usunięto pozostający tekst pobierania po błędzie i dodano kontrolę tego stanu. Brak elementu nie jest pokazywany jako dowód powodzenia żądania.
- Końcowo: Release 0 ostrzeżeń/błędów, 697 testów PASS (206 App, 185 Application, 172 Core, 134 Infrastructure), PL/EN po 321 PNG (po 32 nowe), PluginChecks i klucze lokalizacji PASS. Pokrycie wybranych plików i zakres odbioru: docs/SYNCHRONIZACJA_DWUKIERUNKOWA.md. Bez nowych zgód, migracji, wersji wydania lub zmiany personalizacji. Rzeczywiste konta, gwarancje współbieżności Microsoft, pełny ręczny DPI/Narrator/klawiatura i dalszy plan pozostają otwarte.

## 2026-11-06T08:56:40+01:00 | 1.9.0 / cab170c + zmiany robocze | konflikty i historia synchronizacji

- Zgłoszenie: kontynuować pełny plan obu usług. Środowisko: Windows, .NET 10/WPF, kontrolowane adaptery i izolowane SQLite. Oczekiwane: ręczny szkic konfliktu, porównanie trzech wersji i historia do lokalnego przywracania/usuwania kopii. W cab170c konflikt oferował tylko wybór lokalna/usługa w Yes/No, a historia nie miała interfejsu.
- Potwierdzona regresja kontroli podglądu: zmiana Baseline przy zachowanych Revision, Local i Remote nie była odrzucana. Test najpierw odtworzył przyjęcie starej decyzji; pełne porównanie bazy, lokalnej/zdalnej treści, tożsamości, ETag, rodzaju/rodzica, metadanych i oznaczeń konfliktu blokuje ten przypadek. Drugi test wykazał niepotrzebny odczyt dla elementu bez konfliktu; nowa kontrola odrzuca go przed siecią. Oba testy po poprawce przechodzą.
- Decyzja zachowuje trzy wersje w historii. Ręczna edycja wykorzystuje istniejące formularze i nie wysyła bezpośrednio do usług. Zwykłe zatwierdzanie skutków nadal obowiązuje, także dla uczestników Google. Przywrócenie starej kopii jest atomową edycją z outboxem, sprawdza podgląd i nie usuwa wcześniejszego niepewnego zapisu. Usunięcie kopii wymaga potwierdzenia i dotyczy tylko wskazanej pozycji historii.
- Trwałość: test SQLite ponownie otwiera połączenie po przywróceniu, potwierdza kolejkę i zachowanie poprzedniej treści, następnie sprawdza trwałe usunięcie tylko wybranej kopii. Błąd zapisu, stary podgląd i brak kopii nie zatwierdzają częściowej zmiany. Wspólna retencja 30 dni chroni oczekujące operacje oraz nierozstrzygnięte konflikty, także bez pozycji kolejki. Dwa początkowe testy retencji miały błędne porównanie referencji JSON po serializacji; poprawiono asercję wartości, bez osłabiania reguł.
- Oględziny pierwszego małego renderu ujawniły listę wypartą przez opis i akcje. Dodano automatyczną kontrolę co najmniej 40 DIP wysokości listy, ograniczono przewijany opis, schowano pusty status i skrócono etykiety. Kontrola wykryła też dłuższy wariant angielski; po poprawce oba języki przechodzą. Nie zmieniano globalnych stylów ani publicznych kontraktów pluginów.
- Weryfikacja końcowa: Release 0 ostrzeżeń/błędów, 669 testów PASS (206 App, 158 Application, 172 Core, 133 Infrastructure), WPF PL/EN po 289 PNG, w tym po 28 nowych, PluginChecks i klucze lokalizacji PASS. Źródła buildów/testów: sync-recovery-20261106-03/04. Rzeczywiste konta, DPI, Narrator, pełna ręczna klawiatura, duże historie i publiczna weryfikacja dostawców nie zostały odebrane. Wersja pozostaje 1.9.0, bez wydania 1.10.0 i bez nowej migracji bazy.

## 2026-11-06T08:30:45+01:00 | 1.9.0 / acbeb66 + zmiany robocze | subskrypcje i ACL Google

- Zgłoszenie: kontynuować pełny plan synchronizacji. Środowisko: Windows, .NET 10/WPF, izolowane SQLite i kontrolowane HTTP/OAuth bez logowania do usług. Oczekiwane: osobne zarządzanie własną listą kalendarzy i udostępnieniami z ochroną kolejek. W acbeb66 brakowało formularzy i zgód dla tych operacji; podłączono je do trwałego silnika.
- Potwierdzone inspekcją katalogu: pełny odczyt oparty na CalendarList traktował brak wcześniej znanego ID jako usunięcie globalnego kalendarza. Brak wpisu oznacza jednak także wypisanie. Regresja symuluje pustą listę i nadal istniejący zasób; dodatkowy GET zachowuje globalny kalendarz. Wypisanie ma własny dokument, znacznik i konflikt z niewysłaną zawartością.
- Delta CalendarList nie obejmuje wszystkich zmian readonly, m.in. nazwy i ról. Oddzielny pełny odczyt metadanych oraz kontekst ACL odświeżają uprawnienia bez zmiany kursora. Test utraty roli blokuje nawet wcześniej zatwierdzony zapis; przerwane metadane zachowują poprzedni kursor i szkic.
- Dwa testy pustej odpowiedzi bez items najpierw odtworzyły błąd odczytu, po poprawce przechodzą. Test cofnięcia wypisania w trakcie wysyłki odtworzył brak znanego ID w kolejce ponownego dodania; poprawka zachowuje ID i pozwala uzgodnić niepewny wynik bez kolejnego POST. Osobne testy anulowania niewysłanego szkicu i dopasowania scope reguły ACL przechodzą.
- SQLite 8 migruje wcześniejsze formaty do 5 i podnosi CAS, zachowując dane, kolejkę i kursory. Kopia pre-sync-v8 zawiera oryginalny format i rewizję, co potwierdzono odczytem backupu w teście migracji 7→8. Starszy otwarty zapis nie zastępuje nowego formatu. Paneli i personalizacji nie przepisano.
- Weryfikacja końcowa: Release 0 ostrzeżeń/błędów; 651 testów PASS (206 App, 141 Application, 172 Core, 132 Infrastructure); WPF PL/EN po 261 PNG i PluginChecks PASS. Walidacja, chronione role, publiczne ostrzeżenie, odrębne skutki wypisania, anulowanie oraz zamykanie okien przeszły automatyczne interakcje. Jedna powtórka na wcześniej użytym profilu WPF zawiodła na zachowanym postępie afirmacji; świeże profile przeszły w obu językach. Początkowy błąd kompilacji testu zgód (nieistniejący argument ParentId powiązania) poprawiono; końcowy build i testy powtórzono.
- Ograniczenia: brak rzeczywistych kont/zaproszeń/zmian ACL, pełnej ręcznej klawiatury, DPI i Narratora; automatyczne rendery nie dowodzą tych zachowań. Dalsze funkcje i publiczne weryfikacje pozostają otwarte, bez wydania 1.10.0. Szczegóły pokrycia i odbioru: docs/SYNCHRONIZACJA_DWUKIERUNKOWA.md.

## 2026-11-06T07:23:13+01:00 | 1.9.0 / 58af3b2 + zmiany robocze | katalogi zasobów i kontrast integracji

- Zgłoszenie: kontynuować pełny plan synchronizacji. Środowisko: Windows, WPF/.NET 10, izolowane SQLite, kontrolowane HTTP i zastępczy przebieg zgody bez przeglądarki. Oczekiwane: zarządzanie własnymi listami/kalendarzami z ochroną zależności i praw. W 58af3b2 istniały jedynie pomocnicze transporty; nowe formularze i katalogi podłączono do trwałego silnika.
- Ochrona danych: usunięcie całego zasobu sprawdza kolejki zawartości w innych powiązaniach, także bez widocznego panelu. Krótka blokada lokalna usuwa wyścig edycja/usunięcie bez blokowania edycji na czas sieci. Testy obejmują restart, niepewne usunięcie, konflikty zdalnego usunięcia, zmianę praw i odtworzenie nazwy pod nowym ID. Stare dokumenty nie są automatycznie przepinane ani publikowane.
- Dokumentacja Google potwierdza różnicę między rolą owner a wyłącznym prawem dataOwner do usuwania. Implementacja porównuje dataOwner z ID własnego kalendarza głównego, ponownie przed zapisem. Samo owner nie udostępnia usuwania. Zasoby systemowe/główne oraz kandydaci o zmienionych prawach są blokowani również przy ręcznym uzgadnianiu niepewnego tworzenia.
- Oględziny renderu Dark-catalog-GoogleCalendar-480x400.png ujawniły biały obszar listy z jasnym tekstem. Potwierdzona przyczyna: domyślny styl ListBox w Controls.xaml ustawiał tylko font, a okno dziedziczyło jasny tekst. Poprawka lokalna: wspólne powierzchnie i ThemedListBoxItem w listach centrum, edytora i wyboru istniejącego ID. Dodano automatyczną kontrolę kontrastu co najmniej 4,5:1 dla powierzchni listy i tekstu; to nie zastępuje ręcznego odbioru dostępności.
- Wstępne kompilacje ujawniły brak importu WindowFrame oraz konflikt nazwy pomocnika testowego Button z typem WPF; poprawiono je i powtórzono build. Release bez ostrzeżeń/błędów, 609 testów PASS oraz PluginChecks PASS. Dokładne końcowe rendery, pokrycie i ograniczenia zapisano w docs/SYNCHRONIZACJA_DWUKIERUNKOWA.md. Prawdziwe logowanie, operacje na kontach, DPI, Narrator, pełna klawiatura i publiczne weryfikacje nie zostały wykonane; brak wydania 1.10.0.

## 2026-11-06T06:16:00+01:00 | 1.9.0 / c326140 + zmiany robocze | opcje Google i przegląd skutków

- Zgłoszenie: kontynuować pełny plan synchronizacji. Środowisko: Windows, WPF/.NET 10, izolowane profile, kontrolowane HTTP. Oczekiwane: lokalna edycja uczestników, własnej odpowiedzi i ustawień wydarzenia bez utraty pozostałych danych, z jawnym przeglądem skutków. Faktyczne w c326140: formularz miał tylko podstawowe pola; rozszerzono go o opcje Google, z zachowaniem trwałej kolejki.
- Potwierdzona inspekcją przyczyna niepełnego przeglądu: Review wybierał wyłącznie Desired.attendees, jeśli istniało. Po usunięciu adresu z tego zbioru usuwana osoba nie pojawiała się na liście skutków. Odtworzenie na danych przykładowych: bazowe adresy A/B, docelowo B/C; stary wybór pokazywał B/C. Nie wykonano rzeczywistej wysyłki zaproszeń.
- Poprawka: porównanie tożsamości e-mail bez rozróżniania wielkości liter; listy dodawanych/usuwanych/pozostałych i dane przed/po zmianie. Zatwierdzanie przyjmuje sprawdzony podgląd i odrzuca zmianę jego bazy lub treści. Regresje Core, App i Application przeszły.
- Transport ponownie odczytuje uprawnienia kopii przed minimalnym PATCH z If-Match. RSVP gościa wysyła wyłącznie jego wpis i attendeesOmitted=true; edycja pełnej listy zachowuje zapisywalne dane innych osób, pomijając pola tylko do odczytu. Kolekcja niepełna lub rozwijana, utrata praw do wspólnych pól i specjalne typy wydarzeń nie prowadzą do zapisu przez zwykły edytor.
- Weryfikacja: Release 0 ostrzeżeń/błędów; 545 testów PASS; VisualChecks PL/EN po 201 PNG (w tym po 18 nowych); PluginChecks PASS. Obejrzano mały formularz i duży tekst. Pierwsza kompilacja ujawniła niejednoznaczną nazwę Control i brak adnotacji null w nowym teście; obie poprawiono, końcowy build oraz testy powtórzono. Nie wykonano logowania ani zmian na kontach, pełnej ręcznej klawiatury, DPI lub Narratora; nie przygotowano wydania. Szczegóły pokrycia i pozostałego zakresu: docs/SYNCHRONIZACJA_DWUKIERUNKOWA.md.

## 2026-11-05T21:50:08+01:00 | 1.9.0 / 1ac890a + zmiany robocze | relacje Microsoft i niepewne wyniki

- Zgłoszenie: kontynuować pełny plan synchronizacji. Środowisko: WPF/.NET 10, Windows, izolowane profile testowe i kontrolowane HTTP. Rzeczywiste konta Microsoft/Google nie były modyfikowane; brak ich odbioru pozostaje otwarty.
- W 1ac890a adapter znał endpointy relacji, lecz magazyn/edytor nie rozróżniał kroków i powiązań od zadań; wybór listy w ochronie konta dopuszczał tylko dokładny adres kolekcji zadań. Dodano rodzaj elementu, stabilne lokalne powiązanie nadrzędne, wspólną transakcję z outboxem i kontrolę właściwej listy przy endpointach relacji. Żadne dopasowanie nie korzysta z tytułu.
- Zależności: dziecko czeka na potwierdzony ID zadania; niepewny POST nie jest powtarzany. Zdalne usunięcie zadania kontra edycja dziecka tworzy konflikt; wybór odtworzenia przygotowuje nowe zdalne tożsamości, a wybór usunięcia archiwizuje lokalne treści. Usunięcie lokalnego zadania z niewysłanymi relacjami jest blokowane do sprawdzenia ich kolejki.
- Pierwszy test nowej akceptacji usunięcia ujawnił zbyt wczesną blokadę zależności dla pustej zmiany; kolejność walidacji poprawiono i regresja przeszła. Odtworzenie zadania po tombstone musi używać CREATE, co sprawdza dodatkowa asercja. Migracja 4/5/6 → 7 zachowuje kolejkę i podnosi CAS wraz z formatem JSON 1 → 2.
- Ręczne uzgodnienie: użytkownik wybiera istniejący ID i porównuje szkic wysyłki, aktualne dane lokalne oraz usługę. Powtórny GET i CAS blokują stare podglądy; nowsza lokalna edycja pozostaje lub tworzy konflikt. Kandydat z własnymi zmianami nie jest automatycznie wchłaniany. Testy obejmują zadania, kroki, mapowanie dzieci, zmianę po podglądzie i Retry-After.
- Analiza odczytów wykazała, że 408/503 z Retry-After nie były przekazywane do mechanizmu odroczenia odczytu. Dodano testy 408/429/503 i obsługę tych odpowiedzi; nie zmieniono ostrożnego traktowania niepewnych zapisów POST.
- Pierwszy odbiór WPF zakończył się brakiem klucza SyncRelatedUrlInvalid; dodano PL/EN i sprawdzono kompletność literalnych kluczy edytorów. Końcowy Release 0 ostrzeżeń/błędów; 522 testy PASS; VisualChecks PL/EN po 183 PNG i PluginChecks PASS. Obejrzano małe okno uzgodnienia i formularz z tekstem 24 DIP. Nie wykonano pełnej ręcznej klawiatury, DPI, Narratora, pomiaru dużych list ani prawdziwego OAuth. Szczegóły pokrycia i pozostający zakres: docs/SYNCHRONIZACJA_DWUKIERUNKOWA.md.

## 2026-11-05T20:56:22+01:00 | 1.9.0 / f0387a1 + zmiany robocze | kontynuacja synchronizacji

- Zgłoszenie: kontynuować pełny plan i oddzielić publiczną instrukcję użytkownika od dokumentacji rozwoju. README opublikowano jako f0387a1; pozostały zakres integracji nadal OTWARTY.
- Przy przeglądzie f9404d7 potwierdzono błąd Complete: po potwierdzeniu starszej wysyłki podmieniano bazę kolejnego zapisu bez scalenia jego treści. Odtworzenie automatyczne: zmienić lokalny tytuł podczas wysyłki i zwrócić niezależną zmianę opisu serwera; następny zapis próbował cofnąć opis. Drugi test potwierdził brak konfliktu dla sprzecznej nowszej zmiany tego samego pola. Oba testy najpierw FAIL, po poprawce PASS.
- Poprawka: trzystronne scalanie odpowiedzi z nowszą rewizją, blokada konfliktu i historia odzyskiwania; nowy ID Google przy cofnięciu usunięcia w trakcie wysyłki. Zmieniony opis Microsoft kodowany do HTML zgodnie z dokumentacją endpointu.
- Rozszerzono zadania o ważność, kategorie, daty, przypomnienie i cykliczność. Dodano wspólny cache kompletnych zakresów kalendarza, izolację tożsamości, ochronę starszej odpowiedzi i przerwanego stronicowania oraz migrację 4/5 → 6 z kopią i zachowaniem kolejki. Zmiana zakresu anuluje i odrzuca starsze pobieranie panelu.
- Kontrola renderów ujawniła techniczny tekst opcji i systemowe białe pole daty; poprawiono etykiety i lokalny szablon. Automatycznie sprawdzono wybór daty, małe okno, PL/EN i font 24 DIP. Pełny ręczny odbiór rozwijanego kalendarza, DPI/Narratora i kont dostawców niewykonany.
- Weryfikacja: Release bez błędów/ostrzeżeń; 489 testów PASS; VisualChecks PL/EN i PluginChecks PASS. Pokrycie linii nowych plików logiki zadań 98,1%, cache DB 96,1%; całego SyncCoordinator 84,2%. Nie jest to pokrycie całego App. Szczegóły i ograniczenia w docs/SYNCHRONIZACJA_DWUKIERUNKOWA.md.

## 2026-11-05T10:35:26+01:00 | 1.9.0 / 78633b6 + zmiany robocze | prywatność i dalsza synchronizacja

- Zgłoszenie: kontynuować dwukierunkową integrację, użyć GitHuba jako strony projektu i sprawdzić rzeczywiste przepływy danych przed publikacją polityki. Właściciel jawnie zlecił zmianę repozytorium na publiczne po sprawdzeniu plików i sekretów.
- Potwierdzono w kodzie: brak serwera autora i automatycznych raportów analitycznych/awarii; połączenia z Microsoft, Google i Open-Meteo są jednak rzeczywiste. Baza, logi i backupy nie są w całości szyfrowane. Poświadczenia sesji są chronione oddzielnie. Przegląd nie zastępuje przechwytywania całego ruchu ani audytu dowolnych pluginów.
- Audyt Git/GitHub: Gitleaks z redakcją, historia i bieżące pliki, kontrola nazw historycznych plików oraz metadanych GitHuba; brak wykrytych sekretów. Polityka PL/EN została opublikowana, a publiczna widoczność i dostęp bez logowania potwierdzone. Google JSON pozostał poza repo; sprawdzono tylko zgodność typu i identyfikatora.
- Wdrożono podstawowy edytor, harmonogram i powiązania kont; dodatkową zgodę na zapis; ochronę starych sesji i nowszych lokalnych/remote edycji; stan limitów i Retry-After; wstrzymanie po odznaczeniu listy Microsoft. Pełny plan nadal OTWARTY, szczegóły w docs/SYNCHRONIZACJA_DWUKIERUNKOWA.md.
- Ograniczono logowanie HTTP. Test wykazał wcześniejsze dopuszczenie INFO, nie potwierdził zapisu pełnych współrzędnych; domyślny .NET maskuje parametry URI. To wzmocnienie prywatności, nie potwierdzony wyciek.
- Regresje: błędna tożsamość/zgoda, odłączenie i odznaczenie listy, przerwany zapis pliku, edycja podczas wysyłania i otwartego formularza, starszy wybór konfliktu, potwierdzony POST z nieudanym GET, oba warianty roboczego schematu 4. Początkowy test WPF miał nieutworzony uchwyt właściciela; poprawiono test, a powtórzony odbiór przeszedł.
- Wyniki: Release PASS, 0 ostrzeżeń/błędów; 438 testów PASS; VisualChecks PL/EN po 161 PNG PASS; PluginChecks PASS. Nie wykonano logowania kont użytkownika, zapisu w rzeczywistych usługach, DPI/Narratora ani publicznej weryfikacji wydawców; nie pakowano wydania.

## 2026-11-05T09:23:23+01:00 | 1.9.0 / dfb5926 + zmiany robocze | integracje jednokierunkowe

- Moduły: Microsoft To Do i Google Calendar; Windows, WPF/.NET 10, SQLite. Typy rzeczywistych kont i produkcyjne rejestracje OAuth niepodane.
- Zgłoszenie: obie integracje muszą działać w obu kierunkach. Oczekiwane: trwała edycja lokalna wysyłana do usług, odbiór zmian zdalnych, konflikty i offline. Faktyczne: panele nadal korzystają z poprzedniego odczytu.
- Analiza ostatnich zmian: dfb5926 zachował implementację odczytu kalendarza i wcześniejsze odświeżanie Microsoft. W MicrosoftTodoSettings.CreateDocument gubione są zdalne ID; CalendarEntry jest projekcją do wyświetlania. Brakuje ścieżki zapisu i trwałej kolejki; nie jest to potwierdzona regresja logowania. Odtworzenie na rzeczywistym koncie nie było wykonane.
- Stan: OTWARTE. Dodano fundament Core/Application/Infrastructure, CAS i outbox w SQLite, scalanie i podstawowe transporty. Nie podłączono OAuth zapisu, edytorów ani wszystkich funkcji planu. Nie wolno uznać zgłoszenia za naprawione.
- Regresje: testy niezależnych pól, konfliktów i kolekcji, atomowości/CAS, kont/powiązań, delta/syncToken, stronicowania, błędów HTTP, niepewnego POST i nowszej edycji podczas wysyłania. Kontrole na rzeczywistych kontach i pomiary pokrycia pozostają niewykonane.
- Weryfikacja robocza: Release 0 ostrzeżeń/błędów, 421 testów PASS, dotychczasowe VisualChecks PL/EN i PluginChecks PASS. Szczegóły i pozostały zakres: docs/SYNCHRONIZACJA_DWUKIERUNKOWA.md.
- Wcześniejszy nieudany build bez szczegółów nie ma potwierdzonej przyczyny. W tej sesji proces testowy bez szerszego dostępu przekroczył limit połączenia testhost; zatwierdzone uruchomienie tych samych testów przeszło. Pierwszy długi przebieg kompilacji trafił dodatkowo na celowo dodany jeszcze bez implementacji test regresji; nie jest to błąd bazowego commita.

## 2026-11-05T07:46:00+01:00 | źródła 1.8.0 / b1512b0 + zmiany robocze | odtwarzanie okna po autostarcie

- Moduły: Shell/MainWindowModeCoordinator, MainWindow, ustawienia uruchamiania. Windows, WPF/.NET 10; dwa monitory, dokładne DPI, wersja Windows i numer uruchomionej kompilacji nieznane.
- Zgłoszenie i odtworzenie: włączyć autostart, pozostawić aplikację jako pasek na drugim monitorze, wyłączyć i uruchomić komputer. Według użytkownika aplikacja wraca jako okno na pierwszym monitorze. Oczekiwane: poprzedni tryb i położenie. Komunikatów błędów nie podano.
- Analiza zmian: ostatni commit b1512b0 dotyczy widoczności przycisku na pasku zadań, nie zapisu trybu. Koordynator odczytuje LaunchMode, ale przełączanie go nie utrwala; monitor automatyczny pozostaje null. Brak zapisu geometrii zwykłego okna. Istniejące wpisy dotyczą przeskakiwania przy zmianie konfiguracji paska, bez testu restartu.
- Potwierdzone przyczyny w kodzie: brak utrwalania udanych przełączeń, brak utrwalania automatycznie wybranego monitora paska i brak magazynu położenia zwykłego okna. Nie potwierdzono dokładnej zapisanej konfiguracji użytkownika ani związku z b1512b0.
- Poprawka 1.8.1: udane jawne przełączenia zapisują tryb i monitor, inicjalizacja/fallback i tymczasowe oddokowanie do zasobnika nie zmieniają preferencji. Osobny autozapis geometrii w pikselach, rozmiaru i maksymalizacji; zapis jest asynchroniczny, scalany po 300 ms i opróżniany przed zwykłym zamknięciem. Geometria paska nie zastępuje zwykłego okna; pozycja spoza dostępnych ekranów jest dopasowywana do obszaru roboczego. Ustawienia grubości zachowują aktualnie zapamiętany tryb.
- Regresje: restart koordynatora z zapamiętanym DISPLAY2 i oknem startującym na DISPLAY1, brak monitora, nieudane dokowanie, tymczasowy tryb zasobnika, jawny powrót do okna, nieaktualny tryb w otwartych ustawieniach, ujemne współrzędne i ograniczanie obszaru. Kontrola WPF na osobnym profilu potwierdza zapis/odtworzenie rzeczywistej geometrii, izolację geometrii paska i maksymalizację.
- Weryfikacja 2026-11-05T07:54:00+01:00: build Release 0 ostrzeżeń/błędów, 372 testy PASS, VisualChecks PASS, PluginChecks PASS, publish win-x64 self-contained PASS. Początkowe uruchomienia .NET blokowały ograniczenia dostępu do usług Windows; sprawdzenia wykonano skutecznie z zatwierdzonym szerszym dostępem. Błędy kompilacji testu i składni poprawiono przed końcowymi kontrolami.
- Kompilacja: Release/Dev/v1.8.1_autostart-20261105-075255-447. Nie wykonano pełnego restartu Windows, testu fizycznych dwóch monitorów ani opóźnionego pojawienia się drugiego monitora przy logowaniu. Istniejący wpis autostartu wskazuje dotychczasowy plik: po przejściu do nowego folderu kompilacji trzeba wyłączyć i ponownie włączyć autostart w nowej aplikacji.

## 2026-11-04T21:41:46+01:00 | źródła 1.8.0 / c7ee972 | przycisk zadokowanego okna na pasku zadań

- Moduły: okno główne, tryb paska, zasobnik. Środowisko: Windows 11, WPF/.NET 10. Numer kompilacji użytkownika nieznany.
- Odtworzenie: przełączyć aplikację w tryb paska. Przycisk aplikacji pozostaje na pasku zadań Windows. Oczekiwane: widoczny zadokowany pasek i ikona w zasobniku.
- Przyczyna w źródłach: dokowanie nie zmieniało ShowInTaskbar, a przywracanie z zasobnika ustawiało tę właściwość zawsze na true.
- Poprawka: dokowanie zapamiętuje i ukrywa przycisk, wyjście z trybu przywraca jego stan, a tryb paska wymusza widoczną ikonę w zasobniku.
- Regresja: kontrola WPF potwierdziła błąd przed poprawką, a po niej ukrywanie przycisku przy dokowaniu i przywracanie jego widoczności po oddokowaniu.

## 2026-11-04T11:31:34+01:00 | źródła 1.8.0 / 23135b8 | kwadraty na pasku tytułu

- Moduł i środowisko: wspólny szablon paska tytułu, Windows 11, WPF/.NET 10. Numer kompilacji uruchomionej przez użytkownika nieznany.
- Odtworzenie: otworzyć główne okno lub Ustawienia po zmianie motywu ramek. Oczekiwane są ikony minimalizacji, maksymalizacji lub przywracania oraz zamknięcia. Na zrzutach przyciski pokazują kwadraty.
- Przyczyna: tekst przycisków w szablonie dodanym w 23135b8 był renderowany w Segoe UI mimo ustawienia fontu ikon na przycisku. Walidacja nagłówka przed poprawką zwróciła `Missing icon glyph in Segoe UI`.
- Poprawka: ikony wektorowe w szablonie nagłówka korzystają z koloru tekstu przycisku. Kontrola WPF sprawdza ikony i renderuje nagłówek w stanie normalnym i zmaksymalizowanym dla motywów jasnego, ciemnego i systemowego. Sześć renderów nagłówka sprawdzono wizualnie.

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
