using System;
using UnityEngine;

/// <summary>
/// Data shapes for each sensor type, based on what the real equipment
/// actually outputs (see citations in the implementation plan / chat).
/// These are the contracts Layer 2 (simulation) must fill in, and what
/// Layer 3 (severity scoring / dashboard) will read.
/// </summary>

/// <summary>
/// Strong-motion accelerometer reading (land station, ShakeAlert-style).
/// Real units report 3-axis acceleration, not a single number - East-West,
/// North-South, and Up-Down are independent readings.
/// </summary>
[Serializable]
public struct SeismicReading
{
    public string stationId;
    public float timestamp;
    public float accelerationX_g; // East-West
    public float accelerationY_g; // Up-Down
    public float accelerationZ_g; // North-South
    public float pWaveArrivalTime; // seconds since quake origin, -1 if not yet arrived
    public float sWaveArrivalTime; // seconds since quake origin, -1 if not yet arrived

    /// <summary>Combined magnitude of the 3-axis reading - convenience for code that just wants "how strong."</summary>
    public float Magnitude => Mathf.Sqrt(accelerationX_g * accelerationX_g
                                        + accelerationY_g * accelerationY_g
                                        + accelerationZ_g * accelerationZ_g);
}

/// <summary>
/// GNSS/GPS displacement reading (land station). Unlike accelerometers,
/// these don't saturate at high magnitude - real ShakeAlert added this
/// specifically to handle very large events accelerometers miss.
/// </summary>
[Serializable]
public struct GnssReading
{
    public string stationId;
    public float timestamp;
    public float displacementX_m;
    public float displacementZ_m; // horizontal displacement, the primary signal GNSS adds
}

/// <summary>
/// Deep-ocean bottom pressure recorder reading (DART buoy style).
/// Real DART units sample every 15s but only REPORT every 15 minutes in
/// Standard mode, switching to more frequent Event mode when a tsunami is
/// suspected - reportingMode reflects that behavior.
/// </summary>
public enum DartReportingMode { Standard, Event }

[Serializable]
public struct DartBuoyReading
{
    public string stationId;
    public float timestamp;
    public float pressureChange_Pa;
    public float estimatedSeaSurfaceHeight_m; // pressure converted to height
    public DartReportingMode reportingMode;
}

/// <summary>
/// Coastal tide gauge reading - confirms a tsunami's arrival near shore,
/// same underlying principle as DART (pressure/level based) but positioned
/// at the coast rather than deep ocean.
/// </summary>
[Serializable]
public struct TideGaugeReading
{
    public string stationId;
    public float timestamp;
    public float seaLevelChange_m;
}

/// <summary>
/// Ocean-bottom combo reading (S-net/DONET style) - a single offshore
/// station producing BOTH a seismic reading and a pressure reading, since
/// real ocean-bottom stations bundle multiple sensor types in one unit.
/// </summary>
[Serializable]
public struct OceanBottomReading
{
    public string stationId;
    public float timestamp;
    public SeismicReading seismic;
    public float pressureChange_Pa;
}
