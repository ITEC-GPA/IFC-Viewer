import sys
from pathlib import Path
import unittest
import ifcopenshell
import ifcopenshell.api
import ifcopenshell.util.element as eu
import test_writer
from ifc_units import property_units, unit_transform, convert, symbol
from ifc_tools import execute


class AdvancedTests(unittest.TestCase):
    setUp = test_writer.WriterTests.setUp
    snapshot = test_writer.WriterTests.snapshot
    change = test_writer.WriterTests.change
    write = test_writer.WriterTests.write
    def quantity(self, entity=None, **kwargs):
        change = self.change(entity, kind="quantity", pset="Qto_WallBaseQuantities",
                             name="Length", value="6")
        change.update(quantity_type="Length", input_unit="m", method="Misura lungo asse")
        change.update(kwargs)
        return change

    def test_quantity_units_and_shared_set(self):
        qto = self.model.by_id(eu.get_psets(self.walls[0])["Qto_WallBaseQuantities"]["id"])
        ifcopenshell.api.run("pset.assign_pset", self.model, products=[self.walls[1]], pset=qto)
        self.snapshot()
        self.write([self.quantity()])
        result = ifcopenshell.open(str(self.destination))
        self.assertEqual(eu.get_psets(result.by_guid(self.walls[0].GlobalId))["Qto_WallBaseQuantities"]["Length"], 6000.)
        self.assertEqual(eu.get_psets(result.by_guid(self.walls[1].GlobalId))["Qto_WallBaseQuantities"]["Length"], 5000.)
        row = property_units(result, [result.by_guid(self.walls[0].GlobalId)], dict(pset="Qto_WallBaseQuantities", name="Length", unit="m"))["rows"][0]
        self.assertEqual(row["converted"], 6.)
        self.assertTrue(row["valid"])

    def test_quantity_atomic_invalid_dimension_template_method(self):
        self.destination.write_bytes(b"keep")
        for change in [self.quantity(input_unit="m2"), self.quantity(quantity_type="Area"),
                       self.quantity(value="-1"), self.quantity(method=""), self.quantity(name="Inventata")]:
            with self.assertRaises(ValueError):
                self.write([change], overwrite=True)
            self.assertEqual(self.destination.read_bytes(), b"keep")

    def test_property_units_missing_text_and_si_powers(self):
        request = dict(source_sha256=self.metadata["source_sha256"], operation="units",
                       elements=[dict(step_id=w.id(), global_id=w.GlobalId) for w in self.walls],
                       pset="Qto_WallBaseQuantities", name="Length", unit="m")
        rows = execute(self.archive, request)["rows"]
        self.assertEqual(rows[0]["converted"], 5.)
        self.assertFalse(rows[1]["valid"])
        for name, power in [("METRE", 1), ("SQUARE_METRE", 2), ("CUBIC_METRE", 3)]:
            unit = self.model.create_entity("IfcSIUnit", UnitType={1:"LENGTHUNIT",2:"AREAUNIT",3:"VOLUMEUNIT"}[power], Name=name, Prefix="MILLI")
            self.assertAlmostEqual(unit_transform(unit)[1], .001 ** power)
        celsius = self.model.create_entity("IfcSIUnit", UnitType="THERMODYNAMICTEMPERATUREUNIT", Name="DEGREE_CELSIUS")
        self.assertAlmostEqual(convert(0, unit_transform(celsius), symbol("K")), 273.15)

    def test_relationships_type_instances_container_recursive(self):
        from ifc_query import relationships
        result = relationships(self.model, self.walls, dict(relation="Type"))
        self.assertEqual(result["branches"][0][0]["ifc_class"], "IfcWallType")
        siblings = relationships(self.model, [eu.get_type(self.walls[0])], dict(relation="Instances"))
        self.assertEqual(len(siblings["branches"][0]), 2)
        parents = relationships(self.model, [self.walls[0]], dict(relation="Parent", recursive=True))
        self.assertEqual([r["ifc_class"] for r in parents["branches"][0]], ["IfcBuildingStorey", "IfcBuilding", "IfcSite", "IfcProject"])

    def test_csv_unicode_quotes_duplicates_and_type_conflicts(self):
        from ifc_query import import_table
        column = "IFC.Attribute.Description"
        csv = 'IFC.GlobalId;%s\n%s;"Città; piano\nseconda riga"\n' % (column, self.walls[0].GlobalId)
        result = import_table(self.model, self.metadata, dict(csv=csv, columns=[column]))
        self.assertTrue(result["valid"])
        self.write(result["changes"])
        self.assertEqual(ifcopenshell.open(str(self.destination)).by_guid(self.walls[0].GlobalId).Description, "Città; piano\nseconda riga")
        duplicate = csv + "%s;duplicato\n" % self.walls[0].GlobalId
        result = import_table(self.model, self.metadata, dict(csv=duplicate, columns=[column]))
        self.assertFalse(result["valid"])
        self.assertEqual(result["changes"], [])
        column = "IFC.TypeProperty.Pset_WallCommon.FireRating"
        csv = "IFC.GlobalId;%s\n%s;REI 90\n%s;REI 30\n" % (column, self.walls[0].GlobalId, self.walls[1].GlobalId)
        self.assertFalse(import_table(self.model, self.metadata, dict(csv=csv, columns=[column]))["valid"])

    def test_ids_pass_fail_absent_and_invalid(self):
        from ifctester import ids
        from ifc_query import validate_ids
        definition = ids.Ids(title="Test Meerkat")
        rule = ids.Specification(name="Codice pareti", minOccurs=1, ifcVersion=["IFC4"])
        rule.applicability.append(ids.Entity(name="IFCWALL"))
        rule.requirements.append(ids.Property(propertySet="Progetto", baseName="Codice", dataType="IFCLABEL"))
        definition.specifications.append(rule)
        path = self.folder / "test.ids"; definition.to_xml(str(path))
        result = validate_ids(self.model, dict(ids_file=str(path)))
        self.assertFalse(result["valid"])
        self.assertEqual(result["failed"], [self.walls[1].id()])
        self.assertEqual(result["passed"], [self.walls[0].id()])
        rule.applicability = [ids.Entity(name="IFCBEAM")]
        definition.to_xml(str(path))
        result = validate_ids(self.model, dict(ids_file=str(path)))
        self.assertFalse(result["valid"])
        self.assertTrue(result["issues"])

    def test_duplicate_type_isolates_properties_and_preserves_siblings(self):
        change = self.change(kind="assign_type", name="Type", value="", scope="instance")
        change.update(type_global_id="", duplicate=True, type_name="Nuovo tipo", map_geometry=False)
        report = self.write([change])
        result = ifcopenshell.open(str(self.destination))
        new_type = eu.get_type(result.by_guid(self.walls[0].GlobalId))
        old_type = eu.get_type(result.by_guid(self.walls[1].GlobalId))
        self.assertNotEqual(new_type.GlobalId, old_type.GlobalId)
        self.assertEqual(new_type.Name, "Nuovo tipo")
        self.assertNotEqual(new_type.HasPropertySets[0].id(), old_type.HasPropertySets[0].id())
        self.assertEqual(eu.get_psets(result.by_guid(self.walls[0].GlobalId))["Pset_WallCommon"]["FireRating"], "REI 60")
        self.assertEqual(report["changes"][0]["type_occurrences"], 1)
        with self.assertRaises(ValueError):
            self.write([change, self.change()], overwrite=True)

    def test_extract_selection_dependencies_and_original_guard(self):
        from ifc_model_ops import extract_model
        before = self.source.read_bytes()
        result = extract_model(self.model, self.metadata, [self.walls[0]], self.destination)
        reopened = ifcopenshell.open(str(self.destination))
        self.assertEqual(len(reopened.by_type("IfcWall")), 1)
        self.assertEqual(len(reopened.by_type("IfcSlab")), 0)
        self.assertEqual(len(reopened.by_type("IfcBuildingStorey")), 1)
        self.assertEqual(eu.get_type(reopened.by_type("IfcWall")[0]).Name, "Parete esterna 300 mm")
        self.assertEqual(eu.get_psets(reopened.by_type("IfcWall")[0])["Progetto"]["Codice"], "P-001")
        self.assertEqual(self.source.read_bytes(), before)
        self.assertEqual(result["retained_products"], 4)
        with self.assertRaises(ValueError):
            extract_model(reopened, self.metadata, reopened.by_type("IfcWall"), self.source, overwrite=True)

    def test_material_layers_millimetres_and_inheritance(self):
        from ifc_model_ops import materials
        wall_type = eu.get_type(self.walls[0])
        ifcopenshell.api.run("material.unassign_material", self.model, products=[wall_type])
        layer_set = self.model.create_entity("IfcMaterialLayerSet", MaterialLayers=[
            self.model.create_entity("IfcMaterialLayer", Material=self.model.by_type("IfcMaterial")[0], LayerThickness=300.)])
        ifcopenshell.api.run("material.assign_material", self.model, products=[wall_type], type="IfcMaterialLayerSet", material=layer_set)
        row = materials(self.model, [self.walls[0]])["rows"][0]
        self.assertEqual(row["scope"], "type")
        self.assertEqual(row["items"][0]["thickness_metres"], .3)


if __name__ == "__main__":
    unittest.main()
