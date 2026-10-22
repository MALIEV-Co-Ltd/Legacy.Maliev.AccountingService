"""Source controls only: no SDK, containers or native admission."""
import json, unittest
import xml.etree.ElementTree as ET
import tempfile
from datetime import datetime, timezone, timedelta
from pathlib import Path
from types import SimpleNamespace
import accounting_v3_guard_core as g

class Controls(unittest.TestCase):
    def restored(self, mutate=lambda x:None, alter=False):
        with tempfile.TemporaryDirectory(prefix='accounting-v3-source-control-') as directory:
            root=Path(directory)
            solution=ET.Element('Solution')
            for relative in sorted(g.PROJECTS):
                ET.SubElement(solution,'Project',Path=relative)
                project=root/relative;project.parent.mkdir();project.write_bytes(b'<Project/>')
                asset=project.parent/'obj'/'project.assets.json';asset.parent.mkdir()
                document={'project':{'restore':{'projectPath':str(project),'projectStyle':'PackageReference','originalTargetFrameworks':['net10.0'],'sources':{'https://api.nuget.org/v3/index.json':{}}},'frameworks':{'net10.0':{}}},'targets':{'net10.0':{}},'libraries':{'Example/1.0.0':{'type':'package'}}}
                mutate(document);asset.write_bytes(json.dumps(document).encode())
            (root/'Legacy.Maliev.AccountingService.slnx').write_bytes(ET.tostring(solution))
            expected=g.restored_audit_identity(root)
            if alter:next(iter(expected.values()))['assets'].write_bytes(b'{}')
            report={'version':1,'parameters':'--vulnerable --include-transitive','sources':['https://api.nuget.org/v3/index.json'],'projects':[{'path':path} for path in expected]}
            return g.verify_bound_vulnerability_audit(json.dumps(report),'',expected)
    def test_actual_restored_binding(self):self.assertEqual(self.restored()['projectCount'],5)
    def test_restored_asset_changed_refused(self):
        with self.assertRaises(ValueError):self.restored(alter=True)
    def test_foreign_restored_framework_refused(self):
        with self.assertRaises(ValueError):self.restored(lambda d:d['project']['restore'].update(originalTargetFrameworks=['net9.0']))
    def test_foreign_restored_source_refused(self):
        with self.assertRaises(ValueError):self.restored(lambda d:d['project']['restore'].update(sources={'https://foreign.invalid':{}}))
    def identity(self, class_name=g.PREFIX+'Example', assembly=g.ASSEMBLY, adapter='executor://xunit/VsTestRunner3/netcore/'):
        namespace='http://microsoft.com/schemas/VisualStudio/TeamTest/2010'
        root=ET.Element('{'+namespace+'}TestRun')
        definitions=ET.SubElement(root,'{'+namespace+'}TestDefinitions')
        definition=ET.SubElement(definitions,'{'+namespace+'}UnitTest',id='one')
        ET.SubElement(definition,'{'+namespace+'}TestMethod',className=class_name,name='Method',codeBase='/build/'+assembly,adapterTypeName=adapter)
        results=ET.SubElement(root,'{'+namespace+'}Results')
        ET.SubElement(results,'{'+namespace+'}UnitTestResult',testId='one',testName=class_name+'.Method')
        return g.exact_focus_identity(root,{'t':namespace},{'Example.Method':1})
    def test_exact_identity(self):self.assertEqual(self.identity(),{'Example.Method':1})
    def test_foreign_namespace_refused(self):
        with self.assertRaises(ValueError):self.identity(class_name='Synthetic.Wrong.Example')
    def test_foreign_assembly_refused(self):
        with self.assertRaises(ValueError):self.identity(assembly='Synthetic.Wrong.Assembly.dll')
    def test_foreign_adapter_refused(self):
        with self.assertRaises(ValueError):self.identity(adapter='synthetic-adapter')
    def test_assembly_suffix_refused(self):
        with self.assertRaises(ValueError):self.identity(class_name=g.PREFIX+'Example, Synthetic.Wrong.Assembly')
    def capacity(self, override=None):
        files = {'/proc/42/cgroup':'0::/parent/worker',
                 '/proc/42/mountinfo':'1 0 0:1 / /cg rw - cgroup2 cgroup rw',
                 '/proc/meminfo':'MemAvailable: 12582912 kB',
                 '/cg/parent/worker/memory.max':str(8*1024**3),
                 '/cg/parent/worker/memory.current':'0',
                 '/cg/parent/worker/cpu.max':'100000 100000',
                 '/cg/parent/worker/pids.max':'512',
                 '/cg/parent/memory.max':str(10*1024**3),
                 '/cg/parent/memory.current':'0'}
        files.update(override or {})
        return g.effective_capacity(42,read=lambda p:files[p.as_posix()],stat=lambda p:SimpleNamespace(st_dev=1,st_ino=2))
    def test_capacity_actual_leaf(self):
        self.assertEqual(Path(self.capacity()['actualCgroup']).as_posix(),'/cg/parent/worker')
    def test_low_leaf_refused(self):
        with self.assertRaises(ValueError):self.capacity({'/cg/parent/worker/memory.max':str(3*1024**3)})
    def test_low_ancestor_refused(self):
        with self.assertRaises(ValueError):self.capacity({'/cg/parent/memory.current':str(7*1024**3)})
    def test_hidden_ancestor_refused(self):
        with self.assertRaises(ValueError):self.capacity({'/proc/42/mountinfo':'1 0 0:1 /hidden /cg rw - cgroup2 cgroup rw'})
    def test_missing_bound_refused(self):
        with self.assertRaises(ValueError):self.capacity({'/cg/parent/worker/memory.max':'max'})
    def phase(self, mode):
        now=[datetime(2026,10,8,tzinfo=timezone.utc)]; ticks=[0]
        lease=g.ImmutableLease(now[0]+timedelta(seconds=10),now=lambda:now[0],clock=lambda:ticks[0])
        events=[]
        def work(guard):
            events.append('work')
            if mode=='failure':raise RuntimeError('first')
            if mode=='expiry':ticks[0]=11
            guard()
        def cleanup():
            events.append('cleanup')
            return dict.fromkeys(('sdkExited','ownedBackendsAbsent','handlesReleased','slotReleased','slotAbsent','cgroupAbsent'),mode!='cleanup')
        def save(result):
            events.append('save')
            if mode in ('save','failure'):raise OSError('save uncertain')
        try:
            result=g.settle_owned_phase(work,cleanup,save,lease,cancelled=lambda:mode=='cancel')
        except BaseException as error:
            self.assertEqual(events[-2:],['cleanup','save'])
            return error
        self.assertFalse(result['nativeAccepted'])
        return result
    def test_first_failure_preserved(self):self.assertIsInstance(self.phase('failure'),RuntimeError)
    def test_expiry_cleans(self):self.assertIsInstance(self.phase('expiry'),TimeoutError)
    def test_cancel_cleans(self):self.assertIsInstance(self.phase('cancel'),KeyboardInterrupt)
    def test_final_save_uncertainty(self):self.assertIsInstance(self.phase('save'),OSError)
    def test_cleanup_uncertainty(self):self.assertIsInstance(self.phase('cleanup'),ValueError)
    def test_settlement_does_not_admit_native(self):self.assertTrue(self.phase('ok')['phaseSettled'])
    def audit(self, modify=lambda x:None, stderr=''):
        doc={'version':1,'parameters':'--vulnerable --include-transitive','sources':['https://api.nuget.org/v3/index.json'],'projects':[{'path':'a.csproj','frameworks':[{'framework':'net10.0'}]}]}
        modify(doc)
        return g.verify_vulnerability_audit(json.dumps(doc),stderr,{'a.csproj':['net10.0']})
    def test_clean_audit(self):self.assertTrue(self.audit()['clean'])
    def test_vulnerability_exit_zero_refused(self):
        with self.assertRaises(ValueError):self.audit(lambda d:d['projects'][0]['frameworks'][0].update(transitivePackages=[{'id':'bad'}]))
    def test_audit_warning_refused(self):
        with self.assertRaises(ValueError):self.audit(lambda d:d.update(warnings=['NU1901']))
    def test_audit_stderr_refused(self):
        with self.assertRaises(ValueError):self.audit(stderr='unavailable')
    def test_audit_missing_project_refused(self):
        with self.assertRaises(ValueError):self.audit(lambda d:d.update(projects=[]))
    def fixture(self, mutate=lambda x:None, inventory=None, inspect=lambda x:404):
        intent={'childRun':'child','name':'accounting-child','owner':'commerce','run':'run','daemon':'daemon','persistentData':False,
                'publication':{'acknowledged':True,'daemon':'daemon','id':'a'*64,'childRun':'child','name':'accounting-child','owner':'commerce','run':'run'}}
        mutate(intent)
        return g.registered_fixture_absence([intent],inventory or [],inspect,'daemon','commerce','run')
    def test_acknowledged_fixture_absent(self):self.assertTrue(self.fixture()['ownedBackendsAbsent'])
    def test_unacknowledged_create_refused(self):
        with self.assertRaises(ValueError):self.fixture(lambda x:x['publication'].update(acknowledged=False))
    def test_wrong_daemon_refused(self):
        with self.assertRaises(ValueError):self.fixture(lambda x:x.update(daemon='foreign'))
    def test_persistent_fixture_refused(self):
        with self.assertRaises(ValueError):self.fixture(lambda x:x.update(persistentData=True))
    def test_existing_exact_id_refused(self):
        with self.assertRaises(ValueError):self.fixture(inspect=lambda x:200 if x=='a'*64 else 404)
    def test_unregistered_container_refused(self):
        with self.assertRaises(ValueError):self.fixture(inventory=[{'owner':'commerce','run':'run','daemon':'daemon','name':'unknown'}])
    def pair(self, mode):
        intents=[]
        for index in range(2):
            row={'childRun':'child'+str(index),'name':'fixture'+str(index),'owner':'commerce','run':'run','daemon':'daemon','persistentData':False}
            row['publication']={key:row[key] for key in ('childRun','name','owner','run','daemon')}
            row['publication'].update(acknowledged=True,id=('a' if index==0 else 'b')*64)
            intents.append(row)
        if mode=='name':
            intents[1]['name']=intents[0]['name'];intents[1]['publication']['name']=intents[0]['name']
        if mode=='id':intents[1]['publication']['id']=intents[0]['publication']['id']
        if mode=='swap':intents[0]['publication'],intents[1]['publication']=intents[1]['publication'],intents[0]['publication']
        return g.registered_fixture_absence(intents,[],lambda x:404,'daemon','commerce','run')
    def test_distinct_fixture_pair(self):self.assertEqual(self.pair('ok')['registeredCount'],2)
    def test_duplicate_fixture_name_refused(self):
        with self.assertRaises(ValueError):self.pair('name')
    def test_duplicate_fixture_id_refused(self):
        with self.assertRaises(ValueError):self.pair('id')
    def test_swapped_fixture_ack_refused(self):
        with self.assertRaises(ValueError):self.pair('swap')
    def test_fixture_ack_child_alias_refused(self):
        with self.assertRaises(ValueError):self.fixture(lambda x:x['publication'].update(childRun='other'))
    def test_fixture_ack_name_alias_refused(self):
        with self.assertRaises(ValueError):self.fixture(lambda x:x['publication'].update(name='other'))
    def test_fixture_ack_owner_alias_refused(self):
        with self.assertRaises(ValueError):self.fixture(lambda x:x['publication'].update(owner='other'))
    def test_fixture_ack_run_alias_refused(self):
        with self.assertRaises(ValueError):self.fixture(lambda x:x['publication'].update(run='other'))

if __name__=='__main__':unittest.main(verbosity=2)
