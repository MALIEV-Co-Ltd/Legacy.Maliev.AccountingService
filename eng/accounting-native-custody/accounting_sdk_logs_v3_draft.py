"""Bounded retained SDK log descriptors; unenabled custody draft component."""
import hashlib,os,uuid
from pathlib import Path
from accounting_owned_sdk_v3_draft import LOG_LIMIT,identity,same_path,write_new,OWNER

class OwnedSdkLogs:
    def __init__(self,root,run):
        uuid.UUID(run);self.root=Path(root);self.run=run;self.owner=OWNER
        self.directory_fd=None;self.rows={};self.total=0;self.bound=False
    def bind(self,stdout,stderr):
        if self.bound or stdout==stderr:raise ValueError('Exactly two unique SDK log streams required')
        self.directory_fd=os.open(self.root,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
        try:
            for source,label in ((stdout,'stdout'),(stderr,'stderr')):
                path=self.root/(self.run+'-'+label+'.log')
                fd=os.open(path,os.O_WRONLY|os.O_CREAT|os.O_EXCL|os.O_NOFOLLOW,0o600)
                self.rows[source]={'fd':fd,'path':path,'identity':identity(fd),'hash':hashlib.sha256(),'bytes':0}
            self.bound=True
        except BaseException:
            self.close();raise
    def append(self,source,raw):
        if not self.bound or source not in self.rows or type(raw)!=bytes:raise ValueError('Unbound SDK output descriptor')
        same_path(self.root,self.directory_fd)
        self.total+=len(raw)
        if self.total>LOG_LIMIT:raise ValueError('Aggregate SDK log quota exceeded')
        row=self.rows[source];same_path(row['path'],row['fd'])
        view=memoryview(raw)
        while view:
            count=os.write(row['fd'],view)
            if count<=0:raise OSError('SDK log short write')
            view=view[count:]
        row['hash'].update(raw);row['bytes']+=len(raw)
    def close(self):
        errors=[];manifest=[]
        for row in self.rows.values():
            if row['fd'] is not None:
                try:
                    same_path(row['path'],row['fd']);os.fsync(row['fd'])
                    manifest.append({'name':row['path'].name,'bytes':row['bytes'],'sha256':row['hash'].hexdigest()})
                except BaseException as failure:errors.append(failure)
                finally:
                    try:os.close(row['fd']);row['fd']=None
                    except BaseException as failure:errors.append(failure)
        if self.directory_fd is not None:
            try:
                same_path(self.root,self.directory_fd);os.fsync(self.directory_fd)
                if self.bound and not errors:
                    write_new(self.root/(self.run+'-logs.json'),{'owner':OWNER,'run':self.run,'files':manifest,'aggregateBytes':self.total})
            except BaseException as failure:errors.append(failure)
            finally:
                try:os.close(self.directory_fd);self.directory_fd=None
                except BaseException as failure:errors.append(failure)
        self.bound=False
        if errors:raise errors[0]
