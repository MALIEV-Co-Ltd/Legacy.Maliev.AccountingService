"""Accounting-specific fail-closed guard components; not an enabled SDK route."""
from pathlib import Path, PurePosixPath
from datetime import datetime, timezone
import collections, hashlib, json, os, re, time
import xml.etree.ElementTree as ET

FLOOR = 4194304 * 1024
LIMIT = 8 * 1024**3
PREFIX = 'Legacy.Maliev.AccountingService.Tests.'
ASSEMBLY = 'Legacy.Maliev.AccountingService.Tests.dll'
PROJECTS = {f'Legacy.Maliev.AccountingService.{name}/Legacy.Maliev.AccountingService.{name}.csproj'
            for name in ('Api','Application','Data','Domain','Tests')}

def unique_json(raw):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result:raise ValueError('Duplicate evidence JSON key')
            result[key] = value
        return result
    return json.loads(raw, object_pairs_hook=unique)

def bounded_bytes(path, maximum):
    with path.open('rb') as stream:raw=stream.read(maximum+1)
    if len(raw)>maximum:raise ValueError('Evidence quota exceeded')
    return raw

def restored_audit_identity(repository):
    repository=repository.resolve(strict=True)
    raw=bounded_bytes(repository/'Legacy.Maliev.AccountingService.slnx',65536)
    if b'<!' in raw:raise ValueError('Solution declarations refused')
    projects=ET.fromstring(raw).findall('Project')
    if len(projects)!=5 or {node.get('Path') for node in projects}!=PROJECTS:
        raise ValueError('Exact Accounting five-project solution required')
    expected={}
    for node in projects:
        path=repository/node.get('Path')
        if any(parent.is_symlink() for parent in (path,*path.parents) if parent!=repository.parent):
            raise ValueError('Symlinked restore evidence refused')
        project=path.resolve(strict=True)
        if not project.is_relative_to(repository):raise ValueError('Foreign project')
        asset=project.parent/'obj'/'project.assets.json'
        if asset.is_symlink() or asset.parent.is_symlink():raise ValueError('Symlinked assets refused')
        blob=bounded_bytes(asset,16*1024**2);doc=unique_json(blob)
        restore=doc['project']['restore']
        if Path(restore['projectPath']).resolve()!=project or restore['projectStyle']!='PackageReference':
            raise ValueError('Restore project binding differs')
        if set(restore['originalTargetFrameworks'])!={'net10.0'} or set(doc['project']['frameworks'])!={'net10.0'} or set(doc['targets'])!={'net10.0'}:
            raise ValueError('Restore framework binding differs')
        if set(restore['sources'])!={'https://api.nuget.org/v3/index.json'}:
            raise ValueError('Restore source binding differs')
        packages={tuple(key.rsplit('/',1)) for key,value in doc['libraries'].items() if value['type']=='package'}
        if any(len(row)!=2 or not all(row) for row in packages):raise ValueError('Malformed restored package identity')
        expected[str(project)]={'frameworks':['net10.0'],'packages':sorted(packages),'assets':asset,'sha256':hashlib.sha256(blob).hexdigest()}
    return expected

def verify_bound_vulnerability_audit(stdout,stderr,expected):
    if len(expected)!=5:raise ValueError('Five actual restored Accounting identities required')
    result=verify_vulnerability_audit(stdout,stderr,{path:row['frameworks'] for path,row in expected.items()})
    for row in expected.values():
        if hashlib.sha256(bounded_bytes(row['assets'],16*1024**2)).hexdigest()!=row['sha256']:
            raise ValueError('Actual restored assets changed during audit')
    return result

def numeric(value):
    if not re.fullmatch(r'0|[1-9][0-9]*', value):
        raise ValueError('Invalid cgroup numeric bound')
    return int(value)

