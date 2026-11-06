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

Po niepewnej edycji lub usunięciu przycisk **Sprawdź stan usługi** w kolejce pokazuje wysłany szkic, bieżącą wersję lokalną i aktualny odczyt z Microsoft lub Google. **Przejdź do decyzji** ponownie sprawdza te dane i przygotowuje lokalny konflikt. Następnie można wybrać wersję lub ręcznie poprawić szkic. Nowsze edycje pozostają zachowane, a zmiana podglądu wymaga ponownego porównania. Operacje wpływające na inne osoby nadal wymagają zatwierdzenia skutków. Brak elementu w odpowiedzi usługi nie jest dowodem wykonania wcześniejszego zapisu.

W edytorze Google Calendar można zmieniać listę adresów uczestników na kopii organizatora, własną odpowiedź na zaproszenie, przypomnienia, widoczność oraz oznaczenie „wolny/zajęty”. Istniejące odpowiedzi i dodatkowe dane uczestników pozostają zachowane. Podgląd przed wysłaniem pokazuje dodawane i usuwane osoby oraz dane przed i po zmianie. Niepełnej listy uczestników nie można zastąpić; specjalne typy wydarzeń i pozostałe niedostępne operacje można otworzyć w Google Calendar.

Przycisk **Listy i kalendarze** otwiera katalog konta. Można tworzyć własne listy Microsoft i kalendarze dodatkowe Google, zmieniać ich właściwości oraz przygotować usunięcie całego zasobu. List systemowych i kalendarza głównego nie można usunąć. Google wymaga osobnej zgody na zarządzanie kalendarzami; usuwanie wymaga potwierdzonego właściciela danych. Niewysłane zmiany zawartości blokują usunięcie zasobu, a kolejka wymaga osobnego zatwierdzenia skutków. **Otwórz zawartość** nie zmienia istniejących paneli; listę Microsoft trzeba wcześniej wybrać w ustawieniach integracji. Niepewne utworzenie listy lub kalendarza można uzgodnić przez wskazanie konkretnego elementu usługi, bez ponawiania tworzenia.

**Subskrypcje Google** pozwalają dodać istniejący kalendarz do własnej listy, nadać mu osobisty alias i kolory oraz zmienić widoczność. **Usuń z mojej listy** pozostawia dane kalendarza w usłudze. Kalendarz główny i własny kalendarz danych są chronione przed tą operacją. **Udostępnianie Google** pozwala zarządcy przygotować dostęp dla osoby, grupy, domeny lub publiczny. Podgląd pokazuje odbiorcę i zmianę poziomu dostępu; dopiero osobne zatwierdzenie w kolejce pozwala wysłać zmianę. Własną regułę dostępu i regułę właściciela danych zmienia się w Google Calendar. Subskrypcje oraz udostępnianie wymagają odrębnych zgód Google. Odmowa dodatkowej zgody zachowuje wcześniejsze funkcje.

**Załączniki** pozwalają wysyłać pliki do zadań Microsoft (do 25 MB), pobierać je i usuwać. Google korzysta z osobnej zgody na pliki Drive: można wysłać plik, wybrać plik dostępny aplikacji, pobrać go lub odpiąć od wydarzenia. Odpięcie nie usuwa pliku z Drive ani nie zmienia jego udostępnienia. Transfery i ich postęp są zachowane po restarcie; nieznany wynik Microsoft można uzgodnić po ID i porównaniu zawartości. Kopie plików pozostają lokalnie do osobnego usunięcia.

**Seria i wystąpienia** otwiera pobrany zakres terminów, także offline. Edytor wydarzenia udostępnia wzory i zaawansowane reguły iCalendar oraz strefy IANA. Można edytować pojedynczy termin, całą serię, odwołać/przywrócić wystąpienie lub przygotować **To i następne**. Podział wymaga przeglądu skutków i praw organizatora z pełną listą uczestników. Google resetuje przyszłe wyjątki; ich kopie są zachowane w oknie **Podziały i kopie wyjątków** i mogą zostać odtworzone jako osobne wydarzenia. Rozpoczęty podział można wznowić po restarcie. Zakończone plany mają retencję 30 dni i osobne usuwanie kopii.

**Cała integracja pozostaje w rozwoju.** Szczegółowe opcje uczestników, specjalne typy wydarzeń i osobiste ustawienia powiadomień wymagają dalszej pracy. Nie zakończono prób na rzeczywistych kontach ani weryfikacji publicznego OAuth. [Aktualny zakres i ograniczenia](docs/SYNCHRONIZACJA_DWUKIERUNKOWA.md).

**Ustawienia → Integracje → Centrum synchronizacji** pokazuje powiązania, kolejkę i konflikty. Edytor umożliwia ponowienie, wstrzymanie synchronizacji i ponowne połączenie konta. Usuwanie wydarzenia lub zadania w edytorze oznacza zmianę w usłudze; usunięcie samego panelu lub zakładki nie usuwa danych w Microsoft ani Google. Dotychczasowe lokalne importy nie są automatycznie wysyłane do usług.

## Dane i prywatność

W oknie synchronizacji **Historia odzyskiwania** pozwala porównać wcześniejsze kopie z bieżącą treścią. **Przywróć i edytuj** otwiera zwykły formularz; zapis tworzy zmianę w lokalnej kolejce i może później zmienić dane w usłudze. Można też usunąć wybraną kopię po osobnym potwierdzeniu. Historia ma domyślnie 30 dni, a kopie związane z niewysłanymi zmianami i konfliktami są chronione przed automatycznym wygasaniem. Osobne backupy mogą nadal zawierać usuniętą kopię.

Okno konfliktu pokazuje wspólną wersję bazową, wersję lokalną i wersję usługi. Można zachować wybraną wersję albo użyć **Edytuj połączony szkic** i poprawić obsługiwane pola. Przed przyjęciem decyzji aplikacja ponownie sprawdza dane w usłudze; zmiana podglądu wymaga ponownego porównania. Po rozstrzygnięciu wszystkie trzy wersje pozostają w historii zgodnie z jej zasadami.

Notatki, zadania lokalne, ustawienia i kolejka synchronizacji znajdują się na komputerze, domyślnie w `%LOCALAPPDATA%\UberDashboard`. Kopie zapasowe są w podfolderze `Backups`, a logi w `LOGS`. Odinstalowanie programu pozostawia profil użytkownika.

Integracje łączą się bezpośrednio z Microsoft, Google lub Open-Meteo i przekazują dane potrzebne do wybranej funkcji. Nie znaleziono automatycznej analityki autora ani wysyłania raportów awarii. Tokeny są chronione mechanizmami Windows; baza danych i zwykłe kopie zapasowe nie są w całości szyfrowane. Wtyczki działają z uprawnieniami aplikacji — instaluj je z zaufanego źródła. Szczegóły opisuje [polityka prywatności](PRIVACY.md).

## Ostatnie zmiany

| Wersja / etap | Zmiany dla użytkownika |
| --- | --- |
| Niepewne zapisy, 6 listopada 2026 | Sprawdzenie niepewnych edycji i usunięć w obu usługach, ponowne porównanie przed decyzją i przejście do konfliktu z ochroną nowszych szkiców. |
| Odzyskiwanie zmian, 6 listopada 2026 | Porównanie trzech wersji konfliktu, ręczna edycja połączonego szkicu i historia kopii. Przywracanie przez lokalną kolejkę, osobne potwierdzenie usunięcia kopii i ochrona nowszych zmian. |
| Dostęp do kalendarzy, 6 listopada 2026 | Subskrypcje, osobiste aliasy i kolory oraz udostępnianie z podglądem skutków i osobnymi zgodami. Usunięcie subskrypcji zachowuje dane kalendarza i lokalne niewysłane zmiany. |
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
