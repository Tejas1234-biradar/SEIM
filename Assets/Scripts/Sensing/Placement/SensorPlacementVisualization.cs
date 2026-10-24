using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class SensorPlacementVisualization : MonoBehaviour
{
    [SerializeField] private SensorSpawner sensorSpawner;
    [SerializeField] private SensorPlacementConfig config;
    [SerializeField] private bool showHud = true;
    [SerializeField] private bool showCoverage = true;
    [SerializeField] private int maximumGeneratedGridCells = 100000;

    private GameObject visualizationRoot;
    private Renderer gridRenderer;
    private Renderer coverageRenderer;
    private Material transparentMaterial;
    private GUIStyle hudStyle;
    private string hudMetrics = "Waiting for sensor placement...";

    public void Configure(SensorSpawner spawner, SensorPlacementConfig placementConfig)
    {
        sensorSpawner = spawner;
        config = placementConfig;
    }

    private void Start()
    {
        if (sensorSpawner == null)
            sensorSpawner = FindFirstObjectByType<SensorSpawner>();
        if (sensorSpawner == null)
        {
            Debug.LogError("[SensorPlacementVisualization] No SensorSpawner is available.", this);
            return;
        }

        sensorSpawner.PlacementApplied += OnPlacementApplied;
        if (sensorSpawner.CurrentPlacement != null)
            OnPlacementApplied(sensorSpawner.CurrentPlacement);
    }

    private void OnPlacementApplied(SensorPlacementResponse response)
    {
        if (config == null || response == null || sensorSpawner == null)
            return;

        PlacementGridCell[] cells = GetGridCells(response);
        if (cells == null || cells.Length == 0)
        {
            Debug.LogWarning("[SensorPlacementVisualization] No usable coverage grid could be built.", this);
            return;
        }

        EnsureVisualizationObjects();
        BuildGridMesh(cells);
        BuildCoverageMesh();
        UpdateHudMetrics(response, cells);
        SetVisualization(showCoverage);
    }

    private PlacementGridCell[] GetGridCells(SensorPlacementResponse response)
    {
        PlacementGridCell[] cells = response.grid;
        if (cells == null || cells.Length == 0)
            cells = response.coverageGrid;
        if (cells != null && cells.Length > 0)
        {
            if (cells.Length > maximumGeneratedGridCells)
            {
                Debug.LogError(
                    $"[SensorPlacementVisualization] Backend coverage grid has {cells.Length} cells, above the configured limit of {maximumGeneratedGridCells}.",
                    this);
                return null;
            }
            return cells;
        }

        if (!TryGetWorldBounds(out Bounds bounds))
        {
            Debug.LogWarning("[SensorPlacementVisualization] Configure a city boundary or area to generate the local coverage grid.", this);
            return null;
        }

        float spacing = Mathf.Max(1f, config.gridResolutionMeters) * config.worldUnitsPerMeter;
        int columns = Mathf.Max(1, Mathf.CeilToInt(bounds.size.x / spacing));
        int rows = Mathf.Max(1, Mathf.CeilToInt(bounds.size.z / spacing));
        long cellCount = (long)columns * rows;
        if (cellCount > maximumGeneratedGridCells)
        {
            Debug.LogError(
                $"[SensorPlacementVisualization] Local coverage grid needs {cellCount} cells, above the configured limit of {maximumGeneratedGridCells}. Increase grid resolution.",
                this);
            return null;
        }

        List<PlacementGridCell> generated = new List<PlacementGridCell>((int)cellCount);
        Vector2[] cityPolygon = GetWorldPolygon();
        float radius = SensorPlacementCoordinates.RadiusWorldUnits(config);
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                Vector3 world = new Vector3(
                    bounds.min.x + (column + 0.5f) * spacing,
                    config.originWorldPosition.y,
                    bounds.min.z + (row + 0.5f) * spacing);
                if (world.x > bounds.max.x || world.z > bounds.max.z)
                    continue;
                if (cityPolygon != null && !IsInsidePolygon(new Vector2(world.x, world.z), cityPolygon))
                    continue;

                int multiplicity = 0;
                foreach (Station station in sensorSpawner.SpawnedStations)
                {
                    Vector2 stationPosition = new Vector2(station.transform.position.x, station.transform.position.z);
                    if (Vector2.Distance(new Vector2(world.x, world.z), stationPosition) <= radius)
                        multiplicity++;
                }

                generated.Add(new PlacementGridCell
                {
                    x = (world.x - config.originWorldPosition.x) / config.worldUnitsPerMeter,
                    y = (world.z - config.originWorldPosition.z) / config.worldUnitsPerMeter,
                    coordinateSystem = "meters",
                    multiplicity = multiplicity
                });
            }
        }

        return generated.ToArray();
    }

    private Vector2[] GetWorldPolygon()
    {
        if (config.useArea || config.boundaryPolygon == null || config.boundaryPolygon.Length < 3)
            return null;

        Vector2[] polygon = new Vector2[config.boundaryPolygon.Length];
        for (int i = 0; i < config.boundaryPolygon.Length; i++)
        {
            SensorPlacementGeoPoint point = config.boundaryPolygon[i];
            if (!SensorPlacementCoordinates.TryToWorld(0f, 0f, point.latitude, point.longitude,
                    true, "latlon", config, out Vector3 world))
                return null;
            polygon[i] = new Vector2(world.x, world.z);
        }
        return polygon;
    }

    private static bool IsInsidePolygon(Vector2 point, Vector2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            bool crosses = (polygon[i].y > point.y) != (polygon[j].y > point.y) &&
                           point.x < (polygon[j].x - polygon[i].x) *
                           (point.y - polygon[i].y) /
                           (polygon[j].y - polygon[i].y) + polygon[i].x;
            if (crosses)
                inside = !inside;
        }
        return inside;
    }

    private bool TryGetWorldBounds(out Bounds bounds)
    {
        if (config.useArea && config.area != null)
        {
            if (!SensorPlacementCoordinates.TryToWorld(
                    0f, 0f, config.area.centerLatitude, config.area.centerLongitude, true,
                    "latlon", config, out Vector3 center))
            {
                bounds = default;
                return false;
            }

            Vector3 size = new Vector3(
                config.area.widthMeters * config.worldUnitsPerMeter,
                0f,
                config.area.heightMeters * config.worldUnitsPerMeter);
            bounds = new Bounds(center, size);
            return size.x > 0f && size.z > 0f;
        }

        if (config.boundaryPolygon == null || config.boundaryPolygon.Length < 3)
        {
            bounds = default;
            return false;
        }

        Vector3 first;
        SensorPlacementGeoPoint firstPoint = config.boundaryPolygon[0];
        if (!SensorPlacementCoordinates.TryToWorld(0f, 0f, firstPoint.latitude, firstPoint.longitude,
                true, "latlon", config, out first))
        {
            bounds = default;
            return false;
        }

        bounds = new Bounds(first, Vector3.zero);
        for (int i = 1; i < config.boundaryPolygon.Length; i++)
        {
            SensorPlacementGeoPoint point = config.boundaryPolygon[i];
            if (!SensorPlacementCoordinates.TryToWorld(0f, 0f, point.latitude, point.longitude,
                    true, "latlon", config, out Vector3 world))
            {
                bounds = default;
                return false;
            }
            bounds.Encapsulate(world);
        }

        return bounds.size.x > 0f && bounds.size.z > 0f;
    }

    private void EnsureVisualizationObjects()
    {
        if (visualizationRoot != null)
            return;

        visualizationRoot = new GameObject("SensorPlacementVisualization");
        visualizationRoot.transform.SetParent(transform, false);
        transparentMaterial = CreateTransparentMaterial();
        gridRenderer = CreateMeshObject("CoverageMultiplicityGrid").GetComponent<MeshRenderer>();
        coverageRenderer = CreateMeshObject("SensorCoverageRadii").GetComponent<MeshRenderer>();
    }

    private GameObject CreateMeshObject(string objectName)
    {
        GameObject meshObject = new GameObject(objectName);
        meshObject.transform.SetParent(visualizationRoot.transform, false);
        meshObject.AddComponent<MeshFilter>().sharedMesh = new Mesh
        {
            name = objectName,
            indexFormat = IndexFormat.UInt32
        };
        MeshRenderer meshRenderer = meshObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = transparentMaterial;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        return meshObject;
    }

    private Material CreateTransparentMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogError("[SensorPlacementVisualization] Could not find the transparent Sprites/Default shader.", this);
            return null;
        }

        return new Material(shader) { name = "SensorPlacementTransparentMaterial" };
    }

    private void BuildGridMesh(PlacementGridCell[] cells)
    {
        Mesh mesh = gridRenderer.GetComponent<MeshFilter>().sharedMesh;
        mesh.Clear();

        float cellSize = Mathf.Max(1f, config.gridResolutionMeters) * config.worldUnitsPerMeter;
        List<Vector3> vertices = new List<Vector3>(cells.Length * 4);
        List<Color> colors = new List<Color>(cells.Length * 4);
        List<int> triangles = new List<int>(cells.Length * 6);

        foreach (PlacementGridCell cell in cells)
        {
            Vector3 center;
            try
            {
                center = SensorPlacementCoordinates.ToWorld(cell, config);
            }
            catch (System.Exception exception) when (
                exception is System.ArgumentException || exception is System.FormatException)
            {
                Debug.LogWarning($"[SensorPlacementVisualization] Ignoring invalid coverage cell: {exception.Message}", this);
                continue;
            }

            int start = vertices.Count;
            float half = cellSize * 0.5f;
            vertices.Add(visualizationRoot.transform.InverseTransformPoint(center + new Vector3(-half, 0.15f, -half)));
            vertices.Add(visualizationRoot.transform.InverseTransformPoint(center + new Vector3(-half, 0.15f, half)));
            vertices.Add(visualizationRoot.transform.InverseTransformPoint(center + new Vector3(half, 0.15f, half)));
            vertices.Add(visualizationRoot.transform.InverseTransformPoint(center + new Vector3(half, 0.15f, -half)));

            Color color = ColorForMultiplicity(cell.multiplicity);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            colors.Add(color);
            triangles.Add(start);
            triangles.Add(start + 1);
            triangles.Add(start + 2);
            triangles.Add(start);
            triangles.Add(start + 2);
            triangles.Add(start + 3);
        }

        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    private void BuildCoverageMesh()
    {
        Mesh mesh = coverageRenderer.GetComponent<MeshFilter>().sharedMesh;
        mesh.Clear();

        IReadOnlyList<Station> stations = sensorSpawner.SpawnedStations;
        const int segments = 32;
        float radius = SensorPlacementCoordinates.RadiusWorldUnits(config);
        List<Vector3> vertices = new List<Vector3>(stations.Count * segments * 3);
        List<Color> colors = new List<Color>(stations.Count * segments * 3);
        List<int> triangles = new List<int>(stations.Count * segments * 3);

        for (int stationIndex = 0; stationIndex < stations.Count; stationIndex++)
        {
            Vector3 center = stations[stationIndex].transform.position + Vector3.up * 0.2f;
            int start = vertices.Count;
            for (int segment = 0; segment < segments; segment++)
            {
                float angleA = segment * Mathf.PI * 2f / segments;
                float angleB = (segment + 1) * Mathf.PI * 2f / segments;
                vertices.Add(visualizationRoot.transform.InverseTransformPoint(center));
                vertices.Add(visualizationRoot.transform.InverseTransformPoint(center +
                    new Vector3(Mathf.Cos(angleA) * radius, 0f, Mathf.Sin(angleA) * radius)));
                vertices.Add(visualizationRoot.transform.InverseTransformPoint(center +
                    new Vector3(Mathf.Cos(angleB) * radius, 0f, Mathf.Sin(angleB) * radius)));

                Color color = new Color(0.1f, 0.55f, 1f, 0.12f);
                colors.Add(color);
                colors.Add(color);
                colors.Add(color);
                triangles.Add(start + segment * 3);
                triangles.Add(start + segment * 3 + 2);
                triangles.Add(start + segment * 3 + 1);
            }
        }

        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
    }

    private Color ColorForMultiplicity(int multiplicity)
    {
        if (multiplicity >= config.k)
            return new Color(0.1f, 0.85f, 0.2f, 0.38f);
        if (multiplicity > 0)
            return new Color(1f, 0.82f, 0.05f, 0.42f);
        return new Color(1f, 0.12f, 0.08f, 0.38f);
    }

    private void UpdateHudMetrics(SensorPlacementResponse response, PlacementGridCell[] cells)
    {
        int covered = 0;
        long totalMultiplicity = 0;
        foreach (PlacementGridCell cell in cells)
        {
            if (cell.multiplicity > 0)
                covered++;
            totalMultiplicity += Mathf.Max(0, cell.multiplicity);
        }

        float derivedCoveragePercent = 100f * covered / cells.Length;
        float derivedAverageMultiplicity = (float)totalMultiplicity / cells.Length;
        float coveragePercent = response.coveragePercent > 0f
            ? response.coveragePercent
            : derivedCoveragePercent;
        float averageMultiplicity = response.avgCoverageMultiplicity > 0f
            ? response.avgCoverageMultiplicity
            : (response.avgMultiplicity > 0f
                ? response.avgMultiplicity
                : (response.averageMultiplicity > 0f ? response.averageMultiplicity : derivedAverageMultiplicity));
        int estimated = response.estimatedCount > 0
            ? response.estimatedCount
            : (response.estimatedSensorCount > 0 ? response.estimatedSensorCount : response.sensors.Length);
        hudMetrics =
            $"Estimated sensors: {estimated}\n" +
            $"Placed sensors: {sensorSpawner.SpawnedStations.Count}\n" +
            $"City coverage: {coveragePercent:F1}%\n" +
            $"Average multiplicity: {averageMultiplicity:F2}";
    }

    private void SetVisualization(bool visible)
    {
        showCoverage = visible;
        if (visualizationRoot != null)
            visualizationRoot.SetActive(visible);
    }

    private void OnGUI()
    {
        if (hudStyle == null)
        {
            hudStyle = new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = Color.white },
                fontSize = 14,
                wordWrap = false
            };
        }

        if (GUI.Button(new Rect(12f, 12f, 190f, 32f), showCoverage ? "Hide placement overlay" : "Show placement overlay"))
            SetVisualization(!showCoverage);

        if (!showHud)
        {
            if (GUI.Button(new Rect(210f, 12f, 110f, 32f), "Show metrics"))
                showHud = true;
            return;
        }

        GUI.Box(new Rect(12f, 52f, 235f, 112f), GUIContent.none);
        GUI.Label(new Rect(22f, 58f, 220f, 96f), hudMetrics, hudStyle);
        if (GUI.Button(new Rect(210f, 12f, 110f, 32f), "Hide metrics"))
            showHud = false;
    }

    private void OnDestroy()
    {
        if (sensorSpawner != null)
            sensorSpawner.PlacementApplied -= OnPlacementApplied;
        if (transparentMaterial != null)
            Destroy(transparentMaterial);
    }
}
