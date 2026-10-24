using System.Collections;
using UnityEngine;

public class SensorPlacementValidation : MonoBehaviour
{
    [SerializeField] private SensorSpawner sensorSpawner;
    [SerializeField] private float testMagnitude = 6.5f;
    [SerializeField] private float testDepthKm = 10f;
    [Min(1f)] [SerializeField] private float observationSeconds = 20f;

    private Coroutine validationCoroutine;

    public void Configure(SensorSpawner spawner)
    {
        sensorSpawner = spawner;
    }

    [ContextMenu("Run Sensor Coverage Earthquake Test")]
    public void RunValidation()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[SensorPlacementValidation] Enter Play mode before running the earthquake test.", this);
            return;
        }
        if (validationCoroutine != null)
        {
            Debug.LogWarning("[SensorPlacementValidation] A validation earthquake is already running.", this);
            return;
        }

        validationCoroutine = StartCoroutine(RunValidationCoroutine());
    }

    private IEnumerator RunValidationCoroutine()
    {
        SimManager simulation = SimManager.Instance != null
            ? SimManager.Instance
            : FindFirstObjectByType<SimManager>();
        EpicenterProcessorNode processor = EpicenterProcessorNode.Instance != null
            ? EpicenterProcessorNode.Instance
            : FindFirstObjectByType<EpicenterProcessorNode>();
        StationNetwork network = StationNetwork.Instance != null
            ? StationNetwork.Instance
            : FindFirstObjectByType<StationNetwork>();

        if (simulation == null || processor == null || network == null)
        {
            Debug.LogError("[SensorPlacementValidation] SimManager, EpicenterProcessorNode, and StationNetwork are required.", this);
            validationCoroutine = null;
            yield break;
        }

        network.RefreshStationList();
        int seismicCount = network.GetStationsByType(StationType.Seismic).Count;
        if (seismicCount == 0)
        {
            Debug.LogError("[SensorPlacementValidation] No seismic stations are available to test.", this);
            validationCoroutine = null;
            yield break;
        }

        Vector2 epicenter = simulation.testEpicenter;
        int predictedCovered = CountPredictedCoverage(network, epicenter);
        processor.ResetAllDetectors();
        simulation.TriggerEarthquake(testMagnitude, epicenter, testDepthKm, false);
        Debug.Log(
            $"[SensorPlacementValidation] Test earthquake started at {epicenter}; predicted coverage is {predictedCovered}/{seismicCount} seismic sensors.",
            this);

        yield return new WaitForSeconds(Mathf.Max(1f, observationSeconds));

        int detected = processor.TriggeredPStationCount;
        Debug.Log(
            $"[SensorPlacementValidation] Earthquake test complete: {detected}/{seismicCount} sensors detected a P-wave; predicted coverage={predictedCovered}/{seismicCount}.",
            this);
        simulation.ResetToIdle();
        validationCoroutine = null;
    }

    private int CountPredictedCoverage(StationNetwork network, Vector2 epicenter)
    {
        if (sensorSpawner == null || sensorSpawner.Config == null)
            return 0;

        float radius = SensorPlacementCoordinates.RadiusWorldUnits(sensorSpawner.Config);
        int covered = 0;
        foreach (Station station in network.GetStationsByType(StationType.Seismic))
        {
            Vector2 stationPosition = new Vector2(station.transform.position.x, station.transform.position.z);
            if (Vector2.Distance(stationPosition, epicenter) <= radius)
                covered++;
        }
        return covered;
    }

    private void OnDestroy()
    {
        if (validationCoroutine != null)
            StopCoroutine(validationCoroutine);
    }
}
