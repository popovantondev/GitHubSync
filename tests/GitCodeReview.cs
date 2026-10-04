using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

internal sealed class LocalGitRemote : IGitCodeRunner
{
    internal readonly GitCodeNativeRunner Native=new GitCodeNativeRunner();
    internal string Server, Git, Directory, RewindTo;
    internal int Pushes, Calls;
    internal bool LoseReply, Reject, Reject502;
    internal readonly List<string[]> Commands=new List<string[]>();
    public GitCommandResult Run(string executable,string directory,string[] arguments,Stream input,int seconds,Action<string> progress)
    {
        Calls++;Commands.Add(arguments);
        var list=arguments.ToList();bool pushing=list.Contains("push");
        for(int i=0;i<list.Count;i++)if(list[i]=="https://github.com/example/test.git")list[i]=Server;
        list.InsertRange(0,new[]{"-c","protocol.file.allow=always"}); // Only this offline test maps the fixed URL to its own bare repo.
        if(pushing) {
            Pushes++;
            if(Reject)return new GitCommandResult {ExitCode=1,Output="",Error="remote: GH013: protected branch; synthetic-secret-never-log"};
            if(Reject502)return new GitCommandResult {ExitCode=1,Output="",Error="error: RPC failed; HTTP 502 curl 22; synthetic-secret-never-log"};
            if(RewindTo!=null)GitCodeReview.NativeCommand(Native,Git,Directory,new[]{"--git-dir="+Server,"update-ref","refs/heads/main",RewindTo});
            if(progress!=null){progress("remote: synthetic-secret-never-log");progress("Writing objects: 150% (2/1)");progress("Writing objects: 85% (6/7), 10.50 MiB | 35.00 KiB/s");}
        }
        var result=Native.Run(executable,directory,list.ToArray(),input,seconds,progress);
        if(pushing && result.ExitCode==0 && LoseReply)throw new GitCodeException("connection");
        return result;
    }
}
internal static class GitCodeReview
{
    private static int checks;
    private static void Assert(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
    internal static string NativeCommand(GitCodeNativeRunner runner,string git,string directory,string[] args,byte[] input=null)
    {
        using(var stream=input==null ? null : new MemoryStream(input)) {
            var result=runner.Run(git,directory,args,stream,30,null);
            if(result.ExitCode!=0)throw new Exception("Artificial Git fixture command failed: "+String.Join(" ",args)+"; "+result.Error);
            return result.Output.Trim();
        }
    }
    private static string Blob(GitCodeNativeRunner native,string git,string directory,string repo,byte[] bytes) {return NativeCommand(native,git,directory,new[]{"--git-dir="+repo,"hash-object","-w","--stdin"},bytes);}
    private static string Commit(GitCodeNativeRunner native,string git,string directory,string repo,string tree,string parent="")
    {
        var args=new List<string>{"-c","user.name=Artificial test","-c","user.email=1+artificial@users.noreply.github.com","--git-dir="+repo,"commit-tree",tree};if(parent!="")args.AddRange(new[]{"-p",parent});
        return NativeCommand(native,git,directory,args.ToArray(),Encoding.UTF8.GetBytes("Artificial fixture\n"));
    }
    private static string Hash(byte[] bytes) {
        using(var sha=SHA1.Create()){byte[] header=Encoding.ASCII.GetBytes("blob "+bytes.Length+"\0");sha.TransformBlock(header,0,header.Length,header,0);sha.TransformFinalBlock(bytes,0,bytes.Length);return BitConverter.ToString(sha.Hash).Replace("-","").ToLowerInvariant();}
    }
    private static GitCodeFile FileEntry(string directory,string name,string target,byte[] bytes,string mode="100644") {
        string path=Path.Combine(directory,name);File.WriteAllBytes(path,bytes);return new GitCodeFile {SourcePath=path,Name=name,Target=target,Sha=Hash(bytes),Length=bytes.Length,Mode=mode};
    }
    private static GitCodeResult Upload(LocalGitRemote remote,string head,string tree,GitCodeFile[] files,string progress) {
        File.WriteAllText(progress,"{\"state\":\"preparing\",\"mode\":\"code\",\"fileSent\":0}");
        return GitCodeTransport.Upload(remote.Git,"example/test","main",head,tree,files,"artificial",1,30,progress,remote);
    }
    private static int Main(string[] args)
    {
        try {
            string git=args[0],directory=args[1];System.IO.Directory.CreateDirectory(directory);
            string caches=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GitHubSync","code-transfers");
            var priorCaches=new HashSet<string>(System.IO.Directory.Exists(caches) ? System.IO.Directory.GetDirectories(caches) : new string[0],StringComparer.OrdinalIgnoreCase);
            var native=new GitCodeNativeRunner();string server=Path.Combine(directory,"server.git"),progress=Path.Combine(directory,"progress.json");
            // Reproduce UTF-8 Windows/CI consoles whose encoding has a BOM.
            Console.InputEncoding=new UTF8Encoding(true);
            NativeCommand(native,git,directory,new[]{"init","--bare","--object-format=sha1",server});
            string keep=Blob(native,git,directory,server,Encoding.UTF8.GetBytes("untouched\n")),old=Blob(native,git,directory,server,Encoding.UTF8.GetBytes("old script\n")),link=Blob(native,git,directory,server,Encoding.UTF8.GetBytes("keep.txt"));
            Assert(keep==Hash(Encoding.UTF8.GetBytes("untouched\n")),"UTF-8 console cannot inject BOM into raw Git input");
            NativeCommand(native,git,directory,new[]{"--git-dir="+server,"update-index","--add","-z","--index-info"},Encoding.UTF8.GetBytes("100644 "+keep+"\tkeep.txt\0"+"100755 "+old+"\tscript.sh\0"+"120000 "+link+"\tlink\0"));
            string tree=NativeCommand(native,git,directory,new[]{"--git-dir="+server,"write-tree"}),baseCommit=Commit(native,git,directory,server,tree);
            NativeCommand(native,git,directory,new[]{"--git-dir="+server,"update-ref","refs/heads/main",baseCommit});
            byte[] binary=new byte[61*1024*1024+2];using(var rng=RandomNumberGenerator.Create())rng.GetBytes(binary);
            var files=new[]{FileEntry(directory,"raw binary.bin","nested/файл ; ' Unicode.bin",binary),FileEntry(directory,"script-source.txt","script.sh",Encoding.UTF8.GetBytes("new script\r\n"),"100755"),FileEntry(directory,"empty.txt","empty.txt",new byte[0])};
            var remote=new LocalGitRemote {Server=server,Git=git,Directory=directory};
            var result=Upload(remote,baseCommit,tree,files,progress);
            Assert(remote.Pushes==1,"Exactly one push, no retries");
            Assert(NativeCommand(native,git,directory,new[]{"--git-dir="+server,"rev-parse","refs/heads/main"})==result.Commit,"Native Git commits into chosen branch");
            Assert(NativeCommand(native,git,directory,new[]{"--git-dir="+server,"rev-parse",result.Commit+"^"})==baseCommit,"One direct-parent commit");
            string entries=NativeCommand(native,git,directory,new[]{"--git-dir="+server,"ls-tree","-r",result.Commit});
            Assert(entries.Contains(keep+"\tkeep.txt") && entries.Contains("120000 blob "+link+"\tlink"),"Unrelated file and symlink unchanged");
            foreach(var file in files)Assert(NativeCommand(native,git,directory,new[]{"--git-dir="+server,"rev-parse",result.Commit+":"+file.Target})==file.Sha,"Raw binary/Unicode/CRLF/empty blob SHA confirmed");
            Assert(entries.Contains("100755 blob "+files[1].Sha),"Executable mode preserved");
            Assert(!remote.Commands.Any(c=>c.Any(a=>a.StartsWith("--force",StringComparison.Ordinal) || a=="--no-verify")),"No force, lease or hook bypass");
            Assert(remote.Commands.Count(c=>c.Contains("commit-tree"))==1,"Only one commit object prepared");
            Assert(remote.Commands.Any(c=>c.Contains("hash-object") && c.Contains("--no-filters")),"No attribute/CRLF transformation");
            Assert(!File.ReadAllText(progress).Contains("synthetic-secret"),"Raw Git stderr never written to snapshot");
            var state=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(progress));
            Assert((string)state["gitStage"]=="confirming" && !(bool)state["fileProgressKnown"],"Commit confirmation separate from object progress, no fabricated file bytes");
            Assert(Convert.ToInt64(state["gitPackBytes"])>=11010048,"Git pack size parses scaled invariant units; retained through confirmation");
            string newTree=NativeCommand(native,git,directory,new[]{"--git-dir="+server,"rev-parse",result.Commit+"^{tree}"});
            remote.Pushes=0;var same=Upload(remote,result.Commit,newTree,files,progress);Assert(same.NoChanges && same.Commit==result.Commit && remote.Pushes==0,"Same tree creates no empty commit/push");
            var replacement=FileEntry(directory,"small.txt","small.txt",Encoding.UTF8.GetBytes("second commit"));
            remote.LoseReply=true;bool lost=false;string acknowledged="";
            try{Upload(remote,result.Commit,newTree,new[]{replacement},progress);}catch(GitCodeException e){lost=e.Category=="connection";acknowledged=e.Commit;}
            Assert(lost && acknowledged==NativeCommand(native,git,directory,new[]{"--git-dir="+server,"rev-parse","refs/heads/main"}) && remote.Pushes==1,"Lost reply carries exact commit for read-only reconciliation; no repeated push");
            remote.LoseReply=false;remote.Pushes=0;
            bool failed=false;try{Upload(remote,result.Commit,newTree,new[]{replacement},progress);}catch(GitCodeException e){failed=e.Category=="branch-changed";}
            Assert(failed && remote.Pushes==0,"Branch changed before fetch prevents push");
            NativeCommand(native,git,directory,new[]{"--git-dir="+server,"update-ref","refs/heads/main",result.Commit});
            remote.RewindTo=baseCommit;remote.Pushes=0;failed=false;
            try{Upload(remote,result.Commit,newTree,new[]{replacement},progress);}catch(GitCodeException e){failed=e.Category=="branch-changed";}
            Assert(failed && remote.Pushes==1 && NativeCommand(native,git,directory,new[]{"--git-dir="+server,"rev-parse","refs/heads/main"})==baseCommit,"Trusted pre-push guard rejects even a fast-forward-safe remote rewind");
            remote.RewindTo=null;remote.Pushes=0;remote.Reject=true;failed=false;
            try{Upload(remote,baseCommit,tree,new[]{replacement},progress);}catch(GitCodeException e){failed=e.Category=="permission" && e.Commit.Length==40 && !e.Message.Contains("synthetic-secret");}
            Assert(failed && remote.Pushes==1 && NativeCommand(native,git,directory,new[]{"--git-dir="+server,"rev-parse","refs/heads/main"})==baseCommit,"Protected/access rejection not retried, sanitized and branch unchanged");
            remote.Reject=false;remote.Reject502=true;remote.Pushes=0;failed=false;
            try{Upload(remote,baseCommit,tree,new[]{replacement},progress);}catch(GitCodeException e){failed=e.Category=="connection" && e.HttpStatus==502 && e.Commit.Length==40 && !e.Message.Contains("synthetic-secret");}
            Assert(failed && remote.Pushes==1 && NativeCommand(native,git,directory,new[]{"--git-dir="+server,"rev-parse","refs/heads/main"})==baseCommit,"HTTP 502 is sanitized, never retried and carries commit for reconciliation");
            remote.Reject502=false;remote.Pushes=0;replacement.Sha=files[0].Sha;failed=false;
            try{Upload(remote,baseCommit,tree,new[]{replacement},progress);}catch(GitCodeException e){failed=e.Category=="local-changed";}
            Assert(failed && remote.Pushes==0,"Changed local bytes rejected before push");
            foreach(string bad in new[]{"../escape",".git/config",".githubsync/state","nested\\escape","a:stream","a\nnewline"}) {
                int calls=remote.Calls;replacement.Target=bad;failed=false;try{Upload(remote,baseCommit,tree,new[]{replacement},progress);}catch(GitCodeException e){failed=e.Category=="invalid-request";}
                Assert(failed && remote.Calls==calls,"Invalid target rejected before any Git process");
            }
            replacement.Target="small.txt";replacement.Length=100L*1024*1024+1;failed=false;
            try{Upload(remote,baseCommit,tree,new[]{replacement},progress);}catch(GitCodeException e){failed=e.Category=="invalid-request";}
            Assert(failed && remote.Pushes==0,"Oversized Code file blocked before push");
            string marker=Path.Combine(directory,"child.pid");var clock=Stopwatch.StartNew();failed=false;
            string shell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"WindowsPowerShell","v1.0","powershell.exe");
            string program="$child=Start-Process -FilePath '"+shell.Replace("'","''")+"' -WindowStyle Hidden -ArgumentList '-NoProfile -NonInteractive -Command Start-Sleep -Seconds 30' -PassThru; [IO.File]::WriteAllText('"+marker.Replace("'","''")+"',$child.Id.ToString()); $child.WaitForExit()";
            try{native.Run(shell,directory,new[]{"-NoLogo","-NoProfile","-NonInteractive","-Command",program},null,3,null);}catch(GitCodeException e){failed=e.Category=="timeout";}
            Assert(failed && clock.Elapsed.TotalSeconds<10,"Native process deadline terminates the entire job");
            Assert(File.Exists(marker),"Deadline fixture actually started its contained process");
            if(File.Exists(marker)){int pid=Int32.Parse(File.ReadAllText(marker).Trim());bool ended=false;try{using(var p=Process.GetProcessById(pid))ended=p.HasExited;}catch(ArgumentException){ended=true;}Assert(ended,"Timed-out native descendant does not survive");}
            Assert(!System.IO.Directory.GetDirectories(caches).Any(p=>!priorCaches.Contains(p)),"Generated Git sessions are cleaned after success/no-op/refusal");
            Console.WriteLine("Native Git Code passed: "+checks+" assertions; real local 61 MiB binary, one commit, raw hashes/modes, no-op, conflict/rewind, access rejection, lost reply, input guards, hidden job-contained processes. No network or credentials.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
    }
}
