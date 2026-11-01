using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IfcViewer
{
    public static class IfcTools
    {
        public static IfcModel SingleModel(IList<IfcElement> elements)
        {
            if (elements.Count == 0) throw new ArgumentException("Selezione vuota.");
            var model = elements[0].Model;
            if (elements.Any(e => e.Model.SourceKey != model.SourceKey || (string)e.Model.Metadata["source_sha256"] != (string)model.Metadata["source_sha256"]))
                throw new ArgumentException("Un solo modello/versione per componente.");
            return model;
        }
        public static JObject Run(IfcModel model, string operation, JObject request, IEnumerable<IfcElement> elements = null)
        {
            request = (JObject)request.DeepClone();
            request["operation"] = operation; request["source_sha256"] = model.Metadata["source_sha256"];
            if (elements != null) request["elements"] = JArray.FromObject(elements.Select(e => new { step_id = e.StepId, global_id = e.GlobalId }));
            string path = Path.Combine(Path.GetTempPath(), "Meerkat", Guid.NewGuid().ToString("N") + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            try
            {
                File.WriteAllText(path, request.ToString(), new UTF8Encoding(false));
                var start = new ProcessStartInfo(Engine.PythonPath(), Engine.Quote(Path.Combine(Engine.PluginDirectory, "ifc_tools.py")) + " " + Engine.Quote(model.ArchivePath) + " " + Engine.Quote(path))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
                start.EnvironmentVariables["PYTHONUTF8"] = "1";
                using (var process = Process.Start(start))
                {
                    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(30 * 60 * 1000)) { process.Kill(); process.WaitForExit(); throw new TimeoutException("Operazione IFC interrotta dopo 30 minuti."); }
                    if (process.ExitCode != 0) throw new InvalidOperationException(stderr.Result);
                    return JObject.Parse(stdout.Result);
                }
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        internal static IfcModel Model(IGH_DataAccess da, int index)
        {
            var items = new List<object>(); da.GetDataList(index, items);
            if (items.Count != 1 || IfcComponent.Unwrap<IfcModel>(items[0]) == null) throw new ArgumentException("Collegare un solo Model.");
            return IfcComponent.Unwrap<IfcModel>(items[0]);
        }
        internal static IEnumerable<IfcElement> Resolve(IfcModel model, JToken ids)
        { var set = new HashSet<int>(ids.Values<int>()); return model.Elements.Where(e => set.Contains(e.StepId)); }
        internal static string ResolveFile(GH_Component component, string path)
        {
            if (Path.IsPathRooted(path)) return path;
            var document = component.OnPingDocument();
            if (document != null && !string.IsNullOrEmpty(document.FilePath))
            {
                string candidate = Path.Combine(Path.GetDirectoryName(document.FilePath), path);
                if (File.Exists(candidate)) return candidate;
            }
            string distributed = Path.Combine(Engine.PluginDirectory, "examples", path);
            return File.Exists(distributed) ? distributed : Path.GetFullPath(path);
        }
    }

    public sealed class PropertyUnitsComponent : ElementTool
    {
        public PropertyUnitsComponent() : base("IFC Property Units", "IFC Units", "Unita effettive, conversione SI o in unita esplicite e controllo dimensionale. Un ramo/valore per elemento; errori indicati da Valid.", "2 Query", "UNT") { }
        public override Guid ComponentGuid { get { return new Guid("A33C7151-8190-4A00-A001-100000000001"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            ElementInput(p); p.AddTextParameter("Property Set", "S", "Pset o Qto esatto.", GH_ParamAccess.item);
            p.AddTextParameter("Property", "P", "Nome proprieta o quantita.", GH_ParamAccess.item);
            p.AddTextParameter("Target unit", "U", "SI, m, mm, cm, ft, in, m2, mm2, cm2, m3, mm3, L, kg, g, t, s, min, h, K, degC, N, kN, Pa, kPa, MPa, 1.", GH_ParamAccess.item, "SI");
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddNumberParameter("Values", "V", "Valori convertiti; NaN se non convertibili.", GH_ParamAccess.list);
            p.AddTextParameter("Source units", "U", "Unita IFC effettive.", GH_ParamAccess.list);
            p.AddBooleanParameter("Valid", "OK", "True per conversioni valide.", GH_ParamAccess.list);
            p.AddTextParameter("Details", "D", "Valore originale, dimensioni o motivo di errore.", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                string set = "", name = "", unit = "SI"; if (!da.GetData(1, ref set) || !da.GetData(2, ref name)) return; da.GetData(3, ref unit);
                var elements = Elements(da, 0); var rows = (JArray)IfcTools.Run(IfcTools.SingleModel(elements), "units", new JObject(new JProperty("pset", set), new JProperty("name", name), new JProperty("unit", unit)), elements)["rows"];
                da.SetDataList(0, rows.Select(r => (bool)r["valid"] ? (double)r["converted"] : double.NaN));
                da.SetDataList(1, rows.Select(r => (string)r["unit"] ?? "")); da.SetDataList(2, rows.Select(r => (bool)r["valid"]));
                da.SetDataList(3, rows.Select(r => r.ToString(Formatting.None)));
                if (rows.Any(r => !(bool)r["valid"])) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Alcuni valori non sono convertibili: consultare Details.");
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class SetQuantityComponent : ElementTool
    {
        public SetQuantityComponent() : base("IFC Set Quantity", "IFC Qto", "Prepara quantita con tipo, unita e metodo espliciti. Non deduce superfici nette/lorde dalla mesh.", "5 Edit", "QTO") { }
        public override Guid ComponentGuid { get { return new Guid("A33C7151-8190-4A00-A001-100000000002"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            ElementInput(p);
            p.AddTextParameter("Quantity Set", "S", "Nome Qto standard o set custom.", GH_ParamAccess.item);
            p.AddTextParameter("Quantity", "Q", "Nome quantita.", GH_ParamAccess.item);
            p.AddNumberParameter("Values", "V", "Un valore per tutti o uno per elemento.", GH_ParamAccess.list);
            p.AddTextParameter("Quantity type", "T", "Length, Area, Volume, Mass, Time, Count.", GH_ParamAccess.item, "Volume");
            p.AddTextParameter("Input unit", "U", "Unita dei Values: SI, m3, m2, mm... oppure IFC per l'unita effettiva originale.", GH_ParamAccess.item, "SI");
            p.AddTextParameter("Measurement method", "M", "Metodo di misura applicabile all'intero Quantity Set. Obbligatorio.", GH_ParamAccess.item);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        { p.AddGenericParameter("Changes", "C", "Richieste per Preview Changes / Write File.", GH_ParamAccess.list); p.AddTextParameter("Preview", "P", "Richieste correnti.", GH_ParamAccess.list); }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                string set = "", name = "", kind = "Volume", unit = "SI", method = ""; var values = new List<double>();
                if (!da.GetData(1, ref set) || !da.GetData(2, ref name) || !da.GetDataList(3, values) || !da.GetData(6, ref method)) return;
                da.GetData(4, ref kind); da.GetData(5, ref unit);
                var changes = SetAttributeComponent.Create(Elements(da, 0), values.Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).ToList(), false,
                    (e, v) => IfcEdit.Request(e, new JObject(new JProperty("kind", "quantity"), new JProperty("name", name), new JProperty("value", v), new JProperty("pset", set),
                        new JProperty("quantity_type", kind), new JProperty("input_unit", unit), new JProperty("method", method))));
                SetAttributeComponent.SetOutputs(da, changes);
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class PreviewChangesComponent : IfcComponent
    {
        public PreviewChangesComponent() : base("IFC Preview Changes", "IFC Preview", "Applica Changes a una copia temporanea, valida e rilegge l'IFC. Nessun file destinazione pubblicato. Il modello risultante puo essere analizzato e incorporato in un nuovo Read IFC.", "5 Edit", "PRV") { }
        public override Guid ComponentGuid { get { return new Guid("A33C7151-8190-4A00-A001-100000000003"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            ModelInput(p); p.AddGenericParameter("Changes", "C", "Richieste da applicare alla copia.", GH_ParamAccess.list); p[1].DataMapping = GH_DataMapping.Flatten;
            p.AddBooleanParameter("Run", "R", "True calcola anteprima; puo richiedere tempo su file grandi.", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Preview model", "M", "Modello temporaneo modificato.", GH_ParamAccess.item);
            p.AddGenericParameter("Elements", "E", "Tutti gli elementi della copia.", GH_ParamAccess.list);
            p.AddGenericParameter("Affected before", "B", "Istanze richieste e istanze dei tipi modificati, prima.", GH_ParamAccess.list);
            p.AddGenericParameter("Affected after", "A", "Stesse istanze, dopo. Include istanze con override locali.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "R", "Modifiche applicate e valori prima/dopo.", GH_ParamAccess.item);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                bool run = false; da.GetData(2, ref run); if (!run) return;
                var model = IfcTools.Model(da, 0); var data = new List<object>(); da.GetDataList(1, data);
                var changes = data.Select(Unwrap<IfcEdit>).ToList(); IfcEditing.Validate(model, changes);
                string folder = Path.Combine(Path.GetTempPath(), "Meerkat", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "preview.ifc");
                try
                {
                    var report = JObject.Parse(IfcEditing.Write(model, changes, path, false)); var after = Engine.Read(path);
                    // Retain the logical source path so downstream writers still
                    // protect the user's original, including after GH serialization.
                    after.Metadata["source_path"] = model.Metadata["source_path"];
                    after.Metadata["source_name"] = model.Metadata["source_name"];
                    using (var zip = ZipFile.Open(after.ArchivePath, ZipArchiveMode.Update))
                    {
                        zip.GetEntry("model.json").Delete();
                        using (var writer = new StreamWriter(zip.CreateEntry("model.json").Open(), new UTF8Encoding(false))) writer.Write(after.Metadata.ToString());
                    }
                    var ids = new HashSet<string>(changes.Select(c => (string)c.Data["global_id"]));
                    foreach (JObject change in report["changes"])
                    {
                        string target = (string)change["target_global_id"];
                        foreach (var e in model.Elements.Where(e => e.Record["type_object"] is JObject && (string)e.Record["type_object"]["GlobalId"] == target)) ids.Add(e.GlobalId);
                    }
                    da.SetData(0, after); da.SetDataList(1, after.Elements);
                    da.SetDataList(2, model.Elements.Where(e => ids.Contains(e.GlobalId)));
                    da.SetDataList(3, after.Elements.Where(e => ids.Contains(e.GlobalId)));
                    report.Remove("file"); da.SetData(4, report.ToString());
                }
                finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(folder); }
            }
            catch (Exception ex) { Error(ex); }
        }
    }
}
