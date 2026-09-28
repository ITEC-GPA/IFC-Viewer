using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino.Geometry;

namespace IfcViewer
{
    public sealed class RevisionResult
    {
        public List<IfcElement> Added = new List<IfcElement>();
        public List<IfcElement> Removed = new List<IfcElement>();
        public List<IfcElement> ChangedBefore = new List<IfcElement>();
        public List<IfcElement> ChangedAfter = new List<IfcElement>();
        public List<IfcElement> Unchanged = new List<IfcElement>();
        public List<List<string>> Reasons = new List<List<string>>();
        public List<string> Warnings = new List<string>();
    }

    public static class Revisions
    {
        private static readonly HashSet<string> Unordered = new HashSet<string> { "HasProperties", "HasPropertySets", "Quantities", "RelatedObjects" };
        private static JToken Normalize(JToken token, JObject graph, HashSet<string> stack, string field)
        {
            if (token == null) return JValue.CreateNull();
            var obj = token as JObject;
            if (obj != null)
            {
                if (obj["$ref"] != null)
                {
                    string id = obj["$ref"].ToString();
                    if (graph != null && graph[id] != null && !stack.Contains(id))
                    {
                        var next = new HashSet<string>(stack) { id };
                        return Normalize(graph[id], graph, next, field);
                    }
                    return new JObject(new JProperty("reference_class", obj["ifc_class"]));
                }
                var result = new JObject();
                foreach (var property in obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (property.Name == "id" || property.Name == "step_id" || property.Name == "OwnerHistory") continue;
                    // Placement/representation changes are compared using meshes.
                    if (property.Name == "ObjectPlacement" || property.Name == "Representation" || property.Name == "RepresentationMaps") continue;
                    result.Add(property.Name, Normalize(property.Value, graph, stack, property.Name));
                }
                return result;
            }
            if (token is JArray)
            {
                var items = token.Select(t => Normalize(t, graph, stack, field)).ToList();
                if (Unordered.Contains(field)) items = items.OrderBy(t => t.ToString(Formatting.None), StringComparer.Ordinal).ToList();
                return new JArray(items);
            }
            return token.DeepClone();
        }
        public static JObject SemanticData(IfcElement element)
        {
            var record = element.Record; var result = new JObject();
            foreach (string field in new[] { "ifc_class", "name", "attributes", "type_object", "psets_effective", "psets_instance", "psets_type", "container", "materials", "geometry_status" })
                result[field] = Normalize(record[field], null, new HashSet<string>(), field);
            result["length_unit_to_metres"] = element.Model.Metadata["length_unit_to_metres"].DeepClone();
            var definition = record["property_graph"] as JObject;
            var roots = new List<JToken>();
            if (definition != null && definition["roots"] is JArray)
            {
                var graph = definition["entities"] as JObject;
                foreach (var root in definition["roots"])
                    roots.Add(Normalize(graph == null ? null : graph[root.ToString()], graph, new HashSet<string>(), "root"));
            }
            result["property_definitions"] = new JArray(roots.OrderBy(t => t.ToString(Formatting.None), StringComparer.Ordinal));
            var associations = new List<JToken>();
            foreach (var association in (record["associations"] as JArray) ?? new JArray())
                associations.Add(new JObject(new JProperty("relation", association["relation"]), new JProperty("attributes", Normalize(association["attributes"], association["entities"] as JObject, new HashSet<string>(), "attributes"))));
            result["associations"] = new JArray(associations.OrderBy(t => t.ToString(Formatting.None), StringComparer.Ordinal));
            return result;
        }
        private static void Differences(JToken before, JToken after, string path, List<string> changes)
        {
            if (JToken.DeepEquals(before, after)) return;
            var oldObject = before as JObject; var newObject = after as JObject;
            if (oldObject != null && newObject != null)
            {
                foreach (string key in oldObject.Properties().Select(p => p.Name).Union(newObject.Properties().Select(p => p.Name)).OrderBy(k => k, StringComparer.Ordinal))
                    Differences(oldObject[key], newObject[key], path.Length == 0 ? key : path + "." + key, changes);
            }
            else changes.Add(path);
        }
        public static string MeshSignature(Mesh mesh, double precision)
        {
            if (mesh == null) return "no-mesh";
            Func<double, string> coordinate = value =>
            {
                double scaled = value / precision;
                if (double.IsInfinity(scaled) || double.IsNaN(scaled)) throw new ArgumentException("Coordinate/precisione fuori intervallo.");
                double rounded = Math.Round(scaled, MidpointRounding.AwayFromZero);
                return (rounded == 0 ? 0 : rounded).ToString("R", CultureInfo.InvariantCulture);
            };
            var vertices = new string[mesh.Vertices.Count];
            for (int i = 0; i < vertices.Length; i++)
            {
                var point = mesh.Vertices.Point3dAt(i); vertices[i] = coordinate(point.X) + "," + coordinate(point.Y) + "," + coordinate(point.Z);
            }
            var triangles = new List<string>();
            foreach (var face in mesh.Faces)
            {
                triangles.Add(string.Join(";", new[] { vertices[face.A], vertices[face.B], vertices[face.C] }.OrderBy(s => s, StringComparer.Ordinal)));
                if (face.IsQuad) triangles.Add(string.Join(";", new[] { vertices[face.A], vertices[face.C], vertices[face.D] }.OrderBy(s => s, StringComparer.Ordinal)));
            }
            triangles.Sort(StringComparer.Ordinal);
            return IfcModel.Hash(string.Join("|", triangles));
        }
        public static RevisionResult Compare(IfcModel before, IfcModel after, bool geometry, double precision)
        {
            if (precision <= 0 || double.IsNaN(precision) || double.IsInfinity(precision)) throw new ArgumentException("Precisione in metri: numero positivo finito.");
            var result = new RevisionResult();
            Func<IfcModel, string, Dictionary<string, IfcElement>> index = delegate(IfcModel model, string label)
            {
                var valid = model.Elements.Where(e => !string.IsNullOrWhiteSpace(e.GlobalId)).ToList();
                var duplicate = valid.GroupBy(e => e.GlobalId, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
                if (duplicate != null) throw new ArgumentException("GlobalId duplicato nella revisione " + label + ": " + duplicate.Key + ". Confronto ambiguo.");
                int missing = model.Elements.Count - valid.Count;
                if (missing > 0) result.Warnings.Add(label + ": " + missing + " oggetti senza GlobalId esclusi dal confronto.");
                return valid.ToDictionary(e => e.GlobalId, e => e, StringComparer.Ordinal);
            };
            var oldMap = index(before, "prima"); var newMap = index(after, "dopo");
            foreach (string id in oldMap.Keys.Union(newMap.Keys).OrderBy(s => s, StringComparer.Ordinal))
            {
                IfcElement oldElement, newElement;
                if (!oldMap.TryGetValue(id, out oldElement)) { result.Added.Add(newMap[id]); continue; }
                if (!newMap.TryGetValue(id, out newElement)) { result.Removed.Add(oldElement); continue; }
                var reasons = new List<string>(); Differences(SemanticData(oldElement), SemanticData(newElement), "", reasons);
                if (geometry && MeshSignature(oldElement.MeshMetres, precision) != MeshSignature(newElement.MeshMetres, precision)) reasons.Add("geometry.mesh_or_placement");
                if (reasons.Count > 0) { result.ChangedBefore.Add(oldElement); result.ChangedAfter.Add(newElement); result.Reasons.Add(reasons); }
                else result.Unchanged.Add(newElement);
            }
            return result;
        }
    }

