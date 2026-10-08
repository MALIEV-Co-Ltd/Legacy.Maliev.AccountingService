"""Producer transition controls with fake daemon; not native custody proof."""
import ast,copy,json,secrets,unittest
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
        producer.network={'id':'e'*64,'name':'accounting-run'}
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
                           NetworkSettings={'Ports':{},'Networks':{producer.network['name']:{'NetworkID':producer.network['id'],'Aliases':None,'Links':None}}},State={'Running':False},Mounts=[],Image='sha256:'+'c'*64,
                           Created=datetime.now(timezone.utc).isoformat())
            return {'Id':'b'*64}
        producer.docker=SimpleNamespace(create=create,inspect=lambda x:current)
        request={'Image':'postgres:18-alpine','Env':['POSTGRES_PASSWORD='+secrets.token_hex(24)],
                 'ExposedPorts':{'5432/tcp':{}},'HostConfig':{'PortBindings':{'5432/tcp':[{'HostIp':'','HostPort':''}]}}}
        return producer,request,events,persist
    def test_actual_producer_dispatch_order(self):
        producer,request,events,persist=self.producer()
        with patch.object(f,'write_new',persist):producer.create(request)
        self.assertEqual([x[0] for x in events],['persist','dispatch','persist'])
        self.assertFalse(producer.uncertain)
        self.assertNotIn(request['Env'][0].partition('=')[2],json.dumps([x[2] for x in events if x[0]=='persist']))
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

    def test_foreign_network_name_and_id_refused_before_dispatch(self):
        for mode in ('foreign-network','d'*64,'host','none','container:foreign',None):
            producer,request,events,persist=self.producer();request['HostConfig']['NetworkMode']=mode
            with patch.object(f,'write_new',persist),self.assertRaises(ValueError):producer.create(request)
            self.assertEqual(events,[])
    def test_approved_default_network_modes_retained(self):
        for mode in ('','default','bridge'):
            producer,request,events,persist=self.producer();request['HostConfig']['NetworkMode']=mode
            with patch.object(f,'write_new',persist):producer.create(request)
            self.assertEqual(events[1][0],'dispatch')
            self.assertEqual(producer.docker.inspect('x')['HostConfig']['NetworkMode'],producer.network['id'])
    def test_sdk_root_primary_gid_refused(self):
        import accounting_owned_sdk_v3_draft as sdk
        with self.assertRaises(ValueError):sdk.validate_sdk_identity(1001,0,[1001])
    def test_sdk_root_supplementary_gid_refused(self):
        import accounting_owned_sdk_v3_draft as sdk
        with self.assertRaises(ValueError):sdk.validate_sdk_identity(1001,1001,[1001,0])
    def test_sdk_root_uid_refused(self):
        import accounting_owned_sdk_v3_draft as sdk
        with self.assertRaises(ValueError):sdk.validate_sdk_identity(0,1001,[1001])
    def test_sdk_invalid_group_ids_refused(self):
        import accounting_owned_sdk_v3_draft as sdk
        for gid,groups in ((-1,[1001]),(1001,[-1]),(1001,[True]),(True,[1001])):
            with self.assertRaises(ValueError):sdk.validate_sdk_identity(1001,gid,groups)
    def test_sdk_empty_or_excessive_groups_refused(self):
        import accounting_owned_sdk_v3_draft as sdk
        for groups in ([],[1001]*33):
            with self.assertRaises(ValueError):sdk.validate_sdk_identity(1001,1001,groups)
    def test_sdk_positive_nonroot_identity_retained(self):
        import accounting_owned_sdk_v3_draft as sdk
        sdk.validate_sdk_identity(1001,1001,[1001,1002])

    def network_fixture(self):
        from accounting_docker_transport_v3_draft import verify_owned_network
        expiry=datetime.now(timezone.utc)+timedelta(minutes=2);run='run'
        cap={'id':'e'*64,'name':'accounting-'+run,'created':'2026-10-08T00:00:00Z','expiresUtc':expiry.isoformat()}
        value={'Id':cap['id'],'Name':cap['name'],'Created':cap['created'],'Driver':'bridge','Scope':'local',
               'Internal':True,'Attachable':False,'Ingress':False,'Containers':{},
               'Labels':{'accounting-owner.task':f.OWNER,'accounting-owner.run':run,'accounting-owner.expires':cap['expiresUtc'],'accounting-owner.persistent':'false'}}
        return verify_owned_network,value,cap,expiry,run
    def test_exact_private_owned_network_profile(self):
        verify,value,cap,expiry,run=self.network_fixture();verify(value,cap,f.OWNER,run,expiry,set())
    def test_foreign_network_identity_or_birth_refused(self):
        for key,value in (('Id','f'*64),('Name','foreign'),('Created','different')):
            verify,row,cap,expiry,run=self.network_fixture();row[key]=value
            with self.assertRaises(ValueError):verify(row,cap,f.OWNER,run,expiry,set())
    def test_shared_or_external_network_profile_refused(self):
        for key,value in (('Internal',False),('Attachable',True),('Ingress',True),('Driver','overlay'),('Scope','swarm')):
            verify,row,cap,expiry,run=self.network_fixture();row[key]=value
            with self.assertRaises(ValueError):verify(row,cap,f.OWNER,run,expiry,set())
    def test_foreign_network_owner_labels_refused(self):
        verify,row,cap,expiry,run=self.network_fixture();row['Labels']['accounting-owner.task']='other'
        with self.assertRaises(ValueError):verify(row,cap,f.OWNER,run,expiry,set())
    def test_foreign_network_endpoint_refused(self):
        verify,row,cap,expiry,run=self.network_fixture();row['Containers']={'f'*64:{}}
        with self.assertRaises(ValueError):verify(row,cap,f.OWNER,run,expiry,set())
    def test_known_owned_network_endpoint_retained(self):
        verify,row,cap,expiry,run=self.network_fixture();row['Containers']={'f'*64:{}}
        verify(row,cap,f.OWNER,run,expiry,{'f'*64})
    def test_unrenewed_network_expiry_required(self):
        verify,row,cap,expiry,run=self.network_fixture();cap['expiresUtc']=(expiry+timedelta(seconds=1)).isoformat()
        with self.assertRaises(ValueError):verify(row,cap,f.OWNER,run,expiry,set())

    def test_foreign_endpoint_config_refused_with_private_hostmode(self):
        producer,request,events,persist=self.producer()
        request['HostConfig']['NetworkMode']=producer.network['id']
        request['NetworkingConfig']={'EndpointsConfig':{'foreign':{}}}
        with patch.object(f,'write_new',persist),self.assertRaises(ValueError):producer.create(request)
        self.assertEqual(events,[])
    def created_profile(self):
        producer,request,events,persist=self.producer()
        with patch.object(f,'write_new',persist):producer.create(request)
        child=next(iter(producer.intents));published=producer.publications[child]
        return producer,producer.intents[child],published['id'],producer.docker.inspect(published['id'])
    def test_actual_foreign_attachment_refused_with_private_hostmode(self):
        producer,intent,identifier,row=self.created_profile()
        row['NetworkSettings']['Networks']['foreign']={'NetworkID':'f'*64}
        self.assertEqual(row['HostConfig']['NetworkMode'],producer.network['id'])
        with self.assertRaises(ValueError):producer.verify_profile(intent,identifier,row)
    def test_actual_network_id_alias_refused(self):
        producer,intent,identifier,row=self.created_profile()
        row['NetworkSettings']['Networks'][producer.network['name']]['NetworkID']='f'*64
        with self.assertRaises(ValueError):producer.verify_profile(intent,identifier,row)
    def test_actual_foreign_endpoint_alias_or_link_refused(self):
        for key,value in (('Aliases',['foreign-alias']),('Links',['foreign-container'])):
            producer,intent,identifier,row=self.created_profile()
            row['NetworkSettings']['Networks'][producer.network['name']][key]=value
            with self.assertRaises(ValueError):producer.verify_profile(intent,identifier,row)

if __name__=='__main__':unittest.main(verbosity=2)
