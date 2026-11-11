using System.Linq;
using IndustrialConveyor.Simulation.Movement;
using IndustrialConveyor.Simulation.TrolleyTypes;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace IndustrialConveyor.Tests
{
    public sealed class TrolleyMovementTests
    {
        [Test]
        public void AdvanceSpeed_UsesAccelerationLimit()
        {
            var speed = TrolleyDriveModel.AdvanceSpeed(0f, 1.5f, 1.5f, 0.3f, 0.5f, 0.1f);

            Assert.That(speed, Is.EqualTo(0.03f).Within(0.0001f));
        }

        [Test]
        public void AdvanceSpeed_UsesDecelerationLimit()
        {
            var speed = TrolleyDriveModel.AdvanceSpeed(1f, 0f, 1.5f, 0.3f, 0.5f, 0.1f);

            Assert.That(speed, Is.EqualTo(0.95f).Within(0.0001f));
        }

        [Test]
        public void AdvanceSpeed_RespectsMaximumSpeed()
        {
            var speed = TrolleyDriveModel.AdvanceSpeed(1.4f, 2f, 1.5f, 1f, 1f, 1f);

            Assert.That(speed, Is.EqualTo(1.5f).Within(0.0001f));
        }

        [Test]
        public void MaximumSafeSpeed_UsesLeaderSpeedAndStoppingDistance()
        {
            var safeSpeed = TrolleyDriveModel.CalculateMaximumSafeSpeed(0.5f, 0.5f, 3.75f);

            Assert.That(safeSpeed * safeSpeed, Is.EqualTo(4f).Within(0.0001f));
        }

        [Test]
        public void AvailableClearance_AccountsForDifferentLengthsAcrossLoopBoundary()
        {
            var clearance = TrolleyDriveModel.CalculateAvailableClearance(
                followerPositionMeters: 990f,
                followerLengthMeters: 7f,
                leaderPositionMeters: 10f,
                leaderLengthMeters: 3f,
                trackLengthMeters: 1000f,
                minimumGapMeters: 1f);

            Assert.That(clearance, Is.EqualTo(14f).Within(0.0001f));
        }

        [Test]
        public void AvailableClearance_IsNegativeWhenUnequalBodiesOverlap()
        {
            var clearance = TrolleyDriveModel.CalculateAvailableClearance(
                followerPositionMeters: 50f,
                followerLengthMeters: 7f,
                leaderPositionMeters: 55f,
                leaderLengthMeters: 5f,
                trackLengthMeters: 1000f,
                minimumGapMeters: 1f);

            Assert.That(clearance, Is.EqualTo(-2f).Within(0.0001f));
        }

        [Test]
        public void SimulationManager_ResizesTrolleyFleetAndRebuildsDriveBuffers()
        {
            var previousScenePath = SceneManager.GetActiveScene().path;
            try
            {
                EditorSceneManager.OpenScene("Assets/_Project/Scenes/TrackTestScene.unity", OpenSceneMode.Single);
                var manager = UnityEngine.Object.FindAnyObjectByType<TrolleySimulationManager>();
                Assert.That(manager, Is.Not.Null);

                manager.Initialize();
                manager.SetTrolleyCount(5);

                Assert.That(manager.Trolleys.Count, Is.EqualTo(5));
                Assert.That(manager.Trolleys.Select(trolley => trolley.TrolleyId).Distinct().Count(), Is.EqualTo(5));
                for (var index = 0; index < manager.Trolleys.Count; index++)
                {
                    var follower = manager.Trolleys[index];
                    var leader = manager.Trolleys[(index + 1) % manager.Trolleys.Count];
                    var clearance = TrolleyDriveModel.CalculateAvailableClearance(
                        follower.SimulationPositionMeters,
                        follower.TrolleyType.BodyLengthMeters,
                        leader.SimulationPositionMeters,
                        leader.TrolleyType.BodyLengthMeters,
                        manager.TrackPath.TotalLengthMeters,
                        1f);
                    Assert.That(clearance, Is.GreaterThanOrEqualTo(-0.001f));
                }

                manager.SetTrolleyCount(2);
                manager.AdvanceSimulation(0.02f);
                Assert.That(manager.Trolleys.Count, Is.EqualTo(2));
                Assert.That(manager.FindTrolley("T-003"), Is.Null);
                Assert.That(manager.SimulationTimeSeconds, Is.EqualTo(0.02f).Within(0.0001f));
            }
            finally
            {
                if (!string.IsNullOrEmpty(previousScenePath) &&
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(previousScenePath) != null)
                    EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
            }
        }

        [Test]
        public void SimulationManager_AddsMovesAndRemovesSelectedTrolleyType()
        {
            var previousScenePath = SceneManager.GetActiveScene().path;
            try
            {
                EditorSceneManager.OpenScene("Assets/_Project/Scenes/TrackTestScene.unity", OpenSceneMode.Single);
                var manager = UnityEngine.Object.FindAnyObjectByType<TrolleySimulationManager>();
                Assert.That(manager, Is.Not.Null);

                manager.Initialize();
                Assert.That(manager.AvailableTrolleyTypes.Count, Is.EqualTo(3));
                var trolleyType = AssetDatabase.LoadAssetAtPath<TrolleyTypeDefinition>(
                    "Assets/_Project/ScriptableObjects/TrolleyTypes/TrolleyType_5m.asset");
                Assert.That(trolleyType, Is.Not.Null);

                var added = manager.AddTrolley(trolleyType, 500f);
                Assert.That(added.TrolleyType, Is.EqualTo(trolleyType));
                Assert.That(manager.Trolleys.Count, Is.EqualTo(4));

                manager.MoveTrolleyToPosition("T-001", 900f);
                Assert.That(manager.FindTrolley("T-001").SimulationPositionMeters, Is.EqualTo(900f).Within(0.001f));

                manager.RemoveTrolley(added.TrolleyId);
                Assert.That(manager.FindTrolley(added.TrolleyId), Is.Null);
                Assert.That(manager.Trolleys.Count, Is.EqualTo(3));
            }
            finally
            {
                if (!string.IsNullOrEmpty(previousScenePath) &&
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(previousScenePath) != null)
                    EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
            }
        }

        [Test]
        public void SimulationManager_AdvancesIndependentDrivesWithoutOverlappingTrolleys()
        {
            var previousScenePath = SceneManager.GetActiveScene().path;
            try
            {
                EditorSceneManager.OpenScene("Assets/_Project/Scenes/TrackTestScene.unity", OpenSceneMode.Single);
                var manager = UnityEngine.Object.FindAnyObjectByType<TrolleySimulationManager>();
                Assert.That(manager, Is.Not.Null);

                manager.Initialize();
                Assert.That(manager.Trolleys.Count, Is.EqualTo(3));
                Assert.That(manager.Trolleys.Select(trolley => trolley.TrolleyId).Distinct().Count(), Is.EqualTo(3));

                manager.SetTargetSpeed("T-001", 1.4f);
                manager.SetTargetSpeed("T-002", 0.4f);
                manager.EmergencyStopTrolley("T-003");
                for (var step = 0; step < 1200; step++)
                    manager.AdvanceSimulation(0.02f);

                var ordered = manager.Trolleys.OrderBy(trolley => trolley.SimulationPositionMeters).ToArray();
                for (var i = 0; i < ordered.Length; i++)
                {
                    var follower = ordered[i];
                    var leader = ordered[(i + 1) % ordered.Length];
                    var clearance = TrolleyDriveModel.CalculateAvailableClearance(
                        follower.SimulationPositionMeters,
                        follower.TrolleyType.BodyLengthMeters,
                        leader.SimulationPositionMeters,
                        leader.TrolleyType.BodyLengthMeters,
                        manager.TrackPath.TotalLengthMeters,
                        1f);
                    Assert.That(clearance, Is.GreaterThanOrEqualTo(-0.001f));
                }

                Assert.That(manager.FindTrolley("T-001").SimulationPositionMeters, Is.Not.EqualTo(70f).Within(0.01f));
                Assert.That(manager.FindTrolley("T-001").TrolleyType.BodyLengthMeters, Is.EqualTo(3f));
                Assert.That(manager.FindTrolley("T-002").TrolleyType.BodyLengthMeters, Is.EqualTo(5f));
                Assert.That(manager.FindTrolley("T-003").TrolleyType.BodyLengthMeters, Is.EqualTo(7f));
                Assert.That(manager.FindTrolley("T-003").DriveState, Is.EqualTo(TrolleyDriveState.EmergencyStopped));
                Assert.That(manager.SimulationTimeSeconds, Is.EqualTo(24f).Within(0.001f));
            }
            finally
            {
                if (!string.IsNullOrEmpty(previousScenePath) &&
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(previousScenePath) != null)
                    EditorSceneManager.OpenScene(previousScenePath, OpenSceneMode.Single);
            }
        }
    }
}