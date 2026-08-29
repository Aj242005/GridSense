using UnityEngine;
using UnityEditor;

public static class TestRaycastAtCamera
{
    public static void Test()
    {
        string p = "Assets/Prefabs/Circuits/RedBullRing_PBR.prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
        GameObject inst = GameObject.Instantiate(prefab);

        Vector3 lookTarget = new Vector3(26.7f, 51.8f, -26.3f);
        Vector3 camPos = lookTarget + new Vector3(15f, 25f, -20f);

        UnityEngine.Debug.Log($"=== TESTING CAMERA VIEW ON RED BULL RING ===");
        UnityEngine.Debug.Log($"CamPos: {camPos}, LookTarget: {lookTarget}");

        // Find what meshes are right around lookTarget
        Collider[] cols = inst.GetComponentsInChildren<Collider>();
        UnityEngine.Debug.Log($"Total Colliders: {cols.Length}");

        MeshRenderer[] mrs = inst.GetComponentsInChildren<MeshRenderer>();
        foreach (var mr in mrs)
        {
            if (mr.bounds.Contains(lookTarget) || Vector3.Distance(mr.bounds.center, lookTarget) < 15.0f)
            {
                UnityEngine.Debug.Log($"  Nearby Mesh: '{mr.gameObject.name}', Mat: '{mr.sharedMaterial?.name}', Bounds: {mr.bounds}, Verts: {mr.GetComponent<MeshFilter>()?.sharedMesh?.vertexCount}");
            }
        }

        GameObject.DestroyImmediate(inst);
    }
}
