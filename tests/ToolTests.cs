using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using IfcViewer;
using Newtonsoft.Json.Linq;
using Rhino;
using Rhino.Geometry;

internal static class ToolTests
{
    internal static void SetText(GH_Component component, int index, params string[] values)
    {
        var p = (Param_String)component.Params.Input[index]; p.PersistentData.Clear();
        foreach (string value in values) p.PersistentData.Append(new GH_String(value));
        component.ExpireSolution(false);
    }
    internal static void SetBool(GH_Component component, int index, bool value)
    { var p = (Param_Boolean)component.Params.Input[index]; p.PersistentData.Clear(); p.PersistentData.Append(new GH_Boolean(value)); component.ExpireSolution(false); }
    internal static void SetData(GH_Component component, int index, IEnumerable<object> values)
    {
        var p = (Param_GenericObject)component.Params.Input[index]; p.PersistentData.Clear();
        foreach (var value in values) p.PersistentData.Append(new GH_ObjectWrapper(value));
        component.ExpireSolution(false);
    }
    private static T Add<T>(GH_Document doc, T component, IEnumerable<IfcElement> elements) where T : GH_Component
    { component.CreateAttributes(); doc.AddObject(component, false); if (elements != null) SetData(component, 0, elements.Cast<object>()); return component; }
    private static string[] Text(GH_Component c, int index) { return c.Params.Output[index].VolatileData.AllData(true).Cast<GH_String>().Select(g => g.Value).ToArray(); }
    private static double[] Numbers(GH_Component c, int index) { return c.Params.Output[index].VolatileData.AllData(true).Cast<GH_Number>().Select(g => g.Value).ToArray(); }
    private static bool[] Bools(GH_Component c, int index) { return c.Params.Output[index].VolatileData.AllData(true).Cast<GH_Boolean>().Select(g => g.Value).ToArray(); }
    private static void Solve(GH_Document doc, GH_Component component, Action<bool, string> check)
    {
        doc.NewSolution(true, GH_SolutionMode.Silent);
        var errors = component.RuntimeMessages(GH_RuntimeMessageLevel.Error);
        check(errors.Count == 0, component.Name + " solves: " + string.Join("; ", errors));
    }
    private static IfcModel Clone(IfcModel source)
    {
        var copy = new IfcModel { ArchivePath = source.ArchivePath, Metadata = (JObject)source.Metadata.DeepClone() };
        foreach (var e in source.Elements) copy.Elements.Add(new IfcElement { Model = copy, Record = (JObject)e.Record.DeepClone(), MeshMetres = e.MeshMetres == null ? null : e.MeshMetres.DuplicateMesh() });
        return copy;
    }
    private static void Renumber(JToken token)
    {
        var obj = token as JObject;
        if (obj != null)
        {
            foreach (var property in obj.Properties().ToArray())
            {
                if ((property.Name == "id" || property.Name == "step_id" || property.Name == "$ref") && property.Value.Type == JTokenType.Integer) property.Value = (int)property.Value + 10000;
                else if (property.Name == "roots") { foreach (var item in ((JArray)property.Value).ToArray()) item.Replace(new JValue((int)item + 10000)); }
                else if (property.Name == "entities")
                {
                    var map = new JObject();
                    foreach (var entity in ((JObject)property.Value).Properties().ToArray()) { Renumber(entity.Value); map.Add(((int.Parse(entity.Name)) + 10000).ToString(), entity.Value.DeepClone()); }
                    property.Value = map;
                }
                else Renumber(property.Value);
            }
        }
        else if (token is JArray) foreach (var item in token) Renumber(item);
    }
    internal static void Run(IfcModel model, RhinoDoc rhino, string root, Action<bool, string> check)
    {
        string output = Path.Combine(root, "test-output", "tools-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        var wall = model.Elements.First(e => e.Name.StartsWith("Parete 1"));
        var otherWall = model.Elements.First(e => e.Name.StartsWith("Parete 2"));
        using (var doc = new GH_Document())
        {
            doc.Enabled = true;
            var info = Add(doc, new ModelInfoComponent(), null); SetData(info, 0, new object[] { model }); Solve(doc, info, check);
            check(Text(info, 1).Single() == "IFC4" && Numbers(info, 4).Single() == 0.001, "Model info exposes schema and IFC unit scale");
            var keys = Add(doc, new PropertyKeysComponent(), model.Elements); SetText(keys, 1, "FireRating"); Solve(doc, keys, check);
            check(Text(keys, 0).Contains("IFC.Property.Pset_WallCommon.FireRating"), "Property keys discovers inherited/instance fields");
            var value = Add(doc, new PropertyValueComponent(), new[] { wall, otherWall }); SetText(value, 1, "IFC.Property.Progetto.Costo"); Solve(doc, value, check);
            check(Numbers(value, 2)[0] == 0 && double.IsNaN(Numbers(value, 2)[1]) && Bools(value, 1).SequenceEqual(new[] { true, false }), "Property value distinguishes zero and absence with aligned outputs");
            SetText(value, 1, "IFC.Property.Pset_WallCommon.IsExternal"); Solve(doc, value, check);
            check(Text(value, 0).SequenceEqual(new[] { "false", "true" }), "Property value preserves booleans");
            var filter = Add(doc, new FilterPropertyComponent(), model.Elements); SetText(filter, 1, "IFC.Property.Pset_WallCommon.FireRating"); SetText(filter, 3, "REI 60"); Solve(doc, filter, check);
            check(filter.Params.Output[0].VolatileDataCount == 1 && filter.Params.Output[1].VolatileDataCount == 7, "Property filter partitions all elements");
            SetText(filter, 1, "IFC.Property.Progetto.Costo"); SetText(filter, 2, ">="); SetText(filter, 3, "0"); Solve(doc, filter, check);
            check(filter.Params.Output[0].VolatileDataCount == 1, "Numeric filter includes zero and rejects absent/non-numeric values");
            check(!DataTools.Predicate("absent", "!=", "x", false)(new PropertyRow(wall)), "Not-equal does not accidentally match missing fields");
            bool invalidNumber = false; try { DataTools.Predicate("key", ">", "1,5", false); } catch (ArgumentException) { invalidNumber = true; }
            check(invalidNumber, "Ambiguous decimal/thousands syntax is rejected");
            var ids = Add(doc, new SelectIdsComponent(), model.Elements); SetText(ids, 1, wall.GlobalId, "missing-id"); Solve(doc, ids, check);
            check(ids.Params.Output[0].VolatileDataCount == 1 && Text(ids, 2).Single() == "missing-id", "ID selection and missing identifiers");
            SetData(ids, 0, new object[] { wall, wall }); Solve(doc, ids, check);
            check(Text(ids, 3).Single() == wall.GlobalId, "Ambiguous ID matches are reported");
            var group = Add(doc, new GroupByComponent(), model.Elements); Solve(doc, group, check);
            check(group.Params.Output[0].VolatileData.PathCount == 7 && group.Params.Output[0].VolatileDataCount == 8, "Group By produces stable class branches");
            var rows = new[] { new PropertyRow(wall), new PropertyRow(otherWall) };
            rows[0].Values["test"] = "(assente)";
            check(DataTools.Groups(rows, "test").Count == 2, "Missing group cannot collide with literal group labels");
            rows[0].Values["test"] = "10"; rows[1].Values["test"] = "2";
            check(DataTools.Sort(rows, "test", true, false).First() == otherWall, "Numeric ordering differs correctly from lexical ordering");
            var sort = Add(doc, new SortElementsComponent(), model.Elements); Solve(doc, sort, check);
            check(sort.Params.Output[0].VolatileDataCount == 8, "Sort preserves every object");
            var colors = Add(doc, new ColorByComponent(), model.Elements); Solve(doc, colors, check);
            check(colors.Params.Output[0].VolatileDataCount == 4 && colors.Params.Output[1].VolatileDataCount == 4 && Text(colors, 2).Length == 7, "Categorical colors and legend align with meshes");
            var metrics = Add(doc, new GeometryMetricsComponent(), new[] { wall }); Solve(doc, metrics, check);
            check(Math.Abs(Numbers(metrics, 0).Single() - 34.8) < 1e-5 && Math.Abs(Numbers(metrics, 1).Single() - 4.5) < 1e-5, "Geometry metrics measure SI square/cubic metres in a millimetre Rhino document");
            var open = new IfcElement { Model = wall.Model, Record = wall.Record, MeshMetres = wall.MeshMetres.DuplicateMesh() }; open.MeshMetres.Faces.DeleteFaces(new[] { 0 });
            SetData(metrics, 0, new object[] { open }); Solve(doc, metrics, check);
            check(double.IsNaN(Numbers(metrics, 1).Single()) && !Bools(metrics, 2).Single(), "Open meshes do not produce misleading volumes");
            var origin = Add(doc, new OriginComponent(), model.Elements); Solve(doc, origin, check);
            var offset = ((GH_Vector)origin.Params.Output[0].VolatileData.AllData(true).Single()).Value;
            check(Math.Abs(offset.X + 2.5) < 1e-6 && Math.Abs(offset.Y + 2.15) < 1e-6 && offset.Z == 0, "Local origin preserves elevations in XY mode");
            var validate = Add(doc, new ValidateElementsComponent(), model.Elements); SetBool(validate, 2, true); Solve(doc, validate, check);
            check(validate.Params.Output[1].VolatileDataCount == 4 && validate.Params.Output[3].VolatileData.PathCount == 8, "Data checks report missing geometry with aligned issue branches");
            var baked = Add(doc, new ReadBakedComponent(), null); SetText(baked, 0, rhino.Objects.GetObjectList(Rhino.DocObjects.ObjectType.Mesh).First().Id.ToString(), Guid.NewGuid().ToString()); Solve(doc, baked, check);
            check(baked.Params.Output[0].VolatileDataCount == 1 && Text(baked, 5).Length == 1 && Text(baked, 4).Single().Contains("ifc_class"), "Read Baked retrieves metadata and reports unresolved Rhino IDs");

            var csvModel = Clone(model); var csvWall = csvModel.Elements.First(e => e.Name.StartsWith("Parete 1")); csvWall.Record["name"] = "=HYPERLINK(\"x\"); città\nseconda riga";
            var csv = Add(doc, new ExportCsvComponent(), new[] { csvWall, otherWall }); SetText(csv, 1, "IFC.Name", "IFC.Property.Progetto.Costo", "missing");
            string csvPath = Path.Combine(output, "abaco.csv"); SetText(csv, 2, csvPath); SetBool(csv, 3, true); Solve(doc, csv, check);
            byte[] csvBytes = File.ReadAllBytes(csvPath);
            check(csvBytes.Take(3).SequenceEqual(new byte[] { 239, 187, 191 }) && File.ReadAllText(csvPath).Contains("'=HYPERLINK"), "CSV preserves UTF-8 and neutralizes spreadsheet formulas");
            DateTime csvTime = File.GetLastWriteTimeUtc(csvPath); Solve(doc, csv, check);
            check(File.GetLastWriteTimeUtc(csvPath) == csvTime, "Held CSV Write=True does not write again");
            SetBool(csv, 3, false); doc.NewSolution(true, GH_SolutionMode.Silent); SetBool(csv, 3, true); doc.NewSolution(true, GH_SolutionMode.Silent);
            check(csv.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 1 && File.ReadAllBytes(csvPath).SequenceEqual(csvBytes), "CSV refuses overwrite and leaves the existing file intact");
            SetBool(csv, 3, false); doc.NewSolution(true, GH_SolutionMode.Silent);
            var export = Add(doc, new Export3dmComponent(), model.Elements);
            export.Params.Input[0].AddSource(group.Params.Output[0]);
            string exportPath = Path.Combine(output, "selected.3dm"); SetText(export, 1, exportPath); SetBool(export, 2, true);
            int activeCount = rhino.Objects.Count; uint activeSerial = RhinoDoc.ActiveDoc.RuntimeSerialNumber; Solve(doc, export, check);
            check(rhino.Objects.Count == activeCount && RhinoDoc.ActiveDoc.RuntimeSerialNumber == activeSerial, "3DM export leaves the active Rhino document untouched");
            using (var file = Rhino.FileIO.File3dm.Read(exportPath))
            {
                check(file.Objects.Count == 4 && file.Settings.ModelUnitSystem == UnitSystem.Millimeters, "3DM export flattens all grouped branches and preserves document units");
                check(file.Objects.Any(o => o != null && o.Attributes.GetUserString("IFC.MetadataJSON") != null), "3DM export contains IFC User Text");
            }
            Solve(doc, export, check);
            var diff = Add(doc, new CompareRevisionsComponent(), null); SetData(diff, 0, new object[] { model }); SetData(diff, 1, new object[] { model }); Solve(doc, diff, check);
            check(diff.Params.Output[4].VolatileDataCount == 8 && diff.Params.Output[3].VolatileDataCount == 0, "Revision component recognizes unchanged models");
        }
        var renumbered = Clone(model); foreach (var element in renumbered.Elements) Renumber(element.Record);
        renumbered.Metadata["source_path"] = "C:\\revision_B.ifc";
        var unchanged = Revisions.Compare(model, renumbered, true, 1e-6);
        check(unchanged.Unchanged.Count == 8, "Revision comparison ignores STEP renumbering and source path changes");
        var changed = Clone(model);
        changed.Elements.RemoveAll(e => e.IfcClass == "IfcBuildingElementProxy");
        var changedWall = changed.Elements.First(e => e.Name.StartsWith("Parete 1")); changedWall.Record["psets_effective"]["Pset_WallCommon"]["FireRating"] = "REI 90";
        changed.Elements.First(e => e.IfcClass == "IfcSlab").MeshMetres.Translate(0, 0, 1);
        var added = new IfcElement { Model = changed, Record = (JObject)otherWall.Record.DeepClone(), MeshMetres = otherWall.MeshMetres.DuplicateMesh() }; added.Record["global_id"] = "new-example-globalid"; changed.Elements.Add(added);
        var revision = Revisions.Compare(model, changed, true, 1e-6);
        check(revision.Added.Count == 1 && revision.Removed.Count == 1 && revision.ChangedAfter.Count == 2 && revision.Unchanged.Count == 5, "Revision comparison classifies additions, removals, property and mesh changes");
        check(revision.Reasons.SelectMany(r => r).Contains("psets_effective.Pset_WallCommon.FireRating") && revision.Reasons.SelectMany(r => r).Contains("geometry.mesh_or_placement"), "Revision reasons identify actual changed fields");
        var duplicate = Clone(model); duplicate.Elements.Add(duplicate.Elements[0]); bool refused = false;
        try { Revisions.Compare(model, duplicate, true, 1e-6); } catch (ArgumentException) { refused = true; }
        check(refused, "Duplicate revision GlobalIds produce an explicit ambiguity error");
        using (var mesh = new Mesh())
        {
            var original = wall.MeshMetres; int n = original.Vertices.Count;
            for (int i = n - 1; i >= 0; i--) mesh.Vertices.Add(original.Vertices.Point3dAt(i));
            foreach (var face in original.Faces.Reverse()) mesh.Faces.AddFace(n - 1 - face.C, n - 1 - face.B, n - 1 - face.A);
            check(Revisions.MeshSignature(mesh, 1e-6) == Revisions.MeshSignature(original, 1e-6), "Revision geometry signature ignores vertex/face order and winding");
        }
        File.WriteAllText(Path.Combine(root, "test-output", "last-tool-output.txt"), output);
    }
}
