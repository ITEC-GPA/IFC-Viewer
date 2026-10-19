"""Apply explicit edits to an archived IFC snapshot and publish a new IFC atomically."""
import argparse
from collections import Counter
import hashlib
import json
import math
import os
from pathlib import Path
import re
import tempfile
import zipfile

import ifcopenshell
import ifcopenshell.api
import ifcopenshell.util.element as eu
import ifcopenshell.util.pset as pu
import ifcopenshell.validate

ATTRIBUTES = {"Name", "Description", "Tag"}
TEXT_TYPES = {"IfcLabel", "IfcText", "IfcIdentifier"}
NUMBER_TYPES = {"IfcReal", "IfcLengthMeasure", "IfcPositiveLengthMeasure", "IfcNonNegativeLengthMeasure",
                "IfcAreaMeasure", "IfcVolumeMeasure", "IfcMassMeasure", "IfcCountMeasure",
                "IfcRatioMeasure", "IfcPositiveRatioMeasure", "IfcNormalisedRatioMeasure",
                "IfcThermalTransmittanceMeasure", "IfcThermodynamicTemperatureMeasure",
                "IfcPressureMeasure", "IfcTimeMeasure", "IfcMonetaryMeasure"}
VALUE_TYPES = TEXT_TYPES | NUMBER_TYPES | {"IfcInteger", "IfcBoolean", "IfcLogical"}


def scalar(model, text, value_type):
    if value_type not in VALUE_TYPES:
        raise ValueError("Tipo scalare non supportato: " + value_type)
    if not isinstance(text, str):
        raise ValueError("Il valore deve essere una stringa; usare il tipo per convertirlo.")
    if value_type in TEXT_TYPES:
        if value_type in {"IfcLabel", "IfcIdentifier"} and len(text) > 255:
            raise ValueError("IfcLabel/IfcIdentifier: massimo 255 caratteri.")
        value = text
    elif value_type in {"IfcBoolean", "IfcLogical"}:
        choices = {"true": True, "false": False}
        if value_type == "IfcLogical":
            choices["unknown"] = "UNKNOWN"
        if text.strip().lower() not in choices:
            raise ValueError("Valore booleano non valido: usare true/false (unknown solo per IfcLogical).")
        value = choices[text.strip().lower()]
    elif value_type == "IfcInteger":
        if not re.fullmatch(r"[+-]?\d+", text.strip()):
            raise ValueError("Intero non valido: " + text)
        value = int(text)
    else:
        value = float(text)
        if not math.isfinite(value):
            raise ValueError("Valore numerico non finito.")
        if value_type in {"IfcPositiveLengthMeasure", "IfcPositiveRatioMeasure"} and value <= 0:
            raise ValueError("La misura deve essere positiva.")
        if value_type == "IfcNonNegativeLengthMeasure" and value < 0:
            raise ValueError("La misura non puo essere negativa.")
        if value_type == "IfcNormalisedRatioMeasure" and not 0 <= value <= 1:
            raise ValueError("Il rapporto normalizzato deve essere tra 0 e 1.")
    return model.create_entity(value_type, value)


def direct_pset(entity, name):
    sets = list(getattr(entity, "HasPropertySets", ()) or ())
    for rel in getattr(entity, "IsDefinedBy", ()) or ():
        if rel.is_a("IfcRelDefinesByProperties"):
            definition = rel.RelatingPropertyDefinition
            sets.extend(definition if isinstance(definition, tuple) else [definition])
    matches = [p for p in sets if p.Name == name]
    if len(matches) > 1:
        raise ValueError("Property Set ambiguo: " + name)
    if matches and not matches[0].is_a("IfcPropertySet"):
        raise ValueError("Le quantita Qto non sono Property Set modificabili: " + name)
    return matches[0] if matches else None


def prop_in(pset, name):
    matches = [p for p in ((pset.HasProperties or ()) if pset else ()) if p.Name == name]
    if len(matches) > 1:
        raise ValueError("Proprieta ambigua: " + name)
    prop = matches[0] if matches else None
    if prop and not prop.is_a("IfcPropertySingleValue"):
        raise ValueError("Solo IfcPropertySingleValue modificabili; proprieta complessa: " + name)
    return prop


