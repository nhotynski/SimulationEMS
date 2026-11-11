using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public sealed class SoccerBall : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float radius = 0.5f;

    private Mesh generatedMesh;
    private Material blackMaterial;
    private Material whiteMaterial;

    private static readonly Vector3[] IcosahedronVertices = CreateIcosahedronVertices();
    private static readonly int[,] IcosahedronFaces =
    {
        { 0, 11, 5 }, { 0, 5, 1 }, { 0, 1, 7 }, { 0, 7, 10 }, { 0, 10, 11 },
        { 1, 5, 9 }, { 5, 11, 4 }, { 11, 10, 2 }, { 10, 7, 6 }, { 7, 1, 8 },
        { 3, 9, 4 }, { 3, 4, 2 }, { 3, 2, 6 }, { 3, 6, 8 }, { 3, 8, 9 },
        { 4, 9, 5 }, { 2, 4, 11 }, { 6, 2, 10 }, { 8, 6, 7 }, { 9, 8, 1 }
    };

    private void OnEnable()
    {
        Rebuild();
    }

    private void OnValidate()
    {
        radius = Mathf.Max(0.1f, radius);
        if (isActiveAndEnabled)
            Rebuild();
    }

    private void OnDisable()
    {
        DestroyGeneratedAssets();
    }

    private void Rebuild()
    {
        DestroyGeneratedAssets();

        var meshFilter = GetComponent<MeshFilter>();
        var meshRenderer = GetComponent<MeshRenderer>();
        var sphereCollider = GetComponent<SphereCollider>();
        sphereCollider.radius = radius;

        generatedMesh = BuildMesh();
        meshFilter.sharedMesh = generatedMesh;

        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (shader == null)
            return;

        blackMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        whiteMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        SetMaterialColor(blackMaterial, new Color(0.035f, 0.045f, 0.055f));
        SetMaterialColor(whiteMaterial, new Color(0.94f, 0.945f, 0.92f));
        meshRenderer.sharedMaterials = new[] { blackMaterial, whiteMaterial };
    }

    private Mesh BuildMesh()
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var blackTriangles = new List<int>();
        var whiteTriangles = new List<int>();

        var neighbors = new List<int>[IcosahedronVertices.Length];
        for (var i = 0; i < neighbors.Length; i++)
            neighbors[i] = new List<int>();

        for (var face = 0; face < IcosahedronFaces.GetLength(0); face++)
        {
            var a = IcosahedronFaces[face, 0];
            var b = IcosahedronFaces[face, 1];
            var c = IcosahedronFaces[face, 2];
            AddNeighbor(neighbors[a], b);
            AddNeighbor(neighbors[a], c);
            AddNeighbor(neighbors[b], a);
            AddNeighbor(neighbors[b], c);
            AddNeighbor(neighbors[c], a);
            AddNeighbor(neighbors[c], b);

            var hexagon = new List<Vector3>
            {
                TruncatedVertex(a, b), TruncatedVertex(b, a),
                TruncatedVertex(b, c), TruncatedVertex(c, b),
                TruncatedVertex(c, a), TruncatedVertex(a, c)
            };
            AddPanel(hexagon, 1, vertices, normals, blackTriangles, whiteTriangles);
        }

        for (var vertex = 0; vertex < neighbors.Length; vertex++)
        {
            var center = IcosahedronVertices[vertex];
            var axis = center.normalized;
            var reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.9f ? Vector3.right : Vector3.up;
            var tangent = Vector3.Cross(axis, reference).normalized;
            var bitangent = Vector3.Cross(axis, tangent);

            neighbors[vertex].Sort((left, right) =>
            {
                var leftDirection = IcosahedronVertices[left] - center;
                var rightDirection = IcosahedronVertices[right] - center;
                var leftAngle = Mathf.Atan2(Vector3.Dot(leftDirection, bitangent), Vector3.Dot(leftDirection, tangent));
                var rightAngle = Mathf.Atan2(Vector3.Dot(rightDirection, bitangent), Vector3.Dot(rightDirection, tangent));
                return leftAngle.CompareTo(rightAngle);
            });

            var pentagon = new List<Vector3>();
            foreach (var neighbor in neighbors[vertex])
                pentagon.Add(TruncatedVertex(vertex, neighbor));
            AddPanel(pentagon, 0, vertices, normals, blackTriangles, whiteTriangles);
        }

        var mesh = new Mesh { name = "Soccer Ball Panels", hideFlags = HideFlags.DontSave };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.subMeshCount = 2;
        mesh.SetTriangles(blackTriangles, 0);
        mesh.SetTriangles(whiteTriangles, 1);
        mesh.RecalculateBounds();
        return mesh;
    }

    private void AddPanel(List<Vector3> panel, int materialIndex, List<Vector3> vertices, List<Vector3> normals,
        List<int> blackTriangles, List<int> whiteTriangles)
    {
        EnsureOutwardWinding(panel);
        var center = Project(Average(panel));
        var triangles = materialIndex == 0 ? blackTriangles : whiteTriangles;

        for (var i = 0; i < panel.Count; i++)
            Subdivide(center, Project(panel[i]), Project(panel[(i + 1) % panel.Count]), 2, triangles, vertices, normals);
    }

    private void Subdivide(Vector3 a, Vector3 b, Vector3 c, int depth, List<int> triangles,
        List<Vector3> vertices, List<Vector3> normals)
    {
        if (depth == 0)
        {
            AddVertex(a, triangles, vertices, normals);
            AddVertex(b, triangles, vertices, normals);
            AddVertex(c, triangles, vertices, normals);
            return;
        }

        var ab = Project((a + b) * 0.5f);
        var bc = Project((b + c) * 0.5f);
        var ca = Project((c + a) * 0.5f);
        Subdivide(a, ab, ca, depth - 1, triangles, vertices, normals);
        Subdivide(ab, b, bc, depth - 1, triangles, vertices, normals);
        Subdivide(ca, bc, c, depth - 1, triangles, vertices, normals);
        Subdivide(ab, bc, ca, depth - 1, triangles, vertices, normals);
    }

    private void AddVertex(Vector3 vertex, List<int> triangles, List<Vector3> vertices, List<Vector3> normals)
    {
        triangles.Add(vertices.Count);
        vertices.Add(vertex);
        normals.Add(vertex.normalized);
    }

    private Vector3 TruncatedVertex(int from, int to)
    {
        return Project((IcosahedronVertices[from] * 2f + IcosahedronVertices[to]) / 3f);
    }

    private Vector3 Project(Vector3 point)
    {
        return point.normalized * radius;
    }

    private static Vector3 Average(List<Vector3> points)
    {
        var sum = Vector3.zero;
        foreach (var point in points)
            sum += point;
        return sum / points.Count;
    }

    private static void EnsureOutwardWinding(List<Vector3> points)
    {
        var normal = Vector3.zero;
        for (var i = 0; i < points.Count; i++)
            normal += Vector3.Cross(points[i], points[(i + 1) % points.Count]);
        if (Vector3.Dot(normal, Average(points)) < 0f)
            points.Reverse();
    }

    private static void AddNeighbor(List<int> neighbors, int candidate)
    {
        if (!neighbors.Contains(candidate))
            neighbors.Add(candidate);
    }

    private static Vector3[] CreateIcosahedronVertices()
    {
        var goldenRatio = (1f + Mathf.Sqrt(5f)) * 0.5f;
        var vertices = new[]
        {
            new Vector3(-1, goldenRatio, 0), new Vector3(1, goldenRatio, 0),
            new Vector3(-1, -goldenRatio, 0), new Vector3(1, -goldenRatio, 0),
            new Vector3(0, -1, goldenRatio), new Vector3(0, 1, goldenRatio),
            new Vector3(0, -1, -goldenRatio), new Vector3(0, 1, -goldenRatio),
            new Vector3(goldenRatio, 0, -1), new Vector3(goldenRatio, 0, 1),
            new Vector3(-goldenRatio, 0, -1), new Vector3(-goldenRatio, 0, 1)
        };

        for (var i = 0; i < vertices.Length; i++)
            vertices[i].Normalize();
        return vertices;
    }

    private static void SetMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        else if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    private void DestroyGeneratedAssets()
    {
        if (generatedMesh != null)
        {
            if (GetComponent<MeshFilter>().sharedMesh == generatedMesh)
                GetComponent<MeshFilter>().sharedMesh = null;
            DestroyGeneratedObject(generatedMesh);
            generatedMesh = null;
        }

        DestroyGeneratedObject(blackMaterial);
        DestroyGeneratedObject(whiteMaterial);
        blackMaterial = null;
        whiteMaterial = null;
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