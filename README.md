# UberDashboard

Twój własny pulpit z notatkami, zadaniami, kalendarzem, pogodą i skrótami — w jednym miejscu na Windows 11. UberDashboard może działać jako zwykłe okno albo pasek przy krawędzi ekranu. Układ, zawartość i wygląd zapisują się lokalnie.

[Strona projektu](https://github.com/Kavaars/UberDashboard) · [Historia zmian](changelog.txt) · [Polityka prywatności / Privacy Policy](PRIVACY.md) · [Zgłoś problem](https://github.com/Kavaars/UberDashboard/issues)

## Co potrafi aplikacja

- **Wiele pulpitów i zakładek** — osobne zestawy do pracy, spraw prywatnych i innych zajęć. Jeden panel może mieć kilka zakładek z niezależną treścią.
- **Własny układ** — dodawanie paneli obok siebie i pod sobą, zmiana proporcji, przenoszenie, ukrywanie i przywracanie.
- **Notatnik** — notatki i import plików TXT oraz Markdown.
- **ToDo** — lokalne listy zadań, import TXT, Markdown i CSV oraz połączenie z Microsoft To Do.
- **Kalendarz Google** — widoki miesiąca, tygodnia, dwóch tygodni, dnia, własnego zakresu i listy wydarzeń.
- **Pogoda** — prognoza dla wybranej miejscowości, pobierana z Open-Meteo po włączeniu połączeń.
- **Skróty** — uruchamianie aplikacji, otwieranie folderów, dokumentów i stron; widoki ikon, listy, przycisków oraz kafelków.
- **Monitor zasobów** — wykorzystanie CPU, GPU, RAM i VRAM; temperatury i wentylatory zależą od dostępnych czujników.
- **Afirmacje** — własne teksty do ćwiczenia i śledzenie postępów.
- **Wtyczki** — dodatkowe panele w paczkach `.udplugin`, między innymi ramka na zdjęcia PhotoFrame.
- **Personalizacja** — motyw jasny, ciemny lub systemowy, kolory, fonty, tapety i wygląd poszczególnych paneli. Interfejs jest dostępny po polsku i angielsku.

## Pierwsze kroki

Projekt jest w rozwoju. Bieżąca wersja źródeł to **1.9.0**; gotowy instalator nie został jeszcze opublikowany w [Wydaniach](https://github.com/Kavaars/UberDashboard/releases). Lista zmian poniżej opisuje również funkcje rozwijane w bieżącym kodzie.

1. Po uruchomieniu utwórz lub wybierz pulpit i dodaj potrzebne panele.
2. Włącz **Edytuj układ**, aby zmienić ich położenie i rozmiary. Menu **Układ** pozwala zrobić to również bez przeciągania.
3. Użyj **+** w nagłówku panelu, aby dodać zakładkę. Nazwę zmienisz dwuklikiem lub klawiszem **F2**, a kolejność także skrótem **Ctrl+Shift+strzałka**.
4. Opcje panelu i sekcja **Wygląd** pozwalają dopasować jego działanie i prezentację. W **Ustawieniach** wybierzesz między innymi tryb okna, motyw, język i integracje. Zmiana języka działa po ponownym uruchomieniu.

Zmiany zapisują się automatycznie. Jeśli zapis się nie powiedzie, aplikacja pokazuje błąd i umożliwia ponowienie. Własna tapeta wskazuje plik na dysku — po jego przeniesieniu trzeba wybrać go ponownie.

## Microsoft To Do i Google Calendar

Microsoft To Do skonfigurujesz w **Ustawienia → Integracje**. Listę można zaimportować jako niezależną lokalną kopię albo powiązać z usługą. Google połączysz w opcjach panelu kalendarza; obecna konfiguracja wymaga lokalnego pliku JSON klienta OAuth typu Desktop.

W bieżącym kodzie przycisk **Synchronizacja dwukierunkowa** otwiera edytor powiązanej listy lub kalendarza. Pierwsze włączenie wymaga osobnej zgody Microsoft lub Google na zapis. Edytor pozwala tworzyć, zmieniać i usuwać podstawowe zadania oraz wydarzenia. Zmiany trafiają najpierw do lokalnej kolejki, która zachowuje je także po restarcie. Synchronizacja działa podczas uruchomienia aplikacji, również w zasobniku.

W zadaniach Microsoft można także zmienić ważność, przypisane kategorie, daty, przypomnienie i sposób powtarzania. Widok Google Calendar zachowuje pobrane zakresy do przeglądania offline i pokazuje datę lokalnej kopii. Zakres, którego wcześniej nie pobrano, pozostaje niedostępny bez sieci. Niewysłane zmiany są dostępne w edytorze; siatka kalendarza pokazuje ostatni pobrany stan usługi.

Po wybraniu zadania przyciski **Checklista zadania** i **Powiązane zasoby** otwierają jego kroki oraz odnośniki do innych aplikacji i dokumentów. Można je dodawać, edytować, usuwać i oznaczać kroki jako wykonane. Gdy wynik tworzenia w Microsoft jest nieznany, w kolejce dostępny jest wybór **Wskaż istniejący element** — po porównaniu danych można powiązać lokalny szkic z konkretnym elementem usługi. Aplikacja nie ponawia tego tworzenia automatycznie.

W edytorze Google Calendar można zmieniać listę adresów uczestników na kopii organizatora, własną odpowiedź na zaproszenie, przypomnienia, widoczność oraz oznaczenie „wolny/zajęty”. Istniejące odpowiedzi i dodatkowe dane uczestników pozostają zachowane. Podgląd przed wysłaniem pokazuje dodawane i usuwane osoby oraz dane przed i po zmianie. Niepełnej listy uczestników nie można zastąpić; specjalne typy wydarzeń i pozostałe niedostępne operacje można otworzyć w Google Calendar.

Przycisk **Listy i kalendarze** otwiera katalog konta. Można tworzyć własne listy Microsoft i kalendarze dodatkowe Google, zmieniać ich właściwości oraz przygotować usunięcie całego zasobu. List systemowych i kalendarza głównego nie można usunąć. Google wymaga osobnej zgody na zarządzanie kalendarzami; usuwanie wymaga potwierdzonego właściciela danych. Niewysłane zmiany zawartości blokują usunięcie zasobu, a kolejka wymaga osobnego zatwierdzenia skutków. **Otwórz zawartość** nie zmienia istniejących paneli; listę Microsoft trzeba wcześniej wybrać w ustawieniach integracji. Niepewne utworzenie listy lub kalendarza można uzgodnić przez wskazanie konkretnego elementu usługi, bez ponawiania tworzenia.

**Ta funkcja pozostaje w rozwoju.** Pełna obsługa cykliczności, załączników, szczegółowych opcji uczestników, subskrypcji i udostępniania kalendarzy nie jest jeszcze ukończona. Nie zakończono prób na rzeczywistych kontach ani weryfikacji publicznego OAuth. [Aktualny zakres i ograniczenia](docs/SYNCHRONIZACJA_DWUKIERUNKOWA.md).

**Ustawienia → Integracje → Centrum synchronizacji** pokazuje powiązania, kolejkę i konflikty. Edytor umożliwia ponowienie, wstrzymanie synchronizacji i ponowne połączenie konta. Usuwanie wydarzenia lub zadania w edytorze oznacza zmianę w usłudze; usunięcie samego panelu lub zakładki nie usuwa danych w Microsoft ani Google. Dotychczasowe lokalne importy nie są automatycznie wysyłane do usług.

## Dane i prywatność

Notatki, zadania lokalne, ustawienia i kolejka synchronizacji znajdują się na komputerze, domyślnie w `%LOCALAPPDATA%\UberDashboard`. Kopie zapasowe są w podfolderze `Backups`, a logi w `LOGS`. Odinstalowanie programu pozostawia profil użytkownika.

Integracje łączą się bezpośrednio z Microsoft, Google lub Open-Meteo i przekazują dane potrzebne do wybranej funkcji. Nie znaleziono automatycznej analityki autora ani wysyłania raportów awarii. Tokeny są chronione mechanizmami Windows; baza danych i zwykłe kopie zapasowe nie są w całości szyfrowane. Wtyczki działają z uprawnieniami aplikacji — instaluj je z zaufanego źródła. Szczegóły opisuje [polityka prywatności](PRIVACY.md).

## Ostatnie zmiany

| Wersja / etap | Zmiany dla użytkownika |
| --- | --- |
| Zarządzanie zasobami, 6 listopada 2026 | Własne listy i kalendarze, osobna zgoda Google, ochrona list systemowych i kalendarza głównego. Usuwanie chroni niewysłane zmiany, wymaga zatwierdzenia i nie usuwa samych paneli. Poprawiony kontrast list integracji. |
| Prace nad synchronizacją, 6 listopada 2026 | Google Calendar: uczestnicy i własna odpowiedź, przypomnienia, widoczność i dostępność. Ochrona niepełnej listy i ograniczeń kopii wydarzenia. Podgląd dodawanych i usuwanych uczestników przed wysłaniem. |
| Prace nad synchronizacją, 5 listopada 2026 | Edytory Microsoft To Do i Google Calendar, trwała kolejka, konflikty i centrum synchronizacji. Zadania: ważność, kategorie, daty, przypomnienia, powtarzanie, checklisty i powiązane zasoby. Wybór istniejącego elementu po niepewnym tworzeniu. Pobrane zakresy kalendarza dostępne offline. Publiczna polityka prywatności. Pełny zakres integracji nadal w przygotowaniu. |
| 1.9.0 | Panel Skróty: aplikacje, foldery, dokumenty i strony, kilka widoków oraz własne ikony i kolory. |
| 1.8.1 | Zapamiętywanie trybu, pozycji i rozmiaru okna oraz bezpieczne odtwarzanie po odłączeniu monitora. |
| Rozwój 1.8.0 | Kalendarz Google, Monitor zasobów, Microsoft To Do, import plików, wspólne zakładki i język angielski. Zadokowany pasek korzysta z zasobnika Windows. |
| 1.7.0 | Odświeżone motywy, bardziej zwarty interfejs i ustawienia wyglądu. |

Pełna historia: [changelog.txt](changelog.txt).

## Pomoc i kontakt

Problemy oraz propozycje zgłaszaj w [Issues projektu](https://github.com/Kavaars/UberDashboard/issues). W sprawach prywatności kontaktuj się przez [profil Kavaars](https://github.com/Kavaars). Nie zamieszczaj w publicznych zgłoszeniach tokenów, haseł, plików poświadczeń ani prywatnej treści notatek i kalendarzy.
