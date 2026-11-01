"""Authored analytical fixture, CSV update and IDS for the additional tutorials."""
from pathlib import Path
import sys
import ifcopenshell
import ifcopenshell.api
from ifctester import ids
from create_fixture import create_fixture

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "engine"))
from ifc_reader import extract


def add_structure(model):
    run = ifcopenshell.api.run
    context = next(c for c in model.by_type("IfcGeometricRepresentationContext") if not c.is_a("IfcGeometricRepresentationSubContext"))
    analysis = run("root.create_entity", model, ifc_class="IfcStructuralAnalysisModel", name="Trave analitica 5 m", predefined_type="LOADING_3D")
    vertices = [model.create_entity("IfcVertexPoint", VertexGeometry=model.create_entity("IfcCartesianPoint", Coordinates=p))
                for p in [(0., 0., 3000.), (5000., 0., 3000.)]]
    member = run("root.create_entity", model, ifc_class="IfcStructuralCurveMember", name="Trave analitica", predefined_type="RIGID_JOINED_MEMBER")
    member.Axis = model.create_entity("IfcDirection", DirectionRatios=(0., 0., 1.))
    edge = model.create_entity("IfcEdge", EdgeStart=vertices[0], EdgeEnd=vertices[1])

    def representation(product, item, kind):
        rep = model.create_entity("IfcTopologyRepresentation", ContextOfItems=context, RepresentationIdentifier="Reference", RepresentationType=kind, Items=[item])
        product.Representation = model.create_entity("IfcProductDefinitionShape", Representations=[rep])

    representation(member, edge, "Edge")
    nodes = []
    for i, vertex in enumerate(vertices):
        node = run("root.create_entity", model, ifc_class="IfcStructuralPointConnection", name="Nodo %s" % (i + 1))
        representation(node, vertex, "Vertex")
        node.AppliedCondition = model.create_entity("IfcBoundaryNodeCondition", Name="Cerniera" if i == 0 else "Carrello",
            TranslationalStiffnessX=model.create_entity("IfcBoolean", i == 0),
            TranslationalStiffnessY=model.create_entity("IfcBoolean", True),
            TranslationalStiffnessZ=model.create_entity("IfcBoolean", True),
            RotationalStiffnessX=model.create_entity("IfcBoolean", False),
            RotationalStiffnessY=model.create_entity("IfcBoolean", False),
            RotationalStiffnessZ=model.create_entity("IfcBoolean", False))
        model.create_entity("IfcRelConnectsStructuralMember", GlobalId=ifcopenshell.guid.new(), RelatingStructuralMember=member, RelatedStructuralConnection=node)
        nodes.append(node)
    force_unit = model.create_entity("IfcSIUnit", UnitType="FORCEUNIT", Name="NEWTON")
    assignment = model.by_type("IfcUnitAssignment")[0]; assignment.Units = tuple(assignment.Units) + (force_unit,)
    load = model.create_entity("IfcStructuralLoadSingleForce", Name="Forza verticale", ForceX=0., ForceY=0., ForceZ=-10000.)
    activity = run("root.create_entity", model, ifc_class="IfcStructuralPointAction", name="Fz = -10 kN")
    activity.AppliedLoad = load; activity.GlobalOrLocal = "GLOBAL_COORDS"; activity.DestabilizingLoad = False
    model.create_entity("IfcRelConnectsStructuralActivity", GlobalId=ifcopenshell.guid.new(), RelatingElement=nodes[1], RelatedStructuralActivity=activity)
    case = run("root.create_entity", model, ifc_class="IfcStructuralLoadCase", name="Carico dimostrativo", predefined_type="LOAD_CASE")
    case.ActionType = "VARIABLE_Q"; case.ActionSource = "LIVE_LOAD_Q"; case.Coefficient = 1.
    analysis.LoadedBy = [case]
    run("group.assign_group", model, products=[member] + nodes, group=analysis)
    run("group.assign_group", model, products=[activity], group=case)
    return member, nodes, activity


def main():
    folder = ROOT / "examples"
    model = create_fixture(folder / "tutorial_struttura.ifc")
    add_structure(model)
    model.write(str(folder / "tutorial_struttura.ifc"))
    extract(folder / "tutorial_struttura.ifc", folder / "tutorial_struttura.ifcdata.zip", threads=1)
    base = ifcopenshell.open(str(folder / "tutorial_A.ifc"))
    walls = base.by_type("IfcWall")
    (folder / "14_aggiornamento.csv").write_text("IFC.GlobalId;IFC.Attribute.Description;IFC.Property.Pset_WallCommon.FireRating\n" +
        "".join("%s;Verificato da tabella;REI 90\n" % wall.GlobalId for wall in walls), encoding="utf-8-sig")
    specification = ids.Ids(title="Meerkat - Requisiti informativi pareti")
    rule = ids.Specification(name="Codice e resistenza al fuoco", minOccurs=1, ifcVersion=["IFC4"])
    rule.applicability.append(ids.Entity(name="IFCWALL"))
    rule.requirements.append(ids.Property(propertySet="Progetto", baseName="Codice", dataType="IFCLABEL"))
    rule.requirements.append(ids.Property(propertySet="Pset_WallCommon", baseName="FireRating", dataType="IFCLABEL"))
    specification.specifications.append(rule)
    specification.to_xml(str(folder / "15_requisiti.ids"))


if __name__ == "__main__":
    main()
