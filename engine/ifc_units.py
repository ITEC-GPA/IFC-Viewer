"""Explicit dimensional conversion. Values are never inferred from names."""
import math
import ifcopenshell.util.unit as uu
import ifcopenshell.util.element as eu


# Symbol -> dimensional exponents (L,M,T,I,temperature,amount,luminous), scale, offset.
L = (1, 0, 0, 0, 0, 0, 0)
A = (2, 0, 0, 0, 0, 0, 0)
V = (3, 0, 0, 0, 0, 0, 0)
M = (0, 1, 0, 0, 0, 0, 0)
T = (0, 0, 1, 0, 0, 0, 0)
K = (0, 0, 0, 0, 1, 0, 0)
ZERO = (0,) * 7
SYMBOLS = {
    "m": (L, 1., 0.), "mm": (L, .001, 0.), "cm": (L, .01, 0.),
    "ft": (L, .3048, 0.), "in": (L, .0254, 0.),
    "m2": (A, 1., 0.), "mm2": (A, 1e-6, 0.), "cm2": (A, 1e-4, 0.),
    "m3": (V, 1., 0.), "mm3": (V, 1e-9, 0.), "L": (V, .001, 0.),
    "kg": (M, 1., 0.), "g": (M, .001, 0.), "t": (M, 1000., 0.),
    "s": (T, 1., 0.), "min": (T, 60., 0.), "h": (T, 3600., 0.),
    "K": (K, 1., 0.), "degC": (K, 1., 273.15),
    "N": ((1, 1, -2, 0, 0, 0, 0), 1., 0.),
    "kN": ((1, 1, -2, 0, 0, 0, 0), 1000., 0.),
    "Pa": ((-1, 1, -2, 0, 0, 0, 0), 1., 0.),
    "kPa": ((-1, 1, -2, 0, 0, 0, 0), 1000., 0.),
    "MPa": ((-1, 1, -2, 0, 0, 0, 0), 1e6, 0.),
    "1": (ZERO, 1., 0.),
}


def unit_transform(unit, active=None):
    """Return dimensions and affine conversion to canonical SI."""
    if unit is None:
        raise ValueError("Unita IFC assente: nessuna conversione implicita.")
    active = set() if active is None else set(active)
    if unit.id() in active:
        raise ValueError("Definizione di unita ciclica.")
    active.add(unit.id())
    if unit.is_a("IfcDerivedUnit"):
        dims, scale = [0] * 7, 1.
        for item in unit.Elements:
            d, s, offset = unit_transform(item.Unit, active)
            if offset:
                raise ValueError("Unita derivata con offset non supportata.")
            dims = [a + b * item.Exponent for a, b in zip(dims, d)]
            scale *= s ** item.Exponent
        return tuple(dims), scale, 0.
    if unit.is_a("IfcSIUnit"):
        dims = tuple(uu.get_si_dimensions(unit.Name))
        scale = uu.get_prefix_multiplier(unit.Prefix)
        if unit.Name in ("SQUARE_METRE", "CUBIC_METRE"):
            scale **= 2 if unit.Name == "SQUARE_METRE" else 3
        if unit.Name == "GRAM":
            scale *= .001
        return dims, scale, 273.15 if unit.Name == "DEGREE_CELSIUS" else 0.
    if unit.is_a("IfcConversionBasedUnit"):
        dims, scale, offset = unit_transform(unit.ConversionFactor.UnitComponent, active)
        scale *= float(unit.ConversionFactor.ValueComponent.wrappedValue)
        offset += float(getattr(unit, "ConversionOffset", 0.) or 0.) * scale
        return dims, scale, offset
    raise ValueError("Unita non convertibile in SI: " + unit.is_a())


def numeric(value):
    if isinstance(value, bool):
        raise ValueError("Un booleano non e una misura.")
    value = float(value)
    if not math.isfinite(value):
        raise ValueError("Misura non finita.")
    return value


def convert(value, source, target):
    sd, ss, so = source
    td, ts, to = target
    if sd != td:
        raise ValueError("Unita dimensionalmente incompatibili.")
    return (numeric(value) * ss + so - to) / ts


def symbol(name, dimensions=None):
    if name == "SI":
        if dimensions is None:
            raise ValueError("Dimensione necessaria per SI.")
        return dimensions, 1., 0.
    if name not in SYMBOLS:
        raise ValueError("Unita destinazione non supportata: " + name)
    return SYMBOLS[name]


def definition(entity, set_name, name, inherit=True):
    data = eu.get_psets(entity, should_inherit=inherit, verbose=True)
    item = data.get(set_name, {}).get(name)
    if not isinstance(item, dict) or "id" not in item:
        raise ValueError("Proprieta/quantita assente: " + set_name + "." + name)
    return entity.file.by_id(item["id"])


def measure(model, prop):
    if prop.is_a("IfcPropertySingleValue"):
        if prop.NominalValue is None:
            raise ValueError("Valore null.")
        value = prop.NominalValue.wrappedValue
        measure_type = prop.NominalValue.is_a()
    elif prop.is_a("IfcPhysicalSimpleQuantity"):
        value, measure_type = prop[3], prop.is_a()
    else:
        raise ValueError("Richiesta proprieta scalare o quantita semplice.")
    unit = uu.get_property_unit(prop, model)
    if unit:
        transform = unit_transform(unit)
        label = uu.get_unit_symbol(unit) if not unit.is_a("IfcDerivedUnit") else str(unit)
    elif measure_type in ("IfcCountMeasure", "IfcQuantityCount", "IfcRatioMeasure",
                           "IfcPositiveRatioMeasure", "IfcNormalisedRatioMeasure"):
        transform, label = SYMBOLS["1"], "1"
    else:
        raise ValueError("Misura senza unita dichiarata: " + measure_type)
    return numeric(value), transform, label


def property_units(model, elements, request):
    result = []
    for entity in elements:
        row = {"step_id": entity.id(), "global_id": entity.GlobalId}
        try:
            prop = definition(entity, request["pset"], request["name"])
            value, source, label = measure(model, prop)
            target = symbol(request.get("unit", "SI"), source[0])
            row.update(value=value, converted=convert(value, source, target),
                       unit=label, target_unit=request.get("unit", "SI"),
                       dimensions=source[0], valid=True)
        except (ValueError, TypeError, AttributeError) as ex:
            row.update(valid=False, error=str(ex))
        result.append(row)
    return {"rows": result}
