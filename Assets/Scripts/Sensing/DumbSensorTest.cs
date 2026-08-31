using UnityEngine;

/// <summary>
/// The most honest Layer 1 test yet: feeds each station a raw synthetic
/// acceleration stream (quiet background noise, then a sudden jump at the
/// "true" arrival time) - exactly like a real dumb sensor would produce.
/// The STA/LTA detector has to FIND the arrival itself; we don't hand it
/// the answer like earlier tests did.
///
/// Setup: same as Layer1IntegrationTest, but also add an
/// EpicenterProcessorNode component to your scene.
/// </summary>
public class DumbSensorPipelineTest : MonoBehaviour
{
    [Header("Fake test event")]
    public Vector2 fakeEpicenter = new Vector2(15f, 8f);
    public float pWaveSpeed = 6f;   // km/s
    public float sWaveSpeed = 3.5f; // km/s - independent, real constant, not derived from pWaveSpeed

    [Header("Synthetic waveform shape")]
    public float noiseFloorG = 0.002f;      // background "noise" level before the quake
    public float pWaveBurstAmplitudeG = 0.15f; // weaker onset - the P-wave
    public float sWaveBurstAmplitudeG = 0.5f;  // stronger onset - the S-wave, arrives later
    public float sampleIntervalSeconds = 0.05f; // how often the "sensor" reports
    public float streamDurationSeconds = 10f;

    [Header("GNSS test (architecture-only - NOT real displacement physics yet)")]
    [Tooltip("If true, also streams synthetic GNSS displacement readings alongside the seismic stream. This only tests that Station -> SensorDataStore.RecordGnssReading -> retrieval works - the displacement FORMULA below is a placeholder stand-in, not a real ground-displacement model. That model is genuine Layer 2 work.")]
    public bool alsoTestGnss = true;
    [Tooltip("Placeholder max displacement at the epicenter itself, tapering with distance - NOT derived from any real GMPE-for-displacement equation.")]
    public float placeholderMaxDisplacementM = 0.4f;

