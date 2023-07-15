using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityBuilderAction.Input;
using UnityBuilderAction.Reporting;
using UnityBuilderAction.Versioning;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;

namespace UnityBuilderAction
{
    static class Builder
    {
        public static void BuildProject()
        {
            // Gather values from args
            var options = ArgumentsParser.GetValidatedOptions();

            // Gather values from project
            var scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(s => s.path).ToArray();

            // Get all buildOptions from options
            BuildOptions buildOptions = BuildOptions.None;
            foreach (string buildOptionString in Enum.GetNames(typeof(BuildOptions)))
            {
                if (options.ContainsKey(buildOptionString))
                {
                    BuildOptions buildOptionEnum = (BuildOptions)Enum.Parse(typeof(BuildOptions), buildOptionString);
                    buildOptions |= buildOptionEnum;
                }
            }

#if UNITY_2021_2_OR_NEWER
            // Determine subtarget
            StandaloneBuildSubtarget buildSubtarget;
            if (!options.TryGetValue("standaloneBuildSubtarget", out var subtargetValue) || !Enum.TryParse(subtargetValue, out buildSubtarget))
            {
                buildSubtarget = default;
            }
#endif

            // Define BuildPlayer Options
            var buildPlayerOptions = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = options["customBuildPath"],
                target = (BuildTarget)Enum.Parse(typeof(BuildTarget), options["buildTarget"]),
                options = buildOptions,
#if UNITY_2021_2_OR_NEWER
                subtarget = (int)buildSubtarget
#endif
            };

            // Set version for this build
            VersionApplicator.SetVersion(options["buildVersion"]);

            // Apply Android settings
            if (buildPlayerOptions.target == BuildTarget.Android)
            {
                VersionApplicator.SetAndroidVersionCode(options["androidVersionCode"]);
                AndroidSettings.Apply(options);
            }

            // Execute default AddressableAsset content build, if the package is installed.
            // Version defines would be the best solution here, but Unity 2018 doesn't support that,
            // so we fall back to using reflection instead.
            var addressableAssetSettingsType = Type.GetType(
              "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings,Unity.Addressables.Editor");
            if (addressableAssetSettingsType != null)
            {
                // ReSharper disable once PossibleNullReferenceException, used from try-catch
                try
                {
                    addressableAssetSettingsType.GetMethod("CleanPlayerContent", BindingFlags.Static | BindingFlags.Public)
                          .Invoke(null, new object[] { null });
                    addressableAssetSettingsType.GetMethod("BuildPlayerContent", new Type[0]).Invoke(null, new object[0]);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to run default addressables build:\n{e}");
                }
            }

            // Apply extra configuration
            ExtraConfiguration(buildPlayerOptions, options);

            // Perform build
            BuildReport buildReport = BuildPipeline.BuildPlayer(buildPlayerOptions);

            // Summary
            BuildSummary summary = buildReport.summary;
            StdOutReporter.ReportSummary(summary);

            // Result
            BuildResult result = summary.result;
            StdOutReporter.ExitWithResult(result);
        }

        public static void ExtraConfiguration(BuildPlayerOptions buildPlayerOptions, Dictionary<string, string> options)
        {
            if (buildPlayerOptions.target == BuildTarget.StandaloneOSX)
            {
                PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
                SetArchitectureForPlatform(BuildTarget.StandaloneOSX, (OSArchitecture)Enum.Parse(typeof(OSArchitecture), options["architecture"]));
            }
            else if (buildPlayerOptions.target == BuildTarget.StandaloneWindows64)
            {
                SetArchitectureForPlatform(BuildTarget.StandaloneWindows64, (OSArchitecture)Enum.Parse(typeof(OSArchitecture), options["architecture"]));
            }
            else if (buildPlayerOptions.target == BuildTarget.StandaloneLinux64)
            {
                var buildTargetSettings = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
                var pluginSettings = buildTargetSettings.AssignedSettings;

                bool removedOpenXRPlugin = XRPackageMetadataStore.RemoveLoader(pluginSettings, "UnityEngine.XR.OpenXR.OpenXRLoader", BuildTargetGroup.Standalone);
                if (!removedOpenXRPlugin)
                {
                    Console.WriteLine("Failed to remove OpenXR Plugin");
                    EditorApplication.Exit(150);
                }

                bool assignedOculusXRPlugin = XRPackageMetadataStore.AssignLoader(pluginSettings, "Unity.XR.Oculus.OculusLoader", BuildTargetGroup.Standalone);
                if (!assignedOculusXRPlugin)
                {
                    Console.WriteLine("Failed to assign OculusXR Plugin");
                    EditorApplication.Exit(151);
                }
            }
        }

        // modified from Unity class DesktopStandaloneBuildWindowExtension
        public static void SetArchitectureForPlatform(BuildTarget buildTarget, OSArchitecture architecture)
        {
            EditorUserBuildSettings.SetPlatformSettings(BuildPipeline.GetBuildTargetName(buildTarget), "Architecture", architecture.ToString().ToLower());
        }
    }
}