def template_type(model, pset, name):
    template = pu.get_template(model.schema_identifier).get_by_name(pset)
    for prop in (template.HasPropertyTemplates if template else ()):
        if prop.Name == name:
            if prop.TemplateType != "P_SINGLEVALUE":
                raise ValueError("Il template richiede una proprieta non scalare: " + name)
            return prop.PrimaryMeasureType
    return None


def prepare(model, metadata, changes):
    if not isinstance(changes, list) or not changes:
        raise ValueError("Nessuna modifica richiesta.")
    records = {p["step_id"]: p for p in metadata["products"]}
    seen, plans = {}, []
    for change in changes:
        record = records.get(change.get("step_id"))
        if not record or record["global_id"] != change.get("global_id"):
            raise ValueError("Identita elemento non coerente con il modello incorporato.")
        entity = model.by_id(record["step_id"])
        if entity.GlobalId != record["global_id"] or entity.is_a() != record["ifc_class"]:
            raise ValueError("Identita IFC sorgente non coerente.")
        scope = change.get("scope", "instance")
        if scope not in ("instance", "type"):
            raise ValueError("Scope deve essere instance oppure type.")
        target = eu.get_type(entity) if scope == "type" else entity
        if not target:
            raise ValueError("L'elemento non ha un tipo associato: " + entity.GlobalId)
        kind, name, value = change.get("kind"), change.get("name"), change.get("value")
        plan = {"target": target, "change": change, "before": None}
        if kind == "attribute":
            if name not in ATTRIBUTES or not hasattr(target, name):
                raise ValueError("Attributo non modificabile: " + str(name) + " su " + target.is_a())
            if value is not None and not isinstance(value, str):
                raise ValueError("Gli attributi richiedono testo o null.")
            if name != "Description" and value is not None and len(value) > 255:
                raise ValueError("Attributo oltre 255 caratteri.")
            plan["before"] = getattr(target, name)
            key = (target.id(), kind, name)
        elif kind == "property":
            pset_name = change.get("pset")
            if not isinstance(pset_name, str) or not pset_name.strip() or not isinstance(name, str) or not name.strip():
                raise ValueError("Nome Property Set e proprieta obbligatori.")
            if len(pset_name) > 255 or len(name) > 255 or pset_name.startswith("Qto_"):
                raise ValueError("Nome non valido o Quantity Set non modificabile.")
            pset = direct_pset(target, pset_name)
            prop = prop_in(pset, name)
            if prop is None and scope == "instance":
                type_object = eu.get_type(target)
                if type_object:
                    prop = prop_in(direct_pset(type_object, pset_name), name)
            old_type = prop.NominalValue.is_a() if prop and prop.NominalValue else None
            standard_type = template_type(model, pset_name, name)
            requested = change.get("value_type", "Auto")
            actual_type = (old_type or standard_type or "IfcLabel") if requested == "Auto" else requested
            if requested != "Auto" and (old_type or standard_type) and requested != (old_type or standard_type):
                raise ValueError("Non cambiare il tipo IFC della proprieta: atteso " + (old_type or standard_type))
            plan["nominal"] = scalar(model, value, actual_type) if value is not None else None
            plan["unit"] = prop.Unit if prop else None
            plan["before"] = prop.NominalValue.wrappedValue if prop and prop.NominalValue else None
            key = (target.id(), kind, pset_name, name)
        else:
            raise ValueError("Operazione non supportata: " + str(kind))
        signature = json.dumps({k: v for k, v in change.items() if k not in ("step_id", "global_id", "scope")}, sort_keys=True)
        if key in seen:
            if seen[key] != signature:
                raise ValueError("Modifiche in conflitto sullo stesso campo/tipo: " + str(key))
            continue
        seen[key] = signature
        plans.append(plan)
    return plans


