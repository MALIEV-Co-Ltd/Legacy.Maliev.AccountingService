"""Seal/verify bounded synthetic wire files against real Accounting source and native proof."""
import argparse
from decimal import Decimal
import hashlib
import importlib.util
import json
from pathlib import Path
import re
import subprocess

REPOSITORY = Path(__file__).resolve().parent.parent
NAMES = ('empty', 'zero', 'mixed', 'null-currency-negative', 'null-days-negative', 'repository-http')
CANONICAL = {'empty': 'invoice-empty.json', 'zero': 'invoice-zero.json', 'mixed': 'invoice-mixed.json',
    'null-currency-negative': 'invoice-null-currency.json'}
SOURCES = (
    'Legacy.Maliev.AccountingService.Application/Models/PaidInvoiceOutcomeReadback.cs',
    'Legacy.Maliev.AccountingService.Api/Controllers/InvoiceControllers.cs',
    'Legacy.Maliev.AccountingService.Api/Program.cs',
    'Legacy.Maliev.AccountingService.Data/AccountingRepository.cs',
    'Legacy.Maliev.AccountingService.Tests/Fixtures/AccountingBoundaryHttpFixture.cs',
    'Legacy.Maliev.AccountingService.Tests/PaidInvoiceOutcomeWireTests.cs',
    'scripts/paid-invoice-wire-expected.json', 'scripts/verify-paid-invoice-wire.py',
    'scripts/retain-paid-invoice-wire.py', 'scripts/test-paid-invoice-wire-reader.py', '.github/workflows/paid-invoice-wire.yml',
    '.github/workflows/_build-and-test.yml',
)


def bounded(path, maximum):
    if path.is_symlink() or not path.is_file():
        raise ValueError('Require ordinary bounded evidence file')
    with path.open('rb') as stream:
        data = stream.read(maximum + 1)
    if len(data) > maximum:
        raise ValueError('Evidence exceeds finite size bound')
    return data


