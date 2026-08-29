using UnityEngine;
using UnityEditor;

/// <summary>
/// Non-destructive postprocessor to normalize circuit scales on import:
/// 1. Red Bull Ring: resets intermediate node 'rbring_0' localScale from (8, 8, 8) to (1, 1, 1),
///    eliminating the 8x overshoot and restoring the circuit to ~4.3km footprint.
/// 2. Yas Marina: corrects root localScale from ~50.17 to (100, 100, 100),
///    restoring the circuit to its true 1:1 metre footprint.
/// </summary>
public class CircuitScalePostprocessor : AssetPostprocessor
{
    void OnPostprocessModel(GameObject root)
    {
        if (assetPath.Contains("redbull-ring.fbx"))
        {
            Transform rbring = root.transform.Find("root/GLTF_SceneRootNode/rbring_0");
            if (rbring != null)
            {
                Debug.Log($"[CircuitScalePostprocessor] Resetting Red Bull Ring rbring_0 scale from {rbring.localScale} to (1, 1, 1)");
                rbring.localScale = Vector3.one;
            }
            else
            {
                Debug.LogWarning("[CircuitScalePostprocessor] Could not find root/GLTF_SceneRootNode/rbring_0");
            }

            // Also check Plane_1 (background plane)
            Transform plane = root.transform.Find("root/GLTF_SceneRootNode/Plane_1");
            if (plane != null)
            {
                Debug.Log($"[CircuitScalePostprocessor] Found Plane_1 with scale {plane.localScale}");
            }
        }
        else if (assetPath.Contains("yasmariana.fbx"))
        {
            Debug.Log($"[CircuitScalePostprocessor] Correcting Yas Marina root localScale from {root.transform.localScale} to (100, 100, 100)");
            root.transform.localScale = new Vector3(100f, 100f, 100f);
        }
    }
}
