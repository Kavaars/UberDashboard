# Pluginy paneli UberDashboard

Dotyczy aplikacji **1.2.0**, kontraktu paneli **1.1.0** i pierwszego pluginu **Ramka na zdjęcia 1.0.0**.

## Instalowanie i usuwanie

1. Uruchom UberDashboard 1.2.0 lub nowszy.
2. Przeciągnij **jeden plik `.udplugin`** z Eksploratora na główne okno albo okno ustawień. Możesz też wejść w **Opcje i Ustawienia → Pluginy → Zainstaluj z pliku…**.
3. Poczekaj na potwierdzenie. Podczas pracy pojawia się rzeczywisty stan operacji; **Anuluj operację** przerywa ją przed zatwierdzeniem. Nie zamykaj programu w trakcie instalowania.
4. W menu **Dodaj panel** wybierz nowy typ panelu. Instalacja nie dodaje automatycznie instancji na każdym pulpicie.
5. W zakładce **Pluginy** widoczne są nazwy, wersje, identyfikatory i stan instalacji. **Usuń** wyłącza kod i usuwa jego paczkę, bez kasowania danych paneli.

Nie trzeba restartować aplikacji. Panele pluginów korzystają z tego samego układu, dzielników, przenoszenia, ukrywania, wyglądu per panel i autozapisu co panele wbudowane. Można utworzyć wiele niezależnie skonfigurowanych instancji.

Po usunięciu pluginu jego aktywne panele pokazują informację o niedostępności. Układ, identyfikatory i stan w SQLite oraz pliki w PluginData pozostają zachowane. Ponowne zainstalowanie zgodnego pluginu odtwarza panele. Zainstalowanie drugiej wersji tego samego pluginu jest blokowane: najpierw usuń poprzednią. Nowa wersja musi potrafić odczytać poprzedni stan. Usuwanie pojedynczego panelu jest osobną operacją aplikacji.

Błędne instalacje po restarcie są widoczne na liście i można je usunąć. Gdy operacja nie powiedzie się, komunikat opisuje walidację albo odsyła do logów w `%LOCALAPPDATA%\UberDashboard\LOGS`. Ponów wybór pliku po usunięciu przyczyny. Zwykłe upuszczanie obrazów i przeciąganie paneli nie jest przechwytywane przez instalator pluginów. Upuszczenie wielu plików naraz nie instaluje ich.

## Ramka na zdjęcia

Gotowa paczka jest generowana w `Release/Plugins/PhotoFrame-1.0.0-<czas>/PhotoFrame-1.0.0.udplugin`.

Po instalacji dodaj **Ramka na zdjęcia**. W opcjach (koło zębate w nagłówku):

- **Wybierz obraz…** wyświetla jeden lokalny plik.
- **Wybierz folder…** uruchamia pokaz zdjęć z bezpośredniej zawartości folderu, bez podfolderów, w kolejności nazw.
- **Zmieniaj obraz co** ustawia czas w sekundach od 1 do 86400. Zatwierdź wpis przez Tab lub opuszczenie pola. Pole jest aktywne, jeśli folder zawiera więcej niż jeden obsługiwany plik.
- **Odśwież źródło** ponownie odczytuje folder, np. po dodaniu albo usunięciu zdjęć, lub po naprawieniu uszkodzonego pliku.

Obsługiwane formaty: JPEG, PNG, BMP, GIF i TIFF. GIF/TIFF pokazują pierwszą klatkę. SVG i WebP nie należą do formatu pierwszej wersji pluginu. Zdjęcie zachowuje proporcje i mieści się w panelu. Pliki pozostają w oryginalnym miejscu; plugin ich nie kopiuje i nie modyfikuje. Ich ścieżka oraz czas pokazu są zapisane osobno dla każdej instancji.

