import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "engine"))
from ifc_reader import extract
from create_fixture import create_fixture


class ReaderTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.TemporaryDirectory()
        cls.folder = Path(cls.tmp.name)
        cls.source = cls.folder / "modello città.ifc"
        cls.ifc = create_fixture(cls.source)
        cls.archive = cls.folder / "saved.ifcdata.zip"
        cls.result = extract(cls.source, cls.archive, threads=1)
        with zipfile.ZipFile(cls.archive) as z:
            cls.data = json.loads(z.read("model.json"))
            cls.entities = [json.loads(line) for line in z.read("entities.jsonl").splitlines()]
            cls.meshes = {name: json.loads(z.read(name)) for name in z.namelist() if name.startswith("geometry/")}

    @classmethod
    def tearDownClass(cls):
        cls.tmp.cleanup()

    def test_all_entities_and_original_are_preserved(self):
        self.assertEqual(len(self.entities), len(list(self.ifc)))
        with zipfile.ZipFile(self.archive) as z:
            self.assertEqual(z.read("original/source.ifc"), self.source.read_bytes())
        self.assertEqual(self.data["source_sha256"], hashlib.sha256(self.source.read_bytes()).hexdigest())

    def test_instance_override_and_type_values_remain_distinct(self):
        wall = next(p for p in self.data["products"] if p["name"].startswith("Parete 1"))
        self.assertEqual(wall["psets_type"]["Pset_WallCommon"]["FireRating"], "REI 120")
        self.assertEqual(wall["psets_effective"]["Pset_WallCommon"]["FireRating"], "REI 60")
        self.assertIs(wall["psets_effective"]["Pset_WallCommon"]["IsExternal"], False)
        self.assertIs(wall["psets_effective"]["Pset_WallCommon"]["LoadBearing"], True)
        self.assertEqual(wall["psets_instance"]["Progetto"]["Costo"], 0.0)
        self.assertEqual(wall["container"]["name"], "Piano terra")
        self.assertEqual(wall["materials"][0]["Name"], "Calcestruzzo")

    def test_property_graph_preserves_types_bounds_and_units(self):
        wall = next(p for p in self.data["products"] if p["name"].startswith("Parete 1"))
        bounds = next(e for e in wall["property_graph"]["entities"].values() if e["type"] == "IfcPropertyBoundedValue")
        self.assertEqual(bounds["UpperBoundValue"], {"ifc_type": "IfcLengthMeasure", "value": 300.0})
        self.assertIn(str(bounds["Unit"]["$ref"]), wall["property_graph"]["entities"])
        self.assertEqual(wall["psets_effective"]["Qto_WallBaseQuantities"]["Length"], 5000.0)

    def test_mesh_units_world_placement_and_no_geometry_alignment(self):
        self.assertEqual(self.data["length_unit_to_metres"], 0.001)
        self.assertEqual(self.result["meshes"], 4)
        for product in self.data["products"]:
            if product["name"].startswith("Parete 2"):
                mesh = self.meshes["geometry/%s.json" % product["step_id"]]
                self.assertAlmostEqual(min(mesh["vertices"][1::3]), 4.0)
                self.assertAlmostEqual(max(mesh["vertices"][::3]), 5.0)
            if product["name"] == "Oggetto con soli attributi":
                self.assertEqual(product["geometry_status"], "no_representation")
                self.assertIs(product["psets_effective"]["Dati"]["Verificato"], False)
        self.assertEqual(self.result["products"], len(self.ifc.by_type("IfcProduct")))

    def test_ifczip_roundtrip(self):
        source = self.folder / "source.ifczip"
        with zipfile.ZipFile(source, "w") as z:
            z.write(self.source, "model.ifc")
        destination = self.folder / "zip.ifcdata.zip"
        result = extract(source, destination, threads=1)
        self.assertEqual(result["meshes"], 4)
        with zipfile.ZipFile(destination) as z:
            self.assertEqual(z.read("original/source.ifczip"), source.read_bytes())

    def test_invalid_input_does_not_overwrite_destination(self):
        destination = self.folder / "keep.zip"
        destination.write_bytes(b"keep")
        source = self.folder / "invalid.ifc"
        source.write_text("not an IFC")
        with self.assertRaises(Exception):
            extract(source, destination)
        self.assertEqual(destination.read_bytes(), b"keep")

    def test_ifc2x3_geometry_and_inherited_properties(self):
        source = self.folder / "legacy.ifc"
        create_fixture(source, schema="IFC2X3")
        destination = self.folder / "legacy.ifcdata.zip"
        result = extract(source, destination, threads=1)
        self.assertEqual(result["meshes"], 4)
        with zipfile.ZipFile(destination) as z:
            data = json.loads(z.read("model.json"))
        self.assertEqual(data["schema"], "IFC2X3")
        wall = next(p for p in data["products"] if p["name"].startswith("Parete 1"))
        self.assertEqual(wall["psets_effective"]["Pset_WallCommon"]["FireRating"], "REI 60")

    def test_ifc4x3_geometry(self):
        source = self.folder / "infrastructure.ifc"
        create_fixture(source, schema="IFC4X3")
        result = extract(source, self.folder / "infrastructure.ifcdata.zip", threads=1)
        self.assertEqual(result["meshes"], 4)


if __name__ == "__main__":
    unittest.main()
