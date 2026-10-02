"""Add a dedicated numeric/boolean tutorial without changing revisions A/B."""
import sys
from pathlib import Path

import ifcopenshell
import ifcopenshell.api

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "engine"))
from ifc_reader import extract


def main():
    model = ifcopenshell.open(str(ROOT / "examples/tutorial_A.ifc"))
    walls = sorted(model.by_type("IfcWall"), key=lambda wall: wall.Name)
    assert len(walls) == 3
    for wall, index, enabled in zip(walls, (2, 10, 1), (True, True, False)):
        pset = ifcopenshell.api.run("pset.add_pset", model, product=wall, name="Tutorial")
        ifcopenshell.api.run("pset.edit_pset", model, pset=pset,
                            properties={"Indice": index, "Selezionabile": enabled})
    source = ROOT / "examples/tutorial_numeri.ifc"
    model.write(str(source))
    result = extract(source, source.with_suffix(".ifcdata.zip"), threads=1)
    assert result["products"] == 11 and result["meshes"] == 6
    print("PASS: IFC numerico: tre pareti con Indice 2, 10, 1 e booleani true, true, false.")


if __name__ == "__main__":
    main()
