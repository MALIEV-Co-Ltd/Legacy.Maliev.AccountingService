"""Verify individual hosted source child-list cases and raw inventory without running .NET."""
import collections
import hashlib
import json
import pathlib
import sys
import xml.etree.ElementTree as ET

expected = {
    "EmptyOrMissingParent_SourceReturns404WithoutMasterOrChildMutation": 8,
    "ZeroParent_SourceDistinguishesItemsRequiredIdentityFromEmptyFiles": 4,
    "PopulatedParent_PreservesOwnedRowsPascalWireAndNoReadMutation": 4,
    "AnonymousOrLiveDenied_NoChildDisclosureOrFinancialMutation": 8,
}
root = pathlib.Path(sys.argv[1])
reports = list(root.rglob("*.trx"))
if len(reports) != 1:
    raise SystemExit("Expected one actual child-list TRX")
namespace = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
document = ET.parse(reports[0])
results = document.findall("./t:Results/t:UnitTestResult", namespace)
actual = collections.Counter()
for result in results:
    matches = [method for method in expected if
               f"AccountingChildListSourceHttpTests.{method}" in result.get("testName", "")]
    if len(matches) != 1 or result.get("outcome") != "Passed":
        raise SystemExit("Unknown or non-passing child-list case")
    actual[matches[0]] += 1
if dict(actual) != expected:
    raise SystemExit(f"Child-list method cardinality mismatch: {dict(actual)}")
counters = document.find("./t:ResultSummary/t:Counters", namespace)
if counters is None or any(int(counters.get(key, "-1")) != 24 for key in ("total", "executed", "passed")):
    raise SystemExit("Expected exactly24 actual individual passes")
if any(int(counters.get(key, "0")) != 0 for key in
       ("failed", "error", "timeout", "aborted", "inconclusive", "notExecuted")):
    raise SystemExit("Failed or skipped child-list case")
coverage = list(root.rglob("coverage.cobertura.xml"))
digests = {hashlib.sha256(path.read_bytes()).hexdigest() for path in coverage}
if len(digests) != 1:
    raise SystemExit("Expected one unique retained raw coverage report")
packages = ET.parse(coverage[0]).findall("./packages/package")
for assembly in ("Api", "Application", "Data", "Domain"):
    selected = [package for package in packages if package.get("name") == "Legacy.Maliev.AccountingService." + assembly]
    if not selected or not any(package.findall(".//line") for package in selected):
        raise SystemExit(f"Missing executable owned coverage inventory: {assembly}")
proof = {"passed":24, "failed":0, "skipped":0, "methods":dict(actual),
         "trxSha256":hashlib.sha256(reports[0].read_bytes()).hexdigest(),
         "rawSha256":next(iter(digests)), "rawCopies":len(coverage), "exclusions":[],
         "fullServiceCoverageAcceptance":False,
         "note":"Focused source-response evidence only; separate full suite gates all four assemblies >=80%. Snapshots cover seven master/child tables, not journal/correlation."}
(root / "child-list-proof.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
print(json.dumps(proof, indent=2))
