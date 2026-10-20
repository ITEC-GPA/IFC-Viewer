"""Read/query tools operate on the hashed IFC embedded in a Meerkat archive."""
import argparse
from contextlib import contextmanager
import hashlib
import json
from pathlib import Path
import tempfile
import zipfile

import ifcopenshell
from ifc_units import property_units


@contextmanager
def snapshot(archive_path, expected_hash):
    with zipfile.ZipFile(archive_path) as archive, tempfile.TemporaryDirectory(prefix="meerkat-query-") as temp:
        metadata = json.loads(archive.read("model.json"))
        names = [n for n in archive.namelist() if n in ("original/source.ifc", "original/source.ifczip")]
        if len(names) != 1:
            raise ValueError("Archivio senza sorgente univoca.")
        content = archive.read(names[0])
        if hashlib.sha256(content).hexdigest() != metadata["source_sha256"] or expected_hash != metadata["source_sha256"]:
            raise ValueError("Hash sorgente non coerente.")
        path = Path(temp) / Path(names[0]).name
        path.write_bytes(content)
        yield ifcopenshell.open(str(path)), metadata


def select(model, refs):
    result = []
    for ref in refs:
        entity = model.by_id(ref["step_id"])
        if getattr(entity, "GlobalId", None) != ref["global_id"]:
            raise ValueError("Identita IFC non coerente.")
        result.append(entity)
    return result


def execute(archive, request):
    with snapshot(archive, request["source_sha256"]) as (model, metadata):
        elements = select(model, request.get("elements", []))
        if request["operation"] == "units":
            return property_units(model, elements, request)
        raise ValueError("Operazione sconosciuta: " + request["operation"])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive")
    parser.add_argument("request")
    args = parser.parse_args()
    request = json.loads(Path(args.request).read_text(encoding="utf-8-sig"))
    print(json.dumps(execute(args.archive, request), ensure_ascii=False, allow_nan=False))


if __name__ == "__main__":
    main()
