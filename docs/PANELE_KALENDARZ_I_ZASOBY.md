# Kalendarz i monitor zasobów

Oba panele są dostępne w trybie edycji, w menu dodawania paneli.

## Kalendarz

Lista nad kalendarzem przełącza widok między pełnym miesiącem, tygodniem, dwoma tygodniami, jednym dniem, własnym zakresem i listą wydarzeń. Ustawienia panelu pozwalają wybrać od 1 do 90 dni dla własnego zakresu i listy, włączyć godziny oraz pokazać puste dni w widoku jednokolumnowym. Strzałki przesuwają wyświetlany zakres, a przycisk **Dziś** wraca do bieżącej daty. W siatce można najechać na dzień lub wydarzenie, aby zobaczyć pełny opis.

1. W projekcie Google Cloud włącz Google Calendar API i skonfiguruj ekran zgody OAuth. Jeśli aplikacja działa w trybie testowym, dodaj swoje konto jako użytkownika testowego.
2. Utwórz klienta OAuth typu **Aplikacja komputerowa** i pobierz jego plik JSON. [Instrukcja Google](https://developers.google.com/identity/protocols/oauth2/native-app)
3. W nagłówku panelu otwórz jego opcje i wybierz **Połącz Google**. Wskaż pobrany plik i dokończ logowanie w przeglądarce.
4. Identyfikator **primary** wskazuje główny kalendarz konta. Aby wybrać inny dostępny kalendarz, wpisz jego identyfikator z ustawień Kalendarza Google.

Widok kalendarza odczytuje rozwinięte wydarzenia po zmianie zakresu i co pięć minut. **Odśwież** pobiera je od razu. W wersji roboczej przycisk **Synchronizacja dwukierunkowa** osobno prosi o zgodę na zapis i otwiera edytor tworzenia, zmiany podstawowych danych i usuwania wydarzeń. Dane oczekujące na wysłanie są przechowywane w istniejącej lokalnej bazie. Operacje dotyczące uczestników wymagają zatwierdzenia w kolejce edytora. Edycja istniejącej serii wymaga potwierdzenia, że dotyczy całej serii; edycja pojedynczego wystąpienia i podział serii nie są jeszcze ukończone.

Dane sesji są zaszyfrowane dla bieżącego użytkownika Windows, osobno dla instancji panelu. Tożsamość konta jest sprawdzana przed użyciem sesji do wysyłania zmian. **Odłącz** usuwa dane sesji tego panelu; lokalna kolejka i dane nie są w ten sposób usuwane. Zgodę można odwołać w ustawieniach konta Google. Pełny zakres funkcji i testów kont pozostaje otwarty: [stan wdrożenia](SYNCHRONIZACJA_DWUKIERUNKOWA.md).

## Monitor zasobów

Monitor co dwie sekundy aktualizuje użycie CPU, GPU, RAM i VRAM, temperaturę CPU i GPU oraz dane wentylatorów. Każda rozpoznana karta graficzna ma własne wiersze pomiarów. Procent sterowania oznacza wartość raportowaną przez kontroler, a RPM to zmierzone obroty. Obie wartości zależą od czujników dostępnych na danym sprzęcie. Wiersz bez pomiaru pokazuje **Niedostępne**.

Dostęp do części czujników płyty głównej i CPU zależy od sterownika, sprzętu i uprawnień. Odczyt zapewnia [LibreHardwareMonitor 0.9.6](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6).
