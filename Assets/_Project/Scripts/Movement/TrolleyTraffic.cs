using System;
using System.Collections.Generic;
using IndustrialConveyor.Simulation.Track;
using UnityEngine;

namespace IndustrialConveyor.Simulation.Movement
{
    /// <summary>
    /// Moves trolleys along a <see cref="TrackNetwork"/>: speed limits from the trolley or stop line ahead, switch locking
    /// while a trolley occupies a switch, and platoon-based right of way at merges. A simple loop is the one-edge case.
    /// </summary>
    public sealed class TrolleyTraffic
    {
        private const float TokenRangeMeters = 25f;
        private const int MaximumPlatoon = 4;
        private const float LeaderHorizonMeters = 80f;
        private const float LoopReserveMeters = 2f;
        private const float SwitchLockMarginMeters = 0.3f;
        private const int SpacingPasses = 3;

        private sealed class MergeState
        {
            public int TokenEdge = -1;
            public int Passed;
            public bool Yielding;
            public float[] LoopRoom;
        }

        private readonly TrackNetwork network;
        private readonly Dictionary<TrackNode, MergeState> merges = new Dictionary<TrackNode, MergeState>();
        private readonly List<TrolleyData>[] byEdge;
        private readonly List<TrackNode> crossed = new List<TrackNode>();
        private float[] nextSpeeds = new float[0];
        private float[] advances = new float[0];
        private float[] leaderDistances = new float[0];
        private int[] leaderIndices = new int[0];
        private TrolleyData[] nearestBuffer = new TrolleyData[4];
        private float[] distanceBuffer = new float[4];

        /// <summary>Creates traffic logic for a network.</summary>
        public TrolleyTraffic(TrackNetwork network, float minimumGapMeters)
        {
            this.network = network ?? throw new ArgumentNullException(nameof(network));
            MinimumGapMeters = minimumGapMeters;
            byEdge = new List<TrolleyData>[network.Edges.Count];
            for (var i = 0; i < byEdge.Length; i++)
                byEdge[i] = new List<TrolleyData>();
            foreach (var node in network.Nodes)
            {
                if (node.Kind == TrackNodeKind.Merge)
                    merges[node] = new MergeState();
            }
        }

        /// <summary>Gets or sets the free distance trolleys keep between bodies.</summary>
        public float MinimumGapMeters { get; set; }

        /// <summary>Gets the network the traffic runs on.</summary>
        public TrackNetwork Network => network;

        /// <summary>Gets the incoming edge that currently has right of way at a merge, or null.</summary>
        public TrackEdge GetMergeRightOfWay(TrackNode node)
        {
            if (node.Kind != TrackNodeKind.Merge || !merges.TryGetValue(node, out var state) || state.TokenEdge < 0)
                return null;
            return node.InEdges[state.TokenEdge];
        }

        /// <summary>Advances every trolley one fixed step.</summary>
        public void Step(IReadOnlyList<TrolleyData> trolleys, float deltaTimeSeconds, bool globalEmergencyStop)
        {
            var count = trolleys.Count;
            if (count == 0)
                return;
            EnsureBuffers(count);
            RebuildLists(trolleys);
            UpdateSwitches();
            UpdateMergeTokens();

            for (var i = 0; i < count; i++)
            {
                var trolley = trolleys[i];
                var targetSpeed = globalEmergencyStop || trolley.IsEmergencyStopped || trolley.IsDriveFaulted
                    ? 0f
                    : trolley.TargetSpeedMetersPerSecond;
                var deceleration = trolley.DecelerationMetersPerSecondSquared;
                var halfLength = trolley.TrolleyType.BodyLengthMeters * 0.5f;

                FindAhead(trolley, true, out var leader, out var leaderDistance, out var obstacleDistance);
                leaderIndices[i] = leader != null ? leader.StepIndex : -1;
                leaderDistances[i] = leaderDistance;
                if (leader != null)
                {
                    var clearance = leaderDistance - (trolley.TrolleyType.BodyLengthMeters + leader.TrolleyType.BodyLengthMeters) * 0.5f -
                                    MinimumGapMeters;
                    targetSpeed = Mathf.Min(targetSpeed, TrolleyDriveModel.CalculateMaximumSafeSpeed(
                        leader.ActualSpeedMetersPerSecond, deceleration, Mathf.Max(0f, clearance)));
                }
                if (!float.IsPositiveInfinity(obstacleDistance))
                {
                    var clearance = obstacleDistance - halfLength - MinimumGapMeters;
                    targetSpeed = Mathf.Min(targetSpeed, TrolleyDriveModel.CalculateMaximumSafeSpeed(
                        0f, deceleration, Mathf.Max(0f, clearance)));
                }

                var nextSpeed = TrolleyDriveModel.AdvanceSpeed(
                    trolley.ActualSpeedMetersPerSecond,
                    targetSpeed,
                    trolley.TrolleyType.MaximumSpeedMetersPerSecond,
                    trolley.AccelerationMetersPerSecondSquared,
                    deceleration,
                    deltaTimeSeconds);
                nextSpeeds[i] = nextSpeed;
                advances[i] = (trolley.ActualSpeedMetersPerSecond + nextSpeed) * 0.5f * deltaTimeSeconds;
            }

            EnforceSpacing(trolleys, count);

            for (var i = 0; i < count; i++)
            {
                var trolley = trolleys[i];
                var edge = trolley.Edge;
                var position = trolley.SimulationPositionMeters;
                crossed.Clear();
                network.Advance(ref edge, ref position, advances[i], crossed);
                if (edge != trolley.Edge)
                    trolley.EnterEdge(edge);
                trolley.SimulationPositionMeters = position;
                trolley.ActualSpeedMetersPerSecond = nextSpeeds[i];
                foreach (var node in crossed)
                {
                    if (node.Kind == TrackNodeKind.Merge && merges.TryGetValue(node, out var state))
                        state.Passed++;
                }
            }
        }

        /// <summary>Checks that no two trolleys overlap along their routes; returns a description of the first overlap.</summary>
        public bool TryFindOverlap(IReadOnlyList<TrolleyData> trolleys, out string description)
        {
            description = null;
            if (trolleys.Count < 2)
                return false;
            RebuildLists(trolleys);
            foreach (var trolley in trolleys)
            {
                FindAhead(trolley, false, out var leader, out var leaderDistance, out _);
                if (leader == null)
                    continue;
                var clearance = leaderDistance - (trolley.TrolleyType.BodyLengthMeters + leader.TrolleyType.BodyLengthMeters) * 0.5f -
                                MinimumGapMeters;
                if (clearance < -0.001f)
                {
                    description = $"Trolley spacing overlaps {trolley.TrolleyId} and {leader.TrolleyId} by {-clearance:F3} m.";
                    return true;
                }
            }
            return false;
        }

        /// <summary>Finds the free location with the most clearance for a body of the given length.</summary>
        public bool TrySuggestLocation(IReadOnlyList<TrolleyData> trolleys, float bodyLengthMeters, out TrackLocation location)
        {
            location = new TrackLocation(network.Edges[0], 0f);
            if (trolleys.Count == 0)
                return true;

            var bestClearance = float.NegativeInfinity;
            foreach (var edge in network.Edges)
            {
                var samples = Mathf.Max(1, Mathf.CeilToInt(edge.LengthMeters * 2f));
                for (var index = 0; index < samples; index++)
                {
                    var meters = edge.LengthMeters * index / samples;
                    var minimum = float.PositiveInfinity;
                    foreach (var existing in trolleys)
                    {
                        var halfSum = (bodyLengthMeters + existing.TrolleyType.BodyLengthMeters) * 0.5f;
                        var forward = ForwardDistance(edge, meters, existing.Edge, existing.SimulationPositionMeters, LeaderHorizonMeters);
                        var backward = ForwardDistance(existing.Edge, existing.SimulationPositionMeters, edge, meters, LeaderHorizonMeters);
                        minimum = Mathf.Min(minimum, forward - halfSum, backward - halfSum);
                    }
                    if (minimum <= bestClearance)
                        continue;
                    bestClearance = minimum;
                    location = new TrackLocation(edge, meters);
                }
            }
            return bestClearance >= MinimumGapMeters;
        }

        /// <summary>Distance along the active route from one location to another, or infinity beyond the limit.</summary>
        public float ForwardDistance(TrackEdge fromEdge, float fromMeters, TrackEdge toEdge, float toMeters, float limitMeters)
        {
            var edge = fromEdge;
            var accumulated = -fromMeters;
            var first = true;
            var maximumSteps = network.Edges.Count * 4 + 4;
            for (var step = 0; step < maximumSteps; step++)
            {
                if (edge == toEdge && (!first || toMeters >= fromMeters))
                    return accumulated + toMeters;
                accumulated += edge.LengthMeters;
                if (accumulated > limitMeters)
                    return float.PositiveInfinity;
                var next = network.NextEdge(edge);
                if (next == null)
                    return float.PositiveInfinity;
                edge = next;
                first = false;
            }
            return float.PositiveInfinity;
        }

        private void EnsureBuffers(int count)
        {
            if (nextSpeeds.Length >= count)
                return;
            nextSpeeds = new float[count];
            advances = new float[count];
            leaderDistances = new float[count];
            leaderIndices = new int[count];
        }

        private void RebuildLists(IReadOnlyList<TrolleyData> trolleys)
        {
            foreach (var list in byEdge)
                list.Clear();
            for (var i = 0; i < trolleys.Count; i++)
            {
                trolleys[i].StepIndex = i;
                byEdge[trolleys[i].Edge.Index].Add(trolleys[i]);
            }
            foreach (var list in byEdge)
                list.Sort((left, right) => left.SimulationPositionMeters.CompareTo(right.SimulationPositionMeters));
        }

        private static float Extent(TrolleyData trolley)
        {
            return Mathf.Max(trolley.TrolleyType.BodyLengthMeters, trolley.TrolleyType.WheelbaseMeters) * 0.5f + SwitchLockMarginMeters;
        }

        private void UpdateSwitches()
        {
            foreach (var node in network.Nodes)
            {
                if (node.Kind != TrackNodeKind.Split)
                    continue;

                var occupied = false;
                foreach (var edge in node.InEdges)
                {
                    foreach (var trolley in byEdge[edge.Index])
                    {
                        if (edge.LengthMeters - trolley.SimulationPositionMeters <= Extent(trolley))
                            occupied = true;
                    }
                }
                foreach (var edge in node.OutEdges)
                {
                    foreach (var trolley in byEdge[edge.Index])
                    {
                        if (trolley.SimulationPositionMeters <= Extent(trolley))
                            occupied = true;
                    }
                }

                node.IsLocked = occupied;
                if (!occupied)
                    node.ApplyRequest();
            }
        }

        private void UpdateMergeTokens()
        {
            foreach (var node in network.Nodes)
            {
                if (node.Kind != TrackNodeKind.Merge)
                    continue;

                var state = merges[node];
                var inCount = node.InEdges.Count;
                var zone = node.ZoneMeters + MinimumGapMeters;
                var busy = false;
                if (nearestBuffer.Length < inCount)
                {
                    nearestBuffer = new TrolleyData[inCount];
                    distanceBuffer = new float[inCount];
                }
                var nearest = nearestBuffer;
                var distance = distanceBuffer;
                ComputeLoopRoom(node, state);
                for (var i = 0; i < inCount; i++)
                {
                    var list = byEdge[node.InEdges[i].Index];
                    nearest[i] = list.Count > 0 ? list[list.Count - 1] : null;
                    distance[i] = nearest[i] == null
                        ? float.PositiveInfinity
                        : node.InEdges[i].LengthMeters - nearest[i].SimulationPositionMeters;
                    if (nearest[i] != null && IsCommitted(nearest[i], distance[i], node))
                        busy = true;
                }

                // Only trolleys still approaching on an input occupy the merge zone; trolleys already past the node are
                // handled as leaders of whoever follows them. An input whose lead trolley would not fit into the loop it
                // joins is not a candidate for the right of way.
                if (node.HasPendingInput && !busy)
                    node.ApplyInputRequest();
                if (node.ManualInIndex >= 0)
                {
                    // Held open by hand: the other inputs always wait before the zone.
                    state.TokenEdge = node.ManualInIndex;
                    state.Yielding = false;
                    continue;
                }

                if (state.TokenEdge >= 0)
                {
                    var holder = state.TokenEdge;
                    var holderInRange = IsCandidate(state, nearest, distance, holder);
                    var challenger = -1;
                    for (var i = 0; i < inCount; i++)
                    {
                        if (i != holder && IsCandidate(state, nearest, distance, i) &&
                            (challenger < 0 || distance[i] < distance[challenger]))
                            challenger = i;
                    }

                    if (!holderInRange && !busy)
                    {
                        state.TokenEdge = -1;
                        state.Yielding = false;
                    }
                    else if (challenger < 0)
                    {
                        state.Yielding = false;
                    }
                    else
                    {
                        // A waiting rival gets its turn after a platoon, or as soon as the holder's lead trolley stands still.
                        if (state.Passed >= MaximumPlatoon || !holderInRange ||
                            (nearest[holder] != null && IsIdle(nearest[holder], distance[holder], zone)))
                            state.Yielding = true;
                        if (state.Yielding && !busy)
                        {
                            state.TokenEdge = challenger;
                            state.Passed = 0;
                            state.Yielding = false;
                        }
                    }
                }

                if (state.TokenEdge < 0)
                {
                    var best = -1;
                    for (var i = 0; i < inCount; i++)
                    {
                        if (!IsCandidate(state, nearest, distance, i))
                            continue;
                        if (best < 0 || distance[i] < distance[best] - 0.5f ||
                            (Mathf.Abs(distance[i] - distance[best]) <= 0.5f && i == node.PriorityInIndex))
                            best = i;
                    }
                    if (best >= 0)
                    {
                        state.TokenEdge = best;
                        state.Passed = 0;
                    }
                }
            }
        }

        private bool IsCandidate(MergeState state, TrolleyData[] nearest, float[] distance, int index)
        {
            return nearest[index] != null && distance[index] <= TokenRangeMeters && FitsLoop(state, index, nearest[index]);
        }

        private bool FitsLoop(MergeState state, int inputIndex, TrolleyData trolley)
        {
            return state.LoopRoom[inputIndex] >= trolley.TrolleyType.BodyLengthMeters + MinimumGapMeters + LoopReserveMeters;
        }

        /// <summary>
        /// Free length of the loop a merge input would feed. Following the active switches from the merge output back to the
        /// merge, a trolley arriving over a different input than the loop's own return edge joins that loop and needs room in it.
        /// </summary>
        private void ComputeLoopRoom(TrackNode node, MergeState state)
        {
            var inCount = node.InEdges.Count;
            if (state.LoopRoom == null || state.LoopRoom.Length != inCount)
                state.LoopRoom = new float[inCount];
            for (var i = 0; i < inCount; i++)
                state.LoopRoom[i] = float.PositiveInfinity;
            if (node.OutEdges.Count == 0)
                return;

            var walk = node.OutEdges[0];
            var length = 0f;
            var occupied = 0f;
            var returnIndex = -1;
            for (var step = 0; step <= network.Edges.Count && walk != null; step++)
            {
                length += walk.LengthMeters;
                foreach (var trolley in byEdge[walk.Index])
                    occupied += trolley.TrolleyType.BodyLengthMeters + MinimumGapMeters;
                if (walk.EndNode == node)
                {
                    returnIndex = IndexOf(node.InEdges, walk);
                    break;
                }
                if (walk.IsRing)
                    break;
                walk = network.NextEdge(walk);
            }

            if (returnIndex < 0)
                return;
            for (var i = 0; i < inCount; i++)
            {
                if (i != returnIndex)
                    state.LoopRoom[i] = length - occupied;
            }
        }

        private static int IndexOf(IReadOnlyList<TrackEdge> edges, TrackEdge edge)
        {
            for (var i = 0; i < edges.Count; i++)
            {
                if (edges[i] == edge)
                    return i;
            }
            return -1;
        }

        private static float StoppingDistance(TrolleyData trolley)
        {
            var speed = trolley.ActualSpeedMetersPerSecond;
            return speed * speed / (2f * Mathf.Max(0.05f, trolley.DecelerationMetersPerSecondSquared));
        }

        /// <summary>True when a trolley is inside the merge zone or can no longer stop before the stop line in front of it.</summary>
        private bool IsCommitted(TrolleyData trolley, float centerDistanceToNode, TrackNode node)
        {
            var front = centerDistanceToNode - trolley.TrolleyType.BodyLengthMeters * 0.5f;
            return front < node.ZoneMeters ||
                   front - (node.ZoneMeters + MinimumGapMeters) < StoppingDistance(trolley) - 0.05f;
        }

        private static bool IsIdle(TrolleyData nearest, float centerDistance, float zone)
        {
            return nearest.ActualSpeedMetersPerSecond < 0.05f &&
                   centerDistance - nearest.TrolleyType.BodyLengthMeters * 0.5f >= zone - 0.05f;
        }

        /// <summary>
        /// Walks the active route ahead of a trolley and reports the nearest trolley in front (center distance) and the
        /// nearest stop line, as center distances. Stop lines are a merge without right of way, the end of a dead end, and a
        /// merge whose far side is blocked by a stopped trolley that leaves no room for this trolley's whole body.
        /// </summary>
        private void FindAhead(TrolleyData follower, bool respectMerges, out TrolleyData leader,
            out float leaderDistance, out float obstacleDistance)
        {
            leader = null;
            leaderDistance = float.PositiveInfinity;
            obstacleDistance = float.PositiveInfinity;

            var edge = follower.Edge;
            var position = follower.SimulationPositionMeters;
            var accumulated = -position;
            var first = true;
            var boxDistance = float.NaN;
            var boxZone = 0f;
            var maximumSteps = network.Edges.Count * 4 + 4;
            for (var step = 0; step < maximumSteps; step++)
            {
                var list = byEdge[edge.Index];
                for (var i = 0; i < list.Count; i++)
                {
                    var other = list[i];
                    if (other == follower)
                        continue;
                    if (first && (other.SimulationPositionMeters < position ||
                                  (other.SimulationPositionMeters == position && other.StepIndex < follower.StepIndex)))
                        continue;
                    leader = other;
                    leaderDistance = accumulated + other.SimulationPositionMeters;
                    if (!float.IsNaN(boxDistance) && other.ActualSpeedMetersPerSecond < 0.05f)
                    {
                        var freeBeyondNode = leaderDistance - boxDistance - other.TrolleyType.BodyLengthMeters * 0.5f;
                        if (freeBeyondNode < follower.TrolleyType.BodyLengthMeters + MinimumGapMeters)
                            obstacleDistance = boxDistance - boxZone;
                    }
                    return;
                }

                accumulated += edge.LengthMeters;
                var node = edge.EndNode;
                if (respectMerges && node != null && node.Kind == TrackNodeKind.Merge && merges.TryGetValue(node, out var state))
                {
                    var speed = follower.ActualSpeedMetersPerSecond;
                    var stopping = speed * speed / (2f * Mathf.Max(0.05f, follower.DecelerationMetersPerSecondSquared));
                    var stopClearance = accumulated - node.ZoneMeters - follower.TrolleyType.BodyLengthMeters * 0.5f - MinimumGapMeters;
                    var canStop = stopClearance >= stopping - 0.05f;
                    var inputIndex = IndexOf(node.InEdges, edge);
                    if (canStop && state.LoopRoom != null && inputIndex >= 0 && !FitsLoop(state, inputIndex, follower))
                    {
                        obstacleDistance = accumulated - node.ZoneMeters;
                        return;
                    }

                    if (state.TokenEdge >= 0)
                    {
                        if (node.InEdges[state.TokenEdge] != edge)
                        {
                            obstacleDistance = accumulated - node.ZoneMeters;
                            return;
                        }
                        if (float.IsNaN(boxDistance))
                        {
                            boxDistance = accumulated;
                            boxZone = node.ZoneMeters;
                        }
                        if (state.Yielding && canStop)
                        {
                            // The right of way is being handed over: trolleys that can still stop in time wait before the zone.
                            obstacleDistance = accumulated - node.ZoneMeters;
                            return;
                        }
                    }
                }
                if (accumulated > LeaderHorizonMeters)
                    return;

                var next = network.NextEdge(edge);
                if (next == null)
                {
                    obstacleDistance = accumulated + MinimumGapMeters;
                    return;
                }
                edge = next;
                first = false;
            }
        }

        private void EnforceSpacing(IReadOnlyList<TrolleyData> trolleys, int count)
        {
            for (var pass = 0; pass < SpacingPasses; pass++)
            {
                for (var i = 0; i < count; i++)
                {
                    var leaderIndex = leaderIndices[i];
                    if (leaderIndex < 0)
                        continue;
                    var follower = trolleys[i];
                    var leader = trolleys[leaderIndex];
                    var required = (follower.TrolleyType.BodyLengthMeters + leader.TrolleyType.BodyLengthMeters) * 0.5f + MinimumGapMeters;
                    var after = leaderDistances[i] + advances[leaderIndex] - advances[i];
                    if (after + 0.0001f >= required)
                        continue;

                    advances[i] = Mathf.Max(0f, advances[i] - (required - after));
                    nextSpeeds[i] = Mathf.Min(nextSpeeds[i], nextSpeeds[leaderIndex]);
                }
            }
        }
    }
}