def effective_capacity(pid, proc=Path('/proc'), read=lambda p:p.read_text(), stat=lambda p:p.stat()):
    membership = read(proc / str(pid) / 'cgroup').splitlines()
    if len(membership) != 1 or not membership[0].startswith('0::/'):
        raise ValueError('Actual unified process membership required')
    relative = membership[0][3:]
    if relative == '/' or '..' in PurePosixPath(relative).parts:
        raise ValueError('Root/unbounded worker or invalid membership refused')
    mounts = []
    for row in read(proc / str(pid) / 'mountinfo').splitlines():
        left, separator, right = row.partition(' - ')
        if separator and right.split()[0] == 'cgroup2':
            parts = left.split()
            if len(parts) < 6 or parts[3] != '/' or '\\' in parts[4]:
                raise ValueError('Hidden cgroup ancestors or unverified mount refused')
            mounts.append(Path(parts[4]))
    if len(mounts) != 1:
        raise ValueError('Exactly one visible cgroup-v2 mount required')
    mount = mounts[0]; group = mount / relative.lstrip('/')
    if group == mount or not group.is_relative_to(mount):
        raise ValueError('Actual worker group outside admitted mount')
    host = re.search(r'^MemAvailable:\s+([0-9]+) kB$', read(proc / 'meminfo'), re.M)
    if host is None:
        raise ValueError('Actual host memory observation missing')
    available = numeric(host[1]) * 1024; rows = []
    current = group
    while current != mount:
        identity = stat(current)
        maximum = read(current / 'memory.max').strip()
        usage = numeric(read(current / 'memory.current').strip())
        if maximum == 'max':
            if current == group:
                raise ValueError('Actual worker must have a finite memory bound')
            bound = None
        else:
            bound = numeric(maximum)
            if usage > bound:
                raise ValueError('Current cgroup usage exceeds bound')
            available = min(available, bound - usage)
        if current == group:
            if bound is None or bound > LIMIT:
                raise ValueError('Actual worker limit is unavailable or too large')
            quota = read(current / 'cpu.max').strip().split()
            if len(quota) != 2 or not 1 <= numeric(quota[0]) <= numeric(quota[1]):
                raise ValueError('Actual one-CPU bound required')
            if not 1 <= numeric(read(current / 'pids.max').strip()) <= 512:
                raise ValueError('Finite actual worker task bound required')
        rows.append({'path': str(current), 'device': identity.st_dev, 'inode': identity.st_ino,
                     'maximum': bound, 'current': usage})
        if len(rows) > 64:
            raise ValueError('Cgroup ancestor traversal budget exceeded')
        current = current.parent
    if available < FLOOR:
        raise ValueError('Effective actual worker/ancestor/host four-GiB floor failed')
    return {'actualProcess': pid, 'actualCgroup': str(group), 'effectiveAvailableBytes': available,
            'observedAncestors': rows}

def exact_focus_identity(document, ns, expected):
    definitions = document.findall('./t:TestDefinitions/t:UnitTest', ns)
    identities = {}
    for definition in definitions:
        methods = definition.findall('t:TestMethod', ns)
        if len(methods) != 1:
            raise ValueError('Exactly one native assembly method required')
        method = methods[0]
        if method.get('adapterTypeName') != 'executor://xunit/VsTestRunner3/netcore/':
            raise ValueError('Unexpected native test adapter')
        name = method.get('className', '')
        # xUnit TRX emits a canonical className without an assembly suffix.
        if ',' in name or not name.startswith(PREFIX):
            raise ValueError('Foreign native namespace or class binding')
        identity = name + '.' + method.get('name', '')
        allowed = {PREFIX + item for item in expected}
        location = method.get('codeBase', '').replace('\\', '/')
        if identity not in allowed or PurePosixPath(location).name != ASSEMBLY or '..' in PurePosixPath(location).parts:
            raise ValueError('Foreign native full identity or assembly')
        if definition.get('id') in identities:
            raise ValueError('Duplicate native definition identity')
        identities[definition.get('id')] = identity
    counts = collections.Counter()
    for result in document.findall('./t:Results/t:UnitTestResult', ns):
        identity = identities.get(result.get('testId'))
        if identity is None or not result.get('testName', '').startswith(identity + '(') and result.get('testName') != identity:
            raise ValueError('Native display name differs from canonical full identity')
        counts[identity.removeprefix(PREFIX)] += 1
    if dict(counts) != expected:
        raise ValueError('Actual full-identity inventory differs')
    return dict(counts)

def verify_vulnerability_audit(stdout, stderr, expected_projects):
    """Strict clean dotnet JSON audit; command exit zero is insufficient."""
    if stderr.strip() or len(stdout.encode('utf-8')) > 16 * 1024**2:
        raise ValueError('Audit diagnostics or output bound failed')
    def unique(pairs):
        result = {}
        for key, value in pairs:
            if key in result: raise ValueError('Duplicate audit JSON key')
            result[key] = value
        return result
    doc = json.loads(stdout, object_pairs_hook=unique)
    if type(doc) is not dict or set(doc) != {'version', 'parameters', 'sources', 'projects'}:
        raise ValueError('Audit envelope differs')
    if type(doc['version']) is not int or doc['version'] != 1:
        raise ValueError('Audit version differs')
    if doc['parameters'] != '--vulnerable --include-transitive' or doc['sources'] != ['https://api.nuget.org/v3/index.json']:
        raise ValueError('Audit scope or source differs')
    projects = doc['projects']
    if type(projects) is not list or len(projects) != len(expected_projects):
        raise ValueError('Audit project inventory differs')
    seen = set()
    for project in projects:
        if type(project) is not dict or not {'path'} <= set(project) <= {'path', 'frameworks'}:
            raise ValueError('Audit project diagnostics or unknown keys')
        path = project['path']
        if path not in expected_projects or path in seen:
            raise ValueError('Foreign or duplicate audit project')
        seen.add(path)
        frameworks = project.get('frameworks', [])
        if type(frameworks) is not list: raise ValueError('Audit frameworks invalid')
        names = set()
        for framework in frameworks:
            if type(framework) is not dict or not {'framework'} <= set(framework) <= {'framework', 'topLevelPackages', 'transitivePackages'}:
                raise ValueError('Audit framework diagnostics or unknown keys')
            name = framework['framework']
            if name not in expected_projects[path] or name in names:
                raise ValueError('Audit framework binding differs')
            names.add(name)
            for category in ('topLevelPackages', 'transitivePackages'):
                if framework.get(category, []) != []:
                    raise ValueError('Vulnerable package rows refuse qualification')
    if seen != set(expected_projects): raise ValueError('Audit incomplete')
    return {'clean': True, 'projectCount': len(seen)}

