using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Grasshopper;
using Grasshopper.Kernel;
using IfcViewer;
using Rhino;

internal static class BrandTests
{
    internal static void Run(string root, Action<bool, string> check)
    {
        var assembly = typeof(ReadIfcComponent).Assembly;
        var info = new MeerkatInfo();
        check(assembly.GetName().Name == "Meerkat" && info.Name == "Meerkat" && info.Id == new Guid("D1313D48-9B86-4F54-886C-B19E419E9FD2"), "Meerkat assembly/name with stable plugin GUID");
        check(info.Icon.Width == 24 && info.Icon.Height == 24, "Meerkat logo embedded at 24 pixels");
        var types = assembly.GetTypes().Where(t => !t.IsAbstract && typeof(IfcComponent).IsAssignableFrom(t)).ToArray();
        var iconProperty = typeof(GH_Component).GetProperty("Icon", BindingFlags.NonPublic | BindingFlags.Instance);
        var hashes = new HashSet<string>();
        foreach (var type in types)
        {
            var c = (GH_Component)Activator.CreateInstance(type);
            var icon = (Bitmap)iconProperty.GetValue(c, null);
            check(c.Category == "Meerkat" && icon != null && icon.Width == 24 && icon.Height == 24 && icon.GetPixel(0, 0).A < 255, "Meerkat category and icon: " + c.Name);
            using (var stream = new MemoryStream())
            using (var sha = SHA256.Create())
            { icon.Save(stream, System.Drawing.Imaging.ImageFormat.Png); hashes.Add(Convert.ToBase64String(sha.ComputeHash(stream.ToArray()))); }
        }
        check(types.Length == 19 && hashes.Count == 19 && assembly.GetManifestResourceNames().Count(n => n.StartsWith("Meerkat.Icons.")) == 20, "19 distinct component icons plus plugin logo embedded");
    }

    internal static void Legacy(string root, Action<bool, string> check)
    {
        string legacy = Path.Combine(root, "tests", "fixtures", "IFCViewer-legacy.gh");
        {
            // Caller owns a live scratch Rhino document. Disposing the active
            // Rhino document between GH suites can leave the headless solver idle.
            check(RhinoDoc.ActiveDoc != null, "Legacy check has a live scratch Rhino document");
            var io = new GH_DocumentIO(); check(io.Open(legacy), "Open original IFC Viewer GH using Meerkat");
            using (var gh = io.Document)
            {
                GH_Document.EnableSolutions = true; gh.Enabled = true; gh.NewSolution(true, GH_SolutionMode.Silent);
                check(gh.Objects.OfType<IfcComponent>().Count() == 19 && gh.Objects.OfType<ReadIfcComponent>().Single().Params.Output[1].VolatileDataCount == 8, "Legacy GH restores 19 components and embedded model");
                check(gh.Objects.OfType<GH_Component>().All(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0), "Legacy GH recomputes without errors after rename");
            }
        }
    }

    internal static void Distribution(Action<bool, string> check)
    {
        string expected = Environment.GetEnvironmentVariable("MEERKAT_PLUGIN_DIR");
        check(!string.IsNullOrEmpty(expected) && Path.GetFullPath(Engine.PluginDirectory).TrimEnd('\\') == Path.GetFullPath(expected).TrimEnd('\\'), "Plugin resolves from relocated distribution");
        string python = Engine.PythonPath();
        check(File.Exists(python) && python.StartsWith(expected, StringComparison.OrdinalIgnoreCase), "Relocated distribution uses its own installed Python runtime");
        var fresh = Engine.Read(Path.Combine(expected, "examples", "tutorial_A.ifc"));
        check(fresh.Elements.Count == 11 && fresh.Elements.Count(e => e.MeshMetres != null) == 6, "Relocated worker reads source IFC end to end without repository runtime");
    }
}
