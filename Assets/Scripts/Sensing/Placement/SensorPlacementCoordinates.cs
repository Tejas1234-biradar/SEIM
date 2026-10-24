using UnityEngine;

public static class SensorPlacementCoordinates
{
    private const float EarthRadiusMeters = 6371000f;

    public static bool TryToWorld(
        float x,
        float y,
        float latitude,
        float longitude,
        bool useLatLon,
        string coordinateSystem,
        SensorPlacementConfig config,
        out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        if (config == null)
            return false;
        if (!IsFinite(x) || !IsFinite(y) || !IsFinite(latitude) || !IsFinite(longitude) ||
            config.worldUnitsPerMeter <= 0f || !IsFinite(config.worldUnitsPerMeter))
            return false;

        bool geographic = useLatLon ||
                          string.Equals(coordinateSystem, "latlon", System.StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(coordinateSystem, "geographic", System.StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(coordinateSystem, "degrees", System.StringComparison.OrdinalIgnoreCase);
        if (geographic)
        {
            if (latitude < -90f || latitude > 90f || longitude < -180f || longitude > 180f ||
                config.originLatitude < -90f || config.originLatitude > 90f)
                return false;

            float northMeters = Mathf.Deg2Rad * (latitude - config.originLatitude) * EarthRadiusMeters;
            float eastMeters = Mathf.Deg2Rad * (longitude - config.originLongitude) *
                               EarthRadiusMeters * Mathf.Cos(config.originLatitude * Mathf.Deg2Rad);
            worldPosition = config.originWorldPosition + new Vector3(
                eastMeters * config.worldUnitsPerMeter,
                0f,
                northMeters * config.worldUnitsPerMeter);
        }
        else if (string.Equals(coordinateSystem, "world", System.StringComparison.OrdinalIgnoreCase) ||
                 config.xyCoordinates == SensorPlacementXYCoordinates.WorldUnits)
        {
            worldPosition = new Vector3(x, config.originWorldPosition.y, y);
        }
        else
        {
            worldPosition = config.originWorldPosition + new Vector3(
                x * config.worldUnitsPerMeter,
                0f,
                y * config.worldUnitsPerMeter);
        }

        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null && terrain.terrainData != null)
        {
            Vector3 terrainOrigin = terrain.transform.position;
            Vector3 terrainSize = terrain.terrainData.size;
            float localX = worldPosition.x - terrainOrigin.x;
            float localZ = worldPosition.z - terrainOrigin.z;
            if (localX >= 0f && localZ >= 0f && localX <= terrainSize.x && localZ <= terrainSize.z)
            {
                worldPosition.y = terrain.SampleHeight(worldPosition) + terrainOrigin.y;
            }
            else
            {
                worldPosition.y = config.originWorldPosition.y;
            }
        }
        else
        {
            worldPosition.y = config.originWorldPosition.y;
        }

        return IsFinite(worldPosition.x) && IsFinite(worldPosition.y) && IsFinite(worldPosition.z);
    }

    public static Vector3 ToWorld(PlacementSensor sensor, SensorPlacementConfig config)
    {
        if (sensor == null)
            throw new System.ArgumentNullException(nameof(sensor));
        if (!HasCoordinates(sensor.x, sensor.y, sensor.latitude, sensor.longitude, sensor.lat, sensor.lon,
                sensor.useLatLon, sensor.coordinateSystem))
        {
            throw new System.FormatException($"Sensor '{GetSensorId(sensor)}' does not include coordinates.");
        }

        float latitude = sensor.latitude != 0f || sensor.longitude != 0f ? sensor.latitude : sensor.lat;
        float longitude = sensor.latitude != 0f || sensor.longitude != 0f ? sensor.longitude : sensor.lon;
        bool useLatLon = sensor.useLatLon || sensor.latitude != 0f || sensor.longitude != 0f ||
                         sensor.lat != 0f || sensor.lon != 0f;

        if (!TryToWorld(sensor.x, sensor.y, latitude, longitude, useLatLon,
                sensor.coordinateSystem, config, out Vector3 worldPosition))
        {
            throw new System.FormatException($"Sensor '{GetSensorId(sensor)}' has invalid coordinates.");
        }

        return worldPosition;
    }

    public static Vector3 ToWorld(PlacementGridCell cell, SensorPlacementConfig config)
    {
        if (cell == null)
            throw new System.ArgumentNullException(nameof(cell));
        if (!HasCoordinates(cell.x, cell.y, cell.latitude, cell.longitude, cell.lat, cell.lon,
                cell.useLatLon, cell.coordinateSystem))
        {
            throw new System.FormatException("A coverage grid cell does not include coordinates.");
        }

        float latitude = cell.latitude != 0f || cell.longitude != 0f ? cell.latitude : cell.lat;
        float longitude = cell.latitude != 0f || cell.longitude != 0f ? cell.longitude : cell.lon;
        bool useLatLon = cell.useLatLon || cell.latitude != 0f || cell.longitude != 0f ||
                         cell.lat != 0f || cell.lon != 0f;

        if (!TryToWorld(cell.x, cell.y, latitude, longitude, useLatLon,
                cell.coordinateSystem, config, out Vector3 worldPosition))
        {
            throw new System.FormatException("A coverage grid cell has invalid coordinates.");
        }

        return worldPosition;
    }

    public static string GetSensorId(PlacementSensor sensor)
    {
        if (!string.IsNullOrEmpty(sensor.stationId)) return sensor.stationId;
        if (!string.IsNullOrEmpty(sensor.sensorId)) return sensor.sensorId;
        return sensor.id;
    }

    public static float RadiusWorldUnits(SensorPlacementConfig config)
    {
        return config.sensorRadiusKm * 1000f * config.worldUnitsPerMeter;
    }

    private static bool IsFinite(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool HasCoordinates(
        float x,
        float y,
        float latitude,
        float longitude,
        float lat,
        float lon,
        bool useLatLon,
        string coordinateSystem)
    {
        return useLatLon || !string.IsNullOrEmpty(coordinateSystem) ||
               latitude != 0f || longitude != 0f || lat != 0f || lon != 0f ||
               x != 0f || y != 0f;
    }
}
