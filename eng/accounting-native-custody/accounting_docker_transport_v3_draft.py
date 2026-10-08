"""Unenabled exact Unix Docker transport draft; never starts daemon or workloads.

No CLI/caller. All socket, peer, daemon, cgroup, image and expiry bindings must
come from independently reviewed Accounting authority. No File grant reuse.
"""
import hashlib,http.client,json,os,re,socket,struct,sys,time
from pathlib import Path
from urllib.parse import quote
from accounting_v3_guard_core import unique_json
from accounting_owned_sdk_v3_draft import birth,same_path,group_empty,OWNER

MAX_BODY=16*1024**2

def attributed_sdk_request(connection,group,group_fd,expected_uid,lease):
    """Actual connecting SDK descendant membership, birth and directory identity."""
    lease.check();same_path(group,group_fd)
    pid,uid,gid=struct.unpack('3i',connection.getsockopt(socket.SOL_SOCKET,socket.SO_PEERCRED,12))
    if uid!=expected_uid or pid<=0:raise ValueError('Private proxy SDK peer identity differs')
    started=birth(pid)
    membership=Path(f'/proc/{pid}/cgroup').read_text().splitlines()
    relative=Path(group).relative_to('/sys/fs/cgroup').as_posix()
    if membership!=['0::/'+relative] or birth(pid)!=started:
        raise ValueError('Actual proxy peer outside exact owned SDK cgroup')
    return {'pid':pid,'birthTicks':started,'uid':uid,'gid':gid}

def verify_owned_network(observed,capability,owner,run,expires,known):
    if type(capability)!=dict or set(capability)!={'id','name','created','expiresUtc'}:
        raise ValueError('Exact reviewed network capability required')
    if not re.fullmatch('[0-9a-f]{64}',capability['id']) or capability['name']!='accounting-'+run or capability['expiresUtc']!=expires.isoformat():
        raise ValueError('Owned network capability identity differs')
    expected={'accounting-owner.task':owner,'accounting-owner.run':run,
              'accounting-owner.expires':capability['expiresUtc'],'accounting-owner.persistent':'false'}
    if (type(observed)!=dict or observed.get('Id')!=capability['id'] or observed.get('Name')!=capability['name']
        or observed.get('Created')!=capability['created'] or observed.get('Driver')!='bridge'
        or observed.get('Scope')!='local' or observed.get('Internal') is not True
        or observed.get('Attachable') is not False or observed.get('Ingress') is not False
        or any(observed.get('Labels',{}).get(key)!=value for key,value in expected.items())):
        raise ValueError('Actual private network ownership/profile differs')
    containers=observed.get('Containers')
    if type(containers)!=dict or not set(containers)<=known:
        raise ValueError('Foreign or unregistered network endpoints; preserve network')
    return observed

