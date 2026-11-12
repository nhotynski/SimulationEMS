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
        private TrackNetwork network;
        private TrolleyTraffic traffic;
        private float accumulatorSeconds;
        private bool initialized;
        private bool globalEmergencyStop;
        private bool hasResolvedAvailableTypes;

        /// <summary>Gets the runtime trolley states in track order (by edge, then position).</summary>
        public IReadOnlyList<TrolleyData> Trolleys => trolleys;

        /// <summary>Gets the simulated elapsed time in seconds.</summary>
        public float SimulationTimeSeconds { get; private set; }

        /// <summary>Gets whether simulation time is paused.</summary>
        public bool IsPaused { get; private set; }

        /// <summary>Gets the geometry of the main track edge (the whole loop on a simple loop).</summary>
        public TrackPath TrackPath => network != null ? network.Edges[0].Path : null;

        /// <summary>Gets the track network shared by the trolley drives.</summary>
        public TrackNetwork Network => network;

        /// <summary>Gets the traffic logic (spacing, switches, merges).</summary>
        public TrolleyTraffic Traffic => traffic;

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
                trolley.Suspension.UpdateSuspension(trolley.Edge, trolley.SimulationPositionMeters, trolley.Trail);
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

            network = TrackSource.ResolveNetwork(trackConfiguration);
            traffic = new TrolleyTraffic(network, minimumGapMeters);
            if (!network.IsMainRouteClosed)
                throw new InvalidOperationException("The main route of the track does not return to its start.");

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

            trolleys.Sort(ComparePosition);
            ValidateInitialSpacing();
            IsPaused = simulationConfiguration.StartPaused;
            initialized = true;
            Debug.Log($"Trolley simulation initialized: {trolleys.Count} drives, {network.TotalLengthMeters:F1} m track " +
                      $"({network.Edges.Count} routes, {network.Nodes.Count} switches), target {initialTargetSpeedMetersPerSecond:F2} m/s.", this);
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
            if (network.MainRouteLengthMeters / count < minimumPitch)
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

                var position = network.MainRouteLengthMeters * (i + 0.5f) / count;
                suspension.SetInitialCenterPositionMeters(position);
                trolleys.Add(CreateTrolleyData(i, suspension, position));
            }

            trolleys.Sort(ComparePosition);
            ValidateInitialSpacing();
            SimulationTimeSeconds = 0f;
            accumulatorSeconds = 0f;
            globalEmergencyStop = false;
        }

        /// <summary>Adds a trolley of the selected type at a distance along the main route (the loop on a simple loop).</summary>
        public TrolleyData AddTrolley(TrolleyTypeDefinition trolleyType, float positionMeters)
        {
            if (!initialized)
                Initialize();
            if (float.IsNaN(positionMeters) || float.IsInfinity(positionMeters))
                throw new ArgumentOutOfRangeException(nameof(positionMeters));
            return AddTrolley(trolleyType, network.ResolveMainRoute(positionMeters));
        }

        /// <summary>Adds a trolley of the selected type at a point of a track edge.</summary>
        public TrolleyData AddTrolley(TrolleyTypeDefinition trolleyType, TrackLocation location)
        {
            if (!initialized)
                Initialize();
            if (trolleyType == null)
                throw new ArgumentNullException(nameof(trolleyType));
            if (!trolleyType.TryValidate(out var typeError))
                throw new ArgumentException(typeError, nameof(trolleyType));
            if (trolleys.Count >= 100)
                throw new InvalidOperationException("The maximum number of trolleys is 100.");
            location = NormalizeLocation(location);

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
                var trolley = CreateTrolleyData(ParseTrolleyIndex(GenerateTrolleyId()) - 1, suspension, location);
                trolleys.Add(trolley);
                trolleys.Sort(ComparePosition);
                ValidateInitialSpacing();
                suspensionPool.Add(suspension);
                suspension.UpdateSuspension(trolley.Edge, trolley.SimulationPositionMeters, trolley.Trail);
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
        public TrackLocation GetSuggestedTrolleyLocation(TrolleyTypeDefinition trolleyType)
        {
            if (trolleyType == null)
                throw new ArgumentNullException(nameof(trolleyType));
            if (!initialized)
                Initialize();
            if (!traffic.TrySuggestLocation(trolleys, trolleyType.BodyLengthMeters, out var location))
                throw new InvalidOperationException("There is no free position for another trolley on the track.");
            return location;
        }

        /// <summary>Finds a free distance (on its edge) for a trolley of the selected type; see <see cref="GetSuggestedTrolleyLocation"/>.</summary>
        public float GetSuggestedTrolleyPositionMeters(TrolleyTypeDefinition trolleyType)
        {
            return GetSuggestedTrolleyLocation(trolleyType).Meters;
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
        }

        /// <summary>Moves one trolley to a distance on its current edge and stops it, rejecting overlapping positions.</summary>
        public void MoveTrolleyToPosition(string trolleyId, float positionMeters)
        {
            if (float.IsNaN(positionMeters) || float.IsInfinity(positionMeters))
                throw new ArgumentOutOfRangeException(nameof(positionMeters));
            MoveTrolleyToLocation(trolleyId, new TrackLocation(RequireTrolley(trolleyId).Edge, positionMeters));
        }

        /// <summary>Moves one trolley to a point of a track edge and stops it, rejecting overlapping positions.</summary>
        public void MoveTrolleyToLocation(string trolleyId, TrackLocation location)
        {
            var trolley = RequireTrolley(trolleyId);
            location = NormalizeLocation(location);
            var previousEdge = trolley.Edge;
            var previousPosition = trolley.SimulationPositionMeters;
            var previousTrail = new List<TrackEdge>(trolley.Trail);
            var previousSpeed = trolley.ActualSpeedMetersPerSecond;
            var previousTargetSpeed = trolley.TargetSpeedMetersPerSecond;
            var previousInitialEdge = trolley.InitialEdge;
            var previousInitialPosition = trolley.InitialPositionMeters;
            trolley.Place(location.Edge, location.Meters);
            trolley.ActualSpeedMetersPerSecond = 0f;
            trolley.TargetSpeedMetersPerSecond = 0f;
            trolley.InitialEdge = location.Edge;
            trolley.InitialPositionMeters = location.Meters;
            trolleys.Sort(ComparePosition);

            try
            {
                ValidateInitialSpacing();
            }
            catch
            {
                trolley.Place(previousEdge, previousPosition);
                trolley.Trail.AddRange(previousTrail);
                trolley.ActualSpeedMetersPerSecond = previousSpeed;
                trolley.TargetSpeedMetersPerSecond = previousTargetSpeed;
                trolley.InitialEdge = previousInitialEdge;
                trolley.InitialPositionMeters = previousInitialPosition;
                trolleys.Sort(ComparePosition);
                throw;
            }

            trolley.Suspension.UpdateSuspension(trolley.Edge, trolley.SimulationPositionMeters, trolley.Trail);
            UpdateState(trolley);
        }

        /// <summary>
        /// Gets the trolley's position as a barcode value (mm): the code strip value under its barcode reader, or -1 when the
        /// track there has no strip. The reader sits <c>BarcodeReaderMountingPositionMeters.z</c> ahead of the trolley center.
        /// On a track without code strips the value is the distance along the track in mm.
        /// </summary>
        public int GetBarcode(TrolleyData trolley)
        {
            if (!initialized)
                Initialize();
            return GetBarcodeForCenter(trolley.TrolleyType, new TrackLocation(trolley.Edge, trolley.SimulationPositionMeters),
                trolley.Trail);
        }

        /// <summary>Gets the barcode value a trolley of the given type would read with its center at <paramref name="center"/>.</summary>
        public int GetBarcodeForCenter(TrolleyTypeDefinition trolleyType, TrackLocation center, IReadOnlyList<TrackEdge> trail = null)
        {
            if (!initialized)
                Initialize();
            var reader = network.ResolveOffset(center.Edge, center.Meters, trolleyType.BarcodeReaderMountingPositionMeters.z, trail);
            return network.GetBarcode(reader);
        }

        /// <summary>Finds the center location at which a trolley of the given type reads the barcode in <paramref name="text"/>.</summary>
        public bool TryResolveCenterFromBarcode(TrolleyTypeDefinition trolleyType, string text, out TrackLocation center,
            out string error)
        {
            if (!initialized)
                Initialize();
            if (!network.TryFindBarcode(text, out var reader, out error))
            {
                center = reader;
                return false;
            }
            center = network.ResolveOffset(reader.Edge, reader.Meters, -trolleyType.BarcodeReaderMountingPositionMeters.z, null);
            return true;
        }

        /// <summary>Gets the splits of the network (the switches whose state is sent over UDP).</summary>
        public IEnumerable<TrackNode> GetSwitches()
        {
            if (!initialized)
                Initialize();
            foreach (var node in network.Nodes)
            {
                if (node.Kind == TrackNodeKind.Split)
                    yield return node;
            }
        }

        /// <summary>
        /// Requests a switch position by switch name. Split: outgoing index (0 = Q0 branch, 1 = Q1 straight).
        /// Merge: incoming index that is held open, or -1 for automatic right of way.
        /// </summary>
        public void SetSwitchState(string switchName, int state)
        {
            if (!initialized)
                Initialize();
            var node = network.FindNode(switchName);
            if (node == null)
                throw new ArgumentException($"Switch '{switchName}' was not found.", nameof(switchName));
            if (node.Kind == TrackNodeKind.Split)
                node.RequestOutIndex(state);
            else
                node.RequestInIndex(state);
        }

        /// <summary>Advances every drive exactly one supplied deterministic simulation step.</summary>
        public void AdvanceSimulation(float deltaTimeSeconds)
        {
            if (!initialized)
                Initialize();
            if (float.IsNaN(deltaTimeSeconds) || float.IsInfinity(deltaTimeSeconds) || deltaTimeSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTimeSeconds));

            if (trolleys.Count == 0)
                return;

            traffic.MinimumGapMeters = minimumGapMeters;
            traffic.Step(trolleys, deltaTimeSeconds, globalEmergencyStop);
            foreach (var trolley in trolleys)
                UpdateState(trolley);
            trolleys.Sort(ComparePosition);

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

            network.ResetSwitches();
            traffic = new TrolleyTraffic(network, minimumGapMeters);
            foreach (var trolley in trolleys)
            {
                trolley.Place(trolley.InitialEdge, trolley.InitialPositionMeters);
                trolley.ActualSpeedMetersPerSecond = 0f;
                trolley.TargetSpeedMetersPerSecond = startMovingOnPlay
                    ? Mathf.Min(initialTargetSpeedMetersPerSecond, trolley.TrolleyType.MaximumSpeedMetersPerSecond)
                    : 0f;
                trolley.IsEmergencyStopped = false;
                trolley.IsDriveFaulted = false;
                trolley.ErrorState = null;
                UpdateState(trolley);
            }

            trolleys.Sort(ComparePosition);
            globalEmergencyStop = false;
            SimulationTimeSeconds = 0f;
            accumulatorSeconds = 0f;
        }

        private void ValidateInitialSpacing()
        {
            traffic.MinimumGapMeters = minimumGapMeters;
            if (traffic.TryFindOverlap(trolleys, out var description))
                throw new InvalidOperationException(description);
        }

        private TrackLocation NormalizeLocation(TrackLocation location)
        {
            var edge = location.Edge ?? network.Edges[0];
            if (float.IsNaN(location.Meters) || float.IsInfinity(location.Meters))
                throw new ArgumentOutOfRangeException(nameof(location));
            var meters = edge.IsRing
                ? Mathf.Repeat(location.Meters, edge.LengthMeters)
                : Mathf.Clamp(location.Meters, 0f, edge.LengthMeters);
            return new TrackLocation(edge, meters);
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

        private TrolleyData CreateTrolleyData(int index, TrolleySuspension suspension, float mainRoutePosition)
        {
            return CreateTrolleyData(index, suspension, network.ResolveMainRoute(mainRoutePosition));
        }

        private TrolleyData CreateTrolleyData(int index, TrolleySuspension suspension, TrackLocation location)
        {
            var targetSpeed = startMovingOnPlay
                ? Mathf.Min(initialTargetSpeedMetersPerSecond, suspension.TrolleyType.MaximumSpeedMetersPerSecond)
                : 0f;
            return new TrolleyData($"T-{index + 1:000}", suspension, location.Edge, location.Meters, targetSpeed);
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
            var byEdge = left.Edge.Index.CompareTo(right.Edge.Index);
            return byEdge != 0 ? byEdge : left.SimulationPositionMeters.CompareTo(right.SimulationPositionMeters);
        }
    }
}