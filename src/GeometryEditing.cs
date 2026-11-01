using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;

namespace IfcViewer
{
    public static class GeometryEdits
    {
        public static List<IfcEdit> FromRhino(IfcModel model, RhinoDoc doc, IEnumerable<Guid> ids, double toleranceMetres)
        {
            if (doc == null || doc.ModelUnitSystem == UnitSystem.None || doc.ModelUnitSystem == UnitSystem.CustomUnits) throw new ArgumentException("Impostare unita standard nel documento Rhino.");
            if (double.IsNaN(toleranceMetres) || double.IsInfinity(toleranceMetres) || toleranceMetres <= 0) throw new ArgumentException("Tolleranza positiva in metri richiesta.");
            var changes = new List<IfcEdit>();
            foreach (Guid id in ids.Distinct())
            {
                var obj = doc.Objects.FindId(id); var element = RhinoEdits.Match(model, obj);
                if (!(obj.Geometry is Mesh) || element.MeshMetres == null) throw new ArgumentException("Sono supportate solo le mesh provenienti dal bake: " + id);
                string encodedOffset = obj.Attributes.GetUserString("IFCViewer.OffsetMetres");
                if (encodedOffset == null) throw new ArgumentException("Offset originale del bake mancante: " + id);
                var offset = encodedOffset.Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
                if (offset.Length != 3 || offset.Any(v => double.IsInfinity(v) || double.IsNaN(v))) throw new ArgumentException("Offset del bake non valido.");
                using (var mesh = ((Mesh)obj.Geometry).DuplicateMesh())
                {
                    mesh.Vertices.UseDoublePrecisionVertices = true;
                    mesh.Scale(RhinoMath.UnitScale(doc.ModelUnitSystem, UnitSystem.Meters));
                    mesh.Translate(new Vector3d(-offset[0], -offset[1], -offset[2]));
                    mesh.Faces.ConvertQuadsToTriangles();
                    var baseline = element.MeshMetres;
                    bool same = mesh.Vertices.Count == baseline.Vertices.Count && mesh.Faces.Count == baseline.Faces.Count;
                    if (same) for (int i = 0; i < mesh.Vertices.Count; i++) if (mesh.Vertices.Point3dAt(i).DistanceTo(baseline.Vertices.Point3dAt(i)) > toleranceMetres) { same = false; break; }
                    if (same) for (int i = 0; i < mesh.Faces.Count; i++) if (mesh.Faces[i] != baseline.Faces[i]) { same = false; break; }
                    if (same) continue;
                    bool oriented, boundary;
                    if (!mesh.IsValid || !mesh.IsClosed || !mesh.IsManifold(true, out oriented, out boundary) || !oriented || boundary || mesh.SolidOrientation() == 0)
                        throw new ArgumentException("Serve una mesh chiusa, manifold e orientata: " + id);
                    if (mesh.SolidOrientation() < 0) mesh.Flip(true, true, true);
                    var vertices = Enumerable.Range(0, mesh.Vertices.Count).Select(i => { var p = mesh.Vertices.Point3dAt(i); return new[] { p.X, p.Y, p.Z }; }).ToArray();
                    var faces = mesh.Faces.Select(f => new[] { f.A, f.B, f.C }).ToArray();
                    changes.Add(IfcEdit.Geometry(element, vertices, faces));
                }
            }
            return changes;
        }
    }

    public sealed class GeometryChangesComponent : IfcComponent
    {
        public GeometryChangesComponent() : base("IFC Geometry From Rhino", "IFC Rhino Mesh", "Prepara la sostituzione Body delle mesh del bake modificate in Rhino. Coordinate convertite in metri e offset annullato. Nuova geometria tessellata, Qto non ricalcolate; solo mesh chiuse connesse senza aperture IFC.", "5 Edit", "GEO") { }
        public override Guid ComponentGuid { get { return new Guid("BEEB272D-1CE2-461C-9771-E9366A837C05"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Model", "M", "Un solo modello originale, stessa versione del bake.", GH_ParamAccess.list); p[0].DataMapping = GH_DataMapping.Flatten;
            p.AddTextParameter("Rhino IDs", "ID", "GUID delle mesh del bake da confrontare. Non trasferisce cancellazioni o nuovi oggetti senza identita IFC.", GH_ParamAccess.list); p[1].DataMapping = GH_DataMapping.Flatten; p[1].Optional = true;
            p.AddBooleanParameter("Refresh", "R", "Button dopo modifiche di forma/posizione in Rhino.", GH_ParamAccess.item, false);
            p.AddNumberParameter("Tolerance metres", "T", "Soglia di confronto dei vertici in metri (default 1e-6). Non e una tolleranza di verifica strutturale.", GH_ParamAccess.item, 1e-6);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Changes", "C", "Richieste geometriche da combinare con modifiche dati e inviare a IFC Write File.", GH_ParamAccess.list);
            p.AddTextParameter("Preview", "P", "Elementi interessati, vertici/facce e limiti della conversione.", GH_ParamAccess.list);
            p.AddTextParameter("Report", "R", "Sintesi del confronto. Nuova geometria non parametrica; quantita e modello analitico restano invariati.", GH_ParamAccess.item);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                var models = new List<object>(); var ids = new List<string>(); double tolerance = 1e-6;
                da.GetDataList(0, models); da.GetDataList(1, ids); da.GetData(3, ref tolerance);
                if (models.Count != 1 || Unwrap<IfcModel>(models[0]) == null) throw new ArgumentException("Collegare un solo modello originale.");
                if (ids.Count == 0) { da.SetData(2, "Nessuna mesh selezionata. Collegare i GUID del bake."); return; }
                var changes = GeometryEdits.FromRhino(Unwrap<IfcModel>(models[0]), RhinoDoc.ActiveDoc, ids.Select(Guid.Parse), tolerance);
                SetAttributeComponent.SetOutputs(da, changes);
                da.SetData(2, changes.Count + " mesh modificate. Body non parametrico; Qto, proprieta dimensionali e modello analitico non ricalcolati.");
                if (changes.Count > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Export come mesh: verificare Qto, proprieta dimensionali e modello analitico. Aperture/riempimenti e mesh disgiunte non supportati.");
            }
            catch (Exception ex) { Error(ex); }
        }
    }
}
