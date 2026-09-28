using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino;
using Rhino.Geometry;
using Rhino.DocObjects;

namespace IfcViewer
{
    public sealed class IfcModel
    {
        public string ArchivePath;
        public JObject Metadata;
        public List<IfcElement> Elements = new List<IfcElement>();
        public string SourceKey { get { return Hash(((string)Metadata["source_path"]).ToUpperInvariant()).Substring(0, 16); } }
        public override string ToString() { return string.Format("{0} | {1} | {2} oggetti", Metadata["source_name"], Metadata["schema"], Elements.Count); }
        internal static string Hash(string text)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }
        public static IfcModel Load(string archivePath)
        {
            var model = new IfcModel { ArchivePath = Path.GetFullPath(archivePath) };
            using (var zip = ZipFile.OpenRead(archivePath))
            {
                var entry = zip.GetEntry("model.json");
                if (entry == null) throw new InvalidDataException("Archivio privo di model.json");
                using (var reader = new StreamReader(entry.Open())) model.Metadata = JObject.Parse(reader.ReadToEnd());
                if ((int)model.Metadata["format_version"] != 1 || (string)model.Metadata["geometry_units"] != "metres")
                    throw new InvalidDataException("Formato archivio IFC non supportato");
                foreach (JObject record in model.Metadata["products"])
                {
                    var element = new IfcElement { Model = model, Record = record };
                    var meshEntry = zip.GetEntry("geometry/" + element.StepId + ".json");
                    if (meshEntry != null)
                    {
                        JObject meshData;
                        using (var reader = new StreamReader(meshEntry.Open())) meshData = JObject.Parse(reader.ReadToEnd());
                        var vertices = meshData["vertices"].ToObject<double[]>();
                        var faces = meshData["faces"].ToObject<int[]>();
                        var mesh = new Mesh();
                        mesh.Vertices.UseDoublePrecisionVertices = true;
                        for (int i = 0; i < vertices.Length; i += 3) mesh.Vertices.Add(vertices[i], vertices[i + 1], vertices[i + 2]);
                        for (int i = 0; i < faces.Length; i += 3) mesh.Faces.AddFace(faces[i], faces[i + 1], faces[i + 2]);
                        mesh.Normals.ComputeNormals(); mesh.Compact();
                        if (!mesh.IsValid) throw new InvalidDataException("Mesh IFC non valida: #" + element.StepId);
                        element.MeshMetres = mesh;
                    }
                    model.Elements.Add(element);
                }
            }
            return model;
        }
    }

    public sealed class IfcElement
    {
        public IfcModel Model;
        public JObject Record;
        public Mesh MeshMetres;
        public int StepId { get { return (int)Record["step_id"]; } }
        public string IfcClass { get { return (string)Record["ifc_class"]; } }
        public string GlobalId { get { return (string)Record["global_id"]; } }
        public string Name { get { return (string)Record["name"] ?? IfcClass; } }
        public string Key { get { return Model.SourceKey + ":" + (string.IsNullOrEmpty(GlobalId) ? "#" + StepId : GlobalId); } }
        public override string ToString() { return IfcClass + " | " + Name + " | " + GlobalId; }
        public Mesh MeshInDocument(RhinoDoc doc, Vector3d offsetMetres)
        {
            if (MeshMetres == null) return null;
            if (doc == null || doc.ModelUnitSystem == UnitSystem.None || doc.ModelUnitSystem == UnitSystem.CustomUnits)
                throw new InvalidOperationException("Impostare unita standard nel documento Rhino prima di visualizzare/bake.");
            var mesh = MeshMetres.DuplicateMesh();
            mesh.Translate(offsetMetres);
            mesh.Scale(RhinoMath.UnitScale(UnitSystem.Meters, doc.ModelUnitSystem));
            return mesh;
        }
        public SortedDictionary<string, string> UserText()
        {
            var values = new SortedDictionary<string, string>(StringComparer.Ordinal);
            values["IFC.GlobalId"] = GlobalId ?? "";
            values["IFC.Key"] = Key;
            values["IFC.SourceKey"] = Model.SourceKey;
            values["IFC.Class"] = IfcClass;
            values["IFC.StepId"] = StepId.ToString(CultureInfo.InvariantCulture);
            values["IFC.Name"] = Name;
            values["IFC.Source"] = (string)Model.Metadata["source_path"];
            values["IFC.SourceSHA256"] = (string)Model.Metadata["source_sha256"];
            values["IFC.Schema"] = (string)Model.Metadata["schema"];
            values["IFC.SourceLengthUnitToMetres"] = Model.Metadata["length_unit_to_metres"].ToString(Formatting.None);
            values["IFC.GeometryStatus"] = (string)Record["geometry_status"];
            Flatten(Record["attributes"], "IFC.Attribute", values);
            Flatten(Record["type_object"], "IFC.Type", values);
            Flatten(Record["psets_effective"], "IFC.Property", values);
            Flatten(Record["psets_instance"], "IFC.InstanceProperty", values);
            Flatten(Record["psets_type"], "IFC.TypeProperty", values);
            Flatten(Record["container"], "IFC.Container", values);
            Flatten(Record["materials"], "IFC.Material", values);
            // Full JSON also includes explicit property units, complex properties,
            // classifications, material layers and typed nominal values.
            values["IFC.MetadataJSON"] = Record.ToString(Formatting.None);
            return values;
        }
        private static string Escape(string key) { return key.Replace("%", "%25").Replace(".", "%2E").Replace("[", "%5B").Replace("]", "%5D"); }
        private static void Flatten(JToken token, string prefix, IDictionary<string, string> output)
        {
            if (token == null) return;
            if (token is JObject)
            {
                foreach (var property in ((JObject)token).Properties()) Flatten(property.Value, prefix + "." + Escape(property.Name), output);
            }
            else if (token is JArray)
            {
                int i = 0; foreach (var item in token) Flatten(item, prefix + "[" + i++ + "]", output);
                if (!token.HasValues) output[prefix] = "[]";
            }
            else output[prefix] = token.Type == JTokenType.String ? (string)token : token.ToString(Formatting.None);
        }
    }

    public static class Engine
    {
        public static string PluginDirectory
        {
            get
            {
                // Grasshopper can COFF-load assemblies; Assembly.Location is
                // then empty, while the component server retains the real path.
                string location = typeof(Engine).Assembly.Location;
                if (string.IsNullOrEmpty(location))
                {
                    var info = Grasshopper.Instances.ComponentServer.FindAssembly(new Guid("D1313D48-9B86-4F54-886C-B19E419E9FD2"));
                    if (info != null) location = info.Location;
                }
                if (string.IsNullOrEmpty(location)) throw new InvalidOperationException("Percorso plugin sconosciuto. Caricare IFC Viewer dalla cartella dist.");
                return Path.GetDirectoryName(location);
            }
        }
        public static string PythonPath()
        {
            string config = Path.Combine(PluginDirectory, "runtime.json");
            if (File.Exists(config))
            {
                string path = (string)JObject.Parse(File.ReadAllText(config))["python"];
                if (!string.IsNullOrEmpty(path)) return Path.GetFullPath(Path.Combine(PluginDirectory, path));
            }
            return Path.GetFullPath(Path.Combine(PluginDirectory, "..", ".venv", "Scripts", "python.exe"));
        }
        // CommandLineToArgvW compatible quoting; no shell is involved.
        internal static string Quote(string arg)
        {
            var b = new StringBuilder("\""); int slashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { b.Append('\\', slashes * 2 + 1); b.Append(c); }
                else { b.Append('\\', slashes); b.Append(c); }
                slashes = 0;
            }
            b.Append('\\', slashes * 2); return b.Append('"').ToString();
        }
        public static IfcModel Read(string input)
        {
            input = Path.GetFullPath(input.Trim().Trim('"'));
            if (!File.Exists(input)) throw new FileNotFoundException("File IFC non trovato", input);
            if (input.EndsWith(".ifcdata.zip", StringComparison.OrdinalIgnoreCase)) return IfcModel.Load(input);
            string python = PythonPath();
            if (!File.Exists(python)) throw new FileNotFoundException("Runtime IFC mancante. Eseguire Setup.cmd nella cartella del progetto.", python);
            string cache = Path.Combine(Path.GetTempPath(), "IfcViewer", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(cache);
            string archive = Path.Combine(cache, "model.ifcdata.zip");
            var start = new ProcessStartInfo(python, Quote(Path.Combine(PluginDirectory, "ifc_reader.py")) + " " + Quote(input) + " " + Quote(archive))
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.EnvironmentVariables["PYTHONUTF8"] = "1";
            using (var process = Process.Start(start))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30 * 60 * 1000))
                {
                    process.Kill(); throw new TimeoutException("Lettura IFC interrotta dopo 30 minuti.");
                }
                if (process.ExitCode != 0) throw new InvalidOperationException(stderr.Result);
                var ignored = stdout.Result;
            }
            return IfcModel.Load(archive);
        }
    }

    public static class Baking
    {
        public static Color ClassColor(string ifcClass)
        {
            var h = IfcModel.Hash(ifcClass);
            return Color.FromArgb(70 + Convert.ToInt32(h.Substring(0, 2), 16) % 150, 70 + Convert.ToInt32(h.Substring(2, 2), 16) % 150, 70 + Convert.ToInt32(h.Substring(4, 2), 16) % 150);
        }
        private static string SafeName(string text)
        {
            var chars = text.Select(c => char.IsControl(c) || "<>/\\\"?:*|".IndexOf(c) >= 0 ? '_' : c).ToArray();
            string result = new string(chars).Trim(); return result.Length == 0 ? "IFC" : result;
        }
        private static int Layer(RhinoDoc doc, string name, Guid parent, Color color)
        {
            var existing = doc.Layers.FirstOrDefault(l => !l.IsDeleted && l.ParentLayerId == parent && l.Name == name);
            if (existing != null) return existing.Index;
            int index = doc.Layers.Add(new Layer { Name = name, ParentLayerId = parent, Color = color });
            if (index < 0) throw new InvalidOperationException("Impossibile creare il layer " + name);
            return index;
        }
        public static List<Guid> Bake(RhinoDoc doc, IEnumerable<IfcElement> items, string rootName, Vector3d offset, bool update, out int skipped)
        {
            if (doc == null) throw new InvalidOperationException("Nessun documento Rhino attivo.");
            if (doc.ModelUnitSystem == UnitSystem.None || doc.ModelUnitSystem == UnitSystem.CustomUnits)
                throw new InvalidOperationException("Impostare unita standard nel documento Rhino.");
            string root = SafeName(rootName);
            var ids = new List<Guid>(); skipped = 0;
            var unique = items.GroupBy(e => e.Key).Select(g => g.First()).ToList();
            // Existing objects must belong to this tool AND this root. Never
            // delete user objects or silently remove elements absent in a filter.
            var existing = new Dictionary<string, RhinoObject>();
            if (update)
            {
                foreach (var obj in doc.Objects.GetObjectList(ObjectType.Mesh))
                {
                    var key = obj.Attributes.GetUserString("IFCViewer.Key");
                    if (obj.Attributes.GetUserString("IFCViewer.Root") == root && key != null && !existing.ContainsKey(key)) existing.Add(key, obj);
                }
            }
            uint undo = doc.BeginUndoRecord("IFC Viewer Bake");
            try
            {
                foreach (var item in unique)
                {
                    using (Mesh mesh = item.MeshInDocument(doc, offset))
                    {
                        if (mesh == null) { skipped++; continue; }
                        int rootIndex = Layer(doc, root, Guid.Empty, Color.SlateGray);
                        string modelName = SafeName(Path.GetFileNameWithoutExtension((string)item.Model.Metadata["source_name"])) + "_" + item.Model.SourceKey.Substring(0, 6);
                        int modelIndex = Layer(doc, modelName, doc.Layers[rootIndex].Id, Color.SlateGray);
                        int classIndex = Layer(doc, item.IfcClass, doc.Layers[modelIndex].Id, ClassColor(item.IfcClass));
                        var attributes = new ObjectAttributes { Name = item.Name, LayerIndex = classIndex, ColorSource = ObjectColorSource.ColorFromLayer };
                        foreach (var pair in item.UserText()) attributes.SetUserString(pair.Key, pair.Value);
                        attributes.SetUserString("IFCViewer.Key", item.Key);
                        attributes.SetUserString("IFCViewer.Root", root);
                        attributes.SetUserString("IFCViewer.OffsetMetres", string.Join(",", new[] { offset.X, offset.Y, offset.Z }.Select(v => v.ToString("R", CultureInfo.InvariantCulture))));
                        RhinoObject old;
                        if (update && existing.TryGetValue(item.Key, out old))
                        {
                            // Refuse locked/hidden objects instead of producing a
                            // second copy or partially changing protected data.
                            if (old.IsLocked || old.IsHidden) throw new InvalidOperationException("Oggetto bloccato/nascosto: " + item.GlobalId);
                            var backupAttributes = old.Attributes.Duplicate();
                            using (var backupGeometry = ((Mesh)old.Geometry).DuplicateMesh())
                            {
                                if (!doc.Objects.Replace(old.Id, mesh)) throw new InvalidOperationException("Replace fallito: " + item.GlobalId);
                                if (!doc.Objects.ModifyAttributes(old.Id, attributes, true))
                                {
                                    doc.Objects.Replace(old.Id, backupGeometry);
                                    doc.Objects.ModifyAttributes(old.Id, backupAttributes, true);
                                    throw new InvalidOperationException("Attributi non applicati: " + item.GlobalId);
                                }
                            }
                            ids.Add(old.Id);
                        }
                        else
                        {
                            Guid id = doc.Objects.AddMesh(mesh, attributes);
                            if (id == Guid.Empty) throw new InvalidOperationException("Bake fallito: " + item.GlobalId);
                            ids.Add(id);
                        }
                    }
                }
            }
            finally { doc.EndUndoRecord(undo); doc.Views.Redraw(); }
            return ids;
        }
    }
}
