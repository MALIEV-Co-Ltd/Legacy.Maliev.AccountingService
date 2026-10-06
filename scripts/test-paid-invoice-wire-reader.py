"""Offline synthetic reader controls; these never claim native producer execution."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import uuid
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parent.parent


def module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def main():
    reader = module(ROOT / 'scripts/retain-paid-invoice-wire.py', 'wire_reader_controls')
    native = module(ROOT / 'scripts/verify-paid-invoice-wire.py', 'wire_native_controls')
    window = {'FromUtc': '2026-08-25T17:00:00Z', 'ToUtc': '2026-09-01T17:00:00Z'}
    day = {'DayUtc': '2026-08-26T00:00:00Z', 'PaidInvoiceCount': 3,
        'SourceAttributedPaidInvoiceCount': 1, 'UnattributedPaidInvoiceCount': 2,
        'PaidInvoiceAmountsByCurrency': [{'Currency': 'THB', 'PaidInvoiceTotal': 120.5, 'PaidInvoiceCount': 2},
            {'Currency': 'USD', 'PaidInvoiceTotal': 25.01, 'PaidInvoiceCount': 1}]}
    zero = dict(day, PaidInvoiceCount=0, SourceAttributedPaidInvoiceCount=0, UnattributedPaidInvoiceCount=0, PaidInvoiceAmountsByCurrency=[])
    null_currency = dict(day, PaidInvoiceCount=1, SourceAttributedPaidInvoiceCount=0, UnattributedPaidInvoiceCount=1,
        PaidInvoiceAmountsByCurrency=[{'PaidInvoiceTotal': 0.0, 'PaidInvoiceCount': 1}])
    unspecified = dict(null_currency, DayUtc='2026-08-27T00:00:00Z',
        PaidInvoiceAmountsByCurrency=[{'Currency': 'UNSPECIFIED', 'PaidInvoiceTotal': 0.0, 'PaidInvoiceCount': 1}])
    fixtures = {'empty': dict(window, Days=[]), 'zero': dict(window, Days=[zero]),
        'mixed': dict(window, Days=[day]), 'null-currency-negative': dict(window, Days=[null_currency]),
        'null-days-negative': window, 'repository-http': dict(window, Days=[day, unspecified])}

    def encode(value):
        return json.dumps(value, separators=(',', ':')).replace('120.5,', '120.5000,').encode()

    passed = []
    for name, value in fixtures.items():
        expected = 'reject-malformed' if name.endswith('-negative') else 'valid-synthetic'
        assert reader.validate_wire(name, encode(value)) == expected
        passed.append('valid-' + name)
    mutations = []
    invalid = copy.deepcopy(fixtures['mixed']); invalid['Days'][0]['DayUtc'] = '2026-08-26T00:00:00'; mutations.append(('missing-utc', encode(invalid)))
    invalid = copy.deepcopy(fixtures['mixed']); invalid['CustomerId'] = 42; mutations.append(('privacy-field', encode(invalid)))
    invalid = copy.deepcopy(fixtures['mixed']); invalid['Days'][0]['PaidInvoiceCount'] = True; mutations.append(('boolean-count', encode(invalid)))
    invalid = copy.deepcopy(fixtures['mixed']); invalid['Days'][0]['UnattributedPaidInvoiceCount'] = 1; mutations.append(('wrong-attribution', encode(invalid)))
    invalid = copy.deepcopy(fixtures['mixed']); invalid['Days'][0]['PaidInvoiceAmountsByCurrency'][0]['Currency'] = None; mutations.append(('null-currency-valid', encode(invalid)))
    invalid = copy.deepcopy(fixtures['mixed']); invalid['Days'][0]['PaidInvoiceAmountsByCurrency'][0]['PaidInvoiceTotal'] = 999; mutations.append(('wrong-total', encode(invalid)))
    invalid = copy.deepcopy(fixtures['mixed']); invalid['Days'].append(invalid['Days'][0]); mutations.append(('extra-day', encode(invalid)))
    mutations.extend([('duplicate-field', encode(fixtures['mixed']).replace(b'{', b'{"FromUtc":"duplicate",', 1)),
        ('non-utf8', b'\xff'), ('nonfinite', encode(fixtures['mixed']).replace(b'120.5000', b'NaN')),
        ('decimal-scale', json.dumps(fixtures['mixed'], separators=(',', ':')).encode())])
    for name, data in mutations:
        try:
            reader.validate_wire('mixed', data)
        except (ValueError, UnicodeError):
            passed.append('reject-' + name)
        else:
            raise AssertionError('Accepted invalid wire: ' + name)

    with tempfile.TemporaryDirectory(prefix='accounting-wire-reader-controls-') as temporary:
        repo = Path(temporary) / 'repo'; repo.mkdir()
        for source in reader.SOURCES:
            target = repo / source; target.parent.mkdir(parents=True, exist_ok=True)
            origin = ROOT / source
            target.write_bytes(origin.read_bytes().replace(b'\r\n', b'\n') if origin.exists() else b'// Offline synthetic source placeholder, never producer evidence.\n')
        for args in [('init',), ('add', '--', *reader.SOURCES), ('-c', 'user.name=Offline synthetic control', '-c', 'user.email=synthetic@invalid', '-c', 'commit.gpgsign=false', 'commit', '-m', 'Offline reader control only')]:
            subprocess.run(['git', '-C', str(repo), *args], check=True, capture_output=True, timeout=30)
        head = subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD'], timeout=30).decode().strip()
        results = repo / 'synthetic-results'; wire_folder = results / 'paid-invoice-wire'; wire_folder.mkdir(parents=True)
        for name, value in fixtures.items(): (wire_folder / ('invoice-' + name + '.json')).write_bytes(encode(value))
        manifest = json.loads((ROOT / 'scripts/paid-invoice-wire-expected.json').read_text())
        ns = 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'
        def tag(name): return '{' + ns + '}' + name
        doc = ET.Element(tag('TestRun')); definitions = ET.SubElement(doc, tag('TestDefinitions')); outcomes = ET.SubElement(doc, tag('Results'))
        for identity, count in manifest['methods'].items():
            class_name, method = identity.rsplit('.', 1)
            for case in range(count):
                test_id, execution_id = str(uuid.uuid4()), str(uuid.uuid4()); display = identity + '(' + str(case) + ')'
                unit = ET.SubElement(definitions, tag('UnitTest'), id=test_id, name=display)
                ET.SubElement(unit, tag('Execution'), id=execution_id); ET.SubElement(unit, tag('TestMethod'), className=class_name, name=method)
                ET.SubElement(outcomes, tag('UnitTestResult'), testId=test_id, executionId=execution_id, testName=display, outcome='Passed')
        summary = ET.SubElement(doc, tag('ResultSummary'), outcome='Completed')
        counts = {name: '6' for name in ('total', 'executed', 'passed')}
        counts.update({name: '0' for name in ('failed', 'error', 'timeout', 'aborted', 'inconclusive', 'passedButRunAborted', 'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed', 'inProgress', 'pending')})
        ET.SubElement(summary, tag('Counters'), **counts); ET.ElementTree(doc).write(results / 'synthetic.trx', encoding='utf-8')
        coverage = ET.Element('coverage'); packages = ET.SubElement(coverage, 'packages')
        for assembly in ('Api', 'Application', 'Data', 'Domain'):
            package = ET.SubElement(packages, 'package', name='Legacy.Maliev.AccountingService.' + assembly)
            ET.SubElement(package, 'line', number='1', hits='1')
        for name in ('raw-a', 'raw-b'):
            folder = results / name; folder.mkdir(); ET.ElementTree(coverage).write(folder / 'coverage.cobertura.xml', encoding='utf-8')
        proof_path = results / 'paid-invoice-wire-proof.json'; proof_path.write_text(json.dumps(native.verify_native(results, False, manifest), indent=2) + '\n')
        reader.REPOSITORY = repo
        receipt = reader.evidence(results, head); assert receipt['wireCasesPassed'] == 6; passed.append('synthetic-complete-reader')
        bindings = {row['file']: row for row in receipt['wires']}
        assert bindings['invoice-null-currency-negative.json']['canonicalConsumerFixture'] == 'invoice-null-currency.json'
        assert bindings['invoice-null-currency-negative.json']['sha256'] == reader.digest(encode(fixtures['null-currency-negative']))
        assert not bindings['invoice-null-days-negative.json']['joinFixture'] and not bindings['invoice-repository-http.json']['joinFixture']
        passed.append('canonical-metadata-only-unchanged-bytes')
        for name, path, data in [('wire-hash-or-schema-tamper', wire_folder / 'invoice-mixed.json', mutations[0][1]),
                ('native-trx-tamper', results / 'synthetic.trx', b'<not-native/>'),
                ('raw-copy-tamper', results / 'raw-a/coverage.cobertura.xml', b'<coverage/>'),
                ('native-proof-tamper', proof_path, b'{}')]:
            original = path.read_bytes(); path.write_bytes(data)
            try:
                reader.evidence(results, head)
            except (ValueError, KeyError, SystemExit):
                passed.append('reject-' + name)
            else:
                raise AssertionError('Accepted tampered evidence: ' + name)
            finally:
                path.write_bytes(original)
        extra = wire_folder / 'unexpected.json'; extra.write_bytes(b'{}')
        try:
            reader.evidence(results, head)
        except ValueError:
            passed.append('reject-extra-export')
        else:
            raise AssertionError('Accepted extra export')
        finally:
            extra.unlink()
        assert reader.evidence(results, head) == receipt
        cli = repo / 'scripts/retain-paid-invoice-wire.py'
        def command(*extra):
            return subprocess.run(['python', '-B', str(cli), str(results), '--expected-head', head, *extra],
                capture_output=True, timeout=30)
        assert command('--seal').returncode == 0
        assert command().returncode == 0
        passed.append('synthetic-seal-and-readback')
        receipt_path = results / 'paid-invoice-wire-receipt.json'
        original_receipt = receipt_path.read_bytes()
        forged = json.loads(original_receipt); forged['wires'][0]['sha256'] = '0' * 64
        receipt_path.write_text(json.dumps(forged))
        try:
            assert command().returncode != 0
            passed.append('reject-receipt-hash-tamper')
        finally:
            receipt_path.write_bytes(original_receipt)
        forged = json.loads(original_receipt); forged['wires'][3]['canonicalConsumerFixture'] = 'wrong-fixture.json'
        receipt_path.write_text(json.dumps(forged))
        try:
            assert command().returncode != 0
            passed.append('reject-canonical-binding-tamper')
        finally:
            receipt_path.write_bytes(original_receipt)
        source_path = repo / 'Legacy.Maliev.AccountingService.Api/Program.cs'
        original_source = source_path.read_bytes()
        receipt_path.unlink(); source_path.write_bytes(original_source + b'// untracked source drift\n')
        try:
            assert command('--seal').returncode != 0
            passed.append('reject-source-worktree-drift')
        finally:
            source_path.write_bytes(original_source)
            receipt_path.write_bytes(original_receipt)
        assert command().returncode == 0
    print(json.dumps({'status': 'PASS offline synthetic reader controls only', 'passed': len(passed), 'controls': passed,
        'nativeProducerExecutions': 0, 'temporaryRepositoriesAndFixturesRemoved': True}))


if __name__ == '__main__':
    main()
