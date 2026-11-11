using System;
using System.Collections.Generic;
using System.Globalization;
using IndustrialConveyor.Simulation.Movement;
using UnityEngine;

namespace IndustrialConveyor.Simulation.HMI
{
    /// <summary>Renders a collapsible IMGUI diagnostics window using the verified working click path.</summary>
    public sealed class TrolleyClickProbe : MonoBehaviour
    {
        private const int WindowId = 638214;
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
            }
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
            Header("ID", 62f);
            Header("TYPE", 96f);
            Header("POSITION m", 112f);
            Header("", 60f);
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
            GUILayout.Label("POSITION m", statusStyle, GUILayout.Width(112f));
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
                addPositionInput = simulationManager.GetSuggestedTrolleyPositionMeters(trolleyType)
                    .ToString("F2", CultureInfo.InvariantCulture);
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
            GUILayout.Label(trolley.TrolleyId, labelStyle, GUILayout.Width(62f));
            GUILayout.Label(trolley.TrolleyType.DisplayName, labelStyle, GUILayout.Width(96f));
            if (!positionInputs.TryGetValue(trolley.TrolleyId, out var positionInput))
                positionInput = trolley.SimulationPositionMeters.ToString("F2", CultureInfo.InvariantCulture);
            positionInputs[trolley.TrolleyId] = GUILayout.TextField(positionInput, fieldStyle, GUILayout.Width(112f));
            if (GUILayout.Button("MOVE", buttonStyle, GUILayout.Width(60f)))
            {
                if (TryParseFloat(positionInputs[trolley.TrolleyId], out var position))
                {
                    try
                    {
                        simulationManager.MoveTrolleyToPosition(trolley.TrolleyId, position);
                        positionInputs[trolley.TrolleyId] = trolley.SimulationPositionMeters.ToString("F2", CultureInfo.InvariantCulture);
                        panelError = null;
                    }
                    catch (Exception exception)
                    {
                        panelError = exception.Message;
                    }
                }
                else
                {
                    panelError = "Enter a valid position in meters.";
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
            if (!TryParseFloat(addPositionInput, out var position))
            {
                panelError = "Enter a valid position in meters.";
                return;
            }

            try
            {
                var trolley = simulationManager.AddTrolley(trolleyType, position);
                positionInputs[trolley.TrolleyId] = trolley.SimulationPositionMeters.ToString("F2", CultureInfo.InvariantCulture);
                targetInputs[trolley.TrolleyId] = (trolley.TargetSpeedMetersPerSecond * 1000f)
                    .ToString("F0", CultureInfo.InvariantCulture);
                addPositionInput = simulationManager.GetSuggestedTrolleyPositionMeters(trolleyType)
                    .ToString("F2", CultureInfo.InvariantCulture);
                scrollPosition.y = float.MaxValue;
                panelMessage = $"Added {trolley.TrolleyId} ({trolley.TrolleyType.DisplayName}) at {trolley.SimulationPositionMeters:F2} m.";
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
            return $"{trolley.OperatingState}  |  Drive {trolley.DriveState}  |  Limit {trolley.TrolleyType.MaximumSpeedMetersPerSecond * 1000f:F0} mm/s{error}";
        }
    }
}
