# Załączniki i serie — prace implementacyjne

Punkt wyjścia: `4f35dbf`, 1.9.0. Polecenie: kontynuować do wdrożenia załączników oraz pełnej obsługi serii. Ten dokument jest dziennikiem wykonania, nie deklaracją gotowości.

## Zakres odbioru kodu

- Microsoft: lista/pobranie/usunięcie pliku, dodawanie do 25 MB, sesje uploadu z wznawianiem, pusty plik, niepewne ukończenie bez duplikatów i jawne uzgodnienie po ID.
- Google: kontekstowe drive.file, upload wznawialny z ID wygenerowanym przed tworzeniem, wybór plików dostępnych aplikacji, pobranie i przypięcie/odpięcie; osobne prawa Drive i kalendarza. Odpięcie nie kasuje pliku. Zmiany wydarzenia z plikami zachowują wersjonowanie i wymagane zatwierdzenia.
- Serie Google: tworzenie/edycja reguł, strefa IANA, pojedyncze wystąpienie, cała seria, to i następne, anulowanie i odtworzenie wyjątku; osobny cache wystąpień i zachowanie wyjątków anulowanych. Podział jest trwałą operacją etapową z rozpoznawaniem niepewnego wyniku. Zresetowane przyszłe wyjątki wymagają jawnej informacji i zachowania kopii odzyskiwania.
- Microsoft: zachowanie pełnego modelu powtarzania zadań, bez sugerowania istnienia API wystąpień analogicznego do Google.
- Offline, restart, quota, brak praw, konflikt podczas transferu/podziału, awaria po odpowiedzi serwera; PL/EN, oba motywy, małe okna, duży tekst, dostępne etykiety. Bez zmian publicznych kontraktów pluginów i personalizacji.

## Architektura

Trwałe transfery i podziały będą częścią dokumentu synchronizacji SQLite, z CAS i istniejącą kolejką pracy DB. Pliki wejściowe są najpierw kopiowane do katalogu zarządzanego profilu i haszowane. URL sesji transferu jest chroniony dla konta/operacji, nigdy logowany. Zależności blokują usunięcie właściciela pliku. Google może przygotować plik przed pierwszym wysłaniem wydarzenia; Microsoft czeka na potwierdzone ID zadania. Niepewnego tworzenia pliku Microsoft bez potwierdzonego ID nie powtarzamy automatycznie.

Podział serii zachowuje plan, pierwotny master, wybrane wystąpienie, docelową serię z uprzednio nadanym ID oraz stan etapów. Nie symulujemy edycji serii wieloma zapisami każdego wystąpienia. Zmiany skutków lub wersji wymagają ponownego przeglądu.

## Stan

- [x] Odczyt zasad i przegląd bieżącego kodu.
- [x] Fundament danych, migracja i pliki lokalne (SQLite 9 / dokument 6, kopia i unieważnienie starych zapisujących; migawki SHA-256).
- [x] Transfery Microsoft i Google, OAuth i trwały silnik.
- [x] Edytory/pobieranie/uzgadnianie plików.
- [x] Reguły i cache wystąpień, wyjątki, trwały podział serii.
- [ ] Integracja interfejsu i wszystkie testy automatyczne/WPF.
- [ ] Dokumentacja użytkownika/prywatności, skany i publikacja kodu.

Rzeczywiste konta i zgody dostawców pozostają osobnym odbiorem; testy kontrolowanych adapterów nie dowodzą zachowania usług produkcyjnych.

Końcowy odbiór kodu: Release 0 ostrzeżeń/błędów; 763 testy (207 App, 209 Application, 187 Core, 160 Infrastructure) PASS. PluginChecks PASS. Nowe okna WPF PL/EN: akcje, małe rozmiary, font 24, oba motywy i zamykanie kontekstu PASS; pełna regresja EN z kontrolą niepustych renderów. Gitleaks źródeł: bez wykrytych sekretów. Siatka panelu zachowuje poprzedni model projekcji ostatniego odczytu usługi; lokalne szkice są w edytorach. Pozostały zakres całego planu opisuje SYNCHRONIZACJA_DWUKIERUNKOWA.md.

### Punkt kontrolny implementacji

Dodano adaptery `MicrosoftTodoFileProvider`, `GoogleDriveFileProvider`, `SyncFileTransport`, silnik `SyncFileCoordinator`, DPAPI sesji, zgodę `drive.file` i okno plików. Testy ukierunkowane: 14 adapterów plików, 12 scenariuszy silnika, 3 magazynu plików oraz 32 magazynu synchronizacji/adapterów bazowych zakończone powodzeniem. W szczególności: Microsoft nie powtarza ostatniego niepewnego kawałka, Google pyta o sesję i znane ID, a aktualna lokalna rewizja nie jest zastępowana starszą.

Dodano Ical.Net 5.2.4, `GoogleRecurrence`, osobny cache wystąpień i zachowanie odwołanych wyjątków; szkic trwałego podziału `SyncSeriesCoordinator` oraz okna wystąpień/przeglądu/kopii. 15 testów reguł przeszło (COUNT z EXDATE, DST, całodniowe, RDATE/EXDATE i walidacja). Release kompiluje się bez ostrzeżeń. Pełny odbiór nowych okien, testy awarii podziału, projekcja panelu, dokumentacja i skany pozostają do wykonania — nie traktować tego punktu jako końca zadania.

Do dalszej kontroli: blokada edycji podczas podziału i ponowna zgoda po zmianie danych; przerwanie po skróceniu mastera; pełny snapshot nie może usuwać wygenerowanych wystąpień; anulowane wyjątki muszą dać się przywrócić; odtworzenie kopii wyjątku tworzy jawnie nowe pojedyncze wydarzenie. Wiele RRULE / RDATE może zmienić fazę reguły przy nowym DTSTART — interfejs wymaga przeglądu reguł przyszłej serii. Nie udawać atomowego podziału dwóch zasobów.
