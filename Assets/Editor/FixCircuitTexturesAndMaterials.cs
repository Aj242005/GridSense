using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// GridSense: Comprehensive circuit texture and material repair tool.
/// Fixes the root causes of incorrect track rendering:
/// 
/// 1. Ensures all circuit MeshRenderers use their per-object textured materials
///    from Assets/Materials/{Circuit}_Textured/ (URP Lit with _BaseMap textures).
/// 2. Syncs _MainTex = _BaseMap on all circuit materials (prevents lightmap/fallback issues).
/// 3. Validates texture import settings: sRGB=true for baseColor, correct max size.
/// 4. Fixes materials with zero or incorrect _BaseColor tint (must be white for textured).
/// 5. Saves all changes to disk permanently.
///
/// Run via: GridSense → Fix All Circuit Textures & Materials
/// </summary>
public static class FixCircuitTexturesAndMaterials
{
    private static readonly (string scenePath, string matFolder, string texFolder)[] Circuits = new[]
    {
        ("Assets/Scenes/Circuits/Bahrain_Occlusion.unity",   "Bahrain_Textured",   "Assets/Bahrain Circuit/Textures"),
        ("Assets/Scenes/Circuits/Shanghai_Occlusion.unity",  "Shanghai_Textured",  "Assets/Shangai/textures"),
        ("Assets/Scenes/Circuits/Suzuka_Occlusion.unity",    "Suzuka_Textured",    "Assets/Suzuka Circuit/Textures"),
        ("Assets/Scenes/Circuits/YasMarina_Occlusion.unity", "YasMarina_Textured", "Assets/Yas Mariana/textures"),
    };

    [MenuItem("GridSense/Fix All Circuit Textures && Materials")]
    public static void FixAll()
    {
        Debug.Log("=== GRIDSENSE: CIRCUIT TEXTURE & MATERIAL REPAIR ===");

        // Phase 1: Fix all per-object materials in the Materials folders
        foreach (var circuit in Circuits)
        {
            FixMaterialsInFolder($"Assets/Materials/{circuit.matFolder}");
        }

        // Phase 2: Fix texture import settings
        foreach (var circuit in Circuits)
        {
            FixTextureImports(circuit.texFolder);
        }

        // Phase 3: Reassign materials in each scene
        foreach (var circuit in Circuits)
        {
            FixScene(circuit.scenePath, circuit.matFolder);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("=== CIRCUIT TEXTURE REPAIR COMPLETE ===");
    }

    /// <summary>
    /// For each .mat in the folder:
    /// - Ensure _BaseColor is white (1,1,1,1) so textures show at full brightness
    /// - Sync _MainTex = _BaseMap (prevents Unity's legacy fallback from showing blank)
    /// - Ensure shader is URP Lit
    /// </summary>
    private static void FixMaterialsInFolder(string folderPath)
    {
        string[] matGuids = AssetDatabase.FindAssets("t:Material", new[] { folderPath });
        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        int fixed_count = 0;

        foreach (string guid in matGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) continue;

            bool dirty = false;

            // Fix 1: Ensure shader is URP Lit
            if (urpLit != null && mat.shader != urpLit)
            {
                // Preserve the base texture before shader swap
                Texture baseTex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null;
                if (baseTex == null && mat.HasProperty("_MainTex"))
                    baseTex = mat.GetTexture("_MainTex");

                mat.shader = urpLit;

                if (baseTex != null)
                {
                    mat.SetTexture("_BaseMap", baseTex);
                    mat.SetTexture("_MainTex", baseTex);
                }
                dirty = true;
            }

            // Fix 2: Ensure _BaseColor is white so textures are not tinted
            if (mat.HasProperty("_BaseColor"))
            {
                Color baseColor = mat.GetColor("_BaseColor");
                // If the base color is too dark or too saturated, it tints the texture
                if (baseColor.r < 0.9f || baseColor.g < 0.9f || baseColor.b < 0.9f)
                {
                    mat.SetColor("_BaseColor", Color.white);
                    dirty = true;
                }
            }
            // Also sync legacy _Color property
            if (mat.HasProperty("_Color"))
            {
                Color legacyColor = mat.GetColor("_Color");
                if (legacyColor.r < 0.9f || legacyColor.g < 0.9f || legacyColor.b < 0.9f)
                {
                    mat.SetColor("_Color", Color.white);
                    dirty = true;
                }
            }

            // Fix 3: Sync _MainTex = _BaseMap
            if (mat.HasProperty("_BaseMap") && mat.HasProperty("_MainTex"))
            {
                Texture baseMap = mat.GetTexture("_BaseMap");
                Texture mainTex = mat.GetTexture("_MainTex");
                if (baseMap != null && mainTex != baseMap)
                {
                    mat.SetTexture("_MainTex", baseMap);
                    dirty = true;
                }
                else if (mainTex != null && baseMap == null)
                {
                    mat.SetTexture("_BaseMap", mainTex);
                    dirty = true;
                }
            }

            // Fix 4: Reasonable PBR defaults for track surfaces
            if (mat.HasProperty("_Smoothness"))
            {
                float smooth = mat.GetFloat("_Smoothness");
                if (smooth > 0.6f) // Track surfaces shouldn't be shiny
                {
                    mat.SetFloat("_Smoothness", 0.15f);
                    dirty = true;
                }
            }

            if (dirty)
            {
                EditorUtility.SetDirty(mat);
                fixed_count++;
            }
        }

        Debug.Log($"[FixCircuitTextures] Fixed {fixed_count}/{matGuids.Length} materials in {folderPath}");
    }

    /// <summary>
    /// Ensure all baseColor texture imports have sRGB=true and reasonable max size.
    /// </summary>
    private static void FixTextureImports(string textureFolder)
    {
        if (!AssetDatabase.IsValidFolder(textureFolder))
        {
            Debug.LogWarning($"[FixCircuitTextures] Texture folder not found: {textureFolder}");
            return;
        }

        string[] texGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { textureFolder });
        int fixed_count = 0;

        foreach (string guid in texGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            bool dirty = false;

            // BaseColor textures must be sRGB for correct color rendering
            if (path.Contains("baseColor") || path.Contains("BaseColor") || path.Contains("diffuse"))
            {
                if (!importer.sRGBTexture)
                {
                    importer.sRGBTexture = true;
                    dirty = true;
                }
            }

            // Cap max texture size for integrated GPU performance
            if (importer.maxTextureSize > 2048)
            {
                importer.maxTextureSize = 2048;
                dirty = true;
            }

            // Use Bilinear filtering minimum
            if (importer.filterMode == FilterMode.Point)
            {
                importer.filterMode = FilterMode.Bilinear;
                dirty = true;
            }

            if (dirty)
            {
                importer.SaveAndReimport();
                fixed_count++;
            }
        }

        Debug.Log($"[FixCircuitTextures] Fixed {fixed_count}/{texGuids.Length} texture imports in {textureFolder}");
    }

    /// <summary>
    /// Open each scene, match renderers to their per-object textured materials,
    /// and save the scene.
    /// </summary>
    private static void FixScene(string scenePath, string materialFolder)
    {
        if (!System.IO.File.Exists(scenePath.Replace("Assets/", "Assets/")))
        {
            // Use AssetDatabase to validate
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                Debug.LogWarning($"[FixCircuitTextures] Scene not found: {scenePath}");
                return;
            }
        }

        var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        int restored = 0;
        int total = 0;

        foreach (var renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include))
        {
            total++;

            // Skip car renderers (they already have correct materials)
            if (renderer.transform.root.name == "F1_PlayerCar") continue;

            // Try to find a matching textured material by renderer name
            string matPath = $"Assets/Materials/{materialFolder}/{renderer.name}_Mat.mat";
            Material texturedMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);

            if (texturedMat != null)
            {
                renderer.sharedMaterial = texturedMat;
                EditorUtility.SetDirty(renderer);
                restored++;
            }

            // Also try LOD variant naming
            string lodMatPath = $"Assets/Materials/{materialFolder}/{renderer.name.Replace("_LOD1", "")}_Mat.mat";
            if (texturedMat == null && renderer.name.Contains("LOD"))
            {
                Material lodMat = AssetDatabase.LoadAssetAtPath<Material>(lodMatPath);
                if (lodMat != null)
                {
                    renderer.sharedMaterial = lodMat;
                    EditorUtility.SetDirty(renderer);
                    restored++;
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"[FixCircuitTextures] Scene '{scene.name}': Restored {restored}/{total} renderers to textured materials.");
    }
}
