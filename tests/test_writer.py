import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
import zipfile

import ifcopenshell
import ifcopenshell.api
import ifcopenshell.util.element as eu

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "engine"))
from ifc_reader import extract
from ifc_writer import write_ifc
from create_fixture import create_fixture


class WriterTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.folder = Path(self.tmp.name)
        self.source = self.folder / "sorgente città.ifc"
        self.model = create_fixture(self.source)
        self.walls = self.model.by_type("IfcWall")
        self.archive = self.folder / "source.ifcdata.zip"
        self.destination = self.folder / "modificato città.ifc"
        self.snapshot()

    def snapshot(self):
        self.model.write(str(self.source))
        extract(self.source, self.archive, threads=1)
        with zipfile.ZipFile(self.archive) as archive:
            self.metadata = json.loads(archive.read("model.json"))

    def change(self, entity=None, kind="property", name="FireRating", value="REI 90", scope="instance", pset="Pset_WallCommon", value_type="Auto"):
        entity = entity or self.walls[0]
        return dict(step_id=entity.id(), global_id=entity.GlobalId, kind=kind, name=name, value=value, scope=scope, pset=pset, value_type=value_type)

    def write(self, changes, destination=None, overwrite=False):
        return write_ifc(self.archive, dict(source_sha256=self.metadata["source_sha256"], changes=changes), destination or self.destination, overwrite)

    def test_roundtrip_preserves_source_identity_geometry_and_other_properties(self):
        before = self.source.read_bytes()
        report = self.write([self.change(), self.change(kind="attribute", name="Name", value="Parete città modificata")])
        reopened = ifcopenshell.open(str(self.destination))
        wall = reopened.by_guid(self.walls[0].GlobalId)
        self.assertEqual(wall.Name, "Parete città modificata")
        self.assertEqual(eu.get_psets(wall)["Pset_WallCommon"]["FireRating"], "REI 90")
        self.assertEqual(eu.get_psets(eu.get_type(wall))["Pset_WallCommon"]["FireRating"], "REI 120")
        self.assertEqual(eu.get_psets(wall)["Progetto"]["Codice"], "P-001")
        for old in self.model.by_type("IfcProduct"):
            new = reopened.by_id(old.id())
            self.assertEqual((new.GlobalId, new.is_a()), (old.GlobalId, old.is_a()))
            for field in ("Representation", "ObjectPlacement"):
                if getattr(old, field, None):
                    self.assertEqual([str(e) for e in self.model.traverse(getattr(old, field))], [str(e) for e in reopened.traverse(getattr(new, field))])
        self.assertEqual(self.source.read_bytes(), before)
        self.assertEqual(report["output_sha256"], hashlib.sha256(self.destination.read_bytes()).hexdigest())

    def test_type_edit_affects_siblings_but_preserves_instance_override(self):
        report = self.write([self.change(scope="type"), self.change(self.walls[1], scope="type")])
        reopened = ifcopenshell.open(str(self.destination))
        self.assertEqual(len(report["changes"]), 1)
        self.assertEqual(report["changes"][0]["type_occurrences"], 2)
        self.assertEqual(eu.get_psets(reopened.by_guid(self.walls[0].GlobalId))["Pset_WallCommon"]["FireRating"], "REI 60")
        self.assertEqual(eu.get_psets(reopened.by_guid(self.walls[1].GlobalId))["Pset_WallCommon"]["FireRating"], "REI 90")

    def test_instance_override_inherits_measure_type_and_explicit_unit(self):
        pset = ifcopenshell.api.run("pset.add_pset", self.model, product=eu.get_type(self.walls[0]), name="Misure")
        prop = self.model.create_entity("IfcPropertySingleValue", Name="Spessore", NominalValue=self.model.create_entity("IfcLengthMeasure", 300.), Unit=self.model.by_type("IfcSIUnit")[0])
        pset.HasProperties = (prop,)
        self.snapshot()
        self.write([self.change(self.walls[1], pset="Misure", name="Spessore", value="350")])
        reopened = ifcopenshell.open(str(self.destination))
        wall = reopened.by_guid(self.walls[1].GlobalId)
        new_pset = reopened.by_id(eu.get_psets(wall, should_inherit=False)["Misure"]["id"])
        self.assertEqual(new_pset.HasProperties[0].NominalValue.is_a(), "IfcLengthMeasure")
        self.assertEqual(new_pset.HasProperties[0].Unit.id(), prop.Unit.id())
        self.assertEqual(eu.get_psets(eu.get_type(wall))["Misure"]["Spessore"], 300.)

    def test_shared_pset_and_property_are_isolated(self):
        pset = ifcopenshell.api.run("pset.add_pset", self.model, product=self.walls[0], name="Condiviso")
        ifcopenshell.api.run("pset.edit_pset", self.model, pset=pset, properties={"Stato": "prima", "Altro": "intatto"})
        ifcopenshell.api.run("pset.assign_pset", self.model, products=[self.walls[1]], pset=pset)
        self.snapshot()
        self.write([self.change(pset="Condiviso", name="Stato", value="dopo")])
        reopened = ifcopenshell.open(str(self.destination))
        self.assertEqual(eu.get_psets(reopened.by_guid(self.walls[0].GlobalId))["Condiviso"]["Stato"], "dopo")
        self.assertEqual(eu.get_psets(reopened.by_guid(self.walls[1].GlobalId))["Condiviso"]["Stato"], "prima")

    def test_zero_false_empty_string_and_null(self):
        self.write([self.change(pset="Nuovo", name="Zero", value="0", value_type="IfcReal"),
                    self.change(pset="Nuovo", name="Bool", value="false", value_type="IfcBoolean"),
                    self.change(pset="Nuovo", name="Testo", value=""), self.change(value=None)])
        psets = eu.get_psets(ifcopenshell.open(str(self.destination)).by_guid(self.walls[0].GlobalId), should_inherit=False)
        self.assertEqual(psets["Nuovo"]["Zero"], 0.)
        self.assertIs(psets["Nuovo"]["Bool"], False)
        self.assertEqual(psets["Nuovo"]["Testo"], "")
        self.assertIn("FireRating", psets["Pset_WallCommon"])
        self.assertIsNone(psets["Pset_WallCommon"]["FireRating"])

    def test_invalid_requests_leave_destination_unchanged(self):
        self.destination.write_bytes(b"esistente")
        bad = [self.change(kind="attribute", name="GlobalId"), self.change(kind="attribute", name="Representation"),
               self.change(name="IsExternal", value="no"), self.change(pset="Nuovo", value="NaN", value_type="IfcReal"),
               self.change(pset="Nuovo", value="1.2", value_type="IfcInteger"), self.change(pset="Progetto", name="Intervallo"),
               self.change(pset="Qto_WallBaseQuantities", name="Length"), self.change(value_type="IfcReal", value="90")]
        for change in bad:
            with self.subTest(change=change), self.assertRaises((ValueError, TypeError)):
                self.write([self.change(), change], overwrite=True)
            self.assertEqual(self.destination.read_bytes(), b"esistente")
        with self.assertRaises(ValueError):
            self.write([self.change(scope="type"), self.change(self.walls[1], scope="type", value="REI 30")], overwrite=True)
        self.assertEqual(self.destination.read_bytes(), b"esistente")

    def test_original_overwrite_identity_and_hash_guards(self):
        with self.assertRaises(ValueError): self.write([self.change()], self.source, True)
        self.write([self.change()])
        with self.assertRaises(FileExistsError): self.write([self.change()])
        bad = self.change(); bad["global_id"] = self.walls[1].GlobalId
        with self.assertRaises(ValueError): self.write([bad], overwrite=True)
        with self.assertRaises(ValueError): write_ifc(self.archive, {"source_sha256": "wrong", "changes": [self.change()]}, self.destination, True)

    def test_embedded_snapshot_works_without_original(self):
        self.source.unlink()
        self.write([self.change()])
        self.assertTrue(self.destination.exists())

    def test_ifczip_and_supported_schemas(self):
        for schema in ("IFC2X3", "IFC4X3"):
            with self.subTest(schema=schema):
                self.model = create_fixture(self.source, schema=schema)
                self.walls = self.model.by_type("IfcWall")
                compressed = self.folder / "input.ifczip"
                with zipfile.ZipFile(compressed, "w") as archive: archive.write(self.source, "input.ifc")
                extract(compressed, self.archive, threads=1)
                with zipfile.ZipFile(self.archive) as archive: self.metadata = json.loads(archive.read("model.json"))
                self.write([self.change(), self.change(pset="Meerkat", name="Numero", value="3", value_type="IfcCountMeasure")], overwrite=True)
                updated = ifcopenshell.open(str(self.destination))
                self.assertEqual(updated.schema, self.model.schema)
                if schema == "IFC4X3":
                    self.assertIsInstance(eu.get_psets(updated.by_guid(self.walls[0].GlobalId))["Meerkat"]["Numero"], int)
                    before = self.destination.read_bytes()
                    with self.assertRaises(ValueError): self.write([self.change(pset="Meerkat", name="Numero", value="3.5", value_type="IfcCountMeasure")], overwrite=True)
                    self.assertEqual(self.destination.read_bytes(), before)


if __name__ == "__main__": unittest.main()
