using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class RestoreCircuitTextures
{
    [MenuItem("GridSense/Restore Circuit Textures")]
    public static void Run()
    {
        Restore("Assets/Scenes/Circuits/Bahrain_Occlusion.unity", "Bahrain_Textured");
        Restore("Assets/Scenes/Circuits/Shanghai_Occlusion.unity", "Shanghai_Textured");
        Restore("Assets/Scenes/Circuits/Suzuka_Occlusion.unity", "Suzuka_Textured");
        Restore("Assets/Scenes/Circuits/YasMarina_Occlusion.unity", "YasMarina_Textured");
        AssetDatabase.SaveAssets();
    }

    private static void Restore(string scenePath, string materialFolder)
    {
        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        int restored = 0;
        foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/{materialFolder}/{renderer.name}_Mat.mat");
            if (material == null) continue;
            renderer.sharedMaterial = material;
            EditorUtility.SetDirty(renderer);
            restored++;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[GridSense] Restored {restored} texture-bound materials in {scene.name}.");
    }
}