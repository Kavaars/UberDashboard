# Microsoft To Do w UberDashboard

Otwórz **Menu → Ustawienia → Integracje → Otwórz kreator Microsoft To Do**.

1. Wpisz identyfikator aplikacji (Application/Client ID) z Microsoft Entra i wybierz **Połącz konto i pobierz listy**. Przy zmianie konta użyj **Wybierz inne konto Microsoft**.
2. Wskaż panel **Lista zadań** na aktywnym pulpicie. Jeśli nie ma takiego panelu, dodaj go z menu pulpitu.
3. Zaznacz przy wybranych listach **Importuj kopię**, **Synchronizuj** lub obie opcje. Wybierz **Zapisz wybór i pobierz zadania**.

Import tworzy nową edytowalną kartę. Synchronizacja aktualizuje tytuły i stan wykonania w karcie tylko do odczytu, w kierunku Microsoft To Do → UberDashboard. Zapisane listy odświeżają się przy starcie i co minutę podczas pracy aplikacji. Aby odświeżyć je ręcznie, otwórz kreator, pobierz listy i zapisz wybór ponownie. Jeżeli sesja wygasła, kreator umożliwia ponowne zalogowanie, a karty pokazują ostatnią kopię. W tym samym miejscu możesz zmienić Client ID, konto, panel docelowy i wybór list. Odznaczenie listy lub rozłączenie konta zatrzymuje aktualizacje powiązanych kart, które zachowują ostatnie pobrane zadania.

## Konfiguracja klienta

1. Zarejestruj publiczną aplikację desktopową w Microsoft Entra. Aby obsłużyć konta osobiste i organizacyjne, wybierz rejestrację wielodostępną z kontami osobistymi Microsoft.
2. Dodaj platformę aplikacji mobilnych i desktopowych oraz adres przekierowania `http://localhost` dla przeglądarki systemowej. Włącz publiczne przepływy klienta.
3. Dodaj delegowane uprawnienie Microsoft Graph `Tasks.Read`. Polityka organizacji może wymagać zgody administratora.
4. Skopiuj Application/Client ID do kreatora. Przy pierwszym otwarciu kreator może wstępnie uzupełnić wartość z dotychczasowej zmiennej `UBERDASHBOARD_MS_TODO_CLIENT_ID`.

MSAL otwiera logowanie Microsoft w przeglądarce. Cache uwierzytelniania jest chroniony mechanizmem Windows i przechowywany w katalogu `Credentials` profilu aplikacji. **Rozłącz zapisane konto** czyści sesję aplikacji dla tego klienta.

[Konfiguracja klienta desktopowego Microsoft Entra](https://learn.microsoft.com/en-us/azure/active-directory/develop/scenario-desktop-app-registration) · [Odczyt zadań w Microsoft Graph](https://learn.microsoft.com/en-us/graph/api/todotasklist-list-tasks?view=graph-rest-1.0)

## Aktualizacja bazy

Schemat 3 przenosi zadania wbudowanych list ToDo z JSON do tabeli `TodoItem`. Przed migracją istniejącej bazy aplikacja zapisuje spójną kopię SQLite `pre-todo-v3-*.db` w katalogu `Backups` profilu. W bazie zachowuje też pierwotny JSON migrowanych list. Nieprawidłowe dane źródłowe przerywają transakcję.

Powrót do aplikacji sprzed migracji wymaga przywrócenia kopii bazy sprzed aktualizacji. Przed przywróceniem należy zamknąć aplikację i zachować osobną kopię aktualnych danych.
