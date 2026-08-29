using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Text;

public static class AssetInspector
{
    [MenuItem("GridSense/Inspect All FBX Assets")]
    public static void InspectAllFBX()
    {
        RunInspection();
    }

    public static void RunAllSection0Tasks()
    {
        RunInspection();
        TrackMetadataGenerator.GenerateAll();
    }

    // Called from batch mode via -executeMethod
    public static void RunInspection()
    {
        var assetFolders = new Dictionary<string, string>
        {
            { "Car",             "Assets/Car/Untitled.fbx" },
            { "Bahrain",         "Assets/Bahrain Circuit/bahrainfbx.fbx" },
            { "Red Bull Ring",   "Assets/Red Bull ring/redbull-ring.fbx" },
            { "Shanghai",        "Assets/Shangai/shangai.fbx" },
            { "Suzuka",          "Assets/Suzuka Circuit/suzuka.fbx" },
            { "Yas Marina",      "Assets/Yas Mariana/yasmariana.fbx" },
        };

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"assets\": [");

        bool first = true;
        foreach (var kvp in assetFolders)
        {
            string name = kvp.Key;
            string path = kvp.Value;

            if (!first) sb.AppendLine(",");
            first = false;

            sb.AppendLine($"    {{");
            sb.AppendLine($"      \"name\": \"{name}\",");
            sb.AppendLine($"      \"path\": \"{path}\",");

            // Load the FBX as a GameObject prefab
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                sb.AppendLine($"      \"error\": \"Could not load asset at {path}\"");
                sb.AppendLine($"    }}");
                continue;
            }

            // Collect all mesh filters
            MeshFilter[] meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
            SkinnedMeshRenderer[] skinnedRenderers = prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true);

            long totalVertices = 0;
            long totalTriangles = 0;
            int meshCount = 0;
            Bounds combinedBounds = new Bounds(Vector3.zero, Vector3.zero);
            bool boundsInitialized = false;

            // Process MeshFilter meshes
            foreach (var mf in meshFilters)
            {
                Mesh mesh = mf.sharedMesh;
                if (mesh == null) continue;
                meshCount++;
                totalVertices += mesh.vertexCount;
                totalTriangles += mesh.triangles.Length / 3;

                if (!boundsInitialized)
                {
                    combinedBounds = mesh.bounds;
                    // Transform bounds by the MeshFilter's transform
                    combinedBounds = TransformBounds(mf.transform, mesh.bounds);
                    boundsInitialized = true;
                }
                else
                {
                    combinedBounds.Encapsulate(TransformBounds(mf.transform, mesh.bounds));
                }
            }

            // Process SkinnedMeshRenderer meshes
            foreach (var smr in skinnedRenderers)
            {
                Mesh mesh = smr.sharedMesh;
                if (mesh == null) continue;
                meshCount++;
                totalVertices += mesh.vertexCount;
                totalTriangles += mesh.triangles.Length / 3;

                if (!boundsInitialized)
                {
                    combinedBounds = TransformBounds(smr.transform, mesh.bounds);
                    boundsInitialized = true;
                }
                else
                {
                    combinedBounds.Encapsulate(TransformBounds(smr.transform, mesh.bounds));
                }
            }

            // Also try loading sub-assets directly (meshes embedded in FBX)
            if (meshCount == 0)
            {
                Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var obj in subAssets)
                {
                    if (obj is Mesh mesh)
                    {
                        meshCount++;
                        totalVertices += mesh.vertexCount;
                        totalTriangles += mesh.triangles.Length / 3;

                        if (!boundsInitialized)
                        {
                            combinedBounds = mesh.bounds;
                            boundsInitialized = true;
                        }
                        else
                        {
                            combinedBounds.Encapsulate(mesh.bounds);
                        }
                    }
                }
            }

            sb.AppendLine($"      \"meshCount\": {meshCount},");
            sb.AppendLine($"      \"totalVertices\": {totalVertices},");
            sb.AppendLine($"      \"totalTriangles\": {totalTriangles},");
            sb.AppendLine($"      \"boundsCenter\": \"{combinedBounds.center}\",");
            sb.AppendLine($"      \"boundsSize\": \"{combinedBounds.size}\",");
            sb.AppendLine($"      \"boundsSizeX\": {combinedBounds.size.x:F4},");
            sb.AppendLine($"      \"boundsSizeY\": {combinedBounds.size.y:F4},");
            sb.AppendLine($"      \"boundsSizeZ\": {combinedBounds.size.z:F4},");

            // Collect textures from materials
            Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
            var textureInfos = new List<string>();
            var seenTextures = new HashSet<string>();

            foreach (var renderer in renderers)
            {
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null) continue;
                    
                    // Check common texture property names
                    string[] texProps = { "_MainTex", "_BaseMap", "_BumpMap", "_NormalMap",
                                          "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap",
                                          "_SpecGlossMap", "_DetailAlbedoMap" };
                    
                    foreach (var prop in texProps)
                    {
                        if (!mat.HasProperty(prop)) continue;
                        Texture tex = mat.GetTexture(prop);
                        if (tex == null) continue;
                        
                        string texPath = AssetDatabase.GetAssetPath(tex);
                        string key = $"{tex.name}_{tex.width}x{tex.height}";
                        if (seenTextures.Contains(key)) continue;
                        seenTextures.Add(key);

                        textureInfos.Add($"        {{ \"name\": \"{tex.name}\", \"width\": {tex.width}, \"height\": {tex.height}, \"property\": \"{prop}\", \"path\": \"{texPath}\" }}");
                    }
                }
            }

            // Also check sub-assets for embedded textures
            {
                Object[] subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                foreach (var obj in subAssets)
                {
                    if (obj is Texture2D tex2d)
                    {
                        string key = $"{tex2d.name}_{tex2d.width}x{tex2d.height}";
                        if (seenTextures.Contains(key)) continue;
                        seenTextures.Add(key);
                        textureInfos.Add($"        {{ \"name\": \"{tex2d.name}\", \"width\": {tex2d.width}, \"height\": {tex2d.height}, \"property\": \"embedded\", \"path\": \"{path}\" }}");
                    }
                }
            }

            sb.AppendLine($"      \"textures\": [");
            sb.AppendLine(string.Join(",\n", textureInfos));
            sb.AppendLine($"      ],");

            // File size
            string fullPath = Path.GetFullPath(path);
            long fileSize = new FileInfo(fullPath).Length;
            sb.AppendLine($"      \"fileSizeBytes\": {fileSize}");

            sb.AppendLine($"    }}");
        }

        sb.AppendLine("  ]");
        sb.AppendLine("}");

        string outputPath = Path.Combine(Application.dataPath, "..", "asset_inspection_report.json");
        File.WriteAllText(outputPath, sb.ToString());
        Debug.Log($"[AssetInspector] Report written to: {outputPath}");
    }

    private static Bounds TransformBounds(Transform t, Bounds localBounds)
    {
        Vector3 center = t.TransformPoint(localBounds.center);
        Vector3 extents = localBounds.extents;
        
        // Transform extents by the absolute values of the transform's axes
        Vector3 axisX = t.TransformVector(extents.x, 0, 0);
        Vector3 axisY = t.TransformVector(0, extents.y, 0);
        Vector3 axisZ = t.TransformVector(0, 0, extents.z);

        extents = new Vector3(
            Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x),
            Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y),
            Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z)
        );

        return new Bounds(center, extents * 2);
    }
}
