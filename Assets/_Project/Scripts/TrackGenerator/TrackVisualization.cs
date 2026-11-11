using System.Collections.Generic;
using IndustrialConveyor.Simulation.Track;
using UnityEngine;

namespace IndustrialConveyor.Simulation.TrackGenerator
{
    /// <summary>Builds a separate rendered rail and support mesh from mathematical track geometry.</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class TrackVisualization : MonoBehaviour
    {
        [SerializeField] private TrackConfiguration configuration;
        [SerializeField, Min(0.1f)] private float railHeightMeters = 8f;
        [SerializeField, Min(0.01f)] private float railWidthMeters = 0.3f;
        [SerializeField, Min(0.01f)] private float railThicknessMeters = 0.2f;
        [SerializeField, Min(0.1f)] private float railSampleSpacingMeters = 1f;
        [SerializeField, Min(0.1f)] private float supportSpacingMeters = 10f;
        [SerializeField, Min(0.01f)] private float supportWidthMeters = 0.12f;
        [SerializeField, Min(0.01f)] private float supportDepthMeters = 0.12f;
        [SerializeField, Min(0.1f)] private float supportSpanMeters = 1.8f;
        [SerializeField, Min(0f)] private float floorMarginMeters = 2000f;
        [SerializeField] private Material railMaterial;
        [SerializeField] private Color railColor = new Color(0.38f, 0.42f, 0.46f);
        [SerializeField] private Color supportColor = new Color(0.22f, 0.27f, 0.3f);
        [SerializeField] private Color floorColor = new Color(0.58f, 0.6f, 0.57f);

        private Mesh generatedMesh;
        private Material generatedRailMaterial;
        private Material generatedSupportMaterial;
        private Material generatedFloorMaterial;

        /// <summary>Gets the assigned source-of-truth track definition.</summary>
        public TrackConfiguration Configuration => configuration;

        /// <summary>Gets the latest calculated path and diagnostics.</summary>
        public TrackBuildResult BuildResult { get; private set; }

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled)
                Rebuild();
        }

        private void OnDisable()
        {
            DestroyGeneratedAssets();
        }

        /// <summary>Rebuilds visual geometry without changing the source configuration.</summary>
        public void Rebuild()
        {
            DestroyGeneratedAssets();
            if (configuration == null)
            {
                BuildResult = null;
                return;
            }

            BuildResult = TrackGeometryBuilder.Build(configuration);
            generatedMesh = BuildMesh();
            GetComponent<MeshFilter>().sharedMesh = generatedMesh;

            var renderer = GetComponent<MeshRenderer>();
            CreateMaterials();
            renderer.sharedMaterials = new[] { generatedRailMaterial, generatedSupportMaterial, generatedFloorMaterial };
        }

        private Mesh BuildMesh()
        {
            var vertices = new List<Vector3>();
            var railTriangles = new List<int>();
            var supportTriangles = new List<int>();
            var floorTriangles = new List<int>();
            var renderablePath = BuildResult.Path;

            foreach (var segment in renderablePath.Segments)
            {
                var steps = Mathf.Max(1, Mathf.CeilToInt(segment.LengthMeters / Mathf.Max(0.1f, railSampleSpacingMeters)));
                var previous = segment.SampleAt(0f);
                for (var step = 1; step <= steps; step++)
                {
                    var current = segment.SampleAt(segment.LengthMeters * step / steps);
                    AddRailSection(previous, current, vertices, railTriangles);
                    previous = current;
                }
            }

            if (renderablePath.TotalLengthMeters > 0f)
            {
                var supportCount = Mathf.FloorToInt(renderablePath.TotalLengthMeters / Mathf.Max(0.1f, supportSpacingMeters));
                for (var i = 0; i <= supportCount; i++)
                {
                    var distance = Mathf.Min(i * supportSpacingMeters, renderablePath.TotalLengthMeters);
                    AddSupport(renderablePath.SampleAtDistance(distance), vertices, supportTriangles);
                }
                if (supportCount == 0 || supportCount * supportSpacingMeters < renderablePath.TotalLengthMeters - 0.01f)
                    AddSupport(renderablePath.SampleAtDistance(renderablePath.TotalLengthMeters), vertices, supportTriangles);
            }

            AddFloor(renderablePath, vertices, floorTriangles);

            var mesh = new Mesh { name = "Generated Overhead Track", hideFlags = HideFlags.DontSave };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            var textureCoordinates = new List<Vector2>(vertices.Count);
            foreach (var vertex in vertices)
                textureCoordinates.Add(new Vector2(vertex.x / 10f, vertex.z / 10f));
            mesh.SetUVs(0, textureCoordinates);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(railTriangles, 0);
            mesh.SetTriangles(supportTriangles, 1);
            mesh.SetTriangles(floorTriangles, 2);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private void AddFloor(TrackPath path, List<Vector3> vertices, List<int> triangles)
        {
            if (path.TotalLengthMeters <= 0f)
                return;

            var minimumX = float.PositiveInfinity;
            var maximumX = float.NegativeInfinity;
            var minimumZ = float.PositiveInfinity;
            var maximumZ = float.NegativeInfinity;
            var sampleCount = Mathf.Max(1, Mathf.CeilToInt(path.TotalLengthMeters / 5f));
            for (var index = 0; index <= sampleCount; index++)
            {
                var distance = path.TotalLengthMeters * index / sampleCount;
                var position = path.SampleAtDistance(distance).Position;
                minimumX = Mathf.Min(minimumX, position.x);
                maximumX = Mathf.Max(maximumX, position.x);
                minimumZ = Mathf.Min(minimumZ, position.z);
                maximumZ = Mathf.Max(maximumZ, position.z);
            }

            minimumX -= floorMarginMeters;
            maximumX += floorMarginMeters;
            minimumZ -= floorMarginMeters;
            maximumZ += floorMarginMeters;

            var firstVertex = vertices.Count;
            const float floorHeight = -0.05f;
            vertices.Add(new Vector3(minimumX, floorHeight, minimumZ));
            vertices.Add(new Vector3(minimumX, floorHeight, maximumZ));
            vertices.Add(new Vector3(maximumX, floorHeight, maximumZ));
            vertices.Add(new Vector3(maximumX, floorHeight, minimumZ));
            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 1);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex);
            triangles.Add(firstVertex + 2);
            triangles.Add(firstVertex + 3);
        }

        private void AddRailSection(TrackSample from, TrackSample to, List<Vector3> vertices, List<int> triangles)
        {
            var startCenter = from.Position + Vector3.up * railHeightMeters;
            var endCenter = to.Position + Vector3.up * railHeightMeters;
            var halfWidth = railWidthMeters * 0.5f;
            var halfHeight = railThicknessMeters * 0.5f;
            var start = AddSectionVertices(startCenter, from.Right, halfWidth, halfHeight, vertices);
            var end = AddSectionVertices(endCenter, to.Right, halfWidth, halfHeight, vertices);

            AddQuad(start[0], start[1], end[1], end[0], triangles);
            AddQuad(start[1], start[2], end[2], end[1], triangles);
            AddQuad(start[2], start[3], end[3], end[2], triangles);
            AddQuad(start[3], start[0], end[0], end[3], triangles);
        }

        private int[] AddSectionVertices(Vector3 center, Vector3 right, float halfWidth, float halfHeight,
            List<Vector3> vertices)
        {
            var first = vertices.Count;
            vertices.Add(center - right * halfWidth - Vector3.up * halfHeight);
            vertices.Add(center + right * halfWidth - Vector3.up * halfHeight);
            vertices.Add(center + right * halfWidth + Vector3.up * halfHeight);
            vertices.Add(center - right * halfWidth + Vector3.up * halfHeight);
            return new[] { first, first + 1, first + 2, first + 3 };
        }

        private void AddSupport(TrackSample sample, List<Vector3> vertices, List<int> triangles)
        {
            var top = railHeightMeters + railThicknessMeters + 0.35f;
            var postHeight = Mathf.Max(0.1f, top);
            var postCenter = Vector3.up * (postHeight * 0.5f);
            var halfSpan = supportSpanMeters * 0.5f;
            AddBox(postCenter - sample.Right * halfSpan, sample.Right, sample.Tangent,
                supportWidthMeters, postHeight, supportDepthMeters, vertices, triangles);
            AddBox(postCenter + sample.Right * halfSpan, sample.Right, sample.Tangent,
                supportWidthMeters, postHeight, supportDepthMeters, vertices, triangles);
            AddBox(Vector3.up * top, sample.Right, sample.Tangent,
                supportSpanMeters + supportWidthMeters, supportWidthMeters, supportDepthMeters,
                vertices, triangles);
        }

        private static void AddBox(Vector3 center, Vector3 right, Vector3 forward, float width, float height,
            float depth, List<Vector3> vertices, List<int> triangles)
        {
            var x = right.normalized * width * 0.5f;
            var y = Vector3.up * height * 0.5f;
            var z = forward.normalized * depth * 0.5f;
            var points = new[]
            {
                center - x - y - z, center + x - y - z, center - x + y - z, center + x + y - z,
                center - x - y + z, center + x - y + z, center - x + y + z, center + x + y + z
            };
            AddQuad(points[0], points[2], points[3], points[1], vertices, triangles);
            AddQuad(points[4], points[5], points[7], points[6], vertices, triangles);
            AddQuad(points[0], points[4], points[6], points[2], vertices, triangles);
            AddQuad(points[1], points[3], points[7], points[5], vertices, triangles);
            AddQuad(points[0], points[1], points[5], points[4], vertices, triangles);
            AddQuad(points[2], points[6], points[7], points[3], vertices, triangles);
        }

        private static void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d,
            List<Vector3> vertices, List<int> triangles)
        {
            var first = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            vertices.Add(d);
            triangles.Add(first);
            triangles.Add(first + 1);
            triangles.Add(first + 2);
            triangles.Add(first);
            triangles.Add(first + 2);
            triangles.Add(first + 3);
        }

        private static void AddQuad(int a, int b, int c, int d, List<int> triangles)
        {
            triangles.Add(a);
            triangles.Add(b);
            triangles.Add(c);
            triangles.Add(a);
            triangles.Add(c);
            triangles.Add(d);
        }

        private void CreateMaterials()
        {
            var shader = railMaterial != null ? railMaterial.shader :
                Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
                return;

            generatedRailMaterial = railMaterial != null
                ? new Material(railMaterial) { hideFlags = HideFlags.DontSave }
                : new Material(shader) { hideFlags = HideFlags.DontSave };
            generatedSupportMaterial = railMaterial != null
                ? new Material(railMaterial) { hideFlags = HideFlags.DontSave }
                : new Material(shader) { hideFlags = HideFlags.DontSave };
            generatedFloorMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            SetMaterialColor(generatedRailMaterial, railColor);
            SetMaterialColor(generatedSupportMaterial, supportColor);
            SetMaterialColor(generatedFloorMaterial, floorColor);
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }

        private void OnDrawGizmos()
        {
            if (configuration == null)
                return;

            var result = TrackGeometryBuilder.Build(configuration);
            if (result.Path.Segments.Count == 0)
                return;

            Gizmos.color = new Color(0.1f, 0.8f, 0.95f, 1f);
            var step = Mathf.Max(5f, result.Path.TotalLengthMeters / 100f);
            var previous = transform.TransformPoint(result.Path.SampleAtDistance(0f).Position + Vector3.up * railHeightMeters);
            for (var distance = step; distance <= result.Path.TotalLengthMeters; distance += step)
            {
                var current = transform.TransformPoint(result.Path.SampleAtDistance(distance).Position + Vector3.up * railHeightMeters);
                Gizmos.DrawLine(previous, current);
                previous = current;
            }
            if (result.Path.TotalLengthMeters > 0f)
            {
                var end = transform.TransformPoint(result.Path.SampleAtDistance(result.Path.TotalLengthMeters).Position + Vector3.up * railHeightMeters);
                Gizmos.DrawLine(previous, end);
            }
        }

        private void DestroyGeneratedAssets()
        {
            var filter = GetComponent<MeshFilter>();
            if (generatedMesh != null)
            {
                if (filter != null && filter.sharedMesh == generatedMesh)
                    filter.sharedMesh = null;
                DestroyGeneratedObject(generatedMesh);
                generatedMesh = null;
            }
            DestroyGeneratedObject(generatedRailMaterial);
            DestroyGeneratedObject(generatedSupportMaterial);
            DestroyGeneratedObject(generatedFloorMaterial);
            generatedRailMaterial = null;
            generatedSupportMaterial = null;
            generatedFloorMaterial = null;
        }

        private static void DestroyGeneratedObject(Object value)
        {
            if (value == null)
                return;
            if (Application.isPlaying)
                Destroy(value);
            else
                DestroyImmediate(value);
        }
    }
}