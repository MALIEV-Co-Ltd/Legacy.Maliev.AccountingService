"""Require actual hosted receipt creation retry cases and raw executable inventory."""
import collections
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

expected = {
    "FreshCreation_NormalRetryStrategyPersistsOneReceiptAndComputedLines": 2,
    "ExistingReceipt_ReconciliationDoesNotInsertOrChangeAnyOwnedState": 1,
    "PreCancellation_DoesNotInsertReceiptOrChangeOwnedCacheAndTables": 1,
    "LineInsertionFailure_RollsBackReceiptAndLeavesCallerContextClean": 1,
    "CancellationAfterReceiptInsertion_RollsBackWithoutReplayingInsert": 1,
    "LostCommitAcknowledgment_FailsClosedAndLaterReconcilesSinglePersistedReceipt": 1,
    "InternalWorkflow_SyntheticProvidersCompleteOnceAndNormalHttpReadExposesLink": 2,
}
root = Path(sys.argv[1])
reports = list(root.rglob("*.trx"))
if len(reports) != 1:
    raise SystemExit("Require one actual receipt creation TRX")
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
trx = ET.parse(reports[0])
actual = collections.Counter()
prefix = "Legacy.Maliev.AccountingService.Tests.ReceiptCreationRetryTests."
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
        raise SystemExit("Unexpected, duplicate, or non-passing receipt creation result: " + name)
    if not (name == identity or name.startswith(identity + "(")):
        raise SystemExit("Result name contradicts its class/method definition")
    executions.add(execution)
    names.add(name)
    used_definitions.add(test_id)
    actual[identity.removeprefix(prefix)] += 1
if used_definitions != set(definitions) or dict(actual) != expected:
    raise SystemExit(f"Receipt creation cardinality mismatch: {dict(actual)}")
counters = trx.find("./t:ResultSummary/t:Counters", ns)
required_counters = {key: 9 for key in ("total", "executed", "passed")}
required_counters.update({key: 0 for key in (
    "failed", "error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
    "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending",
)})
if counters is None or any(key not in counters.attrib or int(counters.get(key)) != value
                           for key, value in required_counters.items()):
    raise SystemExit("Require all 16 standard counters: 9 total/executed/passed and zero in every other state")
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
    "passed": 9, "failed": 0, "skipped": 0, "methods": dict(actual),
    "trxSha256": hashlib.sha256(reports[0].read_bytes()).hexdigest(),
    "rawSha256": next(iter(digests)), "rawCopies": len(raw),
    "fullServiceCoverageAcceptance": False, "actualAuthProducerAcceptance": False,
    "note": "Normal-Program configured-retry PostgreSQL store fault controls and internal workflow with synthetic providers plus real HTTP invoice-readback; actual owned Redis/lock/journal. No live provider, new HTTP creation-authorization, distributed race or deployment guarantee.",
}
(root / "receipt-create-proof.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(proof, indent=2))
