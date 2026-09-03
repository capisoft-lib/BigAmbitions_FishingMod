#if UNITY_EDITOR
using System;
using System.IO;
using BAModTemplate.Editor;
using UnityEditor;
using UnityEngine;

namespace FishingMod.Editor
{
    public static class FishingModBuild
    {
        public static void Run()
        {
            if (!Application.isBatchMode)
                throw new InvalidOperationException("Use FishingMod/tools/build-official.ps1.");
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BA_MOD_BUILD_CLI")))
                throw new InvalidOperationException("Unset BA_MOD_BUILD_CLI; FishingMod official build must not install.");

            Debug.Log("[FishingMod.Build] Official Mod Builder; installation=false.");
            ModPackager.JobChanged += OnJobChanged;
            ModBuildCli.BuildMod("FishingMod", install: false);
        }

        private static void OnJobChanged(BuildJob job)
        {
            if (job.Mod.Manifest.ModId != "FishingMod" || !job.IsTerminal) return;
            ModPackager.JobChanged -= OnJobChanged;
            if (job.State != BuildState.Done) return;

            try
            {
                string source = Path.Combine(Application.dataPath, "Mods", "FishingMod");
                foreach (string name in new[] { "ModManifest.asset", "README.md", "CHANGELOG.md" })
                    File.Copy(Path.Combine(source, name), Path.Combine(job.OutputDirectoryAbsolute, name), true);

                string dll = Path.Combine(job.OutputDirectoryAbsolute, "FishingMod.dll");
                if (!File.Exists(dll) || new FileInfo(dll).Length < 8192)
                    throw new InvalidOperationException("FishingMod.dll is missing or unexpectedly small.");
                if (Directory.GetFiles(job.OutputDirectoryAbsolute, "*.dll", SearchOption.AllDirectories).Length != 1)
                    throw new InvalidOperationException("FishingMod must package exactly one DLL.");

                int checks = FishingModChecks.Run();
                if (checks < 10) throw new InvalidOperationException("FishingMod Unity checks were incomplete.");
                Debug.Log("[FishingMod.Verify] Official package verified; installation=false.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorApplication.Exit(1);
            }
        }
    }
}
#endif
