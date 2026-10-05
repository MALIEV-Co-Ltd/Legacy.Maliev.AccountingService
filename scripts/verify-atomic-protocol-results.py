"""Verify prospective executions without treating historical or source coverage as acceptance."""
import collections
import hashlib
import json
import pathlib
import sys
import xml.etree.ElementTree as ET

root = pathlib.Path(sys.argv[1])
manifest_path = pathlib.Path("tools/CommerceAtomicProtocol.Tests/public-graph.json")
manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
reports = list(root.rglob("*.trx"))
if len(reports) != 1:
    raise SystemExit("Expected one prospective TRX; build failure is not contract RED")
document = ET.parse(reports[0])
counters = document.find(".//{*}Counters")
if counters is None:
    raise SystemExit("Missing prospective execution counters")
counts = {key: int(counters.attrib[key]) for key in ("total", "executed", "passed", "failed", "notExecuted")}
declared = {entry["name"]: entry["executions"] for entry in manifest["expectedTests"]}
expected = sum(declared.values())
results = document.findall(".//{*}UnitTestResult")
actual = collections.Counter(node.attrib["testName"] for node in results)
outcomes = collections.Counter(node.get("outcome") for node in results)
if set(outcomes) - {"Passed", "Failed", "NotExecuted"} or any(
    outcomes[outcome] != counts[counter]
    for outcome, counter in (("Passed", "passed"), ("Failed", "failed"), ("NotExecuted", "notExecuted"))
):
    raise SystemExit("Individual prospective outcomes contradict execution counters")
if actual != declared or expected != manifest["expectedCases"]:
    raise SystemExit("Actual prospective names/cardinality differ from the declared contract")
if counts["total"] != expected or counts["executed"] != expected or counts["notExecuted"] != 0 or counts["passed"] + counts["failed"] != expected:
    raise SystemExit("All prospective cases must actually execute without skips")
coverage = list(root.rglob("coverage.cobertura.xml"))
digests = {hashlib.sha256(path.read_bytes()).hexdigest() for path in coverage}
if len(digests) != 1:
    raise SystemExit("Expected one unique unfiltered prospective raw report")
raw = ET.parse(coverage[0]).getroot()
if raw.tag != "coverage" or not raw.findall("./packages/package/classes/class/lines/line"):
    raise SystemExit("Prospective raw evidence lacks executable source lines")
proof = {"identity": manifest["identity"], "counters": counts, "actualTests": dict(actual),
         "rawSha256": next(iter(digests)), "exclusions": [],
         "prospectiveTestsPassed": counts["passed"] == expected,
         "serviceCoverageAcceptance": False, "historicalGraphAcceptance": False,
         "note": "Test exit is not masked. Source full-suite coverage requires its separate gate; parent/items and saga atomicity are not claimed."}
(root / "atomic-execution-proof.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(proof))
raise SystemExit(0 if proof["prospectiveTestsPassed"] else 1)
