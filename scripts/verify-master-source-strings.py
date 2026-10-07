"""Retain actual native master source-string boundary results; full-service coverage is separate."""
import collections
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import stat
import threading
import uuid
import xml.etree.ElementTree as ET

EXPECTED = {'MasterStringSourceHttpTests.ExactUtf16BoundaryWritesRoundTripAndOverflowRejectsWithoutMutation': 188, 'MasterStringSourceHttpTests.SourceNullableFieldCanClearThroughPostOrReplacementPut': 84, 'MasterStringSourceHttpTests.SourceRequiredMeansNullOnlyAndRetainsEmptyAndWhitespace': 18, 'MasterStringSourceHttpTests.InvalidSourceStringsNeverBypassAuthenticationOrLivePermission': 28, 'MasterStringSourceHttpTests.MissingPutRetains404BeforeInvalidStringValidation': 7, 'MasterStringSourceMigrationTests.RuntimeModelAndSnapshotMatchEveryOwnedSourceRule': 3, 'MasterStringSourceMigrationTests.PhysicalConstraintsEnforceExactBmpAndSupplementaryUtf16WithoutTruncation': 47, 'MasterStringSourceMigrationTests.RetainedOverflowRefusesUpgradeAtomicallyAndNeverRewritesSourceRows': 47, 'MasterStringSourceMigrationTests.RequiredNullBlocksHistoricalAndPhysicalWritesButEmptyAndPaddingRemainLiteral': 9, 'MasterStringSourceMigrationTests.UnexpectedSchemaOrNamedConstraintCollisionRefusesWholeDatabaseUpgrade': 13, 'MasterStringSourceMigrationTests.BoundedRowAdmissionRejects10001ThenAccepts10000WithoutChangingRows': 7, 'MasterStringSourceMigrationTests.UpDownUpPreservesAllRowsRestoresPreimageAndReinstallsSameMetadata': 3, 'MasterStringSourceMigrationTests.MissingOrWrongOwnedCheckRefusesDowngradeWithoutRelaxingOtherConstraints': 6, 'MasterStringSourceMigrationTests.WriterLockDeadlineRefusesUpgradeAndReleasesMigrationTransaction': 3,     'MasterStringSourceMigrationTests.StatementDeadlineRollsBackUpgradeAndRemovesExactOwnedDelayHelpers': 3, 'MasterStringSourceStoreTests.InvalidInvoiceAfterAdmissionRollsBackFinancesAndSameKeyFailsClosed': 4, 'MasterStringSourceStoreTests.RetainedOriginValidationPrecedesInvalidInvoiceStrings': 1, 'MasterStringSourceStoreTests.NullInvoiceCurrencyCannotCreateReceiptOrMutateInvoice': 1, 'MasterStringSourceStoreTests.ReceiptRequiredCurrencyRetainsEmptyAndPaddedLiteral': 2}
NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

MAX_ARTIFACT = 64 * 1024 * 1024
MAX_CANDIDATES = 256
MAX_AGGREGATE = 256 * 1024 * 1024


def owned_regular(path, root):
    path, root = Path(os.path.abspath(path)), Path(os.path.abspath(root))
    path.relative_to(root)
    for parent in (path, *path.parents):
        info = parent.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
            raise ValueError("Linked artifact/source ancestry is forbidden")
    if not stat.S_ISREG(path.stat().st_mode):
        raise ValueError("Require regular artifact/source")
    return path


def bounded_read(path, root, limit=MAX_ARTIFACT):
    path = owned_regular(path, root)
    if path.stat().st_size > limit:
        raise ValueError("Oversized artifact/source")
    with path.open("rb") as stream:
        raw = stream.read(limit + 1)
    if len(raw) > limit:
        raise ValueError("Artifact/source grew beyond limit")
    return raw


def discover(root):
    root = Path(os.path.abspath(root))
    for parent in (root, *root.parents):
        info = parent.lstat()
        if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
            raise ValueError("Linked discovery ancestry is forbidden")
    reports, coverage, count, total = [], [], 0, 0
    pending = [root]
    while pending:
        directory = pending.pop()
        with os.scandir(directory) as entries:
            for entry in entries:
                count += 1
                if count > MAX_CANDIDATES:
                    raise ValueError("Too many artifact candidates")
                path = Path(entry.path)
                info = entry.stat(follow_symlinks=False)
                if stat.S_ISLNK(info.st_mode) or getattr(info, "st_file_attributes", 0) & 0x400:
                    raise ValueError("Linked discovery entry is forbidden")
                if stat.S_ISDIR(info.st_mode):
                    pending.append(path)
                else:
                    owned_regular(path, root)
                    total += info.st_size
                    if total > MAX_AGGREGATE:
                        raise ValueError("Artifact aggregate exceeds limit")
                    if entry.name.endswith(".trx"): reports.append(path)
                    if entry.name == "coverage.cobertura.xml": coverage.append(path)
    return reports, coverage


