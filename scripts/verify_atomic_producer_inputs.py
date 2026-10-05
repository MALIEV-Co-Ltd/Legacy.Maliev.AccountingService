"""Fail closed when current production/build inputs differ from the reviewed graph."""
import fnmatch
import json
import pathlib
import re
import subprocess
import sys

PROJECTS = ("Legacy.Maliev.AccountingService.Api", "Legacy.Maliev.AccountingService.Application",
            "Legacy.Maliev.AccountingService.Data", "Legacy.Maliev.AccountingService.Domain")
ROOT_PATTERNS = ("directory.build.*", "directory.packages.*", "global.json", "nuget.config", "*.slnx")


def select_inputs(entries):
    return {path: blob for path, blob in entries.items()
            if any(path.startswith(project + "/") for project in PROJECTS)
            or ("/" not in path and any(fnmatch.fnmatchcase(path.lower(), pattern) for pattern in ROOT_PATTERNS))}


def require_equal(current, reviewed):
    if not any(path.startswith(PROJECTS[0] + "/") for path in reviewed) or current != reviewed:
        raise ValueError("Current production/build inputs differ from the reviewed producer; additions, removals and drift require a separately reviewed graph")


def require_dependency_pins(source, expected):
    actual = {}
    for repository, commit in re.findall(r"(?m)^\s+repository: MALIEV-Co-Ltd/(\S+)\s*\n\s+ref: ([^\s]+)\s*$", source):
        if repository in actual:
            raise ValueError("Duplicate dependency checkout")
        actual[repository] = commit
    if actual != expected:
        raise ValueError("Ordinary validation dependencies differ from the prospective graph")


def tracked_inputs(repository):
    raw = subprocess.check_output(["git", "-C", str(repository), "ls-tree", "-r", "-z", "HEAD"])
    entries = {}
    for record in raw.split(b"\0"):
        if record:
            metadata, path = record.split(b"\t", 1)
            entries[path.decode("utf-8")] = metadata.decode("ascii")
    return select_inputs(entries)


def main():
    current = pathlib.Path.cwd()
    reviewed = pathlib.Path(sys.argv[1])
    manifest = json.loads((current / "tools/CommerceAtomicProtocol.Tests/public-graph.json").read_text(encoding="utf-8"))
    references = {entry["repository"]: entry["commit"] for entry in manifest["references"]}
    actual_head = subprocess.check_output(["git", "-C", str(reviewed), "rev-parse", "HEAD"], text=True).strip()
    if actual_head != references["Legacy.Maliev.AccountingService"]:
        raise ValueError("Reviewed checkout does not match the declared Accounting pin")
    require_equal(tracked_inputs(current), tracked_inputs(reviewed))
    expected = {name: references[name] for name in ("Legacy.Maliev.ServiceDefaults", "Legacy.Maliev.CompatibilityContracts")}
    for workflow in ("_build-and-test.yml", "atomic-focused-validation.yml"):
        require_dependency_pins((current / ".github/workflows" / workflow).read_text(encoding="utf-8"), expected)
    print("Current production and tracked root build inputs equal the reviewed producer; ordinary dependency pins match the graph")


if __name__ == "__main__":
    main()
