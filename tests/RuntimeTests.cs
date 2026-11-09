using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using IfcViewer;

internal static class RuntimeTests
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string AbandonedSnapshot(byte[] bytes)
    {
        var model = IfcModel.Load(bytes);
        string path = model.ArchivePath;
        GC.KeepAlive(model);
        return path;
    }
    private static void Fails<T>(Action action, Action<bool, string> check, string message) where T : Exception
    {
        try { action(); } catch (T) { check(true, message); return; }
        throw new Exception("FAIL: " + message);
    }
    internal static void Run(string root, Action<bool, string> check)
    {
        string folder = Path.Combine(root, "test-output", "runtime città " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string script = Path.Combine(folder, "worker prova.py");
        string python = Engine.PythonPath();
        File.WriteAllText(script, "import sys\nsys.stderr.write('x' * 200000)\nprint(sys.argv[1])\n", new UTF8Encoding(false));
        string argument = "città con spazi, \"quote\" e slash finale\\";
        check(WorkerProcess.Run(python, script, new[] { argument }).Trim() == argument, "Worker drains stderr and preserves Unicode/quoted arguments");
        string oldHome = Environment.GetEnvironmentVariable("PYTHONHOME"), oldPath = Environment.GetEnvironmentVariable("PYTHONPATH");
        try
        {
            Environment.SetEnvironmentVariable("PYTHONHOME", Path.Combine(folder, "invalid-python-home"));
            Environment.SetEnvironmentVariable("PYTHONPATH", folder);
            File.WriteAllText(Path.Combine(folder, "json.py"), "raise RuntimeError('Host Python path leaked into worker')\n");
            // Put the shadow module outside the script directory: sibling imports remain supported.
            string isolatedScript = Path.Combine(folder, "scripts"); Directory.CreateDirectory(isolatedScript);
            isolatedScript = Path.Combine(isolatedScript, "probe.py");
            File.WriteAllText(isolatedScript, "import json,sys\nprint(json.dumps(sys.flags.no_user_site))\n");
            check(WorkerProcess.Run(python, isolatedScript, new string[0]).Trim() == "1", "Worker ignores host PYTHONHOME/PYTHONPATH and user site packages");
        }
        finally { Environment.SetEnvironmentVariable("PYTHONHOME", oldHome); Environment.SetEnvironmentVariable("PYTHONPATH", oldPath); }
        Fails<FileNotFoundException>(() => WorkerProcess.Run(Path.Combine(folder, "missing.exe"), script, new string[0]), check, "Missing interpreter has actionable error");
        Fails<FileNotFoundException>(() => WorkerProcess.Run(python, script + ".missing", new string[0]), check, "Missing worker detected before launch");
        File.WriteAllText(script, "import missing_meerkat_test_dependency\n");
        try { WorkerProcess.Run(python, script, new string[0]); throw new Exception("Expected worker failure"); }
        catch (InvalidOperationException ex) { check(ex.Message.Contains("missing_meerkat_test_dependency") && ex.Message.Contains("exit 1"), "Worker dependency failure retains diagnostic"); }
        File.WriteAllText(script, "import time\ntime.sleep(60)\n");
        Fails<TimeoutException>(() => WorkerProcess.Run(python, script, new string[0], 500), check, "Worker timeout terminates and waits for process");

        string source = Path.Combine(root, "examples", "demo.ifcdata.zip");
        byte[] bytes = File.ReadAllBytes(source);
        var model = IfcModel.Load(bytes);
        string temporary = model.ArchivePath;
        File.Delete(temporary);
        check(model.ArchiveBytes().SequenceEqual(bytes) && File.ReadAllBytes(model.ArchivePath).SequenceEqual(bytes), "Missing temporary archive restored without losing source bytes");
        check(File.ReadAllBytes(source).SequenceEqual(bytes), "Model snapshot leaves user's archive unchanged");
        string abandoned = AbandonedSnapshot(bytes);
        GC.Collect(); GC.WaitForPendingFinalizers();
        check(!File.Exists(abandoned) && File.Exists(source), "Collected model removes only its own temporary snapshot");
        var chunk = new GH_LooseChunk("multi");
        chunk.SetInt32("ModelCount", 2);
        chunk.SetByteArray("Archive0", bytes); chunk.SetByteArray("Archive1", bytes);
        var read = new ReadIfcComponent();
        check(read.Read(chunk), "Read restores two embedded models without external IFC");
        using (var doc = new GH_Document())
        {
            read.CreateAttributes(); doc.AddObject(read, false);
            GH_Document.EnableSolutions = true; doc.Enabled = true;
            ToolTests.SetText(read, 0, Path.Combine(folder, "missing.ifc"));
            ToolTests.SetBool(read, 1, true);
            doc.NewSolution(true, GH_SolutionMode.Silent);
            check(read.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0 && read.Params.Output[0].VolatileDataCount == 2, "Failed READ preserves and outputs both previous models");
            var saved = new GH_LooseChunk("saved");
            check(read.Write(saved) && saved.GetInt32("ModelCount") == 2 && saved.GetByteArray("Archive0").SequenceEqual(bytes) && saved.GetByteArray("Archive1").SequenceEqual(bytes), "Multiple embedded models serialize byte-for-byte after failed READ");
            var reopened = new ReadIfcComponent();
            check(reopened.Read(saved), "Saved multiple-model component reopens");
        }
        using (var doc = new GH_Document())
        {
            File.Delete(model.ArchivePath);
            var save = new SaveIfcComponent(); save.CreateAttributes(); doc.AddObject(save, false);
            ToolTests.SetData(save, 0, new object[] { model });
            ToolTests.SetText(save, 1, Path.Combine(folder, "archives"));
            ToolTests.SetBool(save, 2, true);
            doc.Enabled = true; doc.NewSolution(true, GH_SolutionMode.Silent);
            check(save.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0, "Save Archive works after temporary file removal");
            string exported = ((GH_String)save.Params.Output[0].VolatileData.AllData(true).Single()).Value;
            check(File.ReadAllBytes(exported).SequenceEqual(bytes) && !File.Exists(temporary), "Archive export uses the in-memory snapshot directly");
        }
        var memoryAssembly = Assembly.Load(File.ReadAllBytes(typeof(Engine).Assembly.Location));
        check(string.IsNullOrEmpty(memoryAssembly.Location), "COFF regression uses an assembly loaded from bytes");
        string resolved = (string)memoryAssembly.GetType("IfcViewer.Engine").GetProperty("PluginDirectory").GetValue(null, null);
        check(resolved == Engine.PluginDirectory, "Memory-loaded plugin resolves physical path through component server");
        var checker = memoryAssembly.GetType("Gpc.PluginChecks.PluginBuildCheck");
        checker.GetMethod("Start").Invoke(null, new object[] { memoryAssembly, "Meerkat", false, resolved });
        check((string)checker.GetField("pluginFolder", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null) == resolved,
            "Build check initializes with the physical folder for memory-loaded assemblies");
    }
}
