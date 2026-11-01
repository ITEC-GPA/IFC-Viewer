"""Geometry-tree clash checks and reading the existing structural analytical model."""
from contextlib import ExitStack
import math
import uuid
import numpy as np
import ifcopenshell
import ifcopenshell.geom as geom
import ifcopenshell.util.placement as placement
import ifcopenshell.util.unit as uu
from ifc_reader import info, reference


def clashes(request):
    from ifc_tools import snapshot, select
    threshold = float(request.get("distance", .002))
    if not math.isfinite(threshold) or threshold < 0:
        raise ValueError("Distanza/tolleranza finita e non negativa richiesta, in metri.")
    mode = request.get("mode", "Intersection")
    if mode not in ("Intersection", "Collision", "Clearance"):
        raise ValueError("Mode: Intersection, Collision, Clearance.")
    excluded_classes = set(request.get("exclude_classes", []))
    excluded_pairs = {frozenset(p.split("|")) for p in request.get("exclude_pairs", [])}
    if any(len(p) != 2 for p in excluded_pairs):
        raise ValueError("Esclusioni coppie: IFC.Key|IFC.Key.")
    with ExitStack() as stack:
        tree = geom.tree()
        settings = geom.settings(); settings.set(settings.USE_WORLD_COORDS, True)
        a, b, lookup, skipped, loaded = [], [], {}, [], set()
        signatures = {}
        # Keep native iterators and all source models alive for the entire query.
        iterators, models = [], []
        id_offset = 0
        for source_index, source in enumerate(request["sources"]):
            model, metadata = stack.enter_context(snapshot(source["archive"], source["source_sha256"]))
            selected = select(model, source["elements"])
            # The native tree also uses numeric product identifiers internally.
            # Rebase STEP IDs in memory, including every reference, for federation.
            if id_offset:
                copied = ifcopenshell.file(schema=model.schema_identifier)
                entities = {e.id(): copied.create_entity(e.is_a(), id=e.id() + id_offset) for e in model}

                def remap(value):
                    if isinstance(value, ifcopenshell.entity_instance):
                        return entities[value.id()] if value.id() else copied.create_entity(value.is_a(), value.wrappedValue)
                    if isinstance(value, tuple):
                        return tuple(remap(v) for v in value)
                    return value

                for original in model:
                    for i, value in enumerate(original):
                        if value is not None:
                            entities[original.id()][i] = remap(value)
                selected = [entities[e.id()] for e in selected]
                model = copied
            models.append(model)
            id_offset = max(e.id() for e in model) + 1
            chosen = []
            for entity, ref in zip(selected, source["elements"]):
                lookup[entity] = dict(source_index=source_index, step_id=ref["step_id"], global_id=ref["global_id"], key=ref["key"])
                if entity.is_a() in excluded_classes:
                    continue
                chosen.append(entity)
                if ref.get("a"):
                    a.append(entity)
                if ref.get("b"):
                    b.append(entity)
                # The native geometry tree indexes by GUID. Federated files may
                # legitimately repeat GUIDs; isolate them in these temporary files.
                entity.GlobalId = ifcopenshell.guid.compress(uuid.uuid5(uuid.NAMESPACE_OID, "%s:%s" % (source_index, entity.id())).hex)
            if not chosen:
                continue
            iterator = geom.iterator(settings, model, 1, include=chosen); iterators.append(iterator)
            if iterator.initialize():
                while True:
                    shape = iterator.get()
                    if shape.geometry.faces:
                        tree.add_element(shape)
                        element = model.by_id(shape.id)
                        loaded.add(element)
                        vertices = list(zip(*[iter(shape.geometry.verts)] * 3))
                        faces = list(zip(*[iter(shape.geometry.faces)] * 3))
                        signature = tuple(sorted(tuple(sorted(vertices[i] for i in face)) for face in faces))
                        signatures.setdefault(signature, []).append(element)
                    if not iterator.next():
                        break
            skipped.extend(lookup[e] for e in chosen if e not in loaded)
        a = [e for e in a if e in loaded]; b = [e for e in b if e in loaded]
        if mode == "Intersection":
            results = tree.clash_intersection_many(a, b, tolerance=threshold, check_all=True)
        elif mode == "Collision":
            results = tree.clash_collision_many(a, b, allow_touching=request.get("touching", False))
        else:
            results = tree.clash_clearance_many(a, b, clearance=threshold, check_all=True)
        seen, pairs = set(), []
        for result in results:
            first, second = lookup[ifcopenshell.entity_instance(result.a)], lookup[ifcopenshell.entity_instance(result.b)]
            pair = frozenset((first["key"], second["key"]))
            if len(pair) != 2 or pair in seen or pair in excluded_pairs:
                continue
            seen.add(pair)
            pairs.append(dict(a=first, b=second, kind=tree.get_clash_type(result.clash_type),
                              distance_metres=result.distance, p1=list(result.p1), p2=list(result.p2)))
        # Native surface tests may omit two exactly coincident triangulations.
        # Report those explicitly, without inventing a penetration depth.
        aset, bset = set(a), set(b)
        for signature, entities in signatures.items():
            for first_entity in (e for e in entities if e in aset):
                for second_entity in (e for e in entities if e in bset):
                    first, second = lookup[first_entity], lookup[second_entity]
                    pair = frozenset((first["key"], second["key"]))
                    if len(pair) != 2 or pair in seen or pair in excluded_pairs:
                        continue
                    seen.add(pair)
                    point = list(signature[0][0])
                    pairs.append(dict(a=first, b=second, kind="coincident_geometry", distance_metres=0., p1=point, p2=point))
        return dict(pairs=sorted(pairs, key=lambda p: (p["a"]["key"], p["b"]["key"])),
                    skipped=skipped, mode=mode, threshold_metres=threshold,
                    warning="Controllo sulle geometrie tessellate nelle coordinate IFC comuni; nessuna riproiezione GIS. Distance e penetrazione/separazione secondo Kind; coincident_geometry indica triangolazioni identiche e non stima la penetrazione.")


