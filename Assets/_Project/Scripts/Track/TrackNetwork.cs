using System;
using System.Collections.Generic;
using UnityEngine;

namespace IndustrialConveyor.Simulation.Track
{
    /// <summary>Distinguishes the two kinds of switch node in a track network.</summary>
    public enum TrackNodeKind
    {
        /// <summary>One incoming edge, several outgoing edges; the active one is selectable.</summary>
        Split,

        /// <summary>Several incoming edges merging into one outgoing edge.</summary>
        Merge
    }

    /// <summary>A position on a track edge.</summary>
    public readonly struct TrackLocation
    {
        public TrackLocation(TrackEdge edge, float meters)
        {
            Edge = edge;
            Meters = meters;
        }

        /// <summary>Gets the edge.</summary>
        public TrackEdge Edge { get; }

        /// <summary>Gets the distance from the edge start in meters.</summary>
        public float Meters { get; }
    }

    /// <summary>A switch point where edges split or merge.</summary>
    public sealed class TrackNode
    {
        private readonly List<TrackEdge> inEdges = new List<TrackEdge>();
        private readonly List<TrackEdge> outEdges = new List<TrackEdge>();

        internal TrackNode(int index, string name, TrackNodeKind kind, Vector3 position, float zoneMeters)
        {
            Index = index;
            Name = name;
            Kind = kind;
            Position = position;
            ZoneMeters = zoneMeters;
            RequestedOutIndex = -1;
            PriorityInIndex = 0;
        }

        /// <summary>Gets the node's index in <see cref="TrackNetwork.Nodes"/>.</summary>
        public int Index { get; }

        /// <summary>Gets the switch element name from the layout.</summary>
        public string Name { get; }

        /// <summary>Gets whether trolleys diverge or converge here.</summary>
        public TrackNodeKind Kind { get; }

        /// <summary>Gets the node position: the entry of a split, the exit of a merge.</summary>
        public Vector3 Position { get; }

        /// <summary>Gets the length before/after the node where branches physically converge.</summary>
        public float ZoneMeters { get; }

        /// <summary>Gets the edges arriving at the node.</summary>
        public IReadOnlyList<TrackEdge> InEdges => inEdges;

        /// <summary>Gets the edges leaving the node.</summary>
        public IReadOnlyList<TrackEdge> OutEdges => outEdges;

        /// <summary>Gets the selected outgoing edge index of a split (always 0 for a merge).</summary>
        public int SelectedOutIndex { get; private set; }

        /// <summary>Gets a switch change that waits for the switch to become free, or -1.</summary>
        public int RequestedOutIndex { get; private set; }

        /// <summary>Gets the incoming edge index that has priority at a merge.</summary>
        public int PriorityInIndex { get; internal set; }

        /// <summary>Gets whether the switch is held because a trolley occupies it.</summary>
        public bool IsLocked { get; internal set; }

        /// <summary>Gets the outgoing edge trolleys currently take.</summary>
        public TrackEdge ActiveOutEdge => outEdges.Count == 0 ? null : outEdges[Kind == TrackNodeKind.Split ? SelectedOutIndex : 0];

        /// <summary>Gets a short label for each outgoing edge, for example "Q0 branch".</summary>
        public IReadOnlyList<string> OutLabels { get; internal set; } = Array.Empty<string>();

        /// <summary>Marks "no pending input change".</summary>
        public const int NoRequest = int.MinValue;

        /// <summary>Gets the incoming edge index a merge is held open for, or -1 for automatic right of way.</summary>
        public int ManualInIndex { get; private set; } = -1;

        /// <summary>Gets a pending merge input change (-1 = automatic) or <see cref="NoRequest"/>.</summary>
        public int RequestedInIndex { get; private set; } = NoRequest;

        /// <summary>Gets whether a merge input change waits for the merge zone to clear.</summary>
        public bool HasPendingInput => RequestedInIndex != NoRequest;

        /// <summary>Asks a merge to open only one input (or -1 for automatic); applied when the merge zone is clear.</summary>
        public void RequestInIndex(int inIndex)
        {
            if (Kind != TrackNodeKind.Merge)
                throw new InvalidOperationException($"{Name} is a split; use {nameof(RequestOutIndex)}.");
            if (inIndex < -1 || inIndex >= inEdges.Count)
                throw new ArgumentOutOfRangeException(nameof(inIndex));
            RequestedInIndex = inIndex == ManualInIndex ? NoRequest : inIndex;
        }

        /// <summary>Sets the merge input immediately (initial setup and tests).</summary>
        public void ForceInIndex(int inIndex)
        {
            if (Kind != TrackNodeKind.Merge)
                throw new InvalidOperationException($"{Name} is a split; use {nameof(ForceOutIndex)}.");
            if (inIndex < -1 || inIndex >= inEdges.Count)
                throw new ArgumentOutOfRangeException(nameof(inIndex));
            ManualInIndex = inIndex;
            RequestedInIndex = NoRequest;
        }

        internal void ApplyInputRequest()
        {
            if (RequestedInIndex == NoRequest)
                return;
            ManualInIndex = RequestedInIndex;
            RequestedInIndex = NoRequest;
        }

        /// <summary>Asks a split to switch; it is applied as soon as no trolley occupies the switch.</summary>
        public void RequestOutIndex(int outIndex)
        {
            if (Kind != TrackNodeKind.Split)
                throw new InvalidOperationException($"{Name} is a merge and has no selectable state.");
            if (outIndex < 0 || outIndex >= outEdges.Count)
                throw new ArgumentOutOfRangeException(nameof(outIndex));
            RequestedOutIndex = outIndex == SelectedOutIndex ? -1 : outIndex;
        }

        /// <summary>Switches immediately regardless of occupancy (initial setup and tests).</summary>
        public void ForceOutIndex(int outIndex)
        {
            if (Kind != TrackNodeKind.Split)
                throw new InvalidOperationException($"{Name} is a merge and has no selectable state.");
            if (outIndex < 0 || outIndex >= outEdges.Count)
                throw new ArgumentOutOfRangeException(nameof(outIndex));
            SelectedOutIndex = outIndex;
            RequestedOutIndex = -1;
        }

        internal void ApplyRequest()
        {
            if (RequestedOutIndex < 0)
                return;
            SelectedOutIndex = RequestedOutIndex;
            RequestedOutIndex = -1;
        }

        internal void AddIn(TrackEdge edge) { inEdges.Add(edge); }
        internal void AddOut(TrackEdge edge) { outEdges.Add(edge); }
    }

    /// <summary>A track section between nodes (or a free end), traversed in one direction.</summary>
    public sealed class TrackEdge
    {
        internal TrackEdge(int index, string name, TrackPath path, bool isRing)
        {
            Index = index;
            Name = name;
            Path = path;
            IsRing = isRing;
        }

        /// <summary>Gets the edge's index in <see cref="TrackNetwork.Edges"/>.</summary>
        public int Index { get; }

        /// <summary>Gets the edge name (first layout element, for example "T37" or "S40.Q1").</summary>
        public string Name { get; }

        /// <summary>Gets the geometry of the edge.</summary>
        public TrackPath Path { get; }

        /// <summary>Gets the exact length in meters.</summary>
        public float LengthMeters => Path.TotalLengthMeters;

        /// <summary>Gets whether the edge's end connects back to its own start without a node.</summary>
        public bool IsRing { get; }

        /// <summary>Gets the node at the start, or null for a free start or a ring.</summary>
        public TrackNode StartNode { get; internal set; }

        /// <summary>Gets the node at the end, or null for a dead end or a ring.</summary>
        public TrackNode EndNode { get; internal set; }

        /// <summary>Samples the edge, clamping to its ends.</summary>
        public TrackSample SampleAt(float meters)
        {
            return Path.SampleAtDistance(Mathf.Clamp(meters, 0f, LengthMeters));
        }

        private int[] barcodeStarts;
        private int[] barcodeStops;

        /// <summary>Gets whether any part of this edge carries a code strip.</summary>
        public bool HasBarcode => barcodeStarts != null;

        internal void SetBarcodes(int[] starts, int[] stops)
        {
            for (var i = 0; i < starts.Length; i++)
            {
                if (starts[i] >= 0 && stops[i] >= 0)
                {
                    barcodeStarts = starts;
                    barcodeStops = stops;
                    return;
                }
            }
        }

        /// <summary>Finds where a barcode value lies on this edge, or false when no strip of the edge contains it.</summary>
        public bool TryFindBarcode(int value, out float meters)
        {
            meters = 0f;
            if (barcodeStarts == null)
                return false;
            var segments = Path.Segments;
            for (var i = 0; i < segments.Count; i++)
            {
                if (barcodeStarts[i] < 0 || barcodeStops[i] < 0)
                    continue;
                var low = Mathf.Min(barcodeStarts[i], barcodeStops[i]);
                var high = Mathf.Max(barcodeStarts[i], barcodeStops[i]);
                if (value < low || value > high)
                    continue;
                var span = barcodeStops[i] - barcodeStarts[i];
                var fraction = span == 0 ? 0f : (value - barcodeStarts[i]) / (float)span;
                meters = segments[i].StartDistanceMeters + fraction * segments[i].LengthMeters;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the code strip value (mm) under a point of the edge, or -1 where there is no strip. Values rise along the
        /// direction of travel and are interpolated linearly over each element.
        /// </summary>
        public int GetBarcode(float meters)
        {
            if (barcodeStarts == null)
                return -1;
            var segments = Path.Segments;
            var clamped = Mathf.Clamp(meters, 0f, LengthMeters);
            for (var i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (clamped > segment.StartDistanceMeters + segment.LengthMeters && i < segments.Count - 1)
                    continue;
                if (barcodeStarts[i] < 0 || barcodeStops[i] < 0)
                    return -1;
                var fraction = segment.LengthMeters > 0f
                    ? Mathf.Clamp((clamped - segment.StartDistanceMeters) / segment.LengthMeters, 0f, 1f)
                    : 0f;
                return Mathf.RoundToInt(barcodeStarts[i] + (barcodeStops[i] - barcodeStarts[i]) * fraction);
            }
            return -1;
        }
    }

    /// <summary>A directed track graph: edges joined by split and merge nodes. A simple loop is one ring edge.</summary>
    public sealed class TrackNetwork
    {
        private readonly TrackEdge[] edges;
        private readonly TrackNode[] nodes;
        private readonly TrackEdge[] mainRoute;
        private readonly Dictionary<string, TrackEdge> edgesByName = new Dictionary<string, TrackEdge>(StringComparer.OrdinalIgnoreCase);

        internal TrackNetwork(IList<TrackEdge> sourceEdges, IList<TrackNode> sourceNodes)
        {
            if (sourceEdges == null || sourceEdges.Count == 0)
                throw new ArgumentException("A track network needs at least one edge.", nameof(sourceEdges));
            edges = new TrackEdge[sourceEdges.Count];
            sourceEdges.CopyTo(edges, 0);
            nodes = new TrackNode[sourceNodes.Count];
            sourceNodes.CopyTo(nodes, 0);

            var minimum = new Vector3(float.PositiveInfinity, 0f, float.PositiveInfinity);
            var maximum = new Vector3(float.NegativeInfinity, 0f, float.NegativeInfinity);
            foreach (var edge in edges)
            {
                edgesByName[edge.Name] = edge;
                TotalLengthMeters += edge.LengthMeters;
                var steps = Mathf.Max(1, Mathf.CeilToInt(edge.LengthMeters / 5f));
                for (var i = 0; i <= steps; i++)
                {
                    var position = edge.SampleAt(edge.LengthMeters * i / steps).Position;
                    minimum.x = Mathf.Min(minimum.x, position.x);
                    minimum.z = Mathf.Min(minimum.z, position.z);
                    maximum.x = Mathf.Max(maximum.x, position.x);
                    maximum.z = Mathf.Max(maximum.z, position.z);
                }
            }
            BoundsMinimum = minimum;
            BoundsMaximum = maximum;

            ResetSwitches();

            var route = new List<TrackEdge>();
            var current = edges[0];
            while (current != null && !route.Contains(current))
            {
                route.Add(current);
                MainRouteLengthMeters += current.LengthMeters;
                current = NextEdge(current);
            }
            mainRoute = route.ToArray();
            IsMainRouteClosed = current == edges[0];
        }

        /// <summary>Gets all edges; edge 0 contains the first layout element.</summary>
        public IReadOnlyList<TrackEdge> Edges => edges;

        /// <summary>Gets all split and merge nodes.</summary>
        public IReadOnlyList<TrackNode> Nodes => nodes;

        /// <summary>Gets whether the whole track is one closed loop without any switch.</summary>
        public bool IsRing => edges.Length == 1 && edges[0].IsRing;

        /// <summary>Gets the summed length of all edges in meters.</summary>
        public float TotalLengthMeters { get; }

        /// <summary>Gets the length of the main route (edge 0 followed with the default switch states).</summary>
        public float MainRouteLengthMeters { get; }

        /// <summary>Gets whether the main route returns to edge 0.</summary>
        public bool IsMainRouteClosed { get; }

        /// <summary>Gets the lower corner of the plan-view bounds.</summary>
        public Vector3 BoundsMinimum { get; }

        /// <summary>Gets the upper corner of the plan-view bounds.</summary>
        public Vector3 BoundsMaximum { get; }

        /// <summary>Gets the edges of the main route in travel order.</summary>
        public IReadOnlyList<TrackEdge> MainRoute => mainRoute;

        /// <summary>Sets every split to its default state (the straight exit) and every merge to automatic right of way.</summary>
        public void ResetSwitches()
        {
            foreach (var node in nodes)
            {
                if (node.Kind == TrackNodeKind.Split && node.OutEdges.Count > 0)
                    node.ForceOutIndex(node.OutEdges.Count - 1);
                else if (node.Kind == TrackNodeKind.Merge)
                    node.ForceInIndex(-1);
            }
        }

        /// <summary>Gets the edge trolleys take after <paramref name="edge"/>, or null at a dead end.</summary>
        public TrackEdge NextEdge(TrackEdge edge)
        {
            if (edge.IsRing)
                return edge;
            return edge.EndNode == null ? null : edge.EndNode.ActiveOutEdge;
        }

        /// <summary>Gets the edge normally preceding <paramref name="edge"/>, or null at a free start.</summary>
        public TrackEdge PreviousEdge(TrackEdge edge)
        {
            if (edge.IsRing)
                return edge;
            var node = edge.StartNode;
            if (node == null || node.InEdges.Count == 0)
                return null;
            return node.InEdges[node.Kind == TrackNodeKind.Merge ? Mathf.Clamp(node.PriorityInIndex, 0, node.InEdges.Count - 1) : 0];
        }

        /// <summary>Finds an edge by name, ignoring case.</summary>
        public TrackEdge FindEdge(string name)
        {
            return name != null && edgesByName.TryGetValue(name.Trim(), out var edge) ? edge : null;
        }

        /// <summary>Finds a node by switch name, ignoring case.</summary>
        public TrackNode FindNode(string name)
        {
            foreach (var node in nodes)
            {
                if (string.Equals(node.Name, name, StringComparison.OrdinalIgnoreCase))
                    return node;
            }
            return null;
        }

        /// <summary>Maps a distance along the main route to an edge location (wrapping when the route is closed).</summary>
        public TrackLocation ResolveMainRoute(float distanceMeters)
        {
            if (float.IsNaN(distanceMeters) || float.IsInfinity(distanceMeters))
                throw new ArgumentOutOfRangeException(nameof(distanceMeters));

            var remaining = IsMainRouteClosed && MainRouteLengthMeters > 0f
                ? Mathf.Repeat(distanceMeters, MainRouteLengthMeters)
                : Mathf.Clamp(distanceMeters, 0f, MainRouteLengthMeters);
            foreach (var edge in mainRoute)
            {
                if (remaining <= edge.LengthMeters)
                    return new TrackLocation(edge, remaining);
                remaining -= edge.LengthMeters;
            }
            var last = mainRoute[mainRoute.Length - 1];
            return new TrackLocation(last, last.LengthMeters);
        }

        /// <summary>Moves a location forward along the active route; stops at a dead end. Crossed nodes are appended when requested.</summary>
        public void Advance(ref TrackEdge edge, ref float meters, float distanceMeters, List<TrackNode> crossedNodes = null)
        {
            if (edge.IsRing)
            {
                meters = Mathf.Repeat(meters + distanceMeters, edge.LengthMeters);
                return;
            }

            meters += distanceMeters;
            var guard = 0;
            while (meters > edge.LengthMeters && guard++ < 256)
            {
                var next = NextEdge(edge);
                if (next == null)
                {
                    meters = edge.LengthMeters;
                    return;
                }
                meters -= edge.LengthMeters;
                if (edge.EndNode != null)
                    crossedNodes?.Add(edge.EndNode);
                edge = next;
            }
        }

        /// <summary>
        /// Samples the track <paramref name="offsetMeters"/> ahead of (positive) or behind (negative) a location. Ahead follows the
        /// active switch states; behind follows the trolley's own trail, most recent edge first, then the default predecessor.
        /// </summary>
        public TrackSample SampleOffset(TrackEdge edge, float meters, float offsetMeters, IReadOnlyList<TrackEdge> trail)
        {
            var location = ResolveOffset(edge, meters, offsetMeters, trail);
            return location.Edge.SampleAt(location.Meters);
        }

        /// <summary>Maps a point <paramref name="offsetMeters"/> ahead of (positive) or behind (negative) a location to an edge location.</summary>
        public TrackLocation ResolveOffset(TrackEdge edge, float meters, float offsetMeters, IReadOnlyList<TrackEdge> trail)
        {
            var current = edge;
            var t = meters + offsetMeters;
            if (current.IsRing)
                return new TrackLocation(current, Mathf.Repeat(t, current.LengthMeters));

            var guard = 0;
            if (offsetMeters >= 0f)
            {
                while (t > current.LengthMeters && guard++ < 256)
                {
                    var next = NextEdge(current);
                    if (next == null)
                    {
                        t = current.LengthMeters;
                        break;
                    }
                    t -= current.LengthMeters;
                    current = next;
                    if (current.IsRing)
                        return new TrackLocation(current, Mathf.Repeat(t, current.LengthMeters));
                }
            }
            else
            {
                var trailIndex = 0;
                while (t < 0f && guard++ < 256)
                {
                    var previous = trail != null && trailIndex < trail.Count ? trail[trailIndex++] : PreviousEdge(current);
                    if (previous == null)
                    {
                        t = 0f;
                        break;
                    }
                    current = previous;
                    t += current.LengthMeters;
                    if (current.IsRing)
                        return new TrackLocation(current, Mathf.Repeat(t, current.LengthMeters));
                }
            }
            return new TrackLocation(current, Mathf.Clamp(t, 0f, current.LengthMeters));
        }

        /// <summary>Gets whether the track carries code strips. Without strips the distance along the track in mm is the barcode.</summary>
        public bool HasBarcodes
        {
            get
            {
                foreach (var edge in edges)
                {
                    if (edge.HasBarcode)
                        return true;
                }
                return false;
            }
        }

        /// <summary>Gets the barcode value (mm) at a location, or -1 where there is no code strip.</summary>
        public int GetBarcode(TrackLocation location)
        {
            if (HasBarcodes)
                return location.Edge.GetBarcode(location.Meters);
            return Mathf.RoundToInt(location.Meters * 1000f);
        }

        /// <summary>Gets the barcode text shown to the user ("-" where there is no code strip).</summary>
        public string FormatBarcode(TrackLocation location)
        {
            var value = GetBarcode(location);
            return value >= 0 ? value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";
        }

        /// <summary>Finds where a barcode value lies on the track (the first match when strips overlap in the data).</summary>
        public bool TryFindBarcode(string text, out TrackLocation location, out string error)
        {
            location = new TrackLocation(edges[0], 0f);
            error = null;
            if (!int.TryParse(text == null ? string.Empty : text.Trim(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var value) || value < 0)
            {
                error = "Enter a barcode value (a whole number, mm).";
                return false;
            }

            if (!HasBarcodes)
            {
                var edge = edges[0];
                var meters = value * 0.001f;
                if (meters > edge.LengthMeters + 0.0005f)
                {
                    error = $"Barcode {value} is outside the track (0 - {Mathf.RoundToInt(edge.LengthMeters * 1000f)}).";
                    return false;
                }
                location = new TrackLocation(edge, Mathf.Min(meters, edge.LengthMeters));
                return true;
            }

            foreach (var edge in edges)
            {
                if (edge.TryFindBarcode(value, out var meters))
                {
                    location = new TrackLocation(edge, meters);
                    return true;
                }
            }
            error = $"Barcode {value} is not on any code strip of the track.";
            return false;
        }

        /// <summary>Builds a one-edge network from a configuration asset. A valid closed loop becomes a ring.</summary>
        public static TrackNetwork FromConfiguration(TrackConfiguration configuration, bool requireValidClosedLoop = true)
        {
            var result = TrackGeometryBuilder.Build(configuration);
            if (requireValidClosedLoop && (!result.Report.IsValid || !result.Path.IsClosed))
            {
                var errors = new List<string>();
                foreach (var issue in result.Report.Issues)
                {
                    if (issue.Severity == TrackIssueSeverity.Error)
                        errors.Add(issue.Message);
                }
                if (errors.Count == 0)
                    errors.Add("The track is not a closed loop.");
                throw new InvalidOperationException("A valid closed track is required: " + string.Join("; ", errors));
            }

            var ring = result.Path.IsClosed && result.Report.IsValid;
            if (!requireValidClosedLoop && result.Path.Segments.Count == 0)
                return null;
            var edge = new TrackEdge(0, "Loop", result.Path, ring);
            return new TrackNetwork(new[] { edge }, Array.Empty<TrackNode>());
        }
    }
}
