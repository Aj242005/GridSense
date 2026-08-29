using System;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;

public static class CalibrateShowcaseScene
{
    private static readonly string ArtifactDir = "C:/Users/AKSHIT JAIN/.gemini/antigravity-ide/brain/50ef8d3f-4e21-401d-8ac1-c2b632068b8d";

    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Circuits/Bahrain_Occlusion.unity", OpenSceneMode.Single);

        // 1. Directional Sun: High afternoon sun shining from rear-left over camera shoulder
        GameObject sunGo = GameObject.Find("Directional Light (Sun)");
        if (sunGo == null) sunGo = new GameObject("Directional Light (Sun)");
        Light sun = sunGo.GetComponent<Light>();
        if (sun == null) sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1.0f, 0.98f, 0.95f, 1.0f); // Warm natural daylight
        sun.intensity = 1.40f;
        sun.shadows = LightShadows.Soft;
        // Elevation 40 deg, Azimuth 150 deg (shining forward-right over camera shoulder)
        sunGo.transform.rotation = Quaternion.Euler(40.0f, 150.0f, 0.0f);
        EditorUtility.SetDirty(sun);

        // 2. Ambient Trilight: Bright, crisp fill lighting so shadows never crush to black
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.85f, 0.90f, 0.98f, 1.0f);     // Sky fill
        RenderSettings.ambientEquatorColor = new Color(0.80f, 0.80f, 0.80f, 1.0f); // Horizon fill
        RenderSettings.ambientGroundColor = new Color(0.55f, 0.55f, 0.55f, 1.0f);  // Ground bounce
        RenderSettings.ambientIntensity = 1.20f;

        // 3. Camera: Elevated 3/4 Chase Broadcast View
        // Shows the car's papaya body, sidepods, tyres, pit buildings, grandstand, and track surface
        GameObject camGo = GameObject.Find("Main Camera");
        if (camGo == null) camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        Camera cam = camGo.GetComponent<Camera>();
        if (cam == null) cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.45f, 0.65f, 0.88f, 1.0f); // Vibrant natural sky blue
        cam.fieldOfView = 56f;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 3500f;
        cam.useOcclusionCulling = true;

        var camData = camGo.GetComponent<UniversalAdditionalCameraData>();
        if (camData == null) camData = camGo.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = false; // Keep pure, true PBR material colors

        // Position: slightly left and behind the car, looking down and forward
        camGo.transform.position = new Vector3(-360.5f, 99.2f, -5.5f);
        camGo.transform.LookAt(new Vector3(-358.0f, 98.3f, 2.5f));
        EditorUtility.SetDirty(camGo);

        EditorSceneManager.SaveScene(scene);
        UnityEngine.Debug.Log("[SCENE SAVED] Calibrated sun and camera saved.");

        // 4. Render and Save
        int w = 1920;
        int h = 1080;
        RenderTexture rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        tex.Apply();

        string outPath = Path.Combine(ArtifactDir, "playable_scene_assembled.png");
        File.WriteAllBytes(outPath, tex.EncodeToPNG());
        UnityEngine.Debug.Log($"[VERIFIED RENDER SAVED] Output saved to: {outPath}");

        cam.targetTexture = null;
        RenderTexture.active = null;
        GameObject.DestroyImmediate(rt);
        GameObject.DestroyImmediate(tex);

        // 5. Rebuild Final Standalone Player
        BuildPlayerOptions opts = new BuildPlayerOptions
        {
            scenes = new string[]
            {
                "Assets/Scenes/Circuits/Bahrain_Occlusion.unity",
                "Assets/Scenes/Circuits/Shanghai_Occlusion.unity",
                "Assets/Scenes/Circuits/Suzuka_Occlusion.unity",
                "Assets/Scenes/Circuits/YasMarina_Occlusion.unity"
            },
            locationPathName = "Build/GridSense.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        if (report.summary.result == BuildResult.Succeeded)
        {
            UnityEngine.Debug.Log($"[REBUILD SUCCESS] Standalone build complete! Size: {report.summary.totalSize / (1024.0 * 1024.0):F2} MB");
        }
    }
}
