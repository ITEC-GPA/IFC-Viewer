"""Relationship traversal, scalar table import and IDS validation."""
import csv
import io
import json
from pathlib import Path

import ifcopenshell.util.element as eu
from ifc_reader import metadata as element_metadata, reference
from ifc_writer import prepare


def related(entity, mode):
    if mode == "Type":
        return [eu.get_type(entity)]
    if mode == "Instances":
        return list(eu.get_types(entity)) if entity.is_a("IfcTypeObject") else list(eu.get_types(eu.get_type(entity))) if eu.get_type(entity) else []
    if mode == "Container":
        return [eu.get_container(entity)]
    if mode == "Parent":
        return [eu.get_parent(entity)]
    if mode == "Parts":
        return list(eu.get_parts(entity)) + [o for r in getattr(entity, "IsNestedBy", ()) for o in r.RelatedObjects]
    if mode == "Groups":
        return [r.RelatingGroup for r in getattr(entity, "HasAssignments", ()) if r.is_a("IfcRelAssignsToGroup")]
    if mode == "Members":
        return [o for r in getattr(entity, "IsGroupedBy", ()) for o in r.RelatedObjects]
    if mode == "Openings":
        return [r.RelatedOpeningElement for r in getattr(entity, "HasOpenings", ())]
    if mode == "Fillings":
        return [r.RelatedBuildingElement for r in getattr(entity, "HasFillings", ())]
    if mode == "Connections":
        return [r.RelatedElement for r in getattr(entity, "ConnectedTo", ()) if hasattr(r, "RelatedElement")] + [
            r.RelatingElement for r in getattr(entity, "ConnectedFrom", ()) if hasattr(r, "RelatingElement")]
    raise ValueError("Relazione non supportata: " + mode)


def relationships(model, elements, request):
    mode = request.get("relation", "Container")
    branches, warnings = [], []
    for entity in elements:
        queue = [e for e in related(entity, mode) if e]
        visited, results = {entity.id()}, []
        while queue:
            other = queue.pop(0)
            if other.id() in visited:
                continue
            visited.add(other.id())
            results.append(element_metadata(model, other, warnings))
            if request.get("recursive", False):
                queue.extend(e for e in related(other, mode) if e)
        branches.append(results)
    return {"branches": branches, "warnings": warnings}


def decode(key):
    return key.replace("%2E", ".").replace("%5B", "[").replace("%5D", "]").replace("%25", "%")


def field_request(entity, key, value):
    request = dict(step_id=entity.id(), global_id=entity.GlobalId, value=value, scope="instance")
    if key == "IFC.Name":
        key = "IFC.Attribute.Name"
    for prefix, scope in (("IFC.Attribute.", "instance"), ("IFC.Type.", "type")):
        if key.startswith(prefix):
            name = key[len(prefix):]
            if name not in ("Name", "Description", "Tag"):
                raise ValueError("Attributo protetto: " + key)
            return dict(request, kind="attribute", name=name, scope=scope)
    for prefix, scope in (("IFC.Property.", "instance"), ("IFC.InstanceProperty.", "instance"), ("IFC.TypeProperty.", "type")):
        if key.startswith(prefix):
            parts = key[len(prefix):].split(".")
            if len(parts) != 2 or not all(parts) or parts[1] == "id" or any(c in key for c in "[]"):
                raise ValueError("Colonna non scalare: " + key)
            return dict(request, kind="property", pset=decode(parts[0]), name=decode(parts[1]), scope=scope, value_type="Auto")
    raise ValueError("Colonna non modificabile: " + key)


def import_table(model, metadata, request):
    source = request["csv"]
    text = source if "\n" in source or "\r" in source else Path(source).read_text(encoding="utf-8-sig")
    separator = request.get("separator", ";")
    if len(separator) != 1:
        raise ValueError("Separatore: un solo carattere.")
    reader = csv.DictReader(io.StringIO(text.lstrip("\ufeff")), delimiter=separator, strict=True)
    headers = reader.fieldnames or []
    columns = request.get("columns", [])
    if not columns or len(set(columns)) != len(columns):
        raise ValueError("Selezionare colonne modificabili, senza duplicati.")
    if len(headers) != len(set(headers)) or "IFC.GlobalId" not in headers or any(c not in headers for c in columns):
        raise ValueError("Header duplicati, GlobalId o colonne richieste mancanti.")
    index = {}
    for record in metadata["products"]:
        index.setdefault(record["global_id"], []).append(record["step_id"])
    changes, rows, seen, errors = [], [], set(), []
    for number, row in enumerate(reader, 2):
        gid = row.get("IFC.GlobalId", "")
        try:
            if None in row or any(v is None for v in row.values()):
                raise ValueError("Numero di celle diverso dall'header.")
            if gid in seen:
                raise ValueError("GlobalId duplicato nella tabella.")
            seen.add(gid)
            if len(index.get(gid, [])) != 1:
                raise ValueError("GlobalId assente o ambiguo nel modello.")
            entity = model.by_id(index[gid][0])
            row_changes = [field_request(entity, c, row[c]) for c in columns]
            plans = prepare(model, metadata, row_changes)
            added = 0
            for plan in plans:
                value = plan["nominal"].wrappedValue if "nominal" in plan and plan["nominal"] is not None else plan["change"]["value"]
                if plan["before"] != value:
                    changes.append(plan["change"])
                    added += 1
            rows.append(dict(row=number, global_id=gid, changes=added, valid=True))
        except (ValueError, TypeError, RuntimeError) as ex:
            rows.append(dict(row=number, global_id=gid, valid=False, error=str(ex)))
            errors.append("Riga %s: %s" % (number, ex))
    if not rows:
        errors.append("Tabella senza righe.")
    if changes and not errors:
        try:
            prepare(model, metadata, changes)  # detect cross-row/shared-type conflicts
        except (ValueError, TypeError) as ex:
            errors.append(str(ex))
    return dict(changes=[] if errors else changes, rows=rows, errors=errors, valid=not errors)


def validate_ids(model, request):
    import ifctester
    from ifctester import reporter
    spec = ifctester.open(request["ids_file"], validate=True)
    if not spec.specifications:
        raise ValueError("IDS senza specifiche.")
    spec.validate(model)
    failed, applicable, issues, mismatch = set(), set(), [], []
    for item in spec.specifications:
        if not item.is_ifc_version:
            mismatch.append(item.name)
        applicable.update(e.id() for e in item.applicable_entities)
        failed.update(e.id() for e in item.failed_entities)
        if item.maxOccurs == 0 and not item.requirements:
            failed.update(e.id() for e in item.applicable_entities)
        for facet in item.requirements:
            for failure in facet.failures:
                issues.append(dict(specification=item.name, element=reference(failure["element"]), reason=failure["reason"]))
        if not item.status and not item.applicable_entities:
            issues.append(dict(specification=item.name, reason="Nessun elemento applicabile alla specifica obbligatoria."))
    report = reporter.Json(spec).report()
    if mismatch:
        issues.append(dict(reason="Schema IFC non previsto da: " + ", ".join(mismatch)))
    return dict(passed=sorted(applicable - failed), failed=sorted(failed), applicable=sorted(applicable),
                valid=bool(report["status"]) and not mismatch, issues=issues, report=report)
