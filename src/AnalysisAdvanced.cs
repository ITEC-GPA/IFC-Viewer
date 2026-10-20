using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino;
using Rhino.Geometry;

namespace IfcViewer
{
    public sealed class ClashComponent : ElementTool
    {
        public ClashComponent() : base("IFC Clash / Clearance", "IFC Clash", "Interferenze, contatti e distanze insufficienti fra due insiemi anche federati. Usa coordinate IFC comuni e geometria tessellata.", "3 Check", "CLH") { }
        public override Guid ComponentGuid { get { return new Guid("A33C7151-8190-4A00-A001-100000000010"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            ElementInput(p); p[0].Name = "Set A"; p[0].DataMapping = GH_DataMapping.Flatten;
            p.AddGenericParameter("Set B", "B", "Secondo insieme. Puo coincidere con A.", GH_ParamAccess.list); p[1].DataMapping = GH_DataMapping.Flatten;
            p.AddTextParameter("Mode", "M", "Intersection (penetrazione), Collision (superfici), Clearance (distanza).", GH_ParamAccess.item, "Intersection");
            p.AddNumberParameter("Distance metres", "D", "Tolleranza Intersection o distanza minima Clearance, in metri. Collision ignora questo valore.", GH_ParamAccess.item, .002);
            p.AddBooleanParameter("Include touching", "T", "Include contatti in Collision.", GH_ParamAccess.item, false);
            p.AddTextParameter("Exclude classes", "X", "Classi esatte da escludere.", GH_ParamAccess.list); p[5].Optional = true;
            p.AddTextParameter("Exclude pairs", "P", "Coppie IFC.Key|IFC.Key da ignorare.", GH_ParamAccess.list); p[6].Optional = true;
            p.AddVectorParameter("Offset", "O", "Offset di sola visualizzazione in metri, come IFC Elements.", GH_ParamAccess.item, Vector3d.Zero);
            p.AddBooleanParameter("Run", "R", "Esegue il controllo.", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Pairs", "E", "Un ramo {i} con i due elementi coinvolti.", GH_ParamAccess.tree);
            p.AddPointParameter("Points A", "A", "Punto indicativo A nelle unita Rhino.", GH_ParamAccess.list);
            p.AddPointParameter("Points B", "B", "Punto indicativo B nelle unita Rhino.", GH_ParamAccess.list);
            p.AddNumberParameter("Distance metres", "D", "Penetrazione/separazione secondo Kind; non volume di intersezione.", GH_ParamAccess.list);
            p.AddTextParameter("Kind", "K", "Tipo restituito dal motore geometrico.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "R", "Coppie, elementi senza geometria e ipotesi.", GH_ParamAccess.item);
        }
        internal static Point3d Point(JToken xyz, Vector3d offset)
        {
            double scale = RhinoMath.UnitScale(UnitSystem.Meters, RhinoDoc.ActiveDoc == null ? UnitSystem.Meters : RhinoDoc.ActiveDoc.ModelUnitSystem);
            return new Point3d(((double)xyz[0] + offset.X) * scale, ((double)xyz[1] + offset.Y) * scale, ((double)xyz[2] + offset.Z) * scale);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                bool run = false; da.GetData(8, ref run); if (!run) return;
                var a = Elements(da, 0); var b = Elements(da, 1); if (a.Count == 0 || b.Count == 0) throw new ArgumentException("Entrambi gli insiemi devono contenere elementi.");
                var all = a.Concat(b).ToList();
                if (all.GroupBy(e => e.Model.SourceKey).Any(g => g.Select(e => (string)e.Model.Metadata["source_sha256"]).Distinct().Count() > 1)) throw new ArgumentException("Non mescolare revisioni della stessa sorgente.");
                var lookup = all.GroupBy(e => e.Key).ToDictionary(g => g.Key, g => g.First()); var aset = new HashSet<string>(a.Select(e => e.Key)); var bset = new HashSet<string>(b.Select(e => e.Key));
                var sources = new JArray();
                foreach (var group in lookup.Values.GroupBy(e => e.Model.SourceKey))
                    sources.Add(new JObject(new JProperty("archive", group.First().Model.ArchivePath), new JProperty("source_sha256", group.First().Model.Metadata["source_sha256"]),
                        new JProperty("elements", JArray.FromObject(group.Select(e => new { step_id = e.StepId, global_id = e.GlobalId, key = e.Key, a = aset.Contains(e.Key), b = bset.Contains(e.Key) })))));
                string mode = "Intersection"; double distance = .002; bool touching = false; var classes = new List<string>(); var excluded = new List<string>(); var offset = Vector3d.Zero;
                da.GetData(2, ref mode); da.GetData(3, ref distance); da.GetData(4, ref touching); da.GetDataList(5, classes); da.GetDataList(6, excluded); da.GetData(7, ref offset);
                var result = IfcTools.Run(a[0].Model, "clash", new JObject(new JProperty("sources", sources), new JProperty("mode", mode), new JProperty("distance", distance),
                    new JProperty("touching", touching), new JProperty("exclude_classes", new JArray(classes)), new JProperty("exclude_pairs", new JArray(excluded))));
                var pairs = (JArray)result["pairs"]; var tree = new GH_Structure<GH_ObjectWrapper>();
                for (int i = 0; i < pairs.Count; i++)
                    foreach (string side in new[] { "a", "b" }) tree.Append(new GH_ObjectWrapper(lookup[(string)pairs[i][side]["key"]]), new GH_Path(i));
                da.SetDataTree(0, tree); da.SetDataList(1, pairs.Select(p => Point(p["p1"], offset))); da.SetDataList(2, pairs.Select(p => Point(p["p2"], offset)));
                da.SetDataList(3, pairs.Select(p => (double)p["distance_metres"])); da.SetDataList(4, pairs.Select(p => (string)p["kind"])); da.SetData(5, result.ToString());
                if (((JArray)result["skipped"]).Count > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Elementi senza geometria utilizzabile esclusi: consultare Report.");
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class StructuralModelComponent : IfcComponent
    {
        public StructuralModelComponent() : base("IFC Structural Model", "IFC Structural", "Legge membri, connessioni, vincoli e carichi analitici esistenti. Visualizza topologie lineari; conserva il grafo originale. Non genera un modello FEM dalle mesh.", "6 Structural", "STR") { }
        public override Guid ComponentGuid { get { return new Guid("A33C7151-8190-4A00-A001-100000000011"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        { ModelInput(p); p.AddVectorParameter("Offset", "O", "Offset visualizzazione in metri.", GH_ParamAccess.item, Vector3d.Zero); }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddTextParameter("GlobalIds", "ID", "Identificativi membri e connessioni, ordine dei rami.", GH_ParamAccess.list);
            p.AddLineParameter("Axes / boundaries", "L", "Assi rettilinei e bordi poligonali nelle unita Rhino, un ramo per entita.", GH_ParamAccess.tree);
            p.AddPointParameter("Nodes / vertices", "P", "Punti topologici nelle unita Rhino, senza fusione fra entita.", GH_ParamAccess.tree);
            p.AddTextParameter("Connections & conditions", "C", "Dati IFC originali di connessioni, vincoli e sistemi locali.", GH_ParamAccess.list);
            p.AddTextParameter("Loads", "F", "Attivita, carichi, gruppi e riferimenti locali/globali nelle unita IFC originali.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "R", "Modelli analitici, load groups, unita e grafi IFC completi delle rappresentazioni.", GH_ParamAccess.item);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var model = IfcTools.Model(da, 0); var offset = Vector3d.Zero; da.GetData(1, ref offset);
                var result = IfcTools.Run(model, "structural", new JObject()); var rows = (JArray)result["rows"];
                var lines = new GH_Structure<GH_Line>(); var points = new GH_Structure<GH_Point>();
                for (int i = 0; i < rows.Count; i++)
                {
                    var path = new GH_Path(i); lines.EnsurePath(path); points.EnsurePath(path);
                    foreach (var segment in rows[i]["geometry"]["segments"]) lines.Append(new GH_Line(new Line(ClashComponent.Point(segment[0], offset), ClashComponent.Point(segment[1], offset))), path);
                    foreach (var point in rows[i]["geometry"]["points"]) points.Append(new GH_Point(ClashComponent.Point(point, offset)), path);
                    foreach (var warning in rows[i]["geometry"]["warnings"]) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, (string)warning);
                }
                da.SetDataList(0, rows.Select(r => (string)r["element"]["global_id"])); da.SetDataTree(1, lines); da.SetDataTree(2, points);
                da.SetDataList(3, rows.Select(r => new JObject(new JProperty("connections", r["connections"]), new JProperty("condition", r["condition"])).ToString(Formatting.None)));
                da.SetDataList(4, rows.Select(r => r["activities"].ToString(Formatting.None))); da.SetData(5, result.ToString());
                if (rows.Count == 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Nessun membro/connessione strutturale analitico nel file.");
            }
            catch (Exception ex) { Error(ex); }
        }
    }
}
