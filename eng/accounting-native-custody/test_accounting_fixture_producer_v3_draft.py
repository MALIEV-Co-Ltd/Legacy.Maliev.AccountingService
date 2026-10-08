"""Producer transition controls with fake daemon; not native custody proof."""
import ast,copy,json,unittest
from datetime import datetime,timedelta,timezone
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
import accounting_fixture_intents_v3_draft as f

class Controls(unittest.TestCase):
    def producer(self,fail=None):
        producer=f.FixtureIntentProducer.__new__(f.FixtureIntentProducer)
        producer.owner=f.OWNER;producer.run='run';producer.daemon='daemon';producer.root=Path('/evidence')
        producer.lease=SimpleNamespace(expires=datetime.now(timezone.utc)+timedelta(minutes=2))
        producer.images={tag:tag.split(':')[0]+'@sha256:'+'a'*64 for tag in f.PROFILES}
        producer.intents={};producer.publications={};producer.uncertain=set();producer.check=lambda:None
        events=[];current={}
        def persist(path,value):
            events.append(('persist',path.name,copy.deepcopy(value)))
            if fail=='save' and path.name.endswith('publication.json'):raise OSError('publication uncertain')
        def create(name,body):
            events.append(('dispatch',name))
            self.assertTrue(events[0][1].endswith('intent.json'))
            self.assertEqual(len(producer.intents),1)
            if fail=='create':raise TimeoutError('Create acknowledgement lost')
            current.update(Id='b'*64,Name='/'+name,Config=copy.deepcopy(body),HostConfig=copy.deepcopy(body['HostConfig']),
                           NetworkSettings={'Ports':{}},State={'Running':False},Mounts=[],Image='sha256:'+'c'*64,
                           Created=datetime.now(timezone.utc).isoformat())
            return {'Id':'b'*64}
        producer.docker=SimpleNamespace(create=create,inspect=lambda x:current)
        request={'Image':'postgres:18-alpine','Env':['POSTGRES_PASSWORD=do-not-record'],
                 'ExposedPorts':{'5432/tcp':{}},'HostConfig':{'PortBindings':{'5432/tcp':[{'HostIp':'','HostPort':''}]}}}
        return producer,request,events,persist
    def test_actual_producer_dispatch_order(self):
        producer,request,events,persist=self.producer()
        with patch.object(f,'write_new',persist):producer.create(request)
        self.assertEqual([x[0] for x in events],['persist','dispatch','persist'])
        self.assertFalse(producer.uncertain)
        self.assertNotIn('do-not-record',json.dumps([x[2] for x in events if x[0]=='persist']))
        self.assertEqual(events[-1][2]['childRun'],events[0][2]['childRun'])
    def test_create_uncertainty_retained(self):
        producer,request,events,persist=self.producer('create')
        with patch.object(f,'write_new',persist),self.assertRaises(TimeoutError):producer.create(request)
        self.assertEqual(len(producer.uncertain),1);self.assertFalse(producer.publications)
    def test_publication_save_uncertainty_retained(self):
        producer,request,events,persist=self.producer('save')
        with patch.object(f,'write_new',persist),self.assertRaises(OSError):producer.create(request)
        self.assertEqual(len(producer.uncertain),1);self.assertFalse(producer.publications)
    def test_persistent_mount_refused_before_dispatch(self):
        producer,request,events,persist=self.producer();request['HostConfig']['Binds']=['data:/data']
        with patch.object(f,'write_new',persist),self.assertRaises(ValueError):producer.create(request)
        self.assertEqual(events,[])
    def test_unknown_image_refused_before_dispatch(self):
        producer,request,events,persist=self.producer();request['Image']='foreign:latest'
        with patch.object(f,'write_new',persist),self.assertRaises(ValueError):producer.create(request)
        self.assertEqual(events,[])
    def test_host_network_refused_before_dispatch(self):
        producer,request,events,persist=self.producer();request['HostConfig']['NetworkMode']='container:foreign'
        with patch.object(f,'write_new',persist),self.assertRaises(ValueError):producer.create(request)
        self.assertEqual(events,[])
    def test_supervisor_has_no_top_level_launch(self):
        source=(Path(__file__).parent/'accounting_owned_sdk_v3_draft.py').read_bytes();tree=ast.parse(source)
        self.assertFalse(any(isinstance(n,ast.Expr) and isinstance(n.value,ast.Call) and isinstance(n.value.func,ast.Name) and n.value.func.id=='run_owned_sdk' for n in tree.body))
    def test_transport_source_parses_without_launch(self):
        tree=ast.parse((Path(__file__).parent/'accounting_docker_transport_v3_draft.py').read_bytes())
        self.assertFalse(any(isinstance(n,ast.Expr) and isinstance(n.value,ast.Call) for n in tree.body))
    def test_log_source_parses_without_launch(self):
        tree=ast.parse((Path(__file__).parent/'accounting_sdk_logs_v3_draft.py').read_bytes())
        self.assertFalse(any(isinstance(n,ast.Expr) and isinstance(n.value,ast.Call) for n in tree.body))

if __name__=='__main__':unittest.main(verbosity=2)
