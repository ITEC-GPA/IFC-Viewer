using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino;
using Rhino.DocObjects;

namespace IfcViewer
{
    public static class RhinoEdits
    {
        public static IfcElement Match(IfcModel model, RhinoObject obj)
        {
            if (model == null || obj == null) throw new ArgumentException("Modello o oggetto Rhino mancante.");
            string key = obj.Attributes.GetUserString("IFCViewer.Key");
            var candidates = model.Elements.Where(e => e.Key == key).ToList();
            if (candidates.Count != 1) throw new ArgumentException("Oggetto Rhino senza corrispondenza univoca nel modello selezionato: " + obj.Id);
            var element = candidates[0];
            if (obj.Attributes.GetUserString("IFC.SourceSHA256") != (string)model.Metadata["source_sha256"] ||
                obj.Attributes.GetUserString("IFC.GlobalId") != element.GlobalId || obj.Attributes.GetUserString("IFC.Class") != element.IfcClass ||
                obj.Attributes.GetUserString("IFC.StepId") != element.StepId.ToString(System.Globalization.CultureInfo.InvariantCulture))
                throw new ArgumentException("Bake di una versione diversa o identificativi alterati: " + obj.Id + ". Ricaricare il modello corretto.");
            return element;
        }
        private static string Decode(string key)
        {
            // Reverse only Meerkat's key encoding, once. Literal %2E is encoded %252E.
            return key.Replace("%2E", ".").Replace("%5B", "[").Replace("%5D", "]").Replace("%25", "%");
        }
        private static IfcEdit Request(IfcElement element, string key, string value)
        {
            if (key == "IFC.Name") return new IfcEdit(element, "attribute", "Name", value, false);
            foreach (string prefix in new[] { "IFC.Attribute.", "IFC.Type." })
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    string field = key.Substring(prefix.Length);
                    if (!new[] { "Name", "Description", "Tag" }.Contains(field)) throw new ArgumentException("Attributo User Text protetto/non supportato: " + key);
                    return new IfcEdit(element, "attribute", field, value, prefix == "IFC.Type.");
                }
            foreach (string prefix in new[] { "IFC.Property.", "IFC.InstanceProperty.", "IFC.TypeProperty." })
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    string[] parts = key.Substring(prefix.Length).Split('.');
                    if (parts.Length != 2 || parts.Any(p => p.Length == 0 || p.Contains("[") || p.Contains("]")) || parts[1] == "id") throw new ArgumentException("Chiave non scalare o identificativo interno: " + key);
                    return new IfcEdit(element, "property", Decode(parts[1]), value, prefix == "IFC.TypeProperty.", Decode(parts[0]));
                }
            throw new ArgumentException("Chiave User Text non modificabile: " + key);
        }
        public static List<IfcEdit> FromUserText(IfcModel model, RhinoDoc doc, IEnumerable<Guid> ids, IEnumerable<string> keys)
        {
            if (doc == null) throw new ArgumentException("Nessun documento Rhino attivo.");
            var selected = ids.Distinct().ToList(); var fields = keys.Distinct(StringComparer.Ordinal).ToList();
            if (selected.Count == 0 || fields.Count == 0) throw new ArgumentException("Selezionare esplicitamente oggetti Rhino e chiavi User Text.");
            var changes = new List<IfcEdit>();
            foreach (var id in selected)
            {
                var obj = doc.Objects.FindId(id); var element = Match(model, obj); var original = element.UserText();
                foreach (string key in fields)
                {
                    string value = obj.Attributes.GetUserString(key);
                    if (value == null) throw new ArgumentException("Chiave assente nell'oggetto " + id + ": " + key + ". Per impostare null usare Clear nei componenti Set.");
                    var change = Request(element, key, value); // Validate even unchanged protected fields.
                    string previous;
                    if (!original.TryGetValue(key, out previous) || value != previous) changes.Add(change);
                }
            }
            return changes;
        }
    }

    public sealed class UserTextChangesComponent : IfcComponent
    {
        public UserTextChangesComponent() : base("IFC Changes From Rhino", "IFC Rhino Edit", "Confronta User Text scelti esplicitamente con il modello IFC e prepara solo le differenze. Verifica provenienza e versione del bake. Non scrive file.", "5 Edit", "RHE") { }
        public override Guid ComponentGuid { get { return new Guid("BEEB272D-1CE2-461C-9771-E9366A837C04"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Model", "M", "Un solo modello originale da Read IFC.", GH_ParamAccess.list); p[0].DataMapping = GH_DataMapping.Flatten;
            p.AddTextParameter("Rhino IDs", "ID", "GUID espliciti degli oggetti del bake, ad esempio da Bake IFC o Read Baked.", GH_ParamAccess.list); p[1].DataMapping = GH_DataMapping.Flatten; p[1].Optional = true;
            p.AddTextParameter("Keys", "K", "Chiavi esatte da trasferire: IFC.Attribute.Name, IFC.Property.Pset_WallCommon.FireRating... Le chiavi IFC.TypeProperty modificano il tipo condiviso.", GH_ParamAccess.list); p[2].DataMapping = GH_DataMapping.Flatten;
            p.AddBooleanParameter("Refresh", "R", "Button dopo aver modificato User Text in Rhino.", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Changes", "C", "Solo differenze: collegare a IFC Write File.", GH_ParamAccess.list);
            p.AddTextParameter("Preview", "P", "Anteprima delle richieste; schema/tipi controllati al Write.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "R", "Conteggio delle differenze o indicazione di assenza di selezione.", GH_ParamAccess.item);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var models = new List<object>(); var ids = new List<string>(); var keys = new List<string>();
                da.GetDataList(0, models); da.GetDataList(1, ids); da.GetDataList(2, keys);
                if (models.Count != 1 || Unwrap<IfcModel>(models[0]) == null) throw new ArgumentException("Collegare un solo modello originale.");
                if (ids.Count == 0) { da.SetData(2, "Nessun oggetto selezionato. Effettuare il bake e collegare i GUID."); return; }
                var changes = RhinoEdits.FromUserText(Unwrap<IfcModel>(models[0]), RhinoDoc.ActiveDoc, ids.Select(Guid.Parse), keys);
                SetAttributeComponent.SetOutputs(da, changes); da.SetData(2, changes.Count + " differenze. Geometrie e campi non selezionati esclusi.");
                if (changes.Any(c => (string)c.Data["scope"] == "type")) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Sono presenti modifiche a tipi condivisi: possono influenzare istanze non selezionate.");
            }
            catch (Exception ex) { Error(ex); }
        }
    }
}
