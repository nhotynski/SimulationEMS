using System;
using System.Collections.Generic;
using System.Globalization;
using IndustrialConveyor.Simulation.Movement;
using UnityEngine;

namespace IndustrialConveyor.Simulation.HMI
{
    /// <summary>Renders a collapsible IMGUI diagnostics window for trolley simulation data.</summary>
    [DisallowMultipleComponent]
    public sealed class TrolleyDiagnosticOverlay : MonoBehaviour
    {
        private const int WindowId = 728314;
        private readonly Dictionary<string, string> targetInputs = new Dictionary<string, string>();

        private TrolleySimulationManager simulationManager;
        private Vector2 scrollPosition;
        private Rect windowRect;
        private bool expanded;
        private GUIStyle windowStyle;
        private GUIStyle headingStyle;
        private GUIStyle bodyStyle;
        private GUIStyle statusStyle;
        private GUIStyle buttonStyle;
        private GUIStyle fieldStyle;

        /// <summary>Toggles the window from the centered, input-tested IMGUI control.</summary>
        public bool ToggleFromClickProbe()
        {
            expanded = !expanded;
            return expanded;
        }

        private void Awake()
        {
            simulationManager = GetComponent<TrolleySimulationManager>();
            if (simulationManager == null)
            {
                Debug.LogError($"{nameof(TrolleyDiagnosticOverlay)} requires {nameof(TrolleySimulationManager)} on the same GameObject.", this);
                enabled = false;
            }
        }

        private void OnGUI()
        {
            if (simulationManager == null)
                return;

            EnsureStyles();
            if (!expanded)
            {
                DrawTopRightToggle();
                return;
            }

            var width = Mathf.Min(620f, Screen.width - 24f);
            var height = Mathf.Min(610f, Screen.height - 24f);
            windowRect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            windowRect = GUI.Window(WindowId, windowRect, DrawWindow, GUIContent.none, windowStyle);
        }

        private void DrawTopRightToggle()
        {
            const float width = 178f;
            const float height = 38f;
            var rectangle = new Rect(Screen.width - width - 12f, 12f, width, height);
            var previousColor = GUI.color;
            GUI.color = new Color(0.08f, 0.72f, 0.76f, 1f);
            if (GUI.Button(rectangle, "DIAGNOSTICS  [ + ]", buttonStyle))
                expanded = true;
            GUI.color = previousColor;
        }

        private void DrawWindow(int id)
        {
            var headerHeight = 32f;
            GUI.Label(new Rect(12f, 8f, windowRect.width - 110f, headerHeight),
                "TROLLEY DIAGNOSTICS", headingStyle);
            if (GUI.Button(new Rect(windowRect.width - 96f, 8f, 84f, 28f), "COLLAPSE", buttonStyle))
            {
                expanded = false;
                return;
            }

            var status = $"SIM {simulationManager.SimulationTimeSeconds:F1} s  |  " +
                         $"{simulationManager.Trolleys.Count} drives  |  " +
                         (simulationManager.IsPaused ? "PAUSED" : "RUNNING");
            GUI.Label(new Rect(12f, 38f, windowRect.width - 24f, 24f), status, statusStyle);

            const float left = 12f;
            var availableWidth = windowRect.width - 40f;
            const float idWidth = 66f;
            const float typeWidth = 88f;
            const float positionWidth = 94f;
            const float actualWidth = 92f;
            const float applyWidth = 62f;
            var targetWidth = Mathf.Max(92f, availableWidth - idWidth - typeWidth - positionWidth - actualWidth - applyWidth);

            var headingsY = 66f;
            var x = left;
            DrawHeaderCell(ref x, headingsY, "ID", idWidth);
            DrawHeaderCell(ref x, headingsY, "TYPE", typeWidth);
            DrawHeaderCell(ref x, headingsY, "POSITION m", positionWidth);
            DrawHeaderCell(ref x, headingsY, "ACTUAL mm/s", actualWidth);
            DrawHeaderCell(ref x, headingsY, "TARGET mm/s", targetWidth);
            DrawHeaderCell(ref x, headingsY, "", applyWidth);

            var viewport = new Rect(left, 88f, availableWidth, windowRect.height - 100f);
            var contentHeight = Mathf.Max(1f, simulationManager.Trolleys.Count * 64f);
            var contentRect = new Rect(0f, 0f, availableWidth - 16f, contentHeight);
            scrollPosition = GUI.BeginScrollView(viewport, scrollPosition, contentRect);
            for (var index = 0; index < simulationManager.Trolleys.Count; index++)
                DrawTrolleyRow(simulationManager.Trolleys[index], index, idWidth, typeWidth,
                    positionWidth, actualWidth, targetWidth, applyWidth, availableWidth);
            GUI.EndScrollView();

            GUI.DragWindow(new Rect(0f, 0f, windowRect.width - 112f, headerHeight));
        }

        private void DrawTrolleyRow(TrolleyData trolley, int index, float idWidth, float typeWidth,
            float positionWidth, float actualWidth, float targetWidth, float applyWidth, float rowWidth)
        {
            var rowY = index * 64f;
            var rowRect = new Rect(0f, rowY, rowWidth - 16f, 58f);
            GUI.Box(rowRect, GUIContent.none, windowStyle);

            var x = 4f;
            GUI.Label(new Rect(x, rowY + 5f, idWidth - 4f, 24f), trolley.TrolleyId, headingStyle);
            x += idWidth;
            GUI.Label(new Rect(x, rowY + 5f, typeWidth - 4f, 24f), trolley.TrolleyType.DisplayName, bodyStyle);
            x += typeWidth;
            GUI.Label(new Rect(x, rowY + 5f, positionWidth - 4f, 24f),
                $"{trolley.SimulationPositionMeters:F2}", bodyStyle);
            x += positionWidth;
            GUI.Label(new Rect(x, rowY + 5f, actualWidth - 4f, 24f),
                $"{trolley.ActualSpeedMetersPerSecond * 1000f:F0}", bodyStyle);
            x += actualWidth;

            if (!targetInputs.TryGetValue(trolley.TrolleyId, out var input))
                input = (trolley.TargetSpeedMetersPerSecond * 1000f).ToString("F0", CultureInfo.InvariantCulture);
            var inputRect = new Rect(x, rowY + 4f, targetWidth - 4f, 28f);
            var oldColor = GUI.color;
            GUI.color = new Color(0.12f, 0.2f, 0.22f, 1f);
            targetInputs[trolley.TrolleyId] = GUI.TextField(inputRect, input, fieldStyle);
            GUI.color = oldColor;
            x += targetWidth;

            if (GUI.Button(new Rect(x, rowY + 4f, applyWidth - 4f, 28f), "SET", buttonStyle))
                ApplyTargetSpeed(trolley, targetInputs[trolley.TrolleyId]);

            var stateColor = trolley.DriveState == TrolleyDriveState.Faulted
                ? new Color(1f, 0.4f, 0.35f)
                : trolley.DriveState == TrolleyDriveState.EmergencyStopped
                    ? new Color(1f, 0.68f, 0.25f)
                    : new Color(0.52f, 0.79f, 0.8f);
            GUI.color = stateColor;
            GUI.Label(new Rect(4f, rowY + 31f, rowWidth - 24f, 20f), GetStateText(trolley), statusStyle);
            GUI.color = oldColor;
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

        private void EnsureStyles()
        {
            if (windowStyle != null)
                return;

            windowStyle = new GUIStyle(GUI.skin.window)
            {
                padding = new RectOffset(10, 10, 10, 10)
            };
            headingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            headingStyle.normal.textColor = new Color(0.36f, 0.9f, 0.9f);
            bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.MiddleLeft
            };
            bodyStyle.normal.textColor = Color.white;
            statusStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                alignment = TextAnchor.MiddleLeft
            };
            statusStyle.normal.textColor = new Color(0.57f, 0.75f, 0.77f);
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            buttonStyle.normal.textColor = Color.white;
            fieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleRight
            };
            fieldStyle.normal.textColor = Color.white;
        }

        private static void DrawHeaderCell(ref float x, float y, string text, float width)
        {
            if (!string.IsNullOrEmpty(text))
                GUI.Label(new Rect(x, y, width, 18f), text, GUI.skin.label);
            x += width;
        }

        private static string GetStateText(TrolleyData trolley)
        {
            var error = string.IsNullOrEmpty(trolley.ErrorState) ? string.Empty : $"  |  {trolley.ErrorState}";
            return $"{trolley.OperatingState}  |  Drive {trolley.DriveState}  |  Limit {trolley.TrolleyType.MaximumSpeedMetersPerSecond * 1000f:F0} mm/s{error}";
        }
    }
}