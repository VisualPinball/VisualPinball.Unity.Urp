using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VisualPinball.Unity.Urp
{
    /// <summary>Keeps STP's runtime resources when users can select it after the build.</summary>
    public sealed class UrpUpscalerBuildResources : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        // After CorePreprocessBuild (+50), before URPPreprocessBuild (+100) snapshots the list.
        public int callbackOrder => int.MinValue + 75;
        private static UniversalRenderPipelineAsset _retentionAsset;
        private static List<RenderPipelineAsset> _buildAssets;

        public void OnPreprocessBuild(BuildReport report)
        {
            Cleanup();
            var assets = CoreBuildData.instance.renderPipelineAssets;
            UniversalRenderPipelineAsset source = null;
            foreach (var asset in assets) {
                if (asset is UniversalRenderPipelineAsset urp) {
                    if (urp.isStpUsed) return;
                    source ??= urp;
                }
            }
            if (source == null) return;
            // Only the transient build-data list changes. Project assets and quality tiers stay untouched.
            _retentionAsset = Object.Instantiate(source);
            _retentionAsset.name = "URP runtime STP resource retention";
            _retentionAsset.hideFlags = HideFlags.HideAndDontSave;
            _retentionAsset.upscalingFilter = UpscalingFilterSelection.STP;
#if ENABLE_UPSCALER_FRAMEWORK
            _retentionAsset.upscalerName = "Spatial-Temporal Post-Processing";
#endif
            _buildAssets = assets;
            assets.Add(_retentionAsset);
            EditorApplication.update += CleanupAfterBuild;
            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
        }

        public void OnPostprocessBuild(BuildReport report) => Cleanup();

        private static void CleanupAfterBuild()
        {
            // Postprocess callbacks may be skipped on failure/cancellation.
            if (!BuildPipeline.isBuildingPlayer) Cleanup();
        }

        private static void Cleanup()
        {
            EditorApplication.update -= CleanupAfterBuild;
            AssemblyReloadEvents.beforeAssemblyReload -= Cleanup;
            if (_buildAssets != null && _retentionAsset != null) _buildAssets.Remove(_retentionAsset);
            _buildAssets = null;
            if (_retentionAsset != null) Object.DestroyImmediate(_retentionAsset);
            _retentionAsset = null;
        }
    }
}
