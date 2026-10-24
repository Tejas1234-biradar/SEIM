using UnityEngine;

/// <summary>
/// Attach to a building GameObject with a collider and Rigidbody. The building
/// is driven by the same signed ground acceleration used by the sensor layer.
/// Applying the inertial load at the building base lets Unity calculate the
/// resulting overturning moment and collision response.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BoxCollider))]
public class BuildingShaker : MonoBehaviour
{
    [Header("Identification")]
    [Tooltip("Zone/building ID used when this data is exported to your sensor/AI layer")]
    public string zoneId = "Zone_1";

    [Header("Structural Response")]
    [Tooltip("Multiplier for the physical earthquake load. Keep at 1 for the model's acceleration.")]
    [Min(0f)]
    public float visualResponseMultiplier = 1f;
    [Tooltip("Horizontal acceleration at which structural damage begins accumulating.")]
    public float damageThresholdG = 0.3f;
    [Tooltip("Mass used by the rigidbody. Larger buildings resist sudden velocity changes.")]
    public float buildingMass = 1000f;
    [Tooltip("Horizontal restoring stiffness in N/m. Increase for stiffer buildings.")]
    [Min(0f)]
    public float stiffness = 40000f;
    [Tooltip("Horizontal velocity damping in N*s/m. Critical damping is approximately 2*sqrt(stiffness*mass).")]
    [Min(0f)]
    public float dampingCoefficient = 12000f;
    [Tooltip("Restoring torque in N*m/rad that keeps the building near its original upright orientation.")]
    [Min(0f)]
    public float uprightStiffness = 8000f;
    [Tooltip("Unity rigidbody linear damping applied even when no quake is active.")]
    [Min(0f)]
    public float linearDamping = 0.15f;
    [Tooltip("Unity rigidbody angular damping applied even when no quake is active.")]
    [Min(0f)]
    public float angularDamping = 0.25f;
    [Tooltip("Per-building ground-motion variation in g. Small values keep buildings correlated but unsynchronized.")]
    [Range(0f, 0.3f)]
    public float shakingNoiseAmplitudeG = 0.06f;
    [Tooltip("Frequency of the smooth per-building shaking variation.")]
    [Min(0.01f)]
    public float shakingNoiseFrequencyHz = 0.8f;
    [Header("Temporary Diagnostics")]
    [Tooltip("Logs earthquake acceleration, force, and velocity once per second during a quake.")]
    public bool logPhysicsDiagnostics = true;

    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private float currentAcceleration; // most recent reading, in g
    private Vector3 basePointLocal;
    private float nextDiagnosticsTime;
    private float noiseSeed;

    void Start()
    {
        originalPosition = transform.position;
        originalRotation = transform.rotation;

        Rigidbody rb = GetComponent<Rigidbody>();
        EnsureBuildingCollider();
        rb.isKinematic = false;
        rb.mass = Mathf.Max(1f, buildingMass);
        rb.useGravity = true;
        rb.linearDamping = linearDamping;
        rb.angularDamping = angularDamping;
        rb.detectCollisions = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        basePointLocal = transform.InverseTransformPoint(FindBasePoint(rb));
        noiseSeed = Mathf.Abs(zoneId.GetHashCode() * 0.00137f + originalPosition.x * 0.173f + originalPosition.z * 0.311f);
    }

    void FixedUpdate()
    {
        if (SimManager.Instance == null) return;

        SeismicReading reading = EarthquakeModel.GetReadingAt(
            originalPosition,
            zoneId,
            SimManager.Instance.ElapsedTime);
        currentAcceleration = reading.Magnitude;

        Vector3 groundAcceleration = new Vector3(
            reading.accelerationX_g,
            reading.accelerationY_g,
            reading.accelerationZ_g) * Physics.gravity.magnitude * visualResponseMultiplier;

        if (reading.Magnitude > 0f && shakingNoiseAmplitudeG > 0f)
        {
            float noiseTime = Time.time * shakingNoiseFrequencyHz;
            float noiseX = Mathf.PerlinNoise(noiseSeed, noiseTime) * 2f - 1f;
            float noiseZ = Mathf.PerlinNoise(noiseSeed + 17.31f, noiseTime) * 2f - 1f;
            Vector3 noiseAcceleration = new Vector3(noiseX, noiseZ * 0.2f, noiseZ) *
                                        (shakingNoiseAmplitudeG * Physics.gravity.magnitude);
            groundAcceleration += noiseAcceleration * visualResponseMultiplier;
        }

        // Relative to the ground, the building experiences the opposite inertial load.
        Rigidbody rb = GetComponent<Rigidbody>();
        Vector3 inertialForce = -groundAcceleration * rb.mass;
        Vector3 horizontalOffset = Vector3.ProjectOnPlane(rb.position - originalPosition, Vector3.up);
        Vector3 horizontalVelocity = Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);
        Vector3 restoringForce = -stiffness * horizontalOffset - dampingCoefficient * horizontalVelocity;
        Vector3 basePoint = transform.TransformPoint(basePointLocal);
        rb.AddForceAtPosition(inertialForce + restoringForce, basePoint, ForceMode.Force);

        Quaternion rotationError = originalRotation * Quaternion.Inverse(rb.rotation);
        rotationError.ToAngleAxis(out float angleDegrees, out Vector3 axis);
        if (angleDegrees > 180f) angleDegrees -= 360f;
        if (axis.sqrMagnitude > 0.001f)
        {
            Vector3 restoringTorque = axis.normalized * (Mathf.Deg2Rad * angleDegrees * uprightStiffness);
            rb.AddTorque(restoringTorque, ForceMode.Force);
        }

        if (logPhysicsDiagnostics && Time.time >= nextDiagnosticsTime)
        {
            Debug.Log($"[BuildingShaker:{zoneId}] acceleration={groundAcceleration.magnitude:F3} m/s^2 " +
                      $"({reading.accelerationX_g:F3}, {reading.accelerationY_g:F3}, {reading.accelerationZ_g:F3})g, " +
                      $"force={inertialForce.magnitude:F1}N, velocity={rb.linearVelocity.magnitude:F3}m/s");
            nextDiagnosticsTime = Time.time + 1f;
        }
    }

    private void EnsureBuildingCollider()
    {
        BoxCollider boxCollider = GetComponent<BoxCollider>();
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null)
            return;

        // Imported building meshes often have no physics shape. Use the mesh's
        // local bounds so the rigidbody cannot fall through a ground collider.
        boxCollider.center = meshFilter.sharedMesh.bounds.center;
        boxCollider.size = meshFilter.sharedMesh.bounds.size;
    }

    private Vector3 FindBasePoint(Rigidbody rb)
    {
        Collider[] colliders = GetComponentsInChildren<Collider>();
        if (colliders.Length == 0)
            return transform.TransformPoint(rb.centerOfMass) - transform.up;

        Bounds bounds = colliders[0].bounds;
        for (int i = 1; i < colliders.Length; i++)
            bounds.Encapsulate(colliders[i].bounds);

        return bounds.center - transform.up * (bounds.size.y * 0.5f);
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