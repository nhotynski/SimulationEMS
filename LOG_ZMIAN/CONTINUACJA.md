# Handoff projektu: Industrial Conveyor Simulator

Data notatki: 2026-11-11

## Po co jest ten plik

Przeczytaj tę notatkę na początku kolejnej sesji. To jest stan prac i historia problemów z tej rozmowy. Użytkownik oczekuje działania sprawdzonego w otwartym Unity, nie tylko informacji, że zewnętrzny build przechodzi. Nie powtarzaj bez potrzeby szerokiego mapowania projektu. Przed każdą poprawką zbierz jeden konkretny błąd lub objaw, a po poprawce wykonaj bezpośrednią walidację.

## Cel projektu

Zbudować w Unity 6 działający symulator przemysłowego przenośnika podwieszanego z zamkniętym torem, wieloma wózkami/zawieszkami, niezależnym sterowaniem napędami, diagnostyką runtime i możliwością obserwacji sceny. Najważniejsze kryterium użytkownika: funkcja ma działać w uruchomionej scenie Play Mode, nie tylko kompilować się.

## Pierwotny plan i wykonane fazy

1. **Tor parametryczny**: definiowanie odcinków prostych i łuków, walidacja zamknięcia i przecięć, próbkowanie pozycji i ramy toru, generowanie wizualizacji oraz edytor konfiguracji.
2. **Typy i zawieszenie**: definicje typów wózków jako ScriptableObject, wizualne prefabrykaty i niezależne próbkowanie przedniego/tylnego koła na torze.
3. **Symulacja ruchu**: manager z krokiem stałym, niezależnymi prędkościami docelowymi, bezpieczeństwem odstępów dla różnych długości wózków oraz sterowaniem zatrzymaniem.
4. **HMI i scena demonstracyjna**: diagnostyka runtime z pozycją i prędkością, zmiana prędkości, dodawanie/usuwanie typów i sterowanie kamerą.

Architektura ruchu jest kinematyczna, bez Rigidbody: `TrackPath` jest źródłem prawdy, a zawieszenie wyznacza world-space z próbek toru.

## Najważniejsze pliki

- `Assets/_Project/Scripts/Track/TrackGeometryBuilder.cs`, `TrackPath.cs`, `TrackConfiguration.cs`: matematyczny tor, walidacja i próbkowanie.
- `Assets/_Project/Scripts/TrackGenerator/TrackVisualization.cs`: generowanie szyn i podpór.
- `Assets/_Project/Editor/TrackConfigurationEditor.cs`: inspektor konfiguracji i podgląd.
- `Assets/_Project/Scripts/TrolleyTypes/TrolleyTypeDefinition.cs`: parametry typu wózka.
- `Assets/_Project/ScriptableObjects/TrolleyTypes/TrolleyType_3m.asset`, `TrolleyType_5m.asset`, `TrolleyType_7m.asset`: dostępne typy.
- `Assets/_Project/Scripts/Suspension/TrolleySuspension.cs`: pozycjonowanie wizualizacji zawieszki i zmiana typu/pozycji runtime.
- `Assets/_Project/Scripts/Movement/TrolleySimulationManager.cs`: inicjalizacja, symulacja, odstępy, dostępne typy oraz API dodawania/usuwania/przesuwania.
- `Assets/_Project/Scripts/Movement/TrolleyData.cs`, `TrolleyDriveModel.cs`: stan runtime i kinematyka.
- `Assets/_Project/Scripts/HMI/TrolleyClickProbe.cs`: właściwy, aktywny panel IMGUI.
- `Assets/_Project/Scripts/HMI/TrolleyDiagnosticWindow.cs`: stary UI Toolkit, komponent sceny jest wyłączony.
- `Assets/_Project/Scripts/HMI/TrolleyDiagnosticOverlay.cs`: stary overlay, komponent sceny jest wyłączony.
- `Assets/_Project/CameraControl/TrolleyCameraController.cs`: nowy kontroler kamery. Jest poza `Scripts/Project.Simulation.asmdef`, celowo w domyślnym `Assembly-CSharp`, aby korzystać z Input System bez błędu referencji.
- `Assets/_Project/Scenes/TrackTestScene/TrackTestScene.unity`: scena testowa, trzy początkowe wózki, manager, typy i kamera.
- `Assets/_Project/Tests/Editor/TrolleyMovementTests.cs`: testy EditMode.

