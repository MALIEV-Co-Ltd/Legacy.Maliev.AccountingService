"""Require the pinned Defaults nine-case cache contract after a clean strict build."""
import collections
import hashlib
import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

PREFIX = "Maliev.Aspire.Tests.Unit.IamPermissionCacheIsolationTests."
EXPECTED = {
    PREFIX + f"IndependentHosts_CachedResultCannotReplaceOwnDecision(sameOrigin: {origin}, firstAllowed: {allowed})"
    for origin in ("False", "True")
    for allowed in ("False", "True")
} | {
    PREFIX + name for name in (
        "IndependentHosts_InFlightAllowCannotReplaceDenial",
        "SameHost_IndependentScopesReuseCachedResult",
        "SameHost_IndependentScopesShareInFlightRequest",
        "SameHost_CanceledScopedWaiterDoesNotCancelSharedFetch",
        "IndependentHosts_LiveRefreshDoesNotOverwriteOtherHost",
    )
}


def verify(root):
    build = (root / "build.log").read_text(encoding="utf-8")
    warnings = re.findall(r"^\s*(\d+) Warning\(s\)\s*$", build, re.MULTILINE)
    errors = re.findall(r"^\s*(\d+) Error\(s\)\s*$", build, re.MULTILINE)
    if "Build succeeded." not in build or warnings != ["0"] or errors != ["0"]:
        raise ValueError("Pinned Defaults build must report zero warnings and zero errors")
    reports = list(root.rglob("*.trx"))
    if len(reports) != 1:
        raise ValueError("Expected exactly one Defaults cache TRX")
    document = ET.parse(reports[0])
    counters = document.find(".//{*}Counters")
    if counters is None:
        raise ValueError("Missing Defaults cache counters")
    counts = {name: int(counters.attrib[name]) for name in
              ("total", "executed", "passed", "failed", "notExecuted")}
    if counts != {"total": 9, "executed": 9, "passed": 9, "failed": 0, "notExecuted": 0}:
        raise ValueError("All nine cache cases must execute and pass without skips")
    results = document.findall(".//{*}UnitTestResult")
    actual = collections.Counter(node.attrib["testName"] for node in results)
    if actual != collections.Counter({name: 1 for name in EXPECTED}):
        raise ValueError("Defaults cache names or cardinalities differ from the pinned nine cases")
    if any(node.get("outcome") != "Passed" for node in results):
        raise ValueError("Every individual Defaults cache result must be Passed")
    return {"defaultsCommit": "7edcd961024868513fd5f373cab3dcb261197f77",
            "counters": counts, "actualTests": dict(actual),
            "buildSha256": hashlib.sha256((root / "build.log").read_bytes()).hexdigest(),
            "trxSha256": hashlib.sha256(reports[0].read_bytes()).hexdigest(),
            "coverageAcceptance": False,
            "note": "Cache compatibility evidence only; neither the 17-case graph nor service coverage acceptance."}


if __name__ == "__main__":
    result_root = pathlib.Path(sys.argv[1])
    proof = verify(result_root)
    (result_root / "defaults-cache-proof.json").write_text(json.dumps(proof, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(proof))
