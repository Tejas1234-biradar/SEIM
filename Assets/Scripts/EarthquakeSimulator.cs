using UnityEngine;

/// <summary>
/// Central earthquake event manager.
/// Attach this to one empty GameObject in the scene (e.g. "EarthquakeManager").
/// Buildings query this for their local ground acceleration each frame.
///
/// Physics basis:
///   - Ground Motion Prediction Equation (GMPE), Boore-Atkinson style:
///       ln(PGA) = a*M - b*ln(R) - c*R + d
///   - Synthetic waveform shape: Ricker wavelet, one per building based on
///     its own distance-triggered arrival time (S-wave is what mainly matters
///     for shaking; P-wave arrives first but is much weaker).
/// </summary>
public class EarthquakeSimulator : MonoBehaviour
{
    public static EarthquakeSimulator Instance { get; private set; }

    [Header("Event Parameters (set these to trigger a quake)")]
    [Tooltip("Moment magnitude of the event, e.g. 6.5")]
    public float magnitude = 6.5f;

    [Tooltip("World-space epicenter position (usually just above/at ground level)")]
    public Transform epicenter;

    [Tooltip("How many real-world km one Unity unit represents. " +
             "If your city is built at real 1:1 scale (1 unit = 1 metre) but you want " +
             "the epicenter to feel kilometres away, increase this instead of moving " +
             "the epicenter transform very far from the scene.")]
    public float kmPerUnit = 0.01f; // 1 unit = 10 m by default

    [Header("GMPE Constants (Boore-Atkinson-style, tune as needed)")]
    public float gmpe_a = 0.5f;
    public float gmpe_b = 0.5f;
    public float gmpe_c = 0.01f;
    public float gmpe_d = -1.5f;

    [Header("Wave Timing")]
    [Tooltip("S-wave speed in km/s (typical crustal value ~3.5)")]
    public float sWaveSpeedKmS = 3.5f;
    [Tooltip("Dominant frequency of shaking in Hz - higher = sharper, shorter shakes")]
    public float dominantFrequencyHz = 1.5f;

    [Header("Runtime State")]
    public bool quakeActive = false;
    [Tooltip("Time since the quake was triggered, in seconds")]
    public float quakeElapsedTime = 0f;
    [Tooltip("How long the shaking lasts after S-wave arrival, in seconds")]
    public float quakeDurationSeconds = 12f;

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        if (quakeActive)
        {
            quakeElapsedTime += Time.deltaTime;
            if (quakeElapsedTime > quakeDurationSeconds + 5f) // small buffer past last arrival
            {
                quakeActive = false;
            }
        }
    }

    /// <summary>Call this to start an earthquake event.</summary>
    public void TriggerQuake(float mag, Transform epicenterTransform)
    {
        magnitude = mag;
        epicenter = epicenterTransform;
        quakeElapsedTime = 0f;
        quakeActive = true;
        Debug.Log($"[EarthquakeSimulator] Quake triggered: M{mag} at {epicenterTransform.position}");
    }

    /// <summary>
    /// Distance from a world position to the epicenter, in km.
    /// </summary>
    public float DistanceToEpicenterKm(Vector3 worldPos)
    {
        if (epicenter == null) return 0f;
        float unityDist = Vector3.Distance(worldPos, epicenter.position);
        return unityDist * kmPerUnit;
    }

    /// <summary>
    /// Peak Ground Acceleration (in g) at a given distance, per the GMPE.
    /// </summary>
    public float ComputePGA(float distanceKm)
    {
        // Avoid log(0) / divide-by-zero right at the epicenter
        float r = Mathf.Max(distanceKm, 0.5f);
        float lnPGA = gmpe_a * magnitude - gmpe_b * Mathf.Log(r) - gmpe_c * r + gmpe_d;
        return Mathf.Exp(lnPGA); // PGA in g
    }

    /// <summary>
    /// S-wave arrival time at a given distance, in seconds after quake trigger.
    /// </summary>
    public float SWaveArrivalTime(float distanceKm)
    {
        return distanceKm / sWaveSpeedKmS;
    }

    /// <summary>
    /// Ricker wavelet value at time t (seconds since this building's S-wave arrived).
    /// Returns a value roughly in [-1, 1] representing the oscillation shape;
    /// multiply by PGA to get actual acceleration.
    /// </summary>
    public float RickerWavelet(float tSinceArrival)
    {
        float f = dominantFrequencyHz;
        // Centre the wavelet so it peaks shortly after arrival, not exactly at t=0
        float t = tSinceArrival - (1f / f);
        float piSq = Mathf.PI * Mathf.PI;
        float ft2 = f * f * t * t;
        return (1f - 2f * piSq * ft2) * Mathf.Exp(-piSq * ft2);
    }

    /// <summary>
    /// Full acceleration (in g) at a given world position and current quake time.
    /// This is the main method buildings/sensors should call every frame.
    /// </summary>
    public float GetAccelerationAt(Vector3 worldPos)
    {
        if (!quakeActive || epicenter == null) return 0f;

        float distKm = DistanceToEpicenterKm(worldPos);
        float pga = ComputePGA(distKm);
        float arrival = SWaveArrivalTime(distKm);
        float tSinceArrival = quakeElapsedTime - arrival;

        if (tSinceArrival < 0f) return 0f; // wave hasn't reached this point yet

        // Repeat the wavelet a few times to simulate sustained shaking,
        // decaying in amplitude over the quake duration.
        float decay = Mathf.Clamp01(1f - (tSinceArrival / quakeDurationSeconds));
        float oscillation = RickerWavelet(tSinceArrival % (2f / dominantFrequencyHz));

        return pga * oscillation * decay;
    }
}
