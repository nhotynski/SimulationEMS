using System;
using System.Collections.Generic;
using IndustrialConveyor.Simulation.Core;
using IndustrialConveyor.Simulation.Suspension;
using IndustrialConveyor.Simulation.Track;
using IndustrialConveyor.Simulation.TrolleyTypes;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace IndustrialConveyor.Simulation.Movement
{
    /// <summary>Runs independent trolley drives at a fixed simulation step and enforces closed-loop spacing.</summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class TrolleySimulationManager : MonoBehaviour
    {
        [SerializeField] private TrackConfiguration trackConfiguration;
        [SerializeField] private SimulationConfiguration simulationConfiguration;
        [SerializeField] private TrolleyTypeDefinition[] availableTrolleyTypes;
        [SerializeField, Min(0f)] private float minimumGapMeters = 1f;
        [SerializeField, Min(0f)] private float initialTargetSpeedMetersPerSecond = 0.6f;
        [SerializeField] private bool startMovingOnPlay = true;
        [SerializeField, Min(1)] private int maximumStepsPerFrame = 8;

        private readonly List<TrolleyData> trolleys = new List<TrolleyData>();
        private readonly List<TrolleySuspension> suspensionPool = new List<TrolleySuspension>();
        private TrackPath trackPath;
        private float[] nextPositions;
        private float[] nextSpeeds;
        private float accumulatorSeconds;
        private bool initialized;
        private bool globalEmergencyStop;
        private bool hasResolvedAvailableTypes;

        /// <summary>Gets the runtime trolley states in current track order.</summary>
        public IReadOnlyList<TrolleyData> Trolleys => trolleys;

        /// <summary>Gets the simulated elapsed time in seconds.</summary>
        public float SimulationTimeSeconds { get; private set; }

        /// <summary>Gets whether simulation time is paused.</summary>
        public bool IsPaused { get; private set; }

        /// <summary>Gets the mathematical path shared by the trolley drives.</summary>
        public TrackPath TrackPath => trackPath;

        /// <summary>Gets the trolley types available to add from the diagnostics panel.</summary>
        public IReadOnlyList<TrolleyTypeDefinition> AvailableTrolleyTypes
        {
            get
            {
                ResolveAvailableTrolleyTypes();
                return availableTrolleyTypes;
            }
        }

        private void Awake()
        {
            Application.runInBackground = true;
            ResolveAvailableTrolleyTypes();
            Initialize();
        }

        private void ResolveAvailableTrolleyTypes()
        {
            if (availableTrolleyTypes != null && availableTrolleyTypes.Length > 0)
            {
                hasResolvedAvailableTypes = true;
                return;
            }
            if (hasResolvedAvailableTypes)
                return;

            hasResolvedAvailableTypes = true;
#if UNITY_EDITOR
            var knownAssetPaths = new[]
            {
                "Assets/_Project/ScriptableObjects/TrolleyTypes/TrolleyType_3m.asset",
                "Assets/_Project/ScriptableObjects/TrolleyTypes/TrolleyType_5m.asset",
                "Assets/_Project/ScriptableObjects/TrolleyTypes/TrolleyType_7m.asset"
            };
            var discoveredTypes = new List<TrolleyTypeDefinition>(knownAssetPaths.Length);
            foreach (var assetPath in knownAssetPaths)
            {
                var trolleyType = AssetDatabase.LoadAssetAtPath<TrolleyTypeDefinition>(assetPath);
                if (trolleyType != null && !discoveredTypes.Contains(trolleyType))
                    discoveredTypes.Add(trolleyType);
            }

            if (discoveredTypes.Count == 0)
            {
                var typeGuids = AssetDatabase.FindAssets("t:TrolleyTypeDefinition");
                foreach (var typeGuid in typeGuids)
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(typeGuid);
                    var trolleyType = AssetDatabase.LoadAssetAtPath<TrolleyTypeDefinition>(assetPath);
                    if (trolleyType != null && !discoveredTypes.Contains(trolleyType))
                        discoveredTypes.Add(trolleyType);
                }
            }

            discoveredTypes.Sort((left, right) =>
                StringComparer.Ordinal.Compare(left.DisplayName, right.DisplayName));
            availableTrolleyTypes = discoveredTypes.ToArray();
            if (availableTrolleyTypes.Length > 0)
                Debug.Log($"Loaded {availableTrolleyTypes.Length} trolley types from project assets.", this);
#endif
        }

        private void Update()
        {
            if (!initialized || IsPaused)
                return;

            var fixedStep = simulationConfiguration.FixedTimeStepSeconds;
            accumulatorSeconds += Time.unscaledDeltaTime * simulationConfiguration.SpeedMultiplier;
            var steps = 0;
            while (accumulatorSeconds >= fixedStep && steps < maximumStepsPerFrame)
            {
                AdvanceSimulation(fixedStep);
                accumulatorSeconds -= fixedStep;
                steps++;
            }

            if (steps == maximumStepsPerFrame && accumulatorSeconds >= fixedStep * maximumStepsPerFrame)
                accumulatorSeconds = fixedStep * maximumStepsPerFrame;
        }

        private void LateUpdate()
        {
            if (!initialized)
                return;

            foreach (var trolley in trolleys)
                trolley.Suspension.UpdateSuspension(trolley.SimulationPositionMeters);
        }

        /// <summary>Discovers scene trolleys, validates spacing, and initializes one state per independent drive.</summary>
        public void Initialize()
        {
            if (initialized)
                return;
            if (trackConfiguration == null)
                throw new InvalidOperationException("Trolley simulation manager requires a track configuration.");
            if (simulationConfiguration == null)
                throw new InvalidOperationException("Trolley simulation manager requires a simulation configuration.");
            if (float.IsNaN(minimumGapMeters) || float.IsInfinity(minimumGapMeters) || minimumGapMeters < 0f)
                throw new InvalidOperationException("Minimum trolley gap must be finite and non-negative.");
            if (float.IsNaN(initialTargetSpeedMetersPerSecond) || float.IsInfinity(initialTargetSpeedMetersPerSecond) ||
                initialTargetSpeedMetersPerSecond < 0f)
                throw new InvalidOperationException("Initial trolley speed must be finite and non-negative.");

            var buildResult = TrackGeometryBuilder.Build(trackConfiguration);
            if (!buildResult.Report.IsValid || !buildResult.Path.IsClosed)
                throw new InvalidOperationException(
                    $"Trolley simulation requires a valid closed track: {string.Join("; ", GetErrors(buildResult.Report))}");
            trackPath = buildResult.Path;

            var suspensions = UnityEngine.Object.FindObjectsByType<TrolleySuspension>(FindObjectsInactive.Exclude);
            Array.Sort(suspensions, CompareInitialPosition);
            if (suspensions.Length == 0)
                throw new InvalidOperationException("Trolley simulation manager could not find any active trolley suspensions.");

            suspensionPool.AddRange(suspensions);
            for (var i = 0; i < suspensions.Length; i++)
            {
                var suspension = suspensions[i];
                suspension.Initialize();
                trolleys.Add(CreateTrolleyData(i, suspension, suspension.InitialCenterPositionMeters));
            }

            ValidateInitialSpacing();
            nextPositions = new float[trolleys.Count];
            nextSpeeds = new float[trolleys.Count];
            IsPaused = simulationConfiguration.StartPaused;
            initialized = true;
            Debug.Log($"Trolley simulation initialized: {trolleys.Count} drives, {trackPath.TotalLengthMeters:F1} m track, " +
                      $"target {initialTargetSpeedMetersPerSecond:F2} m/s.", this);
        }

        /// <summary>Resizes the active trolley fleet, cloning the configured scene suspension when needed.</summary>
        public void SetTrolleyCount(int count)
        {
            if (!initialized)
                Initialize();
            if (count < 1 || count > 100)
                throw new ArgumentOutOfRangeException(nameof(count), "Trolley count must be between 1 and 100.");
            if (count == trolleys.Count)
                return;

            var maximumBodyLength = 0f;
            for (var i = 0; i < Mathf.Min(count, suspensionPool.Count); i++)
                maximumBodyLength = Mathf.Max(maximumBodyLength, suspensionPool[i].TrolleyType.BodyLengthMeters);
            if (count > suspensionPool.Count)
                maximumBodyLength = Mathf.Max(maximumBodyLength, suspensionPool[0].TrolleyType.BodyLengthMeters);
            var minimumPitch = maximumBodyLength + minimumGapMeters;
            if (trackPath.TotalLengthMeters / count < minimumPitch)
                throw new InvalidOperationException(
                    $"The track cannot fit {count} trolleys with the current body lengths and {minimumGapMeters:F2} m minimum gap.");

            while (suspensionPool.Count < count)
            {
                var template = suspensionPool[0];
                var clone = Instantiate(template.gameObject, template.transform.parent);
                clone.name = $"Runtime Trolley {suspensionPool.Count + 1:000}";
                var clonedSuspension = clone.GetComponent<TrolleySuspension>();
                if (clonedSuspension == null)
                {
                    DestroyTrolleyObject(clone);
                    throw new InvalidOperationException("Cloned trolley object is missing its suspension component.");
                }
                suspensionPool.Add(clonedSuspension);
            }

            trolleys.Clear();
            for (var i = 0; i < suspensionPool.Count; i++)
            {
                var suspension = suspensionPool[i];
                var active = i < count;
                if (suspension.gameObject.activeSelf != active)
                    suspension.gameObject.SetActive(active);
                if (!active)
                    continue;

                var position = trackPath.TotalLengthMeters * (i + 0.5f) / count;
                suspension.SetInitialCenterPositionMeters(position);
                trolleys.Add(CreateTrolleyData(i, suspension, position));
            }

            ValidateInitialSpacing();
            nextPositions = new float[count];
            nextSpeeds = new float[count];
            SimulationTimeSeconds = 0f;
            accumulatorSeconds = 0f;
            globalEmergencyStop = false;
        }

        /// <summary>Adds a trolley of the selected type at the requested track distance.</summary>
        public TrolleyData AddTrolley(TrolleyTypeDefinition trolleyType, float positionMeters)
        {
            if (!initialized)
                Initialize();
            if (trolleyType == null)
                throw new ArgumentNullException(nameof(trolleyType));
            if (!trolleyType.TryValidate(out var typeError))
                throw new ArgumentException(typeError, nameof(trolleyType));
            if (trolleys.Count >= 100)
                throw new InvalidOperationException("The maximum number of trolleys is 100.");
            if (float.IsNaN(positionMeters) || float.IsInfinity(positionMeters))
                throw new ArgumentOutOfRangeException(nameof(positionMeters));

            var template = suspensionPool.Find(suspension => suspension.TrolleyType == trolleyType) ?? suspensionPool[0];
            var clone = Instantiate(template.gameObject, template.transform.parent);
            clone.name = $"Runtime Trolley {GenerateTrolleyId()}";
            var suspension = clone.GetComponent<TrolleySuspension>();
            if (suspension == null)
            {
                DestroyTrolleyObject(clone);
                throw new InvalidOperationException("Cloned trolley object is missing its suspension component.");
            }

            try
            {
                suspension.SetTrolleyType(trolleyType);
                var position = trackPath.WrapDistance(positionMeters);
                suspension.SetInitialCenterPositionMeters(position);
                var trolley = CreateTrolleyData(ParseTrolleyIndex(GenerateTrolleyId()) - 1, suspension, position);
                trolleys.Add(trolley);
                trolleys.Sort(ComparePosition);
                ValidateInitialSpacing();
                suspensionPool.Add(suspension);
                ResizeDriveBuffers();
                return trolley;
            }
            catch
            {
                trolleys.RemoveAll(trolley => trolley.Suspension == suspension);
                DestroyTrolleyObject(clone);
                throw;
            }
        }

        /// <summary>Finds a track position with the greatest clearance for a trolley of the selected type.</summary>
        public float GetSuggestedTrolleyPositionMeters(TrolleyTypeDefinition trolleyType)
        {
            if (trolleyType == null)
                throw new ArgumentNullException(nameof(trolleyType));
            if (trolleys.Count == 0)
                return 0f;

            var sampleCount = Mathf.CeilToInt(trackPath.TotalLengthMeters * 2f);
            var bestPosition = 0f;
            var bestClearance = float.NegativeInfinity;
            for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
            {
                var candidatePosition = trackPath.TotalLengthMeters * sampleIndex / sampleCount;
                var minimumClearance = float.PositiveInfinity;
                foreach (var existing in trolleys)
                {
                    var halfLengthSum = (trolleyType.BodyLengthMeters + existing.TrolleyType.BodyLengthMeters) * 0.5f;
                    var forwardClearance = Mathf.Repeat(existing.SimulationPositionMeters - candidatePosition,
                        trackPath.TotalLengthMeters) - halfLengthSum;
                    var backwardClearance = Mathf.Repeat(candidatePosition - existing.SimulationPositionMeters,
                        trackPath.TotalLengthMeters) - halfLengthSum;
                    minimumClearance = Mathf.Min(minimumClearance, forwardClearance, backwardClearance);
                }

                if (minimumClearance <= bestClearance)
                    continue;
                bestClearance = minimumClearance;
                bestPosition = candidatePosition;
            }

            if (bestClearance < minimumGapMeters)
                throw new InvalidOperationException("There is no free position for another trolley on the track.");
            return bestPosition;
        }

        /// <summary>Removes one trolley from the active simulation.</summary>
        public void RemoveTrolley(string trolleyId)
        {
            if (trolleys.Count <= 1)
                throw new InvalidOperationException("At least one trolley must remain on the track.");

            var trolley = RequireTrolley(trolleyId);
            trolleys.Remove(trolley);
            suspensionPool.Remove(trolley.Suspension);
            DestroyTrolleyObject(trolley.Suspension.gameObject);
            ResizeDriveBuffers();
        }

        /// <summary>Moves one trolley to a track distance and stops it, rejecting overlapping positions.</summary>
        public void MoveTrolleyToPosition(string trolleyId, float positionMeters)
        {
            if (float.IsNaN(positionMeters) || float.IsInfinity(positionMeters))
                throw new ArgumentOutOfRangeException(nameof(positionMeters));

            var trolley = RequireTrolley(trolleyId);
            var previousPosition = trolley.SimulationPositionMeters;
            var previousSpeed = trolley.ActualSpeedMetersPerSecond;
            var previousTargetSpeed = trolley.TargetSpeedMetersPerSecond;
            trolley.SimulationPositionMeters = trackPath.WrapDistance(positionMeters);
            trolley.ActualSpeedMetersPerSecond = 0f;
            trolley.TargetSpeedMetersPerSecond = 0f;
            trolley.InitialPositionMeters = trolley.SimulationPositionMeters;
            trolleys.Sort(ComparePosition);

            try
            {
                ValidateInitialSpacing();
            }
            catch
            {
                trolley.SimulationPositionMeters = previousPosition;
                trolley.ActualSpeedMetersPerSecond = previousSpeed;
                trolley.TargetSpeedMetersPerSecond = previousTargetSpeed;
                trolley.InitialPositionMeters = previousPosition;
                trolleys.Sort(ComparePosition);
                throw;
            }

            trolley.Suspension.SetInitialCenterPositionMeters(trolley.SimulationPositionMeters);
            UpdateState(trolley);
        }

        /// <summary>Advances every drive exactly one supplied deterministic simulation step.</summary>
        public void AdvanceSimulation(float deltaTimeSeconds)
        {
            if (!initialized)
                Initialize();
            if (float.IsNaN(deltaTimeSeconds) || float.IsInfinity(deltaTimeSeconds) || deltaTimeSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTimeSeconds));

            var count = trolleys.Count;
            if (count == 0)
                return;

            trolleys.Sort(ComparePosition);
            for (var i = 0; i < count; i++)
            {
                var trolley = trolleys[i];
                var leader = count > 1 ? trolleys[(i + 1) % count] : null;
                var targetSpeed = globalEmergencyStop || trolley.IsEmergencyStopped || trolley.IsDriveFaulted
                    ? 0f
                    : trolley.TargetSpeedMetersPerSecond;

                if (leader != null)
                {
                    var clearance = TrolleyDriveModel.CalculateAvailableClearance(
                        trolley.SimulationPositionMeters,
                        trolley.TrolleyType.BodyLengthMeters,
                        leader.SimulationPositionMeters,
                        leader.TrolleyType.BodyLengthMeters,
                        trackPath.TotalLengthMeters,
                        minimumGapMeters);
                    var safeSpeed = TrolleyDriveModel.CalculateMaximumSafeSpeed(
                        leader.ActualSpeedMetersPerSecond,
                        trolley.DecelerationMetersPerSecondSquared,
                        Mathf.Max(0f, clearance));
                    targetSpeed = Mathf.Min(targetSpeed, safeSpeed);
                }

                var nextSpeed = TrolleyDriveModel.AdvanceSpeed(
                    trolley.ActualSpeedMetersPerSecond,
                    targetSpeed,
                    trolley.TrolleyType.MaximumSpeedMetersPerSecond,
                    trolley.AccelerationMetersPerSecondSquared,
                    trolley.DecelerationMetersPerSecondSquared,
                    deltaTimeSeconds);
                var distance = (trolley.ActualSpeedMetersPerSecond + nextSpeed) * 0.5f * deltaTimeSeconds;
                nextSpeeds[i] = nextSpeed;
                nextPositions[i] = trackPath.WrapDistance(trolley.SimulationPositionMeters + distance);
            }

            EnforceSpacingAfterStep(count);

            for (var i = 0; i < count; i++)
            {
                var trolley = trolleys[i];
                trolley.SimulationPositionMeters = nextPositions[i];
                trolley.ActualSpeedMetersPerSecond = nextSpeeds[i];
                UpdateState(trolley);
            }

            SimulationTimeSeconds += deltaTimeSeconds;
        }

        /// <summary>Sets a trolley's requested speed, bounded by its configured maximum.</summary>
        public void SetTargetSpeed(string trolleyId, float targetSpeedMetersPerSecond)
        {
            if (float.IsNaN(targetSpeedMetersPerSecond) || float.IsInfinity(targetSpeedMetersPerSecond) ||
                targetSpeedMetersPerSecond < 0f)
                throw new ArgumentOutOfRangeException(nameof(targetSpeedMetersPerSecond));

            var trolley = RequireTrolley(trolleyId);
            if (trolley.IsDriveFaulted || trolley.IsEmergencyStopped)
                return;
            trolley.TargetSpeedMetersPerSecond = Mathf.Min(targetSpeedMetersPerSecond,
                trolley.TrolleyType.MaximumSpeedMetersPerSecond);
        }

        /// <summary>Commands an individual trolley to its configured maximum speed.</summary>
        public void StartTrolley(string trolleyId)
        {
            var trolley = RequireTrolley(trolleyId);
            SetTargetSpeed(trolleyId, trolley.TrolleyType.MaximumSpeedMetersPerSecond);
        }

        /// <summary>Commands one trolley to decelerate to a stop.</summary>
        public void StopTrolley(string trolleyId)
        {
            var trolley = RequireTrolley(trolleyId);
            trolley.TargetSpeedMetersPerSecond = 0f;
        }

        /// <summary>Applies an individual emergency stop using the configured maximum deceleration.</summary>
        public void EmergencyStopTrolley(string trolleyId)
        {
            var trolley = RequireTrolley(trolleyId);
            trolley.IsEmergencyStopped = true;
            trolley.TargetSpeedMetersPerSecond = 0f;
        }

        /// <summary>Clears one trolley's emergency-stop latch without starting it.</summary>
        public void ResetEmergencyStop(string trolleyId)
        {
            var trolley = RequireTrolley(trolleyId);
            trolley.IsEmergencyStopped = false;
        }

        /// <summary>Simulates or clears an individual drive fault.</summary>
        public void SetDriveFault(string trolleyId, bool faulted, string errorMessage = "Simulated drive fault.")
        {
            var trolley = RequireTrolley(trolleyId);
            trolley.IsDriveFaulted = faulted;
            trolley.ErrorState = faulted ? errorMessage : null;
            if (faulted)
                trolley.TargetSpeedMetersPerSecond = 0f;
        }

        /// <summary>Applies a global emergency stop to every trolley.</summary>
        public void EmergencyStopAll()
        {
            globalEmergencyStop = true;
        }

        /// <summary>Clears the global emergency-stop latch without restarting the trolleys.</summary>
        public void ResetGlobalEmergencyStop()
        {
            globalEmergencyStop = false;
        }

        /// <summary>Pauses or resumes the fixed-step simulation clock.</summary>
        public void SetPaused(bool paused)
        {
            IsPaused = paused;
        }

        /// <summary>Finds one runtime trolley state by its generated unique ID.</summary>
        public TrolleyData FindTrolley(string trolleyId)
        {
            for (var i = 0; i < trolleys.Count; i++)
            {
                if (string.Equals(trolleys[i].TrolleyId, trolleyId, StringComparison.Ordinal))
                    return trolleys[i];
            }
            return null;
        }

        /// <summary>Resets position, speed, target, time, and stop/fault state to the initial scene configuration.</summary>
        public void ResetSimulation()
        {
            if (!initialized)
                Initialize();

            foreach (var trolley in trolleys)
            {
                trolley.SimulationPositionMeters = trolley.InitialPositionMeters;
                trolley.ActualSpeedMetersPerSecond = 0f;
                trolley.TargetSpeedMetersPerSecond = startMovingOnPlay
                    ? Mathf.Min(initialTargetSpeedMetersPerSecond, trolley.TrolleyType.MaximumSpeedMetersPerSecond)
                    : 0f;
                trolley.IsEmergencyStopped = false;
                trolley.IsDriveFaulted = false;
                trolley.ErrorState = null;
                UpdateState(trolley);
            }

            globalEmergencyStop = false;
            SimulationTimeSeconds = 0f;
            accumulatorSeconds = 0f;
        }

        private void ValidateInitialSpacing()
        {
            if (trolleys.Count < 2)
                return;

            for (var i = 0; i < trolleys.Count; i++)
            {
                var follower = trolleys[i];
                var leader = trolleys[(i + 1) % trolleys.Count];
                var clearance = TrolleyDriveModel.CalculateAvailableClearance(
                    follower.SimulationPositionMeters,
                    follower.TrolleyType.BodyLengthMeters,
                    leader.SimulationPositionMeters,
                    leader.TrolleyType.BodyLengthMeters,
                    trackPath.TotalLengthMeters,
                    minimumGapMeters);
                if (clearance < 0f)
                    throw new InvalidOperationException(
                        $"Initial trolley spacing overlaps {follower.TrolleyId} and {leader.TrolleyId} by {-clearance:F3} m.");
            }
        }

        private void EnforceSpacingAfterStep(int count)
        {
            if (count < 2)
                return;

            for (var i = count - 1; i >= 0; i--)
            {
                var leaderIndex = (i + 1) % count;
                var follower = trolleys[i];
                var leader = trolleys[leaderIndex];
                var requiredCenterDistance =
                    (follower.TrolleyType.BodyLengthMeters + leader.TrolleyType.BodyLengthMeters) * 0.5f + minimumGapMeters;
                var actualCenterDistance = Mathf.Repeat(nextPositions[leaderIndex] - nextPositions[i], trackPath.TotalLengthMeters);
                if (actualCenterDistance + 0.0001f >= requiredCenterDistance)
                    continue;

                nextPositions[i] = trackPath.WrapDistance(nextPositions[leaderIndex] - requiredCenterDistance);
                nextSpeeds[i] = Mathf.Min(nextSpeeds[i], nextSpeeds[leaderIndex]);
            }
        }

        private void UpdateState(TrolleyData trolley)
        {
            if (trolley.IsDriveFaulted)
            {
                trolley.OperatingState = TrolleyOperatingState.Faulted;
                trolley.DriveState = TrolleyDriveState.Faulted;
            }
            else if (globalEmergencyStop || trolley.IsEmergencyStopped)
            {
                trolley.OperatingState = trolley.ActualSpeedMetersPerSecond > 0.001f
                    ? TrolleyOperatingState.EmergencyStopping
                    : TrolleyOperatingState.Stopped;
                trolley.DriveState = TrolleyDriveState.EmergencyStopped;
            }
            else if (trolley.ActualSpeedMetersPerSecond > 0.001f &&
                     trolley.TargetSpeedMetersPerSecond < trolley.ActualSpeedMetersPerSecond - 0.001f)
            {
                trolley.OperatingState = TrolleyOperatingState.Braking;
                trolley.DriveState = TrolleyDriveState.Running;
            }
            else if (trolley.ActualSpeedMetersPerSecond > 0.001f)
            {
                trolley.OperatingState = TrolleyOperatingState.Moving;
                trolley.DriveState = TrolleyDriveState.Running;
            }
            else
            {
                trolley.OperatingState = TrolleyOperatingState.Stopped;
                trolley.DriveState = TrolleyDriveState.Stopped;
            }
        }

        private TrolleyData CreateTrolleyData(int index, TrolleySuspension suspension, float position)
        {
            var wrappedPosition = trackPath.WrapDistance(position);
            var targetSpeed = startMovingOnPlay
                ? Mathf.Min(initialTargetSpeedMetersPerSecond, suspension.TrolleyType.MaximumSpeedMetersPerSecond)
                : 0f;
            return new TrolleyData($"T-{index + 1:000}", suspension, wrappedPosition, targetSpeed);
        }

        private string GenerateTrolleyId()
        {
            for (var index = 1; index <= 100; index++)
            {
                var candidate = $"T-{index:000}";
                if (FindTrolley(candidate) == null)
                    return candidate;
            }
            throw new InvalidOperationException("No trolley IDs are available.");
        }

        private static int ParseTrolleyIndex(string trolleyId)
        {
            return int.Parse(trolleyId.Substring(2));
        }

        private void ResizeDriveBuffers()
        {
            nextPositions = new float[trolleys.Count];
            nextSpeeds = new float[trolleys.Count];
        }

        private static void DestroyTrolleyObject(GameObject trolleyObject)
        {
            if (Application.isPlaying)
                Destroy(trolleyObject);
            else
                DestroyImmediate(trolleyObject);
        }

        private TrolleyData RequireTrolley(string trolleyId)
        {
            var trolley = FindTrolley(trolleyId);
            if (trolley == null)
                throw new ArgumentException($"Trolley '{trolleyId}' was not found.", nameof(trolleyId));
            return trolley;
        }

        private static int CompareInitialPosition(TrolleySuspension left, TrolleySuspension right)
        {
            return left.InitialCenterPositionMeters.CompareTo(right.InitialCenterPositionMeters);
        }

        private static int ComparePosition(TrolleyData left, TrolleyData right)
        {
            return left.SimulationPositionMeters.CompareTo(right.SimulationPositionMeters);
        }

        private static string[] GetErrors(TrackValidationReport report)
        {
            var messages = new List<string>();
            foreach (var issue in report.Issues)
            {
                if (issue.Severity == TrackIssueSeverity.Error)
                    messages.Add(issue.Message);
            }
            return messages.ToArray();
        }
    }
}