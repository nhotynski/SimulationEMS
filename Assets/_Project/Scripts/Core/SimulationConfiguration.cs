using UnityEngine;

namespace IndustrialConveyor.Simulation.Core
{
    /// <summary>Defines shared timing settings for deterministic simulation modules.</summary>
    [CreateAssetMenu(menuName = "Industrial Conveyor/Simulation Configuration", fileName = "SimulationConfiguration")]
    public sealed class SimulationConfiguration : ScriptableObject
    {
        [SerializeField, Min(0.001f)] private float fixedTimeStepSeconds = 0.02f;
        [SerializeField, Min(0.01f)] private float speedMultiplier = 1f;
        [SerializeField] private bool startPaused;

        /// <summary>Gets the fixed simulation step in seconds.</summary>
        public float FixedTimeStepSeconds => fixedTimeStepSeconds;

        /// <summary>Gets the real-time speed multiplier for the simulation clock.</summary>
        public float SpeedMultiplier => speedMultiplier;

        /// <summary>Gets whether a simulation should begin paused.</summary>
        public bool StartPaused => startPaused;
    }
}