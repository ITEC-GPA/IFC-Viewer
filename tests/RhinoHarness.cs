using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GH_IO.Serialization;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using IfcViewer;
using Rhino;
using Rhino.Geometry;
using Rhino.DocObjects;

internal static class Bootstrap
{
    private static string Root;
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetDllDirectory(string path);
    [STAThread]
    private static int Main(string[] args)
    {
        Root = Path.GetFullPath(args[0]);
        string rhino = @"C:\Program Files\Rhino 8\System";
        SetDllDirectory(rhino);
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name;
            string pluginDirectory = System.Environment.GetEnvironmentVariable("MEERKAT_PLUGIN_DIR") ?? Path.Combine(Root, "bin");
            foreach (string dir in new[] { pluginDirectory, rhino, @"C:\Program Files\Rhino 8\Plug-ins\Grasshopper" })
                foreach (string extension in new[] { ".dll", ".gha" })
                {
                    string file = Path.Combine(dir, name + extension);
                    if (File.Exists(file)) return Assembly.LoadFrom(file);
                }
            return null;
        };
        try
        {
            Assembly.GetExecutingAssembly().GetType("RhinoHarness").GetMethod("Run", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { Root, args.Length > 1 ? args[1] : "test" });
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}

internal static class RhinoHarness
{
    private static string Root;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Run(string root, string mode)
    {
        Root = root;
        using (var core = new Rhino.Runtime.InProcess.RhinoCore(new[] { "/nosplash" }, Rhino.Runtime.InProcess.WindowStyle.NoWindow))
        {
            Directory.CreateDirectory(Path.Combine(Root, "test-output"));
            string archivePath = Path.Combine(Root, "examples", "demo.ifcdata.zip");
            var model = IfcModel.Load(archivePath);
            Check(Instances.ComponentServer.FindAssembly(new Guid("D1313D48-9B86-4F54-886C-B19E419E9FD2")) != null, "Plugin discovered through Grasshopper library registration");
            BrandTests.Run(Root, Check);
            if (mode == "distribution")
            {
                BrandTests.Distribution(Check);
                return;
            }
            if (mode == "examples")
            {
                ExampleDefinitions.Generate(Root, Check);
                return;
            }
            var fresh = Engine.Read(Path.Combine(Root, "examples", "demo.ifc"));
            Check(fresh.Elements.Count == 8 && fresh.Elements.Count(e => e.MeshMetres != null) == 4, "C# worker launches Python and reads an IFC end to end");
            using (var doc = RhinoDoc.CreateHeadless(null))
            {
                RhinoDoc.ActiveDoc = doc;
                doc.ModelUnitSystem = UnitSystem.Millimeters;
                CreateDefinition(model);
                int skipped;
                var ids = Baking.Bake(doc, model.Elements, "IFC", Vector3d.Zero, true, out skipped);
                Check(ids.Count == 4 && skipped == 4, "Bake: 4 meshes, 4 metadata-only products");
                var wall = ids.Select(doc.Objects.FindId).First(o => o.Attributes.GetUserString("IFC.Name").StartsWith("Parete 1"));
                Guid wallId = wall.Id;
                Check(wall.Attributes.GetUserString("IFC.Property.Pset_WallCommon.FireRating") == "REI 60", "IFC effective property in User Text");
                Check(wall.Attributes.GetUserString("IFC.TypeProperty.Pset_WallCommon.FireRating") == "REI 120", "IFC type property preserved");
                Check(wall.Attributes.GetUserString("IFC.MetadataJSON").Contains("IfcPropertyBoundedValue"), "Complex properties and units preserved");
                Check(doc.Layers[wall.Attributes.LayerIndex].FullPath.EndsWith("::IfcWall"), "Layer hierarchy by IFC class");
                Check(Math.Abs(wall.Geometry.GetBoundingBox(true).Max.X - 5000.0) < 0.001, "Metres -> document millimetres");
                var again = Baking.Bake(doc, model.Elements, "IFC", new Vector3d(1, 0, 0), true, out skipped);
                Check(doc.Objects.Count == 4 && ids.SequenceEqual(again), "Re-bake updates without duplicate objects and preserves Rhino IDs");
                Check(Math.Abs(doc.Objects.FindId(wallId).Geometry.GetBoundingBox(true).Max.X - 6000.0) < 0.001, "Offset applied in metres");
                // Different source paths with identical IFC GlobalIds must coexist.
                var second = IfcModel.Load(archivePath);
                second.Metadata["source_path"] = "C:\\Federation\\other.ifc";
                Baking.Bake(doc, second.Elements, "IFC", Vector3d.Zero, true, out skipped);
                Check(doc.Objects.Count == 8, "Federated sources with identical GlobalIds remain distinct");
                string path = Path.Combine(Root, "test-output", "bake-roundtrip.3dm");
                Check(doc.Write3dmFile(path, new Rhino.FileIO.FileWriteOptions()), "Write .3dm");
                using (var file = Rhino.FileIO.File3dm.Read(path))
                {
                    Check(file.Objects.Count == 8, ".3dm preserves objects");
                    var stored = file.Objects.FindId(wallId);
                    Check(stored != null, ".3dm preserves object identity");
                    Check(stored.Attributes != null, ".3dm object has attributes");
                    Check(stored.Attributes.GetUserString("IFC.MetadataJSON") != null, ".3dm preserves all IFC metadata");
                }
                ToolTests.Run(model, doc, Root, Check);
                BrandTests.Legacy(Root, Check);
            }
        }
    }
    private static T Place<T>(GH_Document doc, T obj, float x, float y) where T : IGH_DocumentObject
    {
        obj.CreateAttributes(); obj.Attributes.Pivot = new PointF(x, y); doc.AddObject(obj, false); return obj;
    }
    private static GH_Panel Panel(GH_Document doc, string name, string text, float x, float y, float w, float h)
    {
        var panel = Place(doc, new GH_Panel(), x, y);
        panel.NickName = name; panel.UserText = text;
        panel.Attributes.Bounds = new RectangleF(x, y, w, h);
        return panel;
    }
    private static void CreateDefinition(IfcModel model)
    {
        using (var definition = new GH_Document())
        {
            definition.Enabled = true;
            Panel(definition, "MEERKAT | Rhino 8 / Windows", "1. Modifica il percorso IFC e premi READ.\n2. Filtra le classi IFC e consulta attributi/valori.\n3. Premi BAKE per creare mesh con User Text.\n4. Salva .gh e .3dm; SAVE esporta l'archivio completo.\nIl modello dimostrativo e gia incorporato nel .gh.", 30, 20, 600, 145);
            var reader = new ReadIfcComponent();
            // Embed without evaluating a Grasshopper solution or changing the
            // active Rhino document during artifact generation.
            var chunk = new GH_LooseChunk("Read IFC");
            reader.Write(chunk);
            chunk.RemoveItem("ModelCount");
            chunk.SetInt32("ModelCount", 1);
            chunk.SetByteArray("Archive0", File.ReadAllBytes(model.ArchivePath));
            reader.Read(chunk);
            Place(definition, reader, 400, 270);
            var pathPanel = Panel(definition, "FILE IFC (un percorso per riga)", Path.Combine(Root, "examples", "demo.ifc"), 30, 220, 290, 80);
            reader.Params.Input[0].AddSource(pathPanel);
            var read = Place(definition, new GH_ButtonObject(), 160, 340); read.NickName = "READ"; reader.Params.Input[1].AddSource(read);
            var inspect = Place(definition, new InspectIfcComponent(), 730, 270);
            inspect.Params.Input[0].AddSource(reader.Params.Output[1]);
            var classes = Panel(definition, "CLASSI | vuoto = tutte", "", 470, 460, 200, 70);
            inspect.Params.Input[1].AddSource(classes);
            var bake = Place(definition, new BakeIfcComponent(), 1080, 270);
            bake.Params.Input[0].AddSource(inspect.Params.Output[1]);
            var bakeButton = Place(definition, new GH_ButtonObject(), 890, 460); bakeButton.NickName = "BAKE"; bake.Params.Input[1].AddSource(bakeButton);
            var save = Place(definition, new SaveIfcComponent(), 1080, 630);
            save.Params.Input[0].AddSource(reader.Params.Output[0]);
            var saveButton = Place(definition, new GH_ButtonObject(), 890, 760); saveButton.NickName = "SAVE"; save.Params.Input[2].AddSource(saveButton);
            var folder = Panel(definition, "CARTELLA ARCHIVI", Path.Combine(Root, "exports"), 470, 670, 350, 65);
            save.Params.Input[1].AddSource(folder);
            var report = Panel(definition, "REPORT LETTURA", "", 30, 430, 380, 170); report.AddSource(reader.Params.Output[2]);
            var bakeReport = Panel(definition, "REPORT BAKE", "", 1200, 230, 360, 140); bakeReport.AddSource(bake.Params.Output[1]);
            var saved = Panel(definition, "ARCHIVI SALVATI", "", 1200, 620, 360, 110); saved.AddSource(save.Params.Output[0]);
            var keys = Panel(definition, "ATTRIBUTI | ramo {i} = oggetto i", "", 470, 880, 510, 240); keys.AddSource(inspect.Params.Output[4]);
            var values = Panel(definition, "VALORI | stesso ramo e indice", "", 1020, 880, 510, 240); values.AddSource(inspect.Params.Output[5]);
            var classList = Panel(definition, "TIPOLOGIE IFC", "", 800, 30, 320, 145); classList.AddSource(inspect.Params.Output[2]);
            var previewProxy = Instances.ComponentServer.ObjectProxies.FirstOrDefault(p => p.Desc.Name == "Custom Preview");
            if (previewProxy != null)
            {
                var preview = Place(definition, (GH_Component)previewProxy.CreateInstance(), 1270, 455);
                preview.Params.Input[0].AddSource(inspect.Params.Output[0]);
                preview.Params.Input[1].AddSource(inspect.Params.Output[7]);
                inspect.Hidden = true;
            }
            ToolboxDefinition.Add(definition, reader, inspect, bake, model, Root);
            GH_Document.EnableSolutions = true;
            definition.AssociateWithRhinoDocument();
            definition.Enabled = true;
            definition.NewSolution(true, GH_SolutionMode.Silent);
            if (reader.Params.Output[1].VolatileDataCount != 8)
            {
                Console.WriteLine("Definition state: " + definition.SolutionState + "; enabled=" + definition.Enabled + "; global=" + GH_Document.EnableSolutions);
                Console.WriteLine("Read outputs=" + reader.Params.Output[1].VolatileDataCount + "; phase=" + reader.Phase + "; message=" + reader.Message);
                foreach (var component in definition.Objects.OfType<GH_Component>())
                    foreach (var message in component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Concat(component.RuntimeMessages(GH_RuntimeMessageLevel.Warning)))
                        Console.WriteLine(component.Name + ": " + message);
            }
            Check(reader.Params.Output[1].VolatileDataCount == 8, "Grasshopper Read emits embedded elements");
            Check(inspect.Params.Output[0].VolatileDataCount == 4, "Grasshopper preview emits 4 meshes");
            Check(inspect.Params.Output[0].VolatileData.PathCount == 8, "Grasshopper preserves empty branches for metadata-only objects");
            Check(!definition.Objects.OfType<GH_Component>().Any(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0), "Grasshopper solution has no component errors");
            Check(definition.Objects.OfType<IfcComponent>().Count() == 19, "Definition includes all 19 IFC components");
            classes.UserText = "IfcWall";
            definition.NewSolution(true, GH_SolutionMode.Silent);
            Check(inspect.Params.Output[1].VolatileDataCount == 2 && inspect.Params.Output[0].VolatileDataCount == 2, "Grasshopper class filter selects only walls");
            classes.UserText = "";
            definition.NewSolution(true, GH_SolutionMode.Silent);
            var archive = new GH_Archive();
            Check(archive.AppendObject(definition, "Definition"), "Serialize Grasshopper definition");
            Check(archive.WriteToFile(Path.Combine(Root, "Meerkat.gh"), true, false), "Write Meerkat.gh");
            Check(archive.WriteToFile(Path.Combine(Root, "Meerkat.ghx"), true, false), "Write inspectable Meerkat.ghx");
            // Independently restore the component's embedded archive, without
            // the external IFC or Python worker being consulted.
            var savedChunk = new GH_LooseChunk("Embedded"); reader.Write(savedChunk);
            var restored = new ReadIfcComponent(); restored.Read(savedChunk);
            var verifyChunk = new GH_LooseChunk("Verify"); restored.Write(verifyChunk);
            Check(verifyChunk.GetInt32("ModelCount") == 1 && verifyChunk.GetByteArray("Archive0").SequenceEqual(File.ReadAllBytes(model.ArchivePath)), "Grasshopper embeds and restores the complete source archive");
            var io = new GH_DocumentIO();
            Check(io.Open(Path.Combine(Root, "Meerkat.gh")), "Reopen complete Grasshopper definition");
            using (var reopened = io.Document)
            {
                reopened.Enabled = true;
                reopened.NewSolution(true, GH_SolutionMode.Silent);
                var reopenedReader = reopened.Objects.OfType<ReadIfcComponent>().Single();
                foreach (var component in reopened.Objects.OfType<GH_Component>())
                    foreach (var error in component.RuntimeMessages(GH_RuntimeMessageLevel.Error)) Console.WriteLine(component.Name + ": " + error);
                Check(reopenedReader.Params.Output[1].VolatileDataCount == 8, "Reopened Grasshopper works with embedded data");
                Check(reopened.Objects.OfType<IfcComponent>().Count() == 19 && !reopened.Objects.OfType<GH_Component>().Any(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0), "All 19 components survive serialization and recompute");
            }
            using (var canvas = new Grasshopper.GUI.Canvas.GH_Canvas())
            using (var bitmap = new Bitmap(2250, 1450))
            {
                canvas.Size = new Size(2250, 1450);
                canvas.Document = definition;
                canvas.CreateControl();
                canvas.Viewport.Size = new Size(2250, 1450);
                canvas.Viewport.Zoom = 0.5f;
                canvas.Viewport.Target = new System.Drawing.Point(0, 0);
                canvas.Viewport.ComputeProjection();
                canvas.DrawToBitmap(bitmap, new Rectangle(0, 0, 2250, 1450));
                bitmap.Save(Path.Combine(Root, "examples", "grasshopper.png"));
                canvas.Document = null;
            }
        }
    }
}
