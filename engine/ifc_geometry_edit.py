"""Replace one occurrence Body with a closed mesh; coordinates arrive in world SI metres."""
from collections import defaultdict
import math

import numpy as np
import ifcopenshell.api
import ifcopenshell.geom
import ifcopenshell.util.placement as placement
import ifcopenshell.util.unit as units


def prepare_geometry(model, target, change):
    if change.get("scope", "instance") != "instance" or not target.is_a("IfcElement"):
        raise ValueError("Geometria modificabile solo su istanze IfcElement.")
    if any(getattr(target, name, ()) for name in ("HasOpenings", "VoidsElements", "FillsVoids")):
        raise ValueError("Elemento coinvolto in aperture/riempimenti: serve aggiornare insieme i tagli IFC; operazione non supportata.")
    definition = getattr(target, "Representation", None)
    if definition and getattr(definition, "HasShapeAspects", ()):
        raise ValueError("Body con ShapeAspect: aggiornamento delle sottoforme non supportato.")
    bodies = [r for r in (definition.Representations if definition else ()) if r.RepresentationIdentifier == "Body"]
    if len(bodies) != 1:
        raise ValueError("Serve una sola rappresentazione Body esistente.")
    body = bodies[0]
    if body.ContextOfItems.CoordinateSpaceDimension != 3:
        raise ValueError("Il contesto Body deve essere tridimensionale.")
    # Reject unsupported placements rather than interpreting them as identity.
    current = target.ObjectPlacement
    seen = set()
    while current:
        if not current.is_a("IfcLocalPlacement") or current.id() in seen:
            raise ValueError("Placement non locale o ciclico non supportato.")
        seen.add(current.id()); current = current.PlacementRelTo
    matrix = placement.get_local_placement(target.ObjectPlacement)
    matrix[:3, 3] *= units.calculate_unit_scale(model)
    raw = change.get("vertices_metres")
    if not isinstance(raw, list) or len(raw) < 4:
        raise ValueError("Mesh senza vertici sufficienti.")
    vertices, mapping, indices = [], {}, []
    for vertex in raw:
        if not isinstance(vertex, list) or len(vertex) != 3 or any(isinstance(v, bool) or not isinstance(v, (int, float)) or not math.isfinite(v) for v in vertex):
            raise ValueError("Coordinate mesh non finite o non valide.")
        key = tuple(vertex)
        if key not in mapping:
            mapping[key] = len(vertices); vertices.append(vertex)
        indices.append(mapping[key])
    vertices = np.asarray(vertices, dtype=float)
    faces = change.get("faces")
    if not isinstance(faces, list) or len(faces) < 4:
        raise ValueError("Mesh senza facce sufficienti.")
    triangles, edges, neighbors, face_keys = [], defaultdict(list), defaultdict(set), set()
    scale = float(np.max(np.ptp(vertices, axis=0)))
    if scale <= 0: raise ValueError("Mesh di dimensione nulla.")
    for f in faces:
        if not isinstance(f, list) or len(f) != 3 or any(type(i) is not int or i < 0 or i >= len(indices) for i in f):
            raise ValueError("Faccia triangolare con indici non validi.")
        tri = tuple(indices[i] for i in f)
        if len(set(tri)) != 3 or tuple(sorted(tri)) in face_keys:
            raise ValueError("Faccia degenere o duplicata.")
        face_keys.add(tuple(sorted(tri)))
        a, b, c = vertices[list(tri)]
        if np.linalg.norm(np.cross(b - a, c - a)) <= scale * scale * 1e-14:
            raise ValueError("Triangolo di area nulla o numericamente degenere.")
        face_id = len(triangles); triangles.append(tri)
        for u, v in ((tri[0], tri[1]), (tri[1], tri[2]), (tri[2], tri[0])):
            edges[tuple(sorted((u, v)))].append((face_id, u < v))
    for incident in edges.values():
        if len(incident) != 2 or incident[0][1] == incident[1][1]:
            raise ValueError("La mesh deve essere chiusa, manifold e orientata coerentemente.")
        a, b = incident[0][0], incident[1][0]; neighbors[a].add(b); neighbors[b].add(a)
    visited, pending = set(), [0]
    while pending:
        f = pending.pop()
        if f not in visited: visited.add(f); pending.extend(neighbors[f] - visited)
    if len(visited) != len(triangles):
        raise ValueError("Sono supportate mesh con un solo involucro connesso; separare gli oggetti disgiunti.")
    shifted = vertices - vertices.mean(axis=0)
    volume = sum(np.dot(shifted[a], np.cross(shifted[b], shifted[c])) for a, b, c in triangles) / 6
    if abs(volume) <= scale ** 3 * 1e-12:
        raise ValueError("Volume mesh nullo o numericamente instabile.")
    if volume < 0: triangles = [(a, c, b) for a, b, c in triangles]
    world = np.column_stack((vertices, np.ones(len(vertices))))
    local = (np.linalg.inv(matrix) @ world.T).T[:, :3]
    return {"body": body, "definition": definition, "local": local, "faces": triangles,
            "bounds": (vertices.min(axis=0), vertices.max(axis=0)), "volume": abs(volume)}


def apply_geometry(model, target, prepared):
    old_body, old_definition = prepared["body"], prepared["definition"]
    new_body = ifcopenshell.api.run("geometry.add_mesh_representation", model, context=old_body.ContextOfItems,
                                  vertices=[prepared["local"].tolist()], faces=[prepared["faces"]])
    for item in new_body.Items:
        if item.is_a("IfcTessellatedFaceSet"): item.Closed = True
    # Do not use assign_representation: on mapped types it can rewrite every occurrence.
    representations = tuple(new_body if r == old_body else r for r in old_definition.Representations)
    target.Representation = model.create_entity("IfcProductDefinitionShape", Name=old_definition.Name,
                                                Description=old_definition.Description, Representations=representations)
    if model.get_total_inverses(old_definition) == 0:
        model.remove(old_definition)
    settings = ifcopenshell.geom.settings(); settings.set("use-world-coords", True)
    shape = ifcopenshell.geom.create_shape(settings, target)
    points = np.asarray(shape.geometry.verts).reshape((-1, 3))
    if not len(points) or not np.isfinite(points).all():
        raise ValueError("La nuova geometria IFC non e triangolabile.")
    lower, upper = prepared["bounds"]
    tolerance = max(1e-6, float(np.max(upper - lower)) * 1e-7)
    if not np.allclose(points.min(axis=0), lower, atol=tolerance, rtol=0) or not np.allclose(points.max(axis=0), upper, atol=tolerance, rtol=0):
        raise ValueError("Verifica coordinate fallita: geometria IFC diversa dalla mesh richiesta.")
    centered = points - points.mean(axis=0)
    output_faces = np.asarray(shape.geometry.faces).reshape((-1, 3))
    output_volume = abs(sum(np.dot(centered[a], np.cross(centered[b], centered[c])) for a, b, c in output_faces) / 6)
    if not math.isclose(output_volume, prepared["volume"], rel_tol=1e-6, abs_tol=1e-12):
        raise ValueError("Verifica volume fallita: la triangolazione IFC differisce dalla mesh richiesta.")
    return {"representation": new_body.RepresentationType, "vertices": len(prepared["local"]), "triangles": len(prepared["faces"]),
            "volume_mesh_m3": prepared["volume"], "bounds_metres": [lower.tolist(), upper.tolist()],
            "warning": "Body sostituito con mesh non parametrica. Qto, proprieta dimensionali, rappresentazioni secondarie e modello analitico non ricalcolati. Stili per faccia non trasferiti."}
