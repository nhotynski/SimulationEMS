using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace IndustrialConveyor.Simulation.Track
{
    /// <summary>
    /// Builds a <see cref="TrackNetwork"/> from a WinMOD SIMLINE Layouter sheet (lengths and radii in mm).
    /// Elements are placed through their predecessor links (ConnPre / ConnExit / ConnEntry); free ports that meet at the
    /// same point and heading are joined too, which closes loops the sheet leaves implicit. Out switches split the
    /// network (Q0 curved branch, Q1 straight), In switches merge it (I1 straight, I0 curved branch).
    /// </summary>
    internal static class TrackLayoutReader
    {
        private const float JoinPositionToleranceMeters = 0.03f;
        private const float JoinHeadingToleranceDegrees = 2f;

        private enum ElementKind
        {
            Track,
            Curve,
            SwitchOut,
            SwitchIn
        }

        private struct Frame
        {
            public Vector3 Position;
            public float Heading;
        }

        private sealed class LayoutElement
        {
            public int Row;
            public string Type;
            public string Name;
            public string Predecessor;
            public string Exit;
            public string Entry;
            public string Length;
            public string Angle;
            public string Radius;
            public int[] StripStart = { -1, -1 };
            public int[] StripStop = { -1, -1 };

            public ElementKind Kind;
            public TrackTurnDirection Side;
            public float LengthMeters;
            public float AngleDegrees;
            public float RadiusMeters;
            public Dictionary<string, Frame> LocalPorts;
            public Frame Origin;
            public bool Placed;
            public bool Used;
            public TrackNode Node;
        }

        private readonly struct PortKey : IEquatable<PortKey>
        {
            public PortKey(LayoutElement element, string port)
            {
                Element = element;
                Port = port;
            }

            public LayoutElement Element { get; }
            public string Port { get; }
            public bool Equals(PortKey other) { return Element == other.Element && Port == other.Port; }
            public override bool Equals(object obj) { return obj is PortKey other && Equals(other); }
            public override int GetHashCode() { return (Element.GetHashCode() * 397) ^ Port.GetHashCode(); }
        }

        private sealed class EdgeDraft
        {
            public string Name;
            public Frame Start;
            public readonly List<TrackSegmentDefinition> Segments = new List<TrackSegmentDefinition>();
            public readonly List<string> Elements = new List<string>();
            public LayoutElement StartSwitch;
            public string StartPort;
            public string FirstPassName;
            public LayoutElement EndSwitch;
            public string EndPort;
            public bool IsRing;
            public readonly List<int> BarcodeStarts = new List<int>();
            public readonly List<int> BarcodeStops = new List<int>();

            /// <summary>Adds a segment together with the code strip of <paramref name="element"/> it runs along (0 or 1).</summary>
            public void Add(TrackSegmentDefinition segment, LayoutElement element, int strip)
            {
                Segments.Add(segment);
                BarcodeStarts.Add(element.StripStart[strip]);
                BarcodeStops.Add(element.StripStop[strip]);
            }
        }

        /// <summary>Returns false when the sheet has no Layouter header row; throws for a malformed layout.</summary>
        public static bool TryRead(Dictionary<int, Dictionary<int, string>> rows, string sheetName, out TrackNetwork network)
        {
            network = null;
            var headerRow = 0;
            Dictionary<string, int> columns = null;
            var rowNumbers = new List<int>(rows.Keys);
            rowNumbers.Sort();
            foreach (var rowNumber in rowNumbers)
            {
                var candidate = HeaderColumns(rows[rowNumber]);
                if (candidate.ContainsKey("connpre"))
                {
                    headerRow = rowNumber;
                    columns = candidate;
                    break;
                }
            }
            if (headerRow == 0)
                return false;

            foreach (var required in new[] { "proto type", "node name", "connexit", "connentry", "length", "angle", "radius" })
            {
                if (!columns.ContainsKey(required))
                    throw new InvalidDataException($"Sheet '{sheetName}': Layouter header lacks the '{required}' column.");
            }

            var elements = new List<LayoutElement>();
            var byName = new Dictionary<string, LayoutElement>(StringComparer.Ordinal);
            foreach (var rowNumber in rowNumbers)
            {
                if (rowNumber <= headerRow)
                    continue;
                var row = rows[rowNumber];
                var type = Text(row, columns["proto type"]);
                if (!type.StartsWith("EMS_", StringComparison.OrdinalIgnoreCase))
                    continue;

                var name = Text(row, columns["node name"]);
                if (name.Length == 0)
                    throw new InvalidDataException($"Sheet '{sheetName}' row {rowNumber}: {type} has no element name.");
                if (byName.ContainsKey(name))
                    throw new InvalidDataException($"Sheet '{sheetName}' row {rowNumber}: duplicate element name '{name}'.");

                var element = new LayoutElement
                {
                    Row = rowNumber,
                    Type = type,
                    Name = name,
                    Predecessor = Text(row, columns["connpre"]).Trim('"').Trim(),
                    Exit = Text(row, columns["connexit"]),
                    Entry = Text(row, columns["connentry"]),
                    Length = Text(row, columns["length"]),
                    Angle = Text(row, columns["angle"]),
                    Radius = Text(row, columns["radius"])
                };
                Classify(element, sheetName);
                for (var strip = 0; strip < 2; strip++)
                {
                    var suffix = (strip + 1).ToString("00", CultureInfo.InvariantCulture);
                    if (!columns.TryGetValue("codestripstart" + suffix, out var startColumn) ||
                        !columns.TryGetValue("codestripstop" + suffix, out var stopColumn))
                        continue;
                    var startText = Text(row, startColumn);
                    var stopText = Text(row, stopColumn);
                    if (startText.Length == 0 && stopText.Length == 0)
                        continue;
                    if (!TrackFileReader.TryParseNumber(startText, out var start) ||
                        !TrackFileReader.TryParseNumber(stopText, out var stop))
                        throw new InvalidDataException(
                            $"Sheet '{sheetName}' row {rowNumber} ({name}): code strip {suffix} start/stop must be numbers " +
                            $"(got '{startText}' / '{stopText}').");
                    element.StripStart[strip] = Mathf.RoundToInt(start);
                    element.StripStop[strip] = Mathf.RoundToInt(stop);
                }
                elements.Add(element);
                byName[name] = element;
            }

            if (elements.Count == 0)
                throw new InvalidDataException($"Sheet '{sheetName}' contains no EMS track elements.");

            network = Build(elements, byName, sheetName);
            return true;
        }

        private static void Classify(LayoutElement element, string sheetName)
        {
            var where = $"Sheet '{sheetName}' row {element.Row} ({element.Name})";
            var type = element.Type;
            if (string.Equals(type, "EMS_Track", StringComparison.OrdinalIgnoreCase))
            {
                element.Kind = ElementKind.Track;
                element.LengthMeters = Millimeters(element.Length, "Length", where);
                element.LocalPorts = new Dictionary<string, Frame>
                {
                    ["I0"] = new Frame(),
                    ["Q0"] = new Frame { Position = new Vector3(0f, 0f, element.LengthMeters) }
                };
                return;
            }

            if (type.StartsWith("EMS_Curve", StringComparison.OrdinalIgnoreCase))
            {
                element.Kind = ElementKind.Curve;
                element.Side = SideOf(element, where);
                element.AngleDegrees = Number(element.Angle, "Angle", where);
                element.RadiusMeters = Millimeters(element.Radius, "Radius", where);
                element.LocalPorts = new Dictionary<string, Frame>
                {
                    ["I0"] = new Frame(),
                    ["Q0"] = ArcEnd(element.Side, element.AngleDegrees, element.RadiusMeters)
                };
                return;
            }

            var isSwitch = type.StartsWith("EMS_Switch", StringComparison.OrdinalIgnoreCase);
            var isOut = isSwitch && type.EndsWith("Out", StringComparison.OrdinalIgnoreCase);
            var isIn = isSwitch && type.EndsWith("In", StringComparison.OrdinalIgnoreCase);
            if (isOut || isIn)
            {
                element.Kind = isOut ? ElementKind.SwitchOut : ElementKind.SwitchIn;
                element.Side = SideOf(element, where);
                element.LengthMeters = Millimeters(element.Length, "Length", where);
                element.AngleDegrees = Number(element.Angle, "Angle", where);
                element.RadiusMeters = Millimeters(element.Radius, "Radius", where);
                if (isOut)
                {
                    element.LocalPorts = new Dictionary<string, Frame>
                    {
                        ["I0"] = new Frame(),
                        ["Q0"] = ArcEnd(element.Side, element.AngleDegrees, element.RadiusMeters),
                        ["Q1"] = new Frame { Position = new Vector3(0f, 0f, element.LengthMeters) }
                    };
                }
                else
                {
                    // The branch input mirrors an Out branch: it joins the straight at the output.
                    var sign = element.Side == TrackTurnDirection.Right ? 1f : -1f;
                    var angle = element.AngleDegrees * Mathf.Deg2Rad;
                    element.LocalPorts = new Dictionary<string, Frame>
                    {
                        ["I1"] = new Frame(),
                        ["Q0"] = new Frame { Position = new Vector3(0f, 0f, element.LengthMeters) },
                        ["I0"] = new Frame
                        {
                            Position = new Vector3(sign * element.RadiusMeters * (1f - Mathf.Cos(angle)),
                                0f, element.LengthMeters - element.RadiusMeters * Mathf.Sin(angle)),
                            Heading = -sign * element.AngleDegrees
                        }
                    };
                }
                return;
            }

            throw new InvalidDataException(
                $"{where}: element type {type} is not supported (only EMS_Track, EMS_Curve*, EMS_Switch*In/Out).");
        }

        private static TrackTurnDirection SideOf(LayoutElement element, string where)
        {
            if (element.Type.IndexOf("Left", StringComparison.OrdinalIgnoreCase) >= 0)
                return TrackTurnDirection.Left;
            if (element.Type.IndexOf("Right", StringComparison.OrdinalIgnoreCase) >= 0)
                return TrackTurnDirection.Right;
            throw new InvalidDataException($"{where}: cannot tell the side of {element.Type}.");
        }

        private static Frame ArcEnd(TrackTurnDirection side, float angleDegrees, float radiusMeters)
        {
            var sign = side == TrackTurnDirection.Right ? 1f : -1f;
            var angle = angleDegrees * Mathf.Deg2Rad;
            return new Frame
            {
                Position = new Vector3(sign * radiusMeters * (1f - Mathf.Cos(angle)), 0f, radiusMeters * Mathf.Sin(angle)),
                Heading = sign * angleDegrees
            };
        }

        private static Vector3 Rotate(Vector3 local, float headingDegrees)
        {
            var radians = headingDegrees * Mathf.Deg2Rad;
            var sin = Mathf.Sin(radians);
            var cos = Mathf.Cos(radians);
            return new Vector3(local.x * cos + local.z * sin, 0f, -local.x * sin + local.z * cos);
        }

        private static Frame GlobalPort(LayoutElement element, string port)
        {
            var local = element.LocalPorts[port];
            return new Frame
            {
                Position = element.Origin.Position + Rotate(local.Position, element.Origin.Heading),
                Heading = element.Origin.Heading + local.Heading
            };
        }

        private static TrackNetwork Build(List<LayoutElement> elements, Dictionary<string, LayoutElement> byName, string sheetName)
        {
            var links = new Dictionary<PortKey, PortKey>();
            var explicitLinks = new List<KeyValuePair<PortKey, PortKey>>();
            foreach (var element in elements)
            {
                if (element.Predecessor.Length == 0 || !byName.TryGetValue(element.Predecessor, out var predecessor))
                    continue;
                var where = $"Sheet '{sheetName}' row {element.Row} ({element.Name})";
                RequirePort(predecessor, element.Exit, where);
                RequirePort(element, element.Entry, where);

                var flowsForward = element.Exit.StartsWith("Q", StringComparison.OrdinalIgnoreCase) &&
                                   element.Entry.StartsWith("I", StringComparison.OrdinalIgnoreCase);
                var flowsBackward = element.Exit.StartsWith("I", StringComparison.OrdinalIgnoreCase) &&
                                    element.Entry.StartsWith("Q", StringComparison.OrdinalIgnoreCase);
                if (!flowsForward && !flowsBackward)
                    throw new InvalidDataException(
                        $"{where}: ConnExit '{element.Exit}' and ConnEntry '{element.Entry}' must be one Q and one I port.");

                var predecessorPort = new PortKey(predecessor, element.Exit);
                var ownPort = new PortKey(element, element.Entry);
                var from = flowsForward ? predecessorPort : ownPort;
                var to = flowsForward ? ownPort : predecessorPort;
                if (links.ContainsKey(from) || links.ContainsValue(to))
                    throw new InvalidDataException(
                        $"{where}: port {from.Element.Name}.{from.Port} or {to.Element.Name}.{to.Port} is connected twice.");
                links[from] = to;
                explicitLinks.Add(new KeyValuePair<PortKey, PortKey>(from, to));
            }

            var root = elements.Find(element => element.Predecessor.Length == 0 || !byName.ContainsKey(element.Predecessor));
            if (root == null)
                throw new InvalidDataException($"Sheet '{sheetName}': no start element (an EMS element without predecessor) found.");
            Place(root, explicitLinks, elements, sheetName);
            JoinCoincidentPorts(elements, links);

            var drafts = ExtractEdges(elements, links, root, sheetName);
            return AssembleNetwork(drafts, elements, root, sheetName);
        }

        private static void RequirePort(LayoutElement element, string port, string where)
        {
            if (!element.LocalPorts.ContainsKey(port))
                throw new InvalidDataException($"{where}: {element.Name} ({element.Type}) has no port '{port}'.");
        }

        private static void Place(LayoutElement root, List<KeyValuePair<PortKey, PortKey>> explicitLinks,
            List<LayoutElement> elements, string sheetName)
        {
            root.Placed = true;
            var queue = new Queue<LayoutElement>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var link in explicitLinks)
                {
                    PortKey known;
                    PortKey other;
                    if (link.Key.Element == current && !link.Value.Element.Placed)
                    {
                        known = link.Key;
                        other = link.Value;
                    }
                    else if (link.Value.Element == current && !link.Key.Element.Placed)
                    {
                        known = link.Value;
                        other = link.Key;
                    }
                    else
                    {
                        continue;
                    }

                    var anchor = GlobalPort(known.Element, known.Port);
                    var local = other.Element.LocalPorts[other.Port];
                    var heading = anchor.Heading - local.Heading;
                    other.Element.Origin = new Frame { Position = anchor.Position - Rotate(local.Position, heading), Heading = heading };
                    other.Element.Placed = true;
                    queue.Enqueue(other.Element);
                }
            }

            foreach (var element in elements)
            {
                if (!element.Placed)
                    throw new InvalidDataException(
                        $"Sheet '{sheetName}' row {element.Row}: {element.Name} is not connected to the first element ({root.Name}).");
            }
        }

        private static void JoinCoincidentPorts(List<LayoutElement> elements, Dictionary<PortKey, PortKey> links)
        {
            var freeOutputs = new List<PortKey>();
            var freeInputs = new List<PortKey>();
            var linkedTargets = new HashSet<PortKey>(links.Values);
            foreach (var element in elements)
            {
                foreach (var port in element.LocalPorts.Keys)
                {
                    var key = new PortKey(element, port);
                    if (port.StartsWith("Q", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!links.ContainsKey(key))
                            freeOutputs.Add(key);
                    }
                    else if (!linkedTargets.Contains(key))
                    {
                        freeInputs.Add(key);
                    }
                }
            }

            foreach (var output in freeOutputs)
            {
                var outputFrame = GlobalPort(output.Element, output.Port);
                foreach (var input in freeInputs)
                {
                    if (linkedTargets.Contains(input))
                        continue;
                    var inputFrame = GlobalPort(input.Element, input.Port);
                    var headingGap = Mathf.Abs(Mathf.DeltaAngle(outputFrame.Heading, inputFrame.Heading));
                    if (Vector3.Distance(outputFrame.Position, inputFrame.Position) <= JoinPositionToleranceMeters &&
                        headingGap <= JoinHeadingToleranceDegrees)
                    {
                        links[output] = input;
                        linkedTargets.Add(input);
                        break;
                    }
                }
            }
        }

        private static TrackSegmentDefinition PassSegment(LayoutElement element)
        {
            return element.Kind == ElementKind.Track
                ? TrackSegmentDefinition.Straight(element.Name, element.LengthMeters)
                : TrackSegmentDefinition.Curve(element.Name, element.Side, element.AngleDegrees, element.RadiusMeters);
        }

        private static List<EdgeDraft> ExtractEdges(List<LayoutElement> elements, Dictionary<PortKey, PortKey> links,
            LayoutElement root, string sheetName)
        {
            var drafts = new List<EdgeDraft>();
            var linkedTargets = new HashSet<PortKey>(links.Values);
            foreach (var element in elements)
            {
                switch (element.Kind)
                {
                    case ElementKind.SwitchOut:
                        foreach (var port in new[] { "Q0", "Q1" })
                        {
                            var draft = new EdgeDraft
                            {
                                Name = element.Name + "." + port,
                                Start = GlobalPort(element, "I0"),
                                StartSwitch = element,
                                StartPort = port
                            };
                            draft.Elements.Add(element.Name);
                            draft.Add(port == "Q1"
                                ? TrackSegmentDefinition.Straight(element.Name, element.LengthMeters)
                                : TrackSegmentDefinition.Curve(element.Name, element.Side, element.AngleDegrees, element.RadiusMeters),
                                element, port == "Q1" ? 1 : 0);
                            Follow(draft, new PortKey(element, port), links, elements.Count, null);
                            drafts.Add(draft);
                        }
                        break;
                    case ElementKind.SwitchIn:
                    {
                        var draft = new EdgeDraft
                        {
                            Name = element.Name + ".Q0",
                            Start = GlobalPort(element, "Q0"),
                            StartSwitch = element,
                            StartPort = "Q0"
                        };
                        draft.Elements.Add(element.Name);
                        Follow(draft, new PortKey(element, "Q0"), links, elements.Count, null);
                        drafts.Add(draft);
                        break;
                    }
                    default:
                        if (!linkedTargets.Contains(new PortKey(element, "I0")))
                        {
                            element.Used = true;
                            var draft = new EdgeDraft { Name = element.Name, FirstPassName = element.Name, Start = GlobalPort(element, "I0") };
                            draft.Elements.Add(element.Name);
                            draft.Add(PassSegment(element), element, 0);
                            Follow(draft, new PortKey(element, "Q0"), links, elements.Count, null);
                            drafts.Add(draft);
                        }
                        break;
                }
            }

            if (drafts.Count == 0)
            {
                root.Used = true;
                var draft = new EdgeDraft { Name = root.Name, Start = GlobalPort(root, "I0") };
                draft.Elements.Add(root.Name);
                draft.Add(PassSegment(root), root, 0);
                Follow(draft, new PortKey(root, "Q0"), links, elements.Count, root);
                if (!draft.IsRing)
                    throw new InvalidDataException($"Sheet '{sheetName}': the track from {root.Name} neither closes nor reaches a switch.");
                drafts.Add(draft);
            }

            foreach (var element in elements)
            {
                if ((element.Kind == ElementKind.Track || element.Kind == ElementKind.Curve) && !element.Used)
                    throw new InvalidDataException(
                        $"Sheet '{sheetName}' row {element.Row}: {element.Name} is not on any route " +
                        "(it cannot be reached from a switch or a free track end).");
            }
            return drafts;
        }

        private static void Follow(EdgeDraft draft, PortKey output, Dictionary<PortKey, PortKey> links, int limit,
            LayoutElement ringRoot)
        {
            var current = output;
            for (var guard = 0; guard <= limit + 2; guard++)
            {
                if (!links.TryGetValue(current, out var target))
                    return;

                var element = target.Element;
                if (ringRoot != null && element == ringRoot)
                {
                    draft.IsRing = true;
                    return;
                }

                switch (element.Kind)
                {
                    case ElementKind.SwitchOut:
                        draft.EndSwitch = element;
                        draft.EndPort = target.Port;
                        return;
                    case ElementKind.SwitchIn:
                        var straightInput = string.Equals(target.Port, "I1", StringComparison.OrdinalIgnoreCase);
                        draft.Add(straightInput
                            ? TrackSegmentDefinition.Straight(element.Name, element.LengthMeters)
                            : TrackSegmentDefinition.Curve(element.Name, element.Side, element.AngleDegrees, element.RadiusMeters),
                            element, straightInput ? 1 : 0);
                        draft.Elements.Add(element.Name);
                        draft.EndSwitch = element;
                        draft.EndPort = target.Port;
                        return;
                    default:
                        element.Used = true;
                        draft.Elements.Add(element.Name);
                        if (draft.FirstPassName == null)
                            draft.FirstPassName = element.Name;
                        draft.Add(PassSegment(element), element, 0);
                        current = new PortKey(element, "Q0");
                        break;
                }
            }
            throw new InvalidDataException($"The route starting at {draft.Name} loops without reaching a switch.");
        }

        private static TrackNetwork AssembleNetwork(List<EdgeDraft> drafts, List<LayoutElement> elements,
            LayoutElement root, string sheetName)
        {
            var main = drafts.FindIndex(draft => draft.Elements.Contains(root.Name));
            if (main > 0)
            {
                var draft = drafts[main];
                drafts.RemoveAt(main);
                drafts.Insert(0, draft);
            }

            var nodes = new List<TrackNode>();
            foreach (var element in elements)
            {
                if (element.Kind != ElementKind.SwitchOut && element.Kind != ElementKind.SwitchIn)
                    continue;
                var isSplit = element.Kind == ElementKind.SwitchOut;
                var position = GlobalPort(element, isSplit ? "I0" : "Q0").Position;
                element.Node = new TrackNode(nodes.Count, element.Name, isSplit ? TrackNodeKind.Split : TrackNodeKind.Merge,
                    position, Mathf.Max(1f, element.LengthMeters + 0.5f));
                nodes.Add(element.Node);
            }

            var edges = new List<TrackEdge>();
            foreach (var draft in drafts)
            {
                var length = 0f;
                foreach (var segment in draft.Segments)
                {
                    length += segment.Type == TrackSegmentType.Straight
                        ? segment.StraightLengthMeters
                        : segment.RadiusMeters * segment.AngleDegrees * Mathf.Deg2Rad;
                }

                var settings = new TrackBuildSettings(length, draft.IsRing, 0.03f, 1f, Mathf.Max(0.1f, length), 2f);
                var result = TrackGeometryBuilder.Build(draft.Start.Position, draft.Start.Heading, draft.Segments, settings);
                if (!result.Report.IsValid)
                {
                    var messages = new List<string>();
                    foreach (var issue in result.Report.Issues)
                    {
                        if (issue.Severity == TrackIssueSeverity.Error)
                            messages.Add(issue.Message);
                    }
                    throw new InvalidDataException(
                        $"Sheet '{sheetName}': route {draft.Name} is invalid: {string.Join("; ", messages)}");
                }
                var edge = new TrackEdge(edges.Count, draft.FirstPassName ?? draft.Name, result.Path,
                    draft.IsRing && result.Path.IsClosed);
                edge.SetBarcodes(draft.BarcodeStarts.ToArray(), draft.BarcodeStops.ToArray());
                edges.Add(edge);
            }

            for (var i = 0; i < drafts.Count; i++)
            {
                var draft = drafts[i];
                var edge = edges[i];
                if (draft.EndSwitch != null)
                {
                    edge.EndNode = draft.EndSwitch.Node;
                    draft.EndSwitch.Node.AddIn(edge);
                    if (draft.EndSwitch.Kind == ElementKind.SwitchIn &&
                        string.Equals(draft.EndPort, "I1", StringComparison.OrdinalIgnoreCase))
                        draft.EndSwitch.Node.PriorityInIndex = draft.EndSwitch.Node.InEdges.Count - 1;
                }
            }

            // Outgoing edges are registered per switch in port order (Q0 branch, Q1 straight).
            foreach (var element in elements)
            {
                if (element.Node == null)
                    continue;
                foreach (var port in new[] { "Q0", "Q1" })
                {
                    for (var i = 0; i < drafts.Count; i++)
                    {
                        if (drafts[i].StartSwitch != element || drafts[i].StartPort != port)
                            continue;
                        edges[i].StartNode = element.Node;
                        element.Node.AddOut(edges[i]);
                    }
                }
            }

            foreach (var node in nodes)
            {
                if (node.Kind == TrackNodeKind.Split && node.InEdges.Count > 1)
                    throw new InvalidDataException($"Sheet '{sheetName}': switch {node.Name} has more than one incoming route.");
                node.OutLabels = node.Kind == TrackNodeKind.Split ? new[] { "Q0 branch", "Q1 straight" } : new[] { "Q0" };
            }

            return new TrackNetwork(edges, nodes);
        }

        private static float Millimeters(string text, string column, string where)
        {
            // Some Layouter cells hold "x y z"; the first non-zero value is the one that applies.
            foreach (var token in text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (TrackFileReader.TryParseNumber(token, out var value) && Mathf.Abs(value) > 0f)
                    return value * 0.001f;
            }
            throw new InvalidDataException($"{where}: {column} must be a non-zero number in mm (got '{text}').");
        }

        private static float Number(string text, string column, string where)
        {
            if (!TrackFileReader.TryParseNumber(text, out var value) || value <= 0f)
                throw new InvalidDataException($"{where}: {column} must be a number greater than zero (got '{text}').");
            return value;
        }

        private static Dictionary<string, int> HeaderColumns(Dictionary<int, string> row)
        {
            var result = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var cell in row)
            {
                var key = cell.Value.Trim().TrimStart('+').Trim().ToLower(CultureInfo.InvariantCulture);
                if (key.Length > 0 && !result.ContainsKey(key))
                    result[key] = cell.Key;
            }
            return result;
        }

        private static string Text(Dictionary<int, string> row, int column)
        {
            return row.TryGetValue(column, out var text) ? text.Trim() : string.Empty;
        }
    }
}
