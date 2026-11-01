// Load-time check shared by the gPC Grasshopper plug-ins: keep every copy identical to the reference copy in
// BridGHe/src/BridGHe.Grasshopper/Helpers/PluginBuildCheck.cs, tested by BridGHe/tests/BridGHe.Smoke/PluginBuildCheckTests.cs.
// C# 5: Meerkat compiles with the .NET Framework csc, which rejects even a skipped #nullable; the pragma below silences the
// nullable warnings of the plug-ins that enable them.
#if NET
#pragma warning disable 8600, 8601, 8602, 8603, 8604, 8618, 8619, 8625
#endif
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Grasshopper.Kernel;

namespace Gpc.PluginChecks
{
    /// <summary>
    /// Checks at load that Rhino runs the plug-in of its checkout. First, the bin must be built from the commit the checkout
    /// is on: work merged or pulled into the checkout reaches Rhino only after a rebuild. Second, the gPC libraries shared
    /// with other plug-ins (GPC*, DelaunayMesh, TensileLib) must be the copies shipped next to the plug-in, because plug-ins
    /// in one Rhino share the first loaded copy of each assembly. Differences are written to the Rhino command line and added
    /// as a warning to the plug-in's components after each solution.
    /// </summary>
    public static class PluginBuildCheck
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, string> Hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static Assembly plugin;
        private static string pluginName, pluginFolder, stale, foreign;

        /// <summary>The warning for the plug-in's components, or null when the plug-in is current.</summary>
        public static string Warning
        {
            get { lock (Sync) return stale == null ? foreign : foreign == null ? stale : stale + " " + foreign; }
        }

        /// <summary>Called once from the plug-in's GH_AssemblyPriority. Never throws.</summary>
        public static void Start(Assembly pluginAssembly, string name, bool sharedLibraries = true)
        {
            lock (Sync) { if (plugin != null) return; plugin = pluginAssembly; pluginName = name; }
            try
            {
                pluginFolder = Path.GetDirectoryName(pluginAssembly.Location);
                if (string.IsNullOrEmpty(pluginFolder)) return;
                string head, branch;
                string root = FindRepository(pluginFolder, out head, out branch);
                if (root != null)
                {
                    string built = BuildCommit(pluginAssembly);
                    if (built == null) Write("commit di compilazione non registrato nel plug-in; bin non verificabile.");
                    else
                    {
                        string message = StaleBin(name, built, root, head, branch);
                        if (message != null) { lock (Sync) stale = message; Write(message); }
                    }
                }
                if (sharedLibraries)
                {
                    AppDomain.CurrentDomain.AssemblyLoad += (sender, e) => { if (IsShared(e.LoadedAssembly.GetName().Name)) CheckLoaded(); };
                    CheckLoaded();
                }
                var server = global::Grasshopper.Instances.DocumentServer;
                if (server != null)
                {
                    foreach (GH_Document document in server) Attach(document, pluginAssembly, () => Warning);
                    server.DocumentAdded += (sender, document) => Attach(document, pluginAssembly, () => Warning);
                }
            }
            catch (Exception e) { Write("verifica del bin non riuscita: " + e.Message); }
        }

        /// <summary>After every solution of the document, adds the current warning to the plug-in's components.</summary>
        public static void Attach(GH_Document document, Assembly pluginAssembly, Func<string> warning)
        {
            if (document == null) return;
            document.SolutionEnd += (sender, e) =>
            {
                string text = warning();
                if (text != null) ShowOn(document, pluginAssembly, text);
            };
        }

        /// <summary>Adds the warning, once, to every component of the plug-in in the document.</summary>
        public static void ShowOn(GH_Document document, Assembly pluginAssembly, string warning)
        {
            bool added = false;
            foreach (IGH_DocumentObject item in document.Objects)
            {
                var component = item as GH_Component;
                if (component == null || component.GetType().Assembly != pluginAssembly) continue;
                if (component.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Contains(warning)) continue;
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
                added = true;
            }
            if (added) try { global::Grasshopper.Instances.RedrawCanvas(); } catch (Exception) { }
        }

