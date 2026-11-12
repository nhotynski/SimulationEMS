using IndustrialConveyor.Simulation.Movement;
using IndustrialConveyor.Simulation.Track;
using IndustrialConveyor.Simulation.TrackGenerator;
using UnityEngine;

namespace IndustrialConveyor.Simulation.HMI
{
    /// <summary>Draws a screen-space label with the selected position of every split switch above the rail.</summary>
    [DisallowMultipleComponent]
    public sealed class TrackSwitchIndicators : MonoBehaviour
    {
        private const float LabelHeightAboveRailMeters = 8.6f;
        private const float TrolleyLabelHeightAboveRailMeters = 1.2f;

        private TrolleySimulationManager manager;
        private TrackVisualization visualization;
        private GUIStyle style;

        private void Awake()
        {
            manager = GetComponent<TrolleySimulationManager>();
            visualization = FindAnyObjectByType<TrackVisualization>();
        }

        private void OnGUI()
        {
            if (manager == null || manager.Network == null || Camera.main == null)
                return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = 12,
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter
                };
            }

            var previousColor = GUI.color;
            DrawTrolleyNumbers(previousColor);
            foreach (var node in manager.Network.Nodes)
            {
                var world = new Vector3(node.Position.x, LabelHeightAboveRailMeters, node.Position.z);
                if (visualization != null)
                    world = visualization.transform.TransformPoint(world);
                var screen = Camera.main.WorldToScreenPoint(world);
                if (screen.z < 0f)
                    continue;

                string text;
                Color color;
                if (node.Kind == TrackNodeKind.Split)
                {
                    var label = node.SelectedOutIndex < node.OutLabels.Count ? node.OutLabels[node.SelectedOutIndex] : "Q" + node.SelectedOutIndex;
                    text = $"{node.Name}  {label}{(node.IsLocked ? "  LOCK" : string.Empty)}";
                    color = node.SelectedOutIndex == 0 ? new Color(1f, 0.72f, 0.25f) : new Color(0.45f, 0.95f, 0.55f);
                }
                else
                {
                    var holder = manager.Traffic != null ? manager.Traffic.GetMergeRightOfWay(node) : null;
                    text = node.ManualInIndex >= 0
                        ? $"{node.Name}  only {node.InEdges[node.ManualInIndex].Name}"
                        : holder != null ? $"{node.Name}  auto: {holder.Name}" : $"{node.Name}  auto";
                    color = node.ManualInIndex >= 0 ? new Color(1f, 0.85f, 0.35f) : new Color(0.5f, 0.8f, 1f);
                }

                var rect = new Rect(screen.x - 90f, Screen.height - screen.y - 10f, 180f, 20f);
                GUI.color = new Color(0f, 0f, 0f, 0.65f);
                GUI.Box(rect, GUIContent.none);
                GUI.color = color;
                GUI.Label(rect, text, style);
            }
            GUI.color = previousColor;
        }

        private void DrawTrolleyNumbers(Color previousColor)
        {
            var camera = Camera.main;
            foreach (var trolley in manager.Trolleys)
            {
                var suspension = trolley.Suspension;
                var center = (suspension.FrontWheelPosition + suspension.RearWheelPosition) * 0.5f +
                             Vector3.up * TrolleyLabelHeightAboveRailMeters;
                var screen = camera.WorldToScreenPoint(center);
                if (screen.z < 0f || screen.x < -40f || screen.x > Screen.width + 40f ||
                    screen.y < -40f || screen.y > Screen.height + 40f)
                    continue;

                var rect = new Rect(screen.x - 28f, Screen.height - screen.y - 9f, 56f, 18f);
                GUI.color = new Color(0f, 0f, 0f, 0.7f);
                GUI.Box(rect, GUIContent.none);
                GUI.color = trolley.DriveState == TrolleyDriveState.Faulted ? new Color(1f, 0.4f, 0.35f)
                    : trolley.ActualSpeedMetersPerSecond < 0.01f ? new Color(1f, 0.78f, 0.3f)
                    : Color.white;
                GUI.Label(rect, trolley.TrolleyId, style);
            }
            GUI.color = previousColor;
        }
    }
}
