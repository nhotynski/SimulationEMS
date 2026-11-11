using System.Linq;
using IndustrialConveyor.Simulation.Suspension;
using IndustrialConveyor.Simulation.Track;
using IndustrialConveyor.Simulation.TrolleyTypes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace IndustrialConveyor.Tests
{
    public sealed class TrackGeometryTests
    {
        [Test]
        public void Straight_UsesInitialHeadingAndExactDistance()
        {
            var result = TrackGeometryBuilder.Build(Vector3.zero, 20f,
                new[] { TrackSegmentDefinition.Straight("Straight", 20f) }, Settings(false, 20f));
            var expectedPosition = new Vector3(Mathf.Sin(20f * Mathf.Deg2Rad) * 20f,
                0f, Mathf.Cos(20f * Mathf.Deg2Rad) * 20f);

            Assert.That(result.Report.IsValid, Is.True);
            Assert.That(result.Path.TotalLengthMeters, Is.EqualTo(20f).Within(0.0001f));
            Assert.That(Vector3.Distance(result.Path.SampleAtDistance(20f).Position, expectedPosition), Is.LessThan(0.0001f));
        }

        [TestCase(TrackTurnDirection.Right, 5f)]
        [TestCase(TrackTurnDirection.Left, -5f)]
        public void Curve_UsesCircularArcLengthAndDirection(TrackTurnDirection direction, float expectedX)
        {
            var result = Build(false, 0f, TrackSegmentDefinition.Curve("Quarter turn", direction, 90f, 5f));
            var end = result.Path.SampleAtDistance(result.Path.TotalLengthMeters);

            Assert.That(result.Path.TotalLengthMeters, Is.EqualTo(5f * Mathf.PI * 0.5f).Within(0.0001f));
            Assert.That(Vector3.Distance(end.Position, new Vector3(expectedX, 0f, 5f)), Is.LessThan(0.0001f));
        }

        [Test]
        public void AdjacentSegments_ConnectPositionAndTangent()
        {
            var result = Build(false, 0f,
                TrackSegmentDefinition.Straight("Approach", 20f),
                TrackSegmentDefinition.Curve("Turn", TrackTurnDirection.Right, 90f, 5f));
            var first = result.Path.Segments[0];
            var second = result.Path.Segments[1];

            Assert.That(Vector3.Distance(first.EndPosition, second.StartPosition), Is.LessThan(0.0001f));
            Assert.That(Vector3.Angle(first.EndTangent, second.StartTangent), Is.LessThan(0.0001f));
            Assert.That(result.Report.Issues.Any(issue => issue.Code == "SEGMENT_POSITION_DISCONTINUITY"), Is.False);
            Assert.That(result.Report.Issues.Any(issue => issue.Code == "SEGMENT_TANGENT_DISCONTINUITY"), Is.False);
        }

        [Test]
        public void FullCircle_ClosesPositionAndHeadingAndWrapsDistance()
        {
            var length = 20f * Mathf.PI;
            var result = Build(true, length,
                TrackSegmentDefinition.Curve("Full loop", TrackTurnDirection.Right, 360f, 10f));
            var wrappedStart = result.Path.SampleAtDistance(length, true);

            Assert.That(result.Report.IsClosed, Is.True);
            Assert.That(result.Path.IsClosed, Is.True);
            Assert.That(result.Report.ClosurePositionErrorMeters, Is.LessThan(0.0001f));
            Assert.That(result.Report.ClosureHeadingErrorDegrees, Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(wrappedStart.Position, Vector3.zero), Is.LessThan(0.0001f));
        }

        [Test]
        public void OpenTestConfiguration_ReportsClosureAndTargetLengthErrors()
        {
            var result = TrackGeometryBuilder.Build(Vector3.zero, 0f, new[]
            {
                TrackSegmentDefinition.Straight("North", 200f),
                TrackSegmentDefinition.Curve("Right", TrackTurnDirection.Right, 90f, 5f),
                TrackSegmentDefinition.Straight("East", 100f),
                TrackSegmentDefinition.Curve("Left", TrackTurnDirection.Left, 90f, 5f)
            }, Settings(true, 1000f));

            Assert.That(result.Report.IsClosed, Is.False);
            Assert.That(result.Report.Issues.Any(issue => issue.Code == "TRACK_OPEN"), Is.True);
            Assert.That(result.Report.TargetLengthDifferenceMeters, Is.EqualTo(result.Report.ActualLengthMeters - 1000f).Within(0.0001f));
            Assert.Throws<System.InvalidOperationException>(() => result.Path.WrapDistance(1f));
        }

        [Test]
        public void InvalidCurveRadius_IsReportedWithoutChangingInput()
        {
            var curve = TrackSegmentDefinition.Curve("Invalid curve", TrackTurnDirection.Right, 90f, 0f);
            var result = Build(false, 100f, curve);

            Assert.That(result.Report.IsValid, Is.False);
            Assert.That(result.Report.Issues.Any(issue => issue.Code == "CURVE_RADIUS_INVALID"), Is.True);
            Assert.That(curve.RadiusMeters, Is.EqualTo(0f));
        }

        [Test]
        public void RepeatedLoop_IsReportedAsSelfIntersection()
        {
            var result = Build(false, 125.6637f,
                TrackSegmentDefinition.Curve("Loop A", TrackTurnDirection.Right, 360f, 10f),
                TrackSegmentDefinition.Curve("Loop B", TrackTurnDirection.Right, 360f, 10f));

            Assert.That(result.Report.Issues.Any(issue => issue.Code == "TRACK_SELF_INTERSECTION"), Is.True);
        }

        [Test]
        public void TrackTestAsset_ClosesAtTargetLength()
        {
            var configuration = AssetDatabase.LoadAssetAtPath<TrackConfiguration>(
                "Assets/_Project/ScriptableObjects/Track/TrackConfiguration_TrackTest.asset");
            Assert.That(configuration, Is.Not.Null);

            var result = TrackGeometryBuilder.Build(configuration);

            Assert.That(result.Report.IsValid, Is.True,
                string.Join("; ", result.Report.Issues.Select(issue => issue.Message)));
            Assert.That(result.Report.IsClosed, Is.True);
            Assert.That(result.Report.ActualLengthMeters, Is.EqualTo(1000f).Within(0.1f));
            Assert.That(result.Report.ClosurePositionErrorMeters, Is.LessThan(0.01f));
            Assert.That(result.Report.ClosureHeadingErrorDegrees, Is.LessThan(0.1f));
        }

        [TestCase("TrolleyType_3m.asset", 3f, 2.4f)]
        [TestCase("TrolleyType_5m.asset", 5f, 4.2f)]
        [TestCase("TrolleyType_7m.asset", 7f, 6f)]
        public void TrolleyTypeAsset_SeparatesBodyLengthAndWheelbase(string assetName,
            float expectedLength, float expectedWheelbase)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TrolleyTypeDefinition>(
                $"Assets/_Project/ScriptableObjects/TrolleyTypes/{assetName}");

            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.BodyLengthMeters, Is.EqualTo(expectedLength).Within(0.0001f));
            Assert.That(asset.WheelbaseMeters, Is.EqualTo(expectedWheelbase).Within(0.0001f));
            Assert.That(asset.WheelbaseMeters, Is.Not.EqualTo(asset.BodyLengthMeters));
            Assert.That(asset.TryValidate(out var error), Is.True, error);
        }

        [Test]
        public void Suspension_SamplesFrontAndRearIndependentlyOnStraight()
        {
            AssertSuspensionMatchesPathSamples(100f);
        }

        [Test]
        public void Suspension_SamplesFrontAndRearIndependentlyOnCurve()
        {
            AssertSuspensionMatchesPathSamples(204f);
        }

        private static TrackBuildResult Build(bool requireClosedLoop, float targetLengthMeters,
            params TrackSegmentDefinition[] segments)
        {
            return TrackGeometryBuilder.Build(Vector3.zero, 0f, segments, Settings(requireClosedLoop, targetLengthMeters));
        }

        private static TrackBuildSettings Settings(bool requireClosedLoop, float targetLengthMeters)
        {
            return new TrackBuildSettings(targetLengthMeters, requireClosedLoop, 0.01f, 0.1f, 0.1f, 1f);
        }

        private static void AssertSuspensionMatchesPathSamples(float centerPositionMeters)
        {
            const string trolleyPrefabPath = "Assets/_Project/Prefabs/Trolley/Trolley.prefab";
            const string trackConfigurationPath = "Assets/_Project/ScriptableObjects/Track/TrackConfiguration_TrackTest.asset";
            var trolleyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(trolleyPrefabPath);
            var trackConfiguration = AssetDatabase.LoadAssetAtPath<TrackConfiguration>(trackConfigurationPath);
            Assert.That(trolleyPrefab, Is.Not.Null);
            Assert.That(trackConfiguration, Is.Not.Null);

            var trolleyObject = (GameObject)PrefabUtility.InstantiatePrefab(trolleyPrefab);
            try
            {
                var suspension = trolleyObject.GetComponent<TrolleySuspension>();
                Assert.That(suspension, Is.Not.Null);
                suspension.Initialize();
                suspension.UpdateSuspension(centerPositionMeters);

                var path = TrackGeometryBuilder.Build(trackConfiguration).Path;
                var halfWheelbase = suspension.TrolleyType.WheelbaseMeters * 0.5f;
                var frontSample = path.SampleAtDistance(centerPositionMeters + halfWheelbase, true);
                var rearSample = path.SampleAtDistance(centerPositionMeters - halfWheelbase, true);
                var frame = trolleyObject.transform;
                var expectedFront = frame.TransformPoint(frontSample.Position +
                    frontSample.Normal * suspension.TrackElevationMeters +
                    frontSample.Rotation * suspension.TrolleyType.FrontWheelAssemblyOffsetMeters);
                var expectedRear = frame.TransformPoint(rearSample.Position +
                    rearSample.Normal * suspension.TrackElevationMeters +
                    rearSample.Rotation * suspension.TrolleyType.RearWheelAssemblyOffsetMeters);

                Assert.That(Vector3.Distance(suspension.FrontWheelPosition, expectedFront), Is.LessThan(0.0001f));
                Assert.That(Vector3.Distance(suspension.RearWheelPosition, expectedRear), Is.LessThan(0.0001f));
                Assert.That(frontSample.DistanceMeters - rearSample.DistanceMeters,
                    Is.EqualTo(suspension.TrolleyType.WheelbaseMeters).Within(0.0001f));
                Assert.That(Quaternion.Angle(suspension.BodyRotation,
                    Quaternion.LookRotation(Vector3.ProjectOnPlane(suspension.FrontWheelPosition - suspension.RearWheelPosition,
                        Vector3.up).normalized, Vector3.up)), Is.LessThan(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(trolleyObject);
            }
        }
    }
}