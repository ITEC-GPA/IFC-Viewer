"""Independent verification of the CSV emitted by the native Grasshopper test."""
import csv
from pathlib import Path

root = Path(__file__).resolve().parents[1]
output = Path((root / "test-output" / "last-tool-output.txt").read_text())
with (output / "abaco.csv").open(encoding="utf-8-sig", newline="") as stream:
    rows = list(csv.DictReader(stream, delimiter=";"))
assert len(rows) == 2
assert rows[0]["IFC.Name"] == "'=HYPERLINK(\"x\"); città\nseconda riga"
assert float(rows[0]["IFC.Property.Progetto.Costo"]) == 0.0
assert rows[1]["IFC.Property.Progetto.Costo"] == ""
assert rows[0]["missing"] == rows[1]["missing"] == ""
assert "caffè" in rows[1]["IFC.Name"]
print("PASS: Python CSV parser round-trip preserves delimiters, quotes, newlines, Unicode, zero and absence")
