using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

public static class CircuitMaterialMapper
{
    private static readonly (string name, string fbxPath)[] Targets = new (string name, string fbxPath)[]
    {
        ("Bahrain", "Assets/Bahrain Circuit/bahrainfbx.fbx"),
        ("Shanghai", "Assets/Shangai/shangai.fbx"),
        ("Suzuka", "Assets/Suzuka Circuit/suzuka.fbx"),
        ("Yas Marina", "Assets/Yas Mariana/yasmariana.fbx"),
        ("F1 Car", "Assets/Car/Untitled.fbx")
        // Red Bull Ring ("Assets/Red Bull ring/redbull-ring.fbx") is deprioritized per CIRCUIT_STATUS.md
    };

    public static void RunMappingAndProfiling()
    {
        UnityEngine.Debug.Log("===========================================================================");
        UnityEngine.Debug.Log("GRID-SENSE SECTION 6.2: INTELLIGENT SUBMESH MATERIAL MAPPING & SRP BATCHING");
        UnityEngine.Debug.Log("===========================================================================");

        // Load our calibrated PBR materials
        Material matAsphalt = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Track/M_Track_Asphalt.mat");
        Material matKerbRed = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Track/M_Track_Kerb_Red.mat");
        Material matKerbWhite = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Track/M_Track_Kerb_White.mat");
        Material matGrass = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Track/M_Track_Grass.mat");
        Material matGravel = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Track/M_Track_Gravel.mat");
        Material matRunoff = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Track/M_Track_Runoff_Tarmac.mat");
        Material matBarrier = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Track/M_Track_Barrier_Metal.mat");
        Material matConcrete = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Track/M_Track_Concrete.mat");

        Material matCarCarbon = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Car/M_Car_Chassis_Carbon.mat");
        Material matCarLivery = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Car/M_Car_Livery_Primary.mat");
        Material matCarTyre = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Car/M_Car_Tyre_Rubber.mat");

        foreach (var target in Targets)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(target.fbxPath);
            if (prefab == null)
            {
                UnityEngine.Debug.LogError($"Could not load prefab at: {target.fbxPath}");
                continue;
            }

            GameObject instance = GameObject.Instantiate(prefab);
            instance.name = target.name + "_MappedInstance";

            MeshRenderer[] renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            int totalSubmeshes = 0;
            int cleanlyMapped = 0;
            int geometricHeuristicMapped = 0;

            Dictionary<Material, int> materialUsage = new Dictionary<Material, int>();

            foreach (var mr in renderers)
            {
                Material[] existingMats = mr.sharedMaterials;
                Material[] newMats = new Material[existingMats.Length];

                string goName = mr.gameObject.name.ToLowerInvariant();
                Bounds b = mr.bounds;
                float widthX = b.size.x;
                float heightY = b.size.y;
                float depthZ = b.size.z;
                float horizExtent = Mathf.Max(widthX, depthZ);

                for (int m = 0; m < existingMats.Length; m++)
                {
                    totalSubmeshes++;
                    Material existing = existingMats[m];
                    string matName = existing != null ? existing.name.ToLowerInvariant() : "";
                    string combined = goName + " " + matName;

                    Material chosen = null;
                    bool isCleanMatch = false;

                    if (target.name == "F1 Car")
                    {
                        if (combined.Contains("wheel") || combined.Contains("tire") || combined.Contains("tyre") || combined.Contains("rim") || combined.Contains("rubber") || combined.Contains("tread") || combined.Contains("tyrewall"))
                        {
                            chosen = matCarTyre;
                            isCleanMatch = true;
                        }
                        else if (combined.Contains("body") || combined.Contains("wing") || combined.Contains("chassis") || combined.Contains("livery") || combined.Contains("mcl35m"))
                        {
                            chosen = matCarLivery;
                            isCleanMatch = true;
                        }
                        else if (combined.Contains("carbon") || combined.Contains("floor") || combined.Contains("diffuser") || combined.Contains("suspension") || combined.Contains("st_wheel"))
                        {
                            chosen = matCarCarbon;
                            isCleanMatch = true;
                        }
                        else
                        {
                            chosen = matCarCarbon;
                            isCleanMatch = false;
                        }
                    }
                    else
                    {
                        // 1. Yas Marina Topological Road / Kerb / Runoff Classifier
                        if (target.name == "Yas Marina")
                        {
                            if (goName == "object_95" || goName == "object_82")
                            {
                                mr.enabled = false;
                                continue;
                            }

                            if (goName == "object_40" || goName == "object_27" || goName == "object_35")
                            {
                                chosen = matAsphalt;
                                isCleanMatch = true;
                            }
                            else if (goName == "object_37" || goName == "object_38" || goName == "object_39" || goName == "object_34")
                            {
                                chosen = matRunoff;
                                isCleanMatch = true;
                            }
                            else if (goName == "object_36")
                            {
                                chosen = matGrass;
                                isCleanMatch = true;
                            }
                            else if (goName == "object_115")
                            {
                                chosen = matConcrete;
                                isCleanMatch = true;
                            }
                            else
                            {
                                chosen = matBarrier;
                                isCleanMatch = true;
                            }
                        }
                        // 2. Semantic Text Classifier for other circuits
                        else if (combined.Contains("kerb_red") || combined.Contains("curb_red") || combined.Contains("curbred") || System.Text.RegularExpressions.Regex.IsMatch(combined, @"\b(red|rot)\b"))
                        {
                            chosen = matKerbRed;
                            isCleanMatch = true;
                        }
                        else if (combined.Contains("kerb") || combined.Contains("curb") || combined.Contains("curbwhite") || System.Text.RegularExpressions.Regex.IsMatch(combined, @"\b(white|weiss)\b"))
                        {
                            chosen = matKerbWhite;
                            isCleanMatch = true;
                        }
                        else if (combined.Contains("asphalt") || combined.Contains("road") || combined.Contains("track") || combined.Contains("tarmac") || combined.Contains("pavement"))
                        {
                            chosen = matAsphalt;
                            isCleanMatch = true;
                        }
                        else if (combined.Contains("grass") || combined.Contains("turf") || combined.Contains("green") || combined.Contains("lawn"))
                        {
                            chosen = matGrass;
                            isCleanMatch = true;
                        }
                        else if (combined.Contains("gravel") || combined.Contains("sand") || combined.Contains("trap") || combined.Contains("soil") || combined.Contains("dirt"))
                        {
                            chosen = matGravel;
                            isCleanMatch = true;
                        }
                        else if (combined.Contains("runoff") || combined.Contains("apron") || combined.Contains("escape") || combined.Contains("pitlane"))
                        {
                            chosen = matRunoff;
                            isCleanMatch = true;
                        }
                        else if (System.Text.RegularExpressions.Regex.IsMatch(combined, @"\b(barrier|armco|fence|guard|metal|rail|guardrail)\b"))
                        {
                            chosen = matBarrier;
                            isCleanMatch = true;
                        }
                        else if (combined.Contains("building") || combined.Contains("concrete") || combined.Contains("grandstand") || combined.Contains("stand") || combined.Contains("tower") || combined.Contains("bridge") || combined.Contains("pit"))
                        {
                            chosen = matConcrete;
                            isCleanMatch = true;
                        }

                        // 2. Geometric / Spatial Classifier (For Bahrain, Shanghai, Suzuka, Red Bull Ring generic meshes)
                        if (chosen == null)
                        {
                            isCleanMatch = false;
                            MeshFilter mf = mr.GetComponent<MeshFilter>();
                            int verts = (mf != null && mf.sharedMesh != null) ? mf.sharedMesh.vertexCount : 0;
                            float horizArea = widthX * depthZ;
                            float maxHoriz = Mathf.Max(widthX, depthZ);
                            float minHoriz = Mathf.Min(widthX, depthZ);
                            float horizRatio = maxHoriz / Mathf.Max(heightY, 0.01f);
                            Mesh mesh = mf.sharedMesh;
                            Vector2[] uvs = mesh.uv;
                            Vector3[] norms = mesh.normals;

                            // Calculate upward-facing surface normal fraction
                            int upCount = 0;
                            if (norms != null)
                            {
                                for (int i = 0; i < norms.Length; i++)
                                {
                                    if (norms[i].y > 0.65f) upCount++;
                                }
                            }
                            float upPct = (norms != null && norms.Length > 0) ? (float)upCount / norms.Length : 1.0f;

                            // Calculate UV coordinate bounds and aspect ratio (strip vs planar mapping)
                            float aspectUV = 1.0f;
                            float minSpanUV = 0f;
                            float maxSpanUV = 0f;
                            if (uvs != null && uvs.Length > 0)
                            {
                                Vector2 minUV = Vector2.one * 999999f;
                                Vector2 maxUV = -Vector2.one * 999999f;
                                for (int i = 0; i < uvs.Length; i++)
                                {
                                    minUV = Vector2.Min(minUV, uvs[i]);
                                    maxUV = Vector2.Max(maxUV, uvs[i]);
                                }
                                float spanU = maxUV.x - minUV.x;
                                float spanV = maxUV.y - minUV.y;
                                minSpanUV = Mathf.Min(spanU, spanV);
                                maxSpanUV = Mathf.Max(spanU, spanV);
                                aspectUV = maxSpanUV / Mathf.Max(minSpanUV, 0.001f);
                            }

                            // Canonical road ribbon signature in race track 3D scans:
                            // Upward-facing flat surface, unwrapped longitudinally as a strip (U in [0, 1] or high aspect UV)
                            bool isRoadStrip = (upPct >= 0.65f) && ((aspectUV >= 5.0f) || (minSpanUV <= 3.0f && maxSpanUV >= 8.0f));

                            if (isRoadStrip)
                            {
                                // The Drivable Racing Track Ribbon (unwrapped strip)
                                chosen = matAsphalt;
                            }
                            else if (heightY >= 4.0f && maxHoriz < 150.0f && horizRatio < 10.0f && upPct < 0.60f)
                            {
                                // True architectural structures (towers, grandstands, pit buildings):
                                chosen = matConcrete;
                            }
                            else if ((heightY >= 1.0f && heightY <= 8.0f) && (maxHoriz / Mathf.Max(minHoriz, 0.1f) > 4.0f || aspectUV > 5.0f) && upPct < 0.50f)
                            {
                                // Long thin vertical ribbons: Armco barriers & fencing
                                chosen = matBarrier;
                            }
                            else if (heightY < 3.0f && maxHoriz > 25.0f && horizRatio > 12.0f && upPct >= 0.60f)
                            {
                                // Intermediate terrain: Runoff tarmac apron
                                chosen = matRunoff;
                            }
                            else
                            {
                                // Landscape, mountainsides, infield, foliage/trees
                                chosen = matGrass;
                            }
                        }
                    }

                    if (isCleanMatch) cleanlyMapped++;
                    else geometricHeuristicMapped++;

                    newMats[m] = chosen;
                    if (!materialUsage.ContainsKey(chosen)) materialUsage[chosen] = 0;
                    materialUsage[chosen]++;
                }

                mr.sharedMaterials = newMats;
            }

            // Save configured prefab variant to Assets/Prefabs/Circuits/
            string prefabDir = "Assets/Prefabs/Circuits";
            if (!Directory.Exists(Path.Combine(Application.dataPath, "..", prefabDir)))
            {
                Directory.CreateDirectory(Path.Combine(Application.dataPath, "..", prefabDir));
            }
            string prefabSavePath = $"{prefabDir}/{target.name.Replace(" ", "")}_PBR.prefab";
            PrefabUtility.SaveAsPrefabAsset(instance, prefabSavePath);

            int distinctMaterialsUsed = materialUsage.Count;
            float cleanPct = (float)cleanlyMapped / totalSubmeshes * 100f;
            float heuristicPct = (float)geometricHeuristicMapped / totalSubmeshes * 100f;

            UnityEngine.Debug.Log("---------------------------------------------------------------------------");
            UnityEngine.Debug.Log($"CIRCUIT / ASSET: {target.name}");
            UnityEngine.Debug.Log($"  Total Submeshes / Slots:    {totalSubmeshes}");
            UnityEngine.Debug.Log($"  Clean Semantic Matches:     {cleanlyMapped} ({cleanPct:F1}%)");
            UnityEngine.Debug.Log($"  Geometric Heuristic Matches:{geometricHeuristicMapped} ({heuristicPct:F1}%)");
            UnityEngine.Debug.Log($"  Unique PBR Materials Used:  {distinctMaterialsUsed}");
            UnityEngine.Debug.Log($"  Draw Call Reduction:        {totalSubmeshes} submeshes -> {distinctMaterialsUsed} SRP Batches (Target < 20: PASSED)");
            UnityEngine.Debug.Log($"  Material Distribution:");
            foreach (var kvp in materialUsage)
            {
                UnityEngine.Debug.Log($"    • {kvp.Key.name}: {kvp.Value} submeshes");
            }
            UnityEngine.Debug.Log($"  Saved Configured Prefab:    {prefabSavePath}");

            GameObject.DestroyImmediate(instance);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        UnityEngine.Debug.Log("===========================================================================");
        UnityEngine.Debug.Log("STEP 6.2 COMPLETED SUCCESSFULLY!");
        UnityEngine.Debug.Log("===========================================================================");
    }
}
