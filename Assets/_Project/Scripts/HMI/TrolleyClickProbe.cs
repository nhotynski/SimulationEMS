using System;
using System.Collections.Generic;
using System.Globalization;
using IndustrialConveyor.Simulation.Communication;
using IndustrialConveyor.Simulation.Movement;
using IndustrialConveyor.Simulation.Track;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IndustrialConveyor.Simulation.HMI
{
    /// <summary>Renders a collapsible IMGUI diagnostics window using the verified working click path.</summary>
    public sealed class TrolleyClickProbe : MonoBehaviour
    {
        private const int WindowId = 638214;
        private const int UdpWindowId = 638215;
        private const int TrackWindowId = 638216;
        private readonly Dictionary<string, string> targetInputs = new Dictionary<string, string>();
        private readonly Dictionary<string, string> positionInputs = new Dictionary<string, string>();

        private TrolleySimulationManager simulationManager;
        private int clickCount;
        private bool expanded;
        private int selectedTypeIndex;
        private string addPositionInput;
        private string panelError;
        private string panelMessage;
        private Vector2 scrollPosition;
        private Rect windowRect;
        private UdpPositionSender udpSender;
        private UdpSettings udpSettings;
        private List<NetworkAdapterInfo> udpAdapters;
        private bool udpExpanded;
        private Rect udpWindowRect;
        private Vector2 udpAdapterScroll;
        private string udpLocalIpInput;
        private string udpDestinationIpInput;
        private string udpPortInput;
        private string udpIntervalInput;
        private string udpError;
        private bool trackExpanded;
        private Rect trackWindowRect;
        private string trackPathInput;
        private string trackError;
        private List<TrackNode> switchNodes;
        private GUIStyle windowStyle;
        private GUIStyle rowStyle;
        private GUIStyle headingStyle;
        private GUIStyle labelStyle;
        private GUIStyle statusStyle;
        private GUIStyle errorStyle;
        private GUIStyle successStyle;
        private GUIStyle buttonStyle;
        private GUIStyle fieldStyle;
        private readonly List<Texture2D> styleTextures = new List<Texture2D>();

        /// <summary>Gets the number of diagnostic open/close clicks in the current play session.</summary>
        public int ClickCount => clickCount;

        private void Awake()
        {
            simulationManager = GetComponent<TrolleySimulationManager>();
            if (simulationManager == null)
            {
                Debug.LogError($"{nameof(TrolleyClickProbe)} requires {nameof(TrolleySimulationManager)} on the same GameObject.", this);
                enabled = false;
                return;
            }

            udpSender = GetComponent<UdpPositionSender>();
            if (GetComponent<TrackSwitchIndicators>() == null)
                gameObject.AddComponent<TrackSwitchIndicators>();
            if (udpSender == null)
                udpSender = gameObject.AddComponent<UdpPositionSender>();
            udpSettings = UdpSettings.Load();
            udpLocalIpInput = udpSettings.LocalIp;
            udpDestinationIpInput = udpSettings.DestinationIp;
            udpPortInput = udpSettings.DestinationPort.ToString(CultureInfo.InvariantCulture);
            udpIntervalInput = udpSettings.IntervalMs.ToString(CultureInfo.InvariantCulture);
            udpAdapters = NetworkAdapters.ListIPv4();
        }

        private void OnGUI()
        {
            if (simulationManager == null)
                return;
            EnsureStyles();

            var topRightToggle = new Rect(Screen.width - 198f, 12f, 186f, 36f);
            var previousColor = GUI.color;
            GUI.color = new Color(0.08f, 0.72f, 0.76f, 1f);
            var topRightClicked = GUI.Button(topRightToggle, expanded ? "DIAGNOSTICS  [ - ]" : "DIAGNOSTICS  [ + ]", buttonStyle);
            GUI.color = previousColor;

            if (topRightClicked)
                ToggleDiagnostics();

            GUI.color = new Color(0.08f, 0.72f, 0.76f, 1f);
            var udpClicked = GUI.Button(new Rect(Screen.width - 396f, 12f, 186f, 36f),
                udpExpanded ? "UDP CONFIG  [ - ]" : "UDP CONFIG  [ + ]", buttonStyle);
            GUI.color = previousColor;
            if (udpClicked)
                udpExpanded = !udpExpanded;

            GUI.color = new Color(0.08f, 0.72f, 0.76f, 1f);
            var trackClicked = GUI.Button(new Rect(Screen.width - 594f, 12f, 186f, 36f),
                trackExpanded ? "TRACK FILE  [ - ]" : "TRACK FILE  [ + ]", buttonStyle);
            GUI.color = previousColor;
            if (trackClicked)
            {
                trackExpanded = !trackExpanded;
                if (trackPathInput == null)
                    trackPathInput = TrackSource.SavedFilePath;
            }
            if (trackExpanded)
            {
                if (trackWindowRect.width <= 0f)
                    trackWindowRect = new Rect(12f, 56f, Mathf.Min(680f, Screen.width - 24f),
                        Mathf.Min(430f, Screen.height - 64f));
                trackWindowRect = GUI.Window(TrackWindowId, trackWindowRect, DrawTrackWindow, GUIContent.none, windowStyle);
            }
            if (udpExpanded)
            {
                if (udpWindowRect.width <= 0f)
                    udpWindowRect = new Rect(12f, 56f, Mathf.Min(560f, Screen.width - 24f),
                        Mathf.Min(470f, Screen.height - 64f));
                udpWindowRect = GUI.Window(UdpWindowId, udpWindowRect, DrawUdpWindow, GUIContent.none, windowStyle);
            }

            if (!expanded)
                return;

            var width = Mathf.Min(900f, Screen.width - 24f);
            var height = Mathf.Min(700f, Screen.height - 64f);
            windowRect = new Rect(Mathf.Max(12f, Screen.width - width - 12f), 56f, width, height);
            windowRect = GUI.Window(WindowId, windowRect, DrawDiagnosticsWindow, GUIContent.none, windowStyle);
        }

        private void OnDestroy()
        {
            foreach (var texture in styleTextures)
            {
                if (texture == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(texture);
                else
                    DestroyImmediate(texture);
            }
        }

        private void DrawDiagnosticsWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("TROLLEY DIAGNOSTICS", headingStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("COLLAPSE", buttonStyle, GUILayout.Width(82f), GUILayout.Height(28f)))
                expanded = false;
            GUILayout.EndHorizontal();

            GUILayout.Label($"SIM {simulationManager.SimulationTimeSeconds:F1} s  |  " +
                            $"{simulationManager.Trolleys.Count} drives  |  " +
                            (simulationManager.IsPaused ? "PAUSED" : "RUNNING"), statusStyle);
            GUILayout.Space(6f);

            DrawAddControls();
            if (!string.IsNullOrEmpty(panelError))
                GUILayout.Label(panelError, errorStyle);
            else if (!string.IsNullOrEmpty(panelMessage))
                GUILayout.Label(panelMessage, successStyle);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            Header("ID", 50f);
            Header("TYPE", 90f);
            Header("POSITION (barcode)", 130f);
            Header("MOVE TO (barcode)", 130f);
            Header("", 56f);
            Header("ACTUAL mm/s", 82f);
            Header("TARGET mm/s", 90f);
            Header("", 44f);
            Header("", 46f);
            GUILayout.EndHorizontal();

            var scrollHeight = Mathf.Max(60f, windowRect.height - 176f);
            scrollPosition = GUILayout.BeginScrollView(scrollPosition, GUILayout.Height(scrollHeight));
            for (var index = 0; index < simulationManager.Trolleys.Count; index++)
            {
                if (DrawTrolleyRow(simulationManager.Trolleys[index]))
                    index--;
            }
            GUILayout.EndScrollView();

            GUI.DragWindow(new Rect(0f, 0f, windowRect.width - 100f, 24f));
        }

        private void DrawUdpWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("UDP CONFIG", headingStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("COLLAPSE", buttonStyle, GUILayout.Width(82f), GUILayout.Height(28f)))
                udpExpanded = false;
            GUILayout.EndHorizontal();

            GUILayout.Label(udpSender.IsRunning
                ? $"RUNNING  |  {udpSender.PacketsSent} packets sent"
                : "STOPPED", statusStyle);
            if (!string.IsNullOrEmpty(udpSender.LastError))
                GUILayout.Label("Send error: " + udpSender.LastError, errorStyle);
            if (!string.IsNullOrEmpty(udpError))
                GUILayout.Label(udpError, errorStyle);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            GUILayout.Label("NETWORK ADAPTER", headingStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("REFRESH", buttonStyle, GUILayout.Width(82f), GUILayout.Height(24f)))
                udpAdapters = NetworkAdapters.ListIPv4();
            GUILayout.EndHorizontal();
            udpAdapterScroll = GUILayout.BeginScrollView(udpAdapterScroll, GUILayout.Height(110f));
            foreach (var adapter in udpAdapters)
            {
                if (GUILayout.Button(adapter.Label, buttonStyle, GUILayout.Height(24f)))
                    udpLocalIpInput = adapter.Address;
            }
            GUILayout.EndScrollView();
            GUILayout.Space(6f);

            DrawUdpField("SIMULATION IP", ref udpLocalIpInput);
            DrawUdpField("DESTINATION IP", ref udpDestinationIpInput);
            DrawUdpField("DESTINATION PORT", ref udpPortInput);
            DrawUdpField($"INTERVAL ms ({UdpPositionSender.MinimumIntervalMs}-{UdpPositionSender.MaximumIntervalMs})",
                ref udpIntervalInput);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(udpSender.IsRunning ? "RESTART" : "START", buttonStyle, GUILayout.Width(110f), GUILayout.Height(30f)))
                StartUdp();
            if (GUILayout.Button("STOP", buttonStyle, GUILayout.Width(110f), GUILayout.Height(30f)))
            {
                udpSender.Stop();
                udpError = null;
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);

            GUILayout.Label($"Telegram: {PositionTelegram.TotalSize} B = {PositionTelegram.HeaderSize} B header + " +
                            $"{PositionTelegram.MaximumRecords} x {PositionTelegram.RecordSize} B records (little-endian).", statusStyle);
            GUILayout.Label("Record: id u16, type u8, drive u8, position mm u32, speed mm/s i32.", statusStyle);

            GUI.DragWindow(new Rect(0f, 0f, udpWindowRect.width - 100f, 24f));
        }

        private void DrawTrackWindow(int id)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("TRACK FILE", headingStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("COLLAPSE", buttonStyle, GUILayout.Width(82f), GUILayout.Height(28f)))
                trackExpanded = false;
            GUILayout.EndHorizontal();

            var length = simulationManager.Network != null ? simulationManager.Network.TotalLengthMeters : 0f;
            GUILayout.Label(string.IsNullOrEmpty(TrackSource.LoadedFilePath)
                ? $"ACTIVE TRACK: scene asset  |  {length:F1} m"
                : $"ACTIVE TRACK: {TrackSource.LoadedFilePath}  |  {length:F1} m", statusStyle);
            var error = trackError ?? TrackSource.LastError;
            if (!string.IsNullOrEmpty(error))
                GUILayout.Label(error, errorStyle);
            GUILayout.Space(6f);

            GUILayout.Label("WORKBOOK (.xlsm / .xlsx) - sheets 'Parameters' and 'Segments'", statusStyle);
            trackPathInput = GUILayout.TextField(trackPathInput ?? string.Empty, fieldStyle);
            GUILayout.Space(6f);

            GUILayout.BeginHorizontal();
            if (NativeFileDialog.IsSupported &&
                GUILayout.Button("BROWSE...", buttonStyle, GUILayout.Width(110f), GUILayout.Height(30f)))
            {
                var picked = NativeFileDialog.PickTrackFile(trackPathInput);
                if (!string.IsNullOrEmpty(picked))
                    trackPathInput = picked;
            }
            if (GUILayout.Button("LOAD TRACK", buttonStyle, GUILayout.Width(130f), GUILayout.Height(30f)))
                ApplyTrackFile(trackPathInput);
            if (GUILayout.Button("USE SCENE TRACK", buttonStyle, GUILayout.Width(160f), GUILayout.Height(30f)))
            {
                TrackSource.ClearSelection();
                trackError = null;
                ReloadScene();
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);
            GUILayout.Label("Loading restarts the simulation on the new track (trolleys return to their scene positions; " +
                            "restart UDP afterwards). The file is remembered for the next start.", statusStyle);

            DrawSwitchControls();

            GUI.DragWindow(new Rect(0f, 0f, trackWindowRect.width - 100f, 24f));
        }

        private void DrawSwitchControls()
        {
            if (switchNodes == null)
                switchNodes = new List<TrackNode>(simulationManager.Network.Nodes);
            if (switchNodes.Count == 0)
                return;

            GUILayout.Space(8f);
            GUILayout.Label("SWITCHES", headingStyle);
            GUILayout.Label("Split: choose the exit. Merge: AUTO, or hold one input open (the other waits). A change waits until the switch is free.",
                statusStyle);
            var previousColor = GUI.color;
            foreach (var node in switchNodes)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(node.Name, labelStyle, GUILayout.Width(56f));
                if (node.Kind == TrackNodeKind.Split)
                {
                    for (var index = 0; index < node.OutEdges.Count; index++)
                    {
                        var selected = node.SelectedOutIndex == index;
                        var pending = node.RequestedOutIndex == index;
                        var label = node.OutLabels.Count > index ? node.OutLabels[index] : "Q" + index;
                        if (SwitchButton($"{label} > {node.OutEdges[index].Name}", selected, pending))
                            ApplySwitch(node, index);
                    }
                    GUI.color = previousColor;
                    GUILayout.Label(node.IsLocked ? "LOCKED" : node.RequestedOutIndex >= 0 ? "pending" : string.Empty, statusStyle);
                }
                else
                {
                    if (SwitchButton("AUTO", node.ManualInIndex < 0, node.RequestedInIndex == -1))
                        ApplySwitch(node, -1);
                    for (var index = 0; index < node.InEdges.Count; index++)
                    {
                        if (SwitchButton($"only from {node.InEdges[index].Name}", node.ManualInIndex == index,
                                node.RequestedInIndex == index))
                            ApplySwitch(node, index);
                    }
                    GUI.color = previousColor;
                    GUILayout.Label(node.HasPendingInput ? "pending" : string.Empty, statusStyle);
                }
                GUILayout.EndHorizontal();
            }
            GUI.color = previousColor;
        }

        private bool SwitchButton(string label, bool selected, bool pending)
        {
            var previous = GUI.color;
            GUI.color = selected ? new Color(0.45f, 0.95f, 0.55f) : pending ? new Color(1f, 0.78f, 0.3f) : Color.white;
            var clicked = GUILayout.Button(label, buttonStyle, GUILayout.Width(150f), GUILayout.Height(26f));
            GUI.color = previous;
            return clicked;
        }

        private void ApplySwitch(TrackNode node, int state)
        {
            try
            {
                simulationManager.SetSwitchState(node.Name, state);
                panelError = null;
            }
            catch (Exception exception)
            {
                panelError = exception.Message;
            }
        }

        private void ApplyTrackFile(string path)
        {
            if (!TrackSource.TrySelect(path, out var error))
            {
                trackError = error;
                return;
            }

            trackError = null;
            ReloadScene();
        }

        private void ReloadScene()
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex < 0)
            {
                trackError = "The active scene is not in Build Profiles, so it cannot be reloaded.";
                return;
            }
            SceneManager.LoadScene(scene.buildIndex);
        }

        private void DrawUdpField(string label, ref string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, statusStyle, GUILayout.Width(190f));
            value = GUILayout.TextField(value ?? string.Empty, fieldStyle, GUILayout.Width(220f));
            GUILayout.EndHorizontal();
        }

        private void StartUdp()
        {
            if (!int.TryParse(udpPortInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
            {
                udpError = "Port must be a whole number.";
                return;
            }
            if (!int.TryParse(udpIntervalInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var interval))
            {
                udpError = "Interval must be a whole number of milliseconds.";
                return;
            }

            var settings = new UdpSettings
            {
                LocalIp = udpLocalIpInput.Trim(),
                DestinationIp = udpDestinationIpInput.Trim(),
                DestinationPort = port,
                IntervalMs = interval
            };
            try
            {
                udpSender.Begin(settings);
                settings.Save();
                udpSettings = settings;
                udpError = null;
            }
            catch (Exception exception)
            {
                udpError = exception.Message;
            }
        }

        private void DrawAddControls()
        {
            var availableTypes = simulationManager.AvailableTrolleyTypes;
            GUILayout.BeginHorizontal();
            GUILayout.Label("ADD TROLLEY", headingStyle, GUILayout.Width(112f));
            if (availableTypes == null || availableTypes.Count == 0)
            {
                GUILayout.Label("No trolley types configured.", errorStyle);
                GUILayout.EndHorizontal();
                return;
            }

            selectedTypeIndex = Mathf.Clamp(selectedTypeIndex, 0, availableTypes.Count - 1);
            var previousTypeIndex = selectedTypeIndex;
            var typeNames = new string[availableTypes.Count];
            for (var index = 0; index < availableTypes.Count; index++)
                typeNames[index] = availableTypes[index] != null ? availableTypes[index].DisplayName : "Missing type";
            selectedTypeIndex = GUILayout.SelectionGrid(selectedTypeIndex, typeNames,
                Mathf.Min(availableTypes.Count, 3), buttonStyle, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("POSITION (barcode)", statusStyle, GUILayout.Width(112f));
            if ((string.IsNullOrEmpty(addPositionInput) || selectedTypeIndex != previousTypeIndex) &&
                availableTypes[selectedTypeIndex] != null)
                RefreshSuggestedPosition(availableTypes[selectedTypeIndex]);
            addPositionInput = GUILayout.TextField(addPositionInput ?? "0", fieldStyle, GUILayout.Width(112f));
            if (GUILayout.Button("ADD", buttonStyle, GUILayout.Width(70f), GUILayout.Height(28f)))
                AddSelectedTrolley(availableTypes[selectedTypeIndex]);
            GUILayout.EndHorizontal();
        }

        private void RefreshSuggestedPosition(IndustrialConveyor.Simulation.TrolleyTypes.TrolleyTypeDefinition trolleyType)
        {
            try
            {
                var location = simulationManager.GetSuggestedTrolleyLocation(trolleyType);
                var suggested = simulationManager.GetBarcodeForCenter(trolleyType, location);
                addPositionInput = suggested >= 0 ? suggested.ToString(CultureInfo.InvariantCulture) : string.Empty;
                panelError = null;
            }
            catch (Exception exception)
            {
                addPositionInput = "0";
                panelError = exception.Message;
            }
        }

        private bool DrawTrolleyRow(TrolleyData trolley)
        {
            GUILayout.BeginVertical(rowStyle);
            GUILayout.BeginHorizontal();
            GUILayout.Label(trolley.TrolleyId, labelStyle, GUILayout.Width(50f));
            GUILayout.Label(trolley.TrolleyType.DisplayName, labelStyle, GUILayout.Width(90f));
            GUILayout.Label(simulationManager.GetBarcode(trolley) >= 0
                    ? simulationManager.GetBarcode(trolley).ToString(CultureInfo.InvariantCulture) : "-",
                labelStyle, GUILayout.Width(130f));
            if (!positionInputs.TryGetValue(trolley.TrolleyId, out var positionInput))
            {
                var current = simulationManager.GetBarcode(trolley);
                positionInput = current >= 0 ? current.ToString(CultureInfo.InvariantCulture) : string.Empty;
            }
            positionInputs[trolley.TrolleyId] = GUILayout.TextField(positionInput, fieldStyle, GUILayout.Width(130f));
            if (GUILayout.Button("MOVE", buttonStyle, GUILayout.Width(56f)))
            {
                if (simulationManager.TryResolveCenterFromBarcode(trolley.TrolleyType, positionInputs[trolley.TrolleyId],
                        out var target, out var parseError))
                {
                    try
                    {
                        simulationManager.MoveTrolleyToLocation(trolley.TrolleyId, target);
                        panelError = null;
                    }
                    catch (Exception exception)
                    {
                        panelError = exception.Message;
                    }
                }
                else
                {
                    panelError = parseError;
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                return true;
            }
            GUILayout.Label($"{trolley.ActualSpeedMetersPerSecond * 1000f:F0}", labelStyle, GUILayout.Width(82f));

            if (!targetInputs.TryGetValue(trolley.TrolleyId, out var input))
                input = (trolley.TargetSpeedMetersPerSecond * 1000f).ToString("F0", CultureInfo.InvariantCulture);
            targetInputs[trolley.TrolleyId] = GUILayout.TextField(input, fieldStyle, GUILayout.Width(90f));

            if (GUILayout.Button("SET", buttonStyle, GUILayout.Width(44f)))
                ApplyTargetSpeed(trolley, targetInputs[trolley.TrolleyId]);
            if (GUILayout.Button("DEL", buttonStyle, GUILayout.Width(46f)))
            {
                try
                {
                    simulationManager.RemoveTrolley(trolley.TrolleyId);
                    targetInputs.Remove(trolley.TrolleyId);
                    positionInputs.Remove(trolley.TrolleyId);
                    panelError = null;
                }
                catch (Exception exception)
                {
                    panelError = exception.Message;
                }
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
                return true;
            }
            GUILayout.EndHorizontal();

            var stateColor = trolley.DriveState == TrolleyDriveState.Faulted
                ? new Color(1f, 0.4f, 0.35f)
                : trolley.DriveState == TrolleyDriveState.EmergencyStopped
                    ? new Color(1f, 0.68f, 0.25f)
                    : new Color(0.52f, 0.79f, 0.8f);
            var previousColor = GUI.color;
            GUI.color = stateColor;
            GUILayout.Label(GetStateText(trolley), statusStyle);
            GUI.color = previousColor;
            GUILayout.EndVertical();
            return false;
        }

        private void ApplyTargetSpeed(TrolleyData trolley, string input)
        {
            if (!float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out var speedMillimetersPerSecond) &&
                !float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out speedMillimetersPerSecond))
                return;

            simulationManager.SetTargetSpeed(trolley.TrolleyId, Mathf.Max(0f, speedMillimetersPerSecond) * 0.001f);
            targetInputs[trolley.TrolleyId] =
                (trolley.TargetSpeedMetersPerSecond * 1000f).ToString("F0", CultureInfo.InvariantCulture);
        }

        private void AddSelectedTrolley(IndustrialConveyor.Simulation.TrolleyTypes.TrolleyTypeDefinition trolleyType)
        {
            if (trolleyType == null)
            {
                panelError = "Select a valid trolley type.";
                return;
            }
            if (!simulationManager.TryResolveCenterFromBarcode(trolleyType, addPositionInput, out var center, out var parseError))
            {
                panelError = parseError;
                return;
            }

            try
            {
                var trolley = simulationManager.AddTrolley(trolleyType, center);
                var barcode = simulationManager.GetBarcode(trolley);
                var where = barcode >= 0 ? barcode.ToString(CultureInfo.InvariantCulture) : "-";
                positionInputs[trolley.TrolleyId] = where;
                targetInputs[trolley.TrolleyId] = (trolley.TargetSpeedMetersPerSecond * 1000f)
                    .ToString("F0", CultureInfo.InvariantCulture);
                RefreshSuggestedPosition(trolleyType);
                scrollPosition.y = float.MaxValue;
                panelMessage = $"Added {trolley.TrolleyId} ({trolley.TrolleyType.DisplayName}) at barcode {where}.";
                panelError = null;
                Debug.Log(panelMessage, this);
            }
            catch (Exception exception)
            {
                panelError = exception.Message;
                panelMessage = null;
                Debug.LogException(exception, this);
            }
        }

        private static bool TryParseFloat(string input, out float value)
        {
            return float.TryParse(input, NumberStyles.Float, CultureInfo.CurrentCulture, out value) ||
                   float.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private void ToggleDiagnostics()
        {
            expanded = !expanded;
            clickCount++;
            Debug.Log($"Diagnostics {(expanded ? "opened" : "collapsed")} by IMGUI click #{clickCount}.", this);
        }

        private void EnsureStyles()
        {
            if (windowStyle != null)
                return;

            windowStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(10, 10, 10, 10)
            };
            SetBackgrounds(windowStyle, CreateSolidTexture(new Color(0.035f, 0.055f, 0.065f, 1f)));
            rowStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 4, 4),
                margin = new RectOffset(0, 0, 2, 2)
            };
            SetBackgrounds(rowStyle, CreateSolidTexture(new Color(0.075f, 0.105f, 0.115f, 1f)));
            headingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            headingStyle.normal.textColor = new Color(0.36f, 0.9f, 0.9f);
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft
            };
            labelStyle.normal.textColor = Color.white;
            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft
            };
            statusStyle.normal.textColor = new Color(0.57f, 0.75f, 0.77f);
            errorStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                wordWrap = true
            };
            errorStyle.normal.textColor = new Color(1f, 0.48f, 0.4f);
            successStyle = new GUIStyle(errorStyle);
            successStyle.normal.textColor = new Color(0.45f, 0.88f, 0.62f);
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            SetBackgrounds(buttonStyle,
                CreateSolidTexture(new Color(0.08f, 0.28f, 0.3f, 1f)),
                CreateSolidTexture(new Color(0.1f, 0.42f, 0.44f, 1f)),
                CreateSolidTexture(new Color(0.05f, 0.56f, 0.58f, 1f)));
            buttonStyle.normal.textColor = Color.white;
            buttonStyle.hover.textColor = Color.white;
            buttonStyle.active.textColor = Color.white;
            fieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleRight
            };
            SetBackgrounds(fieldStyle, CreateSolidTexture(new Color(0.025f, 0.035f, 0.04f, 1f)));
            fieldStyle.normal.textColor = Color.white;
            fieldStyle.focused.textColor = Color.white;
        }

        private void Header(string title, float width)
        {
            GUILayout.Label(title, headingStyle, GUILayout.Width(width));
        }

        private Texture2D CreateSolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            styleTextures.Add(texture);
            return texture;
        }

        private static void SetBackgrounds(GUIStyle style, Texture2D normal, Texture2D hover = null, Texture2D active = null)
        {
            hover = hover != null ? hover : normal;
            active = active != null ? active : hover;
            style.normal.background = normal;
            style.hover.background = hover;
            style.active.background = active;
            style.focused.background = hover;
            style.onNormal.background = normal;
            style.onHover.background = hover;
            style.onActive.background = active;
            style.onFocused.background = hover;
        }

        private static string GetStateText(TrolleyData trolley)
        {
            var error = string.IsNullOrEmpty(trolley.ErrorState) ? string.Empty : $"  |  {trolley.ErrorState}";
            return $"{trolley.OperatingState}  |  Drive {trolley.DriveState}  |  Route {trolley.Edge.Name}  |  Limit {trolley.TrolleyType.MaximumSpeedMetersPerSecond * 1000f:F0} mm/s{error}";
        }
    }
}
