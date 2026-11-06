# Macierz integracji — kod roboczy 1.9.0

Stan: 2026-11-06, baza `58af3b2` i etap `sync-containers-20261106-01`. Publiczne API: Microsoft Graph v1.0 i Google Calendar v3. To opis implementacji i pozostałych bramek, nie certyfikat zgodności ani odbiór na rzeczywistych kontach.

| Funkcja | Endpointy używane przez aplikację | Zgoda delegowana aplikacji | Konto i ograniczenia | Odbiór |
| --- | --- | --- | --- | --- |
| Katalog list Microsoft | `/me/todo/lists/delta`, `/me/todo/lists/{id}` | Tasks.ReadWrite po włączeniu zapisu | Osobiste i organizacyjne; dostęp zależy od zgód i zasad organizacji | Testy HTTP; prawdziwe konta otwarte |
| Tworzenie/zmiana/usunięcie listy | POST `/me/todo/lists`, PATCH/DELETE `/me/todo/lists/{id}` | Tasks.ReadWrite | Tylko własne listy użytkownika; isOwner i wellknownListName sprawdzane przed zapisem. Listy systemowe zablokowane | HTTP, silnik, SQLite i WPF |
| Zadania i relacje | `/me/todo/lists/{id}/tasks`, delta, checklistItems, linkedResources | Tasks.ReadWrite | Dokładne statusy i stabilne ID; oddzielne relacje. Niepewny POST bez ID nie jest ponawiany | Automatyczny; ETag/If-Match Microsoft nadal do prób |
| Odkrywanie kalendarzy | `/users/me/calendarList`, `/users/me/calendarList/{id}` i alias `primary` | calendar.calendarlist.readonly | Gmail/Workspace z włączonym Calendar; ukryte zasoby, stronicowanie, role i ID własnego kalendarza głównego | Kontrolowane HTTP, bez rzeczywistych kont |
| Globalne właściwości kalendarza | GET/PATCH `/calendars/{id}`, POST `/calendars` | Osobne calendar.calendars | Nazwa/opis/miejsce/strefa; edycja z potwierdzoną rolą owner. To inne dane niż osobisty summaryOverride w CalendarList | HTTP, walidacja, kolejka, WPF |
| Usunięcie kalendarza | DELETE `/calendars/{id}` | calendar.calendars | Tylko dodatkowy kalendarz potwierdzonego dataOwner. Sama rola owner nie wystarcza. Ponowny odczyt, If-Match i osobne zatwierdzenie całej zawartości | Automatyczny; Gmail/Workspace otwarte |
| Wydarzenia | `/calendars/{id}/events`, syncToken i strony | calendar.events; dotychczasowy odczyt używa calendar.events.readonly | Uprawnienia kalendarza i kopii wydarzenia; 410, 412, jawne sendUpdates, własny RSVP z attendeesOmitted | Automatyczny; rzeczywiste role i zaproszenia otwarte |
| Subskrypcje i osobiste ustawienia CalendarList | Docelowo POST/PATCH/DELETE CalendarList | Docelowo osobne calendar.calendarlist | Nie jest to usunięcie samego kalendarza | **Niewdrożone w interfejsie i zgodach** |
| Udostępnianie kalendarza | Docelowo `/calendars/{id}/acl` | Docelowo osobne calendar.acls | Ograniczenia właściciela i polityki Workspace; osobny przegląd skutków | **Niewdrożone w interfejsie i zgodach** |

Transport pomocniczy endpointu nie oznacza gotowej funkcji. Załączniki, Drive, konferencje, pełna edycja serii, etykiety/godziny pracy kalendarzy i katalog kategorii wymagają kolejnych etapów oraz własnych prób kontraktowych. Aktualny pełny zakres i ograniczenia: [stan synchronizacji](SYNCHRONIZACJA_DWUKIERUNKOWA.md).

Katalogi używają formatu dokumentu 3, zawartość formatu 2; wspólna baza SQLite pozostaje w schemacie 7. Local ID i Remote ID nie są dopasowywane po nazwie. Pełne usunięcie kontenera nie usuwa lokalnych dokumentów jego zawartości, a odtworzenie nazwy pod nowym Remote ID nie przenosi ich automatycznie. Oczekujące zmiany i konflikty zachowują ochronę przy restarcie.

## Źródła i bramki dostawców

- [Microsoft: model listy i ograniczenia systemowe](https://learn.microsoft.com/en-us/graph/api/resources/todotasklist?view=graph-rest-1.0), [aktualizacja i zgody kont](https://learn.microsoft.com/en-us/graph/api/todotasklist-update?view=graph-rest-1.0).
- [Google: Calendars i CalendarList](https://developers.google.com/workspace/calendar/api/concepts/events-calendars), [model CalendarList, role i dataOwner](https://developers.google.com/workspace/calendar/api/v3/reference/calendarList).
- [Google: właściwości kalendarza](https://developers.google.com/workspace/calendar/api/v3/reference/calendars), [usuwanie kalendarza dodatkowego](https://developers.google.com/workspace/calendar/api/v3/reference/calendars/delete), [zakresy OAuth](https://developers.google.com/workspace/calendar/api/auth).

Do wydania pozostają próby na izolowanych kontach MSA, organizacyjnym Microsoft, Gmail i Workspace; rzeczywiste skutki zaproszeń/usuwania, odmowy ról i limity; weryfikacje wydawców oraz pełny ręczny odbiór WPF. Zewnętrzne zasady mogą się zmieniać. Sama obecność kodu i pozytywne testy HTTP nie potwierdzają przyznania dostępu przez dostawcę lub administratora.
