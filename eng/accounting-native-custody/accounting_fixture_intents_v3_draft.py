"""Actual pre-dispatch intent/ack producer draft; no proxy or native launch caller.

Docker transport and private SDK request attribution must be independently bound
before this component can be integrated. Raw request/env never enter receipts.
"""
from datetime import datetime,timezone
from pathlib import Path
import copy,hashlib,json,os,re,uuid
from accounting_owned_sdk_v3_draft import write_new,identity,same_path,OWNER
from accounting_v3_guard_core import unique_json,registered_fixture_absence

PROFILES={'postgres:18-alpine':{'memory':512*1024**2,'cpu':1_000_000_000,'mount':'/var/lib/postgresql','tmpfs':'rw,size=512m','port':'5432/tcp'},
          'redis:7.4-alpine':{'memory':128*1024**2,'cpu':500_000_000,'mount':'/data','tmpfs':'rw,size=64m','port':'6379/tcp'}}
ID=re.compile(r'[0-9a-f]{64}')

class FixtureIntentProducer:
    def __init__(self,root,run,daemon,images,lease,docker):
        uuid.UUID(run)
        if not daemon or set(images)!=set(PROFILES) or any(not re.fullmatch(r'.+@sha256:[0-9a-f]{64}',x) for x in images.values()):
            raise ValueError('Reviewed exact Accounting daemon/image digests required')
        if docker.owner!=OWNER or docker.run!=run or docker.daemon!=daemon:
            raise ValueError('Foreign Docker transport ownership refused')
        self.owner=OWNER;self.run=run;self.daemon=daemon;self.images=dict(images);self.lease=lease;self.docker=docker
        self.root=Path(root);self.fd=None;self.intents={};self.publications={};self.uncertain=set()
        self.proxy=None;self.private_endpoint='unix://'+str(self.root/'docker.sock')
        self.fd=os.open(self.root,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        if list(self.root.iterdir()):self.close();raise ValueError('Exclusive fresh intent directory required')
    def close(self):
        try:
            if self.proxy is not None:self.proxy.close()
        finally:
            try:
                if self.fd is not None:os.close(self.fd);self.fd=None
            finally:
                if hasattr(self.docker,'close'):self.docker.close()
    def start_proxy(self,group,group_fd,uid,gid):
        from accounting_private_proxy_server_v3_draft import OwnedPrivateProxy
        self.check()
        if self.proxy is not None:raise ValueError('Private proxy already registered')
        same_path(self.root,self.fd)
        os.fchown(self.fd,0,gid);os.fchmod(self.fd,0o750)
        self.docker.group=Path(group);self.docker.group_fd=group_fd
        self.proxy=OwnedPrivateProxy(self.root/'docker.sock',self,group,group_fd,uid,gid,self.lease)
        self.proxy.start()
    def bind_sdk_group(self,group,group_fd):
        same_path(Path(group),group_fd)
        self.docker.group=Path(group);self.docker.group_fd=group_fd
    def check(self):
        if self.fd is None:raise ValueError('Intent owner handle released')
        same_path(self.root,self.fd);self.lease.check()
        if self.docker.daemon_identity()!=self.daemon:raise ValueError('Actual Docker daemon changed')
    def create(self,request):
        self.check()
        if len(self.intents)>=512:raise ValueError('Accounting fixture budget exceeded')
        body=copy.deepcopy(request)
        tag=body.get('Image')
        if tag not in PROFILES:raise ValueError('Unapproved Accounting fixture image')
        profile=PROFILES[tag]
        host=body.setdefault('HostConfig',{})
        if any(host.get(key) for key in ('Binds','Mounts','VolumesFrom','Devices','DeviceRequests')) or body.get('Volumes'):
            raise ValueError('Persistent/foreign fixture storage refused')
        if host.get('Privileged') or host.get('NetworkMode')=='host' or str(host.get('NetworkMode','')).startswith('container:') or host.get('PidMode'):
            raise ValueError('Privileged/shared fixture refused')
        if host.get('CapAdd') or host.get('SecurityOpt'):
            raise ValueError('Foreign fixture capability/security configuration')
        exposed=body.get('ExposedPorts',{})
        if set(exposed)!={profile['port']}:raise ValueError('Exact fixture exposed port required')
        ports=host.get('PortBindings',{})
        if set(ports)!={profile['port']} or len(ports[profile['port']])!=1:
            raise ValueError('Exact fixture port binding required')
        port=ports[profile['port']][0]
        if port.get('HostPort') not in ('','0'):raise ValueError('Dynamic fixture port required')
        port['HostIp']='127.0.0.1'
        host.update(Memory=profile['memory'],MemorySwap=profile['memory'],NanoCpus=profile['cpu'],PidsLimit=128,
                    AutoRemove=False,Privileged=False,Tmpfs={profile['mount']:profile['tmpfs']})
        body['Image']=self.images[tag]
        if tag.startswith('redis:'):body['Cmd']=['redis-server','--save','','--appendonly','no']
        child=str(uuid.uuid4());name='accounting-'+child
        labels=body.setdefault('Labels',{})
        if any(key.startswith('accounting-owner.') for key in labels):raise ValueError('Caller cannot impersonate fixture custody')
        labels.update({'accounting-owner.task':OWNER,'accounting-owner.run':self.run,'accounting-owner.child':child,
                       'accounting-owner.expires':self.lease.expires.isoformat(),'accounting-owner.persistent':'false'})
        intent={'owner':OWNER,'run':self.run,'childRun':child,'name':name,'daemon':self.daemon,'persistentData':False,
                'image':self.images[tag],'profile':tag,'expiresUtc':self.lease.expires.isoformat(),
                'createdUtc':datetime.now(timezone.utc).isoformat()}
        # Durable registration completes BEFORE the actual daemon Create dispatch.
        write_new(self.root/(child+'-intent.json'),intent)
        self.intents[child]=intent;self.uncertain.add(child)
        self.check()
        response=self.docker.create(name,body)
        container_id=response.get('Id','')
        if not ID.fullmatch(container_id):raise ValueError('Actual create acknowledgement ID invalid')
        if any(row['id']==container_id for row in self.publications.values()):raise ValueError('Daemon acknowledged duplicate container ID')
        observed=self.docker.inspect(container_id)
        metadata=self.verify_profile(intent,container_id,observed)
        acknowledgement={key:intent[key] for key in ('owner','run','childRun','name','daemon')}
        acknowledgement.update(id=container_id,acknowledged=True,imageId=metadata['imageId'],created=metadata['created'])
        self.check();write_new(self.root/(child+'-publication.json'),acknowledgement)
        self.publications[child]=acknowledgement;self.uncertain.remove(child)
        return response
    def verify_profile(self,intent,container_id,observed):
        if type(observed)!=dict or observed.get('Id')!=container_id or observed.get('Name')!='/'+intent['name']:
            raise ValueError('Actual fixture ID/name differs')
        config=observed['Config'];host=observed['HostConfig'];profile=PROFILES[intent['profile']]
        labels=config['Labels']
        expected={'accounting-owner.task':OWNER,'accounting-owner.run':self.run,'accounting-owner.child':intent['childRun'],
                  'accounting-owner.expires':intent['expiresUtc'],'accounting-owner.persistent':'false'}
        if any(labels.get(key)!=value for key,value in expected.items()) or config['Image']!=intent['image']:
            raise ValueError('Actual fixture labels/image ownership differs')
        if host.get('Memory')!=profile['memory'] or host.get('MemorySwap')!=profile['memory'] or host.get('NanoCpus')!=profile['cpu'] or host.get('PidsLimit')!=128:
            raise ValueError('Actual fixture resource caps differ')
        if host.get('Privileged') or host.get('AutoRemove') or host.get('Binds') or host.get('VolumesFrom') or host.get('Devices') or host.get('DeviceRequests'):
            raise ValueError('Actual fixture persistent/privileged surface differs')
        if host.get('Tmpfs')!={profile['mount']:profile['tmpfs']}:
            raise ValueError('Actual ephemeral storage bound differs')
        if any(row.get('Type')!='tmpfs' or row.get('Destination')!=profile['mount'] for row in observed.get('Mounts',[])):
            raise ValueError('Actual fixture has persistent or foreign mount')
        configured=host.get('PortBindings',{})
        if set(configured)!={profile['port']} or not configured[profile['port']] or any(row.get('HostIp')!='127.0.0.1' for row in configured[profile['port']]):
            raise ValueError('Actual fixture is not exclusively loopback')
        bindings=observed['NetworkSettings'].get('Ports',{})
        if observed['State']['Running'] and (set(bindings)!={profile['port']} or not bindings[profile['port']] or any(row.get('HostIp')!='127.0.0.1' for row in bindings[profile['port']])):
            raise ValueError('Actual running fixture port exposure differs')
        image_id=observed.get('Image','');created=observed.get('Created','')
        if not re.fullmatch(r'sha256:[0-9a-f]{64}',image_id):raise ValueError('Actual immutable image ID unavailable')
        when=datetime.fromisoformat(created.replace('Z','+00:00'))
        start=datetime.fromisoformat(intent['createdUtc'])
        if not start<=when<=self.lease.expires:raise ValueError('Actual fixture creation outside original lease')
        published=self.publications.get(intent['childRun'])
        if published and (published['imageId']!=image_id or published['created']!=created):
            raise ValueError('Actual immutable fixture birth/image changed')
        return {'imageId':image_id,'created':created}
    def cleanup_after_sdk_quiescence(self):
        """Requires transport independently verifies exact SDK quiescence first."""
        same_path(self.root,self.fd)
        if self.docker.sdk_quiescent(self.run) is not True:
            raise ValueError('Actual SDK quiescence proof missing')
        if self.proxy is not None:
            self.proxy.close()
            if self.proxy.closed is not True:raise ValueError('Private proxy actual worker/socket cleanup unverified')
        self.docker.begin_cleanup()
        if self.docker.daemon_identity()!=self.daemon:
            raise ValueError('Actual SDK quiescence/daemon proof missing')
        if self.uncertain:raise ValueError('Unacknowledged Create remains uncertain; retain ownership')
        for child,intent in self.intents.items():
            for suffix,value in (('intent',intent),('publication',self.publications[child])):
                path=self.root/(child+'-'+suffix+'.json')
                with path.open('rb') as stream:raw=stream.read(65537)
                if len(raw)>65536 or unique_json(raw)!=value:
                    raise ValueError('Actual immutable fixture receipt readback differs')
        inventory=self.docker.inventory(OWNER,self.run)
        known={row['id'] for row in self.publications.values()}
        if any(row.get('Id') not in known for row in inventory):raise ValueError('Unregistered actual backend; preserve it')
        for child,intent in self.intents.items():
            published=self.publications[child];container_id=published['id']
            if self.proxy is not None:
                with self.proxy.server.routes.container_operation(container_id):
                    self.proxy.server.routes.fence_execs(container_id)
            observed=self.docker.inspect(container_id)
            if observed==404:continue
            self.verify_profile(intent,container_id,observed)
            if self.docker.has_active_loopback_clients(container_id) is not False:
                raise ValueError('Actual backend still has readers; preserve it')
            if self.docker.daemon_identity()!=self.daemon:raise ValueError('Daemon changed before cleanup')
            self.verify_profile(intent,container_id,self.docker.inspect(container_id))
            self.docker.stop(container_id,5)
            stopped=self.docker.inspect(container_id)
            self.verify_profile(intent,container_id,stopped)
            if stopped['State']['Running'] is not False:raise ValueError('Exact backend still running')
            self.docker.remove(container_id,force=False,volumes=False)
            if self.docker.inspect(container_id)!=404:raise ValueError('Exact backend physical absence unverified')
        rows=[dict(intent,publication=self.publications[child]) for child,intent in self.intents.items()]
        return registered_fixture_absence(rows,self.docker.inventory(OWNER,self.run),self.docker.inspect,self.daemon,OWNER,self.run)['ownedBackendsAbsent']