def capped_command(arguments, limit):
    timer = None
    process = subprocess.Popen(arguments, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
    try:
        timer = threading.Timer(30, process.kill)
        timer.start()
        raw = process.stdout.read(limit + 1)
        if len(raw) > limit:
            raise ValueError("Committed source output exceeds bound")
        if process.wait(timeout=30) != 0:
            raise ValueError("Committed source command failed")
        return raw
    finally:
        try:
            if timer is not None:
                timer.cancel()
                if timer.ident is not None:
                    timer.join(timeout=5)
                    if timer.is_alive():
                        raise ValueError("Command deadline worker did not exit")
        finally:
            try:
                if process.poll() is None: process.kill()
                process.wait(timeout=5)
            finally:
                process.stdout.close()


def committed_source(head, path):
    reference = head + ":" + path
    size = int(capped_command(["git", "cat-file", "-s", reference], 32))
    if size < 0 or size > MAX_ARTIFACT:
        raise ValueError("Committed source size exceeds bound")
    raw = capped_command(["git", "show", reference], size)
    if len(raw) != size:
        raise ValueError("Committed source size differs")
    return raw


def xml(path, root=None):
    raw = bounded_read(path, root or Path(path).parent)
    if not raw:
        raise ValueError("Missing artifact")
    source = raw.decode("utf-8-sig")
    if "<!DOCTYPE" in source.upper() or "<!ENTITY" in source.upper():
        raise ValueError("DTD/entity declarations are forbidden")
    return ET.fromstring(source), hashlib.sha256(raw).hexdigest()

def verify_trx(document):
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
        matches = [name for name in EXPECTED if method.endswith("." + name)]
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
        expected = 474 if name in {"total", "executed", "passed"} else 0
        if int(value) != expected:
            raise ValueError("Failed, skipped, incomplete, or incorrect counters")
    return dict(methods)

def bind_source(path, committed):
    if bounded_read(path, Path.cwd(), len(committed)) != committed:
        raise ValueError("Source bytes differ from the tested commit")
    return hashlib.sha256(committed).hexdigest()

def main():
    if os.environ.get("TZ") != "UTC":
        raise ValueError("Require declared isolated testhost timezone")
    root = Path(sys.argv[1])
    reports, coverage = discover(root)
    if len(reports) != 1:
        raise ValueError("Require one native TRX")
    document, trx_hash = xml(reports[0], root)
    methods = verify_trx(document)
    if not coverage:
        raise ValueError("Missing raw coverage")
    parsed = [xml(path, root) for path in coverage]
    hashes = {digest for _, digest in parsed}
    if len(hashes) != 1:
        raise ValueError("Divergent raw coverage")
    for assembly in ("Api", "Application", "Data", "Domain"):
        selected = [package for package in parsed[0][0].findall("./packages/package")
                    if package.get("name") == "Legacy.Maliev.AccountingService." + assembly]
        if not selected or not any(package.findall(".//line") for package in selected):
            raise ValueError("Missing executable owned assembly")
    head = capped_command(["git", "rev-parse", "HEAD"], 64).decode("ascii").strip()
    if head != os.environ.get("GITHUB_SHA"):
        raise ValueError("Actual checkout differs from hosted tested commit")
    paths = capped_command(["git", "ls-files"], 1024 * 1024).decode("utf-8").splitlines()
    sources = [path for path in paths if path.endswith((".cs", ".csproj", ".props", ".targets", ".slnx")) or path in
               {"global.json", "NuGet.Config", "nuget.config", ".editorconfig",
                "scripts/verify-master-source-strings.py", "scripts/test_master_source_strings_gate.py", ".github/workflows/master-source-strings.yml",
                "scripts/employee-completion-expected.json", "scripts/file-metadata-expected.json",
                "scripts/payment-file-metadata-expected.json", "scripts/paid-invoice-wire-expected.json", ".github/workflows/_build-and-test.yml",
                "docs/master-source-strings-boundary.md"}]
    bindings = {}
    for path in sources:
        committed = committed_source(head, path)
        bindings[path] = bind_source(Path(path), committed)
    proof = {"testedCommit": head, "runId": os.environ["GITHUB_RUN_ID"], "runAttempt": os.environ["GITHUB_RUN_ATTEMPT"],
             "passed": 474, "failed": 0, "skipped": 0, "methods": methods, "trxSha256": trx_hash,
             "rawSha256": next(iter(hashes)), "exclusions": [], "sourceBindings": bindings,
             "testTimezone": os.environ["TZ"], "sourceModelIntentOnly": True, "persistentDataCompatibilityAccepted": False, "fullServiceCoverageAcceptance": False, "actualAuthProducerAcceptance": False,
             "originalSqlServerExecuted": False, "persistentSchemaApplied": False}
    destination = root / "master-source-strings-proof.json"
    if destination.exists():
        raise ValueError("Refuse to rewrite an existing native receipt")
    destination.write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(proof, indent=2))

if __name__ == "__main__":
    main()
