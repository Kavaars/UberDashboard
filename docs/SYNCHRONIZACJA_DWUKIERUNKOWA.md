# Synchronizacja dwukierunkowa — stan wdrożenia

2026-11-05. Baza prac: `dfb5926`, wersja 1.9.0. To kod roboczy, nie gotowe wydanie 1.10.0.

## Dostępne w kodzie roboczym

- Silnik Core/Application/Infrastructure: trzy wersje danych, scalanie niezależnych pól, konflikty edycji/usunięcia, grupy czasu i cykliczności, kolekcje po ID/e-mailu, trwała kolejka, rewizje, kopie odzyskiwania i rozpoznawanie niepewnego wyniku zapisu.
- SQLite 5: atomowy dokument powiązania wraz z outboxem, CAS i kopia przed migracją. Dawne migawki Microsoft są zachowane; lokalne importy nie są publikowane automatycznie. Migracja obejmuje oba wcześniejsze robocze warianty schematu 4, z metadanymi powiązania lub bez nich. Dokument JSON upraszcza transakcję, lecz wymaga pomiarów dla dużych zbiorów i ewentualnej normalizacji.
- Podłączony harmonogram hosta, także podczas pracy w zasobniku. Operacje jednego konta są serializowane. Blokady Retry-After obowiązują dla konta, również przy ręcznym odświeżaniu; stan błędu połączenia jest trwały.
- Powiązana karta To Do i panel Google Calendar udostępniają przycisk **Synchronizacja dwukierunkowa**. Pierwsza aktywacja osobno prosi o zgodę dostawcy na zapis; dotychczasowy import i odczyt nadal działają.
- Edytor zapisuje najpierw lokalnie, pokazuje kolejkę, błędy, konflikty, zatwierdzanie powiadomień, ponowienie, wstrzymanie i odnowienie zgody. Kontrola wersji blokuje nadpisanie elementu zmienionego podczas otwartego formularza.
- Zadania: tworzenie, tytuł, opis, dokładny status Microsoft i usuwanie. Niezmienione dane, w tym bogaty opis i statusy inne niż wykonane/niewykonane, pozostają zachowane.
- Wydarzenia: tworzenie, tytuł, opis, miejsce, terminy, cały dzień i usuwanie. Nieedytowane pola są zachowywane. Zmiana istniejącej serii wymaga potwierdzenia i dotyczy całej serii. Operacje na wydarzeniach z uczestnikami wymagają osobnego zatwierdzenia skutków przed wysłaniem.
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

1. Dedykowane UI i przypadki użycia wszystkich pól zadań, checklist, linkedResources, plików i katalogu kategorii; kompletne zarządzanie listami/kalendarzami, subskrypcjami oraz ACL. Podstawowe pomocnicze transporty nie oznaczają ukończenia tych funkcji.
2. Edycja uczestników/odpowiedzi, konferencje i Drive; pojedyncze wystąpienia, trwałe wyjątki anulowane oraz podział serii jako operacja wieloetapowa. Nowy edytor nie oferuje jeszcze tworzenia reguł cykliczności.
3. Pełny cache zakresów rozwiniętego widoku kalendarza offline. Dokumenty w edytorze są trwałe; zwykły widok dat nadal używa dotychczasowego pobierania rozwiniętych wydarzeń i może wymagać sieci.
4. Ręczna edycja połączonego rozwiązania konfliktu, ręczne uzgodnienie POST Microsoft bez znanego ID, kompletne odłączenie/usuwanie kont wraz z wyborem kopii i UI historii odzyskiwania. Obecny wybór konfliktu dotyczy wersji lokalnej lub wersji usługi, z ponownym sprawdzeniem serwera.
5. Zarządzanie widocznością zasobów dla interwałów 60/300 sekund; nowo aktywowane powiązania są obecnie traktowane jako widoczne. Synchronizacja kont działa niezależnie od timerów lokalnego odczytu widoków; odczyty projekcji nie zostały jeszcze w pełni skonsolidowane.
6. Próby na rzeczywistych kontach osobistych/organizacyjnych Microsoft i Gmail/Workspace; testy ETag/If-Match Microsoft, realnych odmów ról i limitów. Bez potwierdzonego wsparcia API nie deklarujemy atomowej ochrony równoczesnego zapisu Microsoft.
7. Pełny odbiór DPI/Narratora/klawiatury, fontu 24 DIP, dużych zbiorów, wielu kont, zakładek, zamykania i awarii w operacjach wieloetapowych. Automatyczne testy nie zastępują tych prób.
8. Weryfikacje wydawcy i publicznego OAuth. Właściciel wybrał GitHub jako stronę projektu; publiczne repo i polityka nie oznaczają spełnienia wymagań Google dotyczących własności domeny. Wydanie 1.10.0 nie zostało przygotowane.

## Prywatność i kontrola

Polityka odpowiada stwierdzonym przepływom: lokalny zapis nie wyklucza komunikacji z Microsoft/Google/Open-Meteo. Nie znaleziono automatycznej analityki autora lub wysyłania raportów awarii. Baza i zwykłe backupy nie są w całości szyfrowane. Szczegóły zakresu przeglądu i ograniczenia: [audyt prywatności](AUDYT_PRYWATNOSCI_2026-11-05.md).

Testy transportów używają kontrolowanego HTTP, testy DB izolowanych profili, a testy edytorów danych przykładowych. Nie wykonano prawdziwego logowania, modyfikacji kont użytkownika ani weryfikacji konsol dostawców.

Kontrola `sync-privacy-20261105-1010`: Release bez ostrzeżeń i błędów; 438 testów PASS (185 App, 87 Application, 95 Core, 71 Infrastructure); VisualChecks PL/EN po 161 PNG, w tym po 8 renderów nowych edytorów; PluginChecks PASS. Obejrzano także formularz w małym oknie i poprawiono stały dolny pasek akcji. Nie mierzono procentowego pokrycia nowych linii; nie pakowano instalatora ani wydania produkcyjnego.

## Źródła wymagań

- [Microsoft: uprawnienia i aktualizacja](https://learn.microsoft.com/en-us/graph/api/todotask-update?view=graph-rest-1.0)
- [Microsoft: delta](https://learn.microsoft.com/en-us/graph/api/todotask-delta?view=graph-rest-1.0)
- [Microsoft: throttling](https://learn.microsoft.com/en-us/graph/throttling)
- [Google: syncToken](https://developers.google.com/workspace/calendar/api/guides/sync)
- [Google: ETag](https://developers.google.com/workspace/calendar/api/guides/version-resources)
- [Google: zakresy](https://developers.google.com/workspace/calendar/api/auth)
- [Google: OAuth desktop](https://developers.google.com/identity/protocols/oauth2/native-app)
- [Google: wymagania publikacji](https://developers.google.com/identity/protocols/oauth2/production-readiness/sensitive-scope-verification)
