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
    public abstract class ElementTool : IfcComponent
    {
        protected ElementTool(string name, string nick, string description, string panel, string icon) : base(name, nick, description, panel, icon) { }
        protected List<IfcElement> Elements(IGH_DataAccess da, int index)
        {
            var data = new List<object>();
            da.GetDataList(index, data);
            var result = data.Select(Unwrap<IfcElement>).ToList();
            if (result.Any(e => e == null)) throw new ArgumentException("Collegare elementi IFC da Read IFC o IFC Elements.");
            return result;
        }
        protected void ElementInput(GH_InputParamManager p) { p.AddGenericParameter("Elements", "E", "Lista di elementi IFC.", GH_ParamAccess.list); }
        protected void KeyInput(GH_InputParamManager p, string key) { p.AddTextParameter("Key", "K", "Chiave esatta da IFC Property Keys, es. IFC.Property.Pset_WallCommon.FireRating.", GH_ParamAccess.item, key); }
    }

    public sealed class ModelInfoComponent : IfcComponent
    {
        public ModelInfoComponent() : base("IFC Model Info", "IFC Info", "Schema, sorgenti, unita, conteggi e classi dei modelli caricati.", "1 Read", "INF") { }
        public override Guid ComponentGuid { get { return new Guid("E2B93E81-4253-4BB4-918A-5129BB880001"); } }
        protected override void RegisterInputParams(GH_InputParamManager p) { p.AddGenericParameter("Models", "M", "Modelli da Read IFC.", GH_ParamAccess.list); }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Names", "N", "Nome sorgente.", GH_ParamAccess.list);
            p.AddTextParameter("Schemas", "S", "Schema IFC.", GH_ParamAccess.list);
            p.AddIntegerParameter("Products", "P", "Numero prodotti.", GH_ParamAccess.list);
            p.AddIntegerParameter("Meshes", "G", "Numero mesh.", GH_ParamAccess.list);
            p.AddNumberParameter("Length to metres", "U", "Scala delle unita lineari IFC verso metri.", GH_ParamAccess.list);
            p.AddTextParameter("Classes", "C", "Classi uniche, ramo {modello}.", GH_ParamAccess.tree);
            p.AddIntegerParameter("Class counts", "Q", "Conteggi allineati a Classes.", GH_ParamAccess.tree);
            p.AddTextParameter("JSON", "J", "Metadati del modello, esclusi i prodotti.", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var input = new List<object>(); if (!da.GetDataList(0, input)) return;
                var models = input.Select(Unwrap<IfcModel>).ToList();
                if (models.Any(m => m == null)) throw new ArgumentException("Collegare Models da Read IFC.");
                var classes = new GH_Structure<GH_String>(); var counts = new GH_Structure<GH_Integer>();
                var summaries = new List<string>();
                for (int i = 0; i < models.Count; i++)
                {
                    var path = new GH_Path(i); classes.EnsurePath(path); counts.EnsurePath(path);
                    foreach (var group in models[i].Elements.GroupBy(e => e.IfcClass).OrderBy(g => g.Key, StringComparer.Ordinal))
                    { classes.Append(new GH_String(group.Key), path); counts.Append(new GH_Integer(group.Count()), path); }
                    var summary = new JObject(models[i].Metadata.Properties().Where(p => p.Name != "products").Select(p => new JProperty(p.Name, p.Value.DeepClone())));
                    summaries.Add(summary.ToString(Formatting.None));
                }
                da.SetDataList(0, models.Select(m => (string)m.Metadata["source_name"])); da.SetDataList(1, models.Select(m => (string)m.Metadata["schema"]));
                da.SetDataList(2, models.Select(m => m.Elements.Count)); da.SetDataList(3, models.Select(m => m.Elements.Count(e => e.MeshMetres != null)));
                da.SetDataList(4, models.Select(m => (double)m.Metadata["length_unit_to_metres"])); da.SetDataTree(5, classes); da.SetDataTree(6, counts); da.SetDataList(7, summaries);
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class PropertyKeysComponent : ElementTool
    {
        public PropertyKeysComponent() : base("IFC Property Keys", "IFC Keys", "Elenco unificato delle chiavi e numero di oggetti in cui ogni chiave e presente.", "2 Query", "KEY") { }
        public override Guid ComponentGuid { get { return new Guid("E2B93E81-4253-4BB4-918A-5129BB880002"); } }
        protected override void RegisterInputParams(GH_InputParamManager p) { ElementInput(p); p.AddTextParameter("Search", "S", "Testo contenuto nel nome della chiave.", GH_ParamAccess.item, ""); }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Keys", "K", "Chiavi esatte da usare negli altri componenti.", GH_ParamAccess.list);
            p.AddIntegerParameter("Occurrences", "N", "Numero di elementi con la chiave (anche se il valore e null).", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var elements = Elements(da, 0); string search = ""; da.GetData(1, ref search);
                var keys = elements.SelectMany(e => e.UserText().Keys).Where(k => k.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    .GroupBy(k => k, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
                da.SetDataList(0, keys.Select(g => g.Key)); da.SetDataList(1, keys.Select(g => g.Count()));
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class PropertyValueComponent : ElementTool
    {
        public PropertyValueComponent() : base("IFC Property Value", "IFC Value", "Estrae un parametro per ogni oggetto mantenendo allineamento e valori mancanti.", "2 Query", "VAL") { }
        public override Guid ComponentGuid { get { return new Guid("E2B93E81-4253-4BB4-918A-5129BB880003"); } }
        protected override void RegisterInputParams(GH_InputParamManager p) { ElementInput(p); KeyInput(p, "IFC.Name"); }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Values", "V", "Valore testuale; stringa vuota se assente.", GH_ParamAccess.list);
            p.AddBooleanParameter("Found", "F", "True se la chiave esiste; un valore null resta presente.", GH_ParamAccess.list);
            p.AddNumberParameter("Numbers", "N", "Numeri nelle unita IFC originali; NaN se assente/non numerico.", GH_ParamAccess.list);
            p.AddBooleanParameter("Is numeric", "NUM", "Maschera dei valori numerici finiti.", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var elements = Elements(da, 0); string key = "IFC.Name"; da.GetData(1, ref key);
                var values = new List<string>(); var found = new List<bool>(); var numbers = new List<double>(); var numeric = new List<bool>();
                foreach (var e in elements)
                {
                    string value; bool exists = new PropertyRow(e).Get(key, out value); double number;
                    bool isNumber = DataTools.Number(value, out number);
                    values.Add(exists ? value : ""); found.Add(exists); numbers.Add(isNumber ? number : double.NaN); numeric.Add(isNumber);
                }
                da.SetDataList(0, values); da.SetDataList(1, found); da.SetDataList(2, numbers); da.SetDataList(3, numeric);
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class FilterPropertyComponent : ElementTool
    {
        public FilterPropertyComponent() : base("IFC Filter Property", "IFC Filter", "Separa gli oggetti in base a un parametro. Collegare piu filtri in serie per combinare condizioni AND.", "2 Query", "FLT") { }
        public override Guid ComponentGuid { get { return new Guid("E2B93E81-4253-4BB4-918A-5129BB880004"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            ElementInput(p); KeyInput(p, "IFC.Class");
            p.AddTextParameter("Operator", "OP", "exists, missing, =, !=, contains, starts with, >, >=, <, <=. Gli operatori numerici usano valori nelle unita IFC originali.", GH_ParamAccess.item, "=");
            p.AddTextParameter("Value", "V", "Valore atteso; per i numeri usare il punto decimale.", GH_ParamAccess.item, "IfcWall");
            p.AddBooleanParameter("Case sensitive", "CS", "Distingue maiuscole/minuscole nei valori testuali.", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Matched", "YES", "Elementi che soddisfano il filtro.", GH_ParamAccess.list);
            p.AddGenericParameter("Rejected", "NO", "Altri elementi.", GH_ParamAccess.list);
            p.AddBooleanParameter("Mask", "M", "Maschera allineata all'input.", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var elements = Elements(da, 0); string key = "IFC.Class", op = "=", value = "IfcWall"; bool sensitive = false;
                da.GetData(1, ref key); da.GetData(2, ref op); da.GetData(3, ref value); da.GetData(4, ref sensitive);
                var predicate = DataTools.Predicate(key, op, value, sensitive); var mask = elements.Select(e => predicate(new PropertyRow(e))).ToList();
                da.SetDataList(0, elements.Where((e, i) => mask[i])); da.SetDataList(1, elements.Where((e, i) => !mask[i])); da.SetDataList(2, mask);
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class SelectIdsComponent : ElementTool
    {
        public SelectIdsComponent() : base("IFC Select IDs", "IFC IDs", "Seleziona GlobalId, #STEP-ID oppure chiavi sorgente:GlobalId. I GlobalId distinguono maiuscole/minuscole.", "2 Query", "ID") { }
        public override Guid ComponentGuid { get { return new Guid("E2B93E81-4253-4BB4-918A-5129BB880005"); } }
        protected override void RegisterInputParams(GH_InputParamManager p) { ElementInput(p); p.AddTextParameter("IDs", "ID", "GlobalId, #42 o chiave completa (sorgente:GlobalId).", GH_ParamAccess.list); }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Elements", "E", "Elementi selezionati nell'ordine originale.", GH_ParamAccess.list);
            p.AddIntegerParameter("Indices", "I", "Indici nella lista di input.", GH_ParamAccess.list);
            p.AddTextParameter("Not found", "MISS", "Identificativi senza corrispondenza.", GH_ParamAccess.list);
            p.AddTextParameter("Ambiguous", "MULTI", "Identificativi che selezionano piu elementi (es. modelli federati).", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var elements = Elements(da, 0); var requested = new List<string>(); if (!da.GetDataList(1, requested)) return;
                var lookup = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
                for (int i = 0; i < elements.Count; i++)
                    foreach (string id in new[] { elements[i].GlobalId, "#" + elements[i].StepId, elements[i].Key }.Where(s => !string.IsNullOrEmpty(s)))
                    { HashSet<int> indices; if (!lookup.TryGetValue(id, out indices)) { indices = new HashSet<int>(); lookup.Add(id, indices); } indices.Add(i); }
                var selected = new HashSet<int>(); var missing = new List<string>(); var ambiguous = new List<string>();
                foreach (string id in requested.Select(s => s.Trim()).Distinct(StringComparer.Ordinal))
                { HashSet<int> indices; if (!lookup.TryGetValue(id, out indices)) missing.Add(id); else { selected.UnionWith(indices); if (indices.Count > 1) ambiguous.Add(id); } }
                var ordered = selected.OrderBy(i => i).ToList(); da.SetDataList(0, ordered.Select(i => elements[i])); da.SetDataList(1, ordered); da.SetDataList(2, missing); da.SetDataList(3, ambiguous);
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class GroupByComponent : ElementTool
    {
        public GroupByComponent() : base("IFC Group By", "IFC Group", "Raggruppa per classe, piano, materiale o qualsiasi parametro. Ogni gruppo ha un ramo.", "2 Query", "GRP") { }
        public override Guid ComponentGuid { get { return new Guid("E2B93E81-4253-4BB4-918A-5129BB880006"); } }
        protected override void RegisterInputParams(GH_InputParamManager p) { ElementInput(p); KeyInput(p, "IFC.Class"); }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Groups", "E", "Elementi per ramo {gruppo}.", GH_ParamAccess.tree);
            p.AddTextParameter("Labels", "L", "Valore del parametro per gruppo.", GH_ParamAccess.list);
            p.AddIntegerParameter("Counts", "N", "Numero oggetti per gruppo.", GH_ParamAccess.list);
            p.AddBooleanParameter("Key exists", "F", "False identifica il gruppo delle chiavi assenti.", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var elements = Elements(da, 0); string key = "IFC.Class"; da.GetData(1, ref key);
                var groups = DataTools.Groups(elements.Select(e => new PropertyRow(e)), key); var tree = new GH_Structure<GH_ObjectWrapper>();
                for (int i = 0; i < groups.Count; i++) foreach (var e in groups[i].Elements) tree.Append(new GH_ObjectWrapper(e), new GH_Path(i));
                da.SetDataTree(0, tree); da.SetDataList(1, groups.Select(g => g.Value)); da.SetDataList(2, groups.Select(g => g.Elements.Count)); da.SetDataList(3, groups.Select(g => g.Found));
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class SortElementsComponent : ElementTool
    {
        public SortElementsComponent() : base("IFC Sort", "IFC Sort", "Ordina stabilmente gli oggetti per un parametro, come testo o numero. Valori assenti/non numerici in fondo.", "2 Query", "SRT") { }
        public override Guid ComponentGuid { get { return new Guid("E2B93E81-4253-4BB4-918A-5129BB880007"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        { ElementInput(p); KeyInput(p, "IFC.Name"); p.AddBooleanParameter("Numeric", "N", "Ordinamento numerico nelle unita originarie.", GH_ParamAccess.item, false); p.AddBooleanParameter("Descending", "D", "Ordine decrescente.", GH_ParamAccess.item, false); }
        protected override void RegisterOutputParams(GH_OutputParamManager p) { p.AddGenericParameter("Elements", "E", "Elementi ordinati.", GH_ParamAccess.list); }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try { var elements = Elements(da, 0); string key = "IFC.Name"; bool numeric = false, desc = false; da.GetData(1, ref key); da.GetData(2, ref numeric); da.GetData(3, ref desc); da.SetDataList(0, DataTools.Sort(elements.Select(e => new PropertyRow(e)), key, numeric, desc)); }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class ValidateElementsComponent : ElementTool
    {
        public ValidateElementsComponent() : base("IFC Check Data", "IFC Check", "Controlla parametri richiesti, GlobalId duplicati/mancanti e disponibilita delle mesh. Non e una validazione normativa IFC.", "5 Check", "CHK") { }
        public override Guid ComponentGuid { get { return new Guid("E2B93E81-4253-4BB4-918A-5129BB88000A"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        { ElementInput(p); p.AddTextParameter("Required keys", "K", "Parametri che devono avere un valore non vuoto/null.", GH_ParamAccess.list); p[1].Optional = true; p.AddBooleanParameter("Require geometry", "G", "Richiede una mesh per ogni elemento.", GH_ParamAccess.item, false); }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Passed", "OK", "Elementi senza problemi rilevati.", GH_ParamAccess.list);
            p.AddGenericParameter("Failed", "FAIL", "Elementi con problemi.", GH_ParamAccess.list);
            p.AddBooleanParameter("Valid", "V", "Maschera allineata alla lista di input.", GH_ParamAccess.list);
            p.AddTextParameter("Issues", "R", "Problemi per ramo {indice originale}; ramo vuoto se valido.", GH_ParamAccess.tree);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var elements = Elements(da, 0); var required = new List<string>(); bool geometry = false; da.GetDataList(1, required); da.GetData(2, ref geometry);
                var duplicate = new HashSet<string>(elements.GroupBy(e => e.Key).Where(g => g.Count() > 1).Select(g => g.Key));
                var mask = new List<bool>(); var tree = new GH_Structure<GH_String>();
                for (int i = 0; i < elements.Count; i++)
                {
                    var issues = DataTools.Issues(elements[i], required.Where(k => !string.IsNullOrWhiteSpace(k)), geometry, duplicate.Contains(elements[i].Key));
                    mask.Add(issues.Count == 0); var path = new GH_Path(i); tree.EnsurePath(path); foreach (string issue in issues) tree.Append(new GH_String(issue), path);
                }
                da.SetDataList(0, elements.Where((e, i) => mask[i])); da.SetDataList(1, elements.Where((e, i) => !mask[i])); da.SetDataList(2, mask); da.SetDataTree(3, tree);
            }
            catch (Exception ex) { Error(ex); }
        }
    }
}
