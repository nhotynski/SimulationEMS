using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndustrialConveyor.Simulation.Track
{
    /// <summary>A sampled point and Frenet frame on the track.</summary>
    public readonly struct TrackSample
    {
        internal TrackSample(float distanceMeters, int segmentIndex, Vector3 position, Vector3 tangent)
        {
            DistanceMeters = distanceMeters;
            SegmentIndex = segmentIndex;
            Position = position;
            Tangent = tangent.normalized;
            Normal = Vector3.up;
            Right = Vector3.Cross(Normal, Tangent).normalized;
            Rotation = Quaternion.LookRotation(Tangent, Normal);
        }

        /// <summary>Gets the distance along the path in meters.</summary>
        public float DistanceMeters { get; }

        /// <summary>Gets the source segment index.</summary>
        public int SegmentIndex { get; }

        /// <summary>Gets the local-space position in meters.</summary>
        public Vector3 Position { get; }

        /// <summary>Gets the unit forward tangent.</summary>
        public Vector3 Tangent { get; }

        /// <summary>Gets the track plane normal, which is local up for Phase 1.</summary>
        public Vector3 Normal { get; }

        /// <summary>Gets the unit lateral direction to the right of travel.</summary>
        public Vector3 Right { get; }

        /// <summary>Gets a rotation whose forward axis follows the track tangent.</summary>
        public Quaternion Rotation { get; }
    }

    /// <summary>Resolved geometric data for one source segment.</summary>
    public readonly struct TrackSegmentGeometry
    {
        internal TrackSegmentGeometry(int index, string label, TrackSegmentType type,
            TrackTurnDirection turnDirection, Vector3 startPosition, Vector3 endPosition,
            Vector3 startTangent, Vector3 endTangent, Vector3 arcCenter,
            float startDistanceMeters, float lengthMeters, float radiusMeters, float signedAngleRadians)
        {
            Index = index;
            Label = label;
            Type = type;
            TurnDirection = turnDirection;
            StartPosition = startPosition;
            EndPosition = endPosition;
            StartTangent = startTangent;
            EndTangent = endTangent;
            ArcCenter = arcCenter;
            StartDistanceMeters = startDistanceMeters;
            LengthMeters = lengthMeters;
            RadiusMeters = radiusMeters;
            SignedAngleRadians = signedAngleRadians;
        }

        /// <summary>Gets the segment's original zero-based list index.</summary>
        public int Index { get; }

        /// <summary>Gets the segment label.</summary>
        public string Label { get; }

        /// <summary>Gets the segment type.</summary>
        public TrackSegmentType Type { get; }

        /// <summary>Gets the curve direction.</summary>
        public TrackTurnDirection TurnDirection { get; }

        /// <summary>Gets the calculated start point in local meters.</summary>
        public Vector3 StartPosition { get; }

        /// <summary>Gets the calculated end point in local meters.</summary>
        public Vector3 EndPosition { get; }

        /// <summary>Gets the incoming unit tangent.</summary>
        public Vector3 StartTangent { get; }

        /// <summary>Gets the outgoing unit tangent.</summary>
        public Vector3 EndTangent { get; }

        /// <summary>Gets the circle center for a curve.</summary>
        public Vector3 ArcCenter { get; }

        /// <summary>Gets the cumulative distance at the segment start.</summary>
        public float StartDistanceMeters { get; }

        /// <summary>Gets the exact segment length in meters.</summary>
        public float LengthMeters { get; }

        /// <summary>Gets the curve radius in meters, or zero for a straight.</summary>
        public float RadiusMeters { get; }

        /// <summary>Gets the absolute curve angle in degrees, or zero for a straight.</summary>
        public float AngleDegrees => Mathf.Abs(SignedAngleRadians) * Mathf.Rad2Deg;

        internal float SignedAngleRadians { get; }

        internal TrackSample SampleAt(float localDistanceMeters)
        {
            var localDistance = Mathf.Clamp(localDistanceMeters, 0f, LengthMeters);
            if (Type == TrackSegmentType.Straight)
                return new TrackSample(StartDistanceMeters + localDistance, Index,
                    StartPosition + StartTangent * localDistance, StartTangent);

            var angleDegrees = SignedAngleRadians * localDistance / LengthMeters * Mathf.Rad2Deg;
            var rotation = Quaternion.AngleAxis(angleDegrees, Vector3.up);
            return new TrackSample(StartDistanceMeters + localDistance, Index,
                ArcCenter + rotation * (StartPosition - ArcCenter), rotation * StartTangent);
        }
    }

    /// <summary>Immutable, queryable path data used by visuals and future trolley/barcode modules.</summary>
    public sealed class TrackPath
    {
        private readonly TrackSegmentGeometry[] segments;
        private readonly IReadOnlyList<TrackSegmentGeometry> readOnlySegments;

        internal TrackPath(IReadOnlyList<TrackSegmentGeometry> sourceSegments, Vector3 startPosition,
            Vector3 startTangent, float totalLengthMeters, bool isClosed)
        {
            segments = new TrackSegmentGeometry[sourceSegments.Count];
            for (var i = 0; i < sourceSegments.Count; i++)
                segments[i] = sourceSegments[i];
            readOnlySegments = Array.AsReadOnly(segments);
            StartPosition = startPosition;
            StartTangent = startTangent;
            TotalLengthMeters = totalLengthMeters;
            IsClosed = isClosed;
        }

        /// <summary>Gets resolved segments in source order.</summary>
        public IReadOnlyList<TrackSegmentGeometry> Segments => readOnlySegments;

        /// <summary>Gets the path origin in local meters.</summary>
        public Vector3 StartPosition { get; }

        /// <summary>Gets the initial unit tangent.</summary>
        public Vector3 StartTangent { get; }

        /// <summary>Gets the exact cumulative path length in meters.</summary>
        public float TotalLengthMeters { get; }

        /// <summary>Gets whether the path satisfies its closure tolerances.</summary>
        public bool IsClosed { get; }

        /// <summary>Wraps a distance into one loop. Open paths are rejected rather than silently wrapped.</summary>
        public float WrapDistance(float distanceMeters)
        {
            if (!IsClosed)
                throw new InvalidOperationException("Only a validated closed track can wrap distance.");
            if (!IsFinite(distanceMeters))
                throw new ArgumentOutOfRangeException(nameof(distanceMeters));
            return Mathf.Repeat(distanceMeters, TotalLengthMeters);
        }

        /// <summary>Evaluates position, tangent, normal, and rotation at a track distance.</summary>
        public TrackSample SampleAtDistance(float distanceMeters, bool wrapDistance = false)
        {
            if (segments.Length == 0)
                throw new InvalidOperationException("An empty path cannot be sampled.");
            if (!IsFinite(distanceMeters))
                throw new ArgumentOutOfRangeException(nameof(distanceMeters));

            var distance = wrapDistance ? WrapDistance(distanceMeters) : Mathf.Clamp(distanceMeters, 0f, TotalLengthMeters);
            var low = 0;
            var high = segments.Length - 1;
            while (low < high)
            {
                var middle = (low + high) / 2;
                if (distance <= segments[middle].StartDistanceMeters + segments[middle].LengthMeters)
                    high = middle;
                else
                    low = middle + 1;
            }

            var segment = segments[low];
            return segment.SampleAt(distance - segment.StartDistanceMeters);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}