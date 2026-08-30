using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Collects every Station in the scene into one queryable network.
/// Attach to any single GameObject (e.g. "StationNetworkManager").
///
/// This does NOT store readings - it's just "what stations exist and where
/// are they." Readings live in SensorDataStore.
/// </summary>
public class StationNetwork : MonoBehaviour
{
    public static StationNetwork Instance { get; private set; }

    private Station[] allStations;

    private void Awake()
    {
        Instance = this;
        RefreshStationList();
    }

    /// <summary>Re-scan the scene for Station objects. Call if stations are added/removed at runtime.</summary>
    public void RefreshStationList()
    {
        allStations = FindObjectsByType<Station>(FindObjectsSortMode.None);
        Debug.Log($"[StationNetwork] Found {allStations.Length} stations.");
    }

    public Station[] GetAllStations()
    {
        if (allStations == null) RefreshStationList();
        return allStations;
    }

    public List<Station> GetStationsByType(StationType type)
    {
        List<Station> result = new List<Station>();
        foreach (var s in GetAllStations())
        {
            if (s.stationType == type) result.Add(s);
        }
        return result;
    }

    public Station GetStationById(string id)
    {
        foreach (var s in GetAllStations())
        {
            if (s.stationId == id) return s;
        }
        return null;
    }
}
