"""Unenabled Linux ownership source draft. No CLI, grants, or native launch caller.

Root review and actual hosted lifecycle controls are required before integration.
The fixture custodian interface must be the actual Accounting intent producer.
"""
import ctypes
from contextlib import contextmanager
import hashlib,json,os,select,signal,sys,time,uuid
from pathlib import Path
from accounting_v3_guard_core import ImmutableLease,effective_capacity,LIMIT

OWNER='01a1009c-aa47-72d2-9914-3a0784a67c0e'
FLAGS=('sdkExited','ownedBackendsAbsent','handlesReleased','slotReleased','slotAbsent','cgroupAbsent')
LOG_LIMIT=32*1024**2

@contextmanager
def registration_window():
    old=signal.pthread_sigmask(signal.SIG_BLOCK,{signal.SIGINT,signal.SIGTERM})
    try:yield old
    finally:signal.pthread_sigmask(signal.SIG_SETMASK,old)

def identity(fd):
    value=os.fstat(fd);return value.st_dev,value.st_ino

def same_path(path,fd):
    value=os.lstat(path)
    if (value.st_dev,value.st_ino)!=identity(fd):raise RuntimeError('Ownership inode changed; preserve resource')

def birth(pid):
    text=Path(f'/proc/{pid}/stat').read_text()
    return int(text[text.rfind(')')+2:].split()[19])

def group_empty(group,fd):
    same_path(group,fd)
    rows=dict(line.split() for line in (group/'cgroup.events').read_text().splitlines())
    if rows.get('populated') not in ('0','1'):raise RuntimeError('Actual cgroup population unavailable')
    return rows['populated']=='0'

def child_guard(parent,mask):
    libc=ctypes.CDLL(None,use_errno=True)
    if libc.prctl(1,signal.SIGKILL,0,0,0)!=0 or os.getppid()!=parent:
        raise RuntimeError('Actual parent-death guard unavailable')
    signal.signal(signal.SIGINT,signal.SIG_DFL);signal.signal(signal.SIGTERM,signal.SIG_DFL)
    signal.pthread_sigmask(signal.SIG_SETMASK,mask)

