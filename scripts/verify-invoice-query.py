"""Verify actual hosted invoice source-query evidence."""
import collections
import hashlib
import json
from pathlib import Path
import sys
import xml.etree.ElementTree as ET

expected = {
    "InvoiceMasterQuerySourceHttpTests.LiteralAndNumericSourceFields_FilterWithoutTrimmingOrWildcardExpansion": 10,
    "InvoiceMasterQuerySourceHttpTests.NullableDateSourceOrder_PrecedesPagingWithStableIdentifierTies": 4,
    "InvoiceMasterQuerySourceHttpTests.SelectedEmptyPage_SourceReturns404WithoutFinancialMutation": 2,
    "InvoiceMasterQuerySourceHttpTests.CustomerAndPaidFilters_ApplyBeforeSelectingPage": 2,
    "InvoiceMasterQuerySourceHttpTests.AnonymousAndLiveDenied_RejectBeforeFinancialDisclosure": 2,
    "InvoiceMasterQuerySourceHttpTests.ThaiLiteralAndNonnumericSearch_DoNotMatchReceiptZeroOrMutateStorage": 1,
    "InvoiceNumberSourceHttpTests.ExactNumber_BeyondEarlierSubstringRows_IsFoundWithoutPagingOrCacheMutation": 1,
    "InvoiceNumberSourceHttpTests.MissingWholeNumber_SubstringAndPurchaseOrderMatchesDoNotSubstitute": 3,
    "InvoiceNumberSourceHttpTests.DuplicateWholeNumber_BeyondEarlierSubstringRows_RefusesOpaqueWithoutMutation": 3,
    "InvoiceNumberSourceHttpTests.FiniteCaseAdaptation_PreservesExistingPostgresCaseInsensitiveRead": 2,
    "InvoiceNumberSourceHttpTests.WholeNumber_KeepsLiteralCharactersThaiAndSignificantPadding": 5,
    "InvoiceNumberSourceHttpTests.NumericRoute_PreservesIdentifierPrecedenceOverAnotherInvoicesNumber": 2,
    "InvoiceNumberSourceHttpTests.AnonymousOrLiveDenied_NumberReadCannotDiscloseOrMutate": 2,
}
fixture = Path(__file__).resolve().parents[1] / "Legacy.Maliev.AccountingService.Tests/InvoiceMasterQuerySourceHttpTests.cs"
source = fixture.read_bytes().decode("utf-8", errors="strict")
thai = r"\u0e0a\u0e34\u0e49\u0e19\u0e07\u0e32\u0e19"
if not source.isascii() or source.count(thai) != 1:
    raise SystemExit("Require exact escaped Thai fixture bytes")
number_source = fixture.with_name("InvoiceNumberSourceHttpTests.cs").read_bytes().decode("utf-8", errors="strict")
if not number_source.isascii() or number_source.count(thai) != 1:
    raise SystemExit("Require exact escaped Thai number fixture bytes")
if sys.argv[1] == "--encoding":
    print("Exact UTF-8/ASCII Invoice fixture and Thai literal verified")
    raise SystemExit(0)
root = Path(sys.argv[1])
reports = list(root.rglob("*.trx"))
if len(reports) != 1:
    raise SystemExit("Require exactly one actual invoice query TRX")
ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
trx = ET.parse(reports[0])
definitions = {}
namespace = "Legacy.Maliev.AccountingService.Tests."
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
    owned = identity.removeprefix(namespace)
    name = definition.get("name", "")
    if not identity.startswith(namespace) or owned not in expected or not (name == identity or name.startswith(identity + "(")):
        raise SystemExit("Unexpected class/method definition: " + identity)
    if test_id in definitions and definitions[test_id] != identity:
        raise SystemExit("Contradictory definitions for reused theory test ID")
    definitions[test_id] = identity
actual = collections.Counter()
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
        raise SystemExit("Unexpected, duplicate, or non-passing invoice query result: " + name)
    if not (name == identity or name.startswith(identity + "(")):
        raise SystemExit("Result name contradicts its class/method definition")
    executions.add(execution)
    names.add(name)
    used_definitions.add(test_id)
    actual[identity.removeprefix(namespace)] += 1
if used_definitions != set(definitions) or dict(actual) != expected:
    raise SystemExit("Invoice query case cardinality mismatch: " + str(dict(actual)))
counters = trx.find("./t:ResultSummary/t:Counters", ns)
required_counters = {key: 39 for key in ("total", "executed", "passed")}
required_counters.update({key: 0 for key in (
    "failed", "error", "timeout", "aborted", "inconclusive", "passedButRunAborted",
    "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending",
)})
if counters is None or any(key not in counters.attrib or int(counters.get(key)) != value
                           for key, value in required_counters.items()):
    raise SystemExit("Require all 16 standard counters: 39 total/executed/passed and zero in every other state")
raw = list(root.rglob("coverage.cobertura.xml"))
digests = {hashlib.sha256(path.read_bytes()).hexdigest() for path in raw}
if len(digests) != 1:
    raise SystemExit("Require one unique retained raw coverage report")
packages = ET.parse(raw[0]).findall("./packages/package")
for assembly in ("Api", "Application", "Data", "Domain"):
    if not any(package.get("name") == "Legacy.Maliev.AccountingService." + assembly
               and package.findall(".//line") for package in packages):
        raise SystemExit("Missing executable coverage inventory: " + assembly)
receipt = {"passed": 39, "failed": 0, "skipped": 0, "methods": dict(actual),
           "trx_sha256": hashlib.sha256(reports[0].read_bytes()).hexdigest(),
           "raw_sha256": next(iter(digests)), "raw_copies": len(raw),
           "note": "Focused evidence; full unfiltered suite separately gates all four owned assemblies at 80 percent."}
(root / "invoice-query-proof.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
print(json.dumps(receipt, indent=2))