Skanowanie folderu i dekodowanie obrazu działają poza UI. W pamięci utrzymywane jest bieżące przeskalowane zdjęcie, nie cała kolekcja. Dłuższy bok jest dekodowany do maksymalnie 1920 px. Limity ochronne: 10 000 zdjęć na folder, 50 MiB i 100 megapikseli na obraz. Uszkodzone lub niedostępne zdjęcia są pomijane. Gdy nie ma poprawnych zdjęć, panel pokazuje komunikat. Zmiana źródła anuluje poprzednie wczytywanie. Ukrycie, zmiana pulpitu i usunięcie zatrzymują timer i zwalniają obraz. Nowe pliki w folderze są uwzględniane po użyciu odświeżania; plugin nie prowadzi ciągłego monitorowania dysku.

## Zaufanie i ograniczenia wykonania

Plugin jest natywnym kodem .NET działającym w procesie aplikacji i z uprawnieniami bieżącego użytkownika. **Instaluj tylko paczki ze źródła, któremu ufasz.** Nazwa wydawcy jest deklaracją, a nie podpisem. Obecny format nie weryfikuje podpisów ani nie izoluje kodu w osobnym procesie. `capabilities` opisują działanie; nie wymuszają uprawnień.

Oddzielny AssemblyLoadContext rozdziela prywatne zależności zarządzane, ale nie chroni przed wadliwym lub złośliwym kodem. Anulowanie jest kooperacyjne: autor musi przestrzegać tokenów. Host nie może bezpiecznie przerwać arbitralnego kodu pluginu. Błędy konstruktora, inicjalizacji modułu i cyklu życia panelu są obsługiwane przez hosta; autor nadal odpowiada za własne zdarzenia UI i zadania w tle. Nie ma obietnicy stałego czasu działania dowolnego zewnętrznego pluginu.

## Format paczki `.udplugin`

To ZIP z `plugin.json` bezpośrednio w katalogu głównym oraz DLL wejściową i opcjonalnymi prywatnymi zależnościami zarządzanymi. Nie pakuj nadrzędnego folderu, aplikacji hosta, Core/Contracts, runtime .NET, danych użytkownika ani sekretów.

```text
PhotoFrame-1.0.0.udplugin
  plugin.json
  PhotoFrame.Plugin.dll
```

Przykładowy pełny manifest:

```json
{
  "id": "pl.uberdashboard.photoframe",
  "name": "Ramka na zdjęcia",
  "publisher": "UberDashboard",
  "version": "1.0.0",
  "apiVersion": "1.1.0",
  "minHostVersion": "1.2.0",
  "targetFramework": "net10.0-windows",
  "entryAssembly": "PhotoFrame.Plugin.dll",
  "entryType": "PhotoFrame.Plugin.PhotoFrameModule",
  "dataSchemaVersion": 1,
  "dependencies": [],
  "capabilities": ["Odczyt lokalnego obrazu lub folderu wybranego przez użytkownika"],
  "panels": [
    { "typeId": "pl.uberdashboard.photoframe.frame", "name": "Ramka na zdjęcia" }
  ]
}
```

`id` to stały identyfikator reverse-DNS zapisany małymi literami. Nie używaj `pl.uberdashboard.builtin`. `version`, `apiVersion` i `minHostVersion` zapisuj jako `major.minor.patch`. Major API musi być zgodny z hostem, a minor nie może być od niego wyższy. Plugin 1.0 może działać z kontraktem 1.1. Framework obecnie musi wynosić `net10.0-windows`. `entryAssembly` jest pojedynczą nazwą DLL, `entryType` pełną nazwą publicznego typu z konstruktorem bez parametrów. Wszystkie panele muszą być deklarowane i dokładnie odpowiadać rejestracjom modułu. `TypeId` musi być unikalne w całej aplikacji; stosuj prefiks identyfikatora pluginu.

Prywatne biblioteki zarządzane umieść obok DLL wejściowej. Core i Contracts są współdzielone z hostem. Nie odwołuj się do implementacji App, Infrastructure ani Application. `dependencies` to metadane, a nie instrukcja pobierania paczek: host niczego nie pobiera. API 1.1 nie wspiera prywatnych bibliotek natywnych ani ich wyszukiwania w folderach runtime. Plugin musi być biblioteką działającą we frameworku desktopowym hosta, bez osobnego EXE i runtime.

