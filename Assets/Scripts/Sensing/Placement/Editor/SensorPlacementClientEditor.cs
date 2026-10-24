using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SensorPlacementClient))]
public class SensorPlacementClientEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("Compute Sensor Placement"))
                ((SensorPlacementClient)target).RequestPlacement();
        }

        if (!Application.isPlaying)
            EditorGUILayout.HelpBox("Enter Play mode to send the configured placement request.", MessageType.Info);
    }
}
