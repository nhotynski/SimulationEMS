using System.Collections.Generic;

namespace IndustrialConveyor.Simulation.Track
{
    /// <summary>Classifies a track validation result.</summary>
    public enum TrackIssueSeverity
    {
        Information,
        Warning,
        Error
    }

    /// <summary>One actionable track validation message.</summary>
    public sealed class TrackValidationIssue
    {
        internal TrackValidationIssue(TrackIssueSeverity severity, string code, string message)
        {
            Severity = severity;
            Code = code;
            Message = message;
        }

        /// <summary>Gets the issue severity.</summary>
        public TrackIssueSeverity Severity { get; }

        /// <summary>Gets a stable machine-readable issue code.</summary>
        public string Code { get; }

        /// <summary>Gets the user-facing explanation.</summary>
        public string Message { get; }
    }

    /// <summary>Collects geometry, loop-closure, and target-length diagnostics.</summary>
    public sealed class TrackValidationReport
    {
        private readonly List<TrackValidationIssue> issues = new List<TrackValidationIssue>();

        /// <summary>Gets all validation messages in build order.</summary>
        public IReadOnlyList<TrackValidationIssue> Issues => issues;

        /// <summary>Gets whether the generated path has no error-severity issues.</summary>
        public bool IsValid { get; internal set; }

        /// <summary>Gets whether position and heading satisfy the configured closure tolerances.</summary>
        public bool IsClosed { get; internal set; }

        /// <summary>Gets the actual generated length in meters.</summary>
        public float ActualLengthMeters { get; internal set; }

        /// <summary>Gets actual length minus target length in meters.</summary>
        public float TargetLengthDifferenceMeters { get; internal set; }

        /// <summary>Gets the distance between final and initial points in meters.</summary>
        public float ClosurePositionErrorMeters { get; internal set; }

        /// <summary>Gets the angle between final and initial tangents in degrees.</summary>
        public float ClosureHeadingErrorDegrees { get; internal set; }

        internal void Add(TrackIssueSeverity severity, string code, string message)
        {
            issues.Add(new TrackValidationIssue(severity, code, message));
        }
    }

    /// <summary>Contains the calculated path and its diagnostics.</summary>
    public sealed class TrackBuildResult
    {
        internal TrackBuildResult(TrackPath path, TrackValidationReport report)
        {
            Path = path;
            Report = report;
        }

        /// <summary>Gets the generated mathematical track path.</summary>
        public TrackPath Path { get; }

        /// <summary>Gets the validation report for the path.</summary>
        public TrackValidationReport Report { get; }
    }

    /// <summary>Groups numerical tolerances and target settings used by the geometry builder.</summary>
    public readonly struct TrackBuildSettings
    {
        /// <summary>Creates validation settings for one build.</summary>
        public TrackBuildSettings(float targetLengthMeters, bool requireClosedLoop,
            float closurePositionToleranceMeters, float closureHeadingToleranceDegrees,
            float targetLengthToleranceMeters, float intersectionSampleSpacingMeters)
        {
            TargetLengthMeters = targetLengthMeters;
            RequireClosedLoop = requireClosedLoop;
            ClosurePositionToleranceMeters = closurePositionToleranceMeters;
            ClosureHeadingToleranceDegrees = closureHeadingToleranceDegrees;
            TargetLengthToleranceMeters = targetLengthToleranceMeters;
            IntersectionSampleSpacingMeters = intersectionSampleSpacingMeters;
        }

        /// <summary>Gets the intended total length in meters.</summary>
        public float TargetLengthMeters { get; }

        /// <summary>Gets whether an open path is an error.</summary>
        public bool RequireClosedLoop { get; }

        /// <summary>Gets the position closure tolerance in meters.</summary>
        public float ClosurePositionToleranceMeters { get; }

        /// <summary>Gets the heading closure tolerance in degrees.</summary>
        public float ClosureHeadingToleranceDegrees { get; }

        /// <summary>Gets the target-length tolerance in meters.</summary>
        public float TargetLengthToleranceMeters { get; }

        /// <summary>Gets the sample spacing used for self-intersection checks.</summary>
        public float IntersectionSampleSpacingMeters { get; }
    }
}