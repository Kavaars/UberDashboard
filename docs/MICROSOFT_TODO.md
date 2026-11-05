# Microsoft To Do w UberDashboard

Otwórz **Menu → Ustawienia → Integracje → Otwórz kreator Microsoft To Do**.

1. Wpisz identyfikator aplikacji (Application/Client ID) z Microsoft Entra i wybierz **Połącz konto i pobierz listy**. Przy zmianie konta użyj **Wybierz inne konto Microsoft**.
2. Wskaż panel **Lista zadań** na aktywnym pulpicie. Jeśli nie ma takiego panelu, dodaj go z menu pulpitu.
3. Zaznacz przy wybranych listach **Importuj kopię**, **Synchronizuj** lub obie opcje. Wybierz **Zapisz wybór i pobierz zadania**.

Import tworzy lokalną edytowalną kopię. Powiązana karta domyślnie odczytuje listę Microsoft. W wersji roboczej przycisk **Synchronizacja dwukierunkowa** pozwala osobno udzielić `Tasks.ReadWrite` i otworzyć edytor tworzenia, zmiany tytułu/opisu/statusu i usuwania zadań w tej liście. Lokalne importy nie są automatycznie publikowane. Dokładne statusy Microsoft są zachowywane, również poza prostym wykonane/niewykonane.

Zmiana jest najpierw zapisywana lokalnie wraz z kolejką. Edytor pokazuje oczekujące operacje, konflikty, ponowienie i wstrzymanie. **Centrum synchronizacji** w Integracjach umożliwia powrót do powiązań. Usunięcie samej karty nie wysyła usunięcia zadań do usługi. Powiązanie dwukierunkowe należy wstrzymać w jego edytorze; dotychczasowy kreator wyboru odczytu nie zarządza jeszcze wszystkimi nowymi powiązaniami. Pełny zakres API, odłączenie z kopiami oraz testy kont pozostają opisane jako otwarte w [stanie wdrożenia](SYNCHRONIZACJA_DWUKIERUNKOWA.md).

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
