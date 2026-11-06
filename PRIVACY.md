# UberDashboard — polityka prywatności / Privacy Policy

Data obowiązywania / Effective date: **2026-11-06**

Strona projektu / Project website: **https://github.com/Kavaars/UberDashboard**

Kontakt / Contact: **[Kavaars on GitHub](https://github.com/Kavaars)**

## Polski

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

---

## English

### 1. How the app works

UberDashboard is a Windows desktop application maintained by Kavaars. Dashboard and panel data is stored in the user's profile. There is no developer-operated server storing that content, and the app does not automatically send usage analytics or crash reports to the developer. User data is not used for advertising, sale, marketing profiling, or AI model training.

**Local storage does not mean that the app never communicates over the internet.** Enabling weather, connecting Microsoft/Google, or opening a website communicates with that service. The provider receives the data needed for the operation and connection information, including the IP address.

### 2. Data and purposes

- **Dashboards and panels:** notes, tasks, appearance settings, layouts, shortcuts, selected locations, file references, and user-entered content support the features you use. They are stored locally where persistence is required. Referenced photos/documents may remain in their selected folders.
- **Microsoft To Do:** account connection uses Microsoft sign-in and Microsoft Graph. The app processes account/list identifiers, selected task data, checklists, linked-resource metadata, and session credentials. Data includes names, states, dates, and provided resource URLs. Local copies may remain after disconnecting. After explicitly enabling write access, user changes are sent back to Microsoft only within the granted permissions and selected synchronization.
- **Google Calendar:** connection uses Google OAuth and Calendar API, processing session credentials, account/calendar identifiers, and event data. The account identifier is obtained through Google OpenID Connect and prevents synchronization from being assigned to the wrong account. The calendar view reads events. In releases with enabled write functionality, approved changes are sent to Google. Participant-related actions can cause Google to send invitations or cancellations according to the approved action.
- **Resource management:** when selected, the app reads the Microsoft list or Google calendar catalog, names, properties, and access restrictions. Google also supplies the primary calendar identifier and the data owner's email to check calendar deletion rights. Calendar management requires separate consent. Entered names, descriptions, locations, and calendar time zones are synchronized with the provider. Properties and contents can be visible to others according to the service's sharing settings. Approved whole-resource deletion may also affect other people's data. Deleted-identifier metadata remains in the local profile to protect unsent changes from stale bindings.
- **Google subscriptions and sharing:** with separate consent, the app reads and writes the user's calendar list, personal aliases, colors, and visibility settings. Sharing management requires different consent and processes access rules, person/group addresses, domains, and permission levels. These data are stored locally and sent directly to Google for the selected operation. Approved sharing can expose the calendar to the chosen recipients; public access can also expose it to signed-out users, subject to the role and Google's policies. Google may send access-change notifications. Unsubscribing only removes the entry from the user's list; it does not delete the calendar, revoke others' permissions, or automatically erase local copies. Sharing changes and unsubscribing require a separate review and approval in the queue.
- **Weather:** place search text is sent to `geocoding-api.open-meteo.com`; coordinates of the selected location are sent to `api.open-meteo.com`. Results and configuration may be cached locally. Notes and tasks are not required for weather and are not sent to Open-Meteo.
- **Resource monitor:** hardware readings display local CPU, GPU, memory, and available sensor information. The built-in monitor does not send them to the developer.
- **Diagnostics:** local logs record timestamps, modules, operation identifiers, and errors. Error details may include local paths, resource identifiers, and technical information. Development code further limits informational HTTP request logging. Logs are not automatically uploaded; review them before sharing them yourself.

Access to additional data or integrations requires appropriate disclosure and consent before use. Code present in the repository is not necessarily enabled in an installed release.

### 3. Recipients and external services

Attachments selected for upload are copied into the local profile and sent directly to Microsoft or Google Drive. Google requests contextual `drive.file` consent for files created by or shared with the app. Attaching does not grant file access to attendees; detaching does not delete a Drive file. Local upload snapshots can be removed separately. Upload session addresses are protected with Windows user DPAPI and excluded from HTTP logs. Series split plans and exception copies remain local; finished plans are retained for 30 days during history cleanup or removed earlier on request. Unresolved plans and unsent transfers do not expire automatically. Approved splitting can cause Google to send updates, cancellations and new invitations.

The developer does not sell user data or supply it to advertisers. Microsoft, Google, and Open-Meteo connections support user-selected features and are subject to the providers' own policies: [Microsoft](https://privacy.microsoft.com/privacystatement), [Google](https://policies.google.com/privacy), and [Open-Meteo](https://open-meteo.com/en/terms).

Visiting the repository, downloading releases, and contacting the project through GitHub is also subject to [GitHub's Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement). Public issue contents are public. Do not post credentials, OAuth account files, private tasks/events, or unreviewed logs.

Opening web shortcuts or network files uses the browser, application, or network resource you selected. Installed plugins execute in the host process without system-level isolation. Third-party plugins may have their own connections and privacy practices; this policy does not guarantee their behavior.

Task resource URLs are stored as data and synchronized with Microsoft; they are not opened automatically. Selecting **Open in browser** for an HTTP/HTTPS address contacts the destination website through your browser under that site's policies. Native-app deep links can be retained, but that button does not launch them.

### 4. Storage and protection

The default profile is `%LOCALAPPDATA%\UberDashboard`, including `Data\dashboard.db`, panel files/assets, `Credentials`, `LOGS`, and `Backups`. Custom profiles may use another location. Network folders, cloud-synchronized folders, and system backups follow your own configuration.

Microsoft sessions use the Windows-protected MSAL cache. Google session files use Windows DPAPI for the current user. **The database, ordinary assets, logs, and database backups are not fully encrypted by UberDashboard.** Windows account security, file permissions, and disk protection also matter. Do not publish your profile or backups.

Content/settings remain until changed or removed. Daily log rotation retains up to 14 files. Ordinary database backups default to 10 retained copies; pre-migration and independently saved copies may remain longer. The new synchronization engine's recovery history defaults to 30 days, retaining unresolved/unsent changes longer. Removing an item from a view does not automatically erase every older copy.

Fetched calendar ranges may be stored in the local database for offline viewing, including titles, times, and event/series identifiers. Copies are separated by account and calendar; disconnecting does not automatically erase them. Removing the full app profile also removes this local cache, subject to separately retained backups.

### 5. Control and deletion

Inspecting an uncertain update or deletion reads directly from Microsoft/Google. After explicitly proceeding to a decision, the sent draft, current local content and observed service version are retained in local history. The inspection and conflict preparation do not repeat the provider write; a later version choice can prepare an ordinary synchronization change.

**Recovery history** in the synchronization window exposes older local copies and the base, local and service versions retained when resolving a conflict. Restoring opens the editor; saving prepares an ordinary change for synchronization with the provider. A selected local copy can be removed early after separate confirmation; this does not remove current content, unsent operations or separate backups. The 30-day retention rule is checked during editing, merging and history reads. Unresolved conflicts and pending changes protect related copies from automatic expiry.

You can leave network integrations disabled, disconnect accounts in the app, and revoke access in your [Google](https://myaccount.google.com/connections) or [Microsoft](https://account.live.com/consent/Manage) account. Organizational accounts may be controlled by an administrator. Disconnecting does not delete cloud tasks/events or necessarily erase local snapshots, logs, or backups.

To erase all local UberDashboard data, close the app and remove your app profile and any backups/exports stored elsewhere. Uninstallation preserves profile data by default. Delete cloud data separately in the provider's service or through an explicitly selected synchronization operation.

### 6. Google API Limited Use

UberDashboard's use and transfer to any other app of information received from Google APIs adheres to the [Google API Services User Data Policy](https://developers.google.com/terms/api-services-user-data-policy), including the Limited Use requirements. Data supports only user-facing features authorized by the user and is not used for advertising or general-purpose AI model training. The developer has no automatic access to users' locally stored account content.

### 7. Contact and updates

The project is maintained by [Kavaars](https://github.com/Kavaars). For privacy matters, use the contact information made available on that profile. Keep public reports free of private data. The current dated policy is published in the project repository. Policy changes do not replace consent for additional permissions.
