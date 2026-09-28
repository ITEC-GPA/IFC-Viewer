"""IFC extraction worker. No Rhino dependency; all geometry is in SI metres."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import shutil
import tempfile
import zipfile

import ifcopenshell
import ifcopenshell.geom
import ifcopenshell.util.element as element_util
import ifcopenshell.util.unit as unit_util

FORMAT_VERSION = 1


def json_value(value):
    if isinstance(value, ifcopenshell.entity_instance):
        if value.id():
            return {"$ref": value.id(), "ifc_class": value.is_a()}
        return {"ifc_type": value.is_a(), "value": json_value(value.wrappedValue)}
    if isinstance(value, dict):
        return {str(k): json_value(v) for k, v in value.items()}
    if isinstance(value, (tuple, list)):
        return [json_value(v) for v in value]
    if isinstance(value, float) and not math.isfinite(value):
        return str(value)
    return value


def info(entity):
    return json_value(entity.get_info()) if entity else None


def property_graph(model, element, type_object):
    """Preserve every IFC property kind, explicit units and nominal value types.

    get_psets is a useful projection, not a lossless IFC representation. Keep the
    forward graph of the actual definitions as well (including unusual types).
    """
    roots = []
    for rel in getattr(element, "IsDefinedBy", ()):
        if rel.is_a("IfcRelDefinesByProperties"):
            definition = rel.RelatingPropertyDefinition
            roots.extend(definition if isinstance(definition, tuple) else [definition])
    for obj in (element, type_object):
        roots.extend(getattr(obj, "HasPropertySets", ()) or ())
    entities = {}
    for root in roots:
        for entity in model.traverse(root):
            entities[str(entity.id())] = info(entity)
    return {"roots": [r.id() for r in roots], "entities": entities}


def reference(entity):
    if not entity:
        return None
    return {"step_id": entity.id(), "ifc_class": entity.is_a(),
            "global_id": getattr(entity, "GlobalId", None), "name": getattr(entity, "Name", None)}


def metadata(model, product, warnings):
    type_obj = element_util.get_type(product)
    record = {"step_id": product.id(), "global_id": getattr(product, "GlobalId", None),
              "ifc_class": product.is_a(), "name": getattr(product, "Name", None),
              "attributes": info(product), "type_object": info(type_obj),
              "property_graph": property_graph(model, product, type_obj),
              "geometry_status": "no_representation"}
    try:
        record["psets_instance"] = json_value(element_util.get_psets(product, should_inherit=False))
        record["psets_type"] = json_value(element_util.get_psets(type_obj)) if type_obj else {}
        record["psets_effective"] = json_value(element_util.get_psets(product, should_inherit=True))
    except Exception as exc:
        warnings.append("#%s: property projection: %s (raw property graph retained)" % (product.id(), exc))
    try:
        record["container"] = reference(element_util.get_container(product))
        record["materials"] = [info(m) for m in element_util.get_materials(product)]
    except Exception as exc:
        warnings.append("#%s: context: %s" % (product.id(), exc))
    # Preserve complete association graphs (material layers, classifications,
    # document references), including associations inherited from the type.
    associations = []
    for obj in (product, type_obj):
        for rel in getattr(obj, "HasAssociations", ()) or ():
            attrs = rel.get_info()
            relating = {k: v for k, v in attrs.items() if k.startswith("Relating")}
            nodes = {}
            for value in relating.values():
                if isinstance(value, ifcopenshell.entity_instance):
                    for node in model.traverse(value):
                        nodes[str(node.id())] = info(node)
            associations.append({"relation": rel.is_a(), "attributes": json_value(relating), "entities": nodes})
    record["associations"] = associations
    return record


def extract(source, destination, threads=0, deflection=0.001):
    """Create a self-contained archive atomically, retaining original source bytes."""
    source, destination = Path(source).resolve(), Path(destination).resolve()
    if source == destination:
        raise ValueError("Output must differ from the IFC input")
    if source.suffix.lower() not in (".ifc", ".ifczip"):
        raise ValueError("Supported inputs: .ifc, .ifczip")
    if not math.isfinite(deflection) or deflection <= 0:
        raise ValueError("Deflection must be positive (metres)")
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="ifc-extract-") as temp:
        temp = Path(temp)
        # Read from an immutable snapshot: geometry, attributes and source agree.
        snapshot = temp / ("source" + source.suffix.lower())
        shutil.copyfile(source, snapshot)
        digest = hashlib.sha256(snapshot.read_bytes()).hexdigest()
        model = ifcopenshell.open(str(snapshot))
        warnings = []
        products = model.by_type("IfcProduct")
        records = {p.id(): metadata(model, p, warnings) for p in products}
        settings = ifcopenshell.geom.settings()
        settings.set("use-world-coords", True)
        settings.set("weld-vertices", True)
        settings.set("mesher-linear-deflection", deflection)
        candidates = [p for p in products if getattr(p, "Representation", None)]
        for product in candidates:
            records[product.id()]["geometry_status"] = "failed_or_unsupported"
        geometry_count = 0
        archive_temp = temp / "model.ifcdata.zip"
        with zipfile.ZipFile(archive_temp, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
            archive.write(snapshot, "original/" + snapshot.name)
            if candidates:
                iterator = ifcopenshell.geom.iterator(settings, model, threads or min(8, os.cpu_count() or 1), include=candidates)
                if iterator.initialize():
                    while True:
                        shape = iterator.get()
                        mesh = {"vertices": list(shape.geometry.verts), "faces": list(shape.geometry.faces)}
                        if mesh["vertices"] and mesh["faces"]:
                            archive.writestr("geometry/%s.json" % shape.id, json.dumps(mesh, separators=(",", ":"), allow_nan=False))
                            records[shape.id]["geometry_status"] = "ok"
                            geometry_count += 1
                        if not iterator.next():
                            break
            for record in records.values():
                if record["geometry_status"] == "failed_or_unsupported":
                    warnings.append("#%s %s: no triangulated geometry; metadata retained" % (record["step_id"], record["ifc_class"]))
            # Every forward IFC attribute for every entity, including relations
            # and non-products. References retain their exact STEP identifiers.
            with archive.open("entities.jsonl", "w") as stream:
                for entity in model:
                    stream.write((json.dumps(info(entity), ensure_ascii=False, allow_nan=False) + "\n").encode("utf-8"))
            payload = {"format_version": FORMAT_VERSION, "source_path": str(source),
                       "source_name": source.name, "source_sha256": digest, "schema": model.schema,
                       "geometry_units": "metres", "length_unit_to_metres": unit_util.calculate_unit_scale(model),
                       "ifcopenshell_version": ifcopenshell.version, "deflection_metres": deflection,
                       "products": list(records.values()), "warnings": warnings, "geometry_count": geometry_count}
            archive.writestr("model.json", json.dumps(payload, ensure_ascii=False, separators=(",", ":"), allow_nan=False))
        # Copy via a sibling temporary file so os.replace remains atomic even
        # when the OS temporary directory and export directory use other drives.
        fd, staging = tempfile.mkstemp(prefix=".ifc-", dir=str(destination.parent))
        os.close(fd)
        try:
            shutil.copyfile(archive_temp, staging)
            os.replace(staging, destination)
        finally:
            if os.path.exists(staging):
                os.unlink(staging)
    return {"archive": str(destination), "products": len(records), "meshes": geometry_count,
            "warnings": len(warnings), "source_sha256": digest}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source")
    parser.add_argument("destination")
    parser.add_argument("--threads", type=int, default=0)
    parser.add_argument("--deflection", type=float, default=0.001)
    args = parser.parse_args()
    if args.threads < 0:
        parser.error("threads must be >= 0")
    print(json.dumps(extract(args.source, args.destination, args.threads, args.deflection)))


if __name__ == "__main__":
    main()
