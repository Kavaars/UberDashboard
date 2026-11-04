# Kierunek UI — miękki i kompaktowy pasek

Data: 2026-11-03, Europe/Warsaw. Baza audytu: 876a2d7 + istniejące zmiany robocze, wersja źródeł 1.6.0. Wdrożenie w WPF: 1.7.0.

## Cel

Pasek jako podstawowy sposób użycia, minimalistyczny, ciepły i delikatny wygląd. Inspiracje użytkownika: Umail i GlassWire. Zachować motywy jasny, ciemny i systemowy, wszystkie ustawienia, dane oraz wygląd poszczególnych paneli. Nowe wartości domyślne nie mogą nadpisywać zapisanych preferencji.

## Ustalenia

- Dzielniki PanelTemplates.xaml mają pełne wypełnienie i rozmiar 6 DIP. Projekt: subtelna linia 0–1 DIP niezależna od obszaru chwytania 6–8 DIP; zachowana klawiatura i zapis proporcji.
- Ustawienia mają 980×720 DIP, minimum 820×620. Cztery pełne edytory ColorWheel jeden pod drugim nadmiernie wydłużają personalizację. Projekt: próbka koloru otwierająca pełny edytor na żądanie.
- W renderze paska 1920×200 prawie pusty toolbar zajmuje osobny wiersz. Zaprojektować kompaktowe sterowanie i sprawdzić oba kierunki paska.
- FontFamily, FontSize, CornerRadius i DensityScale mają już tokeny i zapis. Odstęp paneli i wygląd linii podziału nie mają osobnych zapisanych ustawień; suwaki tych wartości w podglądzie to propozycja rozszerzenia.
- WindowModeSettings ma obecnie domyślny tryb Normal. Docelowo DockedBar dla nowego profilu; zapisany wybór i bezpieczny powrót do okna pozostają nadrzędne.
- SettingsViewModel oraz ThemeService.ApplyAsync obecnie czyszczą własne kolory przy jawnym przełączeniu motywu. Nie zmieniono tego w audycie. Docelowo wybrać model zachowania własnej palety lub zapisu oddzielnie dla motywów; unikać niejawnej utraty personalizacji.

## Inwentarz opcji do zachowania

| Grupa docelowa | Zachowywane możliwości |
| --- | --- |
| Wygląd | Motyw systemowy/jasny/ciemny, akcent, tło okna, główna i alternatywna powierzchnia, zaokrąglenia; koło HSV, jasność, alfa, HEX, powrót do koloru motywu. |
| Tekst i odstępy | Wszystkie fonty systemowe, rozmiar 11–24 DIP, gęstość 0.85–1.35. Dodatkowo proponowane osobne odstępy paneli i linia podziału. |
| Tło i przezroczystość | Wybór/usunięcie tapety, rozmycie 0–30, pokazywanie pulpitu, krycie powierzchni 0–1, widoczność treści 0.2–1, rozmycie Windows. Nie obiecywać pełnej niezależności efektów przed testem DWM. |
| Pasek i ekran | Wszystkie tryby, monitor, cztery krawędzie, grubość 200–1200 px, zapis WidgetOpacity i fallback widgetu. Niedostępne funkcje nadal wyraźnie oznaczone. |
| Aplikacja | Autostart, zasobnik, zachowanie zamknięcia, interwał pogody 5–180 min, ponowienie zapisu, reset domyślnych. |
| Pluginy | Instalacja, usuwanie, anulowanie, rzeczywisty status i opcje paneli pluginów. Zachować osobną kategorię w docelowej implementacji. |
| Opcje panelu | Ustawienia treści/działania danego typu, własne tło, przełącznik własnego tekstu, krój/rozmiar, tapeta/krycie, reset wyglądu i ponowienie zapisu. |

## Projekt i plan wdrożenia

Zwarte wiersze, wspólne wyrównanie, kategorie po lewej, pełny edytor koloru na żądanie. Menu z krótkimi nazwami i oddzielonym usuwaniem. Podstawowe działania dostępne także bez najechania myszą. Zachować fokus, dostępne nazwy, tooltipy, automatyczny zapis i podgląd na żywo.

