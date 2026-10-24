using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-50)]
public class SensorSpawner : MonoBehaviour
{
    [SerializeField] private SensorPlacementClient placementClient;
    [SerializeField] private SensorPlacementConfig config;
    [SerializeField] private GameObject sensorPrefab;
    [SerializeField] private StationType stationType = StationType.Seismic;

    private Transform spawnedRoot;
    private Coroutine spawnCoroutine;
    private int spawnVersion;

    public IReadOnlyList<Station> SpawnedStations => spawnedStations;
    public SensorPlacementResponse CurrentPlacement { get; private set; }
    public SensorPlacementConfig Config => config;
    public event System.Action<SensorPlacementResponse> PlacementApplied;

    private readonly List<Station> spawnedStations = new List<Station>();

    public void Configure(
        SensorPlacementClient client,
        SensorPlacementConfig placementConfig,
        GameObject prefab)
    {
        placementClient = client;
        config = placementConfig;
        sensorPrefab = prefab;
    }

    private void Start()
    {
        if (placementClient == null)
            placementClient = FindFirstObjectByType<SensorPlacementClient>();

        if (placementClient == null)
        {
            Debug.LogError("[SensorSpawner] No SensorPlacementClient is available.", this);
            return;
        }

        placementClient.PlacementUpdated += OnPlacementUpdated;
        if (placementClient.CurrentPlacement != null)
            OnPlacementUpdated(placementClient.CurrentPlacement);
    }

    private void OnPlacementUpdated(SensorPlacementResponse response)
    {
        if (response == null)
            return;
        if (config == null)
        {
            Debug.LogError("[SensorSpawner] No SensorPlacementConfig is assigned.", this);
            return;
        }
        if (sensorPrefab == null)
        {
            Debug.LogError("[SensorSpawner] Assign the VirtualSensor prefab before spawning placements.", this);
            return;
        }
        if (sensorPrefab.GetComponentInChildren<Station>() == null)
        {
            Debug.LogError("[SensorSpawner] The configured prefab must contain a Station component.", this);
            return;
        }

        CurrentPlacement = response;
        spawnVersion++;
        if (spawnCoroutine != null)
            StopCoroutine(spawnCoroutine);
        spawnCoroutine = StartCoroutine(ReplaceSpawnedSensors(response, spawnVersion));
    }

    private IEnumerator ReplaceSpawnedSensors(SensorPlacementResponse response, int version)
    {
        ClearCurrentStations();
        if (config.replaceExistingSeismicStations)
        {
            Station[] existingStations = FindObjectsByType<Station>(FindObjectsSortMode.None);
            foreach (Station station in existingStations)
            {
                if (station != null && station.stationType == stationType)
                    Destroy(station.gameObject);
            }
        }

        yield return null;
        if (version != spawnVersion)
            yield break;

        spawnedRoot = new GameObject("GeneratedSensorStations").transform;
        spawnedRoot.SetParent(transform, false);
        spawnedStations.Clear();
        HashSet<string> usedStationIds = new HashSet<string>();

        for (int i = 0; i < response.sensors.Length; i++)
        {
            PlacementSensor placement = response.sensors[i];
            Vector3 position;
            try
            {
                position = SensorPlacementCoordinates.ToWorld(placement, config);
            }
            catch (System.Exception exception) when (
                exception is System.ArgumentException || exception is System.FormatException)
            {
                Debug.LogError($"[SensorSpawner] Skipping sensor at index {i}: {exception.Message}", this);
                continue;
            }

            string id = SensorPlacementCoordinates.GetSensorId(placement);
            if (string.IsNullOrWhiteSpace(id))
            {
                int generatedId = i + 1;
                id = $"PLACED_{generatedId:000}";
                while (!usedStationIds.Add(id))
                    id = $"PLACED_{++generatedId:000}";
            }
            else if (!usedStationIds.Add(id))
            {
                Debug.LogError($"[SensorSpawner] Duplicate station ID '{id}' at placement index {i}; skipping it.", this);
                continue;
            }

            GameObject instance = Instantiate(sensorPrefab, position, Quaternion.identity, spawnedRoot);
            Station stationComponent = instance.GetComponentInChildren<Station>();
            stationComponent.stationId = id;
            stationComponent.stationType = stationType;
            stationComponent.transform.position = position;
            instance.name = id;
            spawnedStations.Add(stationComponent);
        }

        StationNetwork network = StationNetwork.Instance != null
            ? StationNetwork.Instance
            : FindFirstObjectByType<StationNetwork>();
        if (network != null)
            network.RefreshStationList();

        if (spawnedStations.Count == 0)
        {
            Debug.LogError("[SensorSpawner] No placement sensors could be spawned; the existing sensor set was cleared.", this);
            spawnCoroutine = null;
            yield break;
        }

        PlacementApplied?.Invoke(response);
        Debug.Log($"[SensorSpawner] Replaced the seismic sensor set with {spawnedStations.Count} placed sensors.", this);
        spawnCoroutine = null;
    }

    private void ClearCurrentStations()
    {
        spawnedStations.Clear();
        if (spawnedRoot != null)
        {
            Destroy(spawnedRoot.gameObject);
            spawnedRoot = null;
        }
    }

    private void OnDestroy()
    {
        if (placementClient != null)
            placementClient.PlacementUpdated -= OnPlacementUpdated;
        if (spawnCoroutine != null)
            StopCoroutine(spawnCoroutine);
    }
}
