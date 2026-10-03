"""Regenerate public/data/great-lakes.json from Natural Earth 1:50m lakes (public domain).
Run by hand when the outlines need changing; the output is committed. Not run at deploy."""
import datetime
import json
import pathlib
import urllib.request

SOURCE = "https://raw.githubusercontent.com/nvkelso/natural-earth-vector/v5.1.2/geojson/ne_50m_lakes.geojson"
BOX = (-97.5, 36.5, -74.5, 50.5)  # lon/lat window around the hero map; lakes must lie wholly inside it
OUT = pathlib.Path(__file__).resolve().parent.parent / "public" / "data"


def inside(ring):
    return all(BOX[0] < x < BOX[2] and BOX[1] < y < BOX[3] for x, y in ring)


def main():
    with urllib.request.urlopen(SOURCE, timeout=60) as response:
        source = json.load(response)
    lakes = []
    for feature in source["features"]:
        geometry, name = feature["geometry"], feature["properties"].get("name") or ""
        polygons = geometry["coordinates"] if geometry["type"] == "MultiPolygon" else [geometry["coordinates"]]
        rings = []
        for polygon in polygons:
            outer = [[round(x, 3), round(y, 3)] for x, y in polygon[0]]
            if len(outer) >= 4 and inside(outer):
                if outer[0] != outer[-1]:
                    outer.append(outer[0])
                rings.append(outer)
        if rings:
            lakes.append({"name": name, "rings": rings})
    OUT.mkdir(parents=True, exist_ok=True)
    (OUT / "great-lakes.json").write_text(json.dumps({"lakes": lakes}, separators=(",", ":")), encoding="utf-8")
    (OUT / "provenance.json").write_text(json.dumps({
        "file": "great-lakes.json", "source": "Natural Earth 1:50m lakes v5.1.2", "url": SOURCE,
        "licence": "Public domain (Natural Earth terms of use)", "window": BOX,
        "generated": datetime.date.today().isoformat(), "generator": "tools/build_lakes.py"}, indent=2) + "\n", encoding="utf-8")
    print(f"{len(lakes)} lakes:", ", ".join(lake["name"] for lake in lakes))


if __name__ == "__main__":
    main()
