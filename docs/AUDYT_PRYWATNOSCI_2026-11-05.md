# Kontrola prywatności przed upublicznieniem

Data: 2026-11-05. Repozytorium: https://github.com/Kavaars/UberDashboard. Punkt wyjścia `dfb5926`, z roboczym fundamentem synchronizacji.

## Wynik kontroli przepływu danych

- Nie znaleziono serwera autora, automatycznego raportowania awarii, analityki użycia, reklam ani treningu AI w kodzie aplikacji.
- To nie jest aplikacja działająca wyłącznie offline: Microsoft OAuth/Graph, Google OAuth/Calendar oraz Open-Meteo są rzeczywistymi odbiorcami żądań. Nazwa szukanego miejsca i współrzędne trafiają do Open-Meteo; dostawcy widzą także metadane połączeń.
- Dane pulpitów, kopie zadań, ustawienia, cache pogody i robocza kolejka synchronizacji są w SQLite w profilu. Google Calendar w dotychczasowym widoku trzyma wyniki głównie w pamięci, konfigurację i chronioną sesję osobno. Nowy silnik może utrwalać wydarzenia w SQLite.
- Cache Microsoft jest chroniony przez MSAL Extensions na Windows; sesja Google przez DPAPI CurrentUser. SQLite, logi, zwykłe pliki i backupy nie mają szyfrowania całego pliku przez aplikację.
- Serilog zapisuje lokalne logi, z rotacją i limitem 14 plików. Szczegóły wyjątków mogą zawierać ścieżki. Nie znaleziono zdalnego sinka logów. Zwykłe backupy domyślnie zachowują 10 kopii; backupy przed migracją mają osobne nazwy i nie należą do tej retencji.
- Odłączenie konta nie usuwa wszystkich lokalnych migawek i backupów ani danych w usłudze. Deinstalacja zachowuje profil. Polityka opisuje oddzielnie te działania.
- Uzupełnienie: potwierdzono, że wcześniejsza konfiguracja dopuszczała informacyjne logi HttpClientFactory; nowy override ogranicza je do ostrzeżeń i błędów. Nie potwierdzono zapisu pełnych współrzędnych w logach — .NET od wersji 9 domyślnie maskuje query URI. To dodatkowe ograniczenie diagnostyki, nie dowód wcześniej wykrytego wycieku. [Dokumentacja .NET](https://learn.microsoft.com/en-us/dotnet/core/compatibility/networking/9.0/query-redaction-logs).
- Sprawdzono także instalator: instaluje do katalogu programu w profilu, bez reguły usuwającej `%LOCALAPPDATA%\UberDashboard` podczas deinstalacji. Wersja robocza dodaje OpenID Connect `sub` Google do tożsamości powiązań; identyfikator i cel opisano w polityce.
- Monitor sprzętu i PhotoFrame używają lokalnych źródeł; użytkownik może wybrać zasoby sieciowe. Pluginy nie są izolowane i nie można gwarantować zachowania dowolnych pluginów osób trzecich.

## Kontrola repozytorium

- Gitleaks 8.30.1 pobrano z oficjalnego wydania gitleaks/gitleaks; przed uruchomieniem zweryfikowano SHA-256 archiwum z listą sum wydania.
- `gitleaks git . --log-opts=--all --redact=100`: 10 commitów, około 1,87 MB, kod 0, brak wykrytych sekretów.
- Osobny skan migawki bieżących śledzonych i nieignorowanych nowych plików: około 1,82 MB, kod 0, brak wykrytych sekretów. Migawka i zredagowane raporty są lokalnie pod ignorowanym `Release/Audits/privacy-20261105`.
- Przejrzano nazwy historycznych obiektów pod kątem poświadczeń, baz, logów, backupów, paczek kluczy i konfiguracji lokalnej. Nie znaleziono takich plików w historii. Metadane autora korzystają z domeny `users.noreply.github.com`.
- Zdalny GitHub w chwili kontroli: jedna gałąź `main`, brak wydań i załączników wydań, brak przebiegów Actions oraz brak zgłoszeń/PR w odpowiedzi API. Nie było dodatkowych danych tych typów do upublicznienia.
- Po publikacji polityki ponowiono skan całej historii (11 commitów, kod 0, brak wykrytych sekretów), zmieniono widoczność na publiczną i potwierdzono odczyt polityki bez logowania. Przed kolejnym push skany są ponawiane; lokalne raporty nie są dodawane do Git.
- Rozszerzono `.gitignore` o pliki OAuth, `.env`, klucze, cache i sesje. Identyfikatory aplikacji OAuth są konfiguracją publicznego klienta; plik JSON Google oraz poświadczenia sesji nie są dodawane do repozytorium.

Skaner nie stanowi matematycznej gwarancji braku wszystkich sekretów. Wynik dotyczy sprawdzonego zakresu i momentu; przed kolejnymi publikacjami skan należy ponowić. Raport nie zawiera sekretów ani treści danych użytkownika.

## Ograniczenia i zgodność

To przegląd kodu, konfiguracji zależności i plików repozytorium, nie przechwytywanie ruchu sieciowego wszystkich funkcji, audyt całego kodu bibliotek lub dowolnych pluginów ani opinia prawna. Nie potwierdzono rzeczywistych zgód i weryfikacji aplikacji w konsolach Microsoft/Google.

Zgodnie z decyzją właściciela stroną projektu jest adres repozytorium GitHub, a polityką plik `PRIVACY.md`. Google wymaga dla produkcyjnej weryfikacji także własności domen; sam publiczny adres GitHub nie potwierdza spełnienia tego warunku. Nie zmienia to decyzji o publikacji repozytorium i dokumentu; status publicznego OAuth pozostaje odrębnym etapem.

Źródła: [Google User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), [Google OAuth production compliance](https://developers.google.com/identity/protocols/oauth2/production-readiness/policy-compliance), [Open-Meteo privacy](https://open-meteo.com/en/terms), [GitHub privacy](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement).

## Aktualizacja po rozszerzeniu synchronizacji — 2026-11-05

- Dodano lokalny cache kompletnych zakresów kalendarza w SQLite 6. Zawiera tytuły, terminy i identyfikatory wydarzeń/serii; klucz rozdziela konto, klienta OAuth, kalendarz i strefę wyświetlania. Nie zawiera tokenów ani nowego połączenia z serwerem autora.
- Odłączenie konta blokuje dostęp do jego kopii przez bieżącą sesję, lecz nie kasuje wierszy cache ani backupów. Polityka PL/EN ujawnia ten zakres i sposób usunięcia całego profilu.
- Testy kontrolowanego HTTP/SQLite obejmują zmianę konta podczas pobierania i zapisu cache, brak konfiguracji, przerwane stronicowanie, starszą odpowiedź oraz izolację tożsamości. Nie jest to przechwycenie ruchu prawdziwego konta.
- Bieżący snapshot plików wersjonowanych i nowych plików źródłowych ponownie przeskanowano Gitleaks 8.30.1 z pełną redakcją wyników: brak wykrytych sekretów. Snapshot źródeł i raporty pozostały w ignorowanym Release/Audits; profile testowe są w Release/Dev.
