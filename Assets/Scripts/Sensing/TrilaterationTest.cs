using UnityEngine;

/// <summary>
/// PURE MATH TEST - verifies Trilateration.cs's algorithm in isolation,
/// with hardcoded station positions (no Station GameObjects, no
/// StationNetwork, no SensorDataStore involved at all).
///
/// This is deliberately the lowest-level test in the project: if this ever
/// fails, the bug is in Trilateration.cs itself. If THIS passes but
/// Layer1IntegrationTest fails, the bug is in the wiring around it
/// (Station / StationNetwork / SensorDataStore), not the math.
///
/// See Layer1IntegrationTest.cs for the full-pipeline version of this same
/// idea, using real Station objects and SensorDataStore instead of a
/// hardcoded array.
/// </summary>
public class TrilaterationTest : MonoBehaviour
{
    [Header("Test Setup")]
    public Vector2 trueEpicenter = new Vector2(15f, 8f);
    public float pWaveSpeed = 6f; // km/s, typical crustal P-wave speed

    [Header("Test Station Positions (add at least 3)")]
    public Vector2[] stationPositions = new Vector2[]
    {
        new Vector2(0f, 0f),
        new Vector2(30f, 0f),
        new Vector2(15f, 25f),
        new Vector2(0f, 20f),
    };

    void Start()
    {
        RunTest();
    }

    [ContextMenu("Run Trilateration Test")]
    public void RunTest()
    {
        if (stationPositions.Length < 3)
        {
            Debug.LogError("[TrilaterationTest] Need at least 3 stations.");
            return;
        }

        // Step 1: simulate what each station "would have" recorded, given the
        // true epicenter we chose ourselves.
        var readings = new Trilateration.StationReading[stationPositions.Length];
        for (int i = 0; i < stationPositions.Length; i++)
        {
            float dist = Vector2.Distance(trueEpicenter, stationPositions[i]);
            float arrivalTime = dist / pWaveSpeed;

            readings[i] = new Trilateration.StationReading
            {
                position = stationPositions[i],
                pWaveArrivalTime = arrivalTime
            };
        }

        // Step 2: run the solver on that synthetic data
        Vector2 estimated = Trilateration.EstimateEpicenter(readings, pWaveSpeed);

        // Step 3: compare
        float error = Vector2.Distance(trueEpicenter, estimated);

        Debug.Log($"[TrilaterationTest] True epicenter: {trueEpicenter}");
        Debug.Log($"[TrilaterationTest] Estimated epicenter: {estimated}");
        Debug.Log($"[TrilaterationTest] Error: {error:F4} units " +
                   (error < 0.01f ? "-> PASS" : "-> CHECK YOUR SOLVER"));
    }
}