        /// <summary>The commit the assembly was built from: AssemblyMetadata("BuildCommit") or the SourceLink suffix of the informational version.</summary>
        public static string BuildCommit(Assembly assembly)
        {
            foreach (var metadata in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
                if (metadata.Key == "BuildCommit" && IsCommit(metadata.Value)) return metadata.Value.ToLowerInvariant();
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            return version == null ? null : CommitFromVersion(version.InformationalVersion);
        }

        public static string CommitFromVersion(string informationalVersion)
        {
            int plus = informationalVersion == null ? -1 : informationalVersion.LastIndexOf('+');
            string commit = plus < 0 ? null : informationalVersion.Substring(plus + 1);
            return IsCommit(commit) ? commit.ToLowerInvariant() : null;
        }

        /// <summary>The checkout that contains the folder (git directory, worktree or submodule), with the commit and branch of its HEAD; null outside a checkout.</summary>
        public static string FindRepository(string folder, out string head, out string branch)
        {
            head = null; branch = null;
            var directory = string.IsNullOrEmpty(folder) ? null : new DirectoryInfo(folder);
            for (int level = 0; directory != null && level < 10; level++, directory = directory.Parent)
            {
                string dotGit = Path.Combine(directory.FullName, ".git"), gitDir = null;
                if (Directory.Exists(dotGit)) gitDir = dotGit;
                else if (File.Exists(dotGit))
                {
                    string line = File.ReadAllText(dotGit).Trim();
                    if (line.StartsWith("gitdir:", StringComparison.Ordinal)) gitDir = Path.GetFullPath(Path.Combine(directory.FullName, line.Substring(7).Trim()));
                }
                if (gitDir == null || !File.Exists(Path.Combine(gitDir, "HEAD"))) continue;
                string common = gitDir, commonFile = Path.Combine(gitDir, "commondir");
                if (File.Exists(commonFile)) common = Path.GetFullPath(Path.Combine(gitDir, File.ReadAllText(commonFile).Trim()));
                string text = File.ReadAllText(Path.Combine(gitDir, "HEAD")).Trim();
                if (text.StartsWith("ref:", StringComparison.Ordinal))
                {
                    string reference = text.Substring(4).Trim();
                    branch = reference.StartsWith("refs/heads/", StringComparison.Ordinal) ? reference.Substring(11) : reference;
                    head = ReadReference(gitDir, common, reference);
                }
                else if (IsCommit(text)) head = text.ToLowerInvariant();
                return directory.FullName;
            }
            return null;
        }

        private static string ReadReference(string gitDir, string common, string reference)
        {
            foreach (string root in new[] { gitDir, common })
            {
                string path = Path.Combine(root, reference.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(path)) { string value = File.ReadAllText(path).Trim(); return IsCommit(value) ? value.ToLowerInvariant() : null; }
            }
            string packed = Path.Combine(common, "packed-refs");
            if (File.Exists(packed))
                foreach (string line in File.ReadAllLines(packed))
                    if (line.Length > 41 && line[40] == ' ' && line.Substring(41) == reference && IsCommit(line.Substring(0, 40))) return line.Substring(0, 40).ToLowerInvariant();
            return null;
        }

        /// <summary>The message for a bin built from another commit than the checkout's HEAD, or null.</summary>
        public static string StaleBin(string name, string built, string root, string head, string branch)
        {
            if (built == null || head == null || string.Equals(built, head, StringComparison.OrdinalIgnoreCase)) return null;
            return "Bin non aggiornato: il plug-in è compilato dal commit " + built.Substring(0, 8) + ", ma il checkout " + root + " è al commit " +
                head.Substring(0, 8) + (branch == null ? "" : " (" + branch + ")") + ". Chiudere Rhino e ricompilare " + name + ": i componenti usano il codice di quel commit.";
        }

        public static bool IsShared(string assemblyName)
        {
            return assemblyName != null && (assemblyName.StartsWith("GPC", StringComparison.Ordinal) || assemblyName == "DelaunayMesh" || assemblyName == "TensileLib");
        }

        /// <summary>The message for shared libraries loaded from another file than the copy next to the plug-in, with other content, or null.</summary>
        public static string ForeignLibraries(string name, string folder, IEnumerable<Tuple<string, string, string>> loaded, Func<string, string> hash)
        {
            var differences = new List<string>();
            foreach (var copy in loaded)
            {
                string assemblyName = copy.Item1, version = copy.Item2, path = copy.Item3;
                if (!IsShared(assemblyName)) continue;
                string own = Path.Combine(folder, assemblyName + ".dll");
                if (!File.Exists(own) || string.Equals(Path.GetFullPath(own), string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)) continue;
                string ownVersion = null;
                try { ownVersion = AssemblyName.GetAssemblyName(own).Version.ToString(); } catch (Exception) { }
                string where = string.IsNullOrEmpty(path) ? "posizione sconosciuta" : path;
                if (version != ownVersion) { differences.Add(assemblyName + " " + version + " da " + where + " invece di " + (ownVersion ?? "?")); continue; }
                string actual = null;
                try { if (!string.IsNullOrEmpty(path)) actual = hash(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                if (actual == null) differences.Add(assemblyName + " " + version + " da " + where + ", non verificabile");
                else if (!string.Equals(actual, hash(own), StringComparison.OrdinalIgnoreCase)) differences.Add(assemblyName + " " + version + " da " + where + ", con contenuto diverso");
            }
            return differences.Count == 0 ? null : "Librerie condivise diverse da quelle di " + name + ", probabilmente caricate prima da un altro plug-in (" +
                string.Join("; ", differences) + "). Allineare i plug-in allo stesso set di librerie.";
        }

        public static Tuple<string, string, string> Describe(Assembly assembly)
        {
            var assemblyName = assembly.GetName();
            string location = null;
            try { location = assembly.Location; } catch (NotSupportedException) { }
            return Tuple.Create(assemblyName.Name, assemblyName.Version == null ? null : assemblyName.Version.ToString(), location);
        }

        public static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }

        private static bool IsCommit(string text)
        {
            return text != null && text.Length == 40 && text.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
        }

        private static void CheckLoaded()
        {
            string message = ForeignLibraries(pluginName, pluginFolder, AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).Select(Describe), Hash);
            if (message == null) return;
            lock (Sync) { if (foreign == message) return; foreign = message; }
            Write(message);
        }

        private static string Hash(string path)
        {
            string known;
            lock (Sync) if (Hashes.TryGetValue(path, out known)) return known;
            string value = Sha256(path);
            lock (Sync) Hashes[path] = value;
            return value;
        }

        private static void Write(string message)
        {
            try { Rhino.RhinoApp.WriteLine(pluginName + ": " + message); } catch (Exception) { }
        }
    }
}
