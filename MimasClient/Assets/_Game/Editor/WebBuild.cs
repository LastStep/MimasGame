// The Web build, from the command line.
//
//   unity build MimasClient --execute-method Mimas.Client.Editor.WebBuild.Build      --output-path Build/Web
//   unity build MimasClient --execute-method Mimas.Client.Editor.WebBuild.BuildDebug --output-path Build/WebDebug
//
// or the menu: Mimas > Build Web (Release) / Mimas > Build Web (Debug).
//
// This deliberately drives BuildPipeline rather than a build profile, so the settings that matter are
// stated once, here, in code a reviewer can read — and so a build needs no Editor open and no
// protected `.asset` edit. `docs/hosting.md` depends on every one of them.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Mimas.Client.Editor
{
    public static class WebBuild
    {
        /// <summary>Relative to the Unity project folder, which is the working directory in batch mode.</summary>
        private const string DefaultOutputDir = "Build/Web";

        /// <summary>
        /// What the shipped build uses, and what ProjectSettings.asset is left reading after any build
        /// here — so the committed value is the decided one rather than whichever flavour ran last.
        /// `None` means `try`/`catch` does not work at all: a malformed frame in NetClient, a failed
        /// PlayerPrefs.Save, a bad catalogue or a bad map would abort the page instead of logging.
        /// Rohan chose Unity's own default over that on 17 Sep 2026 (T-0003).
        /// </summary>
        private const WebGLExceptionSupport ShippedExceptionSupport =
            WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;

        [MenuItem("Mimas/Build Web (Release)")]
        public static void Build()
        {
            Run(development: false, ShippedExceptionSupport, DefaultOutputDir);
        }

        /// <summary>
        /// The build to debug with: Development plus full stack traces, so a failure in the browser
        /// console names a C# method instead of a wasm offset. It embeds the com.unity.pipeline
        /// runtime server, so it must never be hosted anywhere but this machine.
        /// </summary>
        [MenuItem("Mimas/Build Web (Debug)")]
        public static void BuildDebug()
        {
            Run(development: true, WebGLExceptionSupport.FullWithStacktrace, "Build/WebDebug");
        }

        private static void Run(bool development, WebGLExceptionSupport exceptions, string fallbackOutput)
        {
            string output = OutputFromCommandLine() ?? fallbackOutput;

            // Only the enabled scenes: a disabled row in EditorBuildSettings means "not shipped", and
            // shipping it anyway shifts every scene index after it.
            var scenePaths = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
                if (scene.enabled) scenePaths.Add(scene.path);

            bool failed = false;

            try
            {
                PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
                PlayerSettings.WebGL.decompressionFallback = false;
                PlayerSettings.WebGL.nameFilesAsHashes = true;
                PlayerSettings.WebGL.dataCaching = true;
                PlayerSettings.WebGL.threadsSupport = false;   // no SharedArrayBuffer, so no COOP/COEP headers
                PlayerSettings.WebGL.exceptionSupport = exceptions;
                PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.High);

                var options = new BuildPlayerOptions
                {
                    scenes = scenePaths.ToArray(),
                    locationPathName = output,
                    target = BuildTarget.WebGL,
                    options = development ? BuildOptions.Development : BuildOptions.None,
                };

                Debug.Log($"[WebBuild] {(development ? "DEBUG" : "RELEASE")} -> {Path.GetFullPath(output)}; "
                    + $"scenes=[{string.Join(", ", scenePaths)}]; exceptions={exceptions}; "
                    + "compression=Brotli; fallback=off; stripping=High");

                BuildReport report = BuildPipeline.BuildPlayer(options);
                BuildSummary summary = report.summary;

                long bytes = 0;
                string buildDir = Path.Combine(output, "Build");
                if (Directory.Exists(buildDir))
                    foreach (var f in Directory.GetFiles(buildDir)) bytes += new FileInfo(f).Length;

                Debug.Log($"[WebBuild] result={summary.result} time={summary.totalTime} "
                    + $"errors={summary.totalErrors} warnings={summary.totalWarnings} "
                    + $"buildFolderBytes={bytes} ({bytes / 1024f / 1024f:F2} MB)");

                if (summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError($"[WebBuild] FAILED: {summary.result}");
                    failed = true;
                }
            }
            finally
            {
                // Not "whatever it was before": the decided value, so a debug build cannot leave the
                // repo recording FullWithStacktrace and a release build cannot re-record None.
                //
                // SaveAssets() alone does not write ProjectSettings.asset — the first run of this proved
                // it, leaving the file still reading None after a build that had set it. Player settings
                // are a separate serialized object and need saving by name.
                PlayerSettings.WebGL.exceptionSupport = ShippedExceptionSupport;
                AssetDatabase.SaveAssets();
                EditorApplication.ExecuteMenuItem("File/Save Project");
            }

            // After the finally, never inside it: EditorApplication.Exit terminates the process on the
            // spot, so exiting from the try skips the restore above entirely.
            if (failed && Application.isBatchMode) EditorApplication.Exit(1);
        }

        /// <summary>
        /// `unity build --output-path X` forwards `-buildOutput X`, and says the executed method owns
        /// honouring it. Without this the flag is silently ignored and the build lands somewhere else.
        /// </summary>
        private static string OutputFromCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], "-buildOutput", StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }
    }
}
