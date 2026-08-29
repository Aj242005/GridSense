using System.IO;
using UnityEngine;
using UnityEditor;

public static class RenderTracksideViews
{
    public static void CaptureTrackside()
    {
        string artifactDir = "C:/Users/AKSHIT JAIN/.gemini/antigravity-ide/brain/50ef8d3f-4e21-401d-8ac1-c2b632068b8d";

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.65f, 0.68f, 0.72f, 1.0f);

        // 1. Red Bull Ring Start/Finish straight view
        {
            string prefabPath = "Assets/Prefabs/Circuits/RedBullRing_PBR.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            GameObject inst = GameObject.Instantiate(prefab);

            GameObject camGo = new GameObject("RenderCam");
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.55f, 0.75f, 0.95f);
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 5000.0f;
            cam.fieldOfView = 60f;

            GameObject lightGo = new GameObject("SunLight");
            Light sun = lightGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.3f;
            sun.color = Color.white;
            lightGo.transform.rotation = Quaternion.Euler(45f, -45f, 0f);

            // Start/Finish straight position from TrackMetadata
            Vector3 sfPos = new Vector3(22.81f, 54.00f, 1.67f);
            Vector3 fwd = new Vector3(0.98f, 0f, 0.20f).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, fwd).normalized;

            // Camera placed 10m behind start/finish, 3.5m above track surface, looking down straight
            cam.transform.position = sfPos - fwd * 15.0f + Vector3.up * 3.5f - right * 2.0f;
            cam.transform.LookAt(sfPos + fwd * 40.0f + Vector3.up * 1.5f);

            int width = 1280;
            int height = 720;
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            string outPath = Path.Combine(artifactDir, "redbullring_trackside_render.png");
            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            UnityEngine.Debug.Log($"Red Bull Ring trackside render saved to: {outPath}");

            cam.targetTexture = null;
            RenderTexture.active = null;
            GameObject.DestroyImmediate(rt);
            GameObject.DestroyImmediate(tex);
            GameObject.DestroyImmediate(camGo);
            GameObject.DestroyImmediate(lightGo);
            GameObject.DestroyImmediate(inst);
        }
    }
}
