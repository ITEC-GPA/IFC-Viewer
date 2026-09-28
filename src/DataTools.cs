using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhino;
using Rhino.Geometry;

namespace IfcViewer
{
    public sealed class PropertyRow
    {
        public IfcElement Element;
        public SortedDictionary<string, string> Values;
        public PropertyRow(IfcElement element) { Element = element; Values = element.UserText(); }
        public bool Get(string key, out string value) { return Values.TryGetValue(key, out value); }
    }

    public sealed class PropertyGroup
    {
        public string Value;
        public bool Found;
        public List<IfcElement> Elements = new List<IfcElement>();
    }

    public static class DataTools
    {
        public static readonly string[] Operators = { "exists", "missing", "=", "!=", "contains", "starts with", ">", ">=", "<", "<=" };
        public static bool Number(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);
        }
        public static Func<PropertyRow, bool> Predicate(string key, string operation, string expected, bool caseSensitive)
        {
            operation = operation.Trim().ToLowerInvariant();
            if (!Operators.Contains(operation)) throw new ArgumentException("Operatore non valido: " + string.Join(", ", Operators));
            bool numeric = operation == ">" || operation == ">=" || operation == "<" || operation == "<=";
            double threshold = 0;
            if (numeric && !Number(expected, out threshold)) throw new ArgumentException("Confronto numerico: usare un numero finito con punto decimale, senza separatori delle migliaia.");
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            return delegate(PropertyRow row)
            {
                string actual; bool found = row.Get(key, out actual);
                if (operation == "exists") return found;
                if (operation == "missing") return !found;
                if (!found) return false; // != does not silently include absent properties.
                if (operation == "=") return string.Equals(actual, expected, comparison);
                if (operation == "!=") return !string.Equals(actual, expected, comparison);
                if (operation == "contains") return actual.IndexOf(expected, comparison) >= 0;
                if (operation == "starts with") return actual.StartsWith(expected, comparison);
                double value; if (!Number(actual, out value)) return false;
                if (operation == ">") return value > threshold;
                if (operation == ">=") return value >= threshold;
                if (operation == "<") return value < threshold;
                return value <= threshold;
            };
        }
        public static List<PropertyGroup> Groups(IEnumerable<PropertyRow> rows, string key)
        {
            var groups = new Dictionary<string, PropertyGroup>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                string value; bool found = row.Get(key, out value);
                string identity = found ? "V:" + value : "M:";
                PropertyGroup group;
                if (!groups.TryGetValue(identity, out group))
                {
                    group = new PropertyGroup { Found = found, Value = found ? value : "(assente)" };
                    groups.Add(identity, group);
                }
                group.Elements.Add(row.Element);
            }
            return groups.Values.OrderBy(g => g.Found ? 0 : 1).ThenBy(g => g.Value, StringComparer.Ordinal).ToList();
        }
        public static List<IfcElement> Sort(IEnumerable<PropertyRow> rows, string key, bool numeric, bool descending)
        {
            var found = new List<Tuple<PropertyRow, string, double>>(); var missing = new List<IfcElement>();
            foreach (var row in rows)
            {
                string text; double number = 0;
                if (!row.Get(key, out text) || (numeric && !Number(text, out number))) missing.Add(row.Element);
                else found.Add(Tuple.Create(row, text, number));
            }
            IEnumerable<Tuple<PropertyRow, string, double>> sorted;
            if (numeric) sorted = descending ? found.OrderByDescending(x => x.Item3) : found.OrderBy(x => x.Item3);
            else sorted = descending ? found.OrderByDescending(x => x.Item2, StringComparer.OrdinalIgnoreCase) : found.OrderBy(x => x.Item2, StringComparer.OrdinalIgnoreCase);
            return sorted.Select(x => x.Item1.Element).Concat(missing).ToList();
        }
        public static List<string> Issues(IfcElement element, IEnumerable<string> required, bool geometryRequired, bool duplicate)
        {
            var result = new List<string>(); var row = new PropertyRow(element);
            if (string.IsNullOrWhiteSpace(element.GlobalId)) result.Add("GlobalId assente");
            if (duplicate) result.Add("GlobalId duplicato nella stessa sorgente/selezione");
            foreach (string key in required.Distinct(StringComparer.Ordinal))
            {
                string value;
                if (!row.Get(key, out value)) result.Add("Parametro assente: " + key);
                else if (string.IsNullOrWhiteSpace(value) || value == "null") result.Add("Parametro vuoto/null: " + key);
            }
            if (geometryRequired && element.MeshMetres == null) result.Add("Mesh assente: " + element.Record["geometry_status"]);
            if (element.MeshMetres != null && !element.MeshMetres.IsValid) result.Add("Mesh non valida");
            return result;
        }
        public static string Csv(IEnumerable<IfcElement> elements, IList<string> keys, string delimiter, bool excelSafe)
        {
            if (delimiter.Length != 1 || delimiter == "\"" || delimiter == "\r" || delimiter == "\n") throw new ArgumentException("Il separatore CSV deve essere un carattere diverso da virgolette/a capo.");
            var rows = elements.Select(e => new PropertyRow(e)).ToList();
            var columns = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct(StringComparer.Ordinal).ToList();
            if (columns.Count == 0) columns = rows.SelectMany(r => r.Values.Keys).Where(k => k != "IFC.MetadataJSON").Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal).ToList();
            Func<string, string> cell = delegate(string value)
            {
                value = value ?? "";
                string trimmed = value.TrimStart();
                if (excelSafe && trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0) value = "'" + value;
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            };
            var csv = new StringBuilder(); csv.AppendLine(string.Join(delimiter, columns.Select(cell)));
            foreach (var row in rows)
                csv.AppendLine(string.Join(delimiter, columns.Select(k => { string value; return cell(row.Get(k, out value) ? value : ""); })));
            return csv.ToString();
        }
        public static string WriteAtomic(string path, string extension, bool overwrite, Action<string> write)
        {
            path = Path.GetFullPath(path);
            if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Il percorso deve terminare con " + extension);
            if (!overwrite && File.Exists(path)) throw new IOException("File gia esistente. Attivare Overwrite o cambiare percorso.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = Path.Combine(Path.GetDirectoryName(path), ".ifc-" + Guid.NewGuid().ToString("N") + extension);
            try
            {
                write(temporary);
                if (File.Exists(path))
                {
                    if (!overwrite) throw new IOException("Il file destinazione esiste gia.");
                    File.Replace(temporary, path, null);
                }
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
            return path;
        }
        public static string Export3dm(IEnumerable<IfcElement> elements, string path, bool overwrite, UnitSystem units, Vector3d offset, string root, out int skipped)
        {
            int noGeometry = 0;
            var rows = elements.ToList();
            if (rows.Count == 0) throw new ArgumentException("Nessun elemento da esportare.");
            if (!offset.IsValid) throw new ArgumentException("Offset non valido.");
            if (units == UnitSystem.None || units == UnitSystem.CustomUnits) throw new ArgumentException("Impostare unita standard nel documento Rhino.");
            string result = WriteAtomic(path, ".3dm", overwrite, delegate(string temporary)
            {
                using (var doc = RhinoDoc.CreateHeadless(null))
                {
                    doc.ModelUnitSystem = units;
                    Baking.Bake(doc, rows, root, offset, true, out noGeometry);
                    if (!doc.Write3dmFile(temporary, new Rhino.FileIO.FileWriteOptions())) throw new IOException("Scrittura .3dm fallita.");
                }
            });
            skipped = noGeometry; return result;
        }
    }
}
