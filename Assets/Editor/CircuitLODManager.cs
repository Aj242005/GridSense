using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

public static class CircuitLODManager
{
    private static readonly (string name, string prefabPath)[] ActiveTargets = new (string name, string prefabPath)[]
    {
        ("Bahrain", "Assets/Prefabs/Circuits/Bahrain_PBR.prefab"),
        ("Shanghai", "Assets/Prefabs/Circuits/Shanghai_PBR.prefab"),
        ("Suzuka", "Assets/Prefabs/Circuits/Suzuka_PBR.prefab"),
        ("YasMarina", "Assets/Prefabs/Circuits/YasMarina_PBR.prefab"),
        ("F1Car", "Assets/Prefabs/Circuits/F1Car_PBR.prefab")
    };

    public static void RunLODGenerationAndSetup()
    {
        UnityEngine.Debug.Log("===========================================================================");
        UnityEngine.Debug.Log("GRID-SENSE SECTION 6.3: MULTI-TIER LODGROUP SETUP & DISTANCE CULLING");
        UnityEngine.Debug.Log("Target Hardware: Integrated Graphics (Ultra-Aggressive Geometry Management)");
        UnityEngine.Debug.Log("===========================================================================");

        string baseMeshDir = "Assets/Meshes/LODs";
        if (!Directory.Exists(baseMeshDir))
        {
            Directory.CreateDirectory(baseMeshDir);
        }

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var target in ActiveTargets)
            {
            UnityEngine.Debug.Log($"\n---------------------------------------------------------------------------");
            UnityEngine.Debug.Log($"PROCESSING TARGET: {target.name}");
            UnityEngine.Debug.Log($"---------------------------------------------------------------------------");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(target.prefabPath);
            if (prefab == null)
            {
                UnityEngine.Debug.LogError($"Prefab not found at: {target.prefabPath}");
                continue;
            }

            GameObject instance = GameObject.Instantiate(prefab);
            instance.name = target.name + "_LODProcessing";

            string targetMeshDir = Path.Combine(baseMeshDir, target.name).Replace('\\', '/');
            if (!Directory.Exists(targetMeshDir))
            {
                Directory.CreateDirectory(targetMeshDir);
            }

            MeshRenderer[] renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            int totalSubmeshes = renderers.Length;
            int trackRibbonProtected = 0;
            int lodGroupsCreated = 0;
            int twoTierCount = 0;
            int singleTierCount = 0;
            long origTrisTotal = 0;
            long lod1TrisTotal = 0;
            List<string> quirksHit = new List<string>();

            // Collect top-level renderers (avoid processing already-added child LOD renderers)
            List<MeshRenderer> topRenderers = new List<MeshRenderer>();
            foreach (var mr in renderers)
            {
                if (mr.gameObject.name.EndsWith("_LOD1")) continue;
                topRenderers.Add(mr);
            }

            foreach (var mr in topRenderers)
            {
                GameObject go = mr.gameObject;
                MeshFilter mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                Mesh origMesh = mf.sharedMesh;
                int triCount = origMesh.triangles.Length / 3;
                origTrisTotal += triCount;

                string matName = (mr.sharedMaterial != null) ? mr.sharedMaterial.name : "";
                string goName = go.name.ToLowerInvariant();

                // 1. Check if this is the Track Surface Ribbon or Kerb (Protected at LOD0 100% fidelity)
                bool isTrackRibbon = matName.Contains("Asphalt") ||
                                     matName.Contains("Kerb") ||
                                     matName.Contains("WhiteLine") ||
                                     matName.Contains("YellowLine") ||
                                     goName.Contains("asphalt") ||
                                     goName.Contains("kerb") ||
                                     goName.Contains("curb");

                if (target.name == "YasMarina")
                {
                    if (goName == "object_40" || goName == "object_27" || goName == "object_35" ||
                        goName == "object_37" || goName == "object_38" || goName == "object_39" || goName == "object_34")
                    {
                        isTrackRibbon = true;
                    }
                }

                if (target.name == "F1Car")
                {
                    // For F1 car, chassis and wheels use vehicle LOD thresholds
                    isTrackRibbon = false;
                }

                if (isTrackRibbon)
                {
                    trackRibbonProtected++;
                    lod1TrisTotal += triCount;
                    // Remove any existing LODGroup on track ribbon to ensure un-culled 100% rendering
                    LODGroup existingLod = go.GetComponent<LODGroup>();
                    if (existingLod != null) GameObject.DestroyImmediate(existingLod);

                    // Also remove any child LOD1 object if previously created
                    Transform childLod1 = go.transform.Find(go.name + "_LOD1");
                    if (childLod1 != null) GameObject.DestroyImmediate(childLod1.gameObject);
                    continue;
                }

                // 2. Classify environment mesh for appropriate LOD transition thresholds
                Bounds b = mr.bounds;
                float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
                float heightY = b.size.y;

                float lod0Transition;
                float cullTransition;

                if (target.name == "F1Car")
                {
                    // Vehicle LODs: visible close up, simplified at medium range, culled only when distant speck
                    lod0Transition = 0.20f;
                    cullTransition = 0.008f;
                }
                else if (matName.Contains("Grass") && maxDim >= 300.0f)
                {
                    // Distant regional landscape / mountains / horizon silhouette
                    // Kept visible across horizon to avoid abrupt popping
                    lod0Transition = 0.15f;
                    cullTransition = 0.005f; // < 0.5% screen height
                }
                else if (matName.Contains("Concrete") || (heightY >= 4.0f && maxDim >= 25.0f))
                {
                    // Architectural structures, grandstands, pit building, control towers
                    lod0Transition = 0.20f;
                    cullTransition = 0.02f; // < 2% screen height
                }
                else
                {
                    // Repeated barriers, fences, guardrails, small runoff patches, props
                    // Aggressive culling for integrated GPU fillrate
                    lod0Transition = 0.25f;
                    cullTransition = 0.05f; // < 5% screen height
                }

                // 3. Setup LODGroup component
                LODGroup lodGroup = go.GetComponent<LODGroup>();
                if (lodGroup == null) lodGroup = go.AddComponent<LODGroup>();
                lodGroup.fadeMode = LODFadeMode.CrossFade;
                lodGroup.animateCrossFading = true;

                // 4. Generate LOD1 Decimated Mesh if mesh has sufficient geometry
                Mesh lod1Mesh = (origMesh.vertexCount >= 150) ? DecimateMesh(origMesh, 0.45f) : null;

                if (lod1Mesh != null && lod1Mesh != origMesh && lod1Mesh.vertexCount < origMesh.vertexCount)
                {
                    string meshAssetPath = $"{targetMeshDir}/{go.name}_LOD1.asset";
                    if (File.Exists(meshAssetPath)) AssetDatabase.DeleteAsset(meshAssetPath);

                    // Save mesh asset
                    AssetDatabase.CreateAsset(lod1Mesh, meshAssetPath);

                    // Ensure clean child GameObject for LOD1
                    Transform existingChild = go.transform.Find(go.name + "_LOD1");
                    GameObject lod1Go;
                    if (existingChild != null)
                    {
                        lod1Go = existingChild.gameObject;
                    }
                    else
                    {
                        lod1Go = new GameObject(go.name + "_LOD1");
                        lod1Go.transform.SetParent(go.transform, false);
                    }

                    MeshFilter lod1Mf = lod1Go.GetComponent<MeshFilter>();
                    if (lod1Mf == null) lod1Mf = lod1Go.AddComponent<MeshFilter>();
                    lod1Mf.sharedMesh = lod1Mesh;

                    MeshRenderer lod1Mr = lod1Go.GetComponent<MeshRenderer>();
                    if (lod1Mr == null) lod1Mr = lod1Go.AddComponent<MeshRenderer>();
                    lod1Mr.sharedMaterials = mr.sharedMaterials;
                    lod1Mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; // Integrated GPU optimization: no shadows on LOD1
                    lod1Mr.receiveShadows = mr.receiveShadows;

                    // Configure 2-tier LODGroup
                    LOD[] lods = new LOD[2];
                    lods[0] = new LOD(lod0Transition, new Renderer[] { mr });
                    lods[1] = new LOD(cullTransition, new Renderer[] { lod1Mr });
                    lodGroup.SetLODs(lods);

                    twoTierCount++;
                    int lod1Tris = lod1Mesh.triangles.Length / 3;
                    lod1TrisTotal += lod1Tris;
                }
                else
                {
                    // Single-tier distance-culled prop (low-poly prop or un-decimated mesh)
                    LOD[] lods = new LOD[1];
                    lods[0] = new LOD(cullTransition, new Renderer[] { mr });
                    lodGroup.SetLODs(lods);

                    singleTierCount++;
                    lod1TrisTotal += triCount;
                }

                lodGroup.RecalculateBounds();
                lodGroupsCreated++;
            }

            // Check for quirks
            if (target.name == "YasMarina" && twoTierCount < 10 && lodGroupsCreated > 100)
            {
                quirksHit.Add("Yas Marina contains predominantly low-poly barrier segments (<150 verts each) -> single-tier aggressive culling dominant");
            }
            if (target.name == "Bahrain" && origTrisTotal > 1000000)
            {
                quirksHit.Add("High raw triangle count on desert terrain mesh");
            }

            // Save configured prefab
            PrefabUtility.SaveAsPrefabAsset(instance, target.prefabPath);
            GameObject.DestroyImmediate(instance);

            AssetDatabase.SaveAssets();

            float reductionPct = (origTrisTotal > 0) ? (1.0f - (float)lod1TrisTotal / origTrisTotal) * 100f : 0f;

            UnityEngine.Debug.Log($"RESULTS FOR {target.name}:");
            UnityEngine.Debug.Log($"  Total Submeshes:            {totalSubmeshes}");
            UnityEngine.Debug.Log($"  Track Ribbon (LOD0 100%):   {trackRibbonProtected} (No culling/decimation)");
            UnityEngine.Debug.Log($"  LODGroups Configured:       {lodGroupsCreated}");
            UnityEngine.Debug.Log($"  2-Tier Decimated (LOD0+1):  {twoTierCount}");
            UnityEngine.Debug.Log($"  1-Tier Distance Culled:     {singleTierCount}");
            UnityEngine.Debug.Log($"  Raw LOD0 Triangles:         {origTrisTotal:N0}");
            UnityEngine.Debug.Log($"  Mid-Range LOD1 Triangles:   {lod1TrisTotal:N0} (-{reductionPct:F1}%)");
            if (quirksHit.Count > 0)
            {
                foreach (var q in quirksHit) UnityEngine.Debug.Log($"  Circuit Quirk:              {q}");
            }
        }
    }
    finally
    {
        AssetDatabase.StopAssetEditing();
    }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        UnityEngine.Debug.Log("\n===========================================================================");
        UnityEngine.Debug.Log("STEP 6.3: LODGROUP SETUP & DISTANCE CULLING COMPLETE ACROSS ALL CIRCUITS!");
        UnityEngine.Debug.Log("===========================================================================");
    }

    /// <summary>
    /// Fast and robust 3D vertex clustering mesh decimation preserving silhouette, normals, and UVs.
    /// </summary>
    public static Mesh DecimateMesh(Mesh source, float targetRatio = 0.45f)
    {
        if (source == null || source.vertexCount < 50) return null;

        Vector3[] srcVerts = source.vertices;
        Vector3[] srcNorms = source.normals;
        Vector2[] srcUVs = source.uv;
        int[] srcTris = source.triangles;

        Bounds b = source.bounds;
        float maxDim = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (maxDim < 0.001f) return null;

        // Calculate spatial cell grid size based on target vertex ratio
        int targetVerts = Mathf.Max(30, (int)(srcVerts.Length * targetRatio));
        float cellsPerAxis = Mathf.Clamp(Mathf.Pow(targetVerts * 1.5f, 1f / 3f), 10f, 160f);
        float cellSize = maxDim / cellsPerAxis;

        Dictionary<long, int> cellToNewIndex = new Dictionary<long, int>();
        List<Vector3> newVerts = new List<Vector3>();
        List<Vector3> newNorms = (srcNorms != null && srcNorms.Length == srcVerts.Length) ? new List<Vector3>() : null;
        List<Vector2> newUVs = (srcUVs != null && srcUVs.Length == srcVerts.Length) ? new List<Vector2>() : null;
        List<int> vertexWeights = new List<int>();

        int[] vertexMap = new int[srcVerts.Length];

        for (int i = 0; i < srcVerts.Length; i++)
        {
            Vector3 v = srcVerts[i];
            int cx = Mathf.FloorToInt((v.x - b.min.x) / cellSize);
            int cy = Mathf.FloorToInt((v.y - b.min.y) / cellSize);
            int cz = Mathf.FloorToInt((v.z - b.min.z) / cellSize);
            long key = ((long)cx * 73856093L) ^ ((long)cy * 19349663L) ^ ((long)cz * 83492791L);

            if (!cellToNewIndex.TryGetValue(key, out int newIdx))
            {
                newIdx = newVerts.Count;
                cellToNewIndex[key] = newIdx;
                newVerts.Add(v);
                if (newNorms != null) newNorms.Add(srcNorms[i]);
                if (newUVs != null) newUVs.Add(srcUVs[i]);
                vertexWeights.Add(1);
            }
            else
            {
                int w = vertexWeights[newIdx];
                newVerts[newIdx] = (newVerts[newIdx] * w + v) / (w + 1);
                if (newNorms != null) newNorms[newIdx] = (newNorms[newIdx] * w + srcNorms[i]).normalized;
                vertexWeights[newIdx] = w + 1;
            }
            vertexMap[i] = newIdx;
        }

        // Reconstruct triangles, discarding degenerate edges
        List<int> newTris = new List<int>();
        for (int i = 0; i < srcTris.Length; i += 3)
        {
            int i0 = vertexMap[srcTris[i]];
            int i1 = vertexMap[srcTris[i + 1]];
            int i2 = vertexMap[srcTris[i + 2]];

            if (i0 != i1 && i1 != i2 && i2 != i0)
            {
                newTris.Add(i0);
                newTris.Add(i1);
                newTris.Add(i2);
            }
        }

        if (newTris.Count < 3) return null; // Fallback if over-collapsed

        Mesh lodMesh = new Mesh();
        lodMesh.name = source.name + "_LOD1";
        lodMesh.indexFormat = newVerts.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        lodMesh.vertices = newVerts.ToArray();
        if (newNorms != null) lodMesh.normals = newNorms.ToArray();
        else lodMesh.RecalculateNormals();
        if (newUVs != null) lodMesh.uv = newUVs.ToArray();
        lodMesh.triangles = newTris.ToArray();
        lodMesh.RecalculateBounds();

        return lodMesh;
    }
}
