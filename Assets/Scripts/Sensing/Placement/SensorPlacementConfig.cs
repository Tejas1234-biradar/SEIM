using UnityEngine;

public enum SensorPlacementTransport
{
    Http,
    Mqtt
}

public enum SensorPlacementXYCoordinates
{
    LocalMeters,
    WorldUnits
}

[CreateAssetMenu(fileName = "SensorPlacementConfig", menuName = "SEIM/Sensor Placement Config")]
public class SensorPlacementConfig : ScriptableObject
{
    [Header("Transport")]
    public SensorPlacementTransport transport = SensorPlacementTransport.Http;
    public string apiBaseUrl = "http://localhost:8080";
    public string computePath = "/api/placement/compute";
    [Min(1)] public int requestTimeoutSeconds = 15;
    public string mqttHost = "localhost";
    [Min(1)] public int mqttPort = 11883;
    public int mqttTimeoutMilliseconds = 3000;
    public string mqttTopic = "seim/placement/latest";

    [Header("City input (provide exactly one of polygon or area)")]
    public SensorPlacementGeoPoint[] boundaryPolygon = new SensorPlacementGeoPoint[0];
    public bool useArea;
    public SensorPlacementArea area = new SensorPlacementArea();
    [Min(0.001f)] public float sensorRadiusKm = 5f;
    [Min(1)] public int k = 3;
    [Min(1f)] public float gridResolutionMeters = 500f;

    [Header("Coordinate conversion")]
    [Tooltip("Geographic origin used when the placement response contains latitude/longitude.")]
    public float originLatitude;
    public float originLongitude;
    [Tooltip("Unity world position corresponding to the geographic origin, using X=east and Z=north.")]
    public Vector3 originWorldPosition;
    [Min(0.000001f)] public float worldUnitsPerMeter = 0.1f;
    [Tooltip("Interpret response x/y as world units instead of local meters.")]
    public SensorPlacementXYCoordinates xyCoordinates = SensorPlacementXYCoordinates.LocalMeters;
    [Tooltip("When true, replace existing seismic Stations on a successful placement.")]
    public bool replaceExistingSeismicStations = true;
    public bool HasValidCityInput(out string error)
    {
        bool hasPolygon = boundaryPolygon != null && boundaryPolygon.Length >= 3;
        bool hasArea = useArea && area != null &&
                       IsPositiveFinite(area.widthMeters) && IsPositiveFinite(area.heightMeters);
        if (hasPolygon == hasArea)
        {
            error = "Configure either a boundary polygon (at least 3 points) or a positive-sized area.";
            return false;
        }

        if (!IsPositiveFinite(sensorRadiusKm) || k < 1 ||
            !IsPositiveFinite(gridResolutionMeters) || !IsPositiveFinite(worldUnitsPerMeter))
        {
            error = "Sensor radius, k, grid resolution, and world scale must be positive.";
            return false;
        }

        if (hasArea && !IsValidLatitudeLongitude(area.centerLatitude, area.centerLongitude))
        {
            error = "The area center must use valid latitude and longitude values.";
            return false;
        }
        if (hasArea && Mathf.Abs(area.centerLatitude) >= 89.9f)
        {
            error = "Area-based placement is not supported near the geographic poles.";
            return false;
        }

        if (hasPolygon)
        {
            foreach (SensorPlacementGeoPoint point in boundaryPolygon)
            {
                if (point == null || !IsValidLatitudeLongitude(point.latitude, point.longitude))
                {
                    error = "Every boundary point must use valid latitude and longitude values.";
                    return false;
                }
            }
        }

        error = null;
        return true;
    }

    public SensorPlacementRequest CreateRequest()
    {
        return new SensorPlacementRequest
        {
            sensorRadiusKm = sensorRadiusKm,
            coverageMultiplicity = k,
            gridResolutionMeters = gridResolutionMeters
        };
    }

    public string CreateRequestJson()
    {
        SensorPlacementGeoPoint[] boundary = useArea ? CreateAreaBoundary() : boundaryPolygon;
        return CreateRequest().ToJsonWithBoundary(
            SensorPlacementRequest.BuildBoundaryGeoJson(boundary));
    }

    private SensorPlacementGeoPoint[] CreateAreaBoundary()
    {
        const double earthRadiusMeters = 6371000.0;
        double latitudeDelta = area.heightMeters * 0.5 / earthRadiusMeters * Mathf.Rad2Deg;
        double longitudeDelta = area.widthMeters * 0.5 /
                                (earthRadiusMeters * System.Math.Cos(area.centerLatitude * Mathf.Deg2Rad)) *
                                Mathf.Rad2Deg;
        double south = area.centerLatitude - latitudeDelta;
        double north = area.centerLatitude + latitudeDelta;
        double west = area.centerLongitude - longitudeDelta;
        double east = area.centerLongitude + longitudeDelta;
        if (south < -90.0 || north > 90.0 || west < -180.0 || east > 180.0)
            throw new System.ArgumentOutOfRangeException(
                nameof(area), "The configured area rectangle crosses a geographic boundary.");

        return new[]
        {
            new SensorPlacementGeoPoint { latitude = (float)south, longitude = (float)west },
            new SensorPlacementGeoPoint { latitude = (float)south, longitude = (float)east },
            new SensorPlacementGeoPoint { latitude = (float)north, longitude = (float)east },
            new SensorPlacementGeoPoint { latitude = (float)north, longitude = (float)west }
        };
    }

    private static bool IsValidLatitudeLongitude(float latitude, float longitude)
    {
        return !float.IsNaN(latitude) && !float.IsInfinity(latitude) &&
               !float.IsNaN(longitude) && !float.IsInfinity(longitude) &&
               latitude >= -90f && latitude <= 90f &&
               longitude >= -180f && longitude <= 180f;
    }

    private static bool IsPositiveFinite(float value)
    {
        return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
