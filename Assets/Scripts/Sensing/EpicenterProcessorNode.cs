using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The "brain" that sits above the dumb sensors. Runs one PWaveDetector per
/// station, watching each station's raw acceleration stream independently.
/// Once 3+ stations have detected their own P-wave arrival, triangulates
/// the epicenter from those detected times. Also tracks each station's
/// S-wave arrival once detected, and exposes the S-P interval per station
/// (a free, independent distance cross-check).
///
/// This is deliberately separate from Station/SensorDataStore - sensors
/// only ever produce raw acceleration; THIS class is where "sensing"
/// becomes "understanding," matching a real edge/decision architecture.
/// </summary>
public class EpicenterProcessorNode : MonoBehaviour
{
    public static EpicenterProcessorNode Instance { get; private set; }

    [Header("STA/LTA Detector Settings (applied to every station)")]
    public float shortWindowSeconds = 0.5f;
    public float longWindowSeconds = 5f;
    public float pTriggerThreshold = 4f;
    public float sTriggerThreshold = 8f;
    public float minGapAfterPSeconds = 0.3f;

    [Header("Trilateration")]
    public float pWaveSpeedKmS = 6f;
    [Tooltip("Minimum number of independently-triggered stations before attempting to locate an epicenter.")]
    public int minStationsToLocate = 3;

    private Dictionary<string, PWaveDetector> detectors = new();

    public Vector2? LastEstimatedEpicenter { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// Feed one raw seismic reading through this station's detector. Call
    /// this every time SensorDataStore records a new SeismicReading -
    /// this is the edge-processing step happening in real time.
    /// </summary>
    public void ProcessRawReading(SeismicReading reading, Vector3 stationWorldPosition)
    {
        if (!detectors.ContainsKey(reading.stationId))
        {
            detectors[reading.stationId] = new PWaveDetector(
                shortWindowSeconds, longWindowSeconds, pTriggerThreshold, sTriggerThreshold, minGapAfterPSeconds);
        }

        PhaseTrigger trigger = detectors[reading.stationId].AddSample(reading.timestamp, reading.Magnitude);

        if (trigger == PhaseTrigger.PWave)
        {
            Debug.Log($"[EpicenterProcessorNode] {reading.stationId} P-wave arrival detected at t={reading.timestamp:F3}s");
            TryLocateEpicenter();
        }
        else if (trigger == PhaseTrigger.SWave)
        {
            var d = detectors[reading.stationId];
            Debug.Log($"[EpicenterProcessorNode] {reading.stationId} S-wave arrival detected at t={reading.timestamp:F3}s " +
                      $"(S-P interval: {d.SPInterval:F3}s)");
        }
    }

    private void TryLocateEpicenter()
    {
        List<Trilateration.StationReading> triggered = new List<Trilateration.StationReading>();

        foreach (var kvp in detectors)
        {
            if (kvp.Value.HasTriggeredP)
            {
                Station station = StationNetwork.Instance.GetStationById(kvp.Key);
                if (station == null) continue;

                Vector2 pos2D = new Vector2(station.transform.position.x, station.transform.position.z);
                triggered.Add(new Trilateration.StationReading
                {
                    position = pos2D,
                    pWaveArrivalTime = kvp.Value.PWaveArrivalTime
                });
            }
        }

        if (triggered.Count >= minStationsToLocate)
        {
            Vector2 estimated = Trilateration.EstimateEpicenter(triggered.ToArray(), pWaveSpeedKmS);
            LastEstimatedEpicenter = estimated;
            Debug.Log($"[EpicenterProcessorNode] Located epicenter from {triggered.Count} stations: {estimated}");
        }
    }

    /// <summary>Get a station's detected S-wave arrival time, if found yet. Returns -1 if not yet detected.</summary>
    public float GetSWaveArrivalTime(string stationId)
    {
        return detectors.TryGetValue(stationId, out var d) && d.HasTriggeredS ? d.SWaveArrivalTime : -1f;
    }

    /// <summary>Get a station's S-P interval, if both phases have been detected. Returns -1 otherwise.</summary>
    public float GetSPInterval(string stationId)
    {
        return detectors.TryGetValue(stationId, out var d) ? d.SPInterval : -1f;
    }

    /// <summary>Reset all detectors - call when starting a new quake event/test.</summary>
    public void ResetAllDetectors()
    {
        foreach (var d in detectors.Values) d.Reset();
        LastEstimatedEpicenter = null;
    }
}