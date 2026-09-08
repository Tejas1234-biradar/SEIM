using UnityEngine;

/// <summary>
/// Produces live GNSS displacement samples for every Gnss station.
/// The displacement uses a simplified co-seismic ramp-and-hold model: its
/// amplitude is derived from the current GMPE PGA and deliberately scaled
/// down. Full Okada (1985) dislocation modeling is out of scope for Layer 2.
/// </summary>
public class StationGnssFeeder : MonoBehaviour
{
    [Tooltip("Virtual sensor sample interval in seconds.")]
    public float sampleIntervalSeconds = 0.05f;

    [Tooltip("Log one MQTT publication status summary per second during an active event.")]
    public bool logPublishing = true;

    [Tooltip("Simplified displacement scale in metres per g of GMPE PGA.")]
    public float displacementMetresPerG = 0.02f;

    [Tooltip("Time for the co-seismic displacement to reach its held value.")]
    public float displacementRampSeconds = 2f;

    public EarthquakeModel.SiteClass siteClass = EarthquakeModel.SiteClass.Bedrock;

    private float sampleAccumulator;
    private float logAccumulator;
    private int samplesSinceLastLog;
    private StationNetwork stationNetwork;
    private SensorDataStore dataStore;

    private void Awake()
    {
        stationNetwork = FindFirstObjectByType<StationNetwork>();
        dataStore = FindFirstObjectByType<SensorDataStore>();
    }

    private void Update()
    {
        if (SimManager.Instance == null || dataStore == null || stationNetwork == null)
            return;

        sampleAccumulator += Time.deltaTime;
        float interval = Mathf.Max(sampleIntervalSeconds, 0.001f);
        while (sampleAccumulator >= interval)
        {
            sampleAccumulator -= interval;
            RecordSample(SimManager.Instance.ElapsedTime);
        }

        if (logPublishing && IsEventActive())
        {
            logAccumulator += Time.deltaTime;
            if (logAccumulator >= 1f)
            {
                logAccumulator = 0f;
                bool mqttConnected = SensorDataStore.Instance != null &&
                                     SensorDataStore.Instance.publishToMqtt &&
                                     MqttPublisher.Instance != null &&
                                     MqttPublisher.Instance.IsConnected;
                Debug.Log($"[StationGnssFeeder] Event readings submitted: {samplesSinceLastLog} in last second, " +
                          $"topic=sensors/Gnss/readings, publishToMqtt={SensorDataStore.Instance.publishToMqtt}, " +
                          $"mqttConnected={mqttConnected}");
                samplesSinceLastLog = 0;
            }
        }
    }

    private void RecordSample(float timestamp)
    {
        Vector2 epicenter = SimManager.Instance.EpicenterSurfacePos;

        foreach (Station station in stationNetwork.GetStationsByType(StationType.Gnss))
        {
            Vector3 stationPosition = station.transform.position;
            bool eventActive = SimManager.Instance.CurrentState == SimEventState.EarthquakeActive ||
                               SimManager.Instance.CurrentState == SimEventState.TsunamiActive;
            float displacement = 0f;

            if (eventActive)
            {
                float timeSinceP = timestamp - EarthquakeModel.GetPWaveArrivalTimeAt(stationPosition);
                float rampProgress = timeSinceP >= 0f
                    ? Mathf.Clamp01(timeSinceP / Mathf.Max(displacementRampSeconds, 0.001f))
                    : 0f;

                displacement = EarthquakeModel.GetCurrentPGAAt(stationPosition, siteClass)
                    * displacementMetresPerG
                    * rampProgress;
            }

            Vector2 horizontalOffset = new Vector2(
                stationPosition.x - epicenter.x,
                stationPosition.z - epicenter.y);
            if (horizontalOffset.sqrMagnitude > 0.0001f)
                horizontalOffset.Normalize();
            else
                horizontalOffset = Vector2.right;

            dataStore.RecordGnssReading(new GnssReading
            {
                stationId = station.stationId,
                timestamp = timestamp,
                displacementX_m = horizontalOffset.x * displacement,
                displacementZ_m = horizontalOffset.y * displacement,
                displacementY_m = displacement * 0.4f
            });
            samplesSinceLastLog++;
        }
    }

    private bool IsEventActive()
    {
        return SimManager.Instance.CurrentState == SimEventState.EarthquakeActive ||
               SimManager.Instance.CurrentState == SimEventState.TsunamiActive;
    }
}