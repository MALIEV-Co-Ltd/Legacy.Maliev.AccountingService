"""Mutation controls for the new billing native-evidence gate."""
import json
import copy
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET

NS = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}


@unittest.skipUnless(os.environ.get('BILLING_NATIVE_EVIDENCE'), 'Requires retained actual native evidence')
class BillingInventoryControls(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name) / 'evidence'
        shutil.copytree(os.environ['BILLING_NATIVE_EVIDENCE'], self.root)
        self.trx, = self.root.rglob('*.trx')
        self.doc = ET.parse(self.trx)

    def check(self, accepted):
        self.doc.write(self.trx, encoding='utf-8', xml_declaration=True)
        result = subprocess.run([sys.executable, '-B', str(Path(__file__).with_name('verify-billing-foundation.py')),
                                 str(self.root), '--full'], capture_output=True, text=True, timeout=30)
        self.assertEqual(result.returncode == 0, accepted, result.stdout + result.stderr)

    def test_actual_retained_native_evidence_passes(self):
        self.check(True)
        proof = json.loads((self.root / 'billing-foundation-proof.json').read_text())
        self.assertEqual((proof['actualPassed'], proof['billingActualPassed'], proof['skipped']), (1714, 62, 0))

    def test_nonpassing_billing_result_is_refused(self):
        result = next(r for r in self.doc.findall('./t:Results/t:UnitTestResult', NS)
                      if '.Billing' in r.get('testName', ''))
        result.set('outcome', 'NotExecuted')
        self.check(False)

    def test_missing_billing_execution_is_refused(self):
        results = self.doc.find('./t:Results', NS)
        result = next(r for r in results if '.Billing' in r.get('testName', ''))
        results.remove(result)
        self.check(False)

    def test_duplicate_execution_identity_is_refused(self):
        results = self.doc.findall('./t:Results/t:UnitTestResult', NS)
        results[1].set('executionId', results[0].get('executionId'))
        self.check(False)

    def test_forged_pass_counter_is_refused(self):
        self.doc.find('./t:ResultSummary/t:Counters', NS).set('passed', '1700')
        self.check(False)

    def test_extra_execution_is_refused(self):
        results = self.doc.find('./t:Results', NS)
        results.append(copy.deepcopy(results[0]))
        self.check(False)

    def test_orphan_definition_is_refused(self):
        definitions = self.doc.find('./t:TestDefinitions', NS)
        definitions.remove(definitions[0])
        self.check(False)

    def test_substituted_billing_method_is_refused(self):
        unit = next(u for u in self.doc.findall('./t:TestDefinitions/t:UnitTest', NS)
                    if '.Billing' in u.find('t:TestMethod', NS).get('className', ''))
        method = unit.find('t:TestMethod', NS)
        original = method.get('className') + '.' + method.get('name')
        method.set('name', 'ForeignSubstitution')
        replacement = method.get('className') + '.ForeignSubstitution'
        unit.set('name', unit.get('name').replace(original, replacement))
        result = next(r for r in self.doc.findall('./t:Results/t:UnitTestResult', NS)
                      if r.get('testId') == unit.get('id'))
        result.set('testName', result.get('testName').replace(original, replacement))
        self.check(False)

    def test_mismatched_raw_coverage_copies_are_refused(self):
        coverage = next(self.root.rglob('coverage.cobertura.xml'))
        coverage.write_bytes(coverage.read_bytes() + b'\n')
        self.check(False)


if __name__ == '__main__':
    unittest.main()
