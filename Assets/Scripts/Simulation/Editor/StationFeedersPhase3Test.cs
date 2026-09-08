using UnityEditor;
using UnityEngine;

public static class StationFeedersPhase3Test
{
    [MenuItem("SEIM/Test Phase 3 (Live Station Feeders)")]
    public static void RunTest()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[StationFeedersPhase3Test] Stop Play Mode before starting this test.");
            return;
        }

        GameObject root = new GameObject("Phase3LivePipelineTest");
        SimManager sim = root.AddComponent<SimManager>();
        StationNetwork network = root.AddComponent<StationNetwork>();
        SensorDataStore store = root.AddComponent<SensorDataStore>();
        EpicenterProcessorNode processor = root.AddComponent<EpicenterProcessorNode>();
        StationSeismicFeeder seismicFeeder = root.AddComponent<StationSeismicFeeder>();
        StationGnssFeeder gnssFeeder = root.AddComponent<StationGnssFeeder>();

        processor.pTriggerThreshold = 2f;
        processor.sTriggerThreshold = 4f;
        processor.minStationsToLocate = 3;
        store.publishToMqtt = false;
        store.exportToJsonFile = false;

        Vector2 epicenter = new Vector2(10f, 10f);
        CreateStation(root.transform, "PHASE3_ST_01", StationType.Seismic, new Vector3(0f, 0f, 10f));
        CreateStation(root.transform, "PHASE3_ST_02", StationType.Seismic, new Vector3(20f, 0f, 10f));
        CreateStation(root.transform, "PHASE3_ST_03", StationType.Seismic, new Vector3(10f, 0f, 0f));
        CreateStation(root.transform, "PHASE3_GNSS_01", StationType.Gnss, new Vector3(10f, 0f, 20f));

        network.RefreshStationList();
        sim.TriggerEarthquake(6.5f, epicenter, 5f, false);

        Debug.Log("[StationFeedersPhase3Test] Triggered SimManager earthquake at (10, 10), M6.5, depth 5 km. Running live feeders for 12 seconds.");

        double startedAt = EditorApplication.timeSinceStartup;
        EditorApplication.CallbackFunction monitor = null;
        monitor = () =>
        {
            if (!EditorApplication.isPlaying)
            {
                EditorApplication.update -= monitor;
                Object.DestroyImmediate(root);
                return;
            }

            if (EditorApplication.timeSinceStartup - startedAt < 12.0)
                return;

            if (processor.LastEstimatedEpicenter.HasValue)
            {
                Vector2 estimated = processor.LastEstimatedEpicenter.Value;
                Debug.Log($"[StationFeedersPhase3Test] True epicenter: {epicenter} | Estimated: {estimated} | Error: {Vector2.Distance(epicenter, estimated):F3}");
            }
            else
            {
                Debug.LogError("[StationFeedersPhase3Test] No epicenter was located from live feeder data.");
            }

            EditorApplication.isPlaying = false;
        };
        EditorApplication.update += monitor;
        EditorApplication.isPlaying = true;
    }

    private static void CreateStation(Transform parent, string id, StationType type, Vector3 position)
    {
        GameObject stationObject = new GameObject(id);
        stationObject.transform.SetParent(parent);
        stationObject.transform.position = position;
        Station station = stationObject.AddComponent<Station>();
        station.stationId = id;
        station.stationType = type;
    }
}