using IndustrialConveyor.Simulation.Suspension;
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
        internal TrolleyData(string trolleyId, TrolleySuspension suspension, float simulationPositionMeters,
            float targetSpeedMetersPerSecond)
        {
            TrolleyId = trolleyId;
            Suspension = suspension;
            TrolleyType = suspension.TrolleyType;
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

        /// <summary>Gets the internal mathematical position in meters along the closed track.</summary>
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
        internal float InitialPositionMeters { get; set; }
        internal bool IsEmergencyStopped { get; set; }
        internal bool IsDriveFaulted { get; set; }
    }
}