    [ContextMenu("Run Dumb Sensor Pipeline Test")]
    public void RunTest()
    {
        EpicenterProcessorNode.Instance.ResetAllDetectors();

        var seismicStations = StationNetwork.Instance.GetStationsByType(StationType.Seismic);
        if (seismicStations.Count < 3)
        {
            Debug.LogError("[DumbSensorTest] Need at least 3 Seismic stations.");
            return;
        }

        // Precompute each station's TRUE arrival times - this is what we're
        // hoping the detector figures out on its own from the noisy stream.
        var truePArrivalTimes = new System.Collections.Generic.Dictionary<string, float>();
        var trueSArrivalTimes = new System.Collections.Generic.Dictionary<string, float>();
        foreach (var station in seismicStations)
        {
            Vector2 pos2D = new Vector2(station.transform.position.x, station.transform.position.z);
            float dist = Vector2.Distance(fakeEpicenter, pos2D);
            truePArrivalTimes[station.stationId] = dist / pWaveSpeed;
            trueSArrivalTimes[station.stationId] = dist / sWaveSpeed;
        }

        // Stream synthetic raw samples through the REAL pipeline:
        // Station data -> SensorDataStore.RecordSeismicReading -> EpicenterProcessorNode -> PWaveDetector
        var gnssStations = alsoTestGnss ? StationNetwork.Instance.GetStationsByType(StationType.Gnss) : new System.Collections.Generic.List<Station>();
        if (alsoTestGnss && gnssStations.Count == 0)
        {
            Debug.LogWarning("[DumbSensorTest] alsoTestGnss is on but no Gnss-type stations found in the scene - skipping GNSS stream.");
        }

        for (float t = 0; t < streamDurationSeconds; t += sampleIntervalSeconds)
        {
            foreach (var station in seismicStations)
            {
                float pArrival = truePArrivalTimes[station.stationId];
                float sArrival = trueSArrivalTimes[station.stationId];

                float amplitude;
                if (t >= sArrival)
                {
                    // S-wave has arrived - strongest signal
                    amplitude = sWaveBurstAmplitudeG + Random.Range(-0.03f, 0.03f);
                }
                else if (t >= pArrival)
                {
                    // Between P and S arrival - weaker P-wave signal only
                    amplitude = pWaveBurstAmplitudeG + Random.Range(-0.01f, 0.01f);
                }
                else
                {
                    // Before any wave has arrived - just background noise
                    amplitude = noiseFloorG + Random.Range(-0.001f, 0.001f);
                }

                SeismicReading reading = new SeismicReading
                {
                    stationId = station.stationId,
                    timestamp = t,
                    accelerationX_g = amplitude,
                    accelerationY_g = 0f,
                    accelerationZ_g = 0f
                };

                SensorDataStore.Instance.RecordSeismicReading(reading);
            }

            // GNSS: unlike acceleration (which oscillates and decays), real
            // co-seismic GNSS displacement ramps up and STAYS elevated
            // (permanent ground offset) rather than bouncing back to zero.
            // This placeholder mimics that SHAPE only - not real magnitudes.
            foreach (var station in gnssStations)
            {
                Vector2 pos2D = new Vector2(station.transform.position.x, station.transform.position.z);
                float dist = Vector2.Distance(fakeEpicenter, pos2D);
                float pArrival = dist / pWaveSpeed;

                float displacement = 0f;
                if (t >= pArrival)
                {
                    float rampProgress = Mathf.Clamp01((t - pArrival) / 2f); // ramps up over ~2s
                    float distanceFalloff = 1f / (1f + dist * 0.05f); // arbitrary taper, placeholder
                    displacement = placeholderMaxDisplacementM * distanceFalloff * rampProgress;
                }

                // Vertical displacement follows the same shape but at reduced
                // magnitude - real GNSS vertical precision is lower than
                // horizontal, so this isn't just a copy of the horizontal value.
                float verticalDisplacement = displacement * 0.4f;

                GnssReading gnssReading = new GnssReading
                {
                    stationId = station.stationId,
                    timestamp = t,
                    displacementX_m = displacement,
                    displacementZ_m = 0f,
                    displacementY_m = verticalDisplacement
                };

                SensorDataStore.Instance.RecordGnssReading(gnssReading);
            }
        }

        // Compare what the detector found vs. the true arrival times
        Debug.Log("[DumbSensorTest] --- Detection accuracy per station ---");
        foreach (var station in seismicStations)
        {
            float truePTime = truePArrivalTimes[station.stationId];
            float trueSTime = trueSArrivalTimes[station.stationId];
            float detectedSTime = EpicenterProcessorNode.Instance.GetSWaveArrivalTime(station.stationId);
            Debug.Log($"[DumbSensorTest] {station.stationId}: true P={truePTime:F2}s, true S={trueSTime:F2}s | " +
                      $"detected S={(detectedSTime >= 0 ? detectedSTime.ToString("F2") + "s" : "not yet detected")}");
        }

        if (EpicenterProcessorNode.Instance.LastEstimatedEpicenter.HasValue)
        {
            Vector2 estimated = EpicenterProcessorNode.Instance.LastEstimatedEpicenter.Value;
            float error = Vector2.Distance(fakeEpicenter, estimated);
            Debug.Log($"[DumbSensorTest] True epicenter: {fakeEpicenter} | Estimated: {estimated} | Error: {error:F3}");
        }
        else
        {
            Debug.LogWarning("[DumbSensorTest] No epicenter was located - check trigger threshold or station count.");
        }

        // GNSS round-trip check - this only proves storage/retrieval wiring
        // works, NOT that the displacement values are physically realistic.
        if (alsoTestGnss && gnssStations.Count > 0)
        {
            Debug.Log("[DumbSensorTest] --- GNSS round-trip check (architecture only, not real physics) ---");
            var allGnss = SensorDataStore.Instance.GetAllLatestGnss();
            foreach (var station in gnssStations)
            {
                if (allGnss.TryGetValue(station.stationId, out GnssReading finalReading))
                {
                    Debug.Log($"[DumbSensorTest] {station.stationId}: final displacementX={finalReading.displacementX_m:F4}m, " +
                              $"displacementY(vertical)={finalReading.displacementY_m:F4}m " +
                              "(stored and retrieved successfully)");
                }
                else
                {
                    Debug.LogWarning($"[DumbSensorTest] {station.stationId}: no GNSS reading found in store - check wiring.");
                }
            }
        }
    }
}