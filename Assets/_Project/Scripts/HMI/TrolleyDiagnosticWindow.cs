using System;
using System.Collections.Generic;
using IndustrialConveyor.Simulation.Movement;
using UnityEngine;
using UnityEngine.UIElements;

namespace IndustrialConveyor.Simulation.HMI
{
    /// <summary>Provides a collapsible runtime diagnostic panel for all trolley drives.</summary>
    [DisallowMultipleComponent]
    public sealed class TrolleyDiagnosticWindow : MonoBehaviour
    {
        private readonly Dictionary<string, TrolleyRow> rows = new Dictionary<string, TrolleyRow>();

        private TrolleySimulationManager simulationManager;
        private PanelSettings panelSettings;
        private GameObject documentHost;
        private VisualElement windowPanel;
        private ScrollView trolleyRows;
        private VisualElement content;
        private Label simulationStatus;
        private Button toggleButton;
        private bool expanded;
        private bool centeredFallback;

        private void Awake()
        {
            simulationManager = GetComponent<TrolleySimulationManager>();
            if (simulationManager == null)
            {
                Debug.LogError($"{nameof(TrolleyDiagnosticWindow)} requires {nameof(TrolleySimulationManager)} on the same GameObject.", this);
                enabled = false;
                return;
            }

            CreateDocument();
            BuildInterface();
            RefreshRows();
            documentHost.GetComponent<UIDocument>().rootVisualElement.schedule.Execute(RefreshRows).Every(100);
        }

        private void OnDestroy()
        {
            if (documentHost != null)
            {
                if (Application.isPlaying)
                    Destroy(documentHost);
                else
                    DestroyImmediate(documentHost);
            }

            if (panelSettings != null)
            {
                if (Application.isPlaying)
                    Destroy(panelSettings);
                else
                    DestroyImmediate(panelSettings);
            }
        }

        private void CreateDocument()
        {
            documentHost = new GameObject("Trolley Diagnostics UI")
            {
                hideFlags = HideFlags.DontSave
            };
            documentHost.transform.SetParent(transform, false);
            documentHost.SetActive(false);

            panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
            panelSettings.name = "Runtime Trolley Diagnostics Panel";
            panelSettings.hideFlags = HideFlags.HideAndDontSave;

            var document = documentHost.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            documentHost.SetActive(true);
        }

        private void BuildInterface()
        {
            var root = documentHost.GetComponent<UIDocument>().rootVisualElement;

            windowPanel = new VisualElement();
            windowPanel.style.position = Position.Absolute;
            windowPanel.style.top = 12f;
            windowPanel.style.right = 12f;
            windowPanel.style.maxHeight = Length.Percent(82f);
            windowPanel.style.backgroundColor = new Color(0.055f, 0.075f, 0.085f, 0.96f);
            windowPanel.style.borderTopWidth = 1f;
            windowPanel.style.borderBottomWidth = 1f;
            windowPanel.style.borderLeftWidth = 1f;
            windowPanel.style.borderRightWidth = 1f;
            windowPanel.style.borderTopColor = new Color(0.2f, 0.72f, 0.78f);
            windowPanel.style.borderBottomColor = new Color(0.2f, 0.72f, 0.78f);
            windowPanel.style.borderLeftColor = new Color(0.2f, 0.72f, 0.78f);
            windowPanel.style.borderRightColor = new Color(0.2f, 0.72f, 0.78f);
            windowPanel.style.paddingLeft = 10f;
            windowPanel.style.paddingRight = 10f;
            windowPanel.style.paddingTop = 8f;
            windowPanel.style.paddingBottom = 8f;

            toggleButton = new Button(ToggleExpanded);
            toggleButton.style.height = 30f;
            toggleButton.style.backgroundColor = new Color(0.08f, 0.17f, 0.19f);
            toggleButton.style.color = new Color(0.38f, 0.9f, 0.9f);
            toggleButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            toggleButton.style.borderTopWidth = 0f;
            toggleButton.style.borderBottomWidth = 0f;
            toggleButton.style.borderLeftWidth = 0f;
            toggleButton.style.borderRightWidth = 0f;
            windowPanel.Add(toggleButton);

            content = new VisualElement();
            content.style.display = DisplayStyle.None;
            content.style.marginTop = 8f;
            windowPanel.Add(content);

            var title = new Label("TROLLEY DIAGNOSTICS");
            title.style.color = new Color(0.84f, 0.91f, 0.92f);
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 13f;
            content.Add(title);

            simulationStatus = new Label();
            simulationStatus.style.color = new Color(0.55f, 0.68f, 0.7f);
            simulationStatus.style.fontSize = 11f;
            simulationStatus.style.marginTop = 3f;
            simulationStatus.style.marginBottom = 8f;
            content.Add(simulationStatus);

            var headings = new VisualElement();
            headings.style.flexDirection = FlexDirection.Row;
            headings.style.paddingBottom = 4f;
            AddHeading(headings, "TROLLEY", 78f);
            AddHeading(headings, "POSITION m", 76f);
            AddHeading(headings, "ACTUAL mm/s", 70f);
            AddHeading(headings, "TARGET mm/s", 112f);
            content.Add(headings);

            trolleyRows = new ScrollView(ScrollViewMode.Vertical);
            trolleyRows.style.maxHeight = 320f;
            content.Add(trolleyRows);
            root.Add(windowPanel);

            UpdateExpandedState();
        }

        private void RefreshRows()
        {
            if (simulationManager == null || trolleyRows == null)
                return;

            var data = simulationManager.Trolleys;
            var rowsChanged = rows.Count != data.Count;
            if (!rowsChanged)
            {
                foreach (var trolley in data)
                {
                    if (!rows.ContainsKey(trolley.TrolleyId))
                    {
                        rowsChanged = true;
                        break;
                    }
                }
            }

            if (rowsChanged)
                RebuildRows(data);

            simulationStatus.text = $"SIM {simulationManager.SimulationTimeSeconds:F1} s  |  " +
                                    $"{data.Count} trolley drives  |  " +
                                    $"{(simulationManager.IsPaused ? "PAUSED" : "RUNNING")}";

            foreach (var trolley in data)
            {
                var row = rows[trolley.TrolleyId];
                row.Position.text = $"{trolley.SimulationPositionMeters:F2} m";
                row.ActualSpeed.text = $"{trolley.ActualSpeedMetersPerSecond * 1000f:F0}";
                row.State.text = GetStateLabel(trolley);
                if (!row.IsEditing)
                    row.TargetSpeed.SetValueWithoutNotify(trolley.TargetSpeedMetersPerSecond * 1000f);
            }
        }

        private void RebuildRows(IReadOnlyList<TrolleyData> data)
        {
            rows.Clear();
            trolleyRows.Clear();

            foreach (var trolley in data)
            {
                var rowRoot = new VisualElement();
                rowRoot.style.flexDirection = FlexDirection.Row;
                rowRoot.style.alignItems = Align.Center;
                rowRoot.style.minHeight = 40f;
                rowRoot.style.borderTopWidth = 1f;
                rowRoot.style.borderTopColor = new Color(0.18f, 0.26f, 0.28f);

                var identity = new VisualElement();
                identity.style.width = 78f;
                var idLabel = new Label(trolley.TrolleyId);
                idLabel.style.color = new Color(0.84f, 0.91f, 0.92f);
                idLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                var typeLabel = new Label(trolley.TrolleyType.DisplayName);
                typeLabel.style.color = new Color(0.52f, 0.67f, 0.69f);
                typeLabel.style.fontSize = 10f;
                identity.Add(idLabel);
                identity.Add(typeLabel);

                var position = CreateValueLabel(76f);
                var actualSpeed = CreateValueLabel(70f);
                var targetSpeed = new FloatField
                {
                    value = trolley.TargetSpeedMetersPerSecond * 1000f
                };
                targetSpeed.style.width = 112f;
                targetSpeed.style.marginRight = 4f;
                targetSpeed.style.color = Color.white;

                var stateLabel = new Label();
                stateLabel.style.position = Position.Absolute;
                stateLabel.style.right = 6f;
                stateLabel.style.bottom = 1f;
                stateLabel.style.fontSize = 9f;
                stateLabel.style.color = new Color(0.45f, 0.78f, 0.8f);
                var row = new TrolleyRow(position, actualSpeed, targetSpeed, stateLabel);
                targetSpeed.RegisterCallback<FocusInEvent>(_ => row.IsEditing = true);
                targetSpeed.RegisterCallback<FocusOutEvent>(_ => row.IsEditing = false);
                targetSpeed.RegisterValueChangedCallback(evt => SetTargetSpeed(trolley.TrolleyId, targetSpeed, evt.newValue));

                rowRoot.Add(identity);
                rowRoot.Add(position);
                rowRoot.Add(actualSpeed);
                rowRoot.Add(targetSpeed);
                rowRoot.Add(stateLabel);

                trolleyRows.Add(rowRoot);
                rows.Add(trolley.TrolleyId, row);
            }
        }

        private void SetTargetSpeed(string trolleyId, FloatField field, float valueMillimetersPerSecond)
        {
            if (float.IsNaN(valueMillimetersPerSecond) || float.IsInfinity(valueMillimetersPerSecond))
                return;

            simulationManager.SetTargetSpeed(trolleyId, Mathf.Max(0f, valueMillimetersPerSecond) * 0.001f);
            var trolley = simulationManager.FindTrolley(trolleyId);
            if (trolley != null)
                field.SetValueWithoutNotify(trolley.TargetSpeedMetersPerSecond * 1000f);
        }

        private void ToggleExpanded()
        {
            expanded = !expanded;
            centeredFallback = false;
            UpdateExpandedState();
        }

        /// <summary>Toggles the panel from the independent center-screen IMGUI click probe.</summary>
        public bool ToggleFromClickProbe()
        {
            expanded = !expanded;
            centeredFallback = expanded;
            UpdateExpandedState();
            return expanded;
        }

        private void UpdateExpandedState()
        {
            if (content == null || toggleButton == null)
                return;
            content.style.display = expanded ? DisplayStyle.Flex : DisplayStyle.None;
            windowPanel.style.width = expanded ? 380f : 176f;
            if (centeredFallback)
            {
                var root = documentHost.GetComponent<UIDocument>().rootVisualElement;
                windowPanel.style.left = Mathf.Max(12f, (root.resolvedStyle.width - 380f) * 0.5f);
                windowPanel.style.right = StyleKeyword.Null;
            }
            else
            {
                windowPanel.style.left = StyleKeyword.Null;
                windowPanel.style.right = 12f;
            }
            toggleButton.text = expanded ? "DIAGNOSTICS  [ - ]" : "DIAGNOSTICS  [ + ]";
        }

        private static void AddHeading(VisualElement parent, string text, float width)
        {
            var label = new Label(text);
            label.style.width = width;
            label.style.fontSize = 9f;
            label.style.color = new Color(0.43f, 0.62f, 0.64f);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            parent.Add(label);
        }

        private static Label CreateValueLabel(float width)
        {
            var label = new Label();
            label.style.width = width;
            label.style.color = Color.white;
            label.style.fontSize = 11f;
            return label;
        }

        private static string GetStateLabel(TrolleyData trolley)
        {
            if (!string.IsNullOrEmpty(trolley.ErrorState))
                return trolley.ErrorState;
            return trolley.OperatingState.ToString().ToUpperInvariant();
        }

        private sealed class TrolleyRow
        {
            public TrolleyRow(Label position, Label actualSpeed, FloatField targetSpeed, Label state)
            {
                Position = position;
                ActualSpeed = actualSpeed;
                TargetSpeed = targetSpeed;
                State = state;
            }

            public Label Position { get; }
            public Label ActualSpeed { get; }
            public FloatField TargetSpeed { get; }
            public Label State { get; }
            public bool IsEditing { get; set; }
        }
    }
}