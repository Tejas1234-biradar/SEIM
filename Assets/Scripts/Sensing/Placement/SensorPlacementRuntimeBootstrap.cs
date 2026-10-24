using UnityEngine;

public static class SensorPlacementRuntimeBootstrap
{
    private static bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetState()
    {
        initialized = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreatePlacementServices()
    {
        if (initialized)
            return;
        initialized = true;

        SensorPlacementConfig config = Resources.Load<SensorPlacementConfig>("SensorPlacementConfig");
        GameObject prefab = Resources.Load<GameObject>("VirtualSensor");
        if (config == null || prefab == null)
        {
            Debug.LogError(
                "[SensorPlacementRuntimeBootstrap] Resources/SensorPlacementConfig.asset and Resources/VirtualSensor.prefab are required.");
            return;
        }

        GameObject services = new GameObject("SensorPlacementServices");
        Object.DontDestroyOnLoad(services);

        SensorPlacementClient client = services.AddComponent<SensorPlacementClient>();
        client.Configure(config);

        SensorSpawner spawner = services.AddComponent<SensorSpawner>();
        spawner.Configure(client, config, prefab);

        SensorPlacementVisualization visualization = services.AddComponent<SensorPlacementVisualization>();
        visualization.Configure(spawner, config);

        SensorPlacementValidation validation = services.AddComponent<SensorPlacementValidation>();
        validation.Configure(spawner);
    }
}
