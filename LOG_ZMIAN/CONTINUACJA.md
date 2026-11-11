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
