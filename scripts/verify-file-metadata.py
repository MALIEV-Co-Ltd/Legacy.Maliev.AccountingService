"""Require actual passing file metadata inventory and all counters; forecasts are not results."""
import collections
import hashlib
import json
import re
from pathlib import Path
import sys
import uuid
import xml.etree.ElementTree as ET

root = Path(sys.argv[1])
full = '--full' in sys.argv[2:]
manifest = json.loads((Path(__file__).parent / 'file-metadata-expected.json').read_text())
expected = manifest['methods']
total = manifest['fullForecast'] if full else manifest['forecast']
assert sum(expected.values()) == manifest['forecast'] == 46
reports = list(root.rglob('*.trx'))
if len(reports) != 1:
    raise SystemExit('Require one actual TRX')
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}

def bounded_bytes(path, maximum):
    with path.open('rb') as stream:
        data = stream.read(maximum + 1)
    if len(data) > maximum:
        raise SystemExit('XML exceeds finite evidence size bound')
    return data

def bounded_xml(path, maximum):
    data = bounded_bytes(path, maximum)
    try:
        text = data.decode('utf-8-sig')
    except UnicodeDecodeError:
        raise SystemExit('Evidence must be UTF8 XML')
    if re.search(r'<!\s*(?:DOCTYPE|ENTITY)\b', text, re.I):
        raise SystemExit('DTD/entity declarations are forbidden')
    return ET.fromstring(text)

def guid(value):
    try:
        parsed = uuid.UUID(value)
    except (ValueError, TypeError, AttributeError):
        raise SystemExit('Missing native GUID identity')
    if parsed.int == 0 or str(parsed) != value:
        raise SystemExit('Native GUID must be canonical nonzero lowercase D format')
    return value

doc = bounded_xml(reports[0], 32 * 1024 * 1024)
if doc.tag != '{' + ns['t'] + '}TestRun':
    raise SystemExit('Require exact namespaced TestRun')
summary = doc.findall('./t:ResultSummary', ns)
if len(summary) != 1 or sum(node.tag.split('}')[-1] == 'ResultSummary' for node in doc.iter()) != 1 or summary[0].get('outcome') != 'Completed':
    raise SystemExit('Require one direct Completed ResultSummary globally')
counter_nodes = summary[0].findall('./t:Counters', ns)
if len(counter_nodes) != 1 or sum(node.tag.split('}')[-1] == 'Counters' for node in doc.iter()) != 1:
    raise SystemExit('Require one direct Counters globally')
definition_nodes = doc.findall('./t:TestDefinitions', ns)
result_nodes = doc.findall('./t:Results', ns)
if len(definition_nodes) != 1 or len(result_nodes) != 1:
    raise SystemExit('Require unique direct definitions/results containers')
if any(node.tag != '{' + ns['t'] + '}UnitTest' for node in definition_nodes[0]) or any(node.tag != '{' + ns['t'] + '}UnitTestResult' for node in result_nodes[0]):
    raise SystemExit('Unexpected native definition/result node')
definitions = {}
definition_executions = set()
for unit in doc.findall('./t:TestDefinitions/t:UnitTest', ns):
    methods = unit.findall('t:TestMethod', ns)
    execution_nodes = unit.findall('t:Execution', ns)
    if len(methods) != 1 or len(execution_nodes) != 1:
        raise SystemExit('Missing unique native test definition')
    test_id = guid(unit.get('id'))
    execution_id = guid(execution_nodes[0].get('id'))
    identity = methods[0].get('className', '') + '.' + methods[0].get('name', '')
    if test_id in definitions or execution_id in definition_executions:
        raise SystemExit('Duplicate native definition identity')
    definition_executions.add(execution_id)
    definitions[test_id] = (identity, execution_id, unit.get('name'))
actual = collections.Counter()
executions, names, used = set(), set(), set()
results = doc.findall('./t:Results/t:UnitTestResult', ns)
for result in results:
    test_id = guid(result.get('testId'))
    definition = definitions.get(test_id)
    identity = definition[0] if definition else None
    name = result.get('testName', '')
    execution = guid(result.get('executionId'))
    if not identity or not execution or execution in executions or name in names or result.get('outcome') != 'Passed':
        raise SystemExit('Missing, duplicate or nonpassing native execution')
    if not (name == identity or name.startswith(identity + '(')):
        raise SystemExit('Result contradicts native test method')
    if definition[1] != execution or definition[2] != name:
        raise SystemExit('Execution/name differs from native definition')
    if identity in expected:
        actual[identity] += 1
    elif not full:
        raise SystemExit('Unexpected focused method: ' + identity)
    executions.add(execution)
    names.add(name)
    used.add(result.get('testId'))
if dict(actual) != expected or used != set(definitions) or len(results) != total:
    raise SystemExit('Exact native execution inventory mismatch')
counters = counter_nodes[0]
required = {key: total for key in ['total', 'executed', 'passed']}
required.update({key: 0 for key in ['failed', 'error', 'timeout', 'aborted', 'inconclusive', 'passedButRunAborted',
 'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed', 'inProgress', 'pending']})
if set(counters.attrib) != set(required) or any(counters.get(key) != str(value) for key, value in required.items()):
    raise SystemExit('All 16 counters must confirm actual passes without skips')
raw = list(root.rglob('coverage.cobertura.xml'))
digests = {hashlib.sha256(bounded_bytes(path, 64 * 1024 * 1024)).hexdigest() for path in raw}
if len(digests) != 1 or len(raw) != 2:
    raise SystemExit('Require two identical generated-inclusive raw coverage copies')
coverage = bounded_xml(raw[0], 64 * 1024 * 1024)
if coverage.tag != 'coverage':
    raise SystemExit('Require exact coverage root')
packages = coverage.findall('./packages/package')
for assembly in ['Api', 'Application', 'Data', 'Domain']:
    if not any(p.get('name') == 'Legacy.Maliev.AccountingService.' + assembly and p.findall('.//line') for p in packages):
        raise SystemExit('Missing owned executable coverage inventory: ' + assembly)
proof = {'actualPassed': total, 'fileMetadataActualPassed': 46, 'failed': 0, 'skipped': 0,
 'methods': dict(actual), 'trxSha256': hashlib.sha256(bounded_bytes(reports[0], 32 * 1024 * 1024)).hexdigest(),
 'rawSha256': next(iter(digests)), 'rawCopies': len(raw), 'exclusions': [],
 'actualProducerJoinProven': False, 'fullSuiteCoverageFloor': 'separate existing coverage gate' if full else False}
(root / 'file-metadata-proof.json').write_text(json.dumps(proof, indent=2) + '\n')
print(json.dumps(proof, indent=2))
