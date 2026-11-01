import unittest

import numpy as np
import ifcopenshell
import ifcopenshell.api
import ifcopenshell.geom
import ifcopenshell.util.element as eu
import ifcopenshell.util.placement as placement

import test_writer
from create_fixture import create_fixture


def world_mesh(product):
    settings = ifcopenshell.geom.settings(); settings.set("use-world-coords", True)
    shape = ifcopenshell.geom.create_shape(settings, product)
    return np.array(shape.geometry.verts).reshape((-1, 3)), np.array(shape.geometry.faces).reshape((-1, 3))


class GeometryWriterTests(unittest.TestCase):
    def setUp(self):
        self.case = test_writer.WriterTests()
        self.case.setUp()
        self.addCleanup(self.case.doCleanups)

    def change(self, wall=None, shift=(1.25, -2., 0.3)):
        s = self.case; wall = wall or s.walls[0]
        vertices, faces = world_mesh(wall)
        return dict(step_id=wall.id(), global_id=wall.GlobalId, scope="instance", kind="geometry", name="Body",
                    vertices_metres=(vertices + shift).tolist(), faces=faces.tolist())

    def test_schema_roundtrip_units_metadata_placements_and_neighbors(self):
        s = self.case
        for schema in ("IFC2X3", "IFC4", "IFC4X3"):
            with self.subTest(schema=schema):
                s.model = create_fixture(s.source, schema); s.walls = s.model.by_type("IfcWall"); s.snapshot()
                change = self.change(s.walls[1]); original = s.source.read_bytes()
                other_points, _ = world_mesh(s.walls[0])
                report = s.write([change, s.change(s.walls[1], kind="attribute", name="Description", value="Geometria modificata")], overwrite=True)
                model = ifcopenshell.open(str(s.destination)); updated = model.by_guid(s.walls[1].GlobalId)
                points, _ = world_mesh(updated); requested = np.array(change["vertices_metres"])
                np.testing.assert_allclose(points.min(axis=0), requested.min(axis=0), atol=1e-6, rtol=0)
                np.testing.assert_allclose(points.max(axis=0), requested.max(axis=0), atol=1e-6, rtol=0)
                np.testing.assert_allclose(placement.get_local_placement(updated.ObjectPlacement), placement.get_local_placement(s.walls[1].ObjectPlacement))
                np.testing.assert_allclose(world_mesh(model.by_guid(s.walls[0].GlobalId))[0], other_points)
                self.assertEqual(eu.get_psets(updated), eu.get_psets(s.walls[1]))
                self.assertEqual(updated.Description, "Geometria modificata")
                self.assertEqual(s.source.read_bytes(), original)
                self.assertAlmostEqual(report["changes"][0]["volume_mesh_m3"], 4.5, places=6)
                self.assertEqual(updated.Representation.Representations[0].RepresentationType, "Brep" if schema == "IFC2X3" else "Tessellation")

    def test_rotation_large_coordinates_and_scaled_mesh(self):
        s = self.case
        matrix = placement.rotation(37., "Z"); matrix[:3, 3] = (650000., 4860000., 7.)
        ifcopenshell.api.run("geometry.edit_object_placement", s.model, product=s.walls[0], matrix=matrix)
        s.snapshot(); change = self.change()
        points = np.array(change["vertices_metres"]); center = points.mean(axis=0)
        change["vertices_metres"] = (center + (points - center) * [1.25, 0.8, 1.5]).tolist()
        s.write([change]); model = ifcopenshell.open(str(s.destination))
        actual, _ = world_mesh(model.by_guid(s.walls[0].GlobalId)); expected = np.array(change["vertices_metres"])
        np.testing.assert_allclose(actual.min(axis=0), expected.min(axis=0), atol=1e-6, rtol=0)
        np.testing.assert_allclose(actual.max(axis=0), expected.max(axis=0), atol=1e-6, rtol=0)

    def test_shared_mapped_geometry_only_changes_selected_occurrence(self):
        s = self.case
        representation = s.walls[0].Representation.Representations[0]
        for wall in s.walls: wall.Representation = None
        ifcopenshell.api.run("geometry.assign_representation", s.model, product=eu.get_type(s.walls[0]), representation=representation)
        # Both occurrences also share the product definition shape.
        s.walls[1].Representation = s.walls[0].Representation
        s.snapshot(); other, _ = world_mesh(s.walls[1]); type_map = str(eu.get_type(s.walls[0]).RepresentationMaps)
        s.write([self.change()]); model = ifcopenshell.open(str(s.destination))
        np.testing.assert_allclose(world_mesh(model.by_guid(s.walls[1].GlobalId))[0], other)
        self.assertEqual(str(eu.get_type(model.by_guid(s.walls[0].GlobalId)).RepresentationMaps), type_map)

    def test_invalid_mesh_and_openings_preserve_destination(self):
        s = self.case; s.destination.write_bytes(b"precedente")
        missing_face = self.change(); missing_face["faces"].pop()
        bad_index = self.change(); bad_index["faces"][0][0] = 9999
        nan = self.change(); nan["vertices_metres"][0][0] = float("nan")
        degenerate = self.change(); degenerate["faces"][0][0] = degenerate["faces"][0][1]
        for change in (missing_face, bad_index, nan, degenerate):
            with self.assertRaises(ValueError): s.write([change], overwrite=True)
            self.assertEqual(s.destination.read_bytes(), b"precedente")
        opening = ifcopenshell.api.run("root.create_entity", s.model, ifc_class="IfcOpeningElement")
        ifcopenshell.api.run("feature.add_feature", s.model, feature=opening, element=s.walls[0])
        s.snapshot()
        with self.assertRaisesRegex(ValueError, "aperture"): s.write([self.change()], overwrite=True)
        self.assertEqual(s.destination.read_bytes(), b"precedente")


if __name__ == "__main__": unittest.main()
