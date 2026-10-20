"""Material inspection, explicit type assignment and dependency-aware extraction."""
import hashlib
import os
from pathlib import Path
import tempfile

import ifcopenshell
import ifcopenshell.api
import ifcopenshell.util.element as eu
import ifcopenshell.util.type as tu
import ifcopenshell.util.unit as uu
from ifc_reader import info, reference


def materials(model, elements):
    scale = uu.calculate_unit_scale(model)
    branches = []
    for entity in elements:
        direct = eu.get_material(entity, should_inherit=False)
        material = direct or eu.get_material(entity)
        row = dict(element=reference(entity), scope="instance" if direct else "type" if material else "none",
                   assignment=info(material), items=[])
        base = material
        if base and base.is_a("IfcMaterialLayerSetUsage"):
            base = base.ForLayerSet
        elif base and base.is_a("IfcMaterialProfileSetUsage"):
            base = base.ForProfileSet
        items = (base.MaterialLayers if base and base.is_a("IfcMaterialLayerSet") else
                 base.MaterialProfiles if base and base.is_a("IfcMaterialProfileSet") else
                 base.MaterialConstituents if base and base.is_a("IfcMaterialConstituentSet") else
                 base.Materials if base and base.is_a("IfcMaterialList") else [base] if base else [])
        for part in items or ():
            mat = part if part.is_a("IfcMaterial") else getattr(part, "Material", None)
            profile = getattr(part, "Profile", None)
            row["items"].append(dict(name=getattr(mat, "Name", None), material=info(mat), assignment=info(part),
                                     thickness_metres=float(part.LayerThickness) * scale if part.is_a("IfcMaterialLayer") else None,
                                     profile=info(profile),
                                     profile_graph=[info(e) for e in model.traverse(profile)] if profile else [],
                                     properties=[info(e) for p in getattr(mat, "HasProperties", ()) for e in model.traverse(p)] if mat else []))
        branches.append(row)
    return dict(rows=branches, length_unit_to_metres=scale)


def prepare_type(model, target, change):
    if change.get("scope", "instance") != "instance" or not target.is_a("IfcObject"):
        raise ValueError("Assegnazione tipo solo a istanze IfcObject.")
    gid = change.get("type_global_id")
    candidates = [t for t in model.by_type("IfcTypeObject") if t.GlobalId == gid] if gid else [eu.get_type(target)]
    if len(candidates) != 1 or candidates[0] is None:
        raise ValueError("Tipo destinazione assente/ambiguo. Senza GlobalId e richiesto un tipo corrente.")
    new_type = candidates[0]
    if new_type.is_a() not in tu.get_applicable_types(target.is_a(), model.schema):
        raise ValueError("Tipo %s incompatibile con %s." % (new_type.is_a(), target.is_a()))
    duplicate = change.get("duplicate", False)
    if not isinstance(duplicate, bool) or not isinstance(change.get("map_geometry", False), bool):
        raise ValueError("Duplicate e Map geometry devono essere booleani.")
    name = change.get("type_name", "")
    if duplicate and (not isinstance(name, str) or not name.strip() or len(name) > 255):
        raise ValueError("Nome del nuovo tipo obbligatorio, massimo 255 caratteri.")
    if getattr(new_type, "RepresentationMaps", None) and not change.get("map_geometry", False):
        raise ValueError("Il tipo ha geometrie mappate: attivare Apply type geometry per accettarne la sostituzione.")
    return dict(source=new_type, duplicate=duplicate, name=name, before=reference(eu.get_type(target)),
                before_properties=eu.get_psets(target))