def unique(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ValueError('Duplicate JSON field')
        result[key] = value
    return result


def load(data):
    return json.loads(data.decode('utf-8'), object_pairs_hook=unique, parse_float=Decimal,
        parse_constant=lambda value: (_ for _ in ()).throw(ValueError('Nonfinite JSON number')))


def require(value, condition):
    if not condition:
        raise ValueError(value)


def keys(value, expected):
    require('Exact JSON object schema required', isinstance(value, dict) and set(value) == set(expected))


def validate_wire(variant, data):
    require('Unknown synthetic wire variant', variant in NAMES)
    document = load(data)
    keys(document, ('FromUtc', 'ToUtc') if variant == 'null-days-negative' else ('FromUtc', 'ToUtc', 'Days'))
    require('Exact exclusive UTC window required', document['FromUtc'] == '2026-08-25T17:00:00Z'
        and document['ToUtc'] == '2026-09-01T17:00:00Z')
    if variant == 'null-days-negative':
        return 'reject-malformed'
    days = document['Days']
    count = 0 if variant == 'empty' else 2 if variant == 'repository-http' else 1
    require('Exact bounded day inventory required', isinstance(days, list) and len(days) == count)
    for index, day in enumerate(days):
        keys(day, ('DayUtc', 'PaidInvoiceCount', 'SourceAttributedPaidInvoiceCount',
            'UnattributedPaidInvoiceCount', 'PaidInvoiceAmountsByCurrency'))
        require('Actual producer UTC day required', day['DayUtc'] == ('2026-08-26T00:00:00Z' if index == 0 else '2026-08-27T00:00:00Z'))
        counts = (0, 0, 0) if variant == 'zero' else (3, 1, 2) if variant == 'mixed' or (variant == 'repository-http' and index == 0) else (1, 0, 1)
        actual = [day[name] for name in ('PaidInvoiceCount', 'SourceAttributedPaidInvoiceCount', 'UnattributedPaidInvoiceCount')]
        require('Exact integral attribution counts required', all(type(value) is int for value in actual) and tuple(actual) == counts)
        amounts = day['PaidInvoiceAmountsByCurrency']
        expected = [] if variant == 'zero' else [('THB', Decimal('120.5000'), 2), ('USD', Decimal('25.01'), 1)] if counts == (3, 1, 2) else [(None if variant == 'null-currency-negative' else 'UNSPECIFIED', Decimal('0.0'), 1)]
        require('Exact bounded currency inventory required', isinstance(amounts, list) and len(amounts) == len(expected))
        for item, (currency, total, paid) in zip(amounts, expected):
            keys(item, ('PaidInvoiceTotal', 'PaidInvoiceCount') if currency is None else ('Currency', 'PaidInvoiceTotal', 'PaidInvoiceCount'))
            require('Currency must match actual wire contract', currency is None or item['Currency'] == currency)
            require('Exact decimal total required', type(item['PaidInvoiceTotal']) in (int, Decimal) and item['PaidInvoiceTotal'] == total)
            require('Exact integral currency count required', type(item['PaidInvoiceCount']) is int and item['PaidInvoiceCount'] == paid)
    if variant == 'mixed':
        require('Decimal lexical scale must be retained', b'"PaidInvoiceTotal":120.5000' in data)
    return 'reject-malformed' if variant == 'null-currency-negative' else 'valid-synthetic'


def digest(data):
    return hashlib.sha256(data).hexdigest()


def git(*arguments):
    return subprocess.check_output(['git', '-C', str(REPOSITORY), *arguments], timeout=30)


def evidence(root, head):
    require('Explicit immutable source head required', re.fullmatch('[0-9a-f]{40}', head) is not None)
    folder = root / 'paid-invoice-wire'
    require('Exact owned results subdirectory required', not folder.is_symlink() and folder.is_dir()
        and folder.resolve().parent == root.resolve())
    require('No missing or unexpected wire export', {path.name for path in folder.iterdir()} == {'invoice-' + name + '.json' for name in NAMES})
    native_path = root / 'paid-invoice-wire-proof.json'
    proof = load(bounded(native_path, 64 * 1024))
    manifest = load(git('show', head + ':scripts/paid-invoice-wire-expected.json'))
    require('Native wire inventory and proof required', proof['paidInvoiceWireActualPassed'] == 6
        and proof['failed'] == 0 and proof['skipped'] == 0 and proof['methods'] == manifest['methods']
        and proof['actualPassed'] in (6, manifest['fullForecast']) and proof['rawCopies'] == 2 and proof['exclusions'] == [])
    spec = importlib.util.spec_from_file_location('actual_wire_native_gate', REPOSITORY / 'scripts/verify-paid-invoice-wire.py')
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    actual_native = module.verify_native(root, proof['actualPassed'] != 6, manifest)
    require('Read original native TRX/raw evidence rather than trusting receipt claims', actual_native == proof)
    sources = {path: digest(git('show', head + ':' + path)) for path in SOURCES}
    wires = []
    for name in NAMES:
        filename = 'invoice-' + name + '.json'
        data = bounded(folder / filename, 16 * 1024)
        disposition = validate_wire(name, data)
        wires.append({'file': filename, 'sha256': digest(data), 'bytes': len(data), 'consumerDisposition': disposition,
            'canonicalConsumerFixture': CANONICAL.get(name), 'joinFixture': name in CANONICAL})
    return {'schemaVersion': 1, 'sourceHead': head, 'sourceTree': git('rev-parse', head + '^{tree}').decode().strip(),
        'sources': sources, 'nativeProofSha256': digest(bounded(native_path, 64 * 1024)), 'trxSha256': proof['trxSha256'],
        'rawSha256': proof['rawSha256'], 'actualPassed': proof['actualPassed'], 'wireCasesPassed': 6,
        'wires': wires, 'actualDtoAndProgramMvcSerializer': True, 'repositoryHttpCasePassed': True,
        'syntheticAggregatesOnly': True, 'actualAuthMintOrCustomerOutcomeProven': False}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('results', type=Path)
    parser.add_argument('--seal', action='store_true')
    parser.add_argument('--expected-head')
    arguments = parser.parse_args()
    root = arguments.results.resolve()
    head = arguments.expected_head or git('rev-parse', 'HEAD').decode().strip()
    expected = evidence(root, head)
    receipt = root / 'paid-invoice-wire-receipt.json'
    if arguments.seal:
        require('Seal only current exact tracked source', git('rev-parse', 'HEAD').decode().strip() == head)
        for path, sha in expected['sources'].items():
            require('Current source must equal immutable Git blob', digest(bounded(REPOSITORY / path, 2 * 1024 * 1024)) == sha)
        require('Do not overwrite existing sealed evidence', not receipt.exists())
        receipt.write_text(json.dumps(expected, indent=2) + '\n', encoding='utf-8')
    require('Exact producer/native/wire/source receipt required', load(bounded(receipt, 64 * 1024)) == expected)
    print(json.dumps({'receiptSha256': digest(bounded(receipt, 64 * 1024)), 'sourceHead': head, 'wireCasesPassed': 6,
        'actualAuthMintOrCustomerOutcomeProven': False}))


if __name__ == '__main__':
    main()
