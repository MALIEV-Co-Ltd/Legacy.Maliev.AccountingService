"""Build-only stage inside an externally admitted Linux SDK unit; allocates no unit/backend."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import selectors
import shutil
import subprocess
import sys
import time
import types

ADAPTER_SHA = '9ddd09bd120d1f79bd4b4581cd3e4bbcc52e0216fe8a632619e25e7bf6e81e15'
ADMISSION_SHA = '3e48f50cadabf216020f10f426d7b837c1f1946500a643470e3d7ca71c5da4cd'
POLICIES = {'accounting': 'c51c0397b97140a21a1a838249ba741a42087ab59a317c9105c5d622517b1696',
            'procurement': 'b85e966fa74cfa5aa022fc502f56038d9105d3dece17612196dbdd6a89d8258c',
            'order': '027218ff9f5ddf87dd6d8afec7ea376b25826822925ddc970fe08eb44ba5c542'}
MINIMUM_KIB = 4194304
MAX_LOG_BYTES = 8 * 1024 * 1024


def verified_module(path, expected):
    path = Path(path).absolute()
    if any(part.is_symlink() or getattr(part.lstat(), 'st_file_attributes', 0) & 0x400 for part in (path, *path.parents)):
        raise ValueError('Linked executable source refused')
    with path.open('rb') as stream:
        raw = stream.read(256 * 1024 + 1)
    if len(raw) > 256 * 1024 or hashlib.sha256(raw).hexdigest() != expected:
        raise ValueError('Reviewed executable source seal differs')
    module = types.ModuleType(path.stem)
    module.__file__ = str(path)
    exec(compile(raw, str(path), 'exec'), module.__dict__)
    return module


def capacity():
    text = Path('/proc/meminfo').read_text()
    match = re.search(r'^MemAvailable:\s+(\d+) kB$', text, re.MULTILINE)
    if match is None or int(match[1]) < MINIMUM_KIB:
        raise ValueError('Fixed 4096MiB SDK admission floor not met')


def gate(admission, context_path, required_seconds=1280, original_context=None):
    if sys.platform != 'linux' or os.environ.get('GITHUB_ACTIONS') != 'false':
        raise ValueError('Only externally admitted Linux SDK unit is allowed')
    context = json.loads(Path(context_path).read_bytes())
    if original_context is not None and context != original_context:
        raise ValueError('External owner context changed during unrenewed build lease')
    if os.environ.get('FINANCIAL_OWNED_CGROUP') != context.get('containerCgroup'):
        raise ValueError('Exact external owner aggregate binding required')
    expiry = datetime.fromisoformat(context['expiresUtc'].replace('Z', '+00:00'))
    if (expiry - datetime.now(timezone.utc)).total_seconds() < required_seconds:
        raise ValueError('Fresh finite owner lease must cover build and cleanup budgets')
    capacity()
    # Reuse independently reviewed actual cgroup, socket/peer, proxy and daemon checks.
    admission.admission(str(context_path))
    return context


def tree(shared, root, git_roots=None):
    root = Path(root).absolute()
    git_roots = {root} if git_roots is None else {Path(path).absolute() for path in git_roots}
    shared.reject_links(root)
    files = {}
    total = 0
    for directory, dirs, names in os.walk(root, followlinks=False):
        # Git metadata belongs to the external trusted baseline owner, never the capsule.
        if Path(directory) in git_roots:
            dirs[:] = [name for name in dirs if name != '.git']
        for name in dirs:
            shared.reject_links(Path(directory) / name)
        for name in names:
            if Path(directory) in git_roots and name == '.git':
                continue
            path = Path(directory) / name
            shared.reject_links(path)
            relative = shared.canonical_path(str(path.relative_to(root)).replace('\\', '/'))
            with path.open('rb') as stream:
                raw = stream.read(shared.MAX_FILE_BYTES + 1)
            total += len(raw)
            if len(raw) > shared.MAX_FILE_BYTES or total > 16 * 1024 * 1024 or len(files) >= 1024:
                raise ValueError('Bounded fresh native source graph required')
            files[relative] = raw
    return files


def bind_graph(shared, adapter, value, capsule, source_root, dependencies):
    decoded = shared.validate_zip(capsule, value['bundleSha256'], value['bundleBytes'], value['entries'])
    manifest, dependency_manifest = adapter.bind(shared, decoded, value)
    expected = {row['path']: row['sha256'] for row in value['baseFiles']}
    expected.update({row['path']: row['sha256'] for row in manifest['files']})
    actual = tree(shared, source_root)
    if {path: shared.digest(raw) for path, raw in actual.items()} != expected:
        raise ValueError('Fresh complete native candidate differs from fixed raw graph')
    expected_dependencies = {row['repository'] + '/' + row['path']: row['sha256'] for row in dependency_manifest['files']}
    metadata_roots = {Path(dependencies) / name for name in value['dependencyPins']}
    if {path: shared.digest(raw) for path, raw in tree(shared, dependencies, metadata_roots).items()} != expected_dependencies:
        raise ValueError('Fresh complete native dependencies differ from fixed raw graph')


def command(shared, argv, cwd, log_path, seconds, ledger):
    record = {'argv': argv, 'timeoutSeconds': seconds, 'pid': None, 'startTicks': None,
              'actualExecutable': None, 'directChildExited': False,
              'sdkUnitQuiescenceProven': False}
    ledger.append(record)
    # Every child inherits the externally owned capped SDK unit; outer owner must settle it.
    with log_path.open('xb') as output:
        owned = subprocess.Popen(argv, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        first_failure = None
        try:
            record['pid'] = owned.pid
            record['startTicks'] = Path(f'/proc/{owned.pid}/stat').read_text().rsplit(')', 1)[1].split()[19]
            record['actualExecutable'] = os.readlink(f'/proc/{owned.pid}/exe')
            deadline = time.monotonic() + seconds
            written = 0
            eof = False
            # The admitted Linux unit supports nonblocking pipes. No additional
            # reader thread is allocated, so cancellation owns only this child.
            descriptor = owned.stdout.fileno()
            os.set_blocking(descriptor, False)
            with selectors.DefaultSelector() as selector:
                selector.register(descriptor, selectors.EVENT_READ)
                while owned.poll() is None or not eof:
                    if time.monotonic() >= deadline:
                        raise TimeoutError('Build-stage command deadline exceeded')
                    for _, _ in selector.select(timeout=0.02):
                        try:
                            chunk = os.read(descriptor, 64 * 1024)
                        except BlockingIOError:
                            continue
                        if not chunk:
                            eof = True
                            selector.unregister(descriptor)
                            break
                        remaining = MAX_LOG_BYTES - written
                        output.write(chunk[:remaining])
                        written += min(len(chunk), remaining)
                        if len(chunk) > remaining:
                            raise ValueError('Build-stage log budget exceeded during execution')
                output.flush()
            record['exitCode'] = owned.returncode
            if owned.returncode:
                raise ValueError('Build-stage command failed; no later phase accepted')
        except BaseException as error:
            first_failure = error
            record['firstFailure'] = type(error).__name__ + ': ' + str(error)
            raise
        finally:
            # Shared exact-child cancellation/settlement barrier; never select a process by name/tree.
            cancellation = shared.recover_fetch_owner(owned)
            record['directChildExited'] = owned.poll() is not None
            if cancellation is not None and first_failure is None:
                raise cancellation
    with log_path.open('rb') as stream:
        raw = stream.read(8 * 1024 * 1024 + 1)
    if len(raw) > MAX_LOG_BYTES:
        raise ValueError('Build-stage log budget exceeded')
    return raw.decode('utf-8', errors='strict')


def build_stage(shared, admission, context_path, lane, source_root, dependencies, results, run=command):
    ledger = []
    original_context = gate(admission, context_path, 1280)
    result_root = Path(results).absolute()
    shared.reject_links(result_root)
    if any(result_root == Path(root).absolute() or result_root.is_relative_to(Path(root).absolute())
           for root in (source_root, dependencies)):
        raise ValueError('Results must remain outside sealed source/dependencies')
    if Path(results).exists():
        raise ValueError('Fresh externally owned results directory required')
    Path(results).mkdir()
    try:
        executable = shutil.which('dotnet')
        if executable is None:
            raise ValueError('Admitted SDK executable unavailable')
        version = run(shared, [executable, '--version'], source_root, Path(results) / 'sdk-version.log', 20, ledger)
        if not re.fullmatch(r'10\.\d+\.\d+\s*', version):
            raise ValueError('Exact .NET10 SDK family required')
        solution = 'Legacy.Maliev.' + lane.capitalize() + 'Service.slnx'
        properties = ['-p:UseLocalMalievDependencies=true', '-p:MalievWorkspaceRoot=' + str(Path(dependencies).absolute())]
        gate(admission, context_path, 1260, original_context)
        run(shared, [executable, 'restore', solution, '--disable-build-servers', *properties],
            source_root, Path(results) / 'restore.log', 300, ledger)
        gate(admission, context_path, 960, original_context)
        output = run(shared, [executable, 'build', solution, '-c', 'Release', '--no-restore',
            '--disable-build-servers', '-warnaserror', '-m:1', '-p:UseSharedCompilation=false', *properties],
            source_root, Path(results) / 'build.log', 900, ledger)
        if 'Build succeeded.' not in output or not re.search(r'^\s*0 Warning\(s\)\s*$', output, re.MULTILINE) or not re.search(r'^\s*0 Error\(s\)\s*$', output, re.MULTILINE):
            raise ValueError('Actual zero-warning/error compiler summary required')
        return {'buildStageExited': True, 'warnings': 0, 'errors': 0, 'nativeAccepted': False,
                'focusedAndFullPending': True, 'outerSdkBackendCleanupPending': True}
    finally:
        (Path(results) / 'owned-child-ledger.json').write_text(json.dumps(ledger, indent=2))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--lane', choices=tuple(POLICIES), required=True)
    parser.add_argument('--transport', type=Path, required=True)
    parser.add_argument('--admission-verifier', type=Path, required=True)
    parser.add_argument('--context', type=Path, required=True)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--dependencies', type=Path, required=True)
    parser.add_argument('--results', type=Path, required=True)
    args = parser.parse_args()
    if args.results.absolute() == args.transport.absolute() or args.results.absolute().is_relative_to(args.transport.absolute()):
        raise ValueError('Results must remain outside reviewed transport')
    adapter = verified_module(args.transport / 'commerce_source_adapter_v1.py', ADAPTER_SHA)
    raw = (args.transport / 'policy.json').read_bytes()
    shared = adapter.load_shared(args.transport / 'sealed_source_capsule.py', adapter.SHARED_SHA256,
                                 raw, POLICIES[args.lane], args.lane)
    value = adapter.policy(shared, raw, POLICIES[args.lane], args.lane)
    bind_graph(shared, adapter, value, (args.transport / 'capsule.zip').read_bytes(), args.source, args.dependencies)
    admission = verified_module(args.admission_verifier, ADMISSION_SHA)
    # The external owner sets these immutable workload limits before this entrypoint.
    required = {'DOTNET_PROCESSOR_COUNT': '1', 'DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER': '1',
                'MSBUILDDISABLENODEREUSE': '1', 'UseSharedCompilation': 'false', 'DOTNET_CLI_UI_LANGUAGE': 'en'}
    if any(os.environ.get(name) != value for name, value in required.items()):
        raise ValueError('Exact externally owned SDK environment required')
    result = build_stage(shared, admission, args.context, args.lane, args.source, args.dependencies, args.results)
    result.update(lane=args.lane, acceptedBase=value['acceptedBase'], candidateFiles=value['candidateFileCount'],
                  policySha256=POLICIES[args.lane], privateProxyRequired=True)
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
