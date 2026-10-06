"""Verify exact hosted Invoice/Receipt file-parent cases and raw owned inventory."""
import collections
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

expected = {
    "MissingParent_Source404BeforeMetadataPersistence": 6,
    "DeletedParent_FreshCheckRejectsStaleCacheAndPriorMemo": 4,
    "ExistingParent_FileWriteGrantCreatesLiteralMetadataWithoutParentReadGrant": 4,
    "MissingMetadata_Source400BeforeParentOrFinancialWrite": 4,
    "AnonymousOrLiveDenied_CannotProbeParentOrPersistMetadata": 4,
    "ExistingParent_SequentialReplayPreservesOneMetadataRow": 2,
}
root = Path(sys.argv[1])
reports = list(root.rglob("*.trx"))
if len(reports) != 1:
    raise SystemExit("Require one actual Invoice/Receipt file-parent TRX")
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
trx = ET.parse(reports[0])
actual = collections.Counter()
prefix = "Legacy.Maliev.AccountingService.Tests.InvoiceReceiptFileParentSourceHttpTests."
definitions = {}
definition_rows = trx.findall("./t:TestDefinitions/t:UnitTest", ns)
definition_container = trx.find("./t:TestDefinitions", ns)
if definition_container is None or len(definition_container) != len(definition_rows):
    raise SystemExit("Require actual UnitTest definitions only")
for definition in definition_rows:
    test_id = definition.get("id", "").strip()
    methods = definition.findall("./t:TestMethod", ns)
    if not test_id or len(methods) != 1:
        raise SystemExit("Missing test identity or unique TestMethod definition")
    method = methods[0]
    identity = method.get("className", "") + "." + method.get("name", "")
    name = definition.get("name", "")
    if not identity.startswith(prefix) or identity.removeprefix(prefix) not in expected or not (
            name == identity or name.startswith(identity + "(")):
        raise SystemExit("Unexpected class/method definition: " + identity)
    if test_id in definitions and definitions[test_id] != identity:
        raise SystemExit("Contradictory definitions for reused theory test ID")
    definitions[test_id] = identity
executions = set()
names = set()
used_definitions = set()
result_rows = trx.findall("./t:Results/t:UnitTestResult", ns)
result_container = trx.find("./t:Results", ns)
if result_container is None or len(result_container) != len(result_rows):
    raise SystemExit("Require actual UnitTestResult rows only")
for result in result_rows:
    name = result.get("testName", "")
    execution = result.get("executionId", "").strip()
    test_id = result.get("testId", "").strip()
    identity = definitions.get(test_id)
    if not execution or execution in executions or identity is None or result.get("outcome") != "Passed" or name in names:
        raise SystemExit("Unexpected, duplicate, or non-passing file-parent result: " + name)
    if not (name == identity or name.startswith(identity + "(")):
        raise SystemExit("Result name contradicts its class/method definition")
    executions.add(execution)
    names.add(name)
    used_definitions.add(test_id)
    actual[identity.removeprefix(prefix)] += 1
if used_definitions != set(definitions) or dict(actual) != expected:
    raise SystemExit(f"File-parent cardinality mismatch: {dict(actual)}")
counters = trx.find("./t:ResultSummary/t:Counters", ns)
required_counters = {key: 24 for key in ("total", "executed", "passed")}
required_counters.update({key: 0 for key in (
    "failed", "error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
    "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending",
)})
if counters is None or any(key not in counters.attrib or int(counters.get(key)) != value
                           for key, value in required_counters.items()):
    raise SystemExit("Require all 16 standard counters: 24 total/executed/passed and zero in every other state")
raw = list(root.rglob("coverage.cobertura.xml"))
digests = {hashlib.sha256(path.read_bytes()).hexdigest() for path in raw}
if len(digests) != 1:
    raise SystemExit("Require one unique raw report")
packages = ET.parse(raw[0]).findall("./packages/package")
for assembly in ("Api", "Application", "Data", "Domain"):
    selected = [p for p in packages if p.get("name") == "Legacy.Maliev.AccountingService." + assembly]
    if not selected or not any(p.findall(".//line") for p in selected):
        raise SystemExit(f"Missing owned executable inventory: {assembly}")
proof = {
    "passed": 24, "failed": 0, "skipped": 0, "methods": dict(actual),
    "trxSha256": hashlib.sha256(reports[0].read_bytes()).hexdigest(),
    "rawSha256": next(iter(digests)), "rawCopies": len(raw),
    "fullServiceCoverageAcceptance": False, "actualAuthProducerAcceptance": False,
    "note": "Focused controlled-IAM HTTP file-parent proof only. Physical snapshots cover Payment6, Invoice5, Receipt3 and journal; no live provider, operational data parity or joined IAM acceptance claim.",
}
(root / "invoice-receipt-file-parent-proof.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(proof, indent=2))