Limity instalatora: ZIP do 64 MiB, rozpakowana zawartość do 128 MiB, najwyżej 512 wpisów, manifest do 64 KiB i najwyżej 32 typy paneli. Blokowane są ścieżki absolutne, `..`, alternatywne strumienie NTFS, dowiązania, nazwy urządzeń Windows, pliki znacznika `.enabled`, powtarzające się nazwy oraz konflikty TypeId. Rozpakowanie i uruchomienie modułu odbywają się w tle. Dopiero kompletna poprawna rejestracja publikuje paczkę; błąd usuwa staging. Aktywne paczki są w `%LOCALAPPDATA%\UberDashboard\Plugins`; nie edytuj ich podczas pracy aplikacji.

## Budowa własnego pluginu

Pełny działający przykład znajduje się w `plugins/PhotoFrame`. Skopiuj projekt do nowego folderu, zmień nazwę assembly, namespace, manifest, ID pluginu i TypeId. Projekt targetuje `net10.0-windows`, włącza `UseWPF` i odwołuje się wyłącznie do `UberDashboard.Contracts.csproj` (Core jest zależnością przechodnią). Wymagane są Windows i .NET SDK 10.

```powershell
dotnet build plugins/PhotoFrame/PhotoFrame.Plugin.csproj -c Release
pwsh -NoProfile -File scripts/BuildPhotoFramePlugin.ps1
```

Druga komenda buduje przykład i tworzy gotowy `.udplugin` w unikalnym folderze. Sprawdza kod zakończenia kompilacji; nie pakuje po błędzie i nie nadpisuje istniejącego katalogu wynikowego. Dla własnego pluginu dostosuj ścieżki i nazwy w kopii tego skryptu, dodając jego prywatne zależności do ZIP.

### Kontrakty i wątki

- `IPluginModule.InitializeAsync(IPluginHostContext, CancellationToken)` działa w tle. Rejestruj tutaj wyłącznie metadane `IPanelDescriptor` przez `RegisterPanel`. Nie twórz WPF ani nie zmieniaj zasobów Application. Rejestracja po zakończeniu InitializeAsync jest blokowana. Moduł nie powinien uruchamiać własnego nieograniczonego cyklu życia: zadania i zasoby należą do instancji panelu.
- `IPanelDescriptor` udostępnia TypeId, DisplayName, opcjonalny IconKey, MinSize w DIP, StateSchemaVersion i Factory. Deskryptor jest niezmienny i nie zawiera danych pojedynczej instancji.
- `IPanelFactory.CreateAsync(PanelContext, token)` oraz Initialize/Restore/Capture/Suspend/Resume/PrepareForRemoval/Dispose instancji są wywoływane przez host WPF na UI. Zwracaj szybko; operacje plikowe/sieciowe i dekodowanie przenieś do async I/O lub Task.Run. Po await wróć na Dispatcher przed zmianą obiektów WPF i właściwości powiązanych z widokiem.
- `IPanelInstance.InstanceId` musi być dokładnie tym, które przekazuje PanelContext. Nie generuj nowej tożsamości podczas odtwarzania. `ViewModel` jest modelem wiązanym z kartą.
- `IPanelContent.Content` na ViewModel pozwala dostarczyć własny widok WPF. Host osadza go w zwykłej karcie. Każda instancja musi mieć własny widok. W przykładzie jest to UserControl z DataContext ustawionym na model. Nie dołączaj tego samego elementu WPF do kilku rodziców.
- `IPanelOptions` na ViewModel udostępnia HasOptions, IsOptionsOpen oraz ICommand ToggleOptionsCommand. W połączeniu z INotifyPropertyChanged daje standardowe koło zębate i stan otwarcia w nagłówku. Widoczność samych opcji obsługuje widok pluginu.
- `IPanelStateNotifications` na instancji udostępnia StateChanged, ChangeVersion i OnStatePersisted. Zgłaszaj StateChanged i zwiększaj ChangeVersion na UI przy zmianie trwałych ustawień. Host stosuje debounce 600 ms i zapisuje stan do SQLite. OnStatePersisted następuje dopiero po sukcesie, jeśli wersja nie zmieniła się podczas zapisu. Samo przesunięcie slajdu nie powinno zapisywać konfiguracji.
- `CaptureStateAsync` zwraca bajty UTF-8 zawierające poprawny JSON. `RestoreStateAsync` odtwarza stan i powinno normalizować starsze wartości lub migrować własny format. Nigdy nie zastępuj nieznanego schematu pustymi danymi. Plugin ramki odrzuca nieobsługiwany schemat i pozostawia oryginalny rekord do odzyskania.
- `PanelContext.CloseToken` jest anulowany przy zamknięciu, ukryciu i usunięciu. Host obecnie zapisuje i usuwa aktywną instancję przy ukrywaniu/przełączeniu pulpitu, a przy odtworzeniu tworzy ją ponownie. Implementuj poprawnie zarówno nową instancję i Restore, jak i Suspend/Resume.
- `DisposeAsync` musi zatrzymać timery, anulować I/O, odpiąć zdarzenia i zwolnić zasoby. Nie wolno blokować Dispatchera ani używać `.Wait()`/`.Result` na zadaniach wymagających UI.
- `PanelContext.Logger`, `Notifier` i `Storage` zapewniają logi, globalne toasty i wydzielony magazyn instancji. Nie sięgaj bezpośrednio do bazy hosta. Nie loguj sekretów ani ścieżek prywatnych zdjęć. Nie wywołuj synchronicznego Storage.ListFiles na UI dla dużych katalogów.

