using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndustrialConveyor.Simulation.Track
{
    /// <summary>Builds exact straight and circular-arc geometry from ordered segment definitions.</summary>
    public static class TrackGeometryBuilder
    {
        private const float IntersectionEpsilon = 0.0001f;
        private const int MaximumIntersectionEdges = 10000;

        /// <summary>Builds path data and validation diagnostics from a configuration asset.</summary>
        public static TrackBuildResult Build(TrackConfiguration configuration)
        {
            if (configuration == null)
            {
                var emptyReport = new TrackValidationReport();
                emptyReport.Add(TrackIssueSeverity.Error, "CONFIGURATION_MISSING", "Track configuration is missing.");
                emptyReport.IsValid = false;
                return new TrackBuildResult(new TrackPath(Array.Empty<TrackSegmentGeometry>(), Vector3.zero,
                    Vector3.forward, 0f, false), emptyReport);
            }

            var settings = new TrackBuildSettings(configuration.TargetLengthMeters, configuration.RequireClosedLoop,
                configuration.ClosurePositionToleranceMeters, configuration.ClosureHeadingToleranceDegrees,
                configuration.TargetLengthToleranceMeters, configuration.IntersectionSampleSpacingMeters);
            return Build(configuration.StartPosition, configuration.InitialHeadingDegrees, configuration.Segments, settings);
        }

        /// <summary>Builds path data from values, allowing deterministic use outside Unity assets.</summary>
        public static TrackBuildResult Build(Vector3 startPosition, float initialHeadingDegrees,
            IReadOnlyList<TrackSegmentDefinition> definitions, TrackBuildSettings settings)
        {
            var report = new TrackValidationReport();
            var resolved = new List<TrackSegmentGeometry>();
            var currentPosition = startPosition;
            var currentTangent = Quaternion.AngleAxis(initialHeadingDegrees, Vector3.up) * Vector3.forward;
            var cumulativeDistance = 0f;
            var allSegmentsValid = true;

            if (!IsFinite(startPosition) || !IsFinite(initialHeadingDegrees))
            {
                report.Add(TrackIssueSeverity.Error, "START_FRAME_INVALID", "Track start position and heading must be finite values.");
                allSegmentsValid = false;
                currentPosition = Vector3.zero;
                currentTangent = Vector3.forward;
            }

            if (definitions == null || definitions.Count == 0)
            {
                report.Add(TrackIssueSeverity.Error, "SEGMENTS_EMPTY", "Add at least one track segment.");
                allSegmentsValid = false;
            }

            if (definitions != null)
            {
                for (var i = 0; i < definitions.Count; i++)
                {
                    var definition = definitions[i];
                    if (definition == null)
                    {
                        report.Add(TrackIssueSeverity.Error, "SEGMENT_MISSING", $"Segment {i + 1} is null.");
                        allSegmentsValid = false;
                        continue;
                    }

                    if (!TryResolveSegment(i, definition, currentPosition, currentTangent,
                        cumulativeDistance, out var segment, out var errorCode, out var errorMessage))
                    {
                        report.Add(TrackIssueSeverity.Error, errorCode, $"Segment {i + 1} ({definition.Label}): {errorMessage}");
                        allSegmentsValid = false;
                        continue;
                    }

                    if (resolved.Count > 0)
                    {
                        var previous = resolved[resolved.Count - 1];
                        var positionGap = Vector3.Distance(previous.EndPosition, segment.StartPosition);
                        var tangentGap = Vector3.Angle(previous.EndTangent, segment.StartTangent);
                        if (positionGap > settings.ClosurePositionToleranceMeters)
                        {
                            report.Add(TrackIssueSeverity.Error, "SEGMENT_POSITION_DISCONTINUITY",
                                $"Connection before segment {i + 1} has a {positionGap:F3} m position gap.");
                            allSegmentsValid = false;
                        }
                        if (tangentGap > settings.ClosureHeadingToleranceDegrees)
                        {
                            report.Add(TrackIssueSeverity.Error, "SEGMENT_TANGENT_DISCONTINUITY",
                                $"Connection before segment {i + 1} has a {tangentGap:F3} degree tangent gap.");
                            allSegmentsValid = false;
                        }
                    }

                    resolved.Add(segment);
                    currentPosition = segment.EndPosition;
                    currentTangent = segment.EndTangent;
                    cumulativeDistance += segment.LengthMeters;
                }
            }

            report.ActualLengthMeters = cumulativeDistance;
            report.TargetLengthDifferenceMeters = cumulativeDistance - settings.TargetLengthMeters;
            if (!IsFinite(settings.TargetLengthMeters) || settings.TargetLengthMeters <= 0f)
            {
                report.Add(TrackIssueSeverity.Error, "TARGET_LENGTH_INVALID", "Target length must be greater than zero.");
                allSegmentsValid = false;
            }
            else if (Mathf.Abs(report.TargetLengthDifferenceMeters) > settings.TargetLengthToleranceMeters)
            {
                report.Add(TrackIssueSeverity.Warning, "TARGET_LENGTH_MISMATCH",
                    $"Generated length is {cumulativeDistance:F3} m; target is {settings.TargetLengthMeters:F3} m " +
                    $"(difference {report.TargetLengthDifferenceMeters:+0.000;-0.000;0.000} m).");
            }

            if (!IsFinite(settings.ClosurePositionToleranceMeters) || settings.ClosurePositionToleranceMeters < 0f ||
                !IsFinite(settings.ClosureHeadingToleranceDegrees) || settings.ClosureHeadingToleranceDegrees < 0f ||
                !IsFinite(settings.TargetLengthToleranceMeters) || settings.TargetLengthToleranceMeters < 0f)
            {
                report.Add(TrackIssueSeverity.Error, "VALIDATION_TOLERANCE_INVALID", "Validation tolerances must be finite and non-negative.");
                allSegmentsValid = false;
            }
            if (!IsFinite(settings.IntersectionSampleSpacingMeters) || settings.IntersectionSampleSpacingMeters <= 0f)
            {
                report.Add(TrackIssueSeverity.Error, "INTERSECTION_SPACING_INVALID", "Intersection sample spacing must be greater than zero.");
                allSegmentsValid = false;
            }

            var closed = false;
            if (resolved.Count > 0 && allSegmentsValid)
            {
                var first = resolved[0];
                var last = resolved[resolved.Count - 1];
                report.ClosurePositionErrorMeters = Vector3.Distance(last.EndPosition, first.StartPosition);
                report.ClosureHeadingErrorDegrees = Vector3.Angle(last.EndTangent, first.StartTangent);
                closed = report.ClosurePositionErrorMeters <= settings.ClosurePositionToleranceMeters &&
                         report.ClosureHeadingErrorDegrees <= settings.ClosureHeadingToleranceDegrees;
                report.IsClosed = closed;

                if (!closed && settings.RequireClosedLoop)
                {
                    report.Add(TrackIssueSeverity.Error, "TRACK_OPEN",
                        $"Track does not close: position gap {report.ClosurePositionErrorMeters:F3} m, " +
                        $"heading gap {report.ClosureHeadingErrorDegrees:F3} degrees.");
                }
                else if (closed)
                {
                    report.Add(TrackIssueSeverity.Information, "TRACK_CLOSED", "Track position and heading close within tolerance.");
                }

                var intersects = TryFindSelfIntersection(resolved, settings.IntersectionSampleSpacingMeters,
                    closed, out var intersectionMessage);
                if (intersects)
                {
                    report.Add(TrackIssueSeverity.Error, "TRACK_SELF_INTERSECTION", intersectionMessage);
                }
                else if (!string.IsNullOrEmpty(intersectionMessage))
                {
                    report.Add(TrackIssueSeverity.Warning, "TRACK_INTERSECTION_CHECK_SKIPPED", intersectionMessage);
                }
            }
            else if (resolved.Count > 0 && settings.RequireClosedLoop)
            {
                report.Add(TrackIssueSeverity.Error, "TRACK_CLOSURE_UNAVAILABLE",
                    "Loop closure cannot be validated until every segment has valid dimensions.");
            }
            else if (resolved.Count > 0)
            {
                report.Add(TrackIssueSeverity.Information, "TRACK_OPEN_ALLOWED", "Open track preview is allowed by this configuration.");
            }

            report.IsValid = true;
            foreach (var issue in report.Issues)
            {
                if (issue.Severity == TrackIssueSeverity.Error)
                {
                    report.IsValid = false;
                    break;
                }
            }

            var pathStartPosition = resolved.Count > 0 ? resolved[0].StartPosition : currentPosition;
            var pathStartTangent = resolved.Count > 0 ? resolved[0].StartTangent : currentTangent;
            var path = new TrackPath(resolved, pathStartPosition, pathStartTangent, cumulativeDistance, closed);
            return new TrackBuildResult(path, report);
        }

        private static bool TryResolveSegment(int index, TrackSegmentDefinition definition, Vector3 startPosition,
            Vector3 startTangent, float startDistanceMeters, out TrackSegmentGeometry segment,
            out string errorCode, out string errorMessage)
        {
            segment = default;
            errorCode = "SEGMENT_INVALID";
            errorMessage = "Segment dimensions are invalid.";

            if (definition.Type == TrackSegmentType.Straight)
            {
                var length = definition.StraightLengthMeters;
                if (!IsFinite(length) || length <= 0f)
                {
                    errorCode = "STRAIGHT_LENGTH_INVALID";
                    errorMessage = "Straight length must be a finite value greater than zero.";
                    return false;
                }

                var endPosition = startPosition + startTangent * length;
                segment = new TrackSegmentGeometry(index, definition.Label, definition.Type,
                    definition.TurnDirection, startPosition, endPosition, startTangent, startTangent,
                    Vector3.zero, startDistanceMeters, length, 0f, 0f);
                return true;
            }

            if (definition.Type != TrackSegmentType.Curve)
            {
                errorMessage = "Segment type is not supported.";
                return false;
            }

            var radius = definition.RadiusMeters;
            var angleDegrees = definition.AngleDegrees;
            if (!IsFinite(radius) || radius <= 0f)
            {
                errorCode = "CURVE_RADIUS_INVALID";
                errorMessage = "Curve radius must be a finite value greater than zero.";
                return false;
            }
            if (!IsFinite(angleDegrees) || angleDegrees <= 0f || angleDegrees > 360f)
            {
                errorCode = "CURVE_ANGLE_INVALID";
                errorMessage = "Curve angle must be greater than zero and no more than 360 degrees.";
                return false;
            }

            var turnSign = definition.TurnDirection == TrackTurnDirection.Right ? 1f :
                definition.TurnDirection == TrackTurnDirection.Left ? -1f : 0f;
            if (turnSign == 0f)
            {
                errorCode = "CURVE_DIRECTION_INVALID";
                errorMessage = "Curve direction must be Left or Right.";
                return false;
            }

            var right = Vector3.Cross(Vector3.up, startTangent).normalized;
            var arcCenter = startPosition + right * (turnSign * radius);
            var signedAngleRadians = turnSign * angleDegrees * Mathf.Deg2Rad;
            var arcRotation = Quaternion.AngleAxis(signedAngleRadians * Mathf.Rad2Deg, Vector3.up);
            var end = arcCenter + arcRotation * (startPosition - arcCenter);
            var endTangent = (arcRotation * startTangent).normalized;
            var arcLength = radius * Mathf.Abs(signedAngleRadians);

            segment = new TrackSegmentGeometry(index, definition.Label, definition.Type,
                definition.TurnDirection, startPosition, end, startTangent, endTangent,
                arcCenter, startDistanceMeters, arcLength, radius, signedAngleRadians);
            return true;
        }

        private static bool TryFindSelfIntersection(IReadOnlyList<TrackSegmentGeometry> segments,
            float sampleSpacingMeters, bool isClosed, out string message)
        {
            message = null;
            if (!IsFinite(sampleSpacingMeters) || sampleSpacingMeters <= 0f)
            {
                message = "Self-intersection check was skipped because sample spacing is invalid.";
                return false;
            }

            var edges = new List<PolylineEdge>();
            foreach (var segment in segments)
            {
                var steps = Mathf.Max(1, Mathf.CeilToInt(segment.LengthMeters / sampleSpacingMeters));
                if (edges.Count + steps > MaximumIntersectionEdges)
                {
                    message = $"Self-intersection check was skipped because sampling exceeds {MaximumIntersectionEdges} edges.";
                    return false;
                }

                var previous = segment.SampleAt(0f).Position;
                for (var step = 1; step <= steps; step++)
                {
                    var distance = segment.LengthMeters * step / steps;
                    var current = segment.SampleAt(distance).Position;
                    var edgeDistance = segment.StartDistanceMeters + segment.LengthMeters * (step - 1) / steps;
                    edges.Add(new PolylineEdge(previous, current, edgeDistance));
                    previous = current;
                }
            }

            for (var first = 0; first < edges.Count; first++)
            {
                for (var second = first + 2; second < edges.Count; second++)
                {
                    if (isClosed && first == 0 && second == edges.Count - 1)
                        continue;
                    if (Intersects(edges[first], edges[second]))
                    {
                        message = $"Track crosses itself near path distances {edges[first].DistanceMeters:F1} m and " +
                                  $"{edges[second].DistanceMeters:F1} m.";
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool Intersects(PolylineEdge first, PolylineEdge second)
        {
            if (Mathf.Max(first.A.x, first.B.x) + IntersectionEpsilon < Mathf.Min(second.A.x, second.B.x) ||
                Mathf.Max(second.A.x, second.B.x) + IntersectionEpsilon < Mathf.Min(first.A.x, first.B.x) ||
                Mathf.Max(first.A.z, first.B.z) + IntersectionEpsilon < Mathf.Min(second.A.z, second.B.z) ||
                Mathf.Max(second.A.z, second.B.z) + IntersectionEpsilon < Mathf.Min(first.A.z, first.B.z))
                return false;

            var a = Orientation(first.A, first.B, second.A);
            var b = Orientation(first.A, first.B, second.B);
            var c = Orientation(second.A, second.B, first.A);
            var d = Orientation(second.A, second.B, first.B);
            if (((a > IntersectionEpsilon && b < -IntersectionEpsilon) || (a < -IntersectionEpsilon && b > IntersectionEpsilon)) &&
                ((c > IntersectionEpsilon && d < -IntersectionEpsilon) || (c < -IntersectionEpsilon && d > IntersectionEpsilon)))
                return true;

            return Mathf.Abs(a) <= IntersectionEpsilon && OnSegment(first.A, second.A, first.B) ||
                   Mathf.Abs(b) <= IntersectionEpsilon && OnSegment(first.A, second.B, first.B) ||
                   Mathf.Abs(c) <= IntersectionEpsilon && OnSegment(second.A, first.A, second.B) ||
                   Mathf.Abs(d) <= IntersectionEpsilon && OnSegment(second.A, first.B, second.B);
        }

        private static bool OnSegment(Vector3 start, Vector3 point, Vector3 end)
        {
            return point.x >= Mathf.Min(start.x, end.x) - IntersectionEpsilon &&
                   point.x <= Mathf.Max(start.x, end.x) + IntersectionEpsilon &&
                   point.z >= Mathf.Min(start.z, end.z) - IntersectionEpsilon &&
                   point.z <= Mathf.Max(start.z, end.z) + IntersectionEpsilon;
        }

        private static float Orientation(Vector3 a, Vector3 b, Vector3 c)
        {
            return (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
        }

        private readonly struct PolylineEdge
        {
            public PolylineEdge(Vector3 a, Vector3 b, float distanceMeters)
            {
                A = a;
                B = b;
                DistanceMeters = distanceMeters;
            }

            public Vector3 A { get; }
            public Vector3 B { get; }
            public float DistanceMeters { get; }
        }
    }
}