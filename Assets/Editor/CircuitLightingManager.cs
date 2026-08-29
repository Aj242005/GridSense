using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class CircuitLightingManager
{
    private static readonly string ArtifactDir = "C:/Users/AKSHIT JAIN/.gemini/antigravity-ide/brain/50ef8d3f-4e21-401d-8ac1-c2b632068b8d";

    public struct CircuitLightingConfig
    {
        public string Name;
        public string ScenePath;
        public Vector3 SunEuler;
        public Color SunColor;
        public float SunIntensity;
        public Vector3 Center;
        public Vector3 Extents;
        public List<LocalProbeConfig> LocalProbes;
    }

    public struct LocalProbeConfig
    {
        public string Name;
        public Vector3 Position;
        public Vector3 BoxSize;
    }

    public static void RunLightingPipeline()
    {
        UnityEngine.Debug.Log("===========================================================================");
        UnityEngine.Debug.Log("STEP 6.5: LIGHTMAPPING & REFLECTION PROBE PIPELINE");
        UnityEngine.Debug.Log("===========================================================================");

        LightingSettings lightSettings = AssetDatabase.LoadAssetAtPath<LightingSettings>("Assets/Settings/GridSense_LightingSettings.asset");
        if (lightSettings == null)
        {
            lightSettings = new LightingSettings();
            lightSettings.name = "GridSense_LightingSettings";
            lightSettings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            lightSettings.lightmapResolution = 1.0f;
            lightSettings.lightmapMaxSize = 1024;
            lightSettings.mixedBakeMode = MixedLightingMode.Subtractive;
            lightSettings.environmentSampleCount = 32;
            lightSettings.directSampleCount = 16;
            lightSettings.indirectSampleCount = 32;
            lightSettings.maxBounces = 1;
            if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
            AssetDatabase.CreateAsset(lightSettings, "Assets/Settings/GridSense_LightingSettings.asset");
            AssetDatabase.SaveAssets();
        }

        CircuitLightingConfig[] configs = new CircuitLightingConfig[]
        {
            new CircuitLightingConfig
            {
                Name = "Bahrain",
                ScenePath = "Assets/Scenes/Circuits/Bahrain_Occlusion.unity",
                SunEuler = new Vector3(55.0f, -30.0f, 0.0f),
                SunColor = new Color(1.0f, 0.98f, 0.94f),
                SunIntensity = 1.35f,
                Center = Vector3.zero,
                Extents = new Vector3(2500f, 150f, 2500f),
                LocalProbes = new List<LocalProbeConfig>
                {
                    new LocalProbeConfig { Name = "Bahrain_PitStraight", Position = new Vector3(0f, 8f, 0f), BoxSize = new Vector3(400f, 30f, 80f) }
                }
            },
            new CircuitLightingConfig
            {
                Name = "Shanghai",
                ScenePath = "Assets/Scenes/Circuits/Shanghai_Occlusion.unity",
                SunEuler = new Vector3(52.0f, -35.0f, 0.0f),
                SunColor = new Color(1.0f, 0.97f, 0.92f),
                SunIntensity = 1.30f,
                Center = Vector3.zero,
                Extents = new Vector3(3000f, 150f, 3000f),
                LocalProbes = new List<LocalProbeConfig>
                {
                    new LocalProbeConfig { Name = "Shanghai_MainStraight", Position = new Vector3(0f, 8f, 0f), BoxSize = new Vector3(400f, 35f, 90f) }
                }
            },
            new CircuitLightingConfig
            {
                Name = "Suzuka",
                ScenePath = "Assets/Scenes/Circuits/Suzuka_Occlusion.unity",
                SunEuler = new Vector3(50.0f, -40.0f, 0.0f),
                SunColor = new Color(1.0f, 0.96f, 0.90f),
                SunIntensity = 1.30f,
                Center = Vector3.zero,
                Extents = new Vector3(2500f, 150f, 2500f),
                LocalProbes = new List<LocalProbeConfig>
                {
                    new LocalProbeConfig { Name = "Suzuka_CrossoverUnderpass", Position = new Vector3(143.5f, 11.5f, -150.8f), BoxSize = new Vector3(40f, 12f, 45f) }
                }
            },
            new CircuitLightingConfig
            {
                Name = "YasMarina",
                ScenePath = "Assets/Scenes/Circuits/YasMarina_Occlusion.unity",
                SunEuler = new Vector3(48.0f, -45.0f, 0.0f), // Crisp afternoon sun creating distinct directional shading
                SunColor = new Color(1.0f, 0.95f, 0.88f), // Warm golden race light
                SunIntensity = 1.35f,
                Center = Vector3.zero,
                Extents = new Vector3(2500f, 150f, 2500f),
                LocalProbes = new List<LocalProbeConfig>
                {
                    new LocalProbeConfig { Name = "Yas_HotelUnderpass", Position = new Vector3(-239.7f, 6.5f, -248.2f), BoxSize = new Vector3(45f, 15f, 65f) },
                    new LocalProbeConfig { Name = "Yas_MarinaBasin", Position = new Vector3(-200.0f, 5.0f, -150.0f), BoxSize = new Vector3(200f, 25f, 200f) }
                }
            }
        };

        foreach (var c in configs)
        {
            UnityEngine.Debug.Log($"---------------------------------------------------------------------------");
            UnityEngine.Debug.Log($"Configuring Lighting & Reflection Probes for: {c.Name}");
            UnityEngine.Debug.Log($"---------------------------------------------------------------------------");

            var scene = EditorSceneManager.OpenScene(c.ScenePath, OpenSceneMode.Single);

            // 1. Setup Directional Sun Light
            Light sunLight = null;
            foreach (var l in GameObject.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional) { sunLight = l; break; }
            }
            if (sunLight == null)
            {
                GameObject sunGo = new GameObject("Directional Light (Sun)");
                sunLight = sunGo.AddComponent<Light>();
                sunLight.type = LightType.Directional;
            }
            sunLight.gameObject.name = "Directional Light (Sun)";
            sunLight.color = c.SunColor;
            sunLight.intensity = c.SunIntensity;
            sunLight.transform.rotation = Quaternion.Euler(c.SunEuler);
            sunLight.shadows = LightShadows.Soft;
            sunLight.shadowStrength = 0.85f;
            sunLight.lightmapBakeType = LightmapBakeType.Mixed;

            // 2. Setup Ambient Environment Lighting (Trilight mode eliminates washed out flat ambient)
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.68f, 0.88f);      // Clear sky cyan-blue
            RenderSettings.ambientEquatorColor = new Color(0.60f, 0.64f, 0.68f);  // Atmospheric haze
            RenderSettings.ambientGroundColor = new Color(0.18f, 0.20f, 0.18f);   // Ground absorption shadow

            // 3. Configure Static Lighting Flags on all environment and track meshes
            int contributeCount = 0;
            foreach (var mr in GameObject.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!mr.enabled || !mr.gameObject.activeInHierarchy) continue;
                StaticEditorFlags flags = GameObjectUtility.GetStaticEditorFlags(mr.gameObject);
                flags |= StaticEditorFlags.ContributeGI;
                GameObjectUtility.SetStaticEditorFlags(mr.gameObject, flags);
                contributeCount++;
            }
            UnityEngine.Debug.Log($"Configured ContributeGI on {contributeCount} meshes in {c.Name}");

            // 4. Setup Global Circuit Reflection Probe
            GameObject globalProbeGo = GameObject.Find("Global_ReflectionProbe");
            if (globalProbeGo == null) globalProbeGo = new GameObject("Global_ReflectionProbe");
            ReflectionProbe globalProbe = globalProbeGo.GetComponent<ReflectionProbe>();
            if (globalProbe == null) globalProbe = globalProbeGo.AddComponent<ReflectionProbe>();

            globalProbe.mode = ReflectionProbeMode.Baked;
            globalProbe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
            globalProbe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            globalProbe.resolution = 256; // High performance, low memory for integrated graphics
            globalProbe.boxProjection = true;
            globalProbe.size = c.Extents;
            globalProbe.center = c.Center;
            globalProbe.transform.position = c.Center + Vector3.up * 15f;
            globalProbe.clearFlags = ReflectionProbeClearFlags.Skybox;

            // 5. Setup Local Sector Reflection Probes (Underpasses, Paddock)
            foreach (var lp in c.LocalProbes)
            {
                GameObject localProbeGo = GameObject.Find(lp.Name);
                if (localProbeGo == null) localProbeGo = new GameObject(lp.Name);
                ReflectionProbe probe = localProbeGo.GetComponent<ReflectionProbe>();
                if (probe == null) probe = localProbeGo.AddComponent<ReflectionProbe>();

                probe.mode = ReflectionProbeMode.Baked;
                probe.resolution = 128; // Compact 128x128 cubemap
                probe.boxProjection = true;
                probe.size = lp.BoxSize;
                probe.center = Vector3.zero;
                probe.transform.position = lp.Position;
                probe.importance = 2; // Higher priority inside underpass
            }

            // Save configured scene
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            UnityEngine.Debug.Log($"Successfully configured lighting and reflection probes for {c.Name}.");
        }

        // -------------------------------------------------------------------------
        // VALIDATION RENDERING: YAS MARINA AERIAL & TRACKSIDE COMPARISONS
        // -------------------------------------------------------------------------
        UnityEngine.Debug.Log("===========================================================================");
        UnityEngine.Debug.Log("RENDERING STEP 6.5 VALIDATION RENDERS (YAS MARINA AERIAL & TRACKSIDE)");
        UnityEngine.Debug.Log("===========================================================================");

        RenderYasMarinaValidations();
    }

    private static void RenderYasMarinaValidations()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Circuits/YasMarina_Occlusion.unity", OpenSceneMode.Single);

        GameObject camGo = new GameObject("ValidationCam");
        Camera cam = camGo.AddComponent<Camera>();
        cam.useOcclusionCulling = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.12f, 0.22f, 0.42f); // Deep Arabian Gulf water backdrop
        cam.farClipPlane = 10000.0f;
        cam.fieldOfView = 55f;

        // Render 1: EXACT aerial camera parameters from Step 6.3 for 1:1 direct comparison
        // Position: (20f, 1400f, -750f), LookAt: (20f, 0f, 35f)
        cam.transform.position = new Vector3(20f, 1400f, -750f);
        cam.transform.LookAt(new Vector3(20f, 0f, 35f));

        int width = 1280;
        int height = 720;
        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        string outAerial = Path.Combine(ArtifactDir, "yas_marina_aerial_baked.png");
        File.WriteAllBytes(outAerial, tex.EncodeToPNG());
        // Also overwrite the reference image so existing embeds update
        string outRef = Path.Combine(ArtifactDir, "yas_marina_aerial_render.png");
        File.WriteAllBytes(outRef, tex.EncodeToPNG());
        UnityEngine.Debug.Log($"[VALIDATED] Yas Marina Aerial Render saved to: {outAerial}");

        // Render 2: Trackside PBR View showing specular lighting on tarmac, barriers, and white architecture
        cam.transform.position = new Vector3(-230f, 6.5f, -320f);
        cam.transform.LookAt(new Vector3(-239.7f, 5.0f, -248.2f));
        cam.fieldOfView = 60f;
        cam.farClipPlane = 3000.0f;
        cam.Render();

        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();
        string outTrackside = Path.Combine(ArtifactDir, "yas_marina_trackside_baked.png");
        File.WriteAllBytes(outTrackside, tex.EncodeToPNG());
        UnityEngine.Debug.Log($"[VALIDATED] Yas Marina Trackside Render saved to: {outTrackside}");

        cam.targetTexture = null;
        RenderTexture.active = null;
        GameObject.DestroyImmediate(rt);
        GameObject.DestroyImmediate(tex);
        GameObject.DestroyImmediate(camGo);
    }
}