class ImmutableLease:
    def __init__(self, expires, now=lambda:datetime.now(timezone.utc), clock=time.monotonic):
        self.now = now; self.clock = clock; self.expires = expires
        remaining = (expires-now()).total_seconds()
        if not 0 < remaining <= 2700:
            raise ValueError('Finite initial Accounting authority required')
        self.deadline = clock()+remaining
    def check(self):
        if self.clock() >= self.deadline or self.now() >= self.expires:
            raise TimeoutError('Unrenewed Accounting authority expired during phase')

def settle_owned_phase(work, cleanup, save, lease, cancelled=lambda:False):
    """Pure coordinator: actual native work must call guard continuously, and own its unit.

    A successful work return never replaces cleanup/receipt proof. Cleanup runs
    on normal return, first failure, expiry, cancellation and final-save failure.
    """
    result = {'nativeAccepted': False, 'cleanupVerified': False, 'receiptSaved': False,
              'uncertain': True, 'firstFailure': None, 'cleanupFailure': None, 'saveFailure': None}
    first = None
    def guard():
        if cancelled():
            raise KeyboardInterrupt('Accounting phase cancelled')
        lease.check()
    try:
        guard(); work(guard); guard()
    except BaseException as failure:
        first = failure; result['firstFailure'] = type(failure).__name__
    finally:
        try:
            proof = cleanup()
            required = {'sdkExited', 'ownedBackendsAbsent', 'handlesReleased', 'slotReleased', 'slotAbsent', 'cgroupAbsent'}
            if type(proof) is not dict or set(proof) != required or any(proof[k] is not True for k in required):
                raise ValueError('All six actual lifecycle absence flags required')
            result['cleanupVerified'] = True
        except BaseException as failure:
            result['cleanupFailure'] = type(failure).__name__
            if first is None:first = failure; result['firstFailure'] = type(failure).__name__
        try:
            # Durable evidence starts conservatively; successful persistence is
            # required before the caller is ever permitted to report success.
            save(dict(result)); result['receiptSaved'] = True
        except BaseException as failure:
            result['saveFailure'] = type(failure).__name__
            if first is None:first = failure; result['firstFailure'] = type(failure).__name__
        result['uncertain'] = not result['cleanupVerified'] or not result['receiptSaved']
    if first is not None:
        first.accounting_settlement = result
        raise first
    if result['uncertain']:
        raise ValueError('Lifecycle/evidence uncertainty refuses native acceptance')
    result['phaseSettled'] = True
    return result

def registered_fixture_absence(intents, inventory, inspect, daemon, owner, run):
    """No removal authority: prove exact pre-dispatch intent and acknowledged ID absence."""
    if len(intents) > 512 or len(inventory) > 512 or len({x['childRun'] for x in intents}) != len(intents):
        raise ValueError('Finite unique fixture intent inventory required')
    names = {x['name'] for x in intents}
    if len(names) != len(intents):
        raise ValueError('Duplicate fixture intent name refused')
    published_ids = set()
    for row in inventory:
        if row['owner'] != owner or row['run'] != run or row['daemon'] != daemon or row['name'] not in names:
            raise ValueError('Foreign or unregistered container; preserve it')
        raise ValueError('Owned fixture still physically exists')
    for intent in intents:
        if intent['owner'] != owner or intent['run'] != run or intent['daemon'] != daemon or intent['persistentData'] is not False:
            raise ValueError('Unverified fixture ownership or persistent storage')
        published = intent.get('publication')
        if not published or published.get('acknowledged') is not True or published.get('daemon') != daemon or not re.fullmatch('[0-9a-f]{64}', published.get('id', '')):
            raise ValueError('Unacknowledged Create absence is not cleanup proof')
        if any(published.get(key) != intent[key] for key in ('childRun','name','owner','run')):
            raise ValueError('Fixture acknowledgement differs from exact pre-dispatch intent')
        if published['id'] in published_ids:
            raise ValueError('Duplicate acknowledged fixture ID refused')
        published_ids.add(published['id'])
        if inspect(published['id']) != 404 or inspect(intent['name']) != 404:
            raise ValueError('Exact ID/name physical absence unverified')
    return {'ownedBackendsAbsent': True, 'registeredCount': len(intents)}
