using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IfcViewer
{
    public sealed class MaterialsLayersComponent : ElementTool
    {
        public MaterialsLayersComponent() : base("IFC Materials & Layers", "IFC Materials", "Materiali, stratigrafie, spessori in metri e profili. Conserva ordine e distingue assegnazione di istanza/tipo.", "2 Query", "MAT") { }
        public override Guid ComponentGuid { get { return new Guid("A33C7151-8190-4A00-A001-100000000007"); } }
        protected override void RegisterInputParams(GH_InputParamManager p) { ElementInput(p); }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Material names", "N", "Un ramo per elemento, ordine degli strati/componenti.", GH_ParamAccess.tree);
            p.AddNumberParameter("Thickness metres", "T", "Spessore in m; NaN per materiali senza strato.", GH_ParamAccess.tree);
            p.AddTextParameter("Profiles", "P", "Profilo IFC JSON; vuoto se assente. Dimensioni originali IFC nel JSON.", GH_ParamAccess.tree);
            p.AddTextParameter("Scope", "S", "instance, type oppure none.", GH_ParamAccess.list);
            p.AddTextParameter("Details", "D", "Assegnazione, offset/direzioni, frazioni, proprieta materiali e grafo profili.", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var elements = Elements(da, 0); var rows = (JArray)IfcTools.Run(IfcTools.SingleModel(elements), "materials", new JObject(), elements)["rows"];
                var names = new GH_Structure<GH_String>(); var thickness = new GH_Structure<GH_Number>(); var profiles = new GH_Structure<GH_String>();
                for (int i = 0; i < rows.Count; i++)
                {
                    var path = new GH_Path(i); names.EnsurePath(path); thickness.EnsurePath(path); profiles.EnsurePath(path);
                    foreach (var item in rows[i]["items"])
                    {
                        names.Append(new GH_String((string)item["name"] ?? ""), path);
                        thickness.Append(new GH_Number(item["thickness_metres"].Type == JTokenType.Null ? double.NaN : (double)item["thickness_metres"]), path);
                        profiles.Append(new GH_String(item["profile"].Type == JTokenType.Null ? "" : item["profile"].ToString(Formatting.None)), path);
                    }
                }
                da.SetDataTree(0, names); da.SetDataTree(1, thickness); da.SetDataTree(2, profiles);
                da.SetDataList(3, rows.Select(r => (string)r["scope"])); da.SetDataList(4, rows.Select(r => r.ToString(Formatting.None)));
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class AssignTypeComponent : ElementTool
    {
        public AssignTypeComponent() : base("IFC Assign Type", "IFC Type", "Prepara riassegnazione o duplicazione del tipo. Controllo di compatibilita al Preview/Write; proprieta di istanza conservate.", "5 Edit", "TYP") { }
        public override Guid ComponentGuid { get { return new Guid("A33C7151-8190-4A00-A001-100000000008"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            ElementInput(p);
            p.AddTextParameter("Type GlobalId", "T", "Tipo nello stesso modello. Vuoto usa il tipo corrente.", GH_ParamAccess.item, "");
            p.AddBooleanParameter("Duplicate", "D", "Duplica il tipo con nuovi GlobalId e Pset indipendenti. Stesso tipo/nome produce una copia condivisa dalla selezione.", GH_ParamAccess.item, false);
            p.AddTextParameter("New type name", "N", "Nome obbligatorio per Duplicate.", GH_ParamAccess.item, "");
            p.AddBooleanParameter("Apply type geometry", "G", "Accetta la geometria mappata del tipo destinazione. Materiali/usages seguono la nuova assegnazione; Qto non ricalcolate.", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        { p.AddGenericParameter("Changes", "C", "Richieste di assegnazione per Preview/Write. Separare da altre modifiche agli stessi oggetti/tipi.", GH_ParamAccess.list); p.AddTextParameter("Preview", "P", "Richieste correnti.", GH_ParamAccess.list); }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var elements = Elements(da, 0); IfcTools.SingleModel(elements); string type = "", name = ""; bool duplicate = false, geometry = false;
                da.GetData(1, ref type); da.GetData(2, ref duplicate); da.GetData(3, ref name); da.GetData(4, ref geometry);
                var changes = elements.Select(e => IfcEdit.Request(e, new JObject(new JProperty("kind", "assign_type"), new JProperty("name", "Type"),
                    new JProperty("value", duplicate ? name : type), new JProperty("type_global_id", type), new JProperty("duplicate", duplicate),
                    new JProperty("type_name", name), new JProperty("map_geometry", geometry)))).ToList();
                SetAttributeComponent.SetOutputs(da, changes);
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class ExtractModelComponent : ElementTool
    {
        private bool _lastWrite;
        private string _signature = "", _file = "", _report = "";
        public ExtractModelComponent() : base("IFC Extract Model", "IFC Extract", "Esporta la selezione e le dipendenze in un nuovo IFC: antenati, host/aperture, tipi, materiali. Valida e riapre prima della pubblicazione.", "4 Export", "EXT") { }
        public override Guid ComponentGuid { get { return new Guid("A33C7151-8190-4A00-A001-100000000009"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            ElementInput(p); p[0].DataMapping = GH_DataMapping.Flatten;
            p.AddTextParameter("File", "F", "Nuovo percorso .ifc.", GH_ParamAccess.item);
            p.AddBooleanParameter("Include descendants", "D", "Include parti/figli della selezione. Gli antenati necessari e le aperture degli host sono sempre inclusi.", GH_ParamAccess.item, true);
            p.AddBooleanParameter("Write", "W", "Button: scrive su False -> True.", GH_ParamAccess.item, false);
            p.AddBooleanParameter("Overwrite", "O", "Sostituisce destinazione, mai il sorgente.", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        { p.AddTextParameter("Saved file", "F", "Percorso esportato.", GH_ParamAccess.item); p.AddTextParameter("Report", "R", "Dipendenze incluse, conteggi e hash.", GH_ParamAccess.item); }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            bool run = false; da.GetData(3, ref run); bool trigger = run && !_lastWrite; _lastWrite = run;
            try
            {
                var elements = Elements(da, 0); var model = IfcTools.SingleModel(elements); string file = ""; bool descendants = true, overwrite = false;
                if (!da.GetData(1, ref file)) return; da.GetData(2, ref descendants); da.GetData(4, ref overwrite);
                string signature = model.SourceKey + model.Metadata["source_sha256"] + file + descendants + overwrite + string.Join(",", elements.Select(e => e.StepId));
                if (_signature != signature) { _file = ""; _report = ""; _signature = signature; }
                if (trigger)
                {
                    var result = IfcTools.Run(model, "extract", new JObject(new JProperty("destination", file), new JProperty("descendants", descendants), new JProperty("overwrite", overwrite)), elements);
                    _file = (string)result["file"]; _report = result.ToString();
                }
            }
            catch (Exception ex) { _file = ""; _report = ""; Error(ex); }
            da.SetData(0, _file); da.SetData(1, _report);
        }
    }
}
