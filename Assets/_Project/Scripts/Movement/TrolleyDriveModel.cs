using System;
using UnityEngine;

namespace IndustrialConveyor.Simulation.Movement
{
    /// <summary>Pure kinematic and spacing calculations for independently driven trolleys.</summary>
    public static class TrolleyDriveModel
    {
        /// <summary>Moves current speed toward a bounded target using the drive's acceleration or deceleration.</summary>
        public static float AdvanceSpeed(float currentSpeedMetersPerSecond, float targetSpeedMetersPerSecond,
            float maximumSpeedMetersPerSecond, float accelerationMetersPerSecondSquared,
            float decelerationMetersPerSecondSquared, float deltaTimeSeconds)
        {
            RequireFiniteNonNegative(currentSpeedMetersPerSecond, nameof(currentSpeedMetersPerSecond));
            RequireFiniteNonNegative(targetSpeedMetersPerSecond, nameof(targetSpeedMetersPerSecond));
            RequireFinitePositive(maximumSpeedMetersPerSecond, nameof(maximumSpeedMetersPerSecond));
            RequireFinitePositive(accelerationMetersPerSecondSquared, nameof(accelerationMetersPerSecondSquared));
            RequireFinitePositive(decelerationMetersPerSecondSquared, nameof(decelerationMetersPerSecondSquared));
            RequireFinitePositive(deltaTimeSeconds, nameof(deltaTimeSeconds));

            var current = Mathf.Min(currentSpeedMetersPerSecond, maximumSpeedMetersPerSecond);
            var target = Mathf.Min(targetSpeedMetersPerSecond, maximumSpeedMetersPerSecond);
            var rate = target >= current ? accelerationMetersPerSecondSquared : decelerationMetersPerSecondSquared;
            return Mathf.MoveTowards(current, target, rate * deltaTimeSeconds);
        }

        /// <summary>Calculates the greatest follower speed that can stop behind a leader with the available clearance.</summary>
        public static float CalculateMaximumSafeSpeed(float leaderSpeedMetersPerSecond,
            float followerDecelerationMetersPerSecondSquared, float availableClearanceMeters)
        {
            RequireFiniteNonNegative(leaderSpeedMetersPerSecond, nameof(leaderSpeedMetersPerSecond));
            RequireFinitePositive(followerDecelerationMetersPerSecondSquared,
                nameof(followerDecelerationMetersPerSecondSquared));
            RequireFiniteNonNegative(availableClearanceMeters, nameof(availableClearanceMeters));

            var stoppingSpeedSquared = leaderSpeedMetersPerSecond * leaderSpeedMetersPerSecond +
                                       2f * followerDecelerationMetersPerSecondSquared * availableClearanceMeters;
            return Mathf.Sqrt(stoppingSpeedSquared);
        }

        /// <summary>Calculates free distance between unequal-length trolley bodies across a closed-loop boundary.</summary>
        public static float CalculateAvailableClearance(float followerPositionMeters, float followerLengthMeters,
            float leaderPositionMeters, float leaderLengthMeters, float trackLengthMeters, float minimumGapMeters)
        {
            RequireFiniteNonNegative(followerPositionMeters, nameof(followerPositionMeters));
            RequireFiniteNonNegative(leaderPositionMeters, nameof(leaderPositionMeters));
            RequireFinitePositive(followerLengthMeters, nameof(followerLengthMeters));
            RequireFinitePositive(leaderLengthMeters, nameof(leaderLengthMeters));
            RequireFinitePositive(trackLengthMeters, nameof(trackLengthMeters));
            RequireFiniteNonNegative(minimumGapMeters, nameof(minimumGapMeters));

            var centerDistance = Mathf.Repeat(leaderPositionMeters - followerPositionMeters, trackLengthMeters);
            return centerDistance - (followerLengthMeters + leaderLengthMeters) * 0.5f - minimumGapMeters;
        }

        private static void RequireFiniteNonNegative(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and non-negative.");
        }

        private static void RequireFinitePositive(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(parameterName, "Value must be finite and greater than zero.");
        }
    }
}