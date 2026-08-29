using System;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public static class DiagnoseCarPhysicsAndTrackColliders
{
    private static readonly string ArtifactDir = "C:/Users/AKSHIT JAIN/.gemini/antigravity-ide/brain/50ef8d3f-4e21-401d-8ac1-c2b632068b8d";

    public static void Run()
    {
        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Circuits/Bahrain_Occlusion.unity", OpenSceneMode.Single);

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== TRACK & CAR PHYSICS COLLIDER AUDIT ===");

        // 1. Audit Track Colliders
        var allColliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include);
        sb.AppendLine($"Total Colliders in Scene: {allColliders.Length}");
        foreach (var c in allColliders)
        {
            sb.AppendLine($"  Collider: '{c.name}' Type: {c.GetType().Name}, Enabled: {c.enabled}, IsTrigger: {c.isTrigger}, Bounds: {c.bounds}");
        }

        // 2. Audit Car Physics
        GameObject car = GameObject.Find("F1_PlayerCar");
        if (car != null)
        {
            sb.AppendLine($"\nF1_PlayerCar: Pos={car.transform.position}, Rot={car.transform.rotation.eulerAngles}");
            var rb = car.GetComponent<Rigidbody>();
            if (rb != null)
            {
                sb.AppendLine($"  Rigidbody: Mass={rb.mass}, UseGravity={rb.useGravity}, IsKinematic={rb.isKinematic}, COM={rb.centerOfMass}");
            }
            else
            {
                sb.AppendLine("  Rigidbody: NONE!");
            }

            var wcs = car.GetComponentsInChildren<WheelCollider>(true);
            sb.AppendLine($"  WheelColliders found: {wcs.Length}");
            foreach (var wc in wcs)
            {
                sb.AppendLine($"    WC '{wc.name}': Pos={wc.transform.position}, Radius={wc.radius}, SuspDist={wc.suspensionDistance}, Spring={wc.suspensionSpring.spring}, Damper={wc.suspensionSpring.damper}");
            }

            // Raycast Down from Car
            Ray ray = new Ray(car.transform.position + Vector3.up * 2.0f, Vector3.down);
            RaycastHit[] hits = Physics.RaycastAll(ray, 100.0f);
            sb.AppendLine($"\nRaycast down from {ray.origin} (max 100m): Hits={hits.Length}");
            foreach (var hit in hits)
            {
                sb.AppendLine($"  Hit '{hit.collider.name}' at point {hit.point}, distance={hit.distance}, normal={hit.normal}");
            }
        }
        else
        {
            sb.AppendLine("F1_PlayerCar: NOT FOUND IN SCENE!");
        }

        string outPath = Path.Combine(ArtifactDir, "physics_collider_audit.txt");
        File.WriteAllText(outPath, sb.ToString());
        UnityEngine.Debug.Log(sb.ToString());
    }
}
