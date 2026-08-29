using System.IO;
using UnityEngine;
using UnityEditor;

public static class TestRenderWithoutTerrain
{
    public static void Test()
    {
        string p = "Assets/Prefabs/Circuits/RedBullRing_PBR.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
        GameObject inst = GameObject.Instantiate(prefab);

        // Check if Object_60 is covering the scene
        Transform obj60 = inst.transform.Find("root/GLTF_SceneRootNode/rbring_0/Object_60");
        if (obj60 != null)
        {
            UnityEngine.Debug.Log("Disabling Object_60 to check occlusion...");
            obj60.gameObject.SetActive(false);
        }

        Transform plane1 = inst.transform.Find("root/GLTF_SceneRootNode/Plane_1");
        if (plane1 != null) plane1.gameObject.SetActive(false);

        // Setup Camera
        GameObject camGo = new GameObject("RenderCam");
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
        cam.nearClipPlane = 0.5f;
        cam.farClipPlane = 10000.0f;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.65f, 0.68f, 0.72f, 1.0f);

        GameObject lightGo = new GameObject("SunLight");
        Light sun = lightGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.intensity = 1.3f;
        sun.color = Color.white;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // Position camera
        Vector3 lookTarget = new Vector3(26.7f, 51.8f, -26.3f);
        Vector3 camPos = lookTarget + new Vector3(15f, 25f, -20f);
        cam.transform.position = camPos;
        cam.transform.LookAt(lookTarget);
        cam.fieldOfView = 45f;

        int width = 1280;
        int height = 720;
        RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        tex.Apply();

        string artifactDir = "C:/Users/AKSHIT JAIN/.gemini/antigravity-ide/brain/50ef8d3f-4e21-401d-8ac1-c2b632068b8d";
        string outPath = Path.Combine(artifactDir, "redbull_no_obj60.png");
        File.WriteAllBytes(outPath, tex.EncodeToPNG());
        UnityEngine.Debug.Log($"Render without Object_60 saved to: {outPath}");

        cam.targetTexture = null;
        RenderTexture.active = null;
        GameObject.DestroyImmediate(rt);
        GameObject.DestroyImmediate(tex);
        GameObject.DestroyImmediate(camGo);
        GameObject.DestroyImmediate(lightGo);
        GameObject.DestroyImmediate(inst);
    }
}
