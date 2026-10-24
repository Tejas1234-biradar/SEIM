using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

[DefaultExecutionOrder(-100)]
public class SensorPlacementClient : MonoBehaviour
{
    [SerializeField] private SensorPlacementConfig config;

    private MqttClient mqttClient;
    private string pendingMqttPayload;
    private Coroutine requestCoroutine;

    public SensorPlacementConfig Config => config;
    public SensorPlacementResponse CurrentPlacement { get; private set; }
    public event Action<SensorPlacementResponse> PlacementUpdated;

    public void Configure(SensorPlacementConfig placementConfig)
    {
        config = placementConfig;
    }

    private void Start()
    {
        if (config == null)
        {
            Debug.LogError("[SensorPlacementClient] Assign a SensorPlacementConfig before requesting placement.", this);
            return;
        }

        LoadSavedPlacement();
        if (config.transport == SensorPlacementTransport.Mqtt)
            ConnectAndSubscribeMqtt();
        else
            RequestPlacement();
    }

    private void Update()
    {
        string payload = Interlocked.Exchange(ref pendingMqttPayload, null);
        if (!string.IsNullOrEmpty(payload))
            ApplyPayload(payload, "MQTT");
    }

    public void RequestPlacement()
    {
        if (config == null)
        {
            Debug.LogError("[SensorPlacementClient] Cannot request placement without a SensorPlacementConfig.", this);
            return;
        }

        if (config.transport != SensorPlacementTransport.Http)
        {
            Debug.LogWarning("[SensorPlacementClient] The configured transport is MQTT; HTTP compute was not started.", this);
            return;
        }

        if (!config.HasValidCityInput(out string validationError))
        {
            Debug.LogError($"[SensorPlacementClient] {validationError}", this);
            UseSavedPlacement();
            return;
        }

        if (string.IsNullOrWhiteSpace(config.apiBaseUrl) || string.IsNullOrWhiteSpace(config.computePath))
        {
            Debug.LogError("[SensorPlacementClient] Configure both the API base URL and compute path.", this);
            UseSavedPlacement();
            return;
        }

        string url = config.apiBaseUrl.TrimEnd('/') + "/" + config.computePath.TrimStart('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri parsedUri) ||
            (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            Debug.LogError($"[SensorPlacementClient] Invalid HTTP placement URL '{url}'.", this);
            UseSavedPlacement();
            return;
        }

        if (requestCoroutine != null)
            StopCoroutine(requestCoroutine);

        requestCoroutine = StartCoroutine(PostPlacement());
    }

    private IEnumerator PostPlacement()
    {
        string url = config.apiBaseUrl.TrimEnd('/') + "/" + config.computePath.TrimStart('/');
        string body;
        try
        {
            body = config.CreateRequestJson();
        }
        catch (ArgumentException exception)
        {
            Debug.LogError($"[SensorPlacementClient] Could not construct the placement request: {exception.Message}", this);
            UseSavedPlacement();
            requestCoroutine = null;
            yield break;
        }
        using (UnityWebRequest request = UnityWebRequest.Post(url, body, "application/json"))
        {
            request.timeout = Mathf.Max(1, config.requestTimeoutSeconds);
            yield return request.SendWebRequest();
            requestCoroutine = null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                string status = request.responseCode > 0 ? $"HTTP {request.responseCode}" : "network/timeout error";
                Debug.LogWarning(
                    $"[SensorPlacementClient] Placement request failed ({status}): {request.error}.",
                    this);
                UseSavedPlacement();
                yield break;
            }

            ApplyPayload(request.downloadHandler.text, "HTTP");
        }
    }

    private void ConnectAndSubscribeMqtt()
    {
        if (!config.HasValidCityInput(out string validationError))
        {
            Debug.LogError($"[SensorPlacementClient] {validationError}", this);
            UseSavedPlacement();
            return;
        }

        try
        {
            mqttClient = new MqttClient(config.mqttHost, config.mqttPort, false, null, null, MqttSslProtocols.None);
            mqttClient.Settings.TimeoutOnConnection = Mathf.Max(1, config.mqttTimeoutMilliseconds);
            mqttClient.MqttMsgPublishReceived += OnMqttMessage;
            byte connectionResult = mqttClient.Connect("unity-placement-" + Guid.NewGuid().ToString("N"));
            if (!mqttClient.IsConnected)
            {
                Debug.LogWarning(
                    $"[SensorPlacementClient] MQTT connection returned code {connectionResult}; using saved placement if available.",
                    this);
                UseSavedPlacement();
                DisconnectMqtt();
                return;
            }

            mqttClient.Subscribe(new[] { config.mqttTopic }, new[] { MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE });
            Debug.Log($"[SensorPlacementClient] Subscribed to MQTT topic '{config.mqttTopic}'.", this);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                $"[SensorPlacementClient] Could not subscribe to placement MQTT topic '{config.mqttTopic}': {exception.Message}",
                this);
            UseSavedPlacement();
            DisconnectMqtt();
        }
    }

    private void OnMqttMessage(object sender, MqttMsgPublishEventArgs eventArgs)
    {
        Interlocked.Exchange(ref pendingMqttPayload, System.Text.Encoding.UTF8.GetString(eventArgs.Message));
    }

    private void ApplyPayload(string payload, string source)
    {
        SensorPlacementResponse response;
        try
        {
            response = JsonUtility.FromJson<SensorPlacementResponse>(payload);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[SensorPlacementClient] Could not parse {source} placement JSON: {exception.Message}", this);
            UseSavedPlacement();
            return;
        }

        if (!IsUsableResponse(response, out string error))
        {
            Debug.LogWarning($"[SensorPlacementClient] {source} placement response was rejected: {error}", this);
            UseSavedPlacement();
            return;
        }

        CurrentPlacement = response;
        SavePlacement(payload);
        PlacementUpdated?.Invoke(response);
        Debug.Log($"[SensorPlacementClient] Received {response.sensors.Length} sensor placements from {source}.", this);
    }

    private bool IsUsableResponse(SensorPlacementResponse response, out string error)
    {
        if (response == null || response.sensors == null || response.sensors.Length == 0)
        {
            error = "the response contained no sensors.";
            return false;
        }

        HashSet<string> stationIds = new HashSet<string>();
        for (int i = 0; i < response.sensors.Length; i++)
        {
            if (response.sensors[i] == null)
            {
                error = $"sensor entry at index {i} is null.";
                return false;
            }

            string stationId = SensorPlacementCoordinates.GetSensorId(response.sensors[i]);
            if (!string.IsNullOrWhiteSpace(stationId) && !stationIds.Add(stationId))
            {
                error = $"station ID '{stationId}' appears more than once.";
                return false;
            }

            try
            {
                SensorPlacementCoordinates.ToWorld(response.sensors[i], config);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is FormatException)
            {
                error = exception.Message;
                return false;
            }
        }

        error = null;
        return true;
    }

    private void LoadSavedPlacement()
    {
        string path = GetCachePath();
        if (!File.Exists(path))
            return;

        try
        {
            string json = File.ReadAllText(path);
            SensorPlacementResponse response = JsonUtility.FromJson<SensorPlacementResponse>(json);
            if (IsUsableResponse(response, out string error))
            {
                CurrentPlacement = response;
                PlacementUpdated?.Invoke(response);
                Debug.Log($"[SensorPlacementClient] Loaded {response.sensors.Length} sensors from the saved placement.", this);
            }
            else
            {
                Debug.LogWarning($"[SensorPlacementClient] Saved placement is not usable: {error}", this);
            }
        }
        catch (Exception exception) when (exception is IOException || exception is ArgumentException)
        {
            Debug.LogWarning($"[SensorPlacementClient] Could not read saved placement '{path}': {exception.Message}", this);
        }
    }

    private void SavePlacement(string json)
    {
        string path = GetCachePath();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
        }
        catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
        {
            Debug.LogError($"[SensorPlacementClient] Could not save placement fallback '{path}': {exception.Message}", this);
        }
    }

    private void UseSavedPlacement()
    {
        if (CurrentPlacement != null)
        {
            PlacementUpdated?.Invoke(CurrentPlacement);
            Debug.Log("[SensorPlacementClient] Reapplying the current saved placement.", this);
            return;
        }

        LoadSavedPlacement();
        if (CurrentPlacement == null)
            Debug.LogWarning("[SensorPlacementClient] No usable saved placement is available.", this);
    }

    private string GetCachePath()
    {
        string fileName = string.IsNullOrEmpty(config.name) ? "default" : config.name;
        foreach (char invalidCharacter in Path.GetInvalidFileNameChars())
            fileName = fileName.Replace(invalidCharacter, '_');
        return Path.Combine(Application.persistentDataPath, "sensor-placement-" + fileName + ".json");
    }

    private void DisconnectMqtt()
    {
        if (mqttClient == null)
            return;

        mqttClient.MqttMsgPublishReceived -= OnMqttMessage;
        if (mqttClient.IsConnected)
        {
            try
            {
                mqttClient.Disconnect();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[SensorPlacementClient] MQTT disconnect failed: {exception.Message}", this);
            }
        }
        mqttClient = null;
    }

    private void OnDestroy()
    {
        if (requestCoroutine != null)
            StopCoroutine(requestCoroutine);
        DisconnectMqtt();
    }
}
