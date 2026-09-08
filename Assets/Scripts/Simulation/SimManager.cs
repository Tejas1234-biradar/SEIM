using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Event lifecycle state for disaster simulation events.
/// </summary>
public enum SimEventState
{
    Idle,
    EarthquakeActive,
    TsunamiActive,
    Ended
}

/// <summary>
/// Central orchestrator ("God Object") for Layer 2 simulation.
/// Controls the disaster lifecycle and holds authoritative event parameters.
/// All other simulation components read event parameters from SimManager.
/// </summary>
public class SimManager : MonoBehaviour
{
    public static SimManager Instance { get; private set; }

    [Header("Current Event State")]
    [SerializeField] private SimEventState currentState = SimEventState.Idle;
    public SimEventState CurrentState => currentState;

    [Header("Current Event Parameters")]
    [SerializeField] private float magnitude = 0f;
    public float Magnitude => magnitude;

    [SerializeField] private Vector2 epicenterSurfacePos = Vector2.zero;
    public Vector2 EpicenterSurfacePos => epicenterSurfacePos;

    [SerializeField] private float depthKm = 10f;
    public float DepthKm => depthKm;

    [Tooltip("World-space units per real kilometre for surface distance calculations.")]
    [SerializeField] private float kmPerUnit = 0.01f;
    public float KmPerUnit => kmPerUnit;

    [SerializeField] private bool isOffshore = false;
    public bool IsOffshore => isOffshore;

    [SerializeField] private float elapsedTime = 0f;
    public float ElapsedTime => elapsedTime;

    [Header("Lifecycle Configuration")]
    [Tooltip("Total duration of the earthquake event before transitioning to Ended.")]
    public float eventDurationSeconds = 30f;

    [Header("Tsunami Trigger Conditions")]
    [Tooltip("Minimum magnitude to qualify for a tsunami trigger.")]
    public float tsunamiMinMagnitude = 7.0f;

    [Tooltip("Maximum hypocentral depth (km) for a tsunami trigger (shallow quakes only).")]
    public float tsunamiMaxDepthKm = 50.0f;

    [Tooltip("Delay in seconds before transitioning from EarthquakeActive to TsunamiActive.")]
    public float tsunamiDelaySeconds = 3.0f;

    [Tooltip("World-space coordinate or boundary indicator used to infer offshore if not specified.")]
    public float offshoreBoundaryX = 0f;

    [Header("Inspector Test Trigger")]
    public float testMagnitude = 6.5f;
    public Vector2 testEpicenter = new Vector2(15f, 8f);
    public float testDepthKm = 10f;
    public bool testIsOffshore = false;

    public event Action<SimEventState> OnStateChanged;

    private Coroutine tsunamiTransitionCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        if (currentState == SimEventState.EarthquakeActive || currentState == SimEventState.TsunamiActive)
        {
            elapsedTime += Time.deltaTime;
            if (elapsedTime >= eventDurationSeconds)
            {
                TransitionToState(SimEventState.Ended);
            }
        }
    }

    /// <summary>
    /// Triggers an earthquake event with given magnitude, 2D surface epicenter, and hypocentral depth.
    /// Infers offshore status if not explicitly specified.
    /// </summary>
    public void TriggerEarthquake(float magnitude, Vector2 epicenterSurfacePos, float depthKm)
    {
        // Infer offshore if epicenter is west of offshoreBoundaryX or custom check
        bool inferredOffshore = epicenterSurfacePos.x < offshoreBoundaryX;
        TriggerEarthquake(magnitude, epicenterSurfacePos, depthKm, inferredOffshore);
    }

    /// <summary>
    /// Triggers an earthquake event with explicit offshore designation.
    /// </summary>
    public void TriggerEarthquake(float magnitude, Vector2 epicenterSurfacePos, float depthKm, bool isOffshore)
    {
        if (tsunamiTransitionCoroutine != null)
        {
            StopCoroutine(tsunamiTransitionCoroutine);
            tsunamiTransitionCoroutine = null;
        }

        this.magnitude = magnitude;
        this.epicenterSurfacePos = epicenterSurfacePos;
        this.depthKm = depthKm;
        this.isOffshore = isOffshore;
        this.elapsedTime = 0f;

        Debug.Log($"[SimManager] Earthquake triggered: M{magnitude:F1}, Epicenter=({epicenterSurfacePos.x:F2}, {epicenterSurfacePos.y:F2}), Depth={depthKm:F1}km, Offshore={isOffshore}");

        TransitionToState(SimEventState.EarthquakeActive);

        // Check tsunami trigger condition: offshore + magnitude >= 7.0 + shallow depth
        if (CheckTsunamiTriggerCondition(magnitude, depthKm, isOffshore))
        {
            Debug.Log($"[SimManager] Tsunami trigger condition MET (M{magnitude:F1} >= {tsunamiMinMagnitude}, Depth {depthKm:F1}km <= {tsunamiMaxDepthKm}km, Offshore={isOffshore}). Transitioning to TsunamiActive in {tsunamiDelaySeconds:F1}s.");
            tsunamiTransitionCoroutine = StartCoroutine(TransitionToTsunamiAfterDelay(tsunamiDelaySeconds));
        }
        else
        {
            Debug.Log($"[SimManager] Tsunami trigger condition not met. Event remains purely seismic.");
        }
    }

    /// <summary>
    /// Resets the simulation state back to Idle.
    /// </summary>
    public void ResetToIdle()
    {
        if (tsunamiTransitionCoroutine != null)
        {
            StopCoroutine(tsunamiTransitionCoroutine);
            tsunamiTransitionCoroutine = null;
        }

        elapsedTime = 0f;
        TransitionToState(SimEventState.Idle);
    }

    private bool CheckTsunamiTriggerCondition(float mag, float depth, bool offshore)
    {
        return offshore && (mag >= tsunamiMinMagnitude) && (depth <= tsunamiMaxDepthKm);
    }

    private IEnumerator TransitionToTsunamiAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        tsunamiTransitionCoroutine = null;

        if (currentState == SimEventState.EarthquakeActive)
        {
            TransitionToState(SimEventState.TsunamiActive);
        }
    }

    private void TransitionToState(SimEventState newState)
    {
        if (currentState == newState) return;

        SimEventState previousState = currentState;
        currentState = newState;

        Debug.Log($"[SimManager] State transition: {previousState} -> {newState} (elapsed={elapsedTime:F2}s)");
        OnStateChanged?.Invoke(newState);
    }

    [ContextMenu("Trigger Test Earthquake (M6.5, Land)")]
    public void TriggerTestEarthquakeLand()
    {
        TriggerEarthquake(testMagnitude, testEpicenter, testDepthKm, false);
    }

    [ContextMenu("Trigger Test Earthquake (M7.5, Offshore Tsunami)")]
    public void TriggerTestEarthquakeOffshore()
    {
        TriggerEarthquake(7.5f, testEpicenter, 15f, true);
    }

    [ContextMenu("Reset to Idle")]
    public void ResetTest()
    {
        ResetToIdle();
    }
}
