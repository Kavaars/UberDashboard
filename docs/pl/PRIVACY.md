# UberDashboard — polityka prywatności

[English version](../../PRIVACY.md)

Data obowiązywania: **2026-11-06**

Strona projektu: **https://github.com/Kavaars/UberDashboard**

Kontakt: **[Kavaars na GitHubie](https://github.com/Kavaars)**

### 1. Zasada działania

UberDashboard jest aplikacją desktopową dla Windows, rozwijaną przez Kavaars. Dane pulpitów i paneli są przechowywane w profilu użytkownika. Aplikacja nie posiada serwera autora przechowującego te dane i nie wysyła autorowi automatycznych raportów użycia ani raportów awarii. Nie używamy danych użytkowników do reklam, sprzedaży, profilowania marketingowego ani trenowania modeli AI.

**„Lokalny zapis” nie oznacza „braku połączeń z internetem”.** Włączenie pogody, połączenie konta Microsoft/Google lub otwarcie strony powoduje komunikację z odpowiednią usługą. Dostawca otrzymuje dane niezbędne do wykonania danej operacji oraz informacje połączenia, w tym adres IP.

### 2. Dane i cele

- **Pulpity i panele:** notatki, zadania, ustawienia wyglądu, układ, skróty, wybrane lokalizacje, odwołania do plików i inne treści podane przez użytkownika służą do działania wybranych funkcji. Są zapisywane lokalnie, gdy dana funkcja wymaga trwałości. Zdjęcia i dokumenty wskazane z dysku mogą pozostawać w wybranym folderze zamiast być kopiowane do profilu aplikacji.
- **Microsoft To Do:** po połączeniu konta aplikacja korzysta z logowania Microsoft i Microsoft Graph. Przetwarza identyfikatory konta/list, dane wybranych zadań, checklisty, metadane powiązań z innymi aplikacjami oraz poświadczenia sesji. Dane obejmują nazwy, stany, daty i podane adresy powiązań. Lokalne kopie mogą pozostać po odłączeniu konta. Po świadomym włączeniu zapisu zmiany użytkownika są przesyłane z powrotem do Microsoft wyłącznie w ramach udzielonych uprawnień i wybranej synchronizacji.
- **Google Calendar:** po połączeniu konta aplikacja komunikuje się z Google OAuth i Calendar API, przetwarzając poświadczenia sesji, identyfikator konta i kalendarza oraz dane wydarzeń. Identyfikator konta służy do bezpiecznego przypisania synchronizacji do właściwej osoby i jest uzyskiwany przez Google OpenID Connect. Widok kalendarza pobiera wydarzenia. W wydaniach z włączonym zapisem zatwierdzone zmiany są przesyłane do Google. Operacje dotyczące uczestników mogą powodować wysłanie zaproszeń lub odwołań przez Google, zgodnie z zatwierdzoną akcją.
- **Zarządzanie zasobami:** po jego wybraniu aplikacja odczytuje katalog list Microsoft lub kalendarzy Google, ich nazwy, właściwości i ograniczenia dostępu. Google udostępnia także identyfikator kalendarza głównego oraz adres właściciela danych; służą do sprawdzenia prawa do usunięcia kalendarza. Zarządzanie kalendarzami wymaga osobnej zgody. Wprowadzone nazwy, opisy, miejsca i strefy kalendarzy są synchronizowane z dostawcą. Właściwości i zawartość mogą być widoczne dla innych osób zgodnie z udostępnieniem w danej usłudze. Zatwierdzone usunięcie całego zasobu może dotyczyć również danych innych osób. Metadane usuniętych identyfikatorów pozostają w lokalnym profilu, aby zabezpieczyć niewysłane zmiany przed użyciem nieaktualnego powiązania.
- **Subskrypcje i udostępnianie Google:** po osobnej zgodzie aplikacja odczytuje i zapisuje własną listę kalendarzy, osobiste aliasy, kolory i ustawienia widoczności. Zarządzanie udostępnieniami wymaga innej zgody; przetwarzane są reguły dostępu, adresy osób/grup, domeny i poziomy uprawnień. Te dane są przechowywane lokalnie i przekazywane bezpośrednio Google w celu realizacji wybranej operacji. Zatwierdzone udostępnienie może ujawnić kalendarz wskazanym odbiorcom, a dostęp publiczny także osobom niezalogowanym, zgodnie z rolą i politykami Google. Google może wysłać powiadomienia o zmianie dostępu. Usunięcie subskrypcji usuwa jedynie wpis z własnej listy; nie usuwa kalendarza, nie odwołuje uprawnień innych osób i nie usuwa automatycznie lokalnych kopii. Zmiany udostępniania i usunięcie subskrypcji wymagają osobnego zatwierdzenia skutków w kolejce.
- **Pogoda:** wyszukiwana nazwa miejsca jest przesyłana do `geocoding-api.open-meteo.com`, a współrzędne wybranej lokalizacji do `api.open-meteo.com`. Wyniki i konfiguracja mogą być zapisane lokalnie. Aplikacja nie potrzebuje do tego treści notatek ani zadań i nie wysyła ich do Open-Meteo.
- **Monitor zasobów:** odczyty sprzętu służą do wyświetlania lokalnych pomiarów CPU, GPU, pamięci i dostępnych czujników. Wbudowany monitor nie wysyła ich do autora.
- **Diagnostyka:** lokalne logi zawierają czas, moduł, identyfikatory operacji i informacje o błędach. Szczegóły błędów mogą zawierać lokalne ścieżki, identyfikatory zasobów i inne informacje techniczne. W kodzie rozwojowym dodatkowo ograniczono informacyjne logowanie HTTP. Logi nie są automatycznie przesyłane autorowi. Przed samodzielnym udostępnieniem logów należy sprawdzić ich treść.

Nowa integracja lub dostęp do dodatkowych danych wymaga odpowiedniego opisu i zgody, zanim funkcja zacznie korzystać z takiego dostępu. Sama obecność kodu funkcji w repozytorium nie oznacza jej aktywacji w zainstalowanym wydaniu.

### 3. Odbiorcy i zewnętrzne usługi

Załączniki wybrane do wysłania są kopiowane do lokalnego profilu i przesyłane bezpośrednio do Microsoft albo Google Drive. Google wymaga kontekstowej zgody `drive.file`, dotyczącej plików utworzonych lub udostępnionych aplikacji. Przypięcie pliku do wydarzenia nie nadaje uczestnikom dostępu do pliku; odpięcie nie usuwa go z Drive. Lokalne migawki plików można usuwać osobno w oknie załączników. Adresy sesji transferów są chronione DPAPI dla użytkownika Windows i nie są zapisywane w logach HTTP. Plany podziału serii i kopie przyszłych wyjątków pozostają lokalnie; zakończone plany są usuwane po 30 dniach podczas porządkowania historii albo wcześniej na żądanie. Nierozstrzygnięte plany i niewysłane transfery nie wygasają automatycznie. Google może wysłać uczestnikom aktualizacje, odwołania i nowe zaproszenia po zatwierdzonym podziale.

Autor nie sprzedaje danych użytkownika ani nie przekazuje ich reklamodawcom. Połączenia z Microsoft, Google i Open-Meteo są potrzebne do funkcji wybranych przez użytkownika; usługi przetwarzają otrzymane dane zgodnie z własnymi zasadami:

- [Microsoft Privacy Statement](https://privacy.microsoft.com/privacystatement)
- [Google Privacy Policy](https://policies.google.com/privacy)
- [Open-Meteo — terms and privacy](https://open-meteo.com/en/terms)

Korzystanie z repozytorium, pobieranie aplikacji i kontakt przez GitHub podlega także [polityce prywatności GitHub](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement). Treści opublikowane w publicznym zgłoszeniu są publiczne — nie należy tam zamieszczać tokenów, haseł, prywatnych wydarzeń, zadań, logów bez sprawdzenia ani plików konta OAuth.

Otwieranie skrótów WWW lub plików z folderów sieciowych korzysta z odpowiedniej przeglądarki, programu lub zasobu wybranego przez użytkownika. Zainstalowane pluginy są kodem wykonywanym w procesie aplikacji i nie mają izolacji systemowej. Pluginy osób trzecich mogą mieć własne połączenia i zasady prywatności; niniejszy opis nie gwarantuje ich zachowania.

Adresy powiązań zadań są przechowywane jako dane i synchronizowane z Microsoft. Nie są otwierane automatycznie. Wybranie **Otwórz w przeglądarce** dla adresu HTTP/HTTPS powoduje kontakt przeglądarki z docelową stroną na zasadach tej strony. Odnośniki aplikacji natywnych mogą być zachowane, ale ten przycisk ich nie uruchamia.

### 4. Przechowywanie i zabezpieczenia

Domyślny profil to `%LOCALAPPDATA%\UberDashboard`: baza `Data\dashboard.db`, pliki paneli i zasoby, `Credentials`, `LOGS` oraz `Backups`. Niestandardowy profil może znajdować się w innym wskazanym miejscu. Foldery synchronizowane lub sieciowe oraz kopie systemowe podlegają konfiguracji użytkownika i systemu.

Sesja Microsoft jest przechowywana przez chroniony cache MSAL dla Windows. Plik sesji Google jest chroniony DPAPI dla bieżącego użytkownika Windows. **Baza danych, zwykłe zasoby, logi i kopie bazy nie są w całości szyfrowane przez UberDashboard.** Ich bezpieczeństwo zależy również od ochrony konta Windows, uprawnień plików i zabezpieczeń dysku. Nie należy publikować profilu ani jego kopii.

Dane treści i konfiguracja pozostają do zmiany/usunięcia przez użytkownika. Logi rotują dziennie, z limitem 14 plików. Usługa zwykłych kopii bazy domyślnie zachowuje 10 kopii; kopie przed migracją i inne samodzielnie zapisane kopie mogą pozostać dłużej. Historia odzyskiwania nowego mechanizmu synchronizacji ma domyślnie 30 dni; nierozstrzygnięte i niewysłane zmiany są zachowywane dłużej. Usunięcie elementu z aktywnego widoku nie usuwa automatycznie wszystkich jego starszych kopii.

Pobrane zakresy kalendarza mogą być przechowywane w lokalnej bazie do przeglądania offline, wraz z tytułami, terminami i identyfikatorami wydarzeń/serii. Kopie są rozdzielone według konta i kalendarza; odłączenie konta nie usuwa ich automatycznie. Usunięcie całego profilu usuwa także tę lokalną pamięć podręczną, z zastrzeżeniem osobno zachowanych kopii zapasowych.

### 5. Kontrola i usuwanie danych

Sprawdzanie niepewnej edycji lub usunięcia wykonuje bezpośredni odczyt Microsoft/Google. Po świadomym przejściu do decyzji wysłany szkic, aktualna treść lokalna i zaobserwowana wersja usługi są zachowywane w lokalnej historii. Sam podgląd i przygotowanie konfliktu nie ponawiają zapisu do dostawcy; późniejszy wybór wersji może przygotować zwykłą zmianę do synchronizacji.

**Historia odzyskiwania** w oknie synchronizacji udostępnia wcześniejsze lokalne kopie oraz wersje bazową, lokalną i zdalną zachowane przy rozstrzygnięciu konfliktu. Przywrócenie otwiera edytor i po zapisie tworzy zwykłą zmianę do synchronizacji z dostawcą. Można wcześniej usunąć wybraną lokalną kopię po osobnym potwierdzeniu; ta operacja nie usuwa bieżącej treści, niewysłanych operacji ani osobnych backupów. Retencja 30 dni jest sprawdzana podczas edycji, scalania i odczytu historii; nierozstrzygnięte konflikty i oczekujące zmiany chronią związane z nimi kopie przed automatycznym wygasaniem.

Można nie włączać integracji sieciowych, odłączyć konto w aplikacji oraz odwołać dostęp w ustawieniach konta [Google](https://myaccount.google.com/connections) lub [Microsoft](https://account.live.com/consent/Manage). Dla kont organizacyjnych dostęp może podlegać administratorowi organizacji. Odłączenie nie oznacza usunięcia zadań/wydarzeń z usługi i nie gwarantuje usunięcia lokalnych kopii, logów lub backupów.

Aby usunąć wszystkie lokalne dane UberDashboard, należy zamknąć aplikację i usunąć własny profil aplikacji oraz ewentualne kopie/eksporty zapisane w innych miejscach. Deinstalacja domyślnie zachowuje dane profilu. Dane w Microsoft/Google usuwa się oddzielnie w danej usłudze lub odpowiednią, świadomie wybraną operacją synchronizacji.

### 6. Google API Limited Use

Wykorzystywanie informacji otrzymanych z Google APIs i ich przekazywanie do innych aplikacji odbywa się zgodnie z [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), w tym wymaganiami Limited Use. Dane służą wyłącznie funkcjom widocznym dla użytkownika, do których udzielił dostępu. Nie są wykorzystywane do reklam ani trenowania ogólnych modeli AI. Autor nie ma automatycznego dostępu do lokalnej treści kont użytkowników.

### 7. Kontakt i zmiany

Opiekunem projektu jest [Kavaars](https://github.com/Kavaars). W sprawach prywatności skorzystaj z danych kontaktowych udostępnionych na tym profilu; publiczne zgłoszenia ogranicz do opisu problemu bez danych prywatnych. Aktualna polityka jest publikowana w repozytorium projektu, z datą zmiany. Zmiana wymagająca nowych uprawnień nie zastępuje zgody użytkownika.

