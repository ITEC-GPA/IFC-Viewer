using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace IfcViewer
{
    public sealed class ExportCsvComponent : ElementTool
    {
        private bool _lastWrite;
        private string _file = "";
        public ExportCsvComponent() : base("IFC Export CSV", "IFC CSV", "Esporta un abaco UTF-8 con un oggetto per riga e parametri per colonna. Scrittura solo su fronte False -> True.", "4 Save", "CSV") { }
        public override Guid ComponentGuid { get { return new Guid("E2B93E81-4253-4BB4-918A-5129BB88000B"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            ElementInput(p); p[0].DataMapping = GH_DataMapping.Flatten;
            p.AddTextParameter("Keys", "K", "Colonne in ordine. Vuoto = tutte le chiavi tranne MetadataJSON.", GH_ParamAccess.list); p[1].Optional = true; p[1].DataMapping = GH_DataMapping.Flatten;
            p.AddTextParameter("File", "F", "Percorso .csv.", GH_ParamAccess.item);
            p.AddBooleanParameter("Write", "W", "Collegare un Button.", GH_ParamAccess.item, false);
            p.AddBooleanParameter("Overwrite", "O", "Permette la sostituzione atomica di un file esistente.", GH_ParamAccess.item, false);
            p.AddTextParameter("Delimiter", "D", "Separatore: ; (predefinito), virgola o tab.", GH_ParamAccess.item, ";");
            p.AddBooleanParameter("Excel safe", "S", "Prefissa con apostrofo celle che iniziano con = + - @, per leggerle come testo in Excel.", GH_ParamAccess.item, true);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p) { p.AddTextParameter("Saved file", "F", "Percorso dell'ultimo salvataggio riuscito.", GH_ParamAccess.item); }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            bool run = false; da.GetData(3, ref run); bool trigger = run && !_lastWrite; _lastWrite = run;
            if (trigger)
            {
                try
                {
                    var elements = Elements(da, 0); var keys = new List<string>(); string file = "", delimiter = ";"; bool overwrite = false, safe = true;
                    if (elements.Count == 0) throw new ArgumentException("Nessun oggetto da esportare.");
                    da.GetDataList(1, keys); if (!da.GetData(2, ref file)) return; da.GetData(4, ref overwrite); da.GetData(5, ref delimiter); da.GetData(6, ref safe);
                    string csv = DataTools.Csv(elements, keys, delimiter, safe);
                    _file = DataTools.WriteAtomic(file, ".csv", overwrite, path => File.WriteAllText(path, csv, new UTF8Encoding(true)));
                }
                catch (Exception ex) { _file = ""; Error(ex); }
            }
            da.SetData(0, _file);
        }
    }

    public sealed class Export3dmComponent : ElementTool
    {
        private bool _lastWrite;
        private string _file = "", _report = "Premere Write per esportare la selezione.";
        public Export3dmComponent() : base("IFC Export 3DM", "IFC 3DM", "Esporta gli elementi in un nuovo .3dm con layer e User Text, senza aggiungere oggetti al documento attivo.", "4 Save", "3DM") { }
        public override Guid ComponentGuid { get { return new Guid("E2B93E81-4253-4BB4-918A-5129BB88000C"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            ElementInput(p); p[0].DataMapping = GH_DataMapping.Flatten; p.AddTextParameter("File", "F", "Percorso .3dm.", GH_ParamAccess.item);
            p.AddBooleanParameter("Write", "W", "Collegare un Button.", GH_ParamAccess.item, false);
            p.AddBooleanParameter("Overwrite", "O", "Sostituisce il file esistente solo se True.", GH_ParamAccess.item, false);
            p.AddVectorParameter("Offset metres", "XYZ", "Stesso offset della preview.", GH_ParamAccess.item, Vector3d.Zero);
            p.AddTextParameter("Root layer", "L", "Layer radice.", GH_ParamAccess.item, "IFC");
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        { p.AddTextParameter("Saved file", "F", "File creato.", GH_ParamAccess.item); p.AddTextParameter("Report", "R", "Oggetti senza mesh non esportati; conservarli nell'archivio IFC.", GH_ParamAccess.item); }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            bool run = false; da.GetData(2, ref run); bool trigger = run && !_lastWrite; _lastWrite = run;
            if (trigger)
            {
                try
                {
                    var elements = Elements(da, 0); string file = "", root = "IFC"; bool overwrite = false; var offset = Vector3d.Zero;
                    if (!da.GetData(1, ref file)) return; da.GetData(3, ref overwrite); da.GetData(4, ref offset); da.GetData(5, ref root);
                    var doc = RhinoDoc.ActiveDoc; if (doc == null) throw new InvalidOperationException("Nessun documento Rhino attivo.");
                    int skipped; _file = DataTools.Export3dm(elements, file, overwrite, doc.ModelUnitSystem, offset, root, out skipped);
                    _report = "Salvato in " + doc.ModelUnitSystem + "; " + skipped + " oggetti senza mesh mantenuti solo nell'archivio IFC.";
                }
                catch (Exception ex) { _file = ""; _report = ex.Message; Error(ex); }
            }
            da.SetData(0, _file); da.SetData(1, _report);
        }
    }

    public sealed class ReadBakedComponent : IfcComponent
    {
        public ReadBakedComponent() : base("IFC Read Baked", "IFC Baked", "Rilegge geometrie e User Text degli oggetti IFC gia presenti nel documento Rhino. Premere Refresh dopo modifiche al documento.", "1 Read", "RHI") { }
        public override Guid ComponentGuid { get { return new Guid("E2B93E81-4253-4BB4-918A-5129BB88000D"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddTextParameter("Rhino object IDs", "ID", "GUID Rhino (es. uscita Bake). Vuoto = tutti gli oggetti marcati IFCViewer.", GH_ParamAccess.list); p[0].Optional = true;
            p.AddTextParameter("Root", "L", "Filtro per radice salvata nel bake; vuoto = tutte.", GH_ParamAccess.item, "");
            p.AddBooleanParameter("Refresh", "R", "Button per ricalcolare dopo modifiche in Rhino.", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("Rhino IDs", "ID", "GUID degli oggetti trovati.", GH_ParamAccess.list);
            p.AddMeshParameter("Geometry", "G", "Mesh nelle coordinate correnti Rhino.", GH_ParamAccess.list);
            p.AddTextParameter("Keys", "K", "User Text per ramo {oggetto}.", GH_ParamAccess.tree);
            p.AddTextParameter("Values", "V", "Valori allineati a Keys.", GH_ParamAccess.tree);
            p.AddTextParameter("Metadata JSON", "J", "Metadati IFC salvati nel bake.", GH_ParamAccess.list);
            p.AddTextParameter("Not found", "MISS", "GUID assenti, non mesh, non IFCViewer o fuori dalla radice richiesta.", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var ids = new List<string>(); string root = ""; da.GetDataList(0, ids); da.GetData(1, ref root);
                var doc = RhinoDoc.ActiveDoc; if (doc == null) throw new InvalidOperationException("Nessun documento Rhino attivo.");
                Func<RhinoObject, bool> accepted = o => o != null && o.Geometry is Mesh && o.Attributes.GetUserString("IFCViewer.Key") != null && (string.IsNullOrEmpty(root) || o.Attributes.GetUserString("IFCViewer.Root") == root);
                var objects = new List<RhinoObject>(); var missing = new List<string>();
                if (ids.Count == 0) objects.AddRange(doc.Objects.GetObjectList(ObjectType.Mesh).Where(accepted));
                else foreach (string id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    Guid guid; var obj = Guid.TryParse(id, out guid) ? doc.Objects.FindId(guid) : null;
                    if (accepted(obj)) objects.Add(obj); else missing.Add(id);
                }
                var keys = new GH_Structure<GH_String>(); var values = new GH_Structure<GH_String>();
                for (int i = 0; i < objects.Count; i++)
                {
                    var path = new GH_Path(i); keys.EnsurePath(path); values.EnsurePath(path);
                    var strings = objects[i].Attributes.GetUserStrings();
                    foreach (string key in strings.AllKeys.OrderBy(k => k, StringComparer.Ordinal))
                    { keys.Append(new GH_String(key), path); values.Append(new GH_String(strings[key]), path); }
                }
                da.SetDataList(0, objects.Select(o => o.Id.ToString())); da.SetDataList(1, objects.Select(o => ((Mesh)o.Geometry).DuplicateMesh()));
                da.SetDataTree(2, keys); da.SetDataTree(3, values); da.SetDataList(4, objects.Select(o => o.Attributes.GetUserString("IFC.MetadataJSON") ?? "")); da.SetDataList(5, missing);
            }
            catch (Exception ex) { Error(ex); }
        }
    }
}
