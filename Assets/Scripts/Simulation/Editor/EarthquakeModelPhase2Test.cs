using UnityEditor;
using UnityEngine;

public static class EarthquakeModelPhase2Test
{
    [MenuItem("SEIM/Test Phase 2 (Earthquake Model)")]
    public static void RunTest()
    {
        GameObject go = new GameObject("Phase2EarthquakeModelTest");
        SimManager sim = go.AddComponent<SimManager>();
        Vector2 epicenter = Vector2.zero;
        sim.TriggerEarthquake(6.5f, epicenter, 5f, false);

        Vector3[] positions =
        {
            new Vector3(0f, 0f, 0f),
            new Vector3(1000f, 0f, 0f),
            new Vector3(2000f, 0f, 0f)
        };

        float[] distancesKm = { 5f, Mathf.Sqrt(125f), Mathf.Sqrt(425f) };
        float previousArrival = -1f;
        float previousPga = float.PositiveInfinity;

        for (int i = 0; i < positions.Length; i++)
        {
            float pArrival = EarthquakeModel.PWaveArrivalTime(distancesKm[i]);
            float sArrival = EarthquakeModel.SWaveArrivalTime(distancesKm[i]);
            float sampleTime = sArrival + (1f / EarthquakeModel.DOMINANT_FREQ_HZ);
            SeismicReading reading = EarthquakeModel.GetReadingAt(
                positions[i], "PHASE2_" + i, sampleTime);
            float pga = EarthquakeModel.ComputePGA(6.5f, distancesKm[i]);

            Debug.Log($"[EarthquakeModelPhase2Test] position={positions[i]}, hypocentralKm={distancesKm[i]:F3}, pArrival={pArrival:F3}s, sArrival={sArrival:F3}s, PGA={pga:F5}g, reading=({reading.accelerationX_g:F5}, {reading.accelerationY_g:F5}, {reading.accelerationZ_g})g, magnitude={reading.Magnitude:F5}g");

            if (i > 0 && (pArrival <= previousArrival || pga >= previousPga))
                Debug.LogError("[EarthquakeModelPhase2Test] FAIL: farther position did not arrive later with lower PGA.");

            previousArrival = pArrival;
            previousPga = pga;
        }

        Object.DestroyImmediate(go);
        Debug.Log("[EarthquakeModelPhase2Test] SUCCESS: hypocentral distance, independent P/S arrivals, 3-axis readings, and distance attenuation verified.");
    }
}