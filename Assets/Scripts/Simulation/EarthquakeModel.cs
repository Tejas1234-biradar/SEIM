using UnityEngine;

/// <summary>
/// Pure physics model for an earthquake event. This is a stateless utility
/// class — it reads the current event parameters from SimManager and returns
/// what any world position is experiencing right now.
///
/// This is NOT a MonoBehaviour. It owns no Unity lifecycle. StationSeismicFeeder
/// (Phase 3) calls GetReadingAt() every frame; BuildingShaker calls it too
/// (Phase 4). Same physics drives both — no diverging implementations.
///
/// Physics basis (all improvements over the old EarthquakeSimulator prototype):
///   1. Hypocentral distance — sqrt(surfaceDist² + depth²), not flat 2D.
///   2. 3-axis output — X (E-W), Y (vertical/Up-Down), Z (N-S) axes.
///   3. Independent P/S wave speeds, each from its own real constant.
///   4. GMPE (Boore-Atkinson style): ln(PGA) = a*M - b*ln(R) - c*R + d
///   5. Distance floor of 0.1 km to avoid log(0), but NOT 0.5 km — the
///      0.5 km floor from EarthquakeSimulator.cs (line 101 there) was
///      diagnosed as flattening real near-epicenter differences. Fixed here.
///   6. Soil amplification multiplier per site class.
/// </summary>
public static class EarthquakeModel
{
    // -----------------------------------------------------------------------
    //  GMPE constants (Boore-Atkinson style, matching EarthquakeSimulator defaults)
    // -----------------------------------------------------------------------
    public const float GMPE_A = 0.5f;   // magnitude scaling
    public const float GMPE_B = 0.5f;   // geometric spreading
    public const float GMPE_C = 0.01f;  // anelastic attenuation
    public const float GMPE_D = -1.5f;  // baseline offset

    // -----------------------------------------------------------------------
    //  Wave speeds
    // -----------------------------------------------------------------------
    public const float P_WAVE_SPEED_KM_S = 6.0f;   // typical crustal P-wave
    public const float S_WAVE_SPEED_KM_S = 3.5f;   // typical crustal S-wave

    // -----------------------------------------------------------------------
    //  Waveform shape
    // -----------------------------------------------------------------------
    public const float DOMINANT_FREQ_HZ     = 1.5f;
    public const float QUAKE_DURATION_S     = 12f;  // how long shaking lasts after S-wave arrival

    // -----------------------------------------------------------------------
    //  Distance floor (MUCH smaller than the old 0.5 km — see class comment)
    // -----------------------------------------------------------------------
    private const float MIN_DISTANCE_KM = 0.1f;

    // -----------------------------------------------------------------------
    //  Axis split fractions (physically: S-waves dominate horizontal motion;
    //  P-waves are strongest on the vertical axis)
    // -----------------------------------------------------------------------
    //  P-wave contribution per axis (normalized across X, Y, Z to sum to 1.0)
    private const float P_FRAC_Y = 0.70f;   // vertical (Up-Down) — P-wave dominated
    private const float P_FRAC_X = 0.20f;   // East-West
    private const float P_FRAC_Z = 0.10f;   // North-South

    //  S-wave contribution per axis
    private const float S_FRAC_X = 0.55f;   // East-West — horizontal S dominant
    private const float S_FRAC_Z = 0.35f;   // North-South
    private const float S_FRAC_Y = 0.10f;   // vertical (much smaller for S-waves)

    // -----------------------------------------------------------------------
    //  Site classes (soil amplification)
    // -----------------------------------------------------------------------
    /// <summary>
    /// Site amplification class. Pass this into GetReadingAt() to apply
    /// a realistic soil-amplification multiplier.
    /// - HardRock:    ~0.8× (stiff bedrock, slight de-amplification)
    /// - Bedrock:     1.0× (reference class, no correction)
    /// - StiffSoil:   1.5× (dense alluvium, gravel)
    /// - SoftSoil:    2.5× (soft clay, fill)
    /// - Verysoft:    4.0× (bay mud, reclaimed land — high amplification zones
    ///                     like parts of coastal cities)
    /// </summary>
    public enum SiteClass { HardRock, Bedrock, StiffSoil, SoftSoil, VerySoft }

    public static float SiteAmplification(SiteClass site)
    {
        switch (site)
        {
            case SiteClass.HardRock:  return 0.8f;
            case SiteClass.Bedrock:   return 1.0f;
            case SiteClass.StiffSoil: return 1.5f;
            case SiteClass.SoftSoil:  return 2.5f;
            case SiteClass.VerySoft:  return 4.0f;
            default:                  return 1.0f;
        }
    }

    // -----------------------------------------------------------------------
    //  Core API
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns the SeismicReading a sensor at <paramref name="worldPos"/> would
    /// record at <paramref name="currentTime"/> seconds after quake trigger.
    ///
    /// Call this from StationSeismicFeeder every frame (Phase 3).
    ///
    /// Returns a zeroed reading if SimManager is not in EarthquakeActive or
    /// TsunamiActive state (i.e., no event is running), or if the wave
    /// hasn't reached this position yet.
    /// </summary>
    /// <param name="worldPos">World-space position of the sensor.</param>
    /// <param name="stationId">Station ID to embed in the returned reading.</param>
    /// <param name="currentTime">Seconds since the earthquake was triggered (SimManager.ElapsedTime).</param>
    /// <param name="site">Soil type at this station — applies amplification multiplier.</param>
    public static SeismicReading GetReadingAt(
        Vector3 worldPos,
        string stationId,
        float currentTime,
        SiteClass site = SiteClass.Bedrock)
    {
        var reading = new SeismicReading
        {
            stationId           = stationId,
            timestamp           = currentTime,
            accelerationX_g     = 0f,
            accelerationY_g     = 0f,
            accelerationZ_g     = 0f
        };

        if (SimManager.Instance == null) return reading;

        var state = SimManager.Instance.CurrentState;
        if (state != SimEventState.EarthquakeActive &&
            state != SimEventState.TsunamiActive)
            return reading;

        float magnitude     = SimManager.Instance.Magnitude;
        Vector2 epicenter2D = SimManager.Instance.EpicenterSurfacePos; // (x, z) world
        float depthKm       = SimManager.Instance.DepthKm;
        float kmPerUnit     = SimManager.Instance.KmPerUnit;

        // 1. Surface distance (horizontal only, in km)
        float dx = worldPos.x - epicenter2D.x;
        float dz = worldPos.z - epicenter2D.y;   // Vector2.y maps to world Z
        float surfaceDistKm = Mathf.Sqrt(dx * dx + dz * dz) * kmPerUnit;

        // 2. Hypocentral distance (3D, incorporating depth) — key fix vs old prototype
        float hypocentralKm = Mathf.Sqrt(surfaceDistKm * surfaceDistKm + depthKm * depthKm);
        float r = Mathf.Max(hypocentralKm, MIN_DISTANCE_KM);

        // 3. GMPE → PGA (in g)
        float lnPGA = GMPE_A * magnitude - GMPE_B * Mathf.Log(r) - GMPE_C * r + GMPE_D;
        float pga   = Mathf.Exp(lnPGA);

        // 4. Soil amplification
        float amp = SiteAmplification(site);
        pga *= amp;

        // 5. P-wave component
        // Arrival timing remains surface-based so Layer 1's intentional 2D
        // trilateration contract is preserved. Depth affects GMPE amplitude.
        float pArrival  = surfaceDistKm / P_WAVE_SPEED_KM_S;
        float tSinceP   = currentTime - pArrival;

        // P-wave PGA is typically ~10–20% of S-wave PGA in intensity
        float pgaP = pga * 0.15f;
        float pContrib = 0f;
        if (tSinceP >= 0f)
        {
            float decayP  = Mathf.Clamp01(1f - (tSinceP / (QUAKE_DURATION_S * 0.3f)));
            pContrib = pgaP * RickerWavelet(tSinceP % (2f / DOMINANT_FREQ_HZ)) * decayP;
        }

        // 6. S-wave component (main shaking)
        float sArrival = surfaceDistKm / S_WAVE_SPEED_KM_S;
        float tSinceS  = currentTime - sArrival;

        float sContrib = 0f;
        if (tSinceS >= 0f)
        {
            float decayS  = Mathf.Clamp01(1f - (tSinceS / QUAKE_DURATION_S));
            sContrib = pga * RickerWavelet(tSinceS % (2f / DOMINANT_FREQ_HZ)) * decayS;
        }

        // 7. Split across axes using physically-motivated fractions
        reading.accelerationX_g = pContrib * P_FRAC_X + sContrib * S_FRAC_X;
        reading.accelerationY_g = pContrib * P_FRAC_Y + sContrib * S_FRAC_Y;
        reading.accelerationZ_g = pContrib * P_FRAC_Z + sContrib * S_FRAC_Z;

        return reading;
    }

    /// <summary>
    /// P-wave arrival time (seconds after trigger) at a given surface distance.
    /// </summary>
    public static float PWaveArrivalTime(float surfaceDistanceKm)
        => surfaceDistanceKm / P_WAVE_SPEED_KM_S;

    /// <summary>
    /// S-wave arrival time (seconds after trigger) at a given surface distance.
    /// </summary>
    public static float SWaveArrivalTime(float surfaceDistanceKm)
        => surfaceDistanceKm / S_WAVE_SPEED_KM_S;

    /// <summary>
    /// Peak Ground Acceleration (g) at the given hypocentral distance for the given magnitude.
    /// Exposed publicly so Phase 2 test and BuildingShaker can query it directly.
    /// </summary>
    public static float ComputePGA(float magnitude, float hypocentralKm, SiteClass site = SiteClass.Bedrock)
    {
        float r = Mathf.Max(hypocentralKm, MIN_DISTANCE_KM);
        float lnPGA = GMPE_A * magnitude - GMPE_B * Mathf.Log(r) - GMPE_C * r + GMPE_D;
        return Mathf.Exp(lnPGA) * SiteAmplification(site);
    }

    /// <summary>
    /// Returns the current event's hypocentral distance from a world position.
    /// </summary>
    public static float GetHypocentralDistanceKm(Vector3 worldPos)
    {
        if (SimManager.Instance == null) return 0f;

        Vector2 epicenter = SimManager.Instance.EpicenterSurfacePos;
        float dx = worldPos.x - epicenter.x;
        float dz = worldPos.z - epicenter.y;
        float surfaceDistanceKm = Mathf.Sqrt(dx * dx + dz * dz) * SimManager.Instance.KmPerUnit;
        float depthKm = SimManager.Instance.DepthKm;
        return Mathf.Sqrt(surfaceDistanceKm * surfaceDistanceKm + depthKm * depthKm);
    }

    /// <summary>
    /// Returns the current event's horizontal surface distance from a world position.
    /// </summary>
    public static float GetSurfaceDistanceKm(Vector3 worldPos)
    {
        if (SimManager.Instance == null) return 0f;

        Vector2 epicenter = SimManager.Instance.EpicenterSurfacePos;
        float dx = worldPos.x - epicenter.x;
        float dz = worldPos.z - epicenter.y;
        return Mathf.Sqrt(dx * dx + dz * dz) * SimManager.Instance.KmPerUnit;
    }

    /// <summary>
    /// Returns the current event's GMPE PGA at a world position.
    /// </summary>
    public static float GetCurrentPGAAt(Vector3 worldPos, SiteClass site = SiteClass.Bedrock)
    {
        if (SimManager.Instance == null) return 0f;
        return ComputePGA(SimManager.Instance.Magnitude, GetHypocentralDistanceKm(worldPos), site);
    }

    /// <summary>
    /// Returns the P-wave arrival time for the current event at a world position.
    /// </summary>
    public static float GetPWaveArrivalTimeAt(Vector3 worldPos)
        => PWaveArrivalTime(GetSurfaceDistanceKm(worldPos));

    // -----------------------------------------------------------------------
    //  Internal helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Ricker wavelet at time t since arrival, centred so it peaks shortly
    /// after t=0. Returns values in roughly [-1, 1].
    /// </summary>
    private static float RickerWavelet(float tSinceArrival)
    {
        float f    = DOMINANT_FREQ_HZ;
        float t    = tSinceArrival - (1f / f); // offset so peak is near t=0
        float piSq = Mathf.PI * Mathf.PI;
        float ft2  = f * f * t * t;
        return (1f - 2f * piSq * ft2) * Mathf.Exp(-piSq * ft2);
    }
}
