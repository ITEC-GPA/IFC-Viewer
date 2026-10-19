using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace IfcViewer
{
    // Requests are immutable snapshots. Creating/previewing them never changes a model or file.
    public sealed class IfcEdit
    {
        public IfcModel Model { get; private set; }
        private readonly JObject _data;
        public JObject Data { get { return (JObject)_data.DeepClone(); } }
        public IfcEdit(IfcElement element, string kind, string name, string value, bool type, string pset = "", string valueType = "Auto")
        {
            if (element == null) throw new ArgumentNullException("element");
            if (type && (element.Record["type_object"] == null || element.Record["type_object"].Type == JTokenType.Null)) throw new ArgumentException("L'elemento non ha un tipo associato: " + element.Name);
            Model = element.Model;
            _data = new JObject(new JProperty("step_id", element.StepId), new JProperty("global_id", element.GlobalId),
                new JProperty("kind", kind), new JProperty("name", name), new JProperty("value", value),
                new JProperty("scope", type ? "type" : "instance"));
            if (kind == "property") { _data["pset"] = pset; _data["value_type"] = valueType; }
        }
        public override string ToString()
        {
            if ((string)_data["kind"] == "geometry") return "instance | " + _data["global_id"] + " | Body -> " + ((JArray)_data["vertices_metres"]).Count + " vertici, " + ((JArray)_data["faces"]).Count + " triangoli (non parametrico; Qto non ricalcolate)";
            return _data["scope"] + " | " + _data["global_id"] + " | " + (_data["pset"] == null ? "" : _data["pset"] + ".") + _data["name"] + " = " + _data["value"].ToString(Formatting.None);
        }
        internal static IfcEdit Geometry(IfcElement element, IEnumerable<double[]> vertices, IEnumerable<int[]> faces)
        {
            var change = new IfcEdit(element, "geometry", "Body", null, false);
            change._data["vertices_metres"] = JArray.FromObject(vertices); change._data["faces"] = JArray.FromObject(faces);
            return change;
        }
    }

    public static class IfcEditing
    {
        public static void Validate(IfcModel model, IList<IfcEdit> changes)
        {
            if (model == null) throw new ArgumentException("Collegare un solo Model da Read IFC.");
            if (changes.Count == 0) throw new ArgumentException("Nessuna modifica richiesta.");
            if (changes.Any(c => c == null || c.Model.SourceKey != model.SourceKey || (string)c.Model.Metadata["source_sha256"] != (string)model.Metadata["source_sha256"]))
                throw new ArgumentException("Le modifiche devono appartenere alla stessa sorgente/versione del modello. Esportare un modello alla volta.");
        }
        public static string Write(IfcModel model, IList<IfcEdit> changes, string path, bool overwrite)
        {
            Validate(model, changes);
            path = Path.GetFullPath(path);
            if (!path.EndsWith(".ifc", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Destinazione .ifc richiesta.");
            if (string.Equals(path, Path.GetFullPath((string)model.Metadata["source_path"]), StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Il file sorgente e protetto: scegliere un nuovo percorso.");
            string python = Engine.PythonPath();
            if (!File.Exists(python)) throw new FileNotFoundException("Runtime IFC mancante. Eseguire Setup.cmd.", python);
            string request = Path.Combine(Path.GetTempPath(), "Meerkat", Guid.NewGuid().ToString("N") + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(request));
            try
            {
                File.WriteAllText(request, new JObject(new JProperty("source_sha256", model.Metadata["source_sha256"]), new JProperty("changes", new JArray(changes.Select(c => c.Data)))).ToString(), new UTF8Encoding(false));
                var start = new ProcessStartInfo(python, Engine.Quote(Path.Combine(Engine.PluginDirectory, "ifc_writer.py")) + " " + Engine.Quote(model.ArchivePath) + " " + Engine.Quote(request) + " " + Engine.Quote(path) + (overwrite ? " --overwrite" : ""))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
                start.EnvironmentVariables["PYTHONUTF8"] = "1";
                using (var process = Process.Start(start))
                {
                    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(30 * 60 * 1000)) { process.Kill(); process.WaitForExit(); throw new TimeoutException("Scrittura IFC interrotta dopo 30 minuti. Verificare il file destinazione prima di ripetere."); }
                    if (process.ExitCode != 0) throw new InvalidOperationException(stderr.Result);
                    return JObject.Parse(stdout.Result).ToString();
                }
            }
            finally { if (File.Exists(request)) File.Delete(request); }
        }
    }

    public sealed class SetAttributeComponent : ElementTool
    {
        public SetAttributeComponent() : base("IFC Set Attribute", "IFC Attr", "Prepara modifiche a Name, Description o Tag. Non modifica il modello: collegare Changes a IFC Write File.", "5 Edit", "ATR") { }
        public override Guid ComponentGuid { get { return new Guid("BEEB272D-1CE2-461C-9771-E9366A837C01"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            ElementInput(p);
            p.AddTextParameter("Attribute", "A", "Name, Description oppure Tag.", GH_ParamAccess.item, "Name");
            p.AddTextParameter("Values", "V", "Un valore per tutti gli elementi oppure un valore per elemento; nessuna ripetizione implicita dell'ultimo.", GH_ParamAccess.list);
            p.AddBooleanParameter("On type", "T", "Modifica il tipo condiviso: riguarda TUTTE le sue istanze.", GH_ParamAccess.item, false);
            p.AddBooleanParameter("Clear", "C", "Imposta l'attributo a null; Values ignorato.", GH_ParamAccess.item, false);
            p[2].Optional = true;
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Changes", "C", "Richieste da unire e collegare a IFC Write File.", GH_ParamAccess.list);
            p.AddTextParameter("Preview", "P", "Anteprima delle richieste; validazione IFC completa al Write.", GH_ParamAccess.list);
        }
        internal static List<IfcEdit> Create(IList<IfcElement> elements, IList<string> values, bool clear, Func<IfcElement, string, IfcEdit> factory)
        {
            if (elements.Count == 0) throw new ArgumentException("Selezione vuota.");
            if (!clear && values.Count != 1 && values.Count != elements.Count) throw new ArgumentException("Values deve contenere un valore oppure esattamente un valore per elemento.");
            return elements.Select((e, i) => factory(e, clear ? null : values[values.Count == 1 ? 0 : i])).ToList();
        }
        internal static void SetOutputs(IGH_DataAccess da, IList<IfcEdit> changes) { da.SetDataList(0, changes); da.SetDataList(1, changes.Select(c => c.ToString())); }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                string name = "Name"; bool type = false, clear = false; var values = new List<string>();
                da.GetData(1, ref name); da.GetDataList(2, values); da.GetData(3, ref type); da.GetData(4, ref clear);
                if (!new[] { "Name", "Description", "Tag" }.Contains(name)) throw new ArgumentException("Attributo ammesso: Name, Description, Tag.");
                SetOutputs(da, Create(Elements(da, 0), values, clear, (e, v) => new IfcEdit(e, "attribute", name, v, type)));
                if (type) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Modifica del tipo condiviso: influenza tutte le istanze di quel tipo.");
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class SetPropertyComponent : ElementTool
    {
        public SetPropertyComponent() : base("IFC Set Property", "IFC Pset", "Prepara creazione/modifica di proprieta scalari. Mantiene tipo e unita delle proprieta esistenti. Le modifiche di istanza isolano i Pset condivisi.", "5 Edit", "PST") { }
        public override Guid ComponentGuid { get { return new Guid("BEEB272D-1CE2-461C-9771-E9366A837C02"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            ElementInput(p);
            p.AddTextParameter("Property Set", "S", "Nome esatto, non una chiave User Text. Es. Pset_WallCommon.", GH_ParamAccess.item);
            p.AddTextParameter("Property", "P", "Nome esatto. Es. FireRating. Quantity Set e proprieta complesse esclusi.", GH_ParamAccess.item);
            p.AddTextParameter("Values", "V", "Un valore per tutti o uno per elemento. Numeri con punto decimale; booleani true/false. Misure nelle unita IFC della proprieta/progetto.", GH_ParamAccess.list); p[3].Optional = true;
            p.AddTextParameter("Value type", "K", "Auto conserva il tipo esistente/template; nuove proprieta custom diventano IfcLabel. Oppure IfcText, IfcBoolean, IfcInteger, IfcReal, IfcLengthMeasure, IfcAreaMeasure, IfcVolumeMeasure...", GH_ParamAccess.item, "Auto");
            p.AddBooleanParameter("On type", "T", "Modifica il tipo condiviso e quindi tutte le sue istanze.", GH_ParamAccess.item, false);
            p.AddBooleanParameter("Clear", "C", "Imposta il valore a null, senza eliminare la proprieta.", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Changes", "C", "Richieste da unire e collegare a IFC Write File.", GH_ParamAccess.list);
            p.AddTextParameter("Preview", "P", "Anteprima delle richieste; validazione IFC completa al Write.", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                string pset = "", name = "", kind = "Auto"; bool type = false, clear = false; var values = new List<string>();
                if (!da.GetData(1, ref pset) || !da.GetData(2, ref name)) return;
                da.GetDataList(3, values); da.GetData(4, ref kind); da.GetData(5, ref type); da.GetData(6, ref clear);
                if (string.IsNullOrWhiteSpace(pset) || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Nomi Pset e proprieta obbligatori.");
                SetAttributeComponent.SetOutputs(da, SetAttributeComponent.Create(Elements(da, 0), values, clear, (e, v) => new IfcEdit(e, "property", name, v, type, pset, kind)));
                if (type) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Il tipo e condiviso: le sue istanze ereditano il valore salvo override di istanza.");
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class WriteIfcComponent : IfcComponent
    {
        private bool _lastWrite;
        private string _signature = "", _file = "", _report = "";
        public WriteIfcComponent() : base("IFC Write File", "IFC Write", "Applica Changes al sorgente incorporato ed esporta un nuovo .ifc. Originale protetto; valida schema e riapertura prima della pubblicazione. Il modello in ingresso resta invariato.", "5 Edit", "WIF") { }
        public override Guid ComponentGuid { get { return new Guid("BEEB272D-1CE2-461C-9771-E9366A837C03"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Model", "M", "Un solo modello da Read IFC.", GH_ParamAccess.list); p[0].DataMapping = GH_DataMapping.Flatten;
            p.AddGenericParameter("Changes", "C", "Unire richieste Set Attribute/Set Property. Conflitti sullo stesso campo bloccano l'intero export.", GH_ParamAccess.list); p[1].DataMapping = GH_DataMapping.Flatten; p[1].Optional = true;
            p.AddTextParameter("File", "F", "Nuovo percorso .ifc, diverso dall'originale.", GH_ParamAccess.item);
            p.AddBooleanParameter("Write", "W", "Button; scrive solo sul fronte False -> True.", GH_ParamAccess.item, false);
            p.AddBooleanParameter("Overwrite", "O", "Sostituisce la destinazione, mai il sorgente originale.", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Saved file", "F", "Ultimo percorso scritto per gli input correnti. Collegare a Read IFC e premere READ per ricaricarlo.", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "Report JSON della scrittura: prima/dopo, hash, schema, istanze del tipo interessate. Si azzera cambiando gli input.", GH_ParamAccess.item);
            p.AddTextParameter("Preview", "P", "Richieste correnti, anche prima del Write. Non equivale a validazione completa dello schema.", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            bool run = false; da.GetData(3, ref run); bool trigger = run && !_lastWrite; _lastWrite = run;
            try
            {
                var modelData = new List<object>(); var editData = new List<object>();
                da.GetDataList(0, modelData); da.GetDataList(1, editData);
                if (modelData.Count != 1) throw new ArgumentException("Esportare un solo modello per componente.");
                var model = Unwrap<IfcModel>(modelData[0]); var changes = editData.Select(Unwrap<IfcEdit>).ToList();
                if (model == null) throw new ArgumentException("Collegare un modello originale.");
                if (changes.Count == 0 && !trigger) { _file = ""; _report = ""; da.SetData(0, ""); da.SetData(1, ""); return; }
                IfcEditing.Validate(model, changes);
                da.SetDataList(2, changes.Select(c => c.ToString()));
                string path = ""; bool overwrite = false; if (!da.GetData(2, ref path)) return; da.GetData(4, ref overwrite);
                string signature = IfcModel.Hash(model.SourceKey + model.Metadata["source_sha256"] + path + overwrite + new JArray(changes.Select(c => c.Data)).ToString(Formatting.None));
                if (signature != _signature) { _file = ""; _report = ""; _signature = signature; }
                if (trigger) { _report = IfcEditing.Write(model, changes, path, overwrite); _file = (string)JObject.Parse(_report)["file"]; }
            }
            catch (Exception ex) { _file = ""; _report = ""; Error(ex); }
            da.SetData(0, _file); da.SetData(1, _report);
        }
    }
}
