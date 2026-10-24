using System;
using System.Globalization;
using System.Text;
using UnityEngine;

[Serializable]
public class SensorPlacementRequest
{
    public float sensorRadiusKm;
    public int coverageMultiplicity;
    public float gridResolutionMeters;

    public string ToJsonWithBoundary(string boundaryGeoJson)
    {
        string requestJson = JsonUtility.ToJson(this);
        return requestJson.Substring(0, requestJson.Length - 1) +
               ",\"boundary\":" + boundaryGeoJson + "}";
    }

    public static string BuildBoundaryGeoJson(SensorPlacementGeoPoint[] points)
    {
        if (points == null || points.Length < 3)
            throw new ArgumentException("A GeoJSON boundary needs at least three points.", nameof(points));

        StringBuilder json = new StringBuilder("{\"type\":\"Polygon\",\"coordinates\":[[");
        bool alreadyClosed = points[0].latitude == points[points.Length - 1].latitude &&
                             points[0].longitude == points[points.Length - 1].longitude;
        int pointCount = alreadyClosed ? points.Length : points.Length + 1;
        for (int i = 0; i < pointCount; i++)
        {
            SensorPlacementGeoPoint point = points[i % points.Length];
            if (point == null)
                throw new ArgumentException($"Boundary point at index {i % points.Length} is null.", nameof(points));
            if (i > 0)
                json.Append(',');
            json.Append('[')
                .Append(point.longitude.ToString("R", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(point.latitude.ToString("R", CultureInfo.InvariantCulture))
                .Append(']');
        }
        return json.Append("]]}").ToString();
    }
}

[Serializable]
public class SensorPlacementArea
{
    public float centerLatitude;
    public float centerLongitude;
    public float widthMeters;
    public float heightMeters;
}

[Serializable]
public class SensorPlacementGeoPoint
{
    public float latitude;
    public float longitude;
}

[Serializable]
public class SensorPlacementResponse
{
    public PlacementSensor[] sensors;
    public PlacementGridCell[] grid;
    public PlacementGridCell[] coverageGrid;
    public int estimatedCount;
    public int estimatedSensorCount;
    public int placedCount;
    public float coveragePercent;
    public float averageMultiplicity;
    public float avgMultiplicity;
    public float avgCoverageMultiplicity;
}

[Serializable]
public class PlacementSensor
{
    public string id;
    public string sensorId;
    public string stationId;
    public string coordinateSystem;
    public bool useLatLon;
    public float latitude;
    public float longitude;
    public float lat;
    public float lon;
    public float x;
    public float y;
    public int multiplicity;
}

[Serializable]
public class PlacementGridCell
{
    public string coordinateSystem;
    public bool useLatLon;
    public float latitude;
    public float longitude;
    public float lat;
    public float lon;
    public float x;
    public float y;
    public int multiplicity;
}