def graph(model, entity):
    return [info(e) for e in model.traverse(entity)] if entity else []


def topology(model, product, scale):
    matrix = np.eye(4)
    if product.ObjectPlacement:
        if not product.ObjectPlacement.is_a("IfcLocalPlacement"):
            return dict(points=[], segments=[], warnings=["Placement analitico non locale; geometria disponibile nel grafo IFC."])
        matrix = placement.get_local_placement(product.ObjectPlacement)

    def point(p):
        values = list(p.Coordinates)
        return ((matrix @ np.array((values + [0.] * (3 - len(values))) + [1.]))[:3] * scale).tolist()

    points, segments, warnings = [], [], []
    representation = product.Representation
    for entity in model.traverse(representation) if representation else []:
        if entity.is_a("IfcVertexPoint") and entity.VertexGeometry.is_a("IfcCartesianPoint"):
            points.append(point(entity.VertexGeometry))
        elif entity.is_a("IfcEdge") and not entity.is_a("IfcOrientedEdge"):
            if entity.is_a("IfcEdgeCurve") and not entity.EdgeGeometry.is_a("IfcLine"):
                warnings.append("Curva analitica non lineare #%s conservata nel grafo, non sostituita da una corda." % entity.id())
                continue
            if all(v.is_a("IfcVertexPoint") and v.VertexGeometry.is_a("IfcCartesianPoint") for v in (entity.EdgeStart, entity.EdgeEnd)):
                segments.append([point(entity.EdgeStart.VertexGeometry), point(entity.EdgeEnd.VertexGeometry)])
        elif entity.is_a("IfcPolyLoop"):
            polygon = [point(p) for p in entity.Polygon]
            points.extend(polygon)
            segments.extend([[polygon[i], polygon[(i + 1) % len(polygon)]] for i in range(len(polygon))])
        elif entity.is_a("IfcPolyline"):
            polygon = [point(p) for p in entity.Points]
            points.extend(polygon)
            segments.extend([[polygon[i], polygon[i + 1]] for i in range(len(polygon) - 1)])
    if representation and not points and not segments:
        warnings.append("Rappresentazione analitica senza topologia lineare supportata: consultare representation_graph.")
    return dict(points=[list(p) for p in sorted(set(tuple(p) for p in points))], segments=segments, warnings=warnings)


def structural_model(model):
    scale = uu.calculate_unit_scale(model)
    rows = []
    for entity in model.by_type("IfcStructuralMember") + model.by_type("IfcStructuralConnection"):
        connections = [r for r in model.get_inverse(entity) if r.is_a("IfcRelConnectsStructuralMember")]
        activities = [r for r in model.get_inverse(entity) if r.is_a("IfcRelConnectsStructuralActivity")]
        shape = topology(model, entity, scale)
        rows.append(dict(element=reference(entity), attributes=info(entity), geometry=shape,
                         connections=[dict(attributes=info(r), member=reference(r.RelatingStructuralMember),
                                           connection=reference(r.RelatedStructuralConnection),
                                           condition=graph(model, r.AppliedCondition),
                                           coordinate_system=graph(model, getattr(r, "ConditionCoordinateSystem", None))) for r in connections],
                         condition=graph(model, getattr(entity, "AppliedCondition", None)),
                         activities=[dict(relation=info(r), activity=info(r.RelatedStructuralActivity),
                                          load=graph(model, r.RelatedStructuralActivity.AppliedLoad),
                                          groups=[reference(a.RelatingGroup) for a in r.RelatedStructuralActivity.HasAssignments if a.is_a("IfcRelAssignsToGroup")]) for r in activities],
                         groups=[reference(r.RelatingGroup) for r in getattr(entity, "HasAssignments", ()) if r.is_a("IfcRelAssignsToGroup")],
                         representation_graph=graph(model, entity.Representation)))
    return dict(rows=rows, models=[info(e) for e in model.by_type("IfcStructuralAnalysisModel")],
                load_groups=[dict(attributes=info(e), members=[reference(o) for r in e.IsGroupedBy for o in r.RelatedObjects])
                             for e in model.by_type("IfcStructuralLoadGroup")],
                units=[info(u) for assignment in model.by_type("IfcUnitAssignment") for unit in assignment.Units for u in model.traverse(unit)],
                geometry_units="metres", data_units="Unita IFC originali; condizioni boolean TRUE=fisso/FALSE=libero, null=non specificato. Rigidezze numeriche e carichi non convertiti.",
                warning="Lettura del modello analitico esistente. Nessuna ricostruzione FEM dalle mesh o verifica di completezza/stabilita.")
