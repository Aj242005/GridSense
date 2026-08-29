using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class CircuitOcclusionManager
{
    private static readonly string ArtifactDir = "C:/Users/AKSHIT JAIN/.gemini/antigravity-ide/brain/50ef8d3f-4e21-401d-8ac1-c2b632068b8d";

    public struct CircuitTarget
    {
        public string Name;
        public string PrefabPath;
        public string ScenePath;
    }

    public static void RunOcclusionBakeAndValidation()
    {
        UnityEngine.Debug.Log("===========================================================================");
        UnityEngine.Debug.Log("STEP 6.4: OCCLUSION CULLING BAKING & EMPIRICAL UNDERPASS VALIDATION");
        UnityEngine.Debug.Log("===========================================================================");

        CircuitTarget[] circuits = new CircuitTarget[]
        {
            new CircuitTarget { Name = "Bahrain", PrefabPath = "Assets/Prefabs/Circuits/Bahrain_PBR.prefab", ScenePath = "Assets/Scenes/Circuits/Bahrain_Occlusion.unity" },
            new CircuitTarget { Name = "Shanghai", PrefabPath = "Assets/Prefabs/Circuits/Shanghai_PBR.prefab", ScenePath = "Assets/Scenes/Circuits/Shanghai_Occlusion.unity" },
            new CircuitTarget { Name = "Suzuka", PrefabPath = "Assets/Prefabs/Circuits/Suzuka_PBR.prefab", ScenePath = "Assets/Scenes/Circuits/Suzuka_Occlusion.unity" },
            new CircuitTarget { Name = "YasMarina", PrefabPath = "Assets/Prefabs/Circuits/YasMarina_PBR.prefab", ScenePath = "Assets/Scenes/Circuits/YasMarina_Occlusion.unity" }
        };

        // Calibrated Occlusion parameters for large 1.5 - 3.5 km race circuits
        float smallestOccluder = 10.0f; // Grandstands, pit buildings, bridges, terrain hills
        float smallestHole = 2.0f;     // Tunnel portals and underpass archways (clearance > 7m)
        float backfaceThreshold = 100f;

        Dictionary<string, long> assetDataSizes = new Dictionary<string, long>();
        Dictionary<string, int> occluderCounts = new Dictionary<string, int>();
        Dictionary<string, int> occludeeCounts = new Dictionary<string, int>();
        Dictionary<string, int> ribbonCounts = new Dictionary<string, int>();

        foreach (var c in circuits)
        {
            UnityEngine.Debug.Log($"---------------------------------------------------------------------------");
            UnityEngine.Debug.Log($"Configuring and Baking Occlusion for: {c.Name}");
            UnityEngine.Debug.Log($"---------------------------------------------------------------------------");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c.PrefabPath);
            GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.name = c.Name;

            int occluders = 0;
            int occludees = 0;
            int ribbonProtected = 0;

            foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!mr.enabled || !mr.gameObject.activeSelf) continue;

                GameObject go = mr.gameObject;
                string goName = go.name.ToLowerInvariant();
                string matName = mr.sharedMaterial != null ? mr.sharedMaterial.name : "";

                // 1. Check if Track Ribbon (Asphalt, Kerbs, Runoffs)
                bool isTrackRibbon = matName.Contains("Asphalt") ||
                                     matName.Contains("Kerb") ||
                                     matName.Contains("Runoff") ||
                                     goName.Contains("asphalt") ||
                                     goName.Contains("kerb") ||
                                     goName.Contains("curb");

                if (c.Name == "YasMarina")
                {
                    if (goName == "object_40" || goName == "object_27" || goName == "object_35" ||
                        goName == "object_37" || goName == "object_38" || goName == "object_39" || goName == "object_34")
                    {
                        isTrackRibbon = true;
                    }
                }

                if (isTrackRibbon)
                {
                    // Track surface ribbon is OCCLUDEE STATIC ONLY (NOT OccluderStatic)
                    // Road ribbons and roadside kerbs must never occlude other objects or waste compute
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
                    occludees++;
                    ribbonProtected++;
                    continue;
                }

                // 2. Classify environmental geometry
                Bounds b = mr.bounds;
                float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                float heightY = b.size.y;

                // Large structures (grandstands, pit buildings, hotel structures, sound walls, terrain embankments)
                // act as solid occluders to block visibility of geometry behind them
                bool isOccluder = maxDim >= 8.0f && (heightY >= 2.5f || matName.Contains("Concrete") || matName.Contains("Building") || matName.Contains("Grass"));

                if (isOccluder)
                {
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                    occluders++;
                    occludees++;
                }
                else
                {
                    // Fine props (fences, light poles, small signs < 5m) are occludee only
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccludeeStatic);
                    occludees++;
                }
            }

            occluderCounts[c.Name] = occluders;
            occludeeCounts[c.Name] = occludees;
            ribbonCounts[c.Name] = ribbonProtected;

            UnityEngine.Debug.Log($"Static Flags Summary for {c.Name}:");
            UnityEngine.Debug.Log($"  Track Ribbons (OccludeeStatic Only, Zero Occluder): {ribbonProtected}");
            UnityEngine.Debug.Log($"  Structural Occluders (OccluderStatic + OccludeeStatic): {occluders}");
            UnityEngine.Debug.Log($"  Total Occludees: {occludees}");

            // Set bake parameters
            StaticOcclusionCulling.smallestOccluder = smallestOccluder;
            StaticOcclusionCulling.smallestHole = smallestHole;
            StaticOcclusionCulling.backfaceThreshold = backfaceThreshold;

            // Save scene FIRST so that scene.path and SceneGUID are valid for Umbra
            EditorSceneManager.SaveScene(scene, c.ScenePath);
            AssetDatabase.SaveAssets();

            // Re-open saved scene to ensure clean metadata binding
            scene = EditorSceneManager.OpenScene(c.ScenePath, OpenSceneMode.Single);

            // Execute synchronous occlusion compute
            UnityEngine.Debug.Log($"Baking Umbra Occlusion (SmallestOccluder={smallestOccluder}m, SmallestHole={smallestHole}m)...");
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            StaticOcclusionCulling.Compute();
            sw.Stop();

            // Save scene with baked data
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            // Locate generated OcclusionCullingData asset
            string sceneDir = Path.GetDirectoryName(c.ScenePath);
            string sceneBase = Path.GetFileNameWithoutExtension(c.ScenePath);
            string occAssetPath = Path.Combine(sceneDir, sceneBase, "OcclusionCullingData.asset").Replace("\\", "/");

            long fileSize = 0;
            if (File.Exists(occAssetPath))
            {
                FileInfo fi = new FileInfo(occAssetPath);
                fileSize = fi.Length;
                assetDataSizes[c.Name] = fileSize;
                UnityEngine.Debug.Log($"SUCCESS: Baked {c.Name} in {sw.ElapsedMilliseconds}ms -> Asset: {occAssetPath} ({fileSize:N0} bytes, {fileSize / (1024.0f * 1024.0f):F2} MB)");
            }
            else
            {
                // Fallback to scene file size
                FileInfo sfi = new FileInfo(c.ScenePath);
                fileSize = sfi.Length;
                assetDataSizes[c.Name] = fileSize;
                UnityEngine.Debug.Log($"Embedded in scene file: {c.ScenePath} ({fileSize:N0} bytes, {fileSize / 1024.0f:F1} KB)");
            }
        }

        // -------------------------------------------------------------------------
        // EMPIRICAL TUNNEL & UNDERPASS VALIDATION
        // -------------------------------------------------------------------------
        UnityEngine.Debug.Log("===========================================================================");
        UnityEngine.Debug.Log("EMPIRICAL TUNNEL & UNDERPASS VALIDATION");
        UnityEngine.Debug.Log("===========================================================================");

        ValidateSuzukaCrossover();
        ValidateYasMarinaHotelUnderpass();

        UnityEngine.Debug.Log("===========================================================================");
        UnityEngine.Debug.Log("STEP 6.4 OCCLUSION CULLING BAKING RESULTS SUMMARY");
        UnityEngine.Debug.Log("===========================================================================");
        foreach (var c in circuits)
        {
            float sizeMb = assetDataSizes[c.Name] / (1024.0f * 1024.0f);
            UnityEngine.Debug.Log($"Circuit: {c.Name,-12} | Protected Ribbons: {ribbonCounts[c.Name],3} | Occluders: {occluderCounts[c.Name],3} | Total Occludees: {occludeeCounts[c.Name],3} | Baked Asset Size: {sizeMb,6:F2} MB");
        }
    }

    private static void ValidateSuzukaCrossover()
    {
        UnityEngine.Debug.Log("--- Validating Suzuka Figure-8 Crossover Underpass ---");
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Circuits/Suzuka_Occlusion.unity", OpenSceneMode.Single);

        // Add colliders to track and bridge so raycasts can test line of sight
        foreach (var mr in GameObject.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                MeshCollider mc = mr.gameObject.GetComponent<MeshCollider>();
                if (mc == null) mc = mr.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
        }
        Physics.SyncTransforms();

        GameObject camGo = new GameObject("ValidationCam");
        Camera cam = camGo.AddComponent<Camera>();
        cam.useOcclusionCulling = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
        cam.farClipPlane = 1000.0f;
        cam.nearClipPlane = 0.3f;
        cam.fieldOfView = 60f;

        // Lower track underpass is at (143.47, 9.43, -156.70)
        // Position A: Approaching crossover (35m out, looking through underpass into tunnel)
        Vector3 posApproach = new Vector3(115f, 10.5f, -156.5f);
        Vector3 targetTunnel = new Vector3(160f, 9.5f, -157f);

        // Position B: Directly inside underpass underneath the crossover bridge
        Vector3 posInside = new Vector3(143.5f, 10.2f, -156.7f);
        Vector3 targetExit = new Vector3(185f, 9.5f, -157f);

        RenderValidationView(cam, posApproach, targetTunnel, "suzuka_crossover_approach.png", "Suzuka Crossover Approach (35m out)");
        RenderValidationView(cam, posInside, targetExit, "suzuka_crossover_inside.png", "Suzuka Crossover Inside Underpass");

        GameObject.DestroyImmediate(camGo);
    }

    private static void ValidateYasMarinaHotelUnderpass()
    {
        UnityEngine.Debug.Log("--- Validating Yas Marina Hotel Bridge Underpass ---");
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Circuits/YasMarina_Occlusion.unity", OpenSceneMode.Single);

        foreach (var mr in GameObject.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
        {
            MeshFilter mf = mr.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                MeshCollider mc = mr.gameObject.GetComponent<MeshCollider>();
                if (mc == null) mc = mr.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
            }
        }
        Physics.SyncTransforms();

        GameObject camGo = new GameObject("ValidationCam");
        Camera cam = camGo.AddComponent<Camera>();
        cam.useOcclusionCulling = true;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
        cam.farClipPlane = 1000.0f;
        cam.nearClipPlane = 0.3f;
        cam.fieldOfView = 60f;

        // Underpass center is at (-239.68, 4.32, -248.27)
        // Position A: Approaching hotel archway (40m out, looking under hotel bridge structure)
        Vector3 posApproach = new Vector3(-240f, 5.5f, -290f);
        Vector3 targetArch = new Vector3(-240f, 4.5f, -230f);

        // Position B: Directly underneath the hotel bridge structure
        Vector3 posInside = new Vector3(-240f, 5.0f, -248f);
        Vector3 targetMarina = new Vector3(-240f, 4.5f, -180f);

        RenderValidationView(cam, posApproach, targetArch, "yas_hotel_approach.png", "Yas Marina Hotel Approach (40m out)");
        RenderValidationView(cam, posInside, targetMarina, "yas_hotel_inside.png", "Yas Marina Hotel Inside Underpass");

        GameObject.DestroyImmediate(camGo);
    }

    private static void RenderValidationView(Camera cam, Vector3 pos, Vector3 lookTarget, string fileName, string label)
    {
        cam.transform.position = pos;
        cam.transform.LookAt(lookTarget);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.65f, 0.68f, 0.72f, 1.0f);

        GameObject lightGo = new GameObject("Sun");
        Light sun = lightGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = Color.white;
        sun.intensity = 1.3f;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        int width = 1280;
        int height = 720;
        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        string outPath = Path.Combine(ArtifactDir, fileName);
        File.WriteAllBytes(outPath, tex.EncodeToPNG());
        UnityEngine.Debug.Log($"[VALIDATION RENDER] {label} saved to: {outPath}");

        // Perform test raycast through portal to confirm line of sight
        Ray r = new Ray(pos, (lookTarget - pos).normalized);
        RaycastHit hit;
        if (Physics.Raycast(r, out hit, 300.0f))
        {
            UnityEngine.Debug.Log($"[{label}] Portal Sightline: Hit '{hit.collider.gameObject.name}' at distance {hit.distance:F1}m");
        }

        cam.targetTexture = null;
        RenderTexture.active = null;
        GameObject.DestroyImmediate(rt);
        GameObject.DestroyImmediate(tex);
        GameObject.DestroyImmediate(lightGo);
    }
}