    public sealed class CompareRevisionsComponent : IfcComponent
    {
        public CompareRevisionsComponent() : base("IFC Compare Revisions", "IFC Diff", "Confronta due modelli per GlobalId: aggiunti, rimossi, dati cambiati e mesh cambiate. Ignora rinumerazione STEP e OwnerHistory.", "5 Check", "DIF") { }
        public override Guid ComponentGuid { get { return new Guid("E2B93E81-4253-4BB4-918A-5129BB88000E"); } }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Before", "A", "Un modello della revisione precedente.", GH_ParamAccess.item);
            p.AddGenericParameter("After", "B", "Un modello della revisione successiva della stessa opera.", GH_ParamAccess.item);
            p.AddBooleanParameter("Compare meshes", "G", "Confronta triangoli e coordinate SI. Una diversa triangolazione e segnalata come modifica.", GH_ParamAccess.item, true);
            p.AddNumberParameter("Precision metres", "P", "Griglia di arrotondamento delle coordinate; non e una distanza massima garantita.", GH_ParamAccess.item, 0.000001);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Added", "+", "Nuovi oggetti (revisione B).", GH_ParamAccess.list);
            p.AddGenericParameter("Removed", "-", "Oggetti rimossi (revisione A).", GH_ParamAccess.list);
            p.AddGenericParameter("Changed before", "OLD", "Versione precedente degli oggetti modificati.", GH_ParamAccess.list);
            p.AddGenericParameter("Changed after", "NEW", "Versione nuova degli oggetti modificati.", GH_ParamAccess.list);
            p.AddGenericParameter("Unchanged", "=", "Oggetti senza differenze rilevate (revisione B).", GH_ParamAccess.list);
            p.AddTextParameter("Changed fields", "K", "Campi cambiati per ramo {indice oggetto modificato}.", GH_ParamAccess.tree);
            p.AddTextParameter("Report", "R", "Conteggi e avvisi.", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            try
            {
                object a = null, b = null; bool geometry = true; double precision = 0.000001;
                if (!da.GetData(0, ref a) || !da.GetData(1, ref b)) return; da.GetData(2, ref geometry); da.GetData(3, ref precision);
                var before = Unwrap<IfcModel>(a); var after = Unwrap<IfcModel>(b);
                if (before == null || after == null) throw new ArgumentException("Collegare un output Models a ciascun input A/B.");
                var diff = Revisions.Compare(before, after, geometry, precision); var tree = new GH_Structure<GH_String>();
                for (int i = 0; i < diff.Reasons.Count; i++) foreach (string reason in diff.Reasons[i]) tree.Append(new GH_String(reason), new GH_Path(i));
                da.SetDataList(0, diff.Added); da.SetDataList(1, diff.Removed); da.SetDataList(2, diff.ChangedBefore); da.SetDataList(3, diff.ChangedAfter); da.SetDataList(4, diff.Unchanged); da.SetDataTree(5, tree);
                da.SetDataList(6, new[] { "+" + diff.Added.Count + " / -" + diff.Removed.Count + " / modificati " + diff.ChangedAfter.Count + " / invariati " + diff.Unchanged.Count }.Concat(diff.Warnings));
                foreach (string warning in diff.Warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
            }
            catch (Exception ex) { Error(ex); }
        }
    }
}
