using System.Collections.Generic;
using UnityEngine;

namespace IndustrialConveyor.Simulation.Track
{
    /// <summary>Stores an ordered parametric track and its closure-validation settings.</summary>
    [CreateAssetMenu(menuName = "Industrial Conveyor/Track Configuration", fileName = "TrackConfiguration")]
    public sealed class TrackConfiguration : ScriptableObject
    {
        [SerializeField] private Vector3 startPosition;
        [SerializeField] private float initialHeadingDegrees;
        [SerializeField, Min(0f)] private float targetLengthMeters = 1000f;
        [SerializeField] private bool requireClosedLoop = true;
        [SerializeField, Min(0f)] private float closurePositionToleranceMeters = 0.01f;
        [SerializeField, Min(0f)] private float closureHeadingToleranceDegrees = 0.1f;
        [SerializeField, Min(0f)] private float targetLengthToleranceMeters = 0.1f;
        [SerializeField, Min(0f)] private float intersectionSampleSpacingMeters = 2f;
        [SerializeField] private List<TrackSegmentDefinition> segments = new List<TrackSegmentDefinition>();

        /// <summary>Gets the starting point in the track's local coordinate frame.</summary>
        public Vector3 StartPosition => startPosition;

        /// <summary>Gets the starting heading clockwise from local positive Z, in degrees.</summary>
        public float InitialHeadingDegrees => initialHeadingDegrees;

        /// <summary>Gets the target track length in meters.</summary>
        public float TargetLengthMeters => targetLengthMeters;

        /// <summary>Gets whether a closed loop is required for this configuration.</summary>
        public bool RequireClosedLoop => requireClosedLoop;

        /// <summary>Gets the allowed start-to-end position gap in meters.</summary>
        public float ClosurePositionToleranceMeters => closurePositionToleranceMeters;

        /// <summary>Gets the allowed start-to-end heading gap in degrees.</summary>
        public float ClosureHeadingToleranceDegrees => closureHeadingToleranceDegrees;

        /// <summary>Gets the allowed target-length difference in meters.</summary>
        public float TargetLengthToleranceMeters => targetLengthToleranceMeters;

        /// <summary>Gets the maximum spacing used by sampled self-intersection detection.</summary>
        public float IntersectionSampleSpacingMeters => intersectionSampleSpacingMeters;

        /// <summary>Gets the ordered segment definitions.</summary>
        public IReadOnlyList<TrackSegmentDefinition> Segments => segments;
    }
}