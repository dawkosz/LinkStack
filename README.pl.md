# LinkStack — instrukcja

Przenośna aplikacja Windows do używania kabla i Wi-Fi równocześnie przy pobieraniu przez wiele połączeń. Interfejs jest po polsku. Brak konta, abonamentu i limitu transferu w aplikacji; pakiet operatora karty SIM nadal obowiązuje.

## Steam

1. Pobierz paczkę z **Releases** i rozpakuj wszystkie pliki do jednego folderu.
2. Uruchom `LinkStack.exe` i rozłącz VPN.
3. Zaznacz Ethernet i Wi-Fi — każde musi mieć własny dostęp do internetu.
4. Zaznacz **Tryb Steam (administrator)** i kliknij **Włącz łączenie**. Potwierdź UAC. Nowe okno uruchomi silnik automatycznie.
5. Wstrzymaj i wznów pobieranie gry. Jeśli licznik Steam nie rośnie, zamknij całkowicie Steam i uruchom go ponownie przy aktywnym LinkStack.
6. Sprawdź, czy obie karty pokazują pobieranie, a licznik przechwyconych połączeń Steam rośnie.
7. Przed powrotem do VPN kliknij **Zatrzymaj**.

Steam używa równoległych połączeń, które LinkStack może rozdzielać między karty. Pojedynczego strumienia TCP program nie przyspieszy. Nie przejmuje starych sesji i obejmuje tylko nowe IPv4 TCP na portach 80/443.

## Porównanie prędkości

Dla tego samego pobierania porównaj osobno kabel, osobno Wi-Fi, a potem oba. Zmieniaj wybór kart po zatrzymaniu LinkStack i twórz nowe sesje pobierania. Daj każdej próbie 1–2 minuty; pomijaj rozpakowywanie i instalację gry. Obie karty muszą przenosić dane jednocześnie, a wynik łączny należy porównać z szybszym pojedynczym łączem.

Wagi 1–10 określają udział nowych połączeń, nie udział w bajtach. Na początek użyj 1 i 1. Liczniki dotyczą ruchu obsłużonego przez LinkStack, nie całego komputera.

## Pozostałe tryby

- Proxy HTTP/HTTPS: `127.0.0.1:17881`.
- SOCKS5 TCP: `127.0.0.1:17880`.
- **Proxy Windows dla przeglądarek** jest opcjonalne. Nie wszystkie aplikacje respektują proxy systemowe.
- Pole URL pobiera bezpośredni plik HTTP/HTTPS w częściach, jeśli serwer obsługuje wiarygodne zakresy i wersjonowanie pliku.
- **Sprawdź oba łącza** wykonuje diagnostykę HTTPS osobno przez każdą kartę.

## Diagnostyka i wymagania

Windows 10/11 x64, .NET Framework 4.8. Tryb Steam wymaga uprawnień administratora oraz dołączonych plików WinDivert. Nie zmienia metryk i tras Windows. Jest to wczesna wersja eksperymentalna, zweryfikowana na jednym komputerze.

Lokalne pliki diagnostyczne: `%LOCALAPPDATA%\LinkStack\status.txt` i `events.log`. Nie są wysyłane poza komputer. Nie publikuj ich bez przejrzenia — mogą zawierać adresy sieciowe i nazwy plików pobieranych przez Ciebie.

W razie niedziałających przeglądarek zatrzymaj LinkStack i uruchom go ponownie. Program przy starcie sprząta własne osierocone proxy, jeśli lokalny serwer nie działa; nie usuwa konfiguracji proxy innych aplikacji.

Instrukcja budowania, ograniczenia i licencje są w [README](README.md).
