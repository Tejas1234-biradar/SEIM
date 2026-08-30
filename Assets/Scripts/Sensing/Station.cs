using UnityEngine;

/// <summary>
/// Represents one sensor station. Attach to an empty GameObject and position
/// it in the scene - transform.position IS the station's position, no need
/// to duplicate that data.
///
/// Station types are grounded in real deployed networks:
///   - Seismic          : land accelerometer (USGS ShakeAlert-style)
///   - Gnss              : land GPS/GNSS displacement receiver (ShakeAlert)
///   - OceanBottomCombo  : offshore seismometer + pressure gauge in one unit
///                         (Japan's S-net / DONET style)
///   - DartBuoy          : deep-ocean bottom pressure recorder (NOAA DART)
///   - TideGauge         : coastal sea-level sensor
///
/// A station can support MORE THAN ONE reading type (e.g. OceanBottomCombo
/// produces both a SeismicReading and a pressure-based reading) - see
/// SensorReadings.cs and SensorDataStore.cs for how that's handled.
/// </summary>
public enum StationType
{
    Seismic,
    Gnss,
    OceanBottomCombo,
    DartBuoy,
    TideGauge
}

public class Station : MonoBehaviour
{
    [Header("Identification")]
    [Tooltip("Unique ID for this station, e.g. 'ST_01'. Used as the key everywhere downstream.")]
    public string stationId;

    [Header("Type")]
    public StationType stationType = StationType.Seismic;

    private void OnValidate()
    {
        // Auto-generate an ID from the GameObject name if one hasn't been set yet,
        // so you don't have to type it twice.
        if (string.IsNullOrEmpty(stationId))
        {
            stationId = gameObject.name;
        }
    }

    /// <summary>
    /// Draws a colored wireframe sphere per station type in the Scene view,
    /// so you can visually distinguish your network without needing 3D models.
    /// </summary>
    private void OnDrawGizmos()
    {
        Gizmos.color = ColorForType(stationType);
        Gizmos.DrawWireSphere(transform.position, 1f);

#if UNITY_EDITOR
        UnityEditor.Handles.Label(transform.position + Vector3.up * 1.2f,
            $"{stationId} ({stationType})");
#endif
    }

    private static Color ColorForType(StationType type)
    {
        switch (type)
        {
            case StationType.Seismic: return Color.red;
            case StationType.Gnss: return Color.yellow;
            case StationType.OceanBottomCombo: return Color.cyan;
            case StationType.DartBuoy: return Color.blue;
            case StationType.TideGauge: return Color.green;
            default: return Color.white;
        }
    }
}
