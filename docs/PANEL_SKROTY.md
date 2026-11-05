# Panel Skróty (1.9.0)

Dodaj panel **Skróty** z katalogu paneli. Przycisk **Dodaj skrót** otwiera edytor nazwy, celu, ikony i kolorów. Cel może być pełną ścieżką do aplikacji, folderu, dokumentu lub skrótu Windows (.lnk/.url), albo adresem http:// lub https://. Dostępne są wybór pliku i folderu oraz rozwijanie zmiennych środowiskowych, np. %SystemRoot%.

Widoki: **Ikony**, **Lista**, **Przyciski**, **Kafelki**. Dwuklik lub Enter otwiera zaznaczony cel; w widoku przycisków wystarczy pojedyncze kliknięcie. Można też użyć przycisku **Otwórz**. Windows wybiera aplikację skojarzoną z dokumentem lub domyślną przeglądarkę.

Menu **⋯** udostępnia edycję, usuwanie, przesuwanie pozycji oraz wybór i wyczyszczenie tapety tego panelu. Usunięcie skrótu nie usuwa pliku ani folderu docelowego. Edycja działa na kopii: anulowanie pozostawia zapisany skrót bez zmian.

Domyślnie panel odczytuje ikonę lokalnego pliku EXE/LNK/ICO. Można zastąpić ją symbolem, emoji lub własnym obrazem PNG/JPG/BMP/ICO. Kolory tła i nazwy są niezależne dla każdej pozycji; **Kolory motywu** przywracają wygląd wynikający z motywu. Kafelki pełnią rolę małych kart ze skrótami.

Skróty, kolejność, widok, kolory i ścieżka tapety zapisują się w istniejącym magazynie stanu panelu. Odczyt obrazów, pobieranie ikon i uruchamianie celów odbywają się poza wątkiem interfejsu. Błędne cele można poprawić w edytorze i otworzyć ponownie; panel pokazuje błąd oraz globalny toast.

Obrazy są wskazywane lokalną ścieżką, nie są kopiowane do profilu. Po przeniesieniu lub usunięciu pliku trzeba wskazać go ponownie. Obrazy z UNC nie są ładowane; skróty do folderów i plików UNC mogą być otwierane przez Windows. Nieobsługiwane są argumenty poleceń i dowolne schematy URI. Aplikację wymagającą argumentów można wskazać przez przygotowany skrót .lnk. Panel nie odtwarza swobodnego rozmieszczania ikon według współrzędnych ani zagnieżdżonych paneli.

Weryfikacja: testy `ShortcutTests` obejmują walidację celów, edycję, kolejność, zapis/odczyt, uszkodzone dane i błędy otwierania. `ShortcutsChecks` sprawdza edytor bez udziału użytkownika oraz renderuje cztery widoki przy trzech rozmiarach w obu motywach; cały zestaw VisualChecks uruchamia się również z opcją `--english`.
