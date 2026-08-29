using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace GridSense.Rendering
{
    /// <summary>
    /// Step 6.6: URP Post-Processing & FSR Upscaling Runtime Manager.
    /// Controls render scale, AMD FidelityFX Super Resolution (FSR 1.0),
    /// and lightweight post-processing volume effects calibrated for integrated graphics.
    /// </summary>
    public class PostProcessingAndFSRManager : MonoBehaviour
    {
        public enum FSRPreset
        {
            Native = 0,         // Render Scale 1.00 (Native resolution + FSR RCAS edge sharpening)
            Quality = 1,        // Render Scale 0.85 (85% linear resolution, ~72% fill-rate load)
            Balanced = 2,       // Render Scale 0.77 (77% linear resolution, ~60% fill-rate load)
            Performance = 3,    // Render Scale 0.67 (67% linear resolution, ~45% fill-rate load, 720p->1080p)
            UltraPerformance = 4 // Render Scale 0.50 (50% linear resolution, ~25% fill-rate load)
        }

        [Header("FSR Upscaling Configuration")]
        [SerializeField] private FSRPreset currentPreset = FSRPreset.Balanced;
        [Range(0.0f, 1.0f)]
        [SerializeField] private float fsrSharpness = 0.85f;

        [Header("Post-Processing Volume")]
        [SerializeField] private Volume postProcessVolume;

        private UniversalRenderPipelineAsset urpAsset;

        public FSRPreset CurrentPreset => currentPreset;
        public float CurrentRenderScale => urpAsset != null ? urpAsset.renderScale : 1.0f;
        public float FSRSharpness => fsrSharpness;

        private void Awake()
        {
            urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            ApplyPreset(currentPreset);
        }

        /// <summary>
        /// Applies an FSR upscaling quality preset to the active URP asset.
        /// </summary>
        public void ApplyPreset(FSRPreset preset)
        {
            currentPreset = preset;
            if (urpAsset == null)
            {
                urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            }

            if (urpAsset == null)
            {
                Debug.LogWarning("[PostProcessingAndFSRManager] No active UniversalRenderPipelineAsset found in GraphicsSettings.");
                return;
            }

            float targetScale = 1.0f;
            switch (preset)
            {
                case FSRPreset.Native:
                    targetScale = 1.00f;
                    break;
                case FSRPreset.Quality:
                    targetScale = 0.85f;
                    break;
                case FSRPreset.Balanced:
                    targetScale = 0.77f;
                    break;
                case FSRPreset.Performance:
                    targetScale = 0.67f; // 720p internal resolution for 1080p target
                    break;
                case FSRPreset.UltraPerformance:
                    targetScale = 0.50f;
                    break;
            }

            urpAsset.upscalingFilter = UpscalingFilterSelection.FSR;
            urpAsset.fsrOverrideSharpness = true;
            urpAsset.fsrSharpness = fsrSharpness;
            urpAsset.renderScale = targetScale;

            Debug.Log($"[PostProcessingAndFSRManager] Configured FSR: Preset={preset}, RenderScale={targetScale:F2}, Sharpness={fsrSharpness:F2}, PixelBudgetReduction={((1f - targetScale * targetScale) * 100f):F1}%");
        }

        /// <summary>
        /// Updates FSR edge-sharpening factor (0.0 = soft, 1.0 = sharpest).
        /// </summary>
        public void SetFSRSharpness(float sharpness)
        {
            fsrSharpness = Mathf.Clamp01(sharpness);
            if (urpAsset != null)
            {
                urpAsset.fsrOverrideSharpness = true;
                urpAsset.fsrSharpness = fsrSharpness;
            }
        }

        /// <summary>
        /// Toggles post-processing volume weight.
        /// </summary>
        public void SetPostProcessingEnabled(bool enabled)
        {
            if (postProcessVolume != null)
            {
                postProcessVolume.weight = enabled ? 1.0f : 0.0f;
            }
        }

        /// <summary>
        /// Returns measured internal rendering resolution given the display viewport.
        /// </summary>
        public Vector2Int GetInternalResolution(int displayWidth, int displayHeight)
        {
            float scale = CurrentRenderScale;
            return new Vector2Int(Mathf.RoundToInt(displayWidth * scale), Mathf.RoundToInt(displayHeight * scale));
        }
    }
}
