using UnityEditor;
using UnityEditor.Build.Reporting;

public static class BuildGridSensePlayer
{
    public static void Run()
    {
        // Fix circuit textures and materials before building
        FixCircuitTexturesAndMaterials.FixAll();

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[]
            {
                "Assets/Scenes/Circuits/Bahrain_Occlusion.unity",
                "Assets/Scenes/Circuits/Shanghai_Occlusion.unity",
                "Assets/Scenes/Circuits/Suzuka_Occlusion.unity",
                "Assets/Scenes/Circuits/YasMarina_Occlusion.unity"
            },
            locationPathName = "Build/GridSense.exe",
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        int exitCode = report.summary.result == BuildResult.Succeeded ? 0 : 1;
        UnityEngine.Debug.Log(exitCode == 0 ? $"GridSense player build succeeded: {report.summary.totalSize} bytes." : $"GridSense player build failed: {report.summary.result}");
        EditorApplication.Exit(exitCode);
    }
}