// SLOT D. Script compile pipeline (docs/sim-design.md section 3).
// Wrapper = the compile_check.sh usings + "using Random = SimShim.DetRandom;" + "public partial class Program : MyGridProgram {"
// + script text + "\n}". Compiled with: mcs -langversion:6 -target:library -optimize+ -nowarn:0169,0414,0649,0219
//   -r:SeApi.dll -r:SimShim.dll -out:out/sim/scripts/<Name>.dll
// sim/build.sh precompiles scripts/*.cs and writes out/sim/scripts/manifest.tsv: "<key>\t<Name>.dll" where
// key = ScriptKey(text). At runtime a PB's <Program> text is looked up by key; unknown text (an edited blueprint) is compiled
// on demand into out/sim/scripts/cache/<key>.dll (process-wide lock), so the PB always runs exactly the text it carries.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace SeSim.Run
{
    public static class ScriptCompiler
    {
        /// <summary>Must stay byte-identical to the wrapper in sim/build.sh.</summary>
        public const string Usings =
            "using System; using System.Collections; using System.Collections.Generic; using System.Linq; using System.Text;\n" +
            "using System.Text.RegularExpressions; using Sandbox.ModAPI.Ingame; using Sandbox.ModAPI.Interfaces; using Sandbox.Game.EntityComponents;\n" +
            "using SpaceEngineers.Game.ModAPI.Ingame; using VRage; using VRage.Collections; using VRage.Game;\n" +
            "using VRage.Game.Components; using VRage.Game.GUI.TextPanel; using VRage.Game.ModAPI.Ingame;\n" +
            "using VRage.Game.ModAPI.Ingame.Utilities; using VRage.Game.ObjectBuilders.Definitions; using VRage.ObjectBuilders; using VRageMath;\n" +
            "using Random = SimShim.DetRandom;\n";

        public static string Wrap(string scriptText) { return Usings + "public partial class Program : MyGridProgram {\n" + scriptText + "\n}\n"; }

        /// <summary>sha1 hex of the text with \r removed and trailing whitespace trimmed.</summary>
        public static string ScriptKey(string scriptText)
        {
            string t = (scriptText ?? "").Replace("\r", "").TrimEnd();
            using (var sha = SHA1.Create())
            {
                byte[] h = sha.ComputeHash(new UTF8Encoding(false).GetBytes(t));
                var sb = new StringBuilder(40);
                for (int i = 0; i < h.Length; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Runs mcs; returns null on success or the compiler errors.</summary>
        public static string Compile(string scriptText, string outDll, string simDir)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(outDll));
            Directory.CreateDirectory(dir);
            string src = Path.ChangeExtension(Path.GetFullPath(outDll), ".cs");
            File.WriteAllText(src, Wrap(scriptText), new UTF8Encoding(false));
            var psi = new ProcessStartInfo
            {
                FileName = "mcs",
                Arguments = "-langversion:6 -target:library -optimize+ -nowarn:0169,0414,0649,0219"
                          + " -r:\"" + Path.Combine(simDir, "SeApi.dll") + "\" -r:\"" + Path.Combine(simDir, "SimShim.dll") + "\""
                          + " -out:\"" + Path.GetFullPath(outDll) + "\" \"" + src + "\"",
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
            };
            try
            {
                using (var p = Process.Start(psi))
                {
                    string so = p.StandardOutput.ReadToEnd(), se = p.StandardError.ReadToEnd();
                    p.WaitForExit();
                    if (p.ExitCode == 0 && File.Exists(outDll)) return null;
                    var sb = new StringBuilder();
                    foreach (string line in (so + "\n" + se).Split('\n'))
                        if (line.IndexOf("error", StringComparison.Ordinal) >= 0) sb.AppendLine(line.Trim());
                    return sb.Length > 0 ? sb.ToString() : "mcs exit code " + p.ExitCode;
                }
            }
            catch (Exception e) { return "cannot run mcs: " + e.Message; }
        }
    }

    /// <summary>Process-wide cache key -> loaded Program Type (thread-safe).</summary>
    public static class ScriptRegistry
    {
        static readonly object Lock = new object();
        static readonly Dictionary<string, string> KeyToDll = new Dictionary<string, string>();     // key -> full dll path
        static readonly Dictionary<string, Type> KeyToType = new Dictionary<string, Type>();
        static readonly Dictionary<string, string> KeyToName = new Dictionary<string, string>();
        static readonly Dictionary<string, string> Failed = new Dictionary<string, string>();
        static string _loadedFor;

        public static void LoadManifest(string simDir)
        {
            lock (Lock) LoadManifestLocked(simDir);
        }

        static void LoadManifestLocked(string simDir)
        {
            string full = Path.GetFullPath(simDir);
            if (_loadedFor == full) return;
            _loadedFor = full;
            string mf = Path.Combine(Path.Combine(full, "scripts"), "manifest.tsv");
            if (!File.Exists(mf)) return;
            foreach (string line in File.ReadAllLines(mf))
            {
                string[] p = line.Split('\t');
                if (p.Length < 2 || p[0].Length == 0) continue;
                string path = Path.Combine(Path.Combine(full, "scripts"), p[1].Trim());
                if (!File.Exists(path)) continue;
                KeyToDll[p[0].Trim()] = path;
                KeyToName[p[0].Trim()] = Path.GetFileNameWithoutExtension(p[1].Trim());
            }
        }

        /// <summary>Key -> script file names in the manifest (for meta.json).</summary>
        public static List<KeyValuePair<string, string>> ManifestEntries(string simDir)
        {
            lock (Lock)
            {
                LoadManifestLocked(simDir);
                var l = new List<KeyValuePair<string, string>>();
                foreach (var kv in KeyToName) l.Add(kv);
                l.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
                return l;
            }
        }

        static readonly Dictionary<string, string[]> KeyToSites = new Dictionary<string, string[]>();

        public static Type GetProgramType(string scriptText, string simDir) { return GetProgramType(scriptText, simDir, null); }

        /// <summary>counting: the instrumented assembly (SimSettings.CountInstructions, sim/src/Run/IlProfile.cs); its injection sites are in SitesOf().</summary>
        public static Type GetProgramType(string scriptText, string simDir, string countMode)
        {
            string key = ScriptCompiler.ScriptKey(scriptText);
            if (!string.IsNullOrEmpty(countMode))
            {
                Type ct;
                lock (Lock) { if (KeyToType.TryGetValue(key + "|" + countMode, out ct)) return ct; }
                if (countMode == "il") GetProgramType(scriptText, simDir, null);      // the plain assembly is the input of the IL injector
                lock (Lock)
                {
                    LoadManifestLocked(simDir);
                    if (!KeyToName.ContainsKey(key)) KeyToName[key] = "custom-" + key.Substring(0, 8);
                    string plain = countMode == "il" ? KeyToDll[key] : null;
                    string idll = IlInstrument.Build(scriptText, key, simDir, countMode, plain);
                    KeyToSites[key + "|" + countMode] = IlInstrument.LoadSites(idll);
                    ct = Assembly.LoadFrom(idll).GetType("Program", true);
                    KeyToType[key + "|" + countMode] = ct;
                    return ct;
                }
            }
            lock (Lock)
            {
                Type t;
                if (KeyToType.TryGetValue(key, out t)) return t;
                string err;
                if (Failed.TryGetValue(key, out err)) throw new InvalidOperationException("script does not compile: " + err);
                LoadManifestLocked(simDir);
                string dll;
                if (!KeyToDll.TryGetValue(key, out dll))
                {
                    string cacheDir = Path.Combine(Path.Combine(Path.GetFullPath(simDir), "scripts"), "cache");
                    dll = Path.Combine(cacheDir, key + ".dll");
                    if (!File.Exists(dll))
                    {
                        err = ScriptCompiler.Compile(scriptText, dll, simDir);
                        if (err != null) { Failed[key] = err; throw new InvalidOperationException("script does not compile: " + err); }
                    }
                    KeyToDll[key] = dll;
                    KeyToName[key] = "custom-" + key.Substring(0, 8);
                }
                Assembly asm = Assembly.LoadFrom(dll);
                t = asm.GetType("Program", true);
                KeyToType[key] = t;
                return t;
            }
        }

        public static string[] SitesOf(string scriptText, string countMode)
        {
            string key = ScriptCompiler.ScriptKey(scriptText) + "|" + countMode;
            lock (Lock) { string[] s; return KeyToSites.TryGetValue(key, out s) ? s : null; }
        }

        public static string NameOf(string scriptText)   // "HornetDroneAI" for reports
        {
            string key = ScriptCompiler.ScriptKey(scriptText);
            lock (Lock)
            {
                string n;
                return KeyToName.TryGetValue(key, out n) ? n : "custom-" + key.Substring(0, 8);
            }
        }
    }
}
