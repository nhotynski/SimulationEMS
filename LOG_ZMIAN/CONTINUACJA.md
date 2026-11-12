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

## Aktualizacja (2026-11-12): tor z zewnętrznego pliku Excel (.xlsm / .xlsx)

- Nowe: `Scripts/Track/TrackFileReader.cs` (zip+XML, bez bibliotek; makra ignorowane), `TrackLayoutReader.cs` (format WinMOD SIMLINE Layouter), `TrackSource.cs` (wybór/zapamiętanie pliku w PlayerPrefs `Track.FilePath`, cache, fallback), `HMI/NativeFileDialog.cs` (okno Windows "Otwórz plik"), `Tests/Editor/TrackFileReaderTests.cs`, szablon prostego formatu `Tracks/TrackTest.xlsx`. Zmienione: `TrackConfiguration.Create(...)`, `TrolleySimulationManager`, `TrolleySuspension`, `TrackVisualization` (w Play pobierają tor przez `TrackSource.Resolve(assetZeSceny)`), `TrolleyClickProbe` (przycisk `TRACK FILE`: BROWSE / LOAD TRACK / USE SCENE TRACK).
- Zachowanie: przy starcie wczytywany jest zapamiętany plik; brak lub błąd pliku -> `Debug.LogError` + fallback do assetu sceny (błąd widać w oknie TRACK FILE). `LOAD TRACK` waliduje plik (poprawny, zamknięty tor), zapisuje ścieżkę i przeładowuje scenę (wózki wracają na pozycje ze sceny, UDP trzeba uruchomić ponownie). W Edit Mode zawsze asset sceny.
- Format Layouter (pliki `Szyna1.xlsm`, `Szyna_poprawiona.xlsm`; nagłówek z kolumną `ConnPre`): tor = pierwszy element EMS bez poprzednika; idzie łańcuchem `ConnPre`; `EMS_Track` = prosta (Length mm), `EMS_CurveLeft/Right` = łuk (Angle°, Radius mm), zwrotnica `...In` -> wyjście Q0, `...Out` -> proste wyjście Q1 (brak Q1 = koniec toru); zwrotnica to prosta o długości `Length`; odgałęzienia (podłączone wstecz `I1/Q0`, Q0 zwrotnicy Out) i wiersze nie-EMS są pomijane. Nieobsługiwany element na trasie głównej (np. `EMS_TrackLift`) = błąd z nazwą elementu. Kolumny pozycji (PosOff*, RotRel) są ignorowane: tor startuje w (0,0,0), kierunek 0°.
- Prosty format (alternatywa): arkusze `Parameters` (Parameter/Value) i `Segments` (Label, Type, Length_m, Direction, Angle_deg, Radius_m); wykrywany automatycznie, gdy oba arkusze istnieją.
- Walidacja bez Unity (program konsolowy z atrapami typów, tymczasowy, poza repo): `Szyna_poprawiona.xlsm` i `Szyna1.xlsm` -> 40 segmentów, 518,627 m, zamknięcie z odchyłką 1,2 mm, kierunek 360°, brak samoprzecięć; szablon `TrackTest.xlsx` -> 11 segmentów, 1000 m, zamknięty. Kompilacja Simulation (tymczasowy csproj): 0 błędów, 0 ostrzeżeń.
- NIEZWERYFIKOWANE w Unity: kompilacja w edytorze, testy EditMode `TrackFileReaderTests`, okno TRACK FILE, dialog BROWSE, przeładowanie sceny, wygląd toru z pliku (kamera sceny jest ustawiona pod stary tor; zoom kółkiem 20-240, może wymagać przesunięcia). Początkowe pozycje wózków ze sceny muszą mieścić się na nowym torze.

## Aktualizacja (2026-11-12, wieczór): zwrotnice - tor jako graf

Obraz `track_przyklad.png` w katalogu projektu był tylko ilustracją układu; nic z niego nie jest generowane. Topologia pochodzi wyłącznie z pliku `.xlsm`.