def apply(model, plans):
    report = []
    for plan in plans:
        target, change = plan["target"], plan["change"]
        if change["kind"] == "attribute":
            # These three descriptive attributes have no PredefinedType side effects.
            setattr(target, change["name"], change["value"])
        else:
            pset = direct_pset(target, change["pset"])
            if pset and len(eu.get_elements_by_pset(pset)) > 1:
                pset = ifcopenshell.api.run("pset.unshare_pset", model, products=[target], pset=pset)[0]
            if pset is None:
                pset = ifcopenshell.api.run("pset.add_pset", model, product=target, name=change["pset"])
            prop = prop_in(pset, change["name"])
            # Copy-on-write also isolates properties shared by different psets.
            new_prop = eu.copy(model, prop) if prop else model.create_entity("IfcPropertySingleValue", Name=change["name"], Unit=plan["unit"])
            new_prop.NominalValue = plan["nominal"]
            pset.HasProperties = tuple(new_prop if p == prop else p for p in (pset.HasProperties or ())) if prop else tuple(pset.HasProperties or ()) + (new_prop,)
            if prop and model.get_total_inverses(prop) == 0:
                model.remove(prop)
        ifcopenshell.api.run("owner.update_owner_history", model, element=target)
        report.append({"target_global_id": target.GlobalId, "target_class": target.is_a(),
                       "scope": change.get("scope", "instance"), "field": (change.get("pset", "") + "." + change["name"]).lstrip("."),
                       "before": plan["before"], "after": change["value"],
                       "type_occurrences": len(eu.get_types(target)) if target.is_a("IfcTypeObject") else 0})
    return report


def validation_errors(model):
    logger = ifcopenshell.validate.json_logger()
    ifcopenshell.validate.validate(model, logger)
    return Counter((s["message"], str(s.get("attribute", ""))) for s in logger.statements if s.get("level") == "error")


def write_ifc(archive_path, request, destination, overwrite=False):
    archive_path, destination = Path(archive_path).resolve(), Path(destination).resolve()
    if destination.suffix.lower() != ".ifc":
        raise ValueError("La destinazione deve terminare con .ifc.")
    with zipfile.ZipFile(archive_path) as archive, tempfile.TemporaryDirectory(prefix="meerkat-write-") as temp:
        metadata = json.loads(archive.read("model.json"))
        original_path = Path(metadata["source_path"]).resolve()
        if destination in (archive_path, original_path) or (destination.exists() and original_path.exists() and os.path.samefile(destination, original_path)):
            raise ValueError("Esportare in un nuovo percorso: il file originale e protetto.")
        if destination.exists() and not overwrite:
            raise FileExistsError("File esistente: cambiare percorso oppure attivare Overwrite.")
        originals = [n for n in archive.namelist() if n in ("original/source.ifc", "original/source.ifczip")]
        if len(originals) != 1:
            raise ValueError("Archivio senza sorgente IFC univoca.")
        content = archive.read(originals[0])
        digest = hashlib.sha256(content).hexdigest()
        if digest != metadata["source_sha256"] or digest != request.get("source_sha256"):
            raise ValueError("Hash sorgente non coerente; ricaricare il modello.")
        snapshot = Path(temp) / Path(originals[0]).name
        snapshot.write_bytes(content)
        model = ifcopenshell.open(str(snapshot))
        previous_errors = validation_errors(model)
        identities = {e.id(): (e.is_a(), e.GlobalId) for e in model.by_type("IfcRoot")}
        plans = prepare(model, metadata, request.get("changes"))
        changes = apply(model, plans)
        for step_id, identity in identities.items():
            current = model.by_id(step_id)
            if (current.is_a(), current.GlobalId) != identity:
                raise ValueError("Identita IFC alterata durante la modifica.")
        destination.parent.mkdir(parents=True, exist_ok=True)
        fd, staging = tempfile.mkstemp(prefix=".meerkat-", suffix=".ifc", dir=str(destination.parent))
        os.close(fd)
        try:
            model.write(staging)
            reopened = ifcopenshell.open(staging)
            new_errors = validation_errors(reopened) - previous_errors
            if new_errors:
                raise ValueError("Nuovi errori di schema IFC: " + str(next(iter(new_errors))))
            if overwrite:
                os.replace(staging, destination)
            else:
                # Exclusive publication: cannot silently replace a concurrently created file.
                os.link(staging, destination)
        finally:
            if os.path.exists(staging):
                os.unlink(staging)
    return {"file": str(destination), "schema": model.schema, "source_sha256": digest,
            "output_sha256": hashlib.sha256(destination.read_bytes()).hexdigest(),
            "preexisting_schema_errors": sum(previous_errors.values()), "changes": changes}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive")
    parser.add_argument("request")
    parser.add_argument("destination")
    parser.add_argument("--overwrite", action="store_true")
    args = parser.parse_args()
    request = json.loads(Path(args.request).read_text(encoding="utf-8-sig"))
    print(json.dumps(write_ifc(args.archive, request, args.destination, args.overwrite), ensure_ascii=False))


if __name__ == "__main__":
    main()
