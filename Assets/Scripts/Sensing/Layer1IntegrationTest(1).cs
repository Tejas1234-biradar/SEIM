using UnityEngine;

/// <summary>
/// Layer 1 "definition of done" test. Uses a fake epicenter and your REAL
/// Station GameObjects placed in the scene, pushes synthetic readings
/// through SensorDataStore, pulls them back out, and confirms
/// Trilateration recovers the epicenter you picked.
///
/// Setup: place 3+ GameObjects with Station.cs (type = Seismic) in your
/// test scene, plus one StationNetwork and one SensorDataStore in the
/// scene. Attach this script to any empty GameObject and hit Play.
/// </summary>
public class Layer1IntegrationTest : MonoBehaviour
{
    [Header("Fake test event")]
    public Vector2 fakeEpicenter = new Vector2(15f, 8f);

    [Header("Wave speeds (km/s) - match EarthquakeSimulator's real constants, not a guessed ratio")]
    public float pWaveSpeed = 6f;   // typical crustal P-wave speed
    public float sWaveSpeed = 3.5f; // typical crustal S-wave speed

    void Start()
    {
        RunTest();
    }

    [ContextMenu("Run Layer 1 Integration Test")]
    public void RunTest()
    {
        var seismicStations = StationNetwork.Instance.GetStationsByType(StationType.Seismic);

        if (seismicStations.Count < 3)
        {
            Debug.LogError("[Layer1Test] Need at least 3 Seismic stations in the scene.");
            return;
        }

        // Step 1: compute fake readings as if this fake epicenter really happened
        foreach (var station in seismicStations)
        {
            Vector2 stationPos2D = new Vector2(station.transform.position.x, station.transform.position.z);
            float dist = Vector2.Distance(fakeEpicenter, stationPos2D);

            // Each wave's arrival time is independently derived from its own
            // real propagation speed - not a guessed ratio between the two.
            float pArrival = dist / pWaveSpeed;
            float sArrival = dist / sWaveSpeed;

            // NOTE: acceleration values below are intentionally unused by this
            // test (Trilateration only needs pWaveArrivalTime). They're left
            // at 0 rather than a fake nonzero number, so nothing downstream
            // could mistake this for a real reading if it's ever read by
            // accident. Real acceleration values are Layer 2's job.
            SeismicReading reading = new SeismicReading
            {
                stationId = station.stationId,
                timestamp = Time.time,
                accelerationX_g = 0f,
                accelerationY_g = 0f,
                accelerationZ_g = 0f,
                pWaveArrivalTime = pArrival,
                sWaveArrivalTime = sArrival
            };

            // Step 2: push into the real data store
            SensorDataStore.Instance.RecordSeismicReading(reading);

            Debug.Log($"[Layer1Test] {station.stationId} @ {stationPos2D} | " +
                      $"dist={dist:F2}km | P-arrival={pArrival:F3}s | S-arrival={sArrival:F3}s");
        }

        // Step 3: pull back out of the store and feed into Trilateration
        var allReadings = SensorDataStore.Instance.GetAllLatestSeismic();
        var trilaterationInput = new Trilateration.StationReading[seismicStations.Count];

        int i = 0;
        foreach (var station in seismicStations)
        {
            SeismicReading r = allReadings[station.stationId];
            Vector2 pos2D = new Vector2(station.transform.position.x, station.transform.position.z);
            trilaterationInput[i] = new Trilateration.StationReading
            {
                position = pos2D,
                pWaveArrivalTime = r.pWaveArrivalTime
            };
            i++;
        }

        Vector2 estimated = Trilateration.EstimateEpicenter(trilaterationInput, pWaveSpeed);
        float error = Vector2.Distance(fakeEpicenter, estimated);

        Debug.Log($"[Layer1Test] True epicenter: {fakeEpicenter}");
        Debug.Log($"[Layer1Test] Estimated (via full store round-trip): {estimated}");
        Debug.Log($"[Layer1Test] Error: {error:F4} -> " + (error < 0.01f ? "PASS - Layer 1 complete" : "CHECK WIRING"));
    }
}