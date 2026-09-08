using UnityEngine;

/// <summary>
/// Attach to each building GameObject. Reads the current ground acceleration
/// from EarthquakeSimulator (based on this building's distance from the
/// epicenter) and applies a believable visual response - positional jitter
/// plus a severity-based tilt. This is a VISUAL PROXY, not structural FEA.
///
/// NOTE: this version reads from EarthquakeSimulator.Instance.GetAccelerationAt()
/// - the original prototype's API. Once Layer 2's EarthquakeModel.cs replaces
/// EarthquakeSimulator, update the one line in Update() marked below to call
/// EarthquakeModel.Instance.GetReadingAt() instead (see the Layer 2 plan,
/// Phase 4 - "Reconnect BuildingShaker" - for the exact expected change).
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class BuildingShaker : MonoBehaviour
{
    [Header("Identification")]
    [Tooltip("Zone/building ID used when this data is exported to your sensor/AI layer")]
    public string zoneId = "Zone_1";

    [Header("Visual Response Tuning")]
    [Tooltip("How much 1g of acceleration translates into visual position jitter (metres)")]
    public float jitterPerG = 0.15f;
    [Tooltip("Max tilt in degrees at high severity")]
    public float maxTiltDegrees = 6f;
    [Tooltip("Acceleration (g) above which the building starts visibly tilting/damaged")]
    public float damageThresholdG = 0.3f;

    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private float currentAcceleration; // most recent reading, in g
    private float smoothedSeverity;    // 0-1, smoothed for less jittery tilt

    void Start()
    {
        originalPosition = transform.position;
        originalRotation = transform.rotation;

        Rigidbody rb = GetComponent<Rigidbody>();
        rb.isKinematic = true; // we drive motion via script, not physics forces
    }

    void Update()
    {
        if (EarthquakeSimulator.Instance == null) return;

        // ---- THIS LINE changes when Layer 2's EarthquakeModel replaces EarthquakeSimulator ----
        currentAcceleration = EarthquakeSimulator.Instance.GetAccelerationAt(transform.position);

        // --- Positional jitter (visual "shake") ---
        Vector3 jitter = new Vector3(
            (Mathf.PerlinNoise(Time.time * 15f, zoneId.GetHashCode()) - 0.5f),
            0f,
            (Mathf.PerlinNoise(zoneId.GetHashCode(), Time.time * 15f) - 0.5f)
        ) * currentAcceleration * jitterPerG;

        transform.position = originalPosition + jitter;

        // --- Severity-based tilt (only kicks in past damage threshold) ---
        float severityRaw = Mathf.Clamp01(Mathf.Abs(currentAcceleration) / (damageThresholdG * 2f));
        smoothedSeverity = Mathf.Lerp(smoothedSeverity, severityRaw, Time.deltaTime * 2f);

        float tilt = smoothedSeverity * maxTiltDegrees;
        transform.rotation = originalRotation * Quaternion.Euler(tilt, 0f, tilt * 0.6f);
    }

    /// <summary>
    /// Current acceleration reading in g - this is what your virtual sensor
    /// layer should poll/subscribe to.
    /// </summary>
    public float GetCurrentAcceleration()
    {
        return currentAcceleration;
    }

    /// <summary>
    /// Convenience for the dashboard/decision engine: is this building
    /// currently past the damage threshold?
    /// </summary>
    public bool IsDamaged()
    {
        return Mathf.Abs(currentAcceleration) > damageThresholdG;
    }
}