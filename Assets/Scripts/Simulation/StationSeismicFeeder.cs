using UnityEngine;

/// <summary>
/// Produces live accelerometer samples for every Seismic station and sends
/// them through the real Station -> SensorDataStore -> edge detector path.
/// </summary>
public class StationSeismicFeeder : MonoBehaviour
{
    [Tooltip("Virtual sensor sample interval in seconds.")]
    public float sampleIntervalSeconds = 0.05f;

    [Tooltip("Log one MQTT publication status summary per second during an active event.")]
    public bool logPublishing = true;

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
                Debug.Log($"[StationSeismicFeeder] Event readings submitted: {samplesSinceLastLog} in last second, " +
                          $"topic=sensors/Seismic/readings, publishToMqtt={SensorDataStore.Instance.publishToMqtt}, " +
                          $"mqttConnected={mqttConnected}");
                samplesSinceLastLog = 0;
            }
        }
    }

    private void RecordSample(float timestamp)
    {
        foreach (Station station in stationNetwork.GetStationsByType(StationType.Seismic))
        {
            SeismicReading reading = EarthquakeModel.GetReadingAt(
                station.transform.position,
                station.stationId,
                timestamp,
                siteClass);
            dataStore.RecordSeismicReading(reading);
            samplesSinceLastLog++;
        }
    }

    private bool IsEventActive()
    {
        return SimManager.Instance.CurrentState == SimEventState.EarthquakeActive ||
               SimManager.Instance.CurrentState == SimEventState.TsunamiActive;
    }
}