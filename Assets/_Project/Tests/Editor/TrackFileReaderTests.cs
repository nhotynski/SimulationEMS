using System.IO;
using System.IO.Compression;
using System.Text;
using IndustrialConveyor.Simulation.Track;
using NUnit.Framework;

namespace IndustrialConveyor.Tests
{
    public sealed class TrackFileReaderTests
    {
        private static readonly string[][] ClosedRectangleSegments =
        {
            new[] { "Label", "Type", "Length_m", "Direction", "Angle_deg", "Radius_m" },
            new[] { "North", "Straight", "20", "", "", "" },
            new[] { "C1", "Curve", "", "Right", "90", "5" },
            new[] { "East", "Straight", "10,5", "", "", "" },
            new[] { "C2", "Curve", "", "Right", "90", "5" },
            new[] { "South", "Straight", "20", "", "", "" },
            new[] { "C3", "Curve", "", "Right", "90", "5" },
            new[] { "West", "Straight", "10,5", "", "", "" },
            new[] { "C4", "Curve", "", "Right", "90", "5" }
        };

        [Test]
        public void Read_ClosedRectangle_BuildsValidClosedTrackWithDefaultTargetLength()
        {
            var parameters = new[] { new[] { "Parameter", "Value" }, new[] { "InitialHeading_deg", "0" } };
            using (var stream = CreateWorkbook(parameters, ClosedRectangleSegments))
            {
                var network = TrackFileReader.Read(stream);
                var path = network.Edges[0].Path;

                Assert.IsTrue(network.IsRing);
                Assert.AreEqual(8, path.Segments.Count);
                Assert.AreEqual(10.5f, path.Segments[2].LengthMeters, 0.0001f);
                Assert.IsTrue(path.IsClosed);
                Assert.AreEqual(92.416f, path.TotalLengthMeters, 0.01f);
            }
        }

        [Test]
        public void Read_UnknownParameter_ReportsTheName()
        {
            var parameters = new[] { new[] { "Parameter", "Value" }, new[] { "Headin_deg", "0" } };
            using (var stream = CreateWorkbook(parameters, ClosedRectangleSegments))
            {
                var exception = Assert.Throws<InvalidDataException>(() => TrackFileReader.Read(stream));
                StringAssert.Contains("Headin_deg", exception.Message);
            }
        }

        [Test]
        public void Read_InvalidSegmentType_ReportsSheetAndRow()
        {
            var segments = new[]
            {
                new[] { "Label", "Type", "Length_m", "Direction", "Angle_deg", "Radius_m" },
                new[] { "Bad", "Spiral", "20", "", "", "" }
            };
            using (var stream = CreateWorkbook(new[] { new[] { "Parameter", "Value" } }, segments))
            {
                var exception = Assert.Throws<InvalidDataException>(() => TrackFileReader.Read(stream));
                StringAssert.Contains("row 2", exception.Message);
            }
        }

        [Test]
        public void Read_LayouterSheet_BuildsSplitWithBranchAndSwitchesRoute()
        {
            var layout = new[]
            {
                new[] { "+ proto type", "+ node name", "ConnPre", "ConnExit", "ConnEntry", "Length", "Angle", "Radius" },
                new[] { "BASIC_RollerConveyor", "R1", "", "", "", "6000", "", "" },
                new[] { "EMS_Track", "T1", "", "", "", "20000", "", "" },
                new[] { "EMS_CurveRight", "C2", "T1", "Q0", "I0", "", "90", "5000" },
                new[] { "EMS_Track", "T3", "C2", "Q0", "I0", "9500", "", "" },
                new[] { "EMS_SwitchLeftOut", "S4", "T3", "Q0", "I0", "1000", "45", "5000" },
                new[] { "EMS_CurveLeft", "B1", "S4", "Q0", "I0", "", "45", "5000" },
                new[] { "EMS_Track", "B2", "B1", "Q0", "I0", "3000", "", "" },
                new[] { "EMS_CurveRight", "C5", "S4", "Q1", "I0", "", "90", "5000" },
                new[] { "EMS_Track", "T6", "C5", "Q0", "I0", "20000", "", "" },
                new[] { "EMS_CurveRight", "C7", "T6", "Q0", "I0", "", "90", "5000" },
                new[] { "EMS_Track", "T8", "C7", "Q0", "I0", "10500", "", "" },
                new[] { "EMS_CurveRight", "C9", "T8", "Q0", "I0", "", "90", "5000" }
            };
            using (var stream = CreateSingleSheetWorkbook("Arkusz1", layout))
            {
                var network = TrackFileReader.Read(stream);
                var split = network.FindNode("S4");

                Assert.IsFalse(network.IsRing);
                Assert.AreEqual(2, network.Edges.Count);
                Assert.IsNotNull(split);
                Assert.AreEqual(TrackNodeKind.Split, split.Kind);
                Assert.AreEqual(2, split.OutEdges.Count);

                // Default state is the straight exit, which closes the main loop.
                var main = network.Edges[0];
                Assert.AreEqual(1, split.SelectedOutIndex);
                Assert.AreSame(main, network.NextEdge(main));
                Assert.IsTrue(network.IsMainRouteClosed);
                Assert.AreEqual(92.416f, network.MainRouteLengthMeters, 0.01f);

                split.ForceOutIndex(0);
                Assert.AreNotSame(main, network.NextEdge(main));
                Assert.IsNull(network.NextEdge(network.NextEdge(main)));
            }
        }

        [Test]
        public void Read_LayouterSheet_UnsupportedElement_NamesIt()
        {
            var layout = new[]
            {
                new[] { "+ proto type", "+ node name", "ConnPre", "ConnExit", "ConnEntry", "Length", "Angle", "Radius" },
                new[] { "EMS_Track", "T1", "", "", "", "20000", "", "" },
                new[] { "EMS_TrackLift", "L2", "T1", "Q0", "I0", "5000", "", "" }
            };
            using (var stream = CreateSingleSheetWorkbook("Arkusz1", layout))
            {
                var exception = Assert.Throws<InvalidDataException>(() => TrackFileReader.Read(stream));
                StringAssert.Contains("L2", exception.Message);
            }
        }

        [Test]
        public void Read_NotAWorkbook_Throws()
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("not a zip")))
                Assert.Throws<InvalidDataException>(() => TrackFileReader.Read(stream));
        }

        [Test]
        public void TryLoad_OpenTrack_IsRejectedWithClosureMessage()
        {
            var segments = new[]
            {
                new[] { "Label", "Type", "Length_m", "Direction", "Angle_deg", "Radius_m" },
                new[] { "Only", "Straight", "20", "", "", "" }
            };
            var path = Path.Combine(Path.GetTempPath(), "open-track-test.xlsx");
            try
            {
                using (var stream = CreateWorkbook(new[] { new[] { "Parameter", "Value" } }, segments))
                    File.WriteAllBytes(path, ((MemoryStream)stream).ToArray());

                Assert.IsFalse(TrackSource.TryLoad(path, out var configuration, out var error));
                Assert.IsNull(configuration);
                Assert.IsNotEmpty(error);
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static Stream CreateWorkbook(string[][] parameters, string[][] segments)
        {
            var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                Write(archive, "xl/workbook.xml",
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                    "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>" +
                    "<sheet name=\"Parameters\" sheetId=\"1\" r:id=\"rId1\"/>" +
                    "<sheet name=\"Segments\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>");
                Write(archive, "xl/_rels/workbook.xml.rels",
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/>" +
                    "<Relationship Id=\"rId2\" Target=\"worksheets/sheet2.xml\"/></Relationships>");
                Write(archive, "xl/worksheets/sheet1.xml", SheetXml(parameters));
                Write(archive, "xl/worksheets/sheet2.xml", SheetXml(segments));
            }
            memory.Position = 0;
            return memory;
        }

        private static Stream CreateSingleSheetWorkbook(string sheetName, string[][] rows)
        {
            var memory = new MemoryStream();
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            {
                Write(archive, "xl/workbook.xml",
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                    "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets>" +
                    "<sheet name=\"" + sheetName + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                Write(archive, "xl/_rels/workbook.xml.rels",
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
                Write(archive, "xl/worksheets/sheet1.xml", SheetXml(rows));
            }
            memory.Position = 0;
            return memory;
        }

        private static string SheetXml(string[][] rows)
        {
            var builder = new StringBuilder(
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>");
            for (var r = 0; r < rows.Length; r++)
            {
                builder.Append("<row r=\"").Append(r + 1).Append("\">");
                for (var c = 0; c < rows[r].Length; c++)
                {
                    if (rows[r][c].Length == 0)
                        continue;
                    builder.Append("<c r=\"").Append((char)('A' + c)).Append(r + 1).Append("\" t=\"inlineStr\"><is><t>")
                        .Append(rows[r][c]).Append("</t></is></c>");
                }
                builder.Append("</row>");
            }
            return builder.Append("</sheetData></worksheet>").ToString();
        }

        private static void Write(ZipArchive archive, string name, string content)
        {
            using (var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false)))
                writer.Write(content);
        }
    }
}
