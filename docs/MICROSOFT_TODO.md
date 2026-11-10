# Microsoft To Do w UberDashboard

Otwórz **Menu → Ustawienia → Integracje → Otwórz kreator Microsoft To Do**.

1. Wpisz identyfikator aplikacji (Application/Client ID) z Microsoft Entra i wybierz **Połącz konto i pobierz listy**. Przy zmianie konta użyj **Wybierz inne konto Microsoft**.
2. Wskaż panel **Lista zadań** na aktywnym pulpicie. Jeśli nie ma takiego panelu, dodaj go z menu pulpitu.
3. Zaznacz przy wybranych listach **Importuj kopię**, **Synchronizuj** lub obie opcje. Wybierz **Zapisz wybór i pobierz zadania**.

Import tworzy lokalną edytowalną kopię. Połączenie konta wymaga zgody `Tasks.ReadWrite`. Zapisanie wyboru list włącza dla nich synchronizację dwukierunkową; pozycja **Synchronizacja dwukierunkowa** w menu kontekstowym zakładki otwiera edytor zadań; pole i przycisk + w panelu dodają zadania do kolejki synchronizacji. Wcześniej ręcznie wstrzymane synchronizacje pozostają wstrzymane. Lokalne importy nie są automatycznie publikowane. Dokładne statusy Microsoft są zachowywane, również poza prostym wykonane/niewykonane.

Zmiana jest najpierw zapisywana lokalnie wraz z kolejką. Edytor pokazuje oczekujące operacje, konflikty, ponowienie i wstrzymanie. **Centrum synchronizacji** w Integracjach umożliwia powrót do powiązań. Usunięcie samej karty nie wysyła usunięcia zadań do usługi. Powiązanie dwukierunkowe należy wstrzymać w jego edytorze; dotychczasowy kreator wyboru odczytu nie zarządza jeszcze wszystkimi nowymi powiązaniami. Pełny zakres API, odłączenie z kopiami oraz testy kont pozostają opisane jako otwarte w [stanie wdrożenia](SYNCHRONIZACJA_DWUKIERUNKOWA.md).

## Konfiguracja klienta

1. Zarejestruj publiczną aplikację desktopową w Microsoft Entra. Aby obsłużyć konta osobiste i organizacyjne, wybierz rejestrację wielodostępną z kontami osobistymi Microsoft.
2. Dodaj platformę aplikacji mobilnych i desktopowych oraz adres przekierowania `http://localhost` dla przeglądarki systemowej. Włącz publiczne przepływy klienta.
3. Dodaj delegowane uprawnienie Microsoft Graph `Tasks.ReadWrite`. Polityka organizacji może wymagać zgody administratora.
4. Skopiuj Application/Client ID do kreatora. Przy pierwszym otwarciu kreator może wstępnie uzupełnić wartość z dotychczasowej zmiennej `UBERDASHBOARD_MS_TODO_CLIENT_ID`.

MSAL otwiera logowanie Microsoft w przeglądarce. Cache uwierzytelniania jest chroniony mechanizmem Windows i przechowywany w katalogu `Credentials` profilu aplikacji. **Rozłącz zapisane konto** czyści sesję aplikacji dla tego klienta.

[Konfiguracja klienta desktopowego Microsoft Entra](https://learn.microsoft.com/en-us/azure/active-directory/develop/scenario-desktop-app-registration) · [Odczyt zadań w Microsoft Graph](https://learn.microsoft.com/en-us/graph/api/todotasklist-list-tasks?view=graph-rest-1.0)

## Aktualizacja bazy

Schemat 3 przenosi zadania wbudowanych list ToDo z JSON do tabeli `TodoItem`. Przed migracją istniejącej bazy aplikacja zapisuje spójną kopię SQLite `pre-todo-v3-*.db` w katalogu `Backups` profilu. W bazie zachowuje też pierwotny JSON migrowanych list. Nieprawidłowe dane źródłowe przerywają transakcję.

Powrót do aplikacji sprzed migracji wymaga przywrócenia kopii bazy sprzed aktualizacji. Przed przywróceniem należy zamknąć aplikację i zachować osobną kopię aktualnych danych.


Zakładkę powiązanej listy można usunąć lokalnie albo wybrać usunięcie również listy Microsoft To Do po potwierdzeniu. Usunięcie z konta korzysta z kolejki synchronizacji; lista z oczekującymi zmianami lub bez odpowiednich uprawnień nie zostanie usunięta. W razie niepotwierdzonej operacji zakładka pozostaje, a stan operacji można sprawdzić w centrum synchronizacji.

### Dziennik integracji

W Ustawieniach → Integracje można włączyć lub wyłączyć logowanie zdarzeń, odświeżyć kopiowalny dziennik i wyczyścić jego wpisy. Logowanie jest domyślnie włączone; ustawienie pozostaje zapisane po ponownym uruchomieniu. Dziennik obejmuje wybrane błędy synchronizacji w tle i uruchamianej w edytorze, odświeżania oraz logowania Microsoft i Google. Zawiera czas, usługę, etap, typ błędu, kod HTTP oraz bezpieczny kod przyczyny i moduł, jeżeli są dostępne. Nie zapisuje komunikatów wyjątków, tokenów, adresów kont ani treści zadań. Plik `LOGS/integration.txt` w profilu aplikacji jest ograniczony do około 1 MB; po osiągnięciu limitu starsze wpisy są zastępowane. Wyłączenie zatrzymuje nowe wpisy, nie usuwa istniejących.
