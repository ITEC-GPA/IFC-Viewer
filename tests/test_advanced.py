import sys
from pathlib import Path
import unittest
import ifcopenshell
import ifcopenshell.api
import ifcopenshell.util.element as eu
from test_writer import WriterTests
from ifc_units import property_units, unit_transform, convert, symbol
from ifc_tools import execute


class AdvancedTests(unittest.TestCase):
    setUp = WriterTests.setUp
    snapshot = WriterTests.snapshot
    change = WriterTests.change
    write = WriterTests.write
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


if __name__ == "__main__":
    unittest.main()
