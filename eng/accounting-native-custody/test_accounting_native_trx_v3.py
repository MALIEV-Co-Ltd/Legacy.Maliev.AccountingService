"""Synthetic TRX source controls, never native execution evidence."""
import copy,hashlib,tempfile,unittest,uuid
from pathlib import Path
import xml.etree.ElementTree as ET
import accounting_native_trx_v3 as v
from accounting_v3_guard_core import PREFIX,ASSEMBLY

class Controls(unittest.TestCase):
    def verify(self,mutate=lambda root:None):
        with tempfile.TemporaryDirectory(prefix='accounting-trx-source-control-') as directory:
            root=Path(directory);assembly=root/ASSEMBLY;assembly.write_bytes(b'synthetic-source-control-not-native')
            namespace=v.NS['t'];tag=lambda x:'{'+namespace+'}'+x
            document=ET.Element(tag('TestRun'));definitions=ET.SubElement(document,tag('TestDefinitions'));results=ET.SubElement(document,tag('Results'))
            for identity,count in v.FOCUS.items():
                class_name,method=identity.rsplit('.',1)
                for case in range(count):
                    test_id=str(uuid.uuid4());execution=str(uuid.uuid4());display=PREFIX+identity+'(case: '+str(case)+')'
                    definition=ET.SubElement(definitions,tag('UnitTest'),id=test_id,name=display)
                    ET.SubElement(definition,tag('Execution'),id=execution)
                    ET.SubElement(definition,tag('TestMethod'),className=PREFIX+class_name,name=method,codeBase=str(assembly),adapterTypeName='executor://xunit/VsTestRunner3/netcore/')
                    ET.SubElement(results,tag('UnitTestResult'),testId=test_id,executionId=execution,testName=display,outcome='Passed')
            summary=ET.SubElement(document,tag('ResultSummary'),outcome='Completed')
            counters={key:'0' for key in ('total','executed','passed','failed','error','timeout','aborted','inconclusive','passedButRunAborted','notRunnable','notExecuted','disconnected','warning','completed','inProgress','pending')}
            counters.update(total='12',executed='12',passed='12');ET.SubElement(summary,tag('Counters'),**counters)
            mutate(document);path=root/'source-only.trx';path.write_bytes(ET.tostring(document))
            return v.verify_native(path,assembly,hashlib.sha256(assembly.read_bytes()).hexdigest(),True)
    def test_all12_full_structural_identity(self):self.assertEqual(self.verify()['passed'],12)
    def test_proof_never_admits_native(self):self.assertFalse(self.verify()['nativeAccepted'])
    def test_foreign_namespace_refused(self):
        with self.assertRaises(ValueError):self.verify(lambda d:d.find('.//t:TestMethod',v.NS).set('className','Synthetic.Wrong.Example'))
    def test_foreign_assembly_refused(self):
        with self.assertRaises(ValueError):self.verify(lambda d:d.find('.//t:TestMethod',v.NS).set('codeBase','/foreign/Other.dll'))
    def test_skipped_counter_refused(self):
        with self.assertRaises(ValueError):self.verify(lambda d:d.find('.//t:Counters',v.NS).set('notExecuted','1'))
    def test_missing_counter_refused(self):
        with self.assertRaises(ValueError):self.verify(lambda d:d.find('.//t:Counters',v.NS).attrib.pop('pending'))
    def test_execution_alias_refused(self):
        def mutate(d):
            rows=d.findall('./t:Results/t:UnitTestResult',v.NS);rows[1].set('executionId',rows[0].get('executionId'))
        with self.assertRaises(ValueError):self.verify(mutate)
    def test_duplicate_summary_refused(self):
        with self.assertRaises(ValueError):self.verify(lambda d:d.append(copy.deepcopy(d.find('t:ResultSummary',v.NS))))
    def test_definition_result_mismatch_refused(self):
        with self.assertRaises(ValueError):self.verify(lambda d:d.find('./t:TestDefinitions/t:UnitTest/t:Execution',v.NS).set('id',str(uuid.uuid4())))

