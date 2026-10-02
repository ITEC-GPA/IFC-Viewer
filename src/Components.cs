using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Newtonsoft.Json.Linq;
using Rhino;
using Rhino.Geometry;

namespace IfcViewer
{
    public class MeerkatInfo : GH_AssemblyInfo
    {
        public override string Name { get { return "Meerkat"; } }
        public override string Description { get { return "IFC geometry, BIM attributes, archives and attributed bake."; } }
        public override Guid Id { get { return new Guid("D1313D48-9B86-4F54-886C-B19E419E9FD2"); } }
        public override string AuthorName { get { return "Meerkat"; } }
        public override string AuthorContact { get { return ""; } }
        public override Bitmap Icon { get { return BrandIcons.Get("Meerkat"); } }
    }

    public abstract class IfcComponent : GH_Component
    {
        protected IfcComponent(string name, string nick, string description, string panel, string icon)
            : base(name, nick, description, "Meerkat", panel) { }
        protected override Bitmap Icon { get { return BrandIcons.Get(GetType().Name); } }
        internal static T Unwrap<T>(object value) where T : class
        {
            var wrapper = value as GH_ObjectWrapper;
            return (wrapper == null ? value : wrapper.Value) as T;
        }
        protected void Error(Exception ex) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, ex.Message); }
    }

    public sealed class ReadIfcComponent : IfcComponent
    {
        private List<IfcModel> _models = new List<IfcModel>();
        private bool _lastRead;
        public ReadIfcComponent() : base("Read IFC", "IFC Read", "Legge IFC/IFCZIP o archivi .ifcdata.zip. Dati e geometrie vengono incorporati nel file Grasshopper salvato.", "1 Read", "IN") { }
        public override Guid ComponentGuid { get { return new Guid("EA039FF7-706E-49EF-9848-E28006DDC782"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Files", "F", "Percorsi IFC/IFCZIP o .ifcdata.zip. Uno o piu file.", GH_ParamAccess.list);
            p[0].DataMapping = GH_DataMapping.Flatten;
            p[0].Optional = true;
            p.AddBooleanParameter("Read", "R", "Collegare un Button. Rilegge solo sul passaggio False -> True.", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Models", "M", "Modelli completi, da collegare a Save IFC Archive.", GH_ParamAccess.list);
            p.AddGenericParameter("Elements", "E", "Tutti gli IfcProduct, anche senza geometria.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "R", "Sorgenti, schema, conteggi e avvisi.", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            var paths = new List<string>(); bool run = false;
            da.GetDataList(0, paths); da.GetData(1, ref run);
            bool trigger = run && !_lastRead; _lastRead = run;
            if (trigger)
            {
                try
                {
                    if (paths.Count == 0) throw new ArgumentException("Collegare almeno un percorso IFC a Files.");
                    // Replace the result only after every input has been parsed.
                    _models = paths.Distinct(StringComparer.OrdinalIgnoreCase).Select(Engine.Read).ToList();
                }
                catch (Exception ex) { _models.Clear(); Error(ex); return; }
            }
            if (_models.Count == 0) { Message = "Premi Read"; return; }
            da.SetDataList(0, _models); da.SetDataList(1, _models.SelectMany(m => m.Elements));
            var report = new List<string>();
            foreach (var model in _models)
            {
                report.Add(model + " | " + model.Metadata["geometry_count"] + " mesh | " + model.Metadata["source_path"]);
                foreach (var warning in model.Metadata["warnings"]) report.Add(warning.ToString());
            }
            int count = _models.Sum(m => ((JArray)m.Metadata["warnings"]).Count);
            if (count > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, count + " avvisi: leggere Report. Gli attributi sono conservati anche senza geometria.");
            da.SetDataList(2, report); Message = _models.Sum(m => m.Elements.Count) + " oggetti IFC";
        }
        public override bool Write(GH_IWriter writer)
        {
            writer.SetInt32("ModelCount", _models.Count);
            for (int i = 0; i < _models.Count; i++)
                writer.SetByteArray("Archive" + i, File.ReadAllBytes(_models[i].ArchivePath));
            return base.Write(writer);
        }
        public override bool Read(GH_IReader reader)
        {
            _models.Clear(); _lastRead = false;
            if (reader.ItemExists("ModelCount"))
            {
                int count = reader.GetInt32("ModelCount");
                for (int i = 0; i < count; i++)
                {
                    string folder = Path.Combine(Path.GetTempPath(), "Meerkat", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(folder);
                    string path = Path.Combine(folder, "embedded.ifcdata.zip");
                    File.WriteAllBytes(path, reader.GetByteArray("Archive" + i));
                    _models.Add(IfcModel.Load(path));
                }
            }
            return base.Read(reader);
        }
    }

    public sealed class InspectIfcComponent : IfcComponent
    {
        public InspectIfcComponent() : base("IFC Elements", "IFC Data", "Filtra, visualizza e consulta oggetti IFC. Ogni ramo {i} corrisponde allo stesso oggetto, anche se privo di geometria.", "3 View", "BIM") { }
        public override Guid ComponentGuid { get { return new Guid("82F1C699-571B-4181-88C6-43A8FA6291AB"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Elements", "E", "Oggetti da Read IFC.", GH_ParamAccess.list);
            p.AddTextParameter("Classes", "C", "Filtro per classe esatta, es. IfcWall, IfcSlab. Vuoto = tutte.", GH_ParamAccess.list); p[1].Optional = true;
            p.AddTextParameter("Search", "S", "Cerca in nome, GlobalId o classe.", GH_ParamAccess.item, "");
            p.AddVectorParameter("Offset metres", "O", "Traslazione comune in metri per modelli lontani dall'origine; usare lo stesso offset nel bake.", GH_ParamAccess.item, Vector3d.Zero);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Geometry", "G", "Mesh nelle unita Rhino; ramo {i} vuoto se l'oggetto non ha mesh.", GH_ParamAccess.tree);
            p.AddGenericParameter("Elements", "E", "Oggetti selezionati da collegare al bake.", GH_ParamAccess.list);
            p.AddTextParameter("Classes", "C", "Classe IFC per ogni oggetto.", GH_ParamAccess.list);
            p.AddTextParameter("GlobalIds", "ID", "GlobalId per ogni oggetto.", GH_ParamAccess.list);
            p.AddTextParameter("Keys", "K", "Nomi dei parametri, un ramo {i} per oggetto.", GH_ParamAccess.tree);
            p.AddTextParameter("Values", "V", "Valori allineati a Keys; unita parametri originali IFC.", GH_ParamAccess.tree);
            p.AddTextParameter("JSON", "J", "Metadata completo per oggetto.", GH_ParamAccess.list);
            p.AddColourParameter("Colors", "RGB", "Colore per classe; utilizzabile con Custom Preview.", GH_ParamAccess.tree);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var input = new List<object>(); var classes = new List<string>(); string query = ""; var offset = Vector3d.Zero;
                if (!da.GetDataList(0, input)) return;
                da.GetDataList(1, classes); da.GetData(2, ref query); da.GetData(3, ref offset);
                if (!offset.IsValid) throw new ArgumentException("Offset non valido.");
                var filter = new HashSet<string>(classes.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);
                var elements = input.Select(Unwrap<IfcElement>).Where(e => e != null)
                    .Where(e => filter.Count == 0 || filter.Contains(e.IfcClass))
                    .Where(e => string.IsNullOrEmpty(query) || e.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                var meshes = new GH_Structure<GH_Mesh>(); var keys = new GH_Structure<GH_String>();
                var values = new GH_Structure<GH_String>(); var colors = new GH_Structure<GH_Colour>();
                for (int i = 0; i < elements.Count; i++)
                {
                    var path = new GH_Path(i); meshes.EnsurePath(path); colors.EnsurePath(path);
                    var mesh = elements[i].MeshInDocument(RhinoDoc.ActiveDoc, offset);
                    if (mesh != null) { meshes.Append(new GH_Mesh(mesh), path); colors.Append(new GH_Colour(Baking.ClassColor(elements[i].IfcClass)), path); }
                    foreach (var pair in elements[i].UserText())
                    { keys.Append(new GH_String(pair.Key), path); values.Append(new GH_String(pair.Value), path); }
                }
                da.SetDataTree(0, meshes); da.SetDataList(1, elements); da.SetDataList(2, elements.Select(e => e.IfcClass));
                da.SetDataList(3, elements.Select(e => e.GlobalId)); da.SetDataTree(4, keys); da.SetDataTree(5, values);
                da.SetDataList(6, elements.Select(e => e.Record.ToString(Newtonsoft.Json.Formatting.None))); da.SetDataTree(7, colors);
                Message = elements.Count + " selezionati";
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class BakeIfcComponent : IfcComponent
    {
        private bool _lastBake;
        private List<Guid> _ids = new List<Guid>();
        private string _report = "Premere Bake per creare oggetti Rhino con User Text IFC.";
        public BakeIfcComponent() : base("Bake IFC", "IFC Bake", "Bake su layer Radice::Modello::ClasseIFC e tutti i parametri negli User Text. Un record Undo per operazione.", "6 Bake", "BAK") { }
        public override Guid ComponentGuid { get { return new Guid("F2C6C624-8665-49FB-BE2B-B904676DD197"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Elements", "E", "Oggetti da IFC Elements o Read IFC.", GH_ParamAccess.list);
            p[0].DataMapping = GH_DataMapping.Flatten;
            p.AddBooleanParameter("Bake", "B", "Button: agisce solo sul passaggio False -> True.", GH_ParamAccess.item, false);
            p.AddTextParameter("Root layer", "L", "Layer radice.", GH_ParamAccess.item, "IFC");
            p.AddBooleanParameter("Update", "U", "True aggiorna gli oggetti precedentemente creati da questo componente, stessa sorgente e GlobalId. False crea copie.", GH_ParamAccess.item, true);
            p.AddVectorParameter("Offset metres", "O", "Stesso offset usato nella visualizzazione.", GH_ParamAccess.item, Vector3d.Zero);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Object IDs", "ID", "GUID Rhino dell'ultima operazione.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "R", "Risultato dell'ultima operazione.", GH_ParamAccess.item);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            var input = new List<object>(); bool run = false, update = true; string root = "IFC"; var offset = Vector3d.Zero;
            da.GetData(1, ref run); bool trigger = run && !_lastBake; _lastBake = run;
            if (!da.GetDataList(0, input)) return;
            da.GetData(2, ref root); da.GetData(3, ref update); da.GetData(4, ref offset);
            if (trigger)
            {
                try
                {
                    if (!offset.IsValid) throw new ArgumentException("Offset non valido.");
                    int skipped;
                    _ids = Baking.Bake(RhinoDoc.ActiveDoc, input.Select(Unwrap<IfcElement>).Where(e => e != null), root, offset, update, out skipped);
                    _report = _ids.Count + " oggetti creati/aggiornati; " + skipped + " senza mesh (attributi presenti nell'archivio). Salvare il documento Rhino per conservarli nel .3dm.";
                }
                catch (Exception ex) { _ids.Clear(); _report = "Bake non completato. Se sono stati creati oggetti, usare Undo in Rhino. " + ex.Message; Error(ex); }
            }
            da.SetDataList(0, _ids); da.SetData(1, _report);
        }
    }

    public sealed class SaveIfcComponent : IfcComponent
    {
        private bool _lastSave;
        private List<string> _paths = new List<string>();
        public SaveIfcComponent() : base("Save IFC Archive", "IFC Save", "Salva archivi .ifcdata.zip con IFC originale, tutte le entita, attributi e mesh. Riapribili con Read IFC senza Python.", "4 Save", "ZIP") { }
        public override Guid ComponentGuid { get { return new Guid("F7A083CD-6E4B-4568-ACCD-623C698AFBE8"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Models", "M", "Modelli completi da Read IFC.", GH_ParamAccess.list);
            p[0].DataMapping = GH_DataMapping.Flatten;
            p.AddTextParameter("Directory", "D", "Cartella in cui salvare gli archivi.", GH_ParamAccess.item);
            p.AddBooleanParameter("Save", "S", "Button: salva sul passaggio False -> True.", GH_ParamAccess.item, false);
            p.AddBooleanParameter("Overwrite", "O", "Consente di sovrascrivere un archivio esistente.", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Files", "F", "Percorsi degli archivi salvati.", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            var input = new List<object>(); string folder = ""; bool run = false, overwrite = false;
            da.GetData(2, ref run); bool trigger = run && !_lastSave; _lastSave = run;
            if (!da.GetDataList(0, input) || !da.GetData(1, ref folder)) return;
            da.GetData(3, ref overwrite);
            if (trigger)
            {
                try
                {
                    var models = input.Select(Unwrap<IfcModel>).Where(m => m != null).ToList();
                    folder = Path.GetFullPath(folder);
                    var paths = models.Select(m => Path.Combine(folder, Path.GetFileNameWithoutExtension((string)m.Metadata["source_name"]) + "_" + m.SourceKey + ".ifcdata.zip")).ToList();
                    if (!overwrite && paths.Any(File.Exists)) throw new IOException("Archivio gia esistente. Attivare Overwrite o scegliere un'altra cartella.");
                    Directory.CreateDirectory(folder);
                    for (int i = 0; i < models.Count; i++)
                    {
                        if (string.Equals(models[i].ArchivePath, paths[i], StringComparison.OrdinalIgnoreCase)) continue;
                        string staging = paths[i] + "." + Guid.NewGuid().ToString("N") + ".tmp";
                        try
                        {
                            File.Copy(models[i].ArchivePath, staging);
                            if (File.Exists(paths[i]))
                            {
                                if (!overwrite) throw new IOException("Archivio gia esistente: " + paths[i]);
                                File.Replace(staging, paths[i], null);
                            }
                            else File.Move(staging, paths[i]);
                        }
                        finally { if (File.Exists(staging)) File.Delete(staging); }
                    }
                    _paths = paths;
                }
                catch (Exception ex) { _paths.Clear(); Error(ex); }
            }
            da.SetDataList(0, _paths);
        }
    }
}
