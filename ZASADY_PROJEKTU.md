# Zasady projektu UberDashboard

Data: 2026-11-05, Europe/Warsaw. Dokument normatywny dla pracy nad tym repozytorium.

## Obowiązek AI przed utworzeniem panelu

**Przed rozpoczęciem projektowania, generowania kodu lub tworzenia nowego panelu AI MUSI przeczytać w całości ten plik oraz [Wytyczne paneli](docs/WYTYCZNE_PANELI.md). Musi przestrzegać obu dokumentów podczas implementacji, testowania i weryfikacji panelu.** Dotyczy to paneli wbudowanych, paneli wtyczek i rozbudowy istniejącego panelu. Samo przeczytanie nagłówków lub znalezienie podobnego panelu nie spełnia tego obowiązku.

AI ma przed implementacją krótko wskazać używane kontrakty, sposób zapisu, integrację z hostem i plan sprawdzenia wyglądu. Przed zakończeniem ma przejść listę odbioru z wytycznych i jawnie wymienić niewykonane kontrole lub odstępstwa. Nie wolno uznać panelu za zgodny tylko dlatego, że się kompiluje.

Te zasady uzupełniają [AGENTS.md](AGENTS.md), który określa ogólny proces pracy. Bieżące instrukcje użytkownika mają pierwszeństwo; konflikt lub świadomy wyjątek należy opisać, zamiast po cichu pomijać wymaganie. Starsze plany i raporty są kontekstem historycznym, a nie potwierdzeniem obecnej implementacji.

## Technologia i granice odpowiedzialności

- Projekt jest aplikacją Windows 11: C#, .NET 10, WPF/XAML, MVVM, CommunityToolkit.Mvvm i lokalna baza SQLite. Zachowuj istniejący stos i konwencje.
- `src/UberDashboard.Core` zawiera modele i reguły domeny; `Application` przypadki użycia i abstrakcje; `Infrastructure` bazę, pliki i integracje; `Platform.Windows` integrację Win32; `App` kompozycję, WPF i funkcje w `Features/<Nazwa>`; `Contracts` publiczne API paneli i wtyczek.
- Nie przenoś zależności WPF ani infrastruktury do domeny. Wtyczki korzystają z publicznych kontraktów, a nie wewnętrznych implementacji aplikacji.
- Host odpowiada za pulpity, układ, wspólną kartę, zakładki, wygląd grupy i orkiestrację zapisu. Panel odpowiada za treść, własne opcje, stan i zasoby. Nie twórz konkurencyjnego systemu tych samych funkcji.
- Nie zmieniaj logiki oznaczonej `// CONTRACT (AI-READ-ONLY)` bez wyraźnej zgody użytkownika.

## Sposób prowadzenia zmian

1. Stosuj EXPLORE → PLAN → TEST → IMPLEMENT → VERIFY → DOCUMENT. Najpierw sprawdź Git status, ostatnie commity, odpowiednie różnice, architekturę i lokalne instrukcje.
2. Zachowaj zastane zmiany. Nie resetuj ich i nie dołączaj do commita niezwiązanych plików. Zanim zmienisz plik już edytowany, poznaj jego różnice.
3. Wyjaśnij istotne decyzje, przypadki brzegowe i skutki uboczne przed większą implementacją. Preferuj istniejące komponenty i najprostsze rozwiązanie spełniające wymagania.
4. Dla zgłoszonych błędów prowadź `problemy.md`: czas ISO 8601 ze strefą, wersja/commit, moduł, oczekiwany i faktyczny wynik, znana reprodukcja, potem potwierdzona przyczyna, naprawa i regresja. Nie zgaduj brakujących danych.
5. Najpierw sprawdzaj związek błędu z ostatnimi zmianami. Dodaj test regresji przed naprawą, gdy jest to praktyczne.
6. Kod, dane testowe, logi, historia i paczki nie mogą zawierać sekretów ani niepotrzebnych danych osobowych.

## Dane, wydajność i Windows

