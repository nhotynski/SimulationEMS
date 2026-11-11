using System;
using UnityEngine;

namespace IndustrialConveyor.Simulation.Track
{
    /// <summary>Describes the supported parametric segment shapes.</summary>
    public enum TrackSegmentType
    {
        Straight,
        Curve
    }

    /// <summary>Describes a curve's turn direction relative to its incoming tangent.</summary>
    public enum TrackTurnDirection
    {
        Left,
        Right
    }

    /// <summary>Inspector-editable dimensions for one straight or circular-arc segment.</summary>
    [Serializable]
    public sealed class TrackSegmentDefinition
    {
        [SerializeField] private string label = "Segment";
        [SerializeField] private TrackSegmentType type;
        [SerializeField, Min(0f)] private float straightLengthMeters = 10f;
        [SerializeField] private TrackTurnDirection turnDirection = TrackTurnDirection.Right;
        [SerializeField, Min(0f)] private float angleDegrees = 90f;
        [SerializeField, Min(0f)] private float radiusMeters = 5f;

        /// <summary>Gets the human-readable segment label.</summary>
        public string Label => label;

        /// <summary>Gets the geometric segment type.</summary>
        public TrackSegmentType Type => type;

        /// <summary>Gets the straight length in meters.</summary>
        public float StraightLengthMeters => straightLengthMeters;

        /// <summary>Gets the curve turn direction.</summary>
        public TrackTurnDirection TurnDirection => turnDirection;

        /// <summary>Gets the absolute curve angle in degrees.</summary>
        public float AngleDegrees => angleDegrees;

        /// <summary>Gets the curve radius in meters.</summary>
        public float RadiusMeters => radiusMeters;

        /// <summary>Creates a straight segment definition.</summary>
        public static TrackSegmentDefinition Straight(string label, float lengthMeters)
        {
            return new TrackSegmentDefinition
            {
                label = label,
                type = TrackSegmentType.Straight,
                straightLengthMeters = lengthMeters
            };
        }

        /// <summary>Creates a circular-arc segment definition.</summary>
        public static TrackSegmentDefinition Curve(string label, TrackTurnDirection direction, float angleDegrees, float radiusMeters)
        {
            return new TrackSegmentDefinition
            {
                label = label,
                type = TrackSegmentType.Curve,
                turnDirection = direction,
                angleDegrees = angleDegrees,
                radiusMeters = radiusMeters
            };
        }
    }
}