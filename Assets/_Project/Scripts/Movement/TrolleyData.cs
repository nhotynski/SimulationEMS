using System.Collections.Generic;
using IndustrialConveyor.Simulation.Suspension;
using IndustrialConveyor.Simulation.Track;
using IndustrialConveyor.Simulation.TrolleyTypes;

namespace IndustrialConveyor.Simulation.Movement
{
    /// <summary>Summarizes the trolley's high-level simulated operation.</summary>
    public enum TrolleyOperatingState
    {
        Stopped,
        Moving,
        Braking,
        EmergencyStopping,
        Faulted
    }

    /// <summary>Summarizes the independent electric drive state.</summary>
    public enum TrolleyDriveState
    {
        Ready,
        Running,
        Stopped,
        EmergencyStopped,
        Faulted
    }

    /// <summary>Runtime-only simulation state for one trolley.</summary>
    public sealed class TrolleyData
    {
        internal const int MaximumTrailLength = 4;

        internal TrolleyData(string trolleyId, TrolleySuspension suspension, TrackEdge edge, float simulationPositionMeters,
            float targetSpeedMetersPerSecond)
        {
            TrolleyId = trolleyId;
            Suspension = suspension;
            TrolleyType = suspension.TrolleyType;
            Edge = edge;
            InitialEdge = edge;
            InitialPositionMeters = simulationPositionMeters;
            SimulationPositionMeters = simulationPositionMeters;
            TargetSpeedMetersPerSecond = targetSpeedMetersPerSecond;
            DriveState = TrolleyDriveState.Ready;
            OperatingState = TrolleyOperatingState.Stopped;
        }

        /// <summary>Gets the unique runtime trolley identifier.</summary>
        public string TrolleyId { get; }

        /// <summary>Gets the data-driven trolley type.</summary>
        public TrolleyTypeDefinition TrolleyType { get; }

        /// <summary>Gets the track edge the trolley center is on (the only edge of a simple loop).</summary>
        public TrackEdge Edge { get; internal set; }

        /// <summary>Gets the internal mathematical position of the trolley center in meters along <see cref="Edge"/>.</summary>
        public float SimulationPositionMeters { get; internal set; }

        /// <summary>Gets the actual simulated speed in meters per second.</summary>
        public float ActualSpeedMetersPerSecond { get; internal set; }

        /// <summary>Gets the commanded speed in meters per second.</summary>
        public float TargetSpeedMetersPerSecond { get; internal set; }

        /// <summary>Gets the configured acceleration in meters per second squared.</summary>
        public float AccelerationMetersPerSecondSquared => TrolleyType.AccelerationMetersPerSecondSquared;

        /// <summary>Gets the configured deceleration in meters per second squared.</summary>
        public float DecelerationMetersPerSecondSquared => TrolleyType.DecelerationMetersPerSecondSquared;

        /// <summary>Gets the current operating state.</summary>
        public TrolleyOperatingState OperatingState { get; internal set; }

        /// <summary>Gets the independent drive state.</summary>
        public TrolleyDriveState DriveState { get; internal set; }

        /// <summary>Gets an optional drive or simulation error.</summary>
        public string ErrorState { get; internal set; }

        internal TrolleySuspension Suspension { get; }
        internal int StepIndex { get; set; }
        internal TrackEdge InitialEdge { get; set; }
        internal float InitialPositionMeters { get; set; }
        internal bool IsEmergencyStopped { get; set; }
        internal bool IsDriveFaulted { get; set; }

        /// <summary>Gets the edges travelled before <see cref="Edge"/>, most recent first, used to place the rear wheel.</summary>
        internal List<TrackEdge> Trail { get; } = new List<TrackEdge>();

        internal void EnterEdge(TrackEdge edge)
        {
            Trail.Insert(0, Edge);
            if (Trail.Count > MaximumTrailLength)
                Trail.RemoveAt(Trail.Count - 1);
            Edge = edge;
        }

        internal void Place(TrackEdge edge, float meters)
        {
            Edge = edge;
            SimulationPositionMeters = meters;
            Trail.Clear();
        }
    }
}
