using UnityEngine;
using UnityEditor;

public static class InspectObject60
{
    public static void Inspect()
    {
        string p = "Assets/Red Bull ring/redbull-ring.fbx";
        GameObject go = AssetDatabase.LoadAssetAtPath<GameObject>(p);
        MeshRenderer[] mrs = go.GetComponentsInChildren<MeshRenderer>(true);

        foreach (var r in mrs)
        {
            if (r.gameObject.name == "Object_60" || r.gameObject.name == "Object_11")
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                UnityEngine.Debug.Log($"GO: {r.gameObject.name}, Parent: {r.transform.parent.name}, Pos: {r.transform.localPosition}, LossyScale: {r.transform.lossyScale}");
                Bounds b = r.bounds;
                UnityEngine.Debug.Log($"  Bounds: Center={b.center}, Size={b.size}, Min={b.min}, Max={b.max}");
            }
        }
    }
}