- Model: `Track/TrackNetwork.cs` (`TrackNetwork`, `TrackEdge`, `TrackNode`, `TrackLocation`). Krawędź = odcinek toru między węzłami; węzeł = zwrotnica `…Out` (rozdzielenie, `Split`) lub `…In` (scalenie, `Merge`). Zwykła pętla (asset sceny lub prosty format Excela) to jedna krawędź-pierścień (`IsRing`), więc stare zachowanie i testy pozostają. `TrackNode.RequestOutIndex(i)` zgłasza zmianę (wykonana, gdy zwrotnica wolna), `ForceOutIndex` ustawia od razu. Domyślnie zwrotnica stoi na Q1 (prosto). Trasa główna = krawędź 0 prowadzona domyślnymi stanami.
- Czytnik Layoutera (`Track/TrackLayoutReader.cs`) buduje cały graf: elementy układa przez ConnPre/ConnExit/ConnEntry (łącze wprost lub wstecz `I1/Q0`), a wolne porty o tej samej pozycji (do 3 cm) i kierunku (do 2°) łączy sam (tak zamykają się pętle). Porty: Track/Curve `I0,Q0`; SwitchOut `I0 -> Q0` (łuk, odgałęzienie) lub `Q1` (prosta, długość `Length`); SwitchIn `I1` (prosta) i `I0` (łuk) `-> Q0`. Nazwy krawędzi: pierwszy element toru (np. `T1`, `T37`, `C41`, `T45`, `T51`). Nieobsługiwany typ EMS (np. `EMS_TrackLift`) lub element nieosiągalny = błąd z nazwą.
- `Szyna_poprawiona.xlsm` daje: 5 krawędzi (`T1` 504,5 m do S36, `T37` 14,2 m, `C41` 9,6 m, `T45` 12,4 m, `T51` 13,7 m - jedyny wolny początek, tor dosyłający do S44), 3 węzły (S36 scalenie, S40 rozdzielenie, S44 scalenie), obejście S40 -> C41 -> S44 -> T45 -> S36 jest drugą pętlą (36 m) dzielącą `T37` z pętlą główną (518,6 m).
- Ruch: `Movement/TrolleyTraffic.cs` (czysta logika, bez Unity). Wózek ma `Edge` + `SimulationPositionMeters` (metry na krawędzi) + `Trail` (poprzednie krawędzie, do tylnego koła). Lider = najbliższy wózek na aktywnej trasie (zwrotnice wg stanu); stop-linie: scalenie bez pierwszeństwa, koniec toru; reguła "nie blokuj skrzyżowania"; pierwszeństwo na scaleniu per kolumna (max 4 wózki, potem przekazanie rywalowi; także gdy czoło stoi); kontrola pojemności pętli (wjazd tylko, gdy w pętli jest miejsce na ciało + odstęp + 2 m rezerwy); zamek zwrotnicy, gdy wózek jest na niej (zmiana czeka). `TrolleySimulationManager`: `Network`, `Traffic`, `GetSwitches()`, `SetSwitchState(nazwa, 0|1)` (0 = Q0 odgałęzienie, 1 = Q1 prosto), `AddTrolley(typ, TrackLocation)`, `MoveTrolleyToLocation`, `GetSuggestedTrolleyLocation`; stare metody z jedną liczbą działają na trasie głównej / bieżącej krawędzi. `TrolleySuspension.UpdateSuspension(edge, metry, trail)` próbkuje przednie koło wzdłuż aktywnych zwrotnic, tylne wzdłuż przebytych krawędzi.
- HMI: okno `TRACK FILE` ma sekcję SWITCHES (przyciski wyjść, zielony = aktywne, pomarańczowy = oczekuje, LOCKED = wózek na zwrotnicy). Pozycje wózków w panelu: `krawędź:mm` (np. `T37:500`); na zwykłej pętli samo `mm`. W scenie etykiety zwrotnic/scaleń (`HMI/TrackSwitchIndicators.cs`). Kamera (`TrolleyCameraController.Start`) po wczytaniu pliku ustawia się na obwiednię toru.
- UDP: telegram wersja 2, 1692 B: nagłówek 28 B (jak v1 + `activeSwitches u16`, `maxSwitches u16`), rekord 16 B (`id u16, typ u8, stan napędu u8, krawędź u16, rezerwa u16, pozycja mm u32 NA KRAWĘDZI, prędkość i32`), po 100 rekordach 16 wpisów zwrotnic po 4 B (`id u16` z cyfr nazwy, `stan u8` 0=Q0 1=Q1, `flagi u8` bit0 zablokowana, bit1 zmiana oczekuje). Numer krawędzi = jej indeks (0 = trasa główna). `track length` w nagłówku = suma długości wszystkich krawędzi.
- Walidacja bez Unity (harness w katalogu sesji, nie w repo; te same pliki źródłowe + atrapy Unity): graf z `Szyna_poprawiona.xlsm`, symulacja z przełączaniem S40 co 30 s przez 12 min: 3-26 wózków (+ 2 na odgałęzieniach) bez kolizji brył i bez zakleszczeń; pętla zwykła (3 wózki) bez kolizji. GRANICA: przy ok. 30+ wózkach i długim utrzymaniu S40 na Q0 przepełnia się krótka pętla obejścia (36 m) i układ potrafi stanąć (zakleszczenie cykliczne przez dwa scalenia); przy 60 wózkach raz zaobserwowano zbliżenie brył w strefie scalenia. To nie jest sterowanie trasami per wózek: stan zwrotnicy dotyczy wszystkich wózków.
- Ręczne przestawianie WSZYSTKICH zwrotnic (UDP na razie bez zmian: wysyła tylko stan rozdzielenia S40): rozdzielenie (`…Out`) - wybór wyjścia Q0/Q1; scalenie (`…In`) - `AUTO` albo `only from <krawędź>` (`TrackNode.RequestInIndex(-1|i)`, `ManualInIndex`, API menedżera `SetSwitchState(nazwa, stan)`): wymuszony wlot ma stałe pierwszeństwo, pozostałe wloty czekają przed strefą scalenia, także gdy z wymuszonego nikt nie jedzie (wróć na AUTO). Zmiana czeka, aż żaden wózek nie jest w strefie ani nie zdąży już przed nią stanąć (`IsCommitted`). Panel: okno TRACK FILE -> SWITCHES, trzy wiersze (S36, S40, S44). Symulacja bez Unity z cyklicznym przełączaniem wszystkich trzech (6/12/20 wózków, 10 min): 0 kolizji; stany wymuszone bez ruchu z wlotu blokują drugi wlot (zamierzone).
- Pozycja = barcode (poprawka): w panelu jest TYLKO wartość barcode, nie ma już `krawędź:mm`. Kolumny `POSITION (barcode)` (na żywo), `MOVE TO (barcode)` i pole ADD to ta sama wartość: barcode pod czytnikiem wózka (środek + `BarcodeReaderMountingPositionMeters.z`). `MOVE`/`ADD` ustawiają wózek tak, by jego czytnik odczytał podaną wartość (`TrolleySimulationManager.TryResolveCenterFromBarcode`, `GetBarcode`, `GetBarcodeForCenter`; `TrackNetwork.TryFindBarcode`, `GetBarcode(TrackLocation)`). Nieistniejący barcode = komunikat błędu. Tor bez pasków (zwykła pętla ze sceny / prosty format) używa jako barcode odległości w mm po pętli. Wewnętrznie wózek nadal ma krawędź + metry (symulacja), ale nigdzie tego nie pokazujemy. Test bez Unity: barcode -> miejsce -> barcode bez rozbieżności (205 punktów na `Szyna_poprawiona.xlsm`, 41 na pętli bez pasków). UDP nadal wysyła krawędź + mm (świadomie nieruszane); do zmiany na barcode przy pracy nad UDP.
- Barcode (kody paskowe z kolumn `CodeStripStart01/Stop01` = DI/DJ i `…02` = DK/DL): wartości w mm, rosnące zgodnie z kierunkiem jazdy, interpolowane liniowo po elemencie. Zwrotnice mają dwa paski: 01 = łuk (Q0 w Out, I0 w In), 02 = prosta (Q1 w Out, I1 w In). `TrackEdge.GetBarcode(metry)`, `TrolleySimulationManager.GetBarcode(wózek)` (czytnik = środek wózka + `BarcodeReaderMountingPositionMeters.z` wzdłuż toru; -1 = brak paska), kolumna paska w panelu zastąpiona jedną kolumną POSITION (barcode). Weryfikacja na `Szyna_poprawiona.xlsm`: początek/koniec każdej krawędzi zgadza się z arkuszem. Skok wartości na łączeniach elementów (np. `S40`/`T1` 525924 -> 10000) wynika z danych. Uwaga: pasek 02 zwrotnicy S44 ma 1962 mm, choć jej prosta ma 1550 mm. UDP barcodu jeszcze nie niesie. Niezweryfikowane w Play.
- Numery wózków (`T-001`...) rysowane nad wózkami i poruszające się z nimi (`HMI/TrackSwitchIndicators.cs`, `DrawTrolleyNumbers`; pomarańczowy = stoi, czerwony = awaria). Zoom kamery: limity ortho size są teraz stałymi w kodzie (1,5-600 m); wcześniej scena miała zapisane `minimumOrthographicSize: 20`, które nadpisywało kod (to był powód, że nie dało się przybliżyć). Niezweryfikowane w Play. NIEZWERYFIKOWANE w Unity/Play: wygląd grafu (tor, podpory), etykiety, panel SWITCHES, ruch przez zwrotnice, testy EditMode (stare `TrolleyMovementTests` na pętli sceny + nowe `TrackFileReaderTests`, `PositionTelegramTests`), wysyłka UDP v2.
- Koniec dnia 2026-11-12: użytkownik potwierdził, że działa w Unity (wczytanie pliku, zwrotnice, numery wózków, zoom, pozycja jako barcode). Do zrobienia: UDP (pozycja jako barcode, stany scaleń S36/S44), sprawdzenie testów EditMode w Test Runnerze, sprawdzenie danych w arkuszu (T31/C30 nachodzą barcodem, pasek 02 S44 = 1962 mm), commit zmian (niezacommitowane).
