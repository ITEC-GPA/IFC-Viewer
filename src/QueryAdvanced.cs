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
    public sealed class RelationshipsComponent : ElementTool
    {
        public RelationshipsComponent() : base("IFC Relationships", "IFC Relations", "Naviga relazioni esplicite IFC. Un ramo per elemento iniziale. I tipi e gruppi restituiti hanno metadati anche senza mesh.", "2 Query", "REL") { }
        public override Guid ComponentGuid { get { return new Guid("A33C7151-8190-4A00-A001-100000000004"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            ElementInput(p);
            p.AddTextParameter("Relation", "R", "Type, Instances, Container, Parent, Parts, Groups, Members, Openings, Fillings, Connections.", GH_ParamAccess.item, "Container");
            p.AddBooleanParameter("Recursive", "D", "Segue ricorsivamente la stessa relazione, senza cicli.", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        { p.AddGenericParameter("Related elements", "E", "Albero {i} di elementi correlati.", GH_ParamAccess.tree); p.AddTextParameter("Details", "D", "Riferimenti e metadati.", GH_ParamAccess.tree); }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var elements = Elements(da, 0); var model = IfcTools.SingleModel(elements);
                string relation = "Container"; bool recursive = false; da.GetData(1, ref relation); da.GetData(2, ref recursive);
                var result = IfcTools.Run(model, "relationships", new JObject(new JProperty("relation", relation), new JProperty("recursive", recursive)), elements);
                var tree = new GH_Structure<GH_ObjectWrapper>(); var details = new GH_Structure<GH_String>(); int i = 0;
                foreach (JArray branch in result["branches"])
                {
                    var path = new GH_Path(i++); tree.EnsurePath(path); details.EnsurePath(path);
                    foreach (JObject record in branch)
                    {
                        var found = model.Elements.FirstOrDefault(e => e.StepId == (int)record["step_id"]);
                        var element = found ?? new IfcElement { Model = model, Record = record };
                        tree.Append(new GH_ObjectWrapper(element), path); details.Append(new GH_String(record.ToString(Formatting.None)), path);
                    }
                }
                da.SetDataTree(0, tree); da.SetDataTree(1, details);
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class ImportTableComponent : IfcComponent
    {
        public ImportTableComponent() : base("IFC Import Table", "IFC Table", "Prepara modifiche da CSV con IFC.GlobalId e colonne Meerkat scelte esplicitamente. Un errore blocca tutto il batch; non scrive l'IFC.", "5 Edit", "TAB") { }
        public override Guid ComponentGuid { get { return new Guid("A33C7151-8190-4A00-A001-100000000005"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            ModelInput(p); p.AddTextParameter("CSV", "F", "Percorso CSV UTF-8 oppure testo CSV multilinea. Header IFC.GlobalId obbligatorio.", GH_ParamAccess.item);
            p.AddTextParameter("Columns", "K", "Colonne modificabili da importare: IFC.Attribute.Name/Description/Tag, IFC.Property.Pset.Property, IFC.TypeProperty.Pset.Property.", GH_ParamAccess.list);
            p.AddTextParameter("Separator", "S", "Separatore CSV.", GH_ParamAccess.item, ";");
            p.AddBooleanParameter("Read", "R", "True legge e valida la tabella. Ricalcolare dopo modifiche al file.", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Changes", "C", "Solo differenze valide; vuoto se esistono errori.", GH_ParamAccess.list);
            p.AddBooleanParameter("Valid", "OK", "Intera tabella valida.", GH_ParamAccess.item);
            p.AddTextParameter("Report", "R", "Esito riga per riga e conflitti.", GH_ParamAccess.item);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                bool run = false; da.GetData(4, ref run); if (!run) return;
                var model = IfcTools.Model(da, 0); string csv = "", sep = ";"; var columns = new List<string>();
                if (!da.GetData(1, ref csv) || !da.GetDataList(2, columns)) return; da.GetData(3, ref sep);
                var result = IfcTools.Run(model, "table", new JObject(new JProperty("csv", csv), new JProperty("columns", new JArray(columns)), new JProperty("separator", sep)));
                da.SetDataList(0, result["changes"].Cast<JObject>().Select(c => IfcEdit.Request(model.Elements.Single(e => e.StepId == (int)c["step_id"]), c)));
                da.SetData(1, (bool)result["valid"]); da.SetData(2, result.ToString());
                if (!(bool)result["valid"]) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Importazione bloccata: correggere gli errori nel Report.");
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class ValidateIdsComponent : IfcComponent
    {
        public ValidateIdsComponent() : base("IFC Validate IDS", "IFC IDS", "Verifica requisiti informativi IDS con IfcTester. Passed/Failed contengono i prodotti applicabili; il report include anche errori globali e oggetti non geometrici.", "3 Check", "IDS") { }
        public override Guid ComponentGuid { get { return new Guid("A33C7151-8190-4A00-A001-100000000006"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            ModelInput(p); p.AddTextParameter("IDS file", "F", "File .ids valido.", GH_ParamAccess.item);
            p.AddBooleanParameter("Run", "R", "Esegue la verifica.", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Passed", "P", "Prodotti applicabili senza requisiti falliti.", GH_ParamAccess.list);
            p.AddGenericParameter("Failed", "F", "Prodotti con requisiti falliti.", GH_ParamAccess.list);
            p.AddBooleanParameter("Valid", "OK", "Esito globale, inclusi requisiti senza elementi applicabili e schema.", GH_ParamAccess.item);
            p.AddTextParameter("Issues", "I", "Motivi, GlobalId e specifiche.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "R", "Report JSON completo IfcTester.", GH_ParamAccess.item);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                bool run = false; da.GetData(2, ref run); if (!run) return; var model = IfcTools.Model(da, 0); string path = "";
                if (!da.GetData(1, ref path)) return;
                var result = IfcTools.Run(model, "ids", new JObject(new JProperty("ids_file", path)));
                da.SetDataList(0, IfcTools.Resolve(model, result["passed"])); da.SetDataList(1, IfcTools.Resolve(model, result["failed"]));
                da.SetData(2, (bool)result["valid"]); da.SetDataList(3, result["issues"].Select(i => i.ToString(Formatting.None))); da.SetData(4, result["report"].ToString());
            }
            catch (Exception ex) { Error(ex); }
        }
    }
}
