using System.Text;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace ShearAndGrow.Shearing.Editor
{
    /// <summary>
    /// Keeps the native haptics plugins correctly configured:
    ///  • ShearHaptics.mm  — iOS only, compiled with ARC, CoreHaptics framework dependency
    ///  • ShearHaptics.java — Android only (compiled into the Gradle project)
    /// and makes sure CoreHaptics is linked into UnityFramework of every generated Xcode project.
    /// </summary>
    public sealed class HapticsBuildSetup : AssetPostprocessor
    {
        public const string IOSPluginPath = "Assets/Plugins/iOS/ShearHaptics.mm";
        public const string AndroidPluginPath = "Assets/Plugins/Android/ShearHaptics.java";

        void OnPreprocessAsset()
        {
            if (assetPath == IOSPluginPath || assetPath == AndroidPluginPath)
                Configure(assetImporter as PluginImporter, assetPath == IOSPluginPath);
        }

        static bool Configure(PluginImporter importer, bool ios)
        {
            if (importer == null) return false;
            importer.SetCompatibleWithAnyPlatform(false);
            importer.SetCompatibleWithEditor(false);
            importer.SetCompatibleWithPlatform(BuildTarget.iOS, ios);
            importer.SetCompatibleWithPlatform(BuildTarget.Android, !ios);
            if (ios)
            {
                importer.SetPlatformData(BuildTarget.iOS, "FrameworkDependencies", "CoreHaptics;");
                importer.SetPlatformData(BuildTarget.iOS, "CompileFlags", "-fobjc-arc");
            }
            return true;
        }

        [MenuItem("Shear & Grow/Haptics/Validate & Fix Native Plugin Setup")]
        public static void ValidateAndFix()
        {
            var sb = new StringBuilder("[Haptics] Native plugin setup:\n");
            foreach (var (path, ios) in new[] { (IOSPluginPath, true), (AndroidPluginPath, false) })
            {
                var importer = AssetImporter.GetAtPath(path) as PluginImporter;
                if (importer == null) { sb.Append("  MISSING ").Append(path).Append('\n'); continue; }
                Configure(importer, ios);
                importer.SaveAndReimport();
                sb.Append("  ").Append(path).Append(": iOS=").Append(importer.GetCompatibleWithPlatform(BuildTarget.iOS))
                  .Append(" Android=").Append(importer.GetCompatibleWithPlatform(BuildTarget.Android))
                  .Append(" Editor=").Append(importer.GetCompatibleWithEditor());
                if (ios)
                    sb.Append(" frameworks=").Append(importer.GetPlatformData(BuildTarget.iOS, "FrameworkDependencies"))
                      .Append(" flags=").Append(importer.GetPlatformData(BuildTarget.iOS, "CompileFlags"));
                sb.Append('\n');
            }
            sb.Append("  iOS min version: ").Append(PlayerSettings.iOS.targetOSVersionString).Append(" (Core Haptics needs 13.0+)\n");
            sb.Append("  Android min SDK: ").Append(PlayerSettings.Android.minSdkVersion).Append(" (VibrationEffect needs 26+)\n");
            Debug.Log(sb.ToString());
        }

        [PostProcessBuild(100)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;
            string projPath = UnityEditor.iOS.Xcode.PBXProject.GetPBXProjectPath(pathToBuiltProject);
            var proj = new UnityEditor.iOS.Xcode.PBXProject();
            proj.ReadFromFile(projPath);
            string frameworkTarget = proj.GetUnityFrameworkTargetGuid();
            proj.AddFrameworkToProject(frameworkTarget, "CoreHaptics.framework", false);
            proj.WriteToFile(projPath);
            Debug.Log("[Haptics] CoreHaptics.framework linked to UnityFramework.");
        }
    }
}