Widok powinien korzystać z dynamicznych zasobów hosta: `Brush.Text`, `Brush.Text.Muted`, `Brush.Surface`, `Brush.Border`, `Brush.Accent` oraz istniejących stylów `ToolbarButton`, `ThemedTextBox`. Nie nadpisuj globalnych stylów. Zachowaj klawiaturę, etykiety AutomationProperties, kontrast i przewijanie przy minimalnym rozmiarze panelu. Bitmapy przygotowane w tle muszą być zamrożone (`Freeze`) przed przekazaniem na UI.

## Weryfikacja

```powershell
dotnet build UberDashboard.slnx -c Release
dotnet test UberDashboard.slnx -c Release --no-build -v q
dotnet run --project tests/UberDashboard.PluginChecks -c Release --no-build -- Release/PluginChecks/moj-unikalny-przebieg
```

`testuj.bat` wykonuje także PluginChecks. Ten scenariusz używa osobnego profilu i prawdziwej paczki DLL: instalacja przez syntetyczne zdarzenie WPF Drop oraz kontroler ustawień, anulowanie, rollback błędnej DLL, duplikaty, panel w natywnej karcie, timer, oba motywy, zapis SQLite, ukrycie, zmiana pulpitu, usunięcie i reinstalacja, nowy kontener hosta, pojedynczy obraz i niedostępny folder. Zapisuje `results.json` i obrazy do wskazanego katalogu.

To automatyczna integracja WPF i SQLite. Odbiór ręcznego przeciągnięcia z Eksploratora, systemowych okien wyboru, dotyku, DPI i czytnika ekranu pozostaje osobnym sprawdzeniem na docelowym pulpicie. Czas pojedynczego przebiegu lokalnej paczki nie jest gwarancją szybkości dowolnego pluginu.
# Dostosowanie do rozmiaru panelu

Wtyczka może zaimplementować opcjonalny `UberDashboard.Contracts.Panels.IResponsivePanel` na własnym widoku (`IPanelContent.Content`) lub modelu widoku. Host wywołuje `OnPanelSizeChanged(double width, double height)` na wątku interfejsu po zmianie dostępnego rozmiaru treści; wymiary są podane w DIP. Wtyczka powinna zmieniać układ szybko, zachowywać stan i umożliwić przewijanie przy małym rozmiarze. Starsze wtyczki działają bez implementacji tego interfejsu; host nie może automatycznie przebudować ich wewnętrznych kontrolek. Przykładem jest `plugins/PhotoFrame/PhotoFrameView.cs`.
