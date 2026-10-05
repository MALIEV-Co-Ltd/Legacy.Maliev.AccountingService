"""Require actual hosted receipt creation retry cases and raw executable inventory."""
import collections
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

expected = {
    "FreshCreation_NormalRetryStrategyPersistsOneReceiptAndComputedLines": 1,
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
for result in trx.findall("./t:Results/t:UnitTestResult", ns):
    matches = [method for method in expected if
               f"ReceiptCreationRetryTests.{method}" in result.get("testName", "")]
    if len(matches) != 1 or result.get("outcome") != "Passed":
        raise SystemExit("Unexpected or non-passing receipt creation case")
    actual[matches[0]] += 1
if dict(actual) != expected:
    raise SystemExit(f"Receipt creation cardinality mismatch: {dict(actual)}")
counters = trx.find("./t:ResultSummary/t:Counters", ns)
if counters is None or any(int(counters.get(key, "-1")) != 8 for key in ("total", "executed", "passed")):
    raise SystemExit("Require exactly 8 executed/passed cases")
if any(int(counters.get(key, "0")) != 0 for key in
       ("failed", "error", "timeout", "aborted", "inconclusive", "notExecuted")):
    raise SystemExit("Failed or skipped receipt creation case")
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
    "passed": 8, "failed": 0, "skipped": 0, "methods": dict(actual),
    "trxSha256": hashlib.sha256(reports[0].read_bytes()).hexdigest(),
    "rawSha256": next(iter(digests)), "rawCopies": len(raw),
    "fullServiceCoverageAcceptance": False, "actualAuthProducerAcceptance": False,
    "note": "Normal-Program configured-retry PostgreSQL store fault controls and internal workflow with synthetic providers plus real HTTP invoice-readback; actual owned Redis/lock/journal. No live provider, new HTTP creation-authorization, distributed race or deployment guarantee.",
}
(root / "receipt-create-proof.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(proof, indent=2))