def write_new(path,value):
    raw=json.dumps(value,sort_keys=True).encode()
    fd=os.open(path,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
    try:
        view=memoryview(raw)
        while view:
            count=os.write(fd,view)
            if count<=0:raise OSError('Short ownership receipt write')
            view=view[count:]
        os.fsync(fd)
    finally:os.close(fd)
    parent=os.open(path.parent,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
    try:os.fsync(parent)
    finally:os.close(parent)

def run_owned_sdk(authority,executable,arguments,repository,environment,uid,gid,groups,
                  lease,fixture_custodian,log_sink,cgroup_parent,claim_path,evidence,cancelled=lambda:False):
    """Exclusive stopped-child registration, continuous expiry, exact-unit cleanup.

    authority must independently verify the reviewed grant/phase/argv/source seals.
    No scope may be inferred from available memory. This module has no caller.
    """
    if sys.platform!='linux' or os.geteuid()!=0 or not hasattr(os,'pidfd_open'):
        raise RuntimeError('Reviewed delegated Linux cgroup/pidfd owner required')
    if not isinstance(lease,ImmutableLease):raise ValueError('Immutable Accounting lease required')
    if type(uid)!=int or uid<=0 or type(gid)!=int or gid<0 or not groups or len(groups)>32:
        raise ValueError('Finite nonroot SDK identity required')
    if any(type(item)!=int or item<0 for item in groups):raise ValueError('SDK groups invalid')
    repository=Path(repository).resolve(strict=True);executable=Path(executable).resolve(strict=True)
    cgroup_parent=Path(cgroup_parent).resolve(strict=True);claim_path=Path(claim_path);evidence=Path(evidence)
    if any(path.is_symlink() for path in (claim_path,evidence,cgroup_parent)):
        raise ValueError('Linked ownership path refused')
    if not isinstance(arguments,list) or not arguments or any(type(x)!=str or len(x)>4096 or '\x00' in x for x in arguments):
        raise ValueError('Bounded fixed SDK argv required')
    if len(arguments)>128:raise ValueError('SDK argument budget exceeded')
    lease.check()
    verified=authority(executable,arguments,repository,environment,uid,gid,groups,cgroup_parent,claim_path,evidence)
    if type(verified)!=dict or verified.get('owner')!=OWNER or verified.get('sdkAuthorized') is not True:
        raise ValueError('Independent Accounting scoped authority absent')
    run=verified['run'];uuid.UUID(run)
    if fixture_custodian.run!=run or fixture_custodian.owner!=OWNER:
        raise ValueError('Actual fixture custodian owner/run binding differs')
    if environment.get('DOCKER_HOST')!=fixture_custodian.private_endpoint or environment.get('TESTCONTAINERS_RYUK_DISABLED')!='true' or environment.get('TESTCONTAINERS_HOST_OVERRIDE')!='127.0.0.1':
        raise ValueError('SDK must use only Accounting private proxy and loopback fixtures')
    executable_sha=verified['executableSha256']
    if hashlib.sha256(executable.read_bytes()).hexdigest()!=executable_sha:
        raise ValueError('Actual SDK executable seal differs')
    state=dict.fromkeys(FLAGS,False);state.update(owner=OWNER,run=run,nativeAccepted=False,firstFailure=None)
    group=cgroup_parent/('accounting-'+run)
    claim_fd=group_fd=exec_fd=pidfd=None;pid=None;child_birth=None;reaped=False
    pipes=[];streams=[];first=None;group_created=False;claim_created=False
    cancellation=[False];previous_handlers={}
    original_cancelled=cancelled
    def cancellation_signal(number,frame):cancellation[0]=True
    def cancelled():return cancellation[0] or original_cancelled()
    # Signals request bounded containment; they cannot interrupt exact cleanup.
    for number in (signal.SIGINT,signal.SIGTERM):
        previous_handlers[number]=signal.signal(number,cancellation_signal)
    def guard():
        if cancelled():raise KeyboardInterrupt('Accounting owner cancelled')
        lease.check()
        if pid is not None and not reaped:
            if birth(pid)!=child_birth:raise RuntimeError('Actual SDK birth changed')
            observation=effective_capacity(pid)
            if observation['actualCgroup']!=str(group):raise RuntimeError('Actual SDK escaped owned cgroup')
    try:
        with registration_window():
            claim_fd=os.open(claim_path,os.O_RDWR|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600);claim_created=True
            os.write(claim_fd,json.dumps({'owner':OWNER,'run':run}).encode());os.fsync(claim_fd)
        with registration_window():
            group.mkdir();group_created=True
            group_fd=os.open(group,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        fixture_custodian.bind_sdk_group(group,group_fd)
        (group/'memory.max').write_text(str(LIMIT));(group/'memory.swap.max').write_text('0')
        (group/'cpu.max').write_text('100000 100000');(group/'pids.max').write_text('512')
        exec_fd=os.open(executable,os.O_RDONLY|os.O_NOFOLLOW)
        if hashlib.sha256(os.pread(exec_fd,64*1024**2,0)).hexdigest()!=executable_sha:
            raise ValueError('Retained SDK executable identity differs')
        for _ in range(2):
            with registration_window():pipes.append(os.pipe2(os.O_CLOEXEC))
        parent=os.getpid()
        with registration_window() as mask:
            pid=os.fork()
            if pid==0:
                try:
                    os.dup2(pipes[0][1],1);os.dup2(pipes[1][1],2)
                    for pair in pipes:
                        for fd in pair:os.close(fd)
                    for fd in (claim_fd,group_fd):os.close(fd)
                    os.setgroups(groups);os.setgid(gid);os.setuid(uid)
                    child_guard(parent,mask);os.kill(os.getpid(),signal.SIGSTOP)
                    os.chdir(repository);os.execve(exec_fd,[str(executable),*arguments],environment)
                except BaseException:os._exit(125)
            child_birth=birth(pid);pidfd=os.pidfd_open(pid,0)
        # Bounded stopped-child handshake; never continue an unregistered unit.
        while True:
            lease.check()
            if cancelled():raise KeyboardInterrupt('Cancelled before SDK registration')
            observed,status=os.waitpid(pid,os.WNOHANG|os.WUNTRACED)
            if observed:
                if not os.WIFSTOPPED(status):reaped=True;raise RuntimeError('SDK exited before registration')
                break
            time.sleep(0.02)
        same_path(group,group_fd);(group/'cgroup.procs').write_text(str(pid))
        guard()
        write_new(evidence/(run+'-sdk-registration.json'),{'owner':OWNER,'run':run,'pid':pid,'birthTicks':child_birth,'cgroup':str(group),'cgroupIdentity':identity(group_fd),'executableSha256':executable_sha,'expiresUtc':lease.expires.isoformat()})
        for read_fd,write_fd in pipes:
            os.close(write_fd);os.set_blocking(read_fd,False);streams.append(read_fd)
        pipes=[]
        log_sink.bind(*streams)
        fixture_custodian.start_proxy(group,group_fd,uid,gid)
        signal.pidfd_send_signal(pidfd,signal.SIGCONT)
        total=0;last_capacity=time.monotonic();status=None
        while streams or not reaped:
            if cancelled():raise KeyboardInterrupt('Accounting SDK cancelled')
            lease.check()
            if not reaped and time.monotonic()-last_capacity>=1:
                guard();last_capacity=time.monotonic()
            ready,_,_=select.select(streams,[],[],0.1)
            for fd in ready:
                chunk=os.read(fd,65536)
                if not chunk:os.close(fd);streams.remove(fd);continue
                total+=len(chunk)
                if total>LOG_LIMIT:raise RuntimeError('Aggregate SDK log quota exceeded')
                log_sink.append(fd,chunk)
            if not reaped:
                observed,status=os.waitpid(pid,os.WNOHANG)
                if observed:reaped=True
        if os.waitstatus_to_exitcode(status)!=0:raise RuntimeError('SDK command failed')
        guard()
    except BaseException as failure:
        first=failure;state['firstFailure']=type(failure).__name__
    finally:
        # Cleanup-only phase never renews execution authority.
        try:
            if pid is not None and not reaped:
                if birth(pid)!=child_birth:raise RuntimeError('Unregistered SDK containment uncertain')
                # An unreaped direct child retains its PID even if pidfd_open failed.
                if pidfd is None:os.kill(pid,signal.SIGTERM)
                else:signal.pidfd_send_signal(pidfd,signal.SIGTERM)
                end=time.monotonic()+5
                while time.monotonic()<end:
                    observed,_=os.waitpid(pid,os.WNOHANG)
                    if observed:reaped=True;break
                    time.sleep(.05)
            if group_fd is not None and not group_empty(group,group_fd):
                same_path(group,group_fd);(group/'cgroup.kill').write_text('1')
            if pid is not None and not reaped:
                if birth(pid)!=child_birth:raise RuntimeError('Direct child identity changed')
                if pidfd is None:os.kill(pid,signal.SIGKILL)
                else:signal.pidfd_send_signal(pidfd,signal.SIGKILL)
                end=time.monotonic()+5
                while time.monotonic()<end:
                    observed,_=os.waitpid(pid,os.WNOHANG)
                    if observed:reaped=True;break
                    time.sleep(.05)
            if pid is not None and not reaped:raise RuntimeError('Actual SDK unreaped; retain claim')
            if group_fd is not None:
                end=time.monotonic()+5
                while not group_empty(group,group_fd) and time.monotonic()<end:time.sleep(.05)
                if not group_empty(group,group_fd):raise RuntimeError('SDK group still populated; retain claim')
            state['sdkExited']=True
            if fixture_custodian.cleanup_after_sdk_quiescence() is not True:
                raise RuntimeError('Actual Accounting backend absence unverified')
            state['ownedBackendsAbsent']=True
        except BaseException as failure:
            state['cleanupFailure']=type(failure).__name__
            if first is None:first=failure;state['firstFailure']=type(failure).__name__
        descriptors=streams+[fd for pair in pipes for fd in pair]+[fd for fd in (pidfd,exec_fd) if fd is not None]
        release_errors=[]
        for closer in (fixture_custodian.close,log_sink.close):
            try:closer()
            except BaseException as failure:release_errors.append(failure)
        for fd in descriptors:
            try:os.close(fd)
            except BaseException as failure:release_errors.append(failure)
        try:
            if release_errors:raise release_errors[0]
            if state['sdkExited'] and state['ownedBackendsAbsent']:
                if group_created:
                    same_path(group,group_fd);os.close(group_fd);group_fd=None;group.rmdir()
                if claim_created:
                    same_path(claim_path,claim_fd);claim_path.unlink();os.close(claim_fd);claim_fd=None
                state['slotReleased']=True;state['slotAbsent']=not claim_path.exists();state['cgroupAbsent']=not group.exists()
        except BaseException as failure:
            state['releaseFailure']=type(failure).__name__
            if first is None:first=failure;state['firstFailure']=type(failure).__name__
        finally:
            for fd in (group_fd,claim_fd):
                if fd is not None:
                    try:os.close(fd)
                    except BaseException as failure:
                        release_errors.append(failure)
                        state['releaseFailure']=type(failure).__name__
                        if first is None:first=failure;state['firstFailure']=type(failure).__name__
            state['handlesReleased']=not release_errors
        try:write_new(evidence/(run+'-sdk-settlement.json'),state)
        except BaseException as failure:
            state['saveFailure']=type(failure).__name__
            if first is None:first=failure;state['firstFailure']=type(failure).__name__
        finally:
            for number,handler in previous_handlers.items():signal.signal(number,handler)
    if first is not None:
        first.accounting_settlement=state;raise first
    if not all(state[key] is True for key in FLAGS):raise RuntimeError('Actual six-flag settlement required')
    return state
