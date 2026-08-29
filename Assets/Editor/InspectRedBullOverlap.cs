using UnityEngine;
using UnityEditor;

public static class InspectRedBullOverlap
{
    public static void Inspect()
    {
        string p = "Assets/Prefabs/Circuits/RedBullRing_PBR.prefab";
        GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
        MeshRenderer[] mrs = go.GetComponentsInChildren<MeshRenderer>(true);

        UnityEngine.Debug.Log("=== RED BULL RING MATERIAL TO SUBMESH MAPPING ===");
        for (int i = 0; i < mrs.Length; i++)
        {
            var r = mrs[i];
            MeshFilter mf = r.GetComponent<MeshFilter>();
            int verts = mf != null && mf.sharedMesh != null ? mf.sharedMesh.vertexCount : 0;
            string mat = r.sharedMaterial != null ? r.sharedMaterial.name : "null";
            Bounds b = r.bounds;
            UnityEngine.Debug.Log($"[{i:D3}] '{r.gameObject.name}' | Mat: {mat} | Verts: {verts} | Y: {b.min.y:F1} to {b.max.y:F1} | Center: ({b.center.x:F1}, {b.center.y:F1}, {b.center.z:F1}) | Size: ({b.size.x:F1}, {b.size.y:F1}, {b.size.z:F1})");
        }
    }
}
