using System;
using System.Text;
using UnityEngine;
using uPLibrary.Networking.M2Mqtt;
using uPLibrary.Networking.M2Mqtt.Messages;

/// <summary>
/// Singleton MQTT publisher for transmitting raw sensor readings from Unity to the
/// disaster-backend (Mosquitto -> Kafka -> PostgreSQL).
///
/// Attach to a single persistent GameObject (e.g. same object as SensorDataStore).
/// </summary>
public class MqttPublisher : MonoBehaviour
{
    public static MqttPublisher Instance { get; private set; }

    [Header("Broker Configuration")]
    [Tooltip("Hostname or IP address of the MQTT broker.")]
    public string brokerHost = "localhost";

    [Tooltip("Port of the MQTT broker (host-mapped port 11883 in disaster-backend docker-compose).")]
    public int brokerPort = 11883;

    [Header("Connection Settings")]
    [Tooltip("Timeout for broker connection in milliseconds.")]
    public int timeoutMs = 3000;

    private MqttClient client;
    private string clientId;

    public bool IsConnected => client != null && client.IsConnected;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (string.IsNullOrEmpty(clientId))
        {
            clientId = "unity-" + Guid.NewGuid().ToString("N");
        }

        TryConnect();
    }

    /// <summary>
    /// Attempts to connect to the MQTT broker. Logs warnings on failure without throwing,
    /// ensuring simulation resilience even if the backend is down.
    /// </summary>
    public bool TryConnect()
    {
        try
        {
            if (client != null && client.IsConnected)
            {
                return true;
            }

            Disconnect();

            if (string.IsNullOrEmpty(clientId))
            {
                clientId = "unity-" + Guid.NewGuid().ToString("N");
            }

            client = new MqttClient(brokerHost, brokerPort, false, null, null, MqttSslProtocols.None);
            client.Settings.TimeoutOnConnection = timeoutMs;

            byte returnCode = client.Connect(clientId);
            if (client.IsConnected)
            {
                Debug.Log($"[MqttPublisher] Connected to MQTT broker at {brokerHost}:{brokerPort} (client: {clientId}).");
                return true;
            }
            else
            {
                Debug.LogWarning($"[MqttPublisher] Connection returned code {returnCode} (not connected) for broker {brokerHost}:{brokerPort}.");
                return false;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MqttPublisher] Could not connect to MQTT broker at {brokerHost}:{brokerPort}: {ex.Message}");
            client = null;
            return false;
        }
    }

    /// <summary>
    /// Publishes a raw JSON payload for the specified reading type to topic:
    ///   sensors/{readingType}/readings
    /// using QoS 0 (at-most-once).
    /// </summary>
    public void Publish(string readingType, string jsonPayload)
    {
        if (string.IsNullOrEmpty(jsonPayload))
        {
            return;
        }

        string topic = $"sensors/{readingType}/readings";
        byte[] payloadBytes = Encoding.UTF8.GetBytes(jsonPayload);

        // Basic reconnect logic: if disconnected, attempt one reconnect
        if (client == null || !client.IsConnected)
        {
            Debug.LogWarning($"[MqttPublisher] Not connected. Attempting reconnection before publishing to '{topic}'...");
            if (!TryConnect())
            {
                Debug.LogWarning($"[MqttPublisher] Reconnect failed. Dropped reading on topic '{topic}'.");
                return;
            }
        }

        try
        {
            client.Publish(topic, payloadBytes, MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE, false);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MqttPublisher] Publish failed on '{topic}': {ex.Message}. Attempting one reconnect and retry...");
            if (TryConnect())
            {
                try
                {
                    client.Publish(topic, payloadBytes, MqttMsgBase.QOS_LEVEL_AT_MOST_ONCE, false);
                }
                catch (Exception retryEx)
                {
                    Debug.LogWarning($"[MqttPublisher] Retry publish failed on '{topic}': {retryEx.Message}. Dropped reading.");
                }
            }
            else
            {
                Debug.LogWarning($"[MqttPublisher] Reconnect attempt failed. Dropped reading on topic '{topic}'.");
            }
        }
    }

    private void Disconnect()
    {
        if (client != null)
        {
            try
            {
                if (client.IsConnected)
                {
                    client.Disconnect();
                }
            }
            catch { }
            finally
            {
                client = null;
            }
        }
    }

    private void OnDestroy()
    {
        Disconnect();
    }

    private void OnApplicationQuit()
    {
        Disconnect();
    }
}

