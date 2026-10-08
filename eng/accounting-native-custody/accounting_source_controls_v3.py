"""Only synthetic source controls. Never calls SDK/Docker/cgroup/proxy launchers."""
from pathlib import Path
import argparse,hashlib,json,os,unittest

MODULES={'test_accounting_v3_guard_core':39,'test_accounting_fixture_producer_v3_draft':9,
         'test_accounting_private_proxy_routes_v3_draft':20,'test_accounting_native_trx_v3':17}

def main():
    parser=argparse.ArgumentParser();parser.add_argument('--receipt',required=True);args=parser.parse_args()
    if os.environ.get('ACCOUNTING_NATIVE_ENABLED','false')!='false':raise ValueError('Source lane forbids native activation')
    root=Path(__file__).resolve().parent
    for path in root.glob('*.py'):compile(path.read_bytes(),str(path),'exec')
    loader=unittest.TestLoader();suite=unittest.TestSuite();inventory={}
    for module,count in MODULES.items():
        tests=loader.loadTestsFromName(module)
        if tests.countTestCases()!=count:raise ValueError('Exact fake/source control inventory changed')
        inventory[module]=count;suite.addTests(tests)
    result=unittest.TextTestRunner(verbosity=2).run(suite)
    receipt={'scope':'synthetic-source-controls-only','testsRun':result.testsRun,'passed':result.testsRun-len(result.errors)-len(result.failures)-len(result.skipped),
             'errors':len(result.errors),'failures':len(result.failures),'skipped':len(result.skipped),'inventory':inventory,
             'files':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(root.glob('*.py'))},
             'sdkStarted':False,'dockerStarted':False,'nativeRuntimeProven':False,'nativeAccepted':False}
    destination=Path(args.receipt)
    with destination.open('xb') as stream:stream.write(json.dumps(receipt,indent=2).encode());stream.flush();os.fsync(stream.fileno())
    if not result.wasSuccessful() or result.skipped or result.testsRun!=85:raise SystemExit(1)

if __name__=='__main__':main()