1. Testy zgodności odczytu i zapisu dotychczasowych ustawień, zachowania palety oraz zmiany pojedynczych opcji.
2. Wspólne tokeny i opcjonalne pola personalizacji, bez destrukcyjnej migracji; zapisane nadpisania nadrzędne.
3. Szablon cienkiego dzielnika niezależny od uchwytu i zachowane sterowanie klawiaturą.
4. Zwarte ustawienia i edytory na żądanie ze wszystkimi istniejącymi bindingami/komendami.
5. Pasek, menu i nagłówki, następnie każdy panel i opcje pluginów.
6. Testy, build, VisualChecks rozszerzone o Pluginy, opcje instancji i skrajne parametry; natywny odbiór AppBar, DWM, DPI, klawiatury i Narratora.

Podgląd w rozmowie przedstawia przykładowy pasek i nowy formularz. Lokalne interakcje: motyw, kolory, font, rozmiar, odstępy, zaokrąglenia, linia podziału, menu i kategorie. Pozostałe kategorie są opisowym szkicem zachowywanych funkcji. Lista fontów podglądu jest skrócona; aplikacja zachowa wszystkie fonty systemowe. Pogoda i treści są demonstracyjne. Podgląd nie zapisuje preferencji aplikacji.

## Weryfikacja

- Build VisualChecks Release --no-restore -m:1: PASS, 0 ostrzeżeń/błędów. Pierwsza próba dotnet run z automatycznym buildem zakończyła się niepowodzeniem bez szczegółowej diagnostyki.
- Pierwszy przebieg VisualChecks: 24 rendery, potem blokada odczytu rejestru autostartu. Ponowienie z dostępem do rejestru na osobnym profilu: PASS, 57 renderów, kontrola fontów i wejścia afirmacji. Wyniki: Release/Dev/design-audit-20261103-full. Nie jest to test AppBar ani wszystkich opcji i pluginów.
- Obejrzano bieżący render paska; personalizację porównano również z renderem z wcześniejszej kontroli tego samego dnia. To wygląd obecnej aplikacji, nie nowego projektu.
- Podgląd: składnia i unikalność identyfikatorów PASS; 11 kontroli skryptu z symulowanym DOM PASS (motywy, rozmiar, odstęp, dzielnik, akcent, kategorie, menu, opcje instancji, stan podglądu).
- Próba otwarcia lokalnego podglądu w przeglądarce odrzucona przez politykę protokołów; nie obchodzono blokady. Układ nowego podglądu nie jest wizualnie potwierdzony w przeglądarce. Kontrole skryptu nie zastępują takiego odbioru.
- Pełnych testów jednostkowych i integracyjnych nie uruchamiano: kod aplikacji nie został zmieniony. Wersji nie podnoszono.

## Wdrożenie 1.7.0

- Domyślne zaokrąglenie wynosi 18 DIP. Domyślny odstęp między panelami to 10 DIP z obszarem chwytania 6 DIP i linią 1 DIP. Odstęp ma zakres 6–32 DIP, a linia 0–3 DIP.
- Własna paleta obowiązuje po przełączeniu motywu. Każdy kolor można osobno przywrócić do wartości motywu w edytorze. Zapisany tryb okna pozostaje nadrzędny, a domyślny start nowego profilu to pasek.
- Przeszły build Release bez ostrzeżeń, 298 testów, 96 renderów i integracja pluginów. Rendery zawierają obie orientacje paska, sześć kategorii ustawień, rozmiar tekstu 24 DIP i minimalne okno ustawień. Na osobnym profilu sprawdzono start zadokowanego paska, menu, podmenu dodawania oraz wybór i autozapis akcentu. Zestaw monitorów, pozostałe krawędzie i konfiguracje DPI wymagają osobnego odbioru.
- Build Windows x64: `Release/Dev/v1.7.0_20261103-2120`.
