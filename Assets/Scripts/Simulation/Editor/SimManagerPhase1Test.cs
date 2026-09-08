using UnityEditor;
using UnityEngine;

public static class SimManagerPhase1Test
{
    [MenuItem("SEIM/Test Phase 1 (SimManager)")]
    public static void RunTest()
    {
        ExecuteTest(false);
    }

    public static void RunPhase1TestHeadless()
    {
        ExecuteTest(true);
    }

    private static void ExecuteTest(bool isBatchmode)
    {
        Debug.Log("=================================================");
        Debug.Log("[SimManagerPhase1Test] STARTING PHASE 1 VERIFICATION");
        Debug.Log("=================================================");

        // Setup clean GameObject with SimManager
        GameObject go = new GameObject("TestSimManager");
        SimManager sim = go.AddComponent<SimManager>();

        Debug.Log($"[SimManagerPhase1Test] Initial State: {sim.CurrentState}");
        if (sim.CurrentState != SimEventState.Idle)
        {
            Debug.LogError($"[SimManagerPhase1Test] FAIL: Expected Initial State Idle, got {sim.CurrentState}");
            if (isBatchmode) EditorApplication.Exit(1);
            return;
        }

        // Test TriggerEarthquake(6.5f, pos, 10f) per checkpoint requirement
        Vector2 testPos = new Vector2(15f, 8f);
        Debug.Log($"[SimManagerPhase1Test] Invoking SimManager.Instance.TriggerEarthquake(6.5f, ({testPos.x}, {testPos.y}), 10f)...");
        sim.TriggerEarthquake(6.5f, testPos, 10f);

        Debug.Log($"[SimManagerPhase1Test] Result State: {sim.CurrentState}");
        Debug.Log($"[SimManagerPhase1Test] Result Magnitude: {sim.Magnitude}");
        Debug.Log($"[SimManagerPhase1Test] Result Epicenter: {sim.EpicenterSurfacePos}");
        Debug.Log($"[SimManagerPhase1Test] Result Depth: {sim.DepthKm} km");
        Debug.Log($"[SimManagerPhase1Test] Result IsOffshore: {sim.IsOffshore}");

        if (sim.CurrentState == SimEventState.EarthquakeActive &&
            Mathf.Approximately(sim.Magnitude, 6.5f) &&
            Mathf.Approximately(sim.DepthKm, 10f) &&
            sim.EpicenterSurfacePos == testPos)
        {
            Debug.Log("[SimManagerPhase1Test] SUCCESS: State transitioned Idle -> EarthquakeActive with correct parameters!");
        }
        else
        {
            Debug.LogError("[SimManagerPhase1Test] FAIL: Unexpected state or parameters after TriggerEarthquake!");
            if (isBatchmode) EditorApplication.Exit(1);
            return;
        }

        // Also verify offshore tsunami trigger condition
        Debug.Log("[SimManagerPhase1Test] Testing Offshore M7.5 Tsunami Condition Check...");
        sim.TriggerEarthquake(7.5f, new Vector2(-20f, 5f), 12f, true);
        Debug.Log($"[SimManagerPhase1Test] Result Offshore State: {sim.CurrentState}, Offshore={sim.IsOffshore}");

        // Cleanup
        Object.DestroyImmediate(go);

        Debug.Log("=================================================");
        Debug.Log("[SimManagerPhase1Test] PHASE 1 VERIFICATION COMPLETED");
        Debug.Log("=================================================");

        if (isBatchmode)
        {
            EditorApplication.Exit(0);
        }
    }
}