## Aktualne zachowania UI i symulacji

- Panel diagnostyczny otwiera się wyłącznie przyciskiem w prawym górnym rogu. Centralny przycisk został usunięty.
- Panel jest IMGUI, ma nieprzezroczyste tła i obsługuje pozycję, prędkość rzeczywistą/docelową, `SET`, `MOVE`, `DEL` oraz dodanie wózka wybranego typu.
- Dostępne typy: 3 m, 5 m i 7 m. Scena ma serialized array `availableTrolleyTypes`; manager ma też fallback edytorowy, który ładuje znane ścieżki assetów, a następnie szuka `TrolleyTypeDefinition` przez AssetDatabase.
- `ADD` wyświetla potwierdzenie z ID/pozycją, przewija listę do nowego wpisu i zapisuje wyjątek w Console. Domyślna pozycja jest szukana w najluźniejszym miejscu toru.
- `MOVE` przyjmuje odległość w metrach wzdłuż zamkniętego toru, zatrzymuje zawieszkę w nowym miejscu i odrzuca pozycje powodujące kolizję.
- `DEL` usuwa aktywny runtime wózek; ostatniego wózka nie da się usunąć.
- Zmiana liczby przez manager tworzy/ukrywa zawieszki; limit 1–100.
- `Application.runInBackground = true` jest ustawiane w `TrolleySimulationManager.Awake`, aby symulacja działała, gdy Unity straci fokus.
- Kamera: `W/S` idzie w pełnym kierunku patrzenia (także składowa pionowa), `A/D` strafuje poziomo, `Shift` przyspiesza, prawy przycisk myszy + ruch myszy obraca widok. Kursor jest blokowany wyłącznie podczas trzymania RMB.
- W scenie startowy kąt kamery to 45° w dół (położenie 40, 27, 100; yaw -90°). Ograniczenie minimalnej wysokości kamery zostało dodane, ale następnie usunięte po korekcie użytkownika: użytkownik mówi, że nie zjeżdża poniżej toru.

## Problemy napotkane i rozwiązania

- W Unity pojawił się `CS0111` dla zdublowanego `ToggleFromClickProbe` w `TrolleyDiagnosticOverlay.cs`. Duplikat został usunięty.
- Później Play Mode blokował `CS0234`: `UnityEngine.InputSystem` nie był referencją custom `Project.Simulation.asmdef`. Dodawanie referencji do asmdef nie rozwiązało kompilacji Unity. Ostateczne rozwiązanie: kontroler kamery przeniesiony do `Assets/_Project/CameraControl`, poza folder asmdef. `Project.Simulation.asmdef` ma pustą listę referencji. Zwykły build po regeneracji csproj i batchowa kompilacja Unity przechodziły.
- Zegar środowiska był rozbieżny z datami timestampów w `Library`; Unity nie zawsze zauważało pliki. Jednorazowo przestawiono timestamps zmienionych assetów, a następnie uruchomiono batchową kompilację Unity. Nie zmieniano zegara systemowego i nie kasowano Library.
- UI Toolkit nie reagowało stabilnie. Ostateczny panel jest rysowany przez IMGUI w `TrolleyClickProbe`; stare komponenty w scenie są wyłączone.
- Użytkownik zgłosił `No trolley types configured` po kliknięciu `ADD`. Dodano w managerze fallback do załadowania assetów 3/5/7 m. Kod jest w repo, ale runtime `ADD` nie został później potwierdzony przez użytkownika. Przy wznowieniu sprawdź panel i Console, nie zakładaj, że działa tylko dlatego, że build przeszedł.
- Pierwsza diagnoza szarego obszaru jako zejścia pod tor była błędna. Użytkownik wyjaśnił, że nie schodzi pod tor. W scenie nie znaleziono obiektu podłoża; szarość to dolna część tła/skyboxa, a tory były przy horyzoncie. Limit wysokości kamery usunięto; zamiast niego zwiększono pitch startowy do 45°.
- Batch Unity został uruchomiony z `-batchmode -quit`; po zakończeniu proces zamknął się. Nie zabijaj aktywnej instancji Unity siłą. W ostatnim stanie Unity Editor GUI nie działał; Unity Hub został.

## Testy i walidacja

- Wcześniejszy stan: 21/21 testów Unity EditMode przechodziło przed ostatnimi zmianami HMI/kamery.
- Dodano testy `SimulationManager_ResizesTrolleyFleetAndRebuildsDriveBuffers` i `SimulationManager_AddsMovesAndRemovesSelectedTrolleyType`; dodana została też asercja liczby typów.
- `dotnet test Assembly-CSharp-Editor.csproj --no-build` nie uruchamia testów Unity, tylko kończy się bez test-runnera. Nie raportuj tego jako wykonanych testów.
- Ostatni znany `dotnet build Assembly-CSharp-Editor.csproj` po zmianie ruchu kamery: sukces, 23 istniejące ostrzeżenia `CS0649` o polach serializowanych.
- Batchowa kompilacja Unity po przeniesieniu kontrolera do `Assembly-CSharp`: `Scripting: successfully reloaded assembly`, `Batchmode quit successfully invoked`, brak błędów w wyjściu sprawdzonym wtedy. Późniejsze zmiany: pełny kierunek W/S, usunięcie limitu wysokości i zmiana startowego pitcha 25°→45° zostały sprawdzone przez dotnet build, ale nie batch-compilowane po raz kolejny.

## Stan do wznowienia i plan następnej sesji

1. Otwórz projekt w Unity Hub (`C:\Users\PLRADHON\My project`) na Unity `6000.6.8f1`; poczekaj na import/kompilację. Otwórz scenę `Assets/_Project/Scenes/TrackTestScene/TrackTestScene.unity` i sprawdź Console.
2. Jeśli pojawi się błąd, napraw pierwszy bieżący błąd z Console/`Editor.log`; nie opieraj diagnozy na starych liniach logu. Potwierdź timestamp `Library/ScriptAssemblies/Assembly-CSharp.dll` i nowy `Assembly-CSharp.csproj` po imporcie.
3. W Play sprawdź kamerę: początkowy widok 45° w dół, `W/S` w pełnym kierunku patrzenia, `A/D` poziomo, `Shift` szybciej, RMB-look.
4. Otwórz prawy panel. Sprawdź czy dropdown pokazuje 3 m/5 m/7 m oraz czy `ADD` dodaje, przewija do nowego wiersza i pokazuje potwierdzenie. Przy `No trolley types configured` sprawdź `TrolleySimulationManager.AvailableTrolleyTypes` i log `Loaded 3 trolley types from project assets.`.
5. Zweryfikuj `MOVE`, `SET`, `DEL` w runtime oraz czy praca w tle działa po przełączeniu fokusu.
6. Uruchom testy EditMode przez Unity Test Runner (nie `dotnet test`) i odnotuj rezultat.

## Ważne dla kolejnej sesji

- Zacznij od tej notatki i aktualnego Console; użytkownik jest zmęczony powtarzaniem kroków i ogólnymi instrukcjami.
- Nie twierdź, że funkcja działa w Play, dopóki nie ma runtime potwierdzenia. Rozróżniaj build .NET, kompilację Unity i test działania w scenie.
- Zachowuj istniejące pliki i zmiany użytkownika; nie czyść `Library`, nie resetuj Git i nie ubijaj Unity siłą.

## Aktualizacja: kadr toru i zoom

Użytkownik pokazał kadr z dużym szarym obszarem i tylko fragmentami szyn. Diagnoza zejścia kamery pod tor była błędna; użytkownik potwierdził, że tak się nie dzieje. W scenie nie znaleziono obiektu podłoża, a `RenderSettings.m_SkyboxMaterial` wskazuje wbudowany skybox, którego dolna część wygląda jak jednolite szare podłoże.

Analiza rzutu: kamera jest ortograficzna. Przy `orthographic size: 35` i aspect 16:9 kadr obejmuje około 70 m wysokości i 125 m szerokości. Zamknięty tor ma w przybliżeniu 125 m rozpiętości X i 370 m Z, więc w kadrze mieścił się tylko fragment pętli. Szary obszar w czerwonym oznaczeniu to tło poza widoczną geometrią, a nie mesh zasłaniający tor.

Ostatnia poprawka:
- Dodano zoom kółkiem myszy dla kamery ortograficznej; zakres `orthographicSize` 20-240, czułość 0.22.
- `TrackTestScene` startuje teraz z `orthographic size: 120`, aby objąć pętlę w jej dłuższym wymiarze; kółkiem można przybliżyć zawieszki.
- Cofnięto błędny limit minimalnej wysokości kamery. Startowy pitch 45 stopni pozostaje.
- Build po dodaniu zoomu przechodził. Zmiana `orthographic size` w scenie została wykonana po tym buildzie i wymaga jeszcze sprawdzenia w Unity Play Mode.
- Po kolejnym zrzucie okazało się, że sam zoom 120 nie centrował całej pętli: kamera była przy `z=100`, a tor rozciąga się w przybliżeniu od `z=0` do `z=370`. Prawa część wypadała poza kadr. Sceniczna kamera została przesunięta do środka obwiedni toru `(x=47, y=27, z=185)`. Czerwone zaznaczenie pokazuje puste wnętrze pętli/skybox, nie przesłaniający obiekt.
- Użytkownik poprosił o podłogę. `TrackVisualization` generuje teraz trzeci submesh: nieprzezroczystą płaszczyznę na `y=-0.05`, obejmującą próbkowaną obwiednię toru z marginesem 30 m, z osobnym kolorem materiału. Materiał jest niszczony razem z pozostałymi wygenerowanymi zasobami przy przebudowie.
- Build po dodaniu podłogi: sukces, 23 istniejące ostrzeżenia `CS0649`. Wizualnego rezultatu podłogi w Unity Play Mode jeszcze nie potwierdzono; po otwarciu sceny `TrackVisualization` przebudowuje mesh w `OnEnable`/`OnValidate`.
- Pierwszy zrzut po dodaniu podłogi pokazał, że jej ciemny kolor zlewał się z dolnym skyboxem. Kolor podłogi zmieniono na jasny beton `0.58, 0.60, 0.57`; wartość i margines 30 m zapisano jawnie w scenie. Do sprawdzenia w Play Mode.
- Kolejny zrzut po ruchu kamery ujawnił krawędź skończonej podłogi: kamera wyjeżdżała poza margines 30 m i odsłaniała skybox. Margines zmieniono na 2000 m w domyślnej wartości `TrackVisualization` i jawnie w `TrackTestScene`.

Nie wracaj do wyjaśnienia „kamera jest pod torem” bez nowego dowodu. Najpierw sprawdź ortho size, aspect Game View, skybox i rzeczywiste rzutowanie toru.

## Aktualizacja: przyczyna pustego dołu kadru (near clip) - POTWIERDZONE przez użytkownika

Objaw: podłoga wyglądała na ucięta, dół widoku pusty/szary z ostrą krawędzią, niezależnie od rozmiaru podłogi (margines 30 m i 2000 m dawały ten sam efekt).

Przyczyna: kamera jest ortograficzna, wisi ok. 27 m nad ziemią i patrzy w dół pod 45°. W kamerze ortograficznej głębokość liczy się wzdłuż osi patrzenia, więc geometria obok/za pozycją kamery ma ujemną głębokość i była ucinana przez domyślny near clip plane (0.3). Nie był to problem podłogi, pozycji kamery ani skyboxa.

Poprawka (działa, użytkownik potwierdził): near clip plane = -2000, far clip plane = 4000.
- `TrolleyCameraController.Awake` ustawia `nearClipPlane`/`farClipPlane` kamery ortograficznej.
- `TrackTestScene` ma te same wartości zapisane w Main Camera.

Wskazówka na przyszłość: przy "znikającej geometrii" w kamerze ortograficznej najpierw sprawdź near/far clip plane, dopiero potem rozmiar obiektów i pozycję kamery. Nie wracaj do hipotezy "kamera pod torem".

Stan Git: projekt wypchnięty/do wypchnięcia do https://github.com/nhotynski/SimulationEMS (gałąź main). Folder `Nowy folder` (lokalny build gry) jest w .gitignore.

## Aktualizacja: build gry (Build And Run) - sceny i shadery

1. Build pokazywał domyślną scenę z magentową piłką: w Build Profiles była tylko `SampleScene`. Ustawiono `TrackTestScene` w `ProjectSettings/EditorBuildSettings.asset`.
2. Build kończył się `Failed` na kroku "Building scenes". Przyczyna: w `TrackTestScene` komponent `TrolleyDiagnosticOverlay` (fileID 1600000135) nie był wpisany w `m_Component` obiektu `Trolley Simulation Manager`. Dopisano go. Build po tej zmianie przechodzi i uruchamia grę (panel DIAGNOSTICS działa w buildzie).
3. W buildzie nie rysował się tor, podłoga ani wózki (tylko tło kamery i panel IMGUI). Podejrzana przyczyna (NIEPOTWIERDZONA w buildzie): `Shader.Find("Universal Render Pipeline/Lit")` zwraca null w playerze (shader wycięty), więc materiały nie powstawały. `Player.log` (`C:\Users\PLRADHON\AppData\LocalLow\DefaultCompany\My project\Player.log`) nie zawierał wyjątków.
4. Wpis URP/Lit w `GraphicsSettings.asset` został nadpisany przez otwarte Unity. NIE edytuj plików `ProjectSettings` ręcznie, gdy edytor jest uruchomiony; zmiany zostaną utracone.
5. Poprawka: `Assets/_Project/Resources/ConveyorLit.mat` (shader URP/Lit, wchodzi do buildu przez Resources) oraz `ConveyorShaders.Lit` (`Scripts/Core/ConveyorShaders.cs`); `TrackVisualization` i `TrolleySuspension` używają jej zamiast `Shader.Find`. Kompilacja Unity i `dotnet build` OK. Do potwierdzenia: nowy Build And Run pokazuje tor, podłogę i wózki. Jeśli nie, następny krok to sprawdzić `Player.log` i przypisać materiał jako zasób w scenie (pole `railMaterial`).

Zasady pracy: po każdej zmianie dopisz ją tutaj i sprawdź `Logs/Editor.log` oraz `Player.log`, czy coś się nie wydarzyło (błędy kompilacji, wyjątki, zmiany plików przez Unity).

## Aktualizacja: pozycje w mm i żywa pozycja w panelu

- `TrolleyClickProbe`: wszystkie pozycje w panelu są teraz w milimetrach (ADD POSITION mm, ACTUAL POS mm, MOVE TO mm); model nadal liczy w metrach (konwersja ×0.001 przy ADD/MOVE).
- Nowa kolumna `ACTUAL POS mm` to etykieta tylko do odczytu, odświeżana co klatkę z `TrolleyData.SimulationPositionMeters`. Wcześniej jedna edytowalna kolumna była wypełniana raz i trzymała stare wartości (stąd "pozycja się nie aktualizuje").
- `MOVE TO mm` + `MOVE` przenosi zawieszkę (`MoveTrolleyToPosition`); pole nie jest nadpisywane w trakcie pisania.
- `dotnet build`: 0 błędów. Po edycji `Library/ScriptAssemblies` miało jeszcze starszy timestamp niż plik (Unity nie przekompilowało jeszcze). Niezweryfikowane w Play/buildzie.

## Aktualizacja: wysyłanie pozycji zawieszek po UDP (okno UDP CONFIG)

Nowe pliki: `Scripts/Communication/PositionTelegram.cs`, `Scripts/Communication/UdpPositionSender.cs`, test `Tests/Editor/PositionTelegramTests.cs`. Zmieniony: `HMI/TrolleyClickProbe.cs` (przycisk `UDP CONFIG [+]` na lewo od `DIAGNOSTICS`, osobne przesuwalne okno). `UdpPositionSender` dodaje się do obiektu managera w runtime (nic nie zmieniono w scenie).

Telegram (little-endian, stały rozmiar 1224 B = nagłówek 24 B + 100 rekordów x 12 B; nieużywane rekordy wyzerowane):
- Nagłówek: magic `CNVP` u32 (0x50564E43), wersja u16 (=1), liczba aktywnych rekordów u16, numer pakietu u32 (rośnie z każdym wysłanym pakietem), czas symulacji ms u32, długość toru mm u32, rozmiar rekordu u16 (=12), maks. rekordów u16 (=100).
- Rekord: id u16 (cyfry z `T-001`), typ u8 (długość korpusu w dm: 30/50/70), stan napędu u8 (`TrolleyDriveState`: 0 Ready, 1 Running, 2 Stopped, 3 EmergencyStopped, 4 Faulted), pozycja mm u32, prędkość mm/s i32.

Okno `UDP CONFIG`: wybór karty sieciowej (lista adresów IPv4 aktywnych kart + `Any`) wpisuje IP symulacji; pola `SIMULATION IP` (adres lokalny, do którego bindowane jest gniazdo, więc to wybiera kartę), `DESTINATION IP`, `DESTINATION PORT`, `INTERVAL ms` (10-2000); `START`/`RESTART`/`STOP`; licznik wysłanych pakietów i ostatni błąd. Ustawienia zapisują się w PlayerPrefs po START.

Implementacja: telegram jest odświeżany co klatkę w `Update` (na wątku głównym), a osobny wątek tła wysyła ostatni snapshot co zadany interwał (`Stopwatch` + `timeBeginPeriod(1)` na Windows, żeby 10 ms było osiągalne). Pozycje są więc tak świeże jak klatka symulacji; przy bardzo niskim FPS dane w pakietach się powtarzają.

Status: kod kompiluje się (kompilacja Simulation z nowymi plikami: 0 błędów; `dotnet build` Editor pokazuje stare błędy do czasu aż Unity przegeneruje csproj). NIE przetestowano w Unity ani przez rzeczywisty odbiór pakietów. Do zrobienia: otworzyć UDP CONFIG, START na `127.0.0.1:5000` i odebrać pakiet (np. prosty listener Python), uruchomić testy EditMode `PositionTelegramTests`.

Test na tym samym komputerze: DESTINATION IP `127.0.0.1`, port np. `5000`, odbiornik Python (`socket.SOCK_DGRAM`, `bind(("0.0.0.0", 5000))`, `struct.unpack_from("<IHHIIIHH", data, 0)` dla nagłówka i `"<HBBIi"` od offsetu `24 + i*12` dla rekordów) uruchomiony przed START. Adres źródłowy (SIMULATION IP) i docelowy muszą być z tej samej "strony": oba loopback albo oba LAN. Plik skryptu trzymaj poza `Assets`.

- UDP: błąd 'Żądany adres jest nieprawidłowy w tym kontekście' (WSAEADDRNOTAVAIL) wystąpił przy DESTINATION IP 0.0.0.0 oraz przy źródle na karcie LAN/Wi-Fi z celem 127.x. `UdpPositionSender.Begin` odrzuca teraz cel 0.0.0.0 i niezgodność loopback/sieć z czytelnym komunikatem. Na jednym komputerze: SIMULATION IP 0.0.0.0 lub 127.0.0.1, DESTINATION IP 127.0.0.1 (nie 127.0.0.0).

## Stan na koniec sesji (2026-11-11)

- Wireshark: UDP na ten sam komputer (DESTINATION IP `127.0.0.1`) łap interfejsem `Adapter for loopback traffic capture` (Npcap, typ BSD loopback), filtr `udp.port == 5000`. Do innego komputera: interfejs, którym wychodzi ruch (np. Wi-Fi). PuTTY nie odbiera UDP.
- Niezweryfikowane w Unity/buildzie: widoczność toru/podłogi/wózków w buildzie po zmianie na `ConveyorShaders`, kolumna `ACTUAL POS mm` i `MOVE TO mm`, faktyczna wysyłka UDP i odbiór pakietów, testy EditMode (`PositionTelegramTests`, testy ruchu).
- Git: zdalne repo `https://github.com/nhotynski/SimulationEMS.git` (`origin`, gałąź `main`). Wypchnięty jest tylko pierwszy commit i notatka near-clip; zmiany z buildu, panelu w mm i UDP są lokalne, niezacommitowane. Nie amenduj wypchniętych commitów.
- Pierwszy krok następnej sesji: otworzyć Unity, sprawdzić Console, zrobić Build And Run (scena `TrackTestScene`) i sprawdzić `Player.log`.
