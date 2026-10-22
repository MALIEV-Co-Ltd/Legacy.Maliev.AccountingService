"""Bounded unenabled Unix proxy owner. No CLI/top-level worker creation."""
import os,socket,socketserver,threading,time,sys
from http.server import BaseHTTPRequestHandler
from pathlib import Path
from accounting_private_proxy_routes_v3_draft import AccountingRoutes
from accounting_owned_sdk_v3_draft import same_path,identity

class Server(socketserver.ThreadingMixIn,getattr(socketserver,'UnixStreamServer',socketserver.BaseServer)):
    daemon_threads=False;block_on_close=False;request_queue_size=2
    def process_request(self,request,address):
        if not self.permits.acquire(blocking=False):request.close();return
        thread=None
        with self.lock:
            if self.closing.is_set():self.permits.release();request.close();return
            try:
                thread=threading.Thread(target=self.process_request_thread,args=(request,address),daemon=False)
                # Retain the exact worker before start: an unscheduled target is
                # an admission, never an absent worker during shutdown.
                self.workers.add(thread);self.connections.add(request)
                self.admissions[thread]={'request':request,'settled':False}
                thread.start()
            except BaseException:
                if thread is None:self.permits.release();request.close()
                elif not thread.is_alive():
                    if thread in self.admissions:self.settle_admission_locked(thread)
                    else:self.connections.discard(request);self.permits.release()
                    self.workers.discard(thread);request.close()
                # A start exception with a live worker retains exact custody.
                raise
    def settle_admission_locked(self,thread):
        row=self.admissions[thread]
        if not row['settled']:
            row['settled']=True;self.connections.discard(row['request']);self.permits.release()
    def process_request_thread(self,request,address):
        thread=threading.current_thread()
        try:super().process_request_thread(request,address)
        finally:
            with self.lock:self.settle_admission_locked(thread)
            # Keep the thread retained until an owner joins/reaps it. Target
            # completion precedes actual thread exit and cannot prove absence.

class Handler(BaseHTTPRequestHandler):
    protocol_version='HTTP/1.1'
    def log_message(self,*args):pass
    def setup(self):
        self.request.settimeout(.5);super().setup()
    def do_GET(self):self.forward()
    def do_HEAD(self):self.forward()
    def do_POST(self):self.forward()
    def do_DELETE(self):self.forward()
    def forward(self):
        self.close_connection=True
        try:
            if self.server.closing.is_set():raise ValueError('Private proxy owner closing')
            if len(self.path)>8192 or len(self.headers)>64 or sum(len(k)+len(v) for k,v in self.headers.items())>65536:
                raise ValueError('Private HTTP header quota exceeded')
            if self.headers.get('Transfer-Encoding') or self.headers.get('Content-Encoding'):
                raise ValueError('Encoded/streaming SDK request refused')
            lengths=self.headers.get_all('Content-Length',[])
            if len(lengths)>1 or lengths and (not lengths[0].isdigit() or int(lengths[0])>1024**2):
                raise ValueError('Bounded unique request length required')
            length=int(lengths[0]) if lengths else 0
            payload=self.rfile.read(length)
            if len(payload)!=length:raise ValueError('Incomplete SDK request')
            status,raw,hijacked=self.server.routes.dispatch(self.connection,self.command,self.path,payload,upgrade=bool(self.headers.get('Upgrade')))
            if self.server.closing.is_set():raise ValueError('Private owner closed before SDK response')
            self.send_response(status)
            if hijacked:
                self.send_header('Upgrade','tcp');self.send_header('Connection','Upgrade')
            else:
                self.send_header('Content-Length',str(len(raw)))
                self.send_header('Content-Type','application/vnd.docker.raw-stream' if '/logs' in self.path else 'application/json')
                self.send_header('Connection','close')
            self.end_headers()
            if self.command!='HEAD':self.wfile.write(raw);self.wfile.flush()
        except BaseException as failure:
            with self.server.lock:self.server.failures.append(type(failure).__name__)
            try:
                raw=b'{"message":"Accounting owned Docker admission failed"}'
                self.send_response(503);self.send_header('Content-Length',str(len(raw)));self.send_header('Connection','close');self.end_headers();self.wfile.write(raw)
            except BaseException:pass

class OwnedPrivateProxy:
    def __init__(self,path,producer,group,group_fd,uid,gid,lease):
        self.path=Path(path);self.producer=producer;self.root_fd=None;self.server=None;self.thread=None;self.socket_identity=None;self.closed=False
        self.group=group;self.group_fd=group_fd;self.uid=uid;self.gid=gid;self.lease=lease
    def start(self):
        if sys.platform!='linux' or not hasattr(socketserver,'UnixStreamServer'):
            raise RuntimeError('Actual Linux Unix proxy required; no TCP fallback')
        self.lease.check()
        if self.server is not None or self.path.exists() or len(str(self.path).encode())>100:
            raise ValueError('Exclusive bounded private proxy socket required')
        self.root_fd=os.open(self.path.parent,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        try:
            self.server=Server(str(self.path),Handler,bind_and_activate=False)
            self.server.permits=threading.BoundedSemaphore(2);self.server.lock=threading.Lock()
            self.server.connections=set();self.server.workers=set();self.server.admissions={};self.server.failures=[];self.server.closing=threading.Event()
            self.server.routes=AccountingRoutes(self.producer,self.group,self.group_fd,self.uid,self.lease)
            self.server.server_bind();self.server.server_activate()
            value=os.lstat(self.path);self.socket_identity=(value.st_dev,value.st_ino)
            os.chown(self.path,0,self.gid);os.chmod(self.path,0o660)
            self.thread=threading.Thread(target=self.server.serve_forever,kwargs={'poll_interval':.1},name='accounting-private-proxy',daemon=False)
            self.thread.start()
        except BaseException:self.close();raise
    def close(self):
        if self.closed:return
        errors=[]
        if self.server is not None:
            self.server.closing.set()
            # Abort only retained sockets owned by this exact private server.
            with self.server.lock:channels=tuple(self.server.connections)
            for channel in channels:
                try:channel.shutdown(socket.SHUT_RDWR)
                except OSError:pass
                finally:channel.close()
            self.producer.docker.close()
            if self.thread is not None and self.thread.ident is not None:
                self.server.shutdown();self.thread.join(5)
                if self.thread.is_alive():errors.append(RuntimeError('Private listener did not exit'))
            with self.server.lock:workers=tuple(self.server.workers)
            deadline=time.monotonic()+5
            for thread in workers:thread.join(max(0,deadline-time.monotonic()))
            if any(thread.is_alive() for thread in workers):errors.append(RuntimeError('Owned proxy workers did not exit'))
            with self.server.lock:
                if any(not row['settled'] for row in self.server.admissions.values()):errors.append(RuntimeError('Private worker admission remains unsettled'))
            self.server.server_close()
        if self.root_fd is not None:
            try:
                same_path(self.path.parent,self.root_fd)
                if self.socket_identity is not None:
                    value=os.lstat(self.path)
                    if (value.st_dev,value.st_ino)!=self.socket_identity:raise ValueError('Private socket identity changed; preserve pathname')
                    if not errors:self.path.unlink()
            except BaseException as failure:errors.append(failure)
            finally:os.close(self.root_fd);self.root_fd=None
        self.closed=not errors
        if errors:raise errors[0]