def apply_type(model, target, change, plan, copies):
    new_type = plan["source"]
    if plan["duplicate"]:
        key = (new_type.id(), plan["name"])
        if key not in copies:
            copied = eu.copy(model, new_type)
            copied.Name = plan["name"]
            copied.HasPropertySets = [eu.copy_deep(model, p) for p in new_type.HasPropertySets or ()] or None
            if getattr(new_type, "RepresentationMaps", None):
                copied.RepresentationMaps = [eu.copy_deep(model, r, exclude=["IfcGeometricRepresentationContext"]) for r in new_type.RepresentationMaps]
            for rel in getattr(new_type, "HasAssociations", ()):
                cloned = eu.copy(model, rel)
                cloned.RelatedObjects = [copied]
            copies[key] = copied
        new_type = copies[key]
    ifcopenshell.api.run("type.assign_type", model, related_objects=[target], relating_type=new_type, should_map_representations=True)
    return dict(target_global_id=target.GlobalId, target_class=target.is_a(), scope="instance", field="Type",
                before=plan["before"], after=reference(new_type), before_properties=plan["before_properties"],
                after_properties=eu.get_psets(target), type_occurrences=len(eu.get_types(new_type)),
                warning="Assegnazione tipo/materiali; Qto e modello analitico non ricalcolati.")


def extract_model(model, metadata, selected, destination, overwrite=False, descendants=True, archive_path=None):
    from ifc_writer import validation_errors
    if not selected:
        raise ValueError("Selezionare almeno un prodotto.")
    if any(not e.is_a("IfcProduct") for e in selected):
        raise ValueError("Extract Model richiede prodotti, non tipi o gruppi.")
    destination = Path(destination).resolve()
    original = Path(metadata["source_path"]).resolve()
    if destination.suffix.lower() != ".ifc" or destination == original or (archive_path and destination == Path(archive_path).resolve()):
        raise ValueError("Destinazione .ifc nuova, diversa da originale e archivio.")
    if destination.exists() and original.exists() and os.path.samefile(destination, original):
        raise ValueError("Il file originale e protetto.")
    if destination.exists() and not overwrite:
        raise FileExistsError("Destinazione esistente: attivare Overwrite.")
    baseline = validation_errors(model)
    keep = set(selected)
    if descendants:
        for entity in selected:
            keep.update(eu.get_decomposition(entity))
    queue = list(keep)
    while queue:
        entity = queue.pop()
        # Include spatial/assembly/host ancestry and all opening cutters on kept hosts.
        dependencies = [eu.get_parent(entity)]
        dependencies.extend(r.RelatedOpeningElement for r in getattr(entity, "HasOpenings", ()))
        for other in dependencies:
            if other and other not in keep:
                keep.add(other); queue.append(other)
    kept_ids = {e.id() for e in keep}
    kept_products = [e for e in keep if e.is_a("IfcProduct")]
    identities = {e.id(): (e.is_a(), e.GlobalId) for e in kept_products}
    deleted = []
    for step_id in [e.id() for e in model.by_type("IfcProduct")]:
        if step_id in kept_ids:
            continue
        try:
            current = model.by_id(step_id)
        except RuntimeError:
            continue  # removal of a parent may already remove an unkept feature
        deleted.append(reference(current))
        ifcopenshell.api.run("root.remove_product", model, product=current)
    for entity in list(model.by_type("IfcTypeProduct")):
        if not eu.get_types(entity):
            ifcopenshell.api.run("root.remove_product", model, product=entity)
    for step_id, identity in identities.items():
        entity = model.by_id(step_id)
        if (entity.is_a(), entity.GlobalId) != identity:
            raise ValueError("Dipendenza selezionata alterata durante l'estrazione.")
    destination.parent.mkdir(parents=True, exist_ok=True)
    fd, staging = tempfile.mkstemp(prefix=".meerkat-extract-", suffix=".ifc", dir=str(destination.parent)); os.close(fd)
    try:
        model.write(staging)
        reopened = ifcopenshell.open(staging)
        errors = validation_errors(reopened) - baseline
        if errors:
            raise ValueError("Nuovi errori di schema nell'estrazione: " + str(next(iter(errors))))
        if overwrite:
            os.replace(staging, destination)
        elif os.name == "nt":
            os.rename(staging, destination)
        else:
            os.link(staging, destination)
    finally:
        if os.path.exists(staging):
            os.unlink(staging)
    return dict(file=str(destination), selected=len(selected), retained_products=len(kept_products),
                dependencies=[reference(e) for e in kept_products if e not in selected],
                removed_products=len(deleted), output_sha256=hashlib.sha256(destination.read_bytes()).hexdigest(),
                warning="Relazioni verso prodotti esclusi rimosse. Contesti e metadati non geometrici possono essere conservati.")
