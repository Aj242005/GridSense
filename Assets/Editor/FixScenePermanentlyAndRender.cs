using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;

public static class FixScenePermanentlyAndRender
{
    private static readonly string ArtifactDir = "C:/Users/AKSHIT JAIN/.gemini/antigravity-ide/brain/50ef8d3f-4e21-401d-8ac1-c2b632068b8d";

    public static void Run()
    {
        UnityEngine.Debug.Log("=== DEFINITIVE SCENE FIX AND RENDER ===");

        // 1. Load PBR Materials
        Material matAsphalt = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Track/M_Track_Asphalt.mat");
        Material matConcrete = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Track/M_Track_Concrete.mat");
        Material matBarrier = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Track/M_Track_Barrier_Metal.mat");
        Material matGrass = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Track/M_Track_Grass.mat");
        Material matRunoff = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Track/M_Track_Runoff_Tarmac.mat");

        Material matLivery = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Car/M_Car_Livery_Primary.mat");
        Material matCarbon = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Car/M_Car_Chassis_Carbon.mat");
        Material matTyre = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Car/M_Car_Tyre_Rubber.mat");

        // Calibrate material properties to vibrant, natural PBR values
        matLivery.SetColor("_BaseColor", new Color(1.00f, 0.48f, 0.02f, 1.0f)); // Papaya Orange
        matLivery.SetFloat("_Smoothness", 0.75f);
        matLivery.SetFloat("_Metallic", 0.15f);

        matCarbon.SetColor("_BaseColor", new Color(0.04f, 0.04f, 0.04f, 1.0f)); // Carbon Black
        matCarbon.SetFloat("_Smoothness", 0.35f);

        matTyre.SetColor("_BaseColor", new Color(0.10f, 0.10f, 0.10f, 1.0f)); // Tyre Rubber Charcoal
        matTyre.SetFloat("_Smoothness", 0.20f);

        matConcrete.SetColor("_BaseColor", new Color(0.88f, 0.88f, 0.86f, 1.0f)); // Architectural White
        matConcrete.SetFloat("_Smoothness", 0.25f);

        matBarrier.SetColor("_BaseColor", new Color(0.82f, 0.84f, 0.88f, 1.0f)); // Steel Metal
        matBarrier.SetFloat("_Metallic", 0.70f);
        matBarrier.SetFloat("_Smoothness", 0.60f);

        matAsphalt.SetColor("_BaseColor", new Color(0.14f, 0.14f, 0.15f, 1.0f)); // Dark Charcoal Asphalt
        matAsphalt.SetFloat("_Smoothness", 0.18f);

        matGrass.SetColor("_BaseColor", new Color(0.28f, 0.44f, 0.18f, 1.0f)); // Desert Circuit Green Turf
        matGrass.SetFloat("_Smoothness", 0.10f);

        EditorUtility.SetDirty(matLivery);
        EditorUtility.SetDirty(matCarbon);
        EditorUtility.SetDirty(matTyre);
        EditorUtility.SetDirty(matConcrete);
        EditorUtility.SetDirty(matBarrier);
        EditorUtility.SetDirty(matAsphalt);
        EditorUtility.SetDirty(matGrass);
        AssetDatabase.SaveAssets();

        // 2. Open Bahrain Scene
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Circuits/Bahrain_Occlusion.unity", OpenSceneMode.Single);

        // 3. Unpack all prefabs
        GameObject circuitRoot = GameObject.Find("Bahrain_PBR");
        if (circuitRoot != null && PrefabUtility.IsPartOfPrefabInstance(circuitRoot))
        {
            PrefabUtility.UnpackPrefabInstance(circuitRoot, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        }

        GameObject car = GameObject.Find("F1_PlayerCar");
        if (car != null)
        {
            Transform visual = car.transform.Find("Visual_MCL35M");
            if (visual != null && PrefabUtility.IsPartOfPrefabInstance(visual.gameObject))
            {
                PrefabUtility.UnpackPrefabInstance(visual.gameObject, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            }
        }

        // 4. Delete old reflection probes and volumes
        foreach (var rp in UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include))
        {
            GameObject.DestroyImmediate(rp.gameObject);
        }
        foreach (var v in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
        {
            GameObject.DestroyImmediate(v.gameObject);
        }

        // 5. Categorize Bahrain Meshes
        var concreteNames = new HashSet<string>
        {
            "Object_4", "Object_4_LOD1", "Object_6", "Object_6_LOD1", "Object_12", "Object_12_LOD1",
            "Object_16", "Object_16_LOD1", "Object_140", "Object_140_LOD1", "Object_154", "Object_186",
            "Object_204", "Object_216", "Object_26", "Object_26_LOD1", "Object_34", "Object_34_LOD1",
            "Object_50", "Object_50_LOD1", "Object_74", "Object_74_LOD1", "Object_88", "Object_170",
            "Object_170_LOD1", "Object_224", "Object_224_LOD1", "Object_10", "Object_10_LOD1",
            "Object_46", "Object_46_LOD1", "Object_64", "Object_64_LOD1", "Object_144", "Object_144_LOD1",
            "Object_158", "Object_162", "Object_164", "Object_164_LOD1", "Object_166", "Object_166_LOD1"
        };

        var barrierNames = new HashSet<string>
        {
            "Object_14", "Object_14_LOD1", "Object_96", "Object_98", "Object_98_LOD1",
            "Object_104", "Object_128", "Object_132", "Object_148", "Object_150"
        };

        var asphaltNames = new HashSet<string>
        {
            "Object_176", "Object_76", "Object_38", "Object_184", "Object_24", "Object_152",
            "Object_40", "Object_44", "Object_30", "Object_188", "Object_200",
            "Object_178", "Object_94", "Object_206", "Object_168", "Object_100_LOD1"
        };

        var runoffNames = new HashSet<string>
        {
            "Object_190"
        };

        int countConcrete = 0, countBarrier = 0, countAsphalt = 0;
        foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            if (car != null && r.transform.IsChildOf(car.transform)) continue;

            string n = r.gameObject.name;
            if (concreteNames.Contains(n))
            {
                r.sharedMaterial = matConcrete;
                countConcrete++;
            }
            else if (barrierNames.Contains(n))
            {
                r.sharedMaterial = matBarrier;
                countBarrier++;
            }
            else if (asphaltNames.Contains(n))
            {
                r.sharedMaterial = matAsphalt;
                countAsphalt++;
            }
            else if (runoffNames.Contains(n))
            {
                r.sharedMaterial = matRunoff;
            }
            else
            {
                r.sharedMaterial = matGrass;
            }
            EditorUtility.SetDirty(r);
        }
        UnityEngine.Debug.Log($"[PERMANENT ASSIGNMENT] Concrete={countConcrete}, Barrier={countBarrier}, Asphalt={countAsphalt}");

        // 6. Assign Car Submesh Materials
        if (car != null)
        {
            car.SetActive(true);
            car.transform.position = new Vector3(-358.4f, 97.6f, 0.0f);
            car.transform.forward = new Vector3(0f, 0f, 1f);

            Transform visual = car.transform.Find("Visual_MCL35M");
            if (visual != null)
            {
                visual.localScale = new Vector3(100f, 100f, 100f);
                visual.localPosition = new Vector3(0f, 0.1f, 0f);

                foreach (var mr in visual.GetComponentsInChildren<MeshRenderer>(true))
                {
                    string mn = mr.name;
                    if (mn.StartsWith("Object_2") || mn.StartsWith("Object_4") || mn.StartsWith("Object_5"))
                    {
                        mr.sharedMaterial = matLivery; // Papaya Orange
                    }
                    else if (mn.StartsWith("Object_3"))
                    {
                        mr.sharedMaterial = matCarbon; // Carbon Black
                    }
                    else if (mn.StartsWith("Object_6") || mn.StartsWith("Object_7") || mn.StartsWith("Object_8") || mn.StartsWith("Object_9"))
                    {
                        mr.sharedMaterial = matTyre; // Pirelli Tyre Rubber
                    }
                    EditorUtility.SetDirty(mr);
                }
            }
            UnityEngine.Debug.Log("[CAR ASSIGNMENT] Body=Papaya, Aero=Carbon, Tyres=Rubber");
        }

        // 7. High Direct Sun + Trilight Ambient (Same verified lighting as playable_scene_chase.png)
        GameObject sunGo = GameObject.Find("Directional Light (Sun)");
        if (sunGo == null) sunGo = new GameObject("Directional Light (Sun)");
        Light sun = sunGo.GetComponent<Light>();
        if (sun == null) sun = sunGo.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1.0f, 0.98f, 0.94f, 1.0f);
        sun.intensity = 1.30f;
        sun.shadows = LightShadows.Soft;
        sunGo.transform.rotation = Quaternion.Euler(55.0f, 30.0f, 0.0f);
        EditorUtility.SetDirty(sun);

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.90f, 0.94f, 1.0f, 1.0f);
        RenderSettings.ambientEquatorColor = new Color(0.85f, 0.85f, 0.85f, 1.0f);
        RenderSettings.ambientGroundColor = new Color(0.65f, 0.65f, 0.65f, 1.0f);
        RenderSettings.ambientIntensity = 1.2f;

        // 8. Main Camera: Verified Chase Position
        GameObject camGo = GameObject.Find("Main Camera");
        if (camGo == null) camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        Camera cam = camGo.GetComponent<Camera>();
        if (cam == null) cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.48f, 0.65f, 0.85f, 1.0f);
        cam.fieldOfView = 58f;
        cam.nearClipPlane = 0.2f;
        cam.farClipPlane = 3500f;
        cam.useOcclusionCulling = true;

        var camData = camGo.GetComponent<UniversalAdditionalCameraData>();
        if (camData == null) camData = camGo.AddComponent<UniversalAdditionalCameraData>();
        camData.renderPostProcessing = false;

        // Chase position looking at car on main straight
        camGo.transform.position = new Vector3(-360.5f, 99.35f, -5.8f);
        camGo.transform.LookAt(new Vector3(-358.4f, 98.45f, 3.5f));
        EditorUtility.SetDirty(camGo);

        // 9. Save Scene Permanently
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        UnityEngine.Debug.Log("[SCENE SAVED] Bahrain_Occlusion.unity permanently saved.");

        // 10. Render Verification Image
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

        // 11. Rebuild Standalone Player
        RebuildFinalPlayer();
    }

    private static void RebuildFinalPlayer()
    {
        UnityEngine.Debug.Log("Rebuilding StandaloneWindows64 release build...");
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
        else
        {
            UnityEngine.Debug.LogError($"[REBUILD FAILED] Result: {report.summary.result}");
        }
    }
}
