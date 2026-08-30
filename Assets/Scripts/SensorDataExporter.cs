using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Collects the current acceleration reading from every BuildingShaker in the
/// scene at a fixed interval, packages it as JSON, and sends it out — either
/// to a local backend over HTTP, or to a file on disk for offline testing.
///
/// This is the bridge between your Unity earthquake simulation and everything
/// downstream (virtual sensor layer / AI / dashboard). Attach to the same
/// GameObject as EarthquakeSimulator, or any object in the scene - just one
/// instance needed.
/// </summary>
public class SensorDataExporter : MonoBehaviour
{
    [Header("Export Settings")]
    [Tooltip("How often to export a reading, in seconds. 0.1 = 10 readings/sec.")]
    public float exportIntervalSeconds = 0.2f;

    [Header("HTTP Export (to a backend)")]
    public bool sendToBackend = true;
    [Tooltip("Your backend's endpoint. Use the included receiver.py for local testing.")]
    public string backendUrl = "http://localhost:5000/sensor-data";

    [Header("File Export (for offline testing without a backend)")]
    public bool writeToFile = false;
    [Tooltip("Relative to the project's persistent data path")]
    public string fileName = "sensor_export_log.jsonl";

    [Header("Debug")]
    public bool logToConsole = true;

    private float timer;
    private BuildingShaker[] cachedBuildings;
    private float rescanTimer;
    private const float RESCAN_INTERVAL = 2f; // re-find buildings every 2s in case any spawn/despawn

    void Start()
    {
        RescanBuildings();
    }

    void Update()
    {
        rescanTimer += Time.deltaTime;
        if (rescanTimer >= RESCAN_INTERVAL)
        {
            RescanBuildings();
            rescanTimer = 0f;
        }

        timer += Time.deltaTime;
        if (timer >= exportIntervalSeconds)
        {
            timer = 0f;
            ExportReading();
        }
    }

    private void RescanBuildings()
    {
        cachedBuildings = FindObjectsByType<BuildingShaker>(FindObjectsSortMode.None);
    }

    private void ExportReading()
    {
        if (cachedBuildings == null || cachedBuildings.Length == 0) return;

        SensorPayload payload = new SensorPayload
        {
            timestamp = Time.time,
            readings = new List<SensorReading>()
        };

        foreach (var building in cachedBuildings)
        {
            payload.readings.Add(new SensorReading
            {
                zone_id = building.zoneId,
                acceleration_g = building.GetCurrentAcceleration(),
                is_damaged = building.IsDamaged()
            });
        }

        string json = JsonUtility.ToJson(payload);

        if (logToConsole)
        {
            Debug.Log($"[SensorDataExporter] {json}");
        }

        if (sendToBackend)
        {
            StartCoroutine(PostJson(json));
        }

        if (writeToFile)
        {
            AppendToFile(json);
        }
    }

    private IEnumerator PostJson(string json)
    {
        UnityWebRequest req = new UnityWebRequest(backendUrl, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(bodyRaw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            // Don't spam errors every 0.2s if the backend isn't running -
            // this is expected during Unity-only testing.
            if (logToConsole)
            {
                Debug.LogWarning($"[SensorDataExporter] POST failed (is the backend running?): {req.error}");
            }
        }
    }

    private void AppendToFile(string json)
    {
        string path = System.IO.Path.Combine(Application.persistentDataPath, fileName);
        System.IO.File.AppendAllText(path, json + "\n");
    }
}

[System.Serializable]
public class SensorReading
{
    public string zone_id;
    public float acceleration_g;
    public bool is_damaged;
}

[System.Serializable]
public class SensorPayload
{
    public float timestamp;
    public List<SensorReading> readings;
}
