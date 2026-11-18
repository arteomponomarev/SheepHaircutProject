using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace ShearAndGrow.Shearing.Editor
{
    /// <summary>
    /// Fast native compile checks without a full player build:
    ///  • Android: javac (Unity's bundled JDK) against android.jar of the highest installed platform in Unity's SDK.
    ///  • iOS: clang -fsyntax-only against the iPhoneOS SDK (requires Xcode on macOS).
    /// </summary>
    public static class HapticsNativeCompileCheck
    {
        [MenuItem("Shear & Grow/Haptics/Compile-check Native Plugins")]
        public static void RunFromMenu() => Debug.Log(Run());

        public static string Run()
        {
            var sb = new StringBuilder("[Haptics] Native compile check\n");
            sb.Append(CheckAndroid()).Append('\n');
            sb.Append(CheckIOS());
            return sb.ToString();
        }

        static string CheckAndroid()
        {
            string sdk = UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath;
            string jdk = UnityEditor.Android.AndroidExternalToolsSettings.jdkRootPath;
            if (string.IsNullOrEmpty(sdk) || string.IsNullOrEmpty(jdk)) return "Android: SDK/JDK path not configured in Unity.";
            string platforms = Path.Combine(sdk, "platforms");
            if (!Directory.Exists(platforms)) return "Android: no platforms folder in " + sdk;
            string jar = Directory.GetDirectories(platforms)
                .Select(d => Path.Combine(d, "android.jar")).Where(File.Exists)
                .OrderByDescending(p => ApiLevel(Path.GetFileName(Path.GetDirectoryName(p)))).FirstOrDefault();
            if (jar == null) return "Android: android.jar not found under " + platforms;
            string javac = Path.Combine(jdk, "bin", "javac");
            if (!File.Exists(javac) && File.Exists(javac + ".exe")) javac += ".exe";
            string outDir = Path.Combine(Path.GetTempPath(), "ShearHapticsJavac");
            Directory.CreateDirectory(outDir);
            string src = Path.GetFullPath(HapticsBuildSetup.AndroidPluginPath);
            int code = Exec(javac, $"-Xlint:-options -source 8 -target 8 -classpath \"{jar}\" -d \"{outDir}\" \"{src}\"", out string output);
            return $"Android: javac exit {code} against {Path.GetFileName(Path.GetDirectoryName(jar))}{(code == 0 ? " — OK" : " — FAILED")}\n{output}".TrimEnd();
        }

        static int ApiLevel(string folder)
        {
            string digits = new string(folder.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out int v) ? v : 0;
        }

        static string CheckIOS()
        {
            if (!File.Exists("/usr/bin/xcrun")) return "iOS: xcrun not found (needs macOS with Xcode) — not checked.";
            if (Exec("/usr/bin/xcrun", "--sdk iphoneos --show-sdk-path", out string sdkPath) != 0 || string.IsNullOrWhiteSpace(sdkPath))
                return "iOS: iPhoneOS SDK not available (install Xcode / run xcode-select) — not checked.\n" + sdkPath;
            sdkPath = sdkPath.Trim().Split('\n').Last().Trim();
            string src = Path.GetFullPath(HapticsBuildSetup.IOSPluginPath);
            int code = Exec("/usr/bin/xcrun",
                $"--sdk iphoneos clang++ -fsyntax-only -x objective-c++ -fobjc-arc -std=c++17 -target arm64-apple-ios15.0 -isysroot \"{sdkPath}\" \"{src}\"",
                out string output);
            return $"iOS: clang exit {code} (arm64-apple-ios15.0, ARC){(code == 0 ? " — OK" : " — FAILED")}\n{output}".TrimEnd();
        }

        static int Exec(string file, string args, out string output)
        {
            try
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    string e = p.StandardError.ReadToEnd();
                    p.WaitForExit(120000);
                    output = (o + e).Trim();
                    return p.HasExited ? p.ExitCode : -1;
                }
            }
            catch (System.Exception ex)
            {
                output = ex.Message;
                return -2;
            }
        }
    }
}
