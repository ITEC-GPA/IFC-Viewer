"""Quantity requests use an explicit quantity class and measurement method."""
import ifcopenshell.api
import ifcopenshell.util.element as eu
import ifcopenshell.util.pset as pu
import ifcopenshell.util.unit as uu
from ifc_units import numeric, symbol, unit_transform, convert, ZERO

KINDS = {
    "Length": ("IfcQuantityLength", "LENGTHUNIT", "Q_LENGTH"),
    "Area": ("IfcQuantityArea", "AREAUNIT", "Q_AREA"),
    "Volume": ("IfcQuantityVolume", "VOLUMEUNIT", "Q_VOLUME"),
    "Mass": ("IfcQuantityWeight", "MASSUNIT", "Q_WEIGHT"),
    "Time": ("IfcQuantityTime", "TIMEUNIT", "Q_TIME"),
    "Count": ("IfcQuantityCount", None, "Q_COUNT"),
}


def direct_qto(entity, name):
    sets = list(getattr(entity, "HasPropertySets", ()) or ())
    for rel in getattr(entity, "IsDefinedBy", ()) or ():
        if rel.is_a("IfcRelDefinesByProperties"):
            sets.append(rel.RelatingPropertyDefinition)
    matches = [s for s in sets if s.Name == name]
    if len(matches) > 1 or (matches and not matches[0].is_a("IfcElementQuantity")):
        raise ValueError("Quantity Set ambiguo o nome occupato da un Pset: " + name)
    return matches[0] if matches else None


def prepare_quantity(model, target, change):
    if change.get("scope", "instance") != "instance":
        raise ValueError("Le quantita sono supportate sulle istanze.")
    set_name, name = change.get("pset"), change.get("name")
    method = change.get("method")
    if not all(isinstance(v, str) and v.strip() for v in (set_name, name, method)):
        raise ValueError("Nome Qto, quantita e metodo di misura obbligatori.")
    if max(len(set_name), len(name), len(method)) > 255:
        raise ValueError("Nomi/metodo: massimo 255 caratteri.")
    kind = change.get("quantity_type")
    if kind not in KINDS:
        raise ValueError("Tipo quantita: Length, Area, Volume, Mass, Time, Count.")
    cls, unit_type, template_type = KINDS[kind]
    template = pu.get_template(model.schema_identifier).get_by_name(set_name)
    if template:
        templates = [p for p in template.HasPropertyTemplates if p.Name == name]
        if not templates or templates[0].TemplateType != template_type:
            raise ValueError("Quantita/tipo non coerente con il template standard.")
    qto = direct_qto(target, set_name)
    quantities = [q for q in qto.Quantities if q.Name == name] if qto else []
    if len(quantities) > 1 or (quantities and quantities[0].is_a() != cls):
        raise ValueError("Quantita ambigua o cambio di tipo non consentito.")
    old = quantities[0] if quantities else None
    # MethodOfMeasurement applies to the entire set, not just this quantity.
    if qto and qto.MethodOfMeasurement and qto.MethodOfMeasurement != method:
        raise ValueError("Metodo diverso da quello del Quantity Set: usare un nuovo set.")
    unit = old.Unit if old else None
    target_unit = unit or (uu.get_project_unit(model, unit_type) if unit_type else None)
    transform = unit_transform(target_unit) if unit_type else (ZERO, 1., 0.)
    input_unit = change.get("input_unit", "IFC")
    value = numeric(change.get("value"))
    if input_unit != "IFC":
        value = convert(value, symbol(input_unit, transform[0]), transform)
    if value < 0 or (kind == "Count" and value != int(value)):
        raise ValueError("Quantita negativa o conteggio non intero.")
    if cls == "IfcQuantityCount" and model.create_entity("IfcCountMeasure").attribute_type(0) == "INT":
        value = int(value)
    return dict(cls=cls, value=value, unit=unit, before=old[3] if old else None, method=method)


def apply_quantity(model, target, change, plan):
    qto = direct_qto(target, change["pset"])
    if qto and len(eu.get_elements_by_pset(qto)) > 1:
        qto = ifcopenshell.api.run("pset.unshare_pset", model, products=[target], pset=qto)[0]
    if qto is None:
        qto = ifcopenshell.api.run("pset.add_qto", model, product=target, name=change["pset"])
    old = next((q for q in qto.Quantities or () if q.Name == change["name"]), None)
    new = eu.copy(model, old) if old else model.create_entity(plan["cls"], Name=change["name"], Unit=plan["unit"])
    new[3] = plan["value"]
    qto.Quantities = tuple(new if q == old else q for q in qto.Quantities or ()) if old else tuple(qto.Quantities or ()) + (new,)
    qto.MethodOfMeasurement = plan["method"]
    if old and model.get_total_inverses(old) == 0:
        model.remove(old)
