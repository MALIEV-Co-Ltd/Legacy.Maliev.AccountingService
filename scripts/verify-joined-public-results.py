"""Require six actual executions and retained unfiltered raw diagnostic coverage."""
import hashlib
import json
import pathlib
import sys
import xml.etree.ElementTree as ET

root = pathlib.Path(sys.argv[1])
reports = list(root.rglob("*.trx"))
if len(reports) != 1:
    raise SystemExit("Expected exactly one joined TRX; build/startup is not contract RED")
document = ET.parse(reports[0])
counters = next((node for node in document.iter() if node.tag.endswith("}Counters")), None)
if counters is None:
    raise SystemExit("Missing actual joined counters")
counts = {name: int(counters.attrib[name]) for name in ("total", "executed", "passed", "failed", "notExecuted")}
if counts["total"] != 6 or counts["executed"] != 6 or counts["notExecuted"] != 0 or counts["passed"] + counts["failed"] != 6:
    raise SystemExit("Joined proof requires all six cases actually executed")
coverage = list(root.rglob("coverage.cobertura.xml"))
digests = {hashlib.sha256(path.read_bytes()).hexdigest() for path in coverage}
if len(digests) != 1:
    raise SystemExit("Expected one unique generated-inclusive raw joined coverage report")
raw = ET.parse(coverage[0]).getroot()
if raw.tag != "coverage" or not raw.findall("./packages/package/classes/class/lines/line"):
    raise SystemExit("Raw joined coverage must contain executable class lines")
proof = {"counters": counts, "rawSha256": next(iter(digests)), "exclusions": [],
         "desiredContractAccepted": False, "serviceCoverageAcceptance": False,
         "note": "Does not mask the test exit. Inspect actual responses and stored fields to classify failures."}
(root / "joined-execution-proof.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(proof))
