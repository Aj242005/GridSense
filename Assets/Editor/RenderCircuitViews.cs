using System.IO;
using UnityEngine;
using UnityEditor;

public static class RenderCircuitViews
{
    public static void CaptureRendersAndInspect()
    {
        UnityEngine.Debug.Log("===========================================================================");
        UnityEngine.Debug.Log("CAPTURING VISUAL RENDERS & INSPECTING GEOMETRY RATIOS");
        UnityEngine.Debug.Log("===========================================================================");

        string artifactDir = "C:/Users/AKSHIT JAIN/.gemini/antigravity-ide/brain/50ef8d3f-4e21-401d-8ac1-c2b632068b8d";

        string[] circuitNames = new string[] { "Shanghai", "Suzuka", "Bahrain", "YasMarina" };

        // Ensure vibrant scene lighting
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.65f, 0.68f, 0.72f, 1.0f);

        foreach (var cName in circuitNames)
        {
            string prefabPath = $"Assets/Prefabs/Circuits/{cName}_PBR.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                UnityEngine.Debug.LogError($"Prefab not found at: {prefabPath}");
                continue;
            }

            GameObject instance = GameObject.Instantiate(prefab);
            instance.name = cName + "_RenderInstance";

            // Calculate total bounds (excluding distant background planes like Plane_1)
            Renderer[] rends = instance.GetComponentsInChildren<Renderer>();
            Bounds totalBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in rends)
            {
                if (r.gameObject.name.ToLowerInvariant().Contains("plane_1")) continue;
                if (first) { totalBounds = r.bounds; first = false; }
                else totalBounds.Encapsulate(r.bounds);
            }

            UnityEngine.Debug.Log($"[{cName}] Track Bounds: Center={totalBounds.center}, Size={totalBounds.size}");

            // Setup Camera
            GameObject camGo = new GameObject("RenderCam");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
            cam.nearClipPlane = 0.5f;
            cam.farClipPlane = 10000.0f;

            Vector3 camPos;
            Vector3 lookTarget;

            camPos = totalBounds.center + new Vector3(totalBounds.extents.x * 0.4f, totalBounds.extents.y * 1.8f + 120f, -totalBounds.extents.z * 0.45f);
            lookTarget = totalBounds.center + Vector3.up * (totalBounds.size.y * 0.1f);
            cam.fieldOfView = 55f;

            cam.transform.position = camPos;
            cam.transform.LookAt(lookTarget);

            // Add directional sun light
            GameObject lightGo = new GameObject("SunLight");
            Light sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.color = Color.white;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Render to texture
            int width = 1280;
            int height = 720;
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            byte[] bytes = tex.EncodeToPNG();
            string outPath = Path.Combine(artifactDir, $"{cName.ToLowerInvariant()}_current_render.png");
            File.WriteAllBytes(outPath, bytes);
            UnityEngine.Debug.Log($"[{cName}] Render screenshot saved to: {outPath}");

            // Clean up scene objects
            cam.targetTexture = null;
            RenderTexture.active = null;
            GameObject.DestroyImmediate(rt);
            GameObject.DestroyImmediate(tex);
            GameObject.DestroyImmediate(camGo);
            GameObject.DestroyImmediate(lightGo);
            GameObject.DestroyImmediate(instance);
        }

        UnityEngine.Debug.Log("===========================================================================");
        UnityEngine.Debug.Log("SCREENSHOT CAPTURE COMPLETE!");
        UnityEngine.Debug.Log("===========================================================================");
    }
}