class FullRosterControls(unittest.TestCase):
    def verify_full(self,mode='valid'):
        import json
        with tempfile.TemporaryDirectory(prefix='accounting-full-roster-source-control-') as directory:
            root=Path(directory);assembly=root/ASSEMBLY;assembly.write_bytes(b'synthetic-reviewed-assembly-not-native')
            assembly_sha=hashlib.sha256(assembly.read_bytes()).hexdigest()
            methods={PREFIX+'Independent.First':1661,PREFIX+'Independent.Second':1}
            cases=[method+'(case: '+str(index)+')' for method,count in methods.items() for index in range(count)]
            inventory={'schemaVersion':1,'assemblySha256':assembly_sha,'sourceReviewSha256':'81f3d42995bcbac1958746d8a80e620c417dfab4fea09351207030ece0e6ef0e','methods':methods,'caseNames':cases}
            inventory_path=root/'reviewed-inventory.json';inventory_raw=json.dumps(inventory).encode();inventory_path.write_bytes(inventory_raw)
            inventory_sha=hashlib.sha256(inventory_raw).hexdigest()
            namespace=v.NS['t'];tag=lambda name:'{'+namespace+'}'+name
            document=ET.Element(tag('TestRun'));definitions=ET.SubElement(document,tag('TestDefinitions'));results=ET.SubElement(document,tag('Results'))
            actual_methods=methods if mode!='counterfeit' else {PREFIX+'Invented.SingleMethod':1662}
            for method,count in actual_methods.items():
                class_name,name=method.rsplit('.',1)
                for index in range(count):
                    display=method+'(case: '+str(index)+')';test_id=str(uuid.uuid4());execution=str(uuid.uuid4())
                    definition=ET.SubElement(definitions,tag('UnitTest'),id=test_id,name=display)
                    ET.SubElement(definition,tag('Execution'),id=execution)
                    ET.SubElement(definition,tag('TestMethod'),className=class_name,name=name,codeBase=str(assembly),adapterTypeName='executor://xunit/VsTestRunner3/netcore/')
                    ET.SubElement(results,tag('UnitTestResult'),testId=test_id,executionId=execution,testName=display,outcome='Passed')
            summary=ET.SubElement(document,tag('ResultSummary'),outcome='Completed')
            counters=dict.fromkeys(('total','executed','passed','failed','error','timeout','aborted','inconclusive','passedButRunAborted','notRunnable','notExecuted','disconnected','warning','completed','inProgress','pending'),'0')
            counters.update(total='1662',executed='1662',passed='1662');ET.SubElement(summary,tag('Counters'),**counters)
            trx=root/'submitted.trx';trx.write_bytes(ET.tostring(document))
            if mode=='missing':inventory_path=None;inventory_sha=None
            if mode=='same-trx':inventory_path=trx;inventory_sha=hashlib.sha256(trx.read_bytes()).hexdigest()
            if mode=='seal':inventory_sha='0'*64
            if mode in ('assembly','multiplicity','case'):
                if mode=='assembly':inventory['assemblySha256']='0'*64
                if mode=='multiplicity':inventory['methods']={PREFIX+'Independent.First':1660,PREFIX+'Independent.Second':2}
                if mode=='case':inventory['caseNames'][-1]=PREFIX+'Independent.Second(case: substituted)'
                inventory_raw=json.dumps(inventory).encode();inventory_path.write_bytes(inventory_raw);inventory_sha=hashlib.sha256(inventory_raw).hexdigest()
            return v.verify_native(trx,assembly,assembly_sha,False,inventory_path,inventory_sha)
    def test_valid_independent_full_roster(self):self.assertEqual(self.verify_full()['passed'],1662)
    def test_invented_single_method1662_refused(self):
        with self.assertRaises(ValueError):self.verify_full('counterfeit')
    def test_full_roster_missing_refused(self):
        with self.assertRaises(ValueError):self.verify_full('missing')
    def test_trx_as_inventory_refused(self):
        with self.assertRaises(ValueError):self.verify_full('same-trx')
    def test_inventory_seal_substitution_refused(self):
        with self.assertRaises(ValueError):self.verify_full('seal')
    def test_inventory_assembly_substitution_refused(self):
        with self.assertRaises(ValueError):self.verify_full('assembly')
    def test_inventory_multiplicity_substitution_refused(self):
        with self.assertRaises(ValueError):self.verify_full('multiplicity')
    def test_inventory_case_substitution_refused(self):
        with self.assertRaises(ValueError):self.verify_full('case')

if __name__=='__main__':unittest.main(verbosity=2)
