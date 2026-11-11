using System.Collections.Generic;
using IndustrialConveyor.Simulation.Track;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace IndustrialConveyor.Editor
{
    [CustomEditor(typeof(TrackConfiguration))]
    public sealed class TrackConfigurationEditor : UnityEditor.Editor
    {
        private ReorderableList segmentList;

        private void OnEnable()
        {
            var segments = serializedObject.FindProperty("segments");
            segmentList = new ReorderableList(serializedObject, segments, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Track Segments"),
                elementHeightCallback = GetSegmentHeight,
                drawElementCallback = DrawSegment
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawProperty("startPosition", "Start Position (m)");
            DrawProperty("initialHeadingDegrees", "Initial Heading (degrees)");
            DrawProperty("targetLengthMeters", "Target Length (m)");
            DrawProperty("requireClosedLoop", "Require Closed Loop");
            DrawProperty("closurePositionToleranceMeters", "Position Tolerance (m)");
            DrawProperty("closureHeadingToleranceDegrees", "Heading Tolerance (degrees)");
            DrawProperty("targetLengthToleranceMeters", "Length Tolerance (m)");
            DrawProperty("intersectionSampleSpacingMeters", "Intersection Sample Spacing (m)");
            segmentList.DoLayoutList();
            serializedObject.ApplyModifiedProperties();

            var configuration = (TrackConfiguration)target;
            var result = TrackGeometryBuilder.Build(configuration);
            DrawReport(result.Report);
            DrawTopDownPreview(result.Path);
        }

        private void DrawProperty(string propertyName, string label)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(propertyName), new GUIContent(label));
        }

        private float GetSegmentHeight(int index)
        {
            var element = segmentList.serializedProperty.GetArrayElementAtIndex(index);
            var type = element.FindPropertyRelative("type").enumValueIndex;
            var lineCount = type == (int)TrackSegmentType.Curve ? 5 : 3;
            return lineCount * (EditorGUIUtility.singleLineHeight + 2f) + 4f;
        }

        private void DrawSegment(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element = segmentList.serializedProperty.GetArrayElementAtIndex(index);
            var y = rect.y + 2f;
            var line = EditorGUIUtility.singleLineHeight;
            DrawElementProperty(element, "label", "Segment", rect, ref y, line);
            DrawElementProperty(element, "type", "Type", rect, ref y, line);
            var type = element.FindPropertyRelative("type").enumValueIndex;
            if (type == (int)TrackSegmentType.Curve)
            {
                DrawElementProperty(element, "turnDirection", "Direction", rect, ref y, line);
                DrawElementProperty(element, "angleDegrees", "Angle (degrees)", rect, ref y, line);
                DrawElementProperty(element, "radiusMeters", "Radius (m)", rect, ref y, line);
            }
            else
            {
                DrawElementProperty(element, "straightLengthMeters", "Length (m)", rect, ref y, line);
            }
        }

        private static void DrawElementProperty(SerializedProperty parent, string propertyName, string label,
            Rect rect, ref float y, float height)
        {
            var property = parent.FindPropertyRelative(propertyName);
            EditorGUI.PropertyField(new Rect(rect.x, y, rect.width, height), property, new GUIContent(label));
            y += height + 2f;
        }

        private static void DrawReport(TrackValidationReport report)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Calculated Track", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Actual length", $"{report.ActualLengthMeters:F3} m");
            EditorGUILayout.LabelField("Target difference", $"{report.TargetLengthDifferenceMeters:+0.000;-0.000;0.000} m");
            if (report.IsClosed)
            {
                EditorGUILayout.LabelField("Closure position error", $"{report.ClosurePositionErrorMeters:F4} m");
                EditorGUILayout.LabelField("Closure heading error", $"{report.ClosureHeadingErrorDegrees:F4} deg");
            }

            foreach (var issue in report.Issues)
            {
                var messageType = issue.Severity == TrackIssueSeverity.Error ? MessageType.Error :
                    issue.Severity == TrackIssueSeverity.Warning ? MessageType.Warning : MessageType.Info;
                EditorGUILayout.HelpBox(issue.Message, messageType);
            }
        }

        private static void DrawTopDownPreview(TrackPath path)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Top-Down Track Preview", EditorStyles.boldLabel);
            var rect = GUILayoutUtility.GetRect(320f, 220f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, new Color(0.12f, 0.14f, 0.16f));
            if (path.Segments.Count == 0 || Event.current.type != EventType.Repaint)
                return;

            var sampleCount = Mathf.Clamp(Mathf.CeilToInt(path.TotalLengthMeters / 2f), 2, 600);
            var samples = new List<Vector3>(sampleCount + 1);
            var minX = float.PositiveInfinity;
            var maxX = float.NegativeInfinity;
            var minZ = float.PositiveInfinity;
            var maxZ = float.NegativeInfinity;
            for (var i = 0; i <= sampleCount; i++)
            {
                var sample = path.SampleAtDistance(path.TotalLengthMeters * i / sampleCount);
                samples.Add(sample.Position);
                minX = Mathf.Min(minX, sample.Position.x);
                maxX = Mathf.Max(maxX, sample.Position.x);
                minZ = Mathf.Min(minZ, sample.Position.z);
                maxZ = Mathf.Max(maxZ, sample.Position.z);
            }

            var bounds = new Rect(minX, minZ, Mathf.Max(0.01f, maxX - minX), Mathf.Max(0.01f, maxZ - minZ));
            var scale = Mathf.Min((rect.width - 30f) / bounds.width, (rect.height - 30f) / bounds.height);
            var center = new Vector2((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
            var points = new Vector3[samples.Count];
            for (var i = 0; i < samples.Count; i++)
            {
                var x = rect.center.x + (samples[i].x - center.x) * scale;
                var y = rect.center.y - (samples[i].z - center.y) * scale;
                points[i] = new Vector3(x, y, 0f);
            }

            Handles.BeginGUI();
            Handles.color = path.IsClosed ? new Color(0.3f, 0.9f, 0.55f) : new Color(0.2f, 0.78f, 0.95f);
            Handles.DrawAAPolyLine(3f, points);
            Handles.color = Color.white;
            var start = path.SampleAtDistance(0f).Position;
            var end = path.SampleAtDistance(path.TotalLengthMeters).Position;
            Handles.Label(points[0] + Vector3.right * 6f, $"Start ({start.x:F1}, {start.z:F1}) m");
            Handles.Label(points[points.Length - 1] + Vector3.right * 6f, $"End ({end.x:F1}, {end.z:F1}) m");
            for (var i = 0; i < path.Segments.Count; i++)
            {
                var segment = path.Segments[i];
                var middle = path.SampleAtDistance(segment.StartDistanceMeters + segment.LengthMeters * 0.5f);
                var x = rect.center.x + (middle.Position.x - center.x) * scale;
                var y = rect.center.y - (middle.Position.z - center.y) * scale;
                var direction = new Vector2(middle.Tangent.x, -middle.Tangent.z).normalized;
                var arrowCenter = new Vector2(x, y);
                var arrowTip = arrowCenter + direction * 13f;
                var arrowBase = arrowTip - direction * 7f;
                var arrowSide = new Vector2(-direction.y, direction.x) * 3f;
                var arrowPoints = new[]
                {
                    new Vector3(arrowCenter.x, arrowCenter.y, 0f),
                    new Vector3(arrowTip.x, arrowTip.y, 0f),
                    new Vector3(arrowBase.x + arrowSide.x, arrowBase.y + arrowSide.y, 0f),
                    new Vector3(arrowTip.x, arrowTip.y, 0f),
                    new Vector3(arrowBase.x - arrowSide.x, arrowBase.y - arrowSide.y, 0f)
                };
                Handles.DrawAAPolyLine(2f, arrowPoints);

                var label = segment.Type == TrackSegmentType.Straight
                    ? $"{i + 1}: {segment.LengthMeters:F0} m"
                    : $"{i + 1}: {segment.TurnDirection} {segment.AngleDegrees:F0} deg R{segment.RadiusMeters:F1} m";
                Handles.Label(new Vector3(x + 7f, y + 7f, 0f), label);
            }
            Handles.EndGUI();
        }
    }
}