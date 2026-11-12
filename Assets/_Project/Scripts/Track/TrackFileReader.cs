using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using UnityEngine;

namespace IndustrialConveyor.Simulation.Track
{
    /// <summary>
    /// Reads a track from an Excel workbook (.xlsx or .xlsm) without external libraries. Two layouts are recognised:
    /// a WinMOD SIMLINE Layouter sheet (EMS_Track / EMS_Curve* / EMS_Switch* elements chained by predecessor, see
    /// <see cref="TrackLayoutReader"/>) or sheets "Parameters" (name/value, row 1 header) and "Segments"
    /// (header Label, Type, Length_m, Direction, Angle_deg, Radius_m) describing a simple loop.
    /// </summary>
    public static class TrackFileReader
    {
        public const string ParametersSheetName = "Parameters";
        public const string SegmentsSheetName = "Segments";

        private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

        /// <summary>Reads and parses a workbook; throws <see cref="InvalidDataException"/> with a user-facing message.</summary>
        public static TrackNetwork Read(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidDataException("Track file path is empty.");
            if (!File.Exists(path))
                throw new FileNotFoundException("Track file not found: " + path);

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    return Read(stream);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (IOException exception)
            {
                throw new InvalidDataException("Cannot read track file: " + exception.Message, exception);
            }
        }

        /// <summary>Parses a workbook from a stream.</summary>
        public static TrackNetwork Read(Stream stream)
        {
            ZipArchive archive;
            try
            {
                archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
            }
            catch (InvalidDataException)
            {
                throw new InvalidDataException("The file is not a valid Excel workbook (.xlsx / .xlsm).");
            }

            using (archive)
            {
                var sharedStrings = ReadSharedStrings(archive);
                var sheets = ReadSheetPaths(archive);
                var parametersPath = FindSheetPath(sheets, ParametersSheetName);
                var segmentsPath = FindSheetPath(sheets, SegmentsSheetName);
                if (parametersPath != null && segmentsPath != null)
                {
                    var configuration = Build(ReadSheet(archive, parametersPath, ParametersSheetName, sharedStrings),
                        ReadSheet(archive, segmentsPath, SegmentsSheetName, sharedStrings));
                    try
                    {
                        return TrackNetwork.FromConfiguration(configuration);
                    }
                    catch (InvalidOperationException exception)
                    {
                        throw new InvalidDataException(exception.Message, exception);
                    }
                }

                foreach (var sheet in sheets)
                {
                    var rows = ReadSheet(archive, sheet.Value, sheet.Key, sharedStrings);
                    if (TrackLayoutReader.TryRead(rows, sheet.Key, out var network))
                        return network;
                }
            }

            throw new InvalidDataException(
                $"No track found. Expected sheets '{ParametersSheetName}' + '{SegmentsSheetName}', " +
                "or a WinMOD SIMLINE Layouter sheet with a 'ConnPre' header.");
        }

        private static string FindSheetPath(List<KeyValuePair<string, string>> sheets, string name)
        {
            foreach (var sheet in sheets)
            {
                if (string.Equals(sheet.Key, name, StringComparison.OrdinalIgnoreCase))
                    return sheet.Value;
            }
            return null;
        }

        private static TrackConfiguration Build(Dictionary<int, Dictionary<int, string>> parameterRows,
            Dictionary<int, Dictionary<int, string>> segmentRows)
        {
            var segments = ParseSegments(segmentRows);
            if (segments.Count == 0)
                throw new InvalidDataException($"Sheet '{SegmentsSheetName}' contains no segments.");

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "StartX_m", "StartY_m", "StartZ_m", "InitialHeading_deg", "TargetLength_m", "RequireClosedLoop",
                "ClosurePositionTolerance_m", "ClosureHeadingTolerance_deg", "TargetLengthTolerance_m",
                "IntersectionSampleSpacing_m"
            };
            foreach (var row in SortedRows(parameterRows))
            {
                if (row.Key == 1)
                    continue;
                var key = Cell(row.Value, 0);
                if (string.IsNullOrEmpty(key))
                    continue;
                if (!known.Contains(key))
                    throw new InvalidDataException($"Sheet '{ParametersSheetName}' row {row.Key}: unknown parameter '{key}'.");
                values[key] = Cell(row.Value, 1);
            }

            var start = new Vector3(
                Number(values, "StartX_m", 0f), Number(values, "StartY_m", 0f), Number(values, "StartZ_m", 0f));
            var targetLength = values.ContainsKey("TargetLength_m") && !string.IsNullOrEmpty(values["TargetLength_m"])
                ? Number(values, "TargetLength_m", 0f)
                : SumLength(segments);
            return TrackConfiguration.Create(start, Number(values, "InitialHeading_deg", 0f), targetLength,
                Boolean(values, "RequireClosedLoop", true),
                Number(values, "ClosurePositionTolerance_m", 0.01f),
                Number(values, "ClosureHeadingTolerance_deg", 0.1f),
                Number(values, "TargetLengthTolerance_m", 0.1f),
                Number(values, "IntersectionSampleSpacing_m", 2f),
                segments);
        }

        private static List<TrackSegmentDefinition> ParseSegments(Dictionary<int, Dictionary<int, string>> rows)
        {
            var result = new List<TrackSegmentDefinition>();
            Dictionary<int, string> header = null;
            foreach (var row in SortedRows(rows))
            {
                if (row.Key == 1)
                {
                    header = row.Value;
                    continue;
                }
                if (header == null)
                    throw new InvalidDataException($"Sheet '{SegmentsSheetName}' needs a header row in row 1.");
                if (IsBlank(row.Value))
                    continue;

                var label = ColumnValue(row.Value, header, "Label");
                if (string.IsNullOrEmpty(label))
                    label = "Segment " + (result.Count + 1).ToString(CultureInfo.InvariantCulture);
                var typeText = ColumnValue(row.Value, header, "Type");
                var where = $"Sheet '{SegmentsSheetName}' row {row.Key}";

                if (IsOneOf(typeText, "straight", "s", "prosta"))
                {
                    result.Add(TrackSegmentDefinition.Straight(label, RequiredNumber(row.Value, header, "Length_m", where)));
                }
                else if (IsOneOf(typeText, "curve", "c", "luk", "łuk"))
                {
                    var directionText = ColumnValue(row.Value, header, "Direction");
                    TrackTurnDirection direction;
                    if (IsOneOf(directionText, "left", "l", "lewo", "lewy"))
                        direction = TrackTurnDirection.Left;
                    else if (IsOneOf(directionText, "right", "r", "prawo", "prawy"))
                        direction = TrackTurnDirection.Right;
                    else
                        throw new InvalidDataException($"{where}: Direction must be Left or Right (got '{directionText}').");
                    result.Add(TrackSegmentDefinition.Curve(label, direction,
                        RequiredNumber(row.Value, header, "Angle_deg", where),
                        RequiredNumber(row.Value, header, "Radius_m", where)));
                }
                else
                {
                    throw new InvalidDataException($"{where}: Type must be Straight or Curve (got '{typeText}').");
                }
            }
            return result;
        }

        private static float SumLength(List<TrackSegmentDefinition> segments)
        {
            var total = 0f;
            foreach (var segment in segments)
            {
                total += segment.Type == TrackSegmentType.Straight
                    ? segment.StraightLengthMeters
                    : segment.RadiusMeters * segment.AngleDegrees * Mathf.Deg2Rad;
            }
            return total;
        }

        private static string ColumnValue(Dictionary<int, string> row, Dictionary<int, string> header, string name)
        {
            foreach (var column in header)
            {
                if (string.Equals(column.Value.Trim(), name, StringComparison.OrdinalIgnoreCase))
                    return Cell(row, column.Key);
            }
            throw new InvalidDataException($"Sheet '{SegmentsSheetName}' is missing the '{name}' column.");
        }

        private static float RequiredNumber(Dictionary<int, string> row, Dictionary<int, string> header, string name, string where)
        {
            var text = ColumnValue(row, header, name);
            if (!TryParseNumber(text, out var value))
                throw new InvalidDataException($"{where}: '{name}' must be a number (got '{text}').");
            return value;
        }

        private static float Number(Dictionary<string, string> values, string key, float fallback)
        {
            if (!values.TryGetValue(key, out var text) || string.IsNullOrEmpty(text))
                return fallback;
            if (!TryParseNumber(text, out var value))
                throw new InvalidDataException($"Sheet '{ParametersSheetName}': '{key}' must be a number (got '{text}').");
            return value;
        }

        private static bool Boolean(Dictionary<string, string> values, string key, bool fallback)
        {
            if (!values.TryGetValue(key, out var text) || string.IsNullOrEmpty(text))
                return fallback;
            if (IsOneOf(text, "true", "1", "yes", "tak"))
                return true;
            if (IsOneOf(text, "false", "0", "no", "nie"))
                return false;
            throw new InvalidDataException($"Sheet '{ParametersSheetName}': '{key}' must be TRUE or FALSE (got '{text}').");
        }

        internal static bool TryParseNumber(string text, out float value)
        {
            value = 0f;
            if (string.IsNullOrWhiteSpace(text))
                return false;
            return float.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                   !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private static bool IsOneOf(string text, params string[] options)
        {
            if (text == null)
                return false;
            var trimmed = text.Trim();
            foreach (var option in options)
            {
                if (string.Equals(trimmed, option, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string Cell(Dictionary<int, string> row, int column)
        {
            return row.TryGetValue(column, out var text) ? text.Trim() : string.Empty;
        }

        private static bool IsBlank(Dictionary<int, string> row)
        {
            foreach (var cell in row.Values)
            {
                if (!string.IsNullOrWhiteSpace(cell))
                    return false;
            }
            return true;
        }

        private static List<KeyValuePair<int, Dictionary<int, string>>> SortedRows(Dictionary<int, Dictionary<int, string>> rows)
        {
            var list = new List<KeyValuePair<int, Dictionary<int, string>>>(rows);
            list.Sort((left, right) => left.Key.CompareTo(right.Key));
            return list;
        }

        private static List<string> ReadSharedStrings(ZipArchive archive)
        {
            var result = new List<string>();
            var entry = archive.GetEntry("xl/sharedStrings.xml");
            if (entry == null)
                return result;

            var document = LoadXml(entry);
            foreach (var item in document.Root.Elements(Main + "si"))
            {
                var builder = new StringBuilder();
                foreach (var text in item.Descendants(Main + "t"))
                {
                    if (text.Parent != null && text.Parent.Name != Main + "rPh")
                        builder.Append(text.Value);
                }
                result.Add(builder.ToString());
            }
            return result;
        }

        private static List<KeyValuePair<string, string>> ReadSheetPaths(ZipArchive archive)
        {
            var workbookEntry = archive.GetEntry("xl/workbook.xml");
            var relationshipsEntry = archive.GetEntry("xl/_rels/workbook.xml.rels");
            if (workbookEntry == null || relationshipsEntry == null)
                throw new InvalidDataException("The file is not a valid Excel workbook (.xlsx / .xlsm).");

            var targets = new Dictionary<string, string>();
            foreach (var relationship in LoadXml(relationshipsEntry).Root.Elements(PackageRelationships + "Relationship"))
            {
                var id = (string)relationship.Attribute("Id");
                var target = (string)relationship.Attribute("Target");
                if (id == null || target == null)
                    continue;
                targets[id] = target.StartsWith("/", StringComparison.Ordinal) ? target.TrimStart('/') : "xl/" + target;
            }

            var result = new List<KeyValuePair<string, string>>();
            foreach (var sheet in LoadXml(workbookEntry).Root.Element(Main + "sheets").Elements(Main + "sheet"))
            {
                var name = (string)sheet.Attribute("name");
                var id = (string)sheet.Attribute(Relationships + "id");
                if (name != null && id != null && targets.TryGetValue(id, out var path))
                    result.Add(new KeyValuePair<string, string>(name, path));
            }
            return result;
        }

        internal static Dictionary<int, Dictionary<int, string>> ReadSheet(ZipArchive archive, string path,
            string sheetName, List<string> sharedStrings)
        {
            var entry = archive.GetEntry(path);
            if (entry == null)
                throw new InvalidDataException($"Sheet '{sheetName}' is missing its data.");

            var rows = new Dictionary<int, Dictionary<int, string>>();
            var data = LoadXml(entry).Root.Element(Main + "sheetData");
            if (data == null)
                return rows;

            foreach (var cell in data.Descendants(Main + "c"))
            {
                var reference = (string)cell.Attribute("r");
                if (!TryParseReference(reference, out var row, out var column))
                    continue;
                var text = CellText(cell, sharedStrings);
                if (!rows.TryGetValue(row, out var columns))
                {
                    columns = new Dictionary<int, string>();
                    rows[row] = columns;
                }
                columns[column] = text;
            }
            return rows;
        }

        private static string CellText(XElement cell, List<string> sharedStrings)
        {
            var type = (string)cell.Attribute("t");
            if (type == "inlineStr")
            {
                var builder = new StringBuilder();
                foreach (var text in cell.Descendants(Main + "t"))
                    builder.Append(text.Value);
                return builder.ToString();
            }

            var value = (string)cell.Element(Main + "v") ?? string.Empty;
            if (type == "s")
            {
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
                    index >= 0 && index < sharedStrings.Count)
                    return sharedStrings[index];
                return string.Empty;
            }
            return value;
        }

        private static bool TryParseReference(string reference, out int row, out int column)
        {
            row = 0;
            column = 0;
            if (string.IsNullOrEmpty(reference))
                return false;

            var i = 0;
            while (i < reference.Length && char.IsLetter(reference[i]))
            {
                column = column * 26 + (char.ToUpperInvariant(reference[i]) - 'A' + 1);
                i++;
            }
            column--;
            return i > 0 && i < reference.Length &&
                   int.TryParse(reference.Substring(i), NumberStyles.Integer, CultureInfo.InvariantCulture, out row);
        }

        private static XDocument LoadXml(ZipArchiveEntry entry)
        {
            using (var stream = entry.Open())
                return XDocument.Load(stream);
        }
    }
}
