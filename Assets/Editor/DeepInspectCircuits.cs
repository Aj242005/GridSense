using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;

public static class DeepInspectCircuits
{
    public static void InspectAll()
    {
        string[] targets = new string[]
        {
            "Assets/Red Bull ring/redbull-ring.fbx",
            "Assets/Shangai/shangai.fbx",
            "Assets/Suzuka Circuit/suzuka.fbx",
            "Assets/Bahrain Circuit/bahrainfbx.fbx"
        };

        StringBuilder sb = new StringBuilder();

        foreach (var p in targets)
        {
            GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            sb.AppendLine($"===========================================================================");
            sb.AppendLine($"FBX: {p}");
            sb.AppendLine($"===========================================================================");

            MeshRenderer[] mrs = go.GetComponentsInChildren<MeshRenderer>(true);
            sb.AppendLine($"Total Submeshes: {mrs.Length}");

            for (int i = 0; i < mrs.Length; i++)
            {
                var r = mrs[i];
                MeshFilter mf = r.GetComponent<MeshFilter>();
                Mesh m = mf != null ? mf.sharedMesh : null;
                Bounds b = r.bounds;
                string matNames = "";
                foreach (var mat in r.sharedMaterials)
                {
                    if (mat != null) matNames += mat.name + " | ";
                }

                sb.AppendLine($"[{i:D3}] GO: '{r.gameObject.name}' | Parent: '{r.transform.parent?.name}' | Mesh: '{(m != null ? m.name : "null")}' (Verts: {(m != null ? m.vertexCount : 0)}, Bounds: {b.size.x:F1}x{b.size.y:F1}x{b.size.z:F1}, Center: {b.center.x:F1},{b.center.y:F1},{b.center.z:F1}) | Mats: [{matNames}]");
            }
        }

        string outPath = "c:/Unity-In-Diversity/GridSense/circuit_deep_inspection.txt";
        File.WriteAllText(outPath, sb.ToString());
        UnityEngine.Debug.Log($"Deep inspection written to: {outPath}");
    }
}
