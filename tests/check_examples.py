"""Verify the exports created by the saved/reopened Grasshopper examples."""
import csv
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUTPUT = Path((ROOT / "test-output/last-examples-output.txt").read_text(encoding="utf-8-sig"))
EXPECTED = json.loads((ROOT / "examples/scenari.json").read_text(encoding="utf-8-sig"))
FIRE = "IFC.Property.Pset_WallCommon.FireRating"
CODE = "IFC.Property.Progetto.Codice"


def rows(number):
    with (OUTPUT / f"esempio_{number:02d}.csv").open(encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream, delimiter=";"))


schedule = rows(1)
assert len(schedule) == 3
assert sorted(row[FIRE] for row in schedule) == ["REI 120", "REI 120", "REI 60"]
assert all(row["IFC.Class"] == "IfcWall" for row in schedule)
quality = rows(3)
assert len(quality) == 1
assert quality[0]["IFC.GlobalId"] == EXPECTED["missing_code_global_id"]
assert quality[0][CODE] == "" and quality[0][FIRE] == "REI 120"
changed = rows(4)
assert {row["IFC.GlobalId"] for row in changed} == set(EXPECTED["revision"]["changed"])
assert next(row for row in changed if row["IFC.Class"] == "IfcWall")[FIRE] == "REI 90"
print("PASS: CSV degli esempi 01, 03, 04: righe, selezioni, celle mancanti e valori effettivi.")