- Zachowuj identyfikatory instancji, dane i ustawienia przy ponownym uruchomieniu, zmianie pulpitu, trybu okna, zakładki i układu. Zmiana wyglądu nie może zmieniać treści.
- Dane użytkownika należą do profilu aplikacji, poza instalacją i `Release/`. Używaj istniejących magazynów i ścieżek; nie wprowadzaj drugiej bazy lub osobnego pliku konfiguracji bez uzasadnienia.
- Operacje DB, sieciowe, plikowe i kosztowne obliczenia wykonuj poza UI. W tym projekcie samo wywołanie metody SQLite z sufiksem `Async` nie gwarantuje przeniesienia pracy poza UI; korzystaj z istniejącej infrastruktury kolejkującej.
- Obsługuj anulowanie, wyścigi odpowiedzi, zamykanie i wstrzymywanie. Postęp i komunikaty mają wynikać z rzeczywistych operacji. Cel reakcji zwykłych interakcji poniżej 50 ms nie jest deklaracją zmierzonej wydajności.
- Używaj wspólnego logowania i globalnych toastów. Nie twórz osobnych systemów powiadomień, logów lub modalnych komunikatów dla rutynowych operacji.
- AppBar, tray, DPI, wiele monitorów i DWM należą do warstwy powłoki. Nowy panel nie może sam zmieniać trybu okna, systemowej tapety ani konfiguracji pulpitu.
- Nie deklaruj wtyczek jako izolowanych lub bezpiecznych dla niezaufanego kodu: działają w procesie hosta. Szczegóły: [Pluginy](docs/PLUGINY.md).

## Wygląd i języki

- Obowiązuje miękki, kompaktowy wygląd z paska: stonowane powierzchnie, oszczędne dekoracje, czytelna typografia, wspólne tokeny, cienkie dzielniki i sensowne wykorzystanie małej wysokości.
- Zachowuj motywy jasny, ciemny i systemowy oraz ustawienia globalne i nadpisania panelu. Nowa funkcja nie może resetować zapisanej personalizacji.
- Nowe teksty interfejsu wymagają polskiej i angielskiej wersji we wspólnym mechanizmie lokalizacji. Polski jest domyślny; obecny wybór języka działa po ponownym uruchomieniu. Nie wprowadzaj osobnego przełącznika języka panelu.
- Dostępność, obsługa klawiatury, widoczny fokus, etykiety UI Automation i kontrast są częścią funkcji, nie opcjonalnym dodatkiem.

## Weryfikacja, wersje i publikacja

- Uruchamiaj kontrole właściwe dla zmiany. Dla kodu panelu sprawdź build Release, odpowiednie testy i renderowanie WPF; dla wtyczek także integrację hosta. Szczegółowa lista jest w wytycznych paneli.
- Zapisuj rzeczywisty wynik i kod wyjścia. Nie przedstawiaj renderu jako testu prawdziwego logowania, DWM, tray, DPI lub interakcji użytkownika.
- Źródłem wersji jest `Directory.Build.props`. Nie podnoś wersji aplikacji za samą dokumentację ani rozpoczęcie kompilacji. Wydawana poprawka zwykle zmienia patch, kompatybilna funkcja minor, zmiana niezgodna major.
- Po zmianach zachowania aktualizuj `listaZmian.md`, `wersje.md` i `changelog.txt` zgodnie z `AGENTS.md`. Wpisy mają być krótkie, z czasem i rzeczywistym zakresem weryfikacji.
- Poznaj `scripts/BuildTools.ps1` i skrypty `buduj*.bat` przed uruchomieniem. Nie pakuj po nieudanym teście lub buildzie. Każdy build ma osobny identyfikator/katalog w istniejącej strukturze `Release/Dev`, `Release/Prod`, `Release/Instal`.
- Nie usuwaj danych użytkownika w ramach sprzątania kompilacji. Operacje destrukcyjne wymagają potwierdzenia zakresu.
- Commituj po przeglądzie różnic. Stosuj konwencję README: `typ: krotki opis po polsku bez ogonkow`. Push wykonuj w zakresie zleconym przez użytkownika; nie nadpisuj zdalnej historii.

## Dokumenty powiązane

- [Wytyczne paneli](docs/WYTYCZNE_PANELI.md) — obowiązkowa specyfikacja wspólnych funkcji, wyglądu i odbioru nowych paneli.
- [README](README.md) — uruchamianie, testy, wersjonowanie, dane i Git.
- [Kierunek UI](docs/UI_KIERUNEK_2026-11-03.md) — uzgodnienia i historia wdrożenia wyglądu; odróżniaj propozycje od wdrożenia.
- [Pluginy](docs/PLUGINY.md) oraz [Instrukcja pluginów](Instrukcja-pluginow.md) — kontrakty, dystrybucja i przykład PhotoFrame.
- [Plan budowy](PLAN_BUDOWY.md) i [audyt założeń](docs/AUDYT_ZALOZEN_2026-11-02.md) — kontekst i ograniczenia; nie są aktualnym raportem testów.

Zmiana dokumentacyjna z 2026-11-05: utworzono zasady projektu i wytyczne paneli oraz obowiązek ich odczytu w `AGENTS.md`. Bez zmiany wersji aplikacji i bez wydania.
