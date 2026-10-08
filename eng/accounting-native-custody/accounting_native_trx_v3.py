"""Accounting native structural TRX verifier; no SDK launch or grant generation.

Preserves all sixteen counter checks and GUID execution/definition bindings.
Adds full canonical namespace/method/adapter/compiled assembly binding.
"""
from pathlib import Path
import collections,hashlib,uuid,xml.etree.ElementTree as ET
from accounting_v3_guard_core import exact_focus_identity,PREFIX,bounded_bytes,unique_json
NS={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
FOCUS={'InvoiceMasterQuerySourceHttpTests.OmittedSize_UsesCompleteFilteredCountBeforePaging':2,
       'ReceiptMasterQuerySourceHttpTests.OmittedSize_UsesCompleteFilteredCountBeforePaging':2,
       'PaymentMasterQuerySourceHttpTests.OmittedSize_ReturnsAllFilteredRowsBeyondFormerLimits':2,
       'MasterStringSourceHttpTests.PaymentMasterDescription_RemainsUnboundedAndRoundTripsLiteralUnicode':4,
       'PaymentListHttpContractTests.OmittedSize_UsesFilteredCountWhileExplicitTwentyRetainsPaging':1,
       'ReceiptSortSourceHttpTests.OmittedSizeUsesFilteredCountWhileExplicitSizeRetainsMaximumCap':1}

def reviewed_full_inventory(path,reviewed_sha,assembly_sha):
    if path is None or reviewed_sha is None:raise ValueError('Independent reviewed compiled-assembly inventory required')
    raw=bounded_bytes(Path(path),8*1024**2)
    if hashlib.sha256(raw).hexdigest()!=reviewed_sha:raise ValueError('Independent inventory seal differs')
    inventory=unique_json(raw)
    if type(inventory)!=dict or set(inventory)!={'schemaVersion','assemblySha256','sourceReviewSha256','methods','caseNames'}:
        raise ValueError('Independent inventory schema differs')
    if type(inventory['schemaVersion']) is not int or inventory['schemaVersion']!=1 or inventory['assemblySha256']!=assembly_sha or inventory['sourceReviewSha256']!='81f3d42995bcbac1958746d8a80e620c417dfab4fea09351207030ece0e6ef0e':
        raise ValueError('Independent assembly/source inventory binding differs')
    methods=inventory['methods'];cases=inventory['caseNames']
    if type(methods)!=dict or not methods or any(type(name)!=str or not name.startswith(PREFIX) or type(count)!=int or count<1 for name,count in methods.items()) or sum(methods.values())!=1662:
        raise ValueError('Exact independently reviewed full method multiplicities required')
    if type(cases)!=list or len(cases)!=1662 or any(type(name)!=str for name in cases) or len(set(cases))!=1662:
        raise ValueError('Independent exact full case-name roster required')
    counts=collections.Counter()
    for case in cases:
        matches=[method for method in methods if case==method or case.startswith(method+'(')]
        if len(matches)!=1:raise ValueError('Independent case/method inventory mismatch')
        counts[matches[0]]+=1
    if dict(counts)!=methods:raise ValueError('Independent case multiplicities differ')
    return {name.removeprefix(PREFIX):count for name,count in methods.items()},set(cases)

def verify_native(path,assembly,assembly_sha,focused,inventory_path=None,reviewed_inventory_sha=None):
    raw=bounded_bytes(Path(path),32*1024**2)
    if b'<!DOCTYPE' in raw.upper() or b'<!ENTITY' in raw.upper():raise ValueError('TRX declarations refused')
    assembly=Path(assembly).resolve(strict=True)
    if hashlib.sha256(bounded_bytes(assembly,32*1024**2)).hexdigest()!=assembly_sha:
        raise ValueError('Actual compiled test assembly changed')
    document=ET.fromstring(raw)
    for method in document.findall('./t:TestDefinitions/t:UnitTest/t:TestMethod',NS):
        if Path(method.get('codeBase','')).resolve()!=assembly:raise ValueError('Native assembly path differs from actual compiled output')
    if focused:expected=FOCUS
    else:
        if Path(inventory_path or path).resolve()==Path(path).resolve():raise ValueError('Submitted TRX cannot supply its expected inventory')
        expected,cases=reviewed_full_inventory(inventory_path,reviewed_inventory_sha,assembly_sha)
        actual=[result.get('testName') for result in document.findall('./t:Results/t:UnitTestResult',NS)]
        if len(actual)!=1662 or set(actual)!=cases:raise ValueError('Submitted full case roster differs from independent inventory')
    total=12 if focused else 1662
    methods=verify_inventory(document,expected,total)
    exact_focus_identity(document,NS,dict(expected))
    return {'passed':total,'failed':0,'skipped':0,'methods':methods,'trxSha256':hashlib.sha256(raw).hexdigest(),'assemblySha256':assembly_sha,'nativeAccepted':False}

def verify_inventory(document, EXPECTED, total):
    if document.tag != "{" + NS["t"] + "}TestRun":
        raise ValueError("Require native TestRun root")
    for name, direct in (("Results", "./t:Results"), ("TestDefinitions", "./t:TestDefinitions"),
                         ("ResultSummary", "./t:ResultSummary"), ("Counters", "./t:ResultSummary/t:Counters")):
        all_nodes = [node for node in document.iter() if node.tag.split("}")[-1] == name]
        if len(all_nodes) != 1 or len(document.findall(direct, NS)) != 1:
            raise ValueError("Duplicate or misplaced native container")
    results = document.findall("./t:Results/t:UnitTestResult", NS)
    definitions = document.findall("./t:TestDefinitions/t:UnitTest", NS)
    if len(results) != len(document.findall(".//t:UnitTestResult", NS)) or len(definitions) != len(document.findall(".//t:UnitTest", NS)):
        raise ValueError("Misplaced native result/definition")
    by_id = {}
    for definition in definitions:
        test_id = str(uuid.UUID(definition.attrib["id"]))
        if test_id in by_id:
            raise ValueError("Duplicate definition")
        method = definition.find("t:TestMethod", NS)
        if method is None:
            raise ValueError("Missing method binding")
        execution = definition.find("t:Execution", NS)
        if execution is None:
            raise ValueError("Missing definition execution")
        by_id[test_id] = (method.attrib["className"].split(",")[0] + "." + method.attrib["name"],
                          str(uuid.UUID(execution.attrib["id"])), definition.attrib["name"])
    methods = collections.Counter()
    executions = set()
    referenced = set()
    display_names = set()
    for result in results:
        execution = str(uuid.UUID(result.attrib["executionId"]))
        if execution in executions or result.get("outcome") != "Passed":
            raise ValueError("Duplicate execution or non-passing case")
        executions.add(execution)
        test_id = str(uuid.UUID(result.attrib["testId"]))
        if test_id in referenced or result.get("testName") in display_names:
            raise ValueError("Repeated native case")
        binding = by_id.get(test_id)
        if binding is None or binding[1] != execution or binding[2] != result.get("testName"):
            raise ValueError("Definition execution/name mismatch")
        method = binding[0]
        matches = [name for name in EXPECTED if method == PREFIX + name]
        if len(matches) != 1 or matches[0] not in result.get("testName", ""):
            raise ValueError("Unknown or mismatched native case")
        referenced.add(test_id)
        display_names.add(result.get("testName"))
        methods[matches[0]] += 1
    if methods != EXPECTED or referenced != set(by_id):
        raise ValueError("Missing, additional, or incorrect native cases")
    summaries = document.findall("./t:ResultSummary", NS)
    all_counters = document.findall("./t:ResultSummary/t:Counters", NS)
    if len(summaries) != 1 or len(all_counters) != 1:
        raise ValueError("Require exactly one summary/counters")
    summary, counters = summaries[0], all_counters[0]
    if summary.get("outcome") != "Completed":
        raise ValueError("Missing completed summary")
    if set(counters.attrib) != {"total", "executed", "passed", "failed", "error", "timeout", "aborted", "inconclusive", "passedButRunAborted", "notRunnable", "notExecuted", "disconnected", "warning", "completed", "inProgress", "pending"}:
        raise ValueError("Missing or unexpected counters")
    for name, value in counters.attrib.items():
        expected = total if name in {"total", "executed", "passed"} else 0
        if int(value) != expected:
            raise ValueError("Failed, skipped, incomplete, or incorrect counters")
    return dict(methods)
