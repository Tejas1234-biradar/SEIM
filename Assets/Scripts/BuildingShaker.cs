using UnityEngine;

/// <summary>
/// Attach to each building GameObject. Reads the current ground acceleration
/// from EarthquakeSimulator (based on this building's distance from the
/// epicenter) and applies a believable visual response - positional jitter
/// plus a severity-based tilt. This is a VISUAL PROXY, not structural FEA.
///
/// Also exposes GetCurrentAcceleration() as the hook point for your virtual
/// sensor layer (e.g. a Python VirtualMPU6050 reading this value, or a
/// direct C# equivalent if you keep sensing inside Unity).
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
    /// layer should poll/subscribe to. Replace/extend this to also push
    /// data out via HTTP/WebSocket to your Python sensor service.
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
