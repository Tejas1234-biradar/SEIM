using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// In-memory store for the latest reading (and short history) per station.
/// Layer 2 writes into this; Layer 3's dashboard reads out of it. No
/// networking involved - everything happens in one Unity process.
///
/// Attach to a single persistent GameObject (e.g. same object as
/// StationNetwork or EarthquakeManager).
/// </summary>
public class SensorDataStore : MonoBehaviour
{
    public static SensorDataStore Instance { get; private set; }

    private const int MAX_HISTORY = 200;

    [Header("JSON Export (for later use as training/replay data)")]
    [Tooltip("When enabled, every recorded reading is also appended as a JSON line to a file on disk.")]
    public bool exportToJsonFile = false;
    [Tooltip("File name only - saved under the project root /data folder.")]
    public string exportFileName = "sensor_log.jsonl";
    [Tooltip("If true, clears any existing file with this name when Play starts. If false, new readings are appended to whatever's already there.")]
    public bool clearFileOnStart = true;

    private string ExportDirectoryPath
    {
        get
        {
            string projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
            string exportDirectory = System.IO.Path.Combine(projectRoot, "data");
            System.IO.Directory.CreateDirectory(exportDirectory);
            return exportDirectory;
        }
    }

    private string ExportFilePath => System.IO.Path.Combine(ExportDirectoryPath, exportFileName);

    private Dictionary<string, SeismicReading> latestSeismic = new();
    private Dictionary<string, List<SeismicReading>> seismicHistory = new();

    private Dictionary<string, DartBuoyReading> latestDart = new();
    private Dictionary<string, TideGaugeReading> latestTide = new();
    private Dictionary<string, GnssReading> latestGnss = new();
    private Dictionary<string, OceanBottomReading> latestOceanBottom = new();

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (exportToJsonFile)
        {
            if (clearFileOnStart && System.IO.File.Exists(ExportFilePath))
            {
                System.IO.File.Delete(ExportFilePath);
            }
            Debug.Log($"[SensorDataStore] JSON export enabled. Writing to: {ExportFilePath}");
        }
    }

    /// <summary>
    /// Appends one JSON line to the export file, tagged with a "readingType"
    /// so a single file can hold multiple sensor types and still be parsed
    /// unambiguously later (e.g. for Layer 3 training data generation).
    /// </summary>
    private void AppendJsonLine(string readingType, object reading)
    {
        if (!exportToJsonFile) return;

        // Wrap in a small envelope so downstream parsing knows which struct
        // this line deserializes into, without guessing from field names.
        string innerJson = JsonUtility.ToJson(reading);
        string line = $"{{\"readingType\":\"{readingType}\",\"data\":{innerJson}}}";

        System.IO.File.AppendAllText(ExportFilePath, line + "\n");
    }

    // ---------- Seismic ----------
    public void RecordSeismicReading(SeismicReading reading)
    {
        latestSeismic[reading.stationId] = reading;

        if (!seismicHistory.ContainsKey(reading.stationId))
            seismicHistory[reading.stationId] = new List<SeismicReading>();

        var list = seismicHistory[reading.stationId];
        list.Add(reading);
        if (list.Count > MAX_HISTORY) list.RemoveAt(0);

        AppendJsonLine("Seismic", reading);
    }

    public bool TryGetLatestSeismic(string stationId, out SeismicReading reading)
    {
        return latestSeismic.TryGetValue(stationId, out reading);
    }

    public Dictionary<string, SeismicReading> GetAllLatestSeismic() => latestSeismic;

    public List<SeismicReading> GetSeismicHistory(string stationId)
    {
        return seismicHistory.TryGetValue(stationId, out var list) ? list : new List<SeismicReading>();
    }

    // ---------- DART buoy ----------
    public void RecordDartReading(DartBuoyReading reading)
    {
        latestDart[reading.stationId] = reading;
        AppendJsonLine("DartBuoy", reading);
    }

    public Dictionary<string, DartBuoyReading> GetAllLatestDart() => latestDart;

    // ---------- Tide gauge ----------
    public void RecordTideReading(TideGaugeReading reading)
    {
        latestTide[reading.stationId] = reading;
        AppendJsonLine("TideGauge", reading);
    }

    public Dictionary<string, TideGaugeReading> GetAllLatestTide() => latestTide;

    // ---------- GNSS ----------
    public void RecordGnssReading(GnssReading reading)
    {
        latestGnss[reading.stationId] = reading;
        AppendJsonLine("Gnss", reading);
    }

    public Dictionary<string, GnssReading> GetAllLatestGnss() => latestGnss;

    // ---------- Ocean-bottom combo ----------
    public void RecordOceanBottomReading(OceanBottomReading reading)
    {
        latestOceanBottom[reading.stationId] = reading;
        AppendJsonLine("OceanBottom", reading);
        // Also file its embedded seismic component under the seismic store,
        // since ocean-bottom stations produce a seismic signal too.
        RecordSeismicReading(reading.seismic);
    }

    public Dictionary<string, OceanBottomReading> GetAllLatestOceanBottom() => latestOceanBottom;

    /// <summary>Clears all stored readings - useful when resetting between test runs.</summary>
    public void ClearAll()
    {
        latestSeismic.Clear();
        seismicHistory.Clear();
        latestDart.Clear();
        latestTide.Clear();
        latestGnss.Clear();
        latestOceanBottom.Clear();
    }
}