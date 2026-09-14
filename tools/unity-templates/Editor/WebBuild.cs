// Copy to MimasClient/Assets/_Game/Editor/WebBuild.cs (inside an Editor-only asmdef).
// Usage: unity run MimasClient -- -executeMethod Mimas.Client.Editor.WebBuild.Build -logFile build.log
//    or: menu Mimas > Build Web
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Mimas.Client.Editor
{
    public static class WebBuild
    {
        private const string OutputDir = "../Build/Web";

        [MenuItem("Mimas/Build Web")]
        public static void Build()
        {
            var scenes = EditorBuildSettings.scenes;
            var options = new BuildPlayerOptions
            {
                scenes = System.Array.ConvertAll(scenes, s => s.path),
                locationPathName = OutputDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };

            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.nameFilesAsHashes = true;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.None;
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.High);

            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            long bytes = 0;
            var buildDir = Path.Combine(OutputDir, "Build");
            if (Directory.Exists(buildDir))
                foreach (var f in Directory.GetFiles(buildDir)) bytes += new FileInfo(f).Length;

            Debug.Log($"[WebBuild] result={summary.result} time={summary.totalTime} buildFolderBytes={bytes} ({bytes / 1024f / 1024f:F2} MB)");
            if (summary.result != BuildResult.Succeeded && Application.isBatchMode)
                EditorApplication.Exit(1);
        }
    }
}
