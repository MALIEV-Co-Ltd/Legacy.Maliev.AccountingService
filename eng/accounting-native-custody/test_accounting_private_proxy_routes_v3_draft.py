"""Routing source controls; no actual SDK peer, daemon, socket or cgroup."""
import json,unittest,ast,threading
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch
import accounting_private_proxy_routes_v3_draft as r
import accounting_private_proxy_server_v3_draft as s

class Controls(unittest.TestCase):
    def router(self):
        calls=[];state={'present':True,'running':True};identifier='a'*64
        def rpc(method,path,*args,**kwargs):
            calls.append((method,path))
            if '/stop?' in path:state['running']=False;return 204,b''
            if method=='DELETE':state['present']=False;return 204,b''
            return 200,b'{}'
        docker=SimpleNamespace(rpc=rpc,has_active_loopback_clients=lambda x:False,inspect=lambda x:404 if not state['present'] else {'State':{'Running':state['running']}})
        producer=SimpleNamespace(docker=docker,check=lambda:None,images={'postgres:18-alpine':'postgres@sha256:'+'b'*64})
        router=r.AccountingRoutes(producer,'/sys/fs/cgroup/unit',1,1000,None)
        def owned(key):
            if key!=identifier:raise ValueError('Foreign acknowledged ID')
            return docker.inspect(key)
        router.owned=owned
        return router,calls,identifier
    def dispatch(self,router,method,path,**kwargs):
        with patch.object(r,'attributed_sdk_request',return_value={'pid':1,'birthTicks':1}):
            return router.dispatch(None,method,path,**kwargs)
    def test_force_volume_delete_translated_to_safe_removal(self):
        router,calls,key=self.router()
        result=self.dispatch(router,'DELETE','/containers/'+key+'?force=true&v=true')
        self.assertEqual(result[0],204)
        self.assertEqual(calls,[('POST','/containers/'+key+'/stop?t=5'),('DELETE','/containers/'+key+'?force=0&v=0')])
    def test_sdk_stop_timeout_normalized(self):
        router,calls,key=self.router();self.dispatch(router,'POST','/containers/'+key+'/stop?t=99999')
        self.assertEqual(calls,[('POST','/containers/'+key+'/stop?t=5')])
    def test_foreign_container_refused(self):
        router,calls,key=self.router()
        with self.assertRaises(ValueError):self.dispatch(router,'DELETE','/containers/'+'b'*64)
        self.assertEqual(calls,[])
    def test_volume_allocation_refused(self):
        router,calls,key=self.router()
        with self.assertRaises(ValueError):self.dispatch(router,'POST','/volumes/create')
        self.assertEqual(calls,[])
    def test_unknown_exec_refused(self):
        router,calls,key=self.router()
        with self.assertRaises(ValueError):self.dispatch(router,'POST','/exec/'+'c'*64+'/start',payload=b'{}',upgrade=True)
        self.assertEqual(calls,[])
    def test_peer_attribution_failure_blocks_rpc(self):
        router,calls,key=self.router()
        with patch.object(r,'attributed_sdk_request',side_effect=ValueError('wrong actual unit')),self.assertRaises(ValueError):
            router.dispatch(None,'GET','/info')
        self.assertEqual(calls,[])
    def test_unknown_start_query_refused(self):
        router,calls,key=self.router()
        with self.assertRaises(ValueError):self.dispatch(router,'POST','/containers/'+key+'/start?checkpoint=foreign')
        self.assertEqual(calls,[])
    def test_duplicate_query_refused(self):
        router,calls,key=self.router()
        with self.assertRaises(ValueError):self.dispatch(router,'DELETE','/containers/'+key+'?v=0&v=1')
        self.assertEqual(calls,[])
    def test_acknowledged_unstarted_exec_blocks_delete(self):
        router,calls,key=self.router();router.execs['b'*64]={'id':'b'*64,'containerId':key,'state':'created'}
        with self.assertRaises(ValueError):self.dispatch(router,'DELETE','/containers/'+key)
        self.assertEqual(calls,[])
    def test_inflight_exec_blocks_stop(self):
        router,calls,key=self.router();router.execs['b'*64]={'id':'b'*64,'containerId':key,'state':'in-flight'}
        with self.assertRaises(ValueError):self.dispatch(router,'POST','/containers/'+key+'/stop')
        self.assertEqual(calls,[])
    def test_exec_unknown_inspection_blocks_delete(self):
        router,calls,key=self.router();router.execs['b'*64]={'id':'b'*64,'containerId':key,'state':'completed'}
        with self.assertRaises(ValueError):self.dispatch(router,'DELETE','/containers/'+key)
        self.assertEqual(calls,[('GET','/exec/'+'b'*64+'/json')])
    def test_exec_create_uncertainty_blocks_delete(self):
        router,calls,key=self.router();router.exec_uncertain.add(key)
        with self.assertRaises(ValueError):self.dispatch(router,'DELETE','/containers/'+key)
        self.assertEqual(calls,[])
    def test_other_thread_container_acquisition_blocks_delete(self):
        router,calls,key=self.router();held=threading.Event();release=threading.Event()
        def worker():
            with router.container_operation(key):held.set();release.wait(2)
        thread=threading.Thread(target=worker);thread.start()
        try:
            self.assertTrue(held.wait(1))
            with self.assertRaises(ValueError):self.dispatch(router,'DELETE','/containers/'+key)
            self.assertEqual(calls,[])
        finally:release.set();thread.join(2);self.assertFalse(thread.is_alive())
    def test_actual_exec_creation_acquisition_race_blocks_delete(self):
        router,calls,key=self.router();entered=threading.Event();release=threading.Event();failures=[]
        router.producer.owner='source-only';router.producer.run='source-only';router.producer.root=Path('/synthetic-no-write')
        def rpc(method,path,*args,**kwargs):
            calls.append((method,path));entered.set();release.wait(2);return 201,{'Id':'b'*64}
        router.docker.rpc=rpc
        def worker():
            try:self.dispatch(router,'POST','/containers/'+key+'/exec',payload=b'{"Cmd":["pg_isready"]}')
            except BaseException as failure:failures.append(failure)
        with patch.object(r,'write_new',return_value=None):
            thread=threading.Thread(target=worker);thread.start()
            try:
                self.assertTrue(entered.wait(1))
                with self.assertRaises(ValueError):self.dispatch(router,'DELETE','/containers/'+key)
                self.assertEqual(calls,[('POST','/containers/'+key+'/exec')])
            finally:release.set();thread.join(2);self.assertFalse(thread.is_alive())
        self.assertEqual(failures,[]);self.assertEqual(router.execs['b'*64]['state'],'created')
    def test_completed_record_with_actual_running_exec_refuses_delete(self):
        router,calls,key=self.router();router.execs['b'*64]={'id':'b'*64,'containerId':key,'state':'completed'}
        def rpc(method,path,*args,**kwargs):
            calls.append((method,path));return 200,{'ID':'b'*64,'ContainerID':key,'Running':True}
        router.docker.rpc=rpc
        with self.assertRaises(ValueError):self.dispatch(router,'DELETE','/containers/'+key)
        self.assertEqual(calls,[('GET','/exec/'+'b'*64+'/json')])
    def test_proxy_server_parses_without_top_level_start(self):
        tree=ast.parse((Path(__file__).parent/'accounting_private_proxy_server_v3_draft.py').read_bytes())
        self.assertFalse(any(isinstance(node,ast.Expr) and isinstance(node.value,ast.Call) for node in tree.body))

class WorkerControls(unittest.TestCase):
    def server(self):
        server=s.Server.__new__(s.Server);server.permits=threading.BoundedSemaphore(1);server.lock=threading.Lock()
        server.closing=threading.Event();server.connections=set();server.workers=set();server.admissions={};server.failures=[]
        server.finish_request=lambda *args:None;server.shutdown_request=lambda request:request.close();server.handle_error=lambda *args:None
        class Request:
            def close(self):pass
        return server,Request()
    def test_worker_retained_before_target_can_execute(self):
        server,request=self.server();held=threading.Event();release=threading.Event();original=server.process_request_thread
        def delayed(*args):held.set();release.wait(2);original(*args)
        server.process_request_thread=delayed;server.process_request(request,None)
        workers=tuple(server.workers)
        try:
            self.assertTrue(held.wait(1));self.assertEqual(len(workers),1)
            self.assertFalse(server.admissions[workers[0]]['settled']);self.assertTrue(workers[0].is_alive())
        finally:
            release.set()
            for worker in workers:worker.join(2);self.assertFalse(worker.is_alive())
        self.assertTrue(server.admissions[workers[0]]['settled'])
    def test_thread_start_fault_settles_admission(self):
        server,request=self.server()
        with patch.object(threading.Thread,'start',side_effect=RuntimeError('start fault')),self.assertRaises(RuntimeError):server.process_request(request,None)
        self.assertFalse(server.workers);self.assertFalse(server.connections)
        self.assertTrue(all(row['settled'] for row in server.admissions.values()))
        self.assertTrue(server.permits.acquire(blocking=False));server.permits.release()
    def test_registration_fault_releases_permit(self):
        class FaultSet(set):
            def add(self,item):raise RuntimeError('registration fault')
        server,request=self.server();server.workers=FaultSet()
        with self.assertRaises(RuntimeError):server.process_request(request,None)
        self.assertFalse(server.connections);self.assertFalse(server.admissions)
        self.assertTrue(server.permits.acquire(blocking=False));server.permits.release()
    def test_closing_server_admits_no_new_worker(self):
        server,request=self.server();server.closing.set();server.process_request(request,None)
        self.assertFalse(server.workers);self.assertFalse(server.admissions)

if __name__=='__main__':unittest.main(verbosity=2)
