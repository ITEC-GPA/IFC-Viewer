using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Geometry;

namespace IfcViewer
{
    public sealed class ColorByComponent : ElementTool
    {
        public ColorByComponent() : base("IFC Color By", "IFC Colors", "Colori categoriali deterministici per un parametro. Collegare Geometry e Colors a Custom Preview.", "3 View", "RGB") { }
        public override Guid ComponentGuid { get { return new Guid("E2B93E81-4253-4BB4-918A-5129BB880008"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        { ElementInput(p); KeyInput(p, "IFC.Class"); p.AddVectorParameter("Offset metres", "O", "Stesso offset usato nel bake.", GH_ParamAccess.item, Vector3d.Zero); }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddMeshParameter("Geometry", "G", "Mesh nelle unita Rhino, rami {oggetto}.", GH_ParamAccess.tree);
            p.AddColourParameter("Colors", "C", "Colori allineati alla geometria.", GH_ParamAccess.tree);
            p.AddTextParameter("Legend", "L", "Valori distinti; chiave assente separata dai valori presenti.", GH_ParamAccess.list);
            p.AddColourParameter("Legend colors", "LC", "Colori della legenda.", GH_ParamAccess.list);
            p.AddIntegerParameter("Counts", "N", "Oggetti per categoria, inclusi quelli senza mesh.", GH_ParamAccess.list);
            p.AddBooleanParameter("Key exists", "F", "False identifica la categoria delle chiavi assenti.", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var elements = Elements(da, 0); string key = "IFC.Class"; Vector3d offset = Vector3d.Zero; da.GetData(1, ref key); da.GetData(2, ref offset);
                if (!offset.IsValid) throw new ArgumentException("Offset non valido.");
                var rows = elements.Select(e => new PropertyRow(e)).ToList(); var groups = DataTools.Groups(rows, key);
                var meshes = new GH_Structure<GH_Mesh>(); var colors = new GH_Structure<GH_Colour>();
                for (int i = 0; i < rows.Count; i++)
                {
                    var path = new GH_Path(i); meshes.EnsurePath(path); colors.EnsurePath(path);
                    string value; bool found = rows[i].Get(key, out value);
                    var mesh = rows[i].Element.MeshInDocument(RhinoDoc.ActiveDoc, offset);
                    if (mesh != null) { meshes.Append(new GH_Mesh(mesh), path); colors.Append(new GH_Colour(found ? Baking.ClassColor(value) : Color.Gray), path); }
                }
                da.SetDataTree(0, meshes); da.SetDataTree(1, colors); da.SetDataList(2, groups.Select(g => g.Value));
                da.SetDataList(3, groups.Select(g => g.Found ? Baking.ClassColor(g.Value) : Color.Gray)); da.SetDataList(4, groups.Select(g => g.Elements.Count)); da.SetDataList(5, groups.Select(g => g.Found));
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class GeometryMetricsComponent : ElementTool
    {
        public GeometryMetricsComponent() : base("IFC Geometry Metrics", "IFC Metrics", "Area e volume delle mesh in SI; non sostituiscono i Quantity Set IFC. Volume NaN per mesh non chiuse/orientate.", "3 View", "M3") { }
        public override Guid ComponentGuid { get { return new Guid("E2B93E81-4253-4BB4-918A-5129BB880009"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        { ElementInput(p); p.AddVectorParameter("Offset metres", "O", "Offset per box/centri visualizzati in Rhino; non modifica le misure.", GH_ParamAccess.item, Vector3d.Zero); }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddNumberParameter("Area m2", "A", "Superficie totale della mesh in metri quadrati; NaN senza mesh.", GH_ParamAccess.list);
            p.AddNumberParameter("Volume m3", "V", "Volume assoluto in metri cubi per mesh chiusa/orientata; altrimenti NaN.", GH_ParamAccess.list);
            p.AddBooleanParameter("Has volume", "OK", "True se e stato calcolato un volume.", GH_ParamAccess.list);
            p.AddVectorParameter("Dimensions metres", "D", "Dimensioni XYZ del bounding box nelle coordinate originali, metri.", GH_ParamAccess.tree);
            p.AddBoxParameter("Boxes", "B", "Box nelle unita del documento Rhino.", GH_ParamAccess.tree);
            p.AddPointParameter("Centers", "C", "Centri dei box nelle unita del documento Rhino.", GH_ParamAccess.tree);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var elements = Elements(da, 0); Vector3d offset = Vector3d.Zero; da.GetData(1, ref offset);
                var doc = RhinoDoc.ActiveDoc;
                if (doc == null || doc.ModelUnitSystem == UnitSystem.None || doc.ModelUnitSystem == UnitSystem.CustomUnits) throw new ArgumentException("Impostare unita Rhino standard.");
                if (!offset.IsValid) throw new ArgumentException("Offset non valido.");
                double scale = RhinoMath.UnitScale(UnitSystem.Meters, doc.ModelUnitSystem);
                var areas = new List<double>(); var volumes = new List<double>(); var has = new List<bool>();
                var sizes = new GH_Structure<GH_Vector>(); var boxes = new GH_Structure<GH_Box>(); var centers = new GH_Structure<GH_Point>();
                for (int i = 0; i < elements.Count; i++)
                {
                    var path = new GH_Path(i); sizes.EnsurePath(path); boxes.EnsurePath(path); centers.EnsurePath(path);
                    var mesh = elements[i].MeshMetres; double area = double.NaN, volume = double.NaN;
                    if (mesh != null)
                    {
                        using (var mass = AreaMassProperties.Compute(mesh)) { if (mass != null) area = mass.Area; }
                        if (mesh.IsClosed && mesh.SolidOrientation() != 0)
                            using (var mass = VolumeMassProperties.Compute(mesh)) { if (mass != null) volume = Math.Abs(mass.Volume); }
                        var bounds = mesh.GetBoundingBox(true); sizes.Append(new GH_Vector(bounds.Max - bounds.Min), path);
                        var display = new BoundingBox((bounds.Min + offset) * scale, (bounds.Max + offset) * scale);
                        boxes.Append(new GH_Box(new Box(display)), path); centers.Append(new GH_Point(display.Center), path);
                    }
                    areas.Add(area); volumes.Add(volume); has.Add(!double.IsNaN(volume) && !double.IsInfinity(volume));
                }
                da.SetDataList(0, areas); da.SetDataList(1, volumes); da.SetDataList(2, has); da.SetDataTree(3, sizes); da.SetDataTree(4, boxes); da.SetDataTree(5, centers);
            }
            catch (Exception ex) { Error(ex); }
        }
    }

    public sealed class OriginComponent : ElementTool
    {
        public OriginComponent() : base("IFC Local Origin", "IFC Origin", "Calcola un offset comune in metri per avvicinare un modello/federazione all'origine. Collegarlo sia alla preview sia al bake/export.", "3 View", "XYZ") { }
        public override Guid ComponentGuid { get { return new Guid("E2B93E81-4253-4BB4-918A-5129BB88000F"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        { ElementInput(p); p.AddTextParameter("Mode", "M", "XY center (mantiene quote Z), center, min.", GH_ParamAccess.item, "XY center"); }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddVectorParameter("Offset metres", "O", "Traslazione da applicare a IFC Elements/Color By/Bake/Export 3DM.", GH_ParamAccess.item);
            p.AddPointParameter("Original origin metres", "P", "Punto originale che diventa l'origine, in metri.", GH_ParamAccess.item);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var elements = Elements(da, 0); string mode = "XY center"; da.GetData(1, ref mode); mode = mode.Trim().ToLowerInvariant();
                if (mode != "xy center" && mode != "center" && mode != "min") throw new ArgumentException("Mode: XY center, center oppure min.");
                var bounds = BoundingBox.Empty; foreach (var e in elements.Where(e => e.MeshMetres != null)) bounds.Union(e.MeshMetres.GetBoundingBox(true));
                if (!bounds.IsValid) { AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Nessuna mesh da cui ricavare l'origine."); return; }
                Point3d origin = mode == "min" ? bounds.Min : bounds.Center; if (mode == "xy center") origin.Z = 0;
                da.SetData(0, new Vector3d(-origin.X, -origin.Y, -origin.Z)); da.SetData(1, origin);
            }
            catch (Exception ex) { Error(ex); }
        }
    }
}
