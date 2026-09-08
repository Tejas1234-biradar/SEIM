using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.Threading;

public static class DumbSensorTestRunner
{
    [MenuItem("SEIM/Run Dumb Sensor Test")]
    public static void RunFromMenu()
    {
        ExecutePipelineTest(false);
    }

    public static void RunDumbSensorTestHeadless()
    {
        ExecutePipelineTest(true);
    }

    private static void ExecutePipelineTest(bool isBatchmode)
    {
        Debug.Log("[DumbSensorTestRunner] Opening test scene: Assets/Scenes/test.unity");
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/test.unity", OpenSceneMode.Single);

        var dumbTest = Object.FindFirstObjectByType<DumbSensorPipelineTest>();
        var dataStore = Object.FindFirstObjectByType<SensorDataStore>();
        var publisher = Object.FindFirstObjectByType<MqttPublisher>();
        var stationNetwork = Object.FindFirstObjectByType<StationNetwork>();
        var epicenterProcessor = Object.FindFirstObjectByType<EpicenterProcessorNode>();

        if (dumbTest == null || dataStore == null || publisher == null || stationNetwork == null || epicenterProcessor == null)
        {
            Debug.LogError($"[DumbSensorTestRunner] Missing required scene components! " +
                           $"dumbTest={dumbTest!=null}, dataStore={dataStore!=null}, publisher={publisher!=null}, " +
                           $"stationNetwork={stationNetwork!=null}, epicenterProcessor={epicenterProcessor!=null}");
            if (isBatchmode) EditorApplication.Exit(1);
            return;
        }

        var awakeMethodDataStore = typeof(SensorDataStore).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        awakeMethodDataStore?.Invoke(dataStore, null);

        var awakeMethodPublisher = typeof(MqttPublisher).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        awakeMethodPublisher?.Invoke(publisher, null);

        var awakeMethodStation = typeof(StationNetwork).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        awakeMethodStation?.Invoke(stationNetwork, null);

        var awakeMethodProcessor = typeof(EpicenterProcessorNode).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        awakeMethodProcessor?.Invoke(epicenterProcessor, null);

        Debug.Log($"[DumbSensorTestRunner] Connecting MqttPublisher to {publisher.brokerHost}:{publisher.brokerPort}...");
        bool connected = publisher.TryConnect();
        Debug.Log($"[DumbSensorTestRunner] MqttPublisher connected: {connected}");

        Debug.Log("[DumbSensorTestRunner] Running DumbSensorPipelineTest.RunTest()...");
        dumbTest.RunTest();
        Debug.Log("[DumbSensorTestRunner] RunTest() completed successfully.");

        Thread.Sleep(1000);

        if (isBatchmode)
        {
            EditorApplication.Exit(0);
        }
    }
}

