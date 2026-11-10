# Wzory paneli wtyczek

Projekt zawiera dwa punkty wyjścia: `simple` (pusta treść) i `modular` (dwa przykładowe elementy do usunięcia lub zastąpienia). Aby utworzyć własną wtyczkę, skopiuj folder, zmień namespace i nazwę DLL, unikalne ID w `plugin.json`, oba `TypeId` oraz nazwy. Można usunąć niepotrzebny deskryptor z modułu i odpowiedni wpis manifestu.

```powershell
dotnet build plugins/PanelTemplates/PanelTemplates.Plugin.csproj -c Release
```

Projekt korzysta tylko z publicznych `Contracts` i przechodniego `Core`; nie odwołuje się do `App`. Manifest wymaga API 1.3.0. Do paczki `.udplugin` umieszcza się bezpośrednio w ZIP `plugin.json` i DLL wtyczki, bez katalogu nadrzędnego i bez kopii DLL hosta. Przykład wymaga gospodarza obsługującego API 1.3.0.

## Wspólne elementy obu wzorów

`TemplateModule` rejestruje deskryptory w tle, deskryptor ma stabilne TypeId i fabrykę, a `TemplateInstance` zachowuje ID z `PanelContext`. Własna treść jest dostarczona przez `IPanelContent`; host zapewnia kartę, zakładki, układ zewnętrzny i wygląd. `IPanelStateNotifications` zgłasza trwałe zmiany i wersję do autozapisu. Stan ma wersję schematu; nieznany schemat jest odrzucany. Każda instancja ma własny widok i dane. Przy dodaniu I/O, timerów i opcji należy uzupełnić cykl życia, anulowanie oraz `IPanelOptions` zgodnie z `docs/WYTYCZNE_PANELI.md` i `docs/PLUGINY.md`.

## Panel modułowy

`IModularPanel` udostępnia hostowi przejściowy tryb edycji, identyfikatory i pozycje; nie zapisuj samego trybu edycji. Host udostępnia jego przełącznik w menu panelu. `TemplateModel` zapisuje pozycje w swoim JSON i zgłasza zmianę przez instancję. `ModuleGrid.Restore` przywraca stare lub niepełne pozycje, a `ModuleGrid.Move` przesuwa kolizje. `ModularView` i `ModularItemsPanel` są kodem widoku wtyczki: zapewniają siatkę, przeciąganie i Ctrl+Shift+strzałki; mogą zostać dostosowane we własnej kopii. ID elementów muszą być stabilne i unikalne w instancji. Puste pola są zachowywane; liczba kolumn wizualnych zmienia się z szerokością bez zmiany zapisanych współrzędnych.

Przed dystrybucją sprawdź niezależność instancji i zakładek, zapis i odczyt, błędny i starszy stan, przeciąganie, klawiaturę, motywy, małe rozmiary, DPI i czytnik ekranu. Wzór ma dwa elementy demonstracyjne, nie pobiera danych i nie zapewnia aplikacji funkcji domenowej. Wtyczki wykonują się w procesie hosta i nie są izolowane.
