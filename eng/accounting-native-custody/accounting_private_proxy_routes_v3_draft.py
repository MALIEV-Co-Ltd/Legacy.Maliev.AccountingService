"""Unenabled Accounting API routing source; worker/socket owner still required.

All requests require actual SDK peer attribution; all workload selectors are
full acknowledged IDs. No network/volume allocation or global cleanup route.
"""
import json,re,threading
from contextlib import contextmanager
from urllib.parse import urlsplit,parse_qs,unquote,quote
from accounting_v3_guard_core import unique_json
from accounting_docker_transport_v3_draft import attributed_sdk_request
from accounting_owned_sdk_v3_draft import write_new

class AccountingRoutes:
    def __init__(self,producer,group,group_fd,uid,lease):
        self.producer=producer;self.docker=producer.docker;self.group=group;self.group_fd=group_fd;self.uid=uid;self.lease=lease
        self.execs={};self.exec_uncertain=set();self.container_locks={};self.registry_lock=threading.RLock()
    @contextmanager
    def container_operation(self,identifier):
        with self.registry_lock:lock=self.container_locks.setdefault(identifier,threading.RLock())
        if not lock.acquire(blocking=False):raise ValueError('Container operation in flight; preserve resource')
        try:yield
        finally:lock.release()
    def fence_execs(self,identifier):
        if identifier in self.exec_uncertain:raise ValueError('Unacknowledged exec remains uncertain')
        with self.registry_lock:rows=tuple(self.execs.values())
        for row in rows:
            if row['containerId']!=identifier:continue
            if row.get('state')!='completed':raise ValueError('Exec not durably settled; preserve container')
            status,value=self.docker.rpc('GET','/exec/'+row['id']+'/json')
            if status!=200 or type(value)!=dict or value.get('ID')!=row['id'] or value.get('ContainerID')!=identifier or value.get('Running') is not False:
                raise ValueError('Actual exec completion/parent identity unknown')
    def owned(self,identifier):
        rows=[(self.producer.intents[child],row) for child,row in self.producer.publications.items() if row['id']==identifier]
        if len(rows)!=1:raise ValueError('Exact acknowledged Accounting container required')
        intent,published=rows[0];observed=self.docker.inspect(identifier)
        if observed!=404:self.producer.verify_profile(intent,identifier,observed)
        return observed
    def dispatch(self,connection,method,target,payload=b'',upgrade=False):
        attributed_sdk_request(connection,self.group,self.group_fd,self.uid,self.lease)
        route=re.sub(r'^/v[0-9]+\.[0-9]+(?=/)','',urlsplit(target).path)
        container=re.match(r'^/containers/([0-9a-f]{64})(?:/|$)',route)
        execution=re.match(r'^/exec/([0-9a-f]{64})(?:/|$)',route)
        identifier=container[1] if container else None
        if execution:
            with self.registry_lock:row=self.execs.get(execution[1])
            if row is None:raise ValueError('Unknown acknowledged exec')
            identifier=row['containerId']
        if identifier is not None:
            with self.container_operation(identifier):return self._dispatch(connection,method,target,payload,upgrade)
        return self._dispatch(connection,method,target,payload,upgrade)
    def _dispatch(self,connection,method,target,payload=b'',upgrade=False):
        attributed_sdk_request(connection,self.group,self.group_fd,self.uid,self.lease)
        self.producer.check()
        if type(payload)!=bytes or len(payload)>1024**2:raise ValueError('Bounded SDK Docker request required')
        parsed=urlsplit(target)
        if parsed.scheme or parsed.netloc or parsed.fragment:raise ValueError('Absolute/fragment Docker endpoint refused')
        route=re.sub(r'^/v[0-9]+\.[0-9]+(?=/)','',parsed.path)
        query=parse_qs(parsed.query,keep_blank_values=True,strict_parsing=True) if parsed.query else {}
        if any(len(values)!=1 for values in query.values()):raise ValueError('Ambiguous Docker query refused')
        body=unique_json(payload) if payload else None
        def encoded(value):return json.dumps(value,separators=(',',':')).encode()
        if route=='/containers/create' and method=='POST' and not upgrade:
            if set(query)-{'name','platform'}:raise ValueError('Unknown fixture create query')
            return 201,encoded(self.producer.create(body)),False
        information={'/_ping','/version','/info'}
        if route in information and method in ('GET','HEAD') and not upgrade:
            if query:raise ValueError('Unknown information query')
            status,raw=self.docker.rpc(method,route,raw_response=True);return status,raw,False
        container=re.fullmatch(r'/containers/([0-9a-f]{64})(?:/(json|start|stop|wait|logs|exec))?',route)
        if container:
            identifier,operation=container.groups();observed=self.owned(identifier)
            if observed==404:return 404,encoded({'message':'Owned container absent'}),False
            if operation=='exec' and method=='POST' and not upgrade:
                if len(self.execs)>=512 or type(body)!=dict or body.get('Privileged') or body.get('AttachStdin') or body.get('Tty'):
                    raise ValueError('Interactive/privileged or unbounded exec refused')
                command=body.get('Cmd')
                if type(command)!=list or not 0<len(command)<=32 or any(type(x)!=str for x in command) or sum(map(len,command))>8192:
                    raise ValueError('Bounded exec command required')
                self.exec_uncertain.add(identifier)
                status,result=self.docker.rpc('POST',route,body)
                if status!=201 or not re.fullmatch('[0-9a-f]{64}',result.get('Id','')) or result['Id'] in self.execs:
                    raise ValueError('Actual unique exec acknowledgement missing')
                row={'id':result['Id'],'containerId':identifier,'owner':self.producer.owner,'run':self.producer.run,'state':'created'}
                write_new(self.producer.root/(result['Id']+'-exec.json'),row)
                with self.registry_lock:self.execs[result['Id']]=row
                self.exec_uncertain.remove(identifier)
                return status,encoded(result),False
            if operation is None and method=='DELETE' and not upgrade:
                self.fence_execs(identifier)
                if set(query)-{'force','v','link'} or query.get('link',['0'])[0].lower() not in ('0','false',''):
                    raise ValueError('Unapproved owned deletion query')
                if any(values[0].lower() not in ('0','1','true','false','') for values in query.values()):
                    raise ValueError('Invalid owned deletion flags')
                # TC4.14 asks force/v=true. Actual verified tmpfs-only fixture is
                # stopped first, then removed nonforced without volume deletion.
                if self.docker.has_active_loopback_clients(identifier):raise ValueError('Owned fixture still has readers')
                self.owned(identifier)
                if observed['State']['Running']:
                    status,_=self.docker.rpc('POST','/containers/'+identifier+'/stop?t=5')
                    if status not in (204,304):raise ValueError('Owned fixture stop failed')
                stopped=self.owned(identifier)
                if stopped==404:return 404,encoded({'message':'Owned container absent'}),False
                if stopped['State']['Running']:raise ValueError('Owned fixture remains running')
                status,raw=self.docker.rpc('DELETE','/containers/'+identifier+'?force=0&v=0',raw_response=True)
                if status!=204 or self.docker.inspect(identifier)!=404:raise ValueError('Actual exact container absence unverified')
                return status,raw,False
            allowed={('GET','json'),('POST','start'),('POST','stop'),('POST','wait'),('GET','logs')}
            if (method,operation) not in allowed or upgrade:raise ValueError('Unapproved owned container operation')
            query_keys={'json':{'size'},'start':set(),'stop':{'t','signal'},'wait':{'condition'},'logs':{'stdout','stderr','since','until','timestamps','tail','follow'}}[operation]
            if set(query)-query_keys:raise ValueError('Unknown owned operation query')
            if operation=='stop':
                if 'signal' in query:raise ValueError('SDK may not override graceful stop signal')
                target=route+'?t=5'
            else:target=route+('?' + parsed.query if parsed.query else '')
            if operation=='stop':
                self.fence_execs(identifier)
                if self.docker.has_active_loopback_clients(identifier):raise ValueError('Owned fixture has active clients')
            status,raw=self.docker.rpc(method,target,body,raw_response=True)
            if operation=='start' and status==204:self.owned(identifier)
            return status,raw,False
        execution=re.fullmatch(r'/exec/([0-9a-f]{64})/(start|json)',route)
        if execution:
            identifier,operation=execution.groups()
            row=self.execs.get(identifier)
            if row is None or self.owned(row['containerId'])==404:raise ValueError('Foreign or absent exec parent')
            if operation=='start':
                if method!='POST' or type(body)!=dict or body.get('Detach') or body.get('Tty'):
                    raise ValueError('Detached/interactive exec refused')
                if row.get('state')!='created':raise ValueError('Exec start state differs')
                row['state']='in-flight'
                status,raw=self.docker.rpc(method,route,body,raw_response=True,upgrade=upgrade)
                actual_status,actual=self.docker.rpc('GET','/exec/'+identifier+'/json')
                if status not in (200,101) or actual_status!=200 or type(actual)!=dict or actual.get('ID')!=identifier or actual.get('ContainerID')!=row['containerId'] or actual.get('Running') is not False:
                    raise ValueError('Exec output ended without actual completion acknowledgement')
                write_new(self.producer.root/(identifier+'-exec-completed.json'),dict(row,state='completed'))
                row['state']='completed'
                return status,raw,status==101
            if method!='GET' or upgrade:raise ValueError('Exec inspect verb differs')
            status,raw=self.docker.rpc(method,route,raw_response=True);return status,raw,False
        image=re.fullmatch(r'/images/(.+)/json',route)
        if image and method=='GET' and not upgrade:
            tag=unquote(image[1]);pinned=self.producer.images.get(tag)
            if pinned is None:raise ValueError('Unapproved image inspection')
            status,raw=self.docker.rpc('GET','/images/'+quote(pinned,safe='')+'/json',raw_response=True)
            return status,raw,False
        raise ValueError('Unapproved Docker route; no global/container/volume/network scope')
