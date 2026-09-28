"""Author tutorial IFC sources, preserving A/B identities and the original demo."""
import json
from pathlib import Path
import sys
import uuid

import numpy as np
import ifcopenshell
import ifcopenshell.api
from ifcopenshell.util.placement import get_local_placement
from ifcopenshell.util.unit import calculate_unit_scale
from ifcopenshell.util.element import get_psets

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "engine"))
from ifc_reader import extract

run = ifcopenshell.api.run


def stable_id(name):
    return ifcopenshell.guid.compress(uuid.uuid5(uuid.NAMESPACE_URL, "ifc-viewer/tutorial/" + name).hex)


def place(model, obj, xyz):
    matrix = np.eye(4)
    matrix[:3, 3] = xyz
    run("geometry.edit_object_placement", model, product=obj, matrix=matrix)


def solid(model, storey, body, ifc_class, name, xyz, length, height, thickness, key):
    obj = run("root.create_entity", model, ifc_class=ifc_class, name=name)
    obj.GlobalId = stable_id(key)
    run("spatial.assign_container", model, products=[obj], relating_structure=storey)
    representation = run("geometry.add_wall_representation", model, context=body, length=length, height=height, thickness=thickness)
    run("geometry.assign_representation", model, product=obj, representation=representation)
    place(model, obj, xyz)
    return obj


def main():
    folder = ROOT / "examples"
    model = ifcopenshell.open(str(folder / "demo.ifc"))
    body = next(c for c in model.by_type("IfcGeometricRepresentationSubContext") if c.ContextIdentifier == "Body")
    building = model.by_type("IfcBuilding")[0]
    floor1 = run("root.create_entity", model, ifc_class="IfcBuildingStorey", name="Piano primo")
    floor1.GlobalId = stable_id("piano-primo")
    floor1.Elevation = 3200.0
    run("aggregate.assign_object", model, products=[floor1], relating_object=building)
    upper_wall = solid(model, floor1, body, "IfcWall", "Parete P1", (0.5, 0, 3.2), 4, 3, 0.25, "parete-p1")
    run("type.assign_type", model, related_objects=[upper_wall], relating_type=model.by_type("IfcWallType")[0])
    pset = run("pset.add_pset", model, product=upper_wall, name="Progetto")
    run("pset.edit_pset", model, pset=pset, properties={"Codice": "P-003", "Note": "Parete al piano primo"})
    solid(model, floor1, body, "IfcSlab", "Solaio piano primo", (0, 0, 3), 5, 0.2, 4.3, "solaio-p1")
    wall1 = next(w for w in model.by_type("IfcWall") if w.Name.startswith("Parete 1"))
    wall2 = next(w for w in model.by_type("IfcWall") if w.Name.startswith("Parete 2"))
    column = model.by_type("IfcColumn")[0]
    a_path = folder / "tutorial_A.ifc"
    model.write(str(a_path))

    # Genuine revision B, with changes in the source IFC rather than patched JSON.
    b = ifcopenshell.open(str(a_path))
    revised_wall = b.by_guid(wall1.GlobalId)
    pset = run("pset.add_pset", b, product=revised_wall, name="Pset_WallCommon")
    run("pset.edit_pset", b, pset=pset, properties={"FireRating": "REI 90"})
    revised_column = b.by_guid(column.GlobalId)
    transform = get_local_placement(revised_column.ObjectPlacement)
    transform[:3, 3] *= calculate_unit_scale(b)
    transform[0, 3] -= 0.8
    run("geometry.edit_object_placement", b, product=revised_column, matrix=transform)
    run("root.remove_product", b, product=b.by_guid(wall2.GlobalId))
    b_body = next(c for c in b.by_type("IfcGeometricRepresentationSubContext") if c.ContextIdentifier == "Body")
    new_wall = solid(b, b.by_guid(floor1.GlobalId), b_body, "IfcWall", "Parete nuova - revisione B", (1, 4, 3.2), 2, 3, 0.25, "parete-nuova-b")
    run("type.assign_type", b, related_objects=[new_wall], relating_type=b.by_type("IfcWallType")[0])
    pset = run("pset.add_pset", b, product=new_wall, name="Progetto")
    run("pset.edit_pset", b, pset=pset, properties={"Codice": "P-004"})
    b_path = folder / "tutorial_B.ifc"
    b.write(str(b_path))

    # Large engineering coordinates, intentionally without GIS map conversion.
    distant = ifcopenshell.open(str(a_path))
    scale = calculate_unit_scale(distant)
    for obj in distant.by_type("IfcProduct"):
        if getattr(obj, "Representation", None) and obj.ObjectPlacement:
            transform = get_local_placement(obj.ObjectPlacement)
            transform[:3, 3] *= scale
            transform[0, 3] += 650000.0
            transform[1, 3] += 4860000.0
            run("geometry.edit_object_placement", distant, product=obj, matrix=transform)
    distant_path = folder / "tutorial_coordinate_grandi.ifc"
    distant.write(str(distant_path))

    manifests = []
    for source in (a_path, b_path, distant_path):
        archive = source.with_suffix(".ifcdata.zip")
        manifests.append(extract(source, archive, threads=1))
    assert all(m["products"] == 11 and m["meshes"] == 6 for m in manifests)
    assert get_psets(model.by_guid(wall1.GlobalId))["Pset_WallCommon"]["FireRating"] == "REI 60"
    assert get_psets(b.by_guid(wall1.GlobalId))["Pset_WallCommon"]["FireRating"] == "REI 90"
    expected = {
        "products": 11, "meshes": 6, "walls": 3, "wall_volumes_m3": [4.5, 4.5, 3.0],
        "floor_counts": {"Piano terra": 4, "Piano primo": 2},
        "quality_pass": 2, "quality_fail": 1, "missing_code_global_id": wall2.GlobalId,
        "revision": {"added": [new_wall.GlobalId], "removed": [wall2.GlobalId],
                     "changed": [wall1.GlobalId, column.GlobalId], "unchanged_count": 8},
        "coordinate_offset_metres": [650000.0, 4860000.0, 0.0],
    }
    (folder / "scenari.json").write_text(json.dumps(expected, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps({"models": manifests, "expected": expected}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