class UnixDockerTransport:
    def __init__(self,path,socket_identity,peer_pid,peer_birth,daemon,run,group,group_fd,lease,cleanup_deadline):
        if sys.platform!='linux' or not hasattr(socket,'SO_PEERCRED'):
            raise RuntimeError('Actual Linux Unix-socket ownership required')
        self.path=Path(path);self.socket_identity=tuple(socket_identity);self.peer_pid=peer_pid;self.peer_birth=peer_birth
        self.owner=OWNER;self.daemon=daemon;self.run=run;self.group=Path(group);self.group_fd=group_fd;self.lease=lease
        # Absolute immutable cleanup expiry must be bound by the independent grant.
        if not lease.deadline<=cleanup_deadline<=lease.deadline+300:
            raise ValueError('Finite original cleanup expiry required')
        self.cleanup_deadline=cleanup_deadline;self.cleanup_mode=False;self.known=set();self.live=set()
    def guard(self):
        if self.cleanup_mode:
            if time.monotonic()>=self.cleanup_deadline or not self.sdk_quiescent(self.run):
                raise TimeoutError('Cleanup expiry/quiescence failed; preserve resources')
        else:self.lease.check()
        actual=os.stat(self.path)
        if (actual.st_dev,actual.st_ino)!=self.socket_identity or birth(self.peer_pid)!=self.peer_birth:
            raise ValueError('Actual Docker endpoint/peer birth changed')
    def rpc(self,method,path,body=None,raw_response=False,upgrade=False):
        if not path.startswith('/') or '\r' in path or '\n' in path or len(path)>8192:
            raise ValueError('Bounded local Docker RPC path required')
        self.guard();deadline=min(time.monotonic()+10,self.cleanup_deadline if self.cleanup_mode else self.lease.deadline)
        raw=None if body is None else json.dumps(body,separators=(',',':')).encode()
        if raw is not None and len(raw)>MAX_BODY:raise ValueError('Docker request quota exceeded')
        channel=socket.socket(socket.AF_UNIX,socket.SOCK_STREAM);self.live.add(channel)
        connection=http.client.HTTPConnection('localhost',timeout=.5)
        try:
            channel.settimeout(.5);channel.connect(str(self.path));self.guard()
            peer=struct.unpack('3i',channel.getsockopt(socket.SOL_SOCKET,socket.SO_PEERCRED,12))
            if peer[0]!=self.peer_pid or peer[1]!=0 or birth(peer[0])!=self.peer_birth:
                raise ValueError('Actual connected daemon peer differs')
            connection.sock=channel
            headers={'Content-Type':'application/json'} if raw is not None else {}
            if upgrade:headers.update(Upgrade='tcp',Connection='Upgrade')
            connection.request(method,path,body=raw,headers=headers)
            response=connection.getresponse();parts=[];length=0
            if response.status==101 and (not upgrade or response.fp is None):
                raise ValueError('Unexpected Docker upgrade response')
            while True:
                self.guard()
                if time.monotonic()>=deadline:raise TimeoutError('Finite Docker RPC expired')
                # A 101 response has no HTTP body; consume its retained stream.
                part=response.fp.read1(65536) if response.status==101 else response.read1(65536)
                if not part:break
                length+=len(part)
                if length>MAX_BODY:raise ValueError('Docker response quota exceeded')
                parts.append(part)
            self.guard();payload=b''.join(parts)
            return response.status,payload if raw_response else unique_json(payload) if payload else None
        finally:
            try:connection.close()
            finally:channel.close();self.live.discard(channel)
    def daemon_identity(self):
        status,value=self.rpc('GET','/info')
        if status!=200 or value.get('OSType')!='linux' or value.get('ID')!=self.daemon:
            raise ValueError('Actual daemon identity differs')
        return value['ID']
    def create(self,name,body):
        if self.cleanup_mode:raise ValueError('Cleanup cannot create fixture')
        status,value=self.rpc('POST','/containers/create?name='+quote(name,safe=''),body)
        if status!=201 or not re.fullmatch(r'[0-9a-f]{64}',value.get('Id','')):
            raise ValueError('Actual daemon create acknowledgement unavailable')
        self.known.add(value['Id']);return value
    def inspect(self,key):
        if not re.fullmatch(r'[0-9a-f]{64}|accounting-[0-9a-f-]{36}',key):raise ValueError('Exact fixture selector required')
        status,value=self.rpc('GET','/containers/'+quote(key,safe='')+'/json')
        if status==404:return 404
        if status!=200:raise ValueError('Actual fixture inspect unavailable')
        return value
    def inventory(self,owner,run):
        if owner!=OWNER or run!=self.run:raise ValueError('Exact Accounting inventory labels required')
        filters=json.dumps({'label':['accounting-owner.task='+owner,'accounting-owner.run='+run]},separators=(',',':'))
        status,value=self.rpc('GET','/containers/json?all=1&filters='+quote(filters,safe=''))
        if status!=200 or type(value)!=list or len(value)>512:raise ValueError('Actual bounded backend inventory unavailable')
        return value
    def sdk_quiescent(self,run):return run==self.run and group_empty(self.group,self.group_fd)
    def begin_cleanup(self):
        if not self.sdk_quiescent(self.run):raise ValueError('Actual SDK cgroup still populated')
        self.cleanup_mode=True;self.guard()
    def has_active_loopback_clients(self,container_id):
        if container_id not in self.known:raise ValueError('Actual owned fixture required')
        self.guard()
        observed=self.inspect(container_id)
        ports=observed['NetworkSettings'].get('Ports',{})
        host_ports=set()
        for bindings in ports.values():
            for row in bindings or []:
                if row['HostIp']!='127.0.0.1' or not row['HostPort'].isdigit():raise ValueError('Actual loopback port unavailable')
                host_ports.add(int(row['HostPort']))
        for path in (Path('/proc/net/tcp'),Path('/proc/net/tcp6')):
            raw=path.read_text()
            if len(raw)>4*1024**2:raise ValueError('TCP client census quota exceeded')
            for line in raw.splitlines()[1:]:
                pieces=line.split()
                if len(pieces)<4:raise ValueError('TCP client census malformed')
                remote,local,state=pieces[2],pieces[1],pieces[3]
                if state=='01' and (int(remote.rsplit(':',1)[1],16) in host_ports or int(local.rsplit(':',1)[1],16) in host_ports):
                    return True
        return False
    def stop(self,container_id,seconds):
        if not self.cleanup_mode or container_id not in self.known or seconds!=5:raise ValueError('Exact bounded owned stop required')
        status,_=self.rpc('POST','/containers/'+container_id+'/stop?t=5')
        if status not in (204,304):raise ValueError('Exact backend stop failed')
    def remove(self,container_id,force=False,volumes=False):
        if not self.cleanup_mode or container_id not in self.known or force is not False or volumes is not False:
            raise ValueError('Only exact nonforced container removal allowed')
        status,_=self.rpc('DELETE','/containers/'+container_id+'?force=0&v=0')
        if status!=204:raise ValueError('Exact backend removal failed')
    def close(self):
        for channel in tuple(self.live):channel.close();self.live.discard(channel)

    def owned_network(self,capability):
        status,value=self.rpc('GET','/networks/'+quote(capability['id'],safe=''))
        if status!=200:raise ValueError('Actual owned network unavailable')
        return verify_owned_network(value,capability,self.owner,self.run,self.lease.expires,self.known)
    def remove_owned_network(self,capability):
        if not self.cleanup_mode or not self.sdk_quiescent(self.run):raise ValueError('Network cleanup requires actual SDK quiescence')
        observed=self.owned_network(capability)
        if observed['Containers']:raise ValueError('Owned network still has endpoints; preserve it')
        status,value=self.rpc('DELETE','/networks/'+quote(capability['id'],safe=''))
        if status!=204:raise ValueError('Owned network removal uncertain')
        for selector in (capability['id'],capability['name']):
            status,value=self.rpc('GET','/networks/'+quote(selector,safe=''))
            if status!=404:raise ValueError('Physical owned network ID/name absence unverified')
        return True
