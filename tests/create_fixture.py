"""Small, authored IFC4 model with type/occurrence overrides and non-mesh data."""
from pathlib import Path
import numpy as np
import ifcopenshell
import ifcopenshell.api


def create_fixture(destination, schema="IFC4"):
    run = ifcopenshell.api.run
    model = ifcopenshell.file(schema=schema)
    if schema == "IFC2X3":
        person = run("owner.add_person", model, identification="tester", family_name="IFC Viewer")
        organisation = run("owner.add_organisation", model, identification="test", name="IFC Viewer")
        run("owner.add_person_and_organisation", model, person=person, organisation=organisation)
        run("owner.add_application", model)
    project = run("root.create_entity", model, ifc_class="IfcProject", name="IFC Viewer - Modello dimostrativo")
    run("unit.assign_unit", model)  # millimetres, square metres, cubic metres
    context = run("context.add_context", model, context_type="Model")
    body = run("context.add_context", model, context_type="Model", context_identifier="Body", target_view="MODEL_VIEW", parent=context)
    site = run("root.create_entity", model, ifc_class="IfcSite", name="Sito")
    building = run("root.create_entity", model, ifc_class="IfcBuilding", name="Edificio A")
    storey = run("root.create_entity", model, ifc_class="IfcBuildingStorey", name="Piano terra")
    for parent, child in ((project, site), (site, building), (building, storey)):
        run("aggregate.assign_object", model, products=[child], relating_object=parent)
    wall_type = run("root.create_entity", model, ifc_class="IfcWallType", name="Parete esterna 300 mm", predefined_type="STANDARD")
    pset = run("pset.add_pset", model, product=wall_type, name="Pset_WallCommon")
    run("pset.edit_pset", model, pset=pset, properties={"FireRating": "REI 120", "IsExternal": True, "LoadBearing": True})
    concrete = run("material.add_material", model, name="Calcestruzzo", category=None if schema == "IFC2X3" else "concrete")
    run("material.assign_material", model, products=[wall_type], type="IfcMaterial", material=concrete)
    walls = []
    for index, y in enumerate((0.0, 4.0)):
        wall = run("root.create_entity", model, ifc_class="IfcWall", name="Parete %s - caffè" % (index + 1))
        run("type.assign_type", model, related_objects=[wall], relating_type=wall_type)
        run("spatial.assign_container", model, products=[wall], relating_structure=storey)
        representation = run("geometry.add_wall_representation", model, context=body, length=5.0, height=3.0, thickness=0.3)
        run("geometry.assign_representation", model, product=wall, representation=representation)
        matrix = np.eye(4); matrix[1, 3] = y
        run("geometry.edit_object_placement", model, product=wall, matrix=matrix)
        walls.append(wall)
    override = run("pset.add_pset", model, product=walls[0], name="Pset_WallCommon")
    run("pset.edit_pset", model, pset=override, properties={"FireRating": "REI 60", "IsExternal": False})
    custom = run("pset.add_pset", model, product=walls[0], name="Progetto")
    run("pset.edit_pset", model, pset=custom, properties={"Codice": "P-001", "Costo": 0.0, "Note": "Accenti: città, è, à"})
    # A bounded property, with explicit units, exercises the lossless graph.
    bound = model.create_entity("IfcPropertyBoundedValue", Name="Intervallo", UpperBoundValue=model.create_entity("IfcLengthMeasure", 300.0), LowerBoundValue=model.create_entity("IfcLengthMeasure", 250.0), Unit=model.by_type("IfcSIUnit")[0])
    custom.HasProperties = tuple(custom.HasProperties) + (bound,)
    quantities = run("pset.add_qto", model, product=walls[0], name="Qto_WallBaseQuantities")
    run("pset.edit_qto", model, qto=quantities, properties={"Length": 5000.0, "NetVolume": 4.5})
    slab = run("root.create_entity", model, ifc_class="IfcSlab", name="Solaio piano terra", predefined_type="FLOOR")
    run("spatial.assign_container", model, products=[slab], relating_structure=storey)
    representation = run("geometry.add_wall_representation", model, context=body, length=5.0, height=0.2, thickness=4.3)
    run("geometry.assign_representation", model, product=slab, representation=representation)
    matrix = np.eye(4); matrix[2, 3] = -0.2
    run("geometry.edit_object_placement", model, product=slab, matrix=matrix)
    column = run("root.create_entity", model, ifc_class="IfcColumn", name="Pilastro C1")
    run("spatial.assign_container", model, products=[column], relating_structure=storey)
    representation = run("geometry.add_wall_representation", model, context=body, length=0.4, height=3.0, thickness=0.4)
    run("geometry.assign_representation", model, product=column, representation=representation)
    matrix = np.eye(4); matrix[0, 3] = 4.6; matrix[1, 3] = 2.0
    run("geometry.edit_object_placement", model, product=column, matrix=matrix)
    proxy = run("root.create_entity", model, ifc_class="IfcBuildingElementProxy", name="Oggetto con soli attributi")
    run("spatial.assign_container", model, products=[proxy], relating_structure=storey)
    pset = run("pset.add_pset", model, product=proxy, name="Dati")
    run("pset.edit_pset", model, pset=pset, properties={"Stato": "Geometria assente", "Verificato": False})
    Path(destination).parent.mkdir(parents=True, exist_ok=True)
    model.write(str(destination))
    return model


if __name__ == "__main__":
    import sys
    create_fixture(sys.argv[1] if len(sys.argv) > 1 else "examples/demo.ifc")
