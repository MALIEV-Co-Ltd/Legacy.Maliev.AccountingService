"""Evidence-gate mutation controls; synthetic documents are never native acceptance."""
import copy
import importlib.util
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import sys
import uuid
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location('gate', Path(__file__).with_name('verify-master-source-strings.py'))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)
namespace = gate.NS['t']
def tag(name): return '{' + namespace + '}' + name

def document():
    root = ET.Element(tag('TestRun'))
    results = ET.SubElement(root, tag('Results'))
    definitions = ET.SubElement(root, tag('TestDefinitions'))
    for method, count in gate.EXPECTED.items():
        for index in range(count):
            identity, execution = str(uuid.uuid4()), str(uuid.uuid4())
            name = 'Legacy.Maliev.AccountingService.Tests.' + method + '(' + str(index) + ')'
            ET.SubElement(results, tag('UnitTestResult'), testId=identity, executionId=execution, testName=name, outcome='Passed')
            definition = ET.SubElement(definitions, tag('UnitTest'), id=identity, name=name)
            ET.SubElement(definition, tag('Execution'), id=execution)
            entity, method_name = method.rsplit('.', 1)
            ET.SubElement(definition, tag('TestMethod'), className='Legacy.Maliev.AccountingService.Tests.' + entity, name=method_name)
    summary = ET.SubElement(root, tag('ResultSummary'), outcome='Completed')
    ET.SubElement(summary, tag('Counters'), **{name: '474' if name in {'total', 'executed', 'passed'} else '0' for name in
        ('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'inconclusive', 'passedButRunAborted',
         'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed', 'inProgress', 'pending')})
    return root

class EvidenceGateMutationTests(unittest.TestCase):
    def setUp(self): self.document = document()

    def test_complete_synthetic_control_validates_only_structure(self):
        self.assertEqual(gate.EXPECTED, gate.verify_trx(self.document))

    def test_duplicate_execution_rejected(self):
        rows = self.document.find(tag('Results'))
        rows[1].set('executionId', rows[0].get('executionId'))
        with self.assertRaises(ValueError): gate.verify_trx(self.document)

    def test_definition_display_name_mismatch_rejected(self):
        self.document.find(tag('TestDefinitions'))[0].set('name', 'unrelated')
        with self.assertRaises(ValueError): gate.verify_trx(self.document)

    def test_skipped_outcome_rejected(self):
        self.document.find(tag('Results'))[0].set('outcome', 'NotExecuted')
        with self.assertRaises(ValueError): gate.verify_trx(self.document)

    def test_missing_case_with_repaired_counters_rejected(self):
        rows = self.document.find(tag('Results'))
        rows.remove(rows[0])
        counters = self.document.find(tag('ResultSummary')).find(tag('Counters'))
        for name in ('total', 'executed', 'passed'): counters.set(name, '473')
        with self.assertRaises(ValueError): gate.verify_trx(self.document)

    def test_additional_summary_container_rejected(self):
        self.document.append(copy.deepcopy(self.document.find(tag('ResultSummary'))))
        with self.assertRaises(ValueError): gate.verify_trx(self.document)

    def test_counter_drift_rejected(self):
        self.document.find(tag('ResultSummary')).find(tag('Counters')).set('warning', '1')
        with self.assertRaises(ValueError): gate.verify_trx(self.document)

    def test_unknown_method_substituted_without_count_change_rejected(self):
        self.document.find(tag('TestDefinitions'))[0].find(tag('TestMethod')).set('name', 'UnknownMethod')
        with self.assertRaises(ValueError): gate.verify_trx(self.document)

    def test_xml_entity_declaration_rejected_before_parse(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'synthetic.trx'
            path.write_text('<!DOCTYPE TestRun [<!ENTITY injected "value">]><TestRun/>', encoding='utf-8')
            with self.assertRaises(ValueError): gate.xml(path)

class ResourceBoundTests(unittest.TestCase):
    def test_oversized_file_refused_before_open(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "large.trx"
            path.write_bytes(b"12345")
            with mock.patch.object(Path, "open", side_effect=AssertionError("must not open")):
                with self.assertRaises(ValueError): gate.bounded_read(path, directory, 4)

    def test_growth_overflow_byte_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "growing.trx"
            path.write_bytes(b"1")
            import io
            with mock.patch.object(Path, "open", return_value=io.BytesIO(b"12345")):
                with self.assertRaises(ValueError): gate.bounded_read(path, directory, 4)

    def test_many_candidates_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            for index in range(3): (Path(directory) / str(index)).write_bytes(b"1")
            with mock.patch.object(gate, "MAX_CANDIDATES", 2):
                with self.assertRaises(ValueError): gate.discover(directory)

    def test_aggregate_bytes_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            (Path(directory) / "a").write_bytes(b"123")
            with mock.patch.object(gate, "MAX_AGGREGATE", 2):
                with self.assertRaises(ValueError): gate.discover(directory)

    def test_parent_link_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "a"
            path.write_bytes(b"1")
            original = Path.lstat
            def linked_parent(candidate):
                info = original(candidate)
                if candidate == path.parent:
                    class Linked:
                        st_mode = info.st_mode
                        st_file_attributes = 0x400
                    return Linked()
                return info
            with mock.patch.object(Path, "lstat", linked_parent):
                with self.assertRaises(ValueError): gate.bounded_read(path, directory)

    def run_owned_process_failure(self, timer_failure):
        processes = []
        original = gate.subprocess.Popen
        def tracked(*args, **kwargs):
            process = original(*args, **kwargs)
            processes.append(process)
            return process
        with mock.patch.object(gate.subprocess, "Popen", tracked):
            if timer_failure:
                with mock.patch.object(gate.threading.Timer, "start", side_effect=RuntimeError("injected timer failure")):
                    with self.assertRaises(RuntimeError):
                        gate.capped_command([sys.executable, "-c", "import time; time.sleep(30)"], 4)
            else:
                with self.assertRaises(ValueError):
                    gate.capped_command([sys.executable, "-c", "import sys; sys.stdout.write('12345')"], 4)
        self.assertEqual(1, len(processes))
        self.assertIsNotNone(processes[0].poll())
        self.assertTrue(processes[0].stdout.closed)

    def test_source_output_overflow_rejected_and_process_reaped(self):
        self.run_owned_process_failure(False)

    def test_timer_start_failure_reaps_process_and_closes_pipe(self):
        self.run_owned_process_failure(True)

if __name__ == '__main__': unittest.main()
