using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

public sealed class GitCodeException : IOException
{
    public readonly string Category, Commit;
    public readonly int HttpStatus;
    public GitCodeException(string category, string commit = "", int httpStatus=0) : base("Git transfer: " + category) { Category = category; Commit = commit; HttpStatus=httpStatus; }
}
public sealed class GitCodeFile
{
    public string SourcePath, Name, Target, Sha, Mode;
    public long Length;
}
public sealed class GitCodeResult { public string Commit; public bool NoChanges; }
public sealed class GitCommandResult { public int ExitCode; public string Output, Error; }
// Test seam: production always uses the hidden, job-contained native runner.
public interface IGitCodeRunner
{
    GitCommandResult Run(string executable, string directory, string[] arguments, Stream input, int seconds, Action<string> progress);
}
public sealed class GitCodeNativeRunner : IGitCodeRunner
{
    private static readonly object InputHandleGate=new object();
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes { public int Length; public IntPtr Descriptor; public int Inherit; }
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool CreatePipe(out SafeFileHandle read,out SafeFileHandle write,ref SecurityAttributes attributes,uint size);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetHandleInformation(SafeFileHandle handle,uint mask,uint flags);
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int kind);
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool SetStdHandle(int kind,IntPtr handle);
    private static void AppendTail(StringBuilder buffer,string line)
    {
        const int maximum=65536;
        if(line.Length>=maximum){buffer.Clear();buffer.Append(line.Substring(line.Length-maximum));return;}
        int excess=buffer.Length+line.Length+2-maximum;
        if(excess>0)buffer.Remove(0,excess);
        buffer.AppendLine(line);
    }
    private sealed class JobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public JobHandle() : base(true) { }
        protected override bool ReleaseHandle() { return CloseHandle(handle); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct BasicLimits { public long ProcessTime, JobTime; public uint Flags; public UIntPtr MinWorkingSet, MaxWorkingSet; public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling; }
    [StructLayout(LayoutKind.Sequential)] private struct IoCounters { public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes; }
    [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] private static extern JobHandle CreateJobObject(IntPtr security, string name);
    [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(JobHandle job, int information, ref ExtendedLimits limits, uint size);
    [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(JobHandle job, IntPtr process);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    public static string QuoteArgument(string value)
    {
        if(value==null || value.IndexOf('\0')>=0) throw new GitCodeException("invalid-request");
        var result=new StringBuilder("\""); int slashes=0;
        foreach(char c in value) {
            if(c=='\\') {slashes++;continue;}
            if(c=='\"') {result.Append('\\',slashes*2+1);result.Append(c);slashes=0;continue;}
            result.Append('\\',slashes);slashes=0;result.Append(c);
        }
        result.Append('\\',slashes*2);return result.Append('"').ToString();
    }
    public GitCommandResult Run(string executable, string directory, string[] arguments, Stream input, int seconds, Action<string> progress)
    {
        var info=new ProcessStartInfo {FileName=executable,WorkingDirectory=directory,Arguments=String.Join(" ",arguments.Select(QuoteArgument)),UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardInput=false,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
        // Inherited Git environment must not redirect objects/indexes or trace credentials.
        foreach(string key in info.EnvironmentVariables.Keys.Cast<string>().ToArray())
            if(key.StartsWith("GIT_",StringComparison.OrdinalIgnoreCase) || key.StartsWith("GCM_",StringComparison.OrdinalIgnoreCase)) info.EnvironmentVariables.Remove(key);
        string gitRoot=Path.GetDirectoryName(Path.GetDirectoryName(executable));
        info.EnvironmentVariables["PATH"]=Path.GetDirectoryName(executable)+";"+Path.Combine(gitRoot,"mingw64","bin")+";"+Path.Combine(gitRoot,"usr","bin")+";"+info.EnvironmentVariables["PATH"];
        info.EnvironmentVariables["GIT_TERMINAL_PROMPT"]="0";info.EnvironmentVariables["GCM_INTERACTIVE"]="Never";
        info.EnvironmentVariables["GIT_CONFIG_NOSYSTEM"]="1";info.EnvironmentVariables["GIT_CONFIG_GLOBAL"]="NUL";
        info.EnvironmentVariables["GCM_TRACE"]="0";info.EnvironmentVariables["GCM_TRACE_SECRETS"]="0";
        var output=new StringBuilder();var error=new StringBuilder();object gate=new object();
        using(var job=CreateJobObject(IntPtr.Zero,null)) {
            var limits=new ExtendedLimits();limits.Basic.Flags=0x2000; // KILL_ON_JOB_CLOSE, including descendants if the worker exits.
            if(job.IsInvalid || !SetInformationJobObject(job,9,ref limits,(uint)Marshal.SizeOf(limits))) throw new GitCodeException("process-control");
            using(var process=new Process {StartInfo=info}) {
                process.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e) {if(e.Data!=null)lock(gate){AppendTail(output,e.Data);}};
                process.ErrorDataReceived+=delegate(object sender,DataReceivedEventArgs e) {
                    if(e.Data==null)return;
                    lock(gate){AppendTail(error,e.Data);}
                    if(progress!=null)try{progress(e.Data);}catch{/* Snapshot errors do not repeat a push. */}
                };
                // Supply a raw inherited pipe, not Framework's encoding-dependent
                // StreamWriter. This works in a winexe with no console as well.
                var attributes=new SecurityAttributes {Length=Marshal.SizeOf(typeof(SecurityAttributes)),Inherit=1};
                SafeFileHandle inputRead,inputWrite;
                if(!CreatePipe(out inputRead,out inputWrite,ref attributes,0))throw new GitCodeException("process-control");
                using(inputRead) using(inputWrite) {
                if(!SetHandleInformation(inputWrite,1,0))throw new GitCodeException("process-control");
                lock(InputHandleGate) {
                    IntPtr prior=GetStdHandle(-10);
                    if(!SetStdHandle(-10,inputRead.DangerousGetHandle()))throw new GitCodeException("process-control");
                    try {if(!process.Start())throw new GitCodeException("process-control");}
                    finally {SetStdHandle(-10,prior);}
                }
                inputRead.Dispose(); // Child owns its duplicate; parent must not keep EOF open.
                if(!AssignProcessToJobObject(job,process.Handle)){try{process.Kill();}catch{}throw new GitCodeException("process-control");}
                process.BeginOutputReadLine();process.BeginErrorReadLine();
                var writer=Task.Run(delegate {using(var pipe=new FileStream(inputWrite,FileAccess.Write,262144,false)){if(input!=null)input.CopyTo(pipe,262144);}});
                if(!process.WaitForExit(seconds*1000)){job.Dispose();try{process.WaitForExit(5000);}catch{}throw new GitCodeException("timeout");}
                process.WaitForExit(); // Drain asynchronous output handlers before disposing.
                try{writer.GetAwaiter().GetResult();}catch{if(process.ExitCode==0)throw new GitCodeException("local-read");}
                lock(gate)return new GitCommandResult {ExitCode=process.ExitCode,Output=output.ToString(),Error=error.ToString()};
                }
            }
        }
    }
}
public static class GitCodeTransport
{
    private static readonly Regex Sha=new Regex("^[a-f0-9]{40}$",RegexOptions.CultureInvariant);
    private static string ShellQuote(string value) { return "'"+value.Replace("'","'\"'\"'")+"'"; }
    private static int HttpStatus(GitCommandResult response) {
        var match=Regex.Match(response.Error??"",@"\b(?:HTTP(?:/[0-9.]+)?\s+|URL returned error:\s*)([45][0-9]{2})\b",RegexOptions.CultureInvariant|RegexOptions.IgnoreCase);
        int status;return match.Success && Int32.TryParse(match.Groups[1].Value,out status) ? status : 0;
    }
    private static string Classification(GitCommandResult response)
    {
        string error=(response.Error??"").ToLowerInvariant();int status=HttpStatus(response);
        if(error.Contains("authentication failed") || error.Contains("could not read username") || error.Contains("could not read password") || status==401 || error.Contains("no accounts"))return "authentication";
        if(error.Contains("githubsync-base-changed") || error.Contains("non-fast-forward") || error.Contains("fetch first") || error.Contains("stale info"))return "branch-changed";
        if(error.Contains("gh001") || error.Contains("exceeds github's file size limit") || status==413)return "size";
        if(error.Contains("permission") || error.Contains("protected branch") || error.Contains("gh006") || error.Contains("gh013") || status==403 || error.Contains("repository rule") || error.Contains("workflow scope"))return "permission";
        if(error.Contains("repository not found") || status==404)return "not-found";
        return "connection"; // Never return captured process/server output or credentials.
    }
    private sealed class Progress
    {
        private readonly string path;
        private readonly Dictionary<string,object> snapshot;
        private readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=16*1024*1024};
        private readonly object gate=new object();
        public Progress(string value) {path=value;try{snapshot=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path,Encoding.UTF8));}catch{snapshot=new Dictionary<string,object>();}}
        private void Save() {if(String.IsNullOrEmpty(path))return;try{string temp=path+".tmp";File.WriteAllText(temp,json.Serialize(snapshot),new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}catch{}}
        public void Stage(string stage,string file="") {
            lock(gate){snapshot["state"]=stage=="sending" ? "uploading" : stage=="confirming" ? "committing" : "preparing";snapshot["transferKind"]="git";snapshot["gitStage"]=stage;snapshot["file"]=file;snapshot["fileBytes"]=0;snapshot["fileSent"]=0;snapshot["fileProgressKnown"]=false;snapshot["gitProgressKnown"]=false;if(stage!="confirming")snapshot["gitPackBytes"]=0;snapshot["updated"]=DateTime.UtcNow.ToString("o");Save();}
        }
        public void Line(string line) {
            var match=Regex.Match(line,@"^Writing objects:\s+(\d{1,3})% \((\d+)/(\d+)\)",RegexOptions.CultureInvariant);
            if(!match.Success)return;
            long done,total;int percent;
            if(!Int32.TryParse(match.Groups[1].Value,out percent) || !Int64.TryParse(match.Groups[2].Value,out done) || !Int64.TryParse(match.Groups[3].Value,out total) || percent>100 || total<=0 || done>total)return;
            var pack=Regex.Match(line,@",\s+([0-9]+(?:\.[0-9]+)?)\s+(bytes|KiB|MiB|GiB)(?:\s|,|$)",RegexOptions.CultureInvariant);
            double amount=0;long packBytes=0;
            if(pack.Success && Double.TryParse(pack.Groups[1].Value,System.Globalization.NumberStyles.AllowDecimalPoint,System.Globalization.CultureInfo.InvariantCulture,out amount)) {
                double factor=pack.Groups[2].Value=="GiB" ? 1073741824.0 : pack.Groups[2].Value=="MiB" ? 1048576.0 : pack.Groups[2].Value=="KiB" ? 1024.0 : 1.0;
                if(amount>=0 && amount*factor<Int64.MaxValue)packBytes=(long)(amount*factor);
            }
            lock(gate){snapshot["gitProgressKnown"]=true;snapshot["gitObjectsPercent"]=percent;snapshot["gitObjectsDone"]=done;snapshot["gitObjectsTotal"]=total;if(packBytes>0)snapshot["gitPackBytes"]=packBytes;snapshot["updated"]=DateTime.UtcNow.ToString("o");Save();}
        }
    }
    public static GitCodeResult Upload(string git,string repository,string branch,string baseCommit,string baseTree,GitCodeFile[] files,string login,long accountId,int seconds,string progressPath,IGitCodeRunner runner=null)
    {
        if(!Regex.IsMatch(repository??"",@"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$") || !Sha.IsMatch(baseCommit??"") || !Sha.IsMatch(baseTree??"") || !Regex.IsMatch(login??"",@"^[A-Za-z0-9-]{1,39}$") || accountId<=0 || seconds<1 || seconds>86400 || String.IsNullOrEmpty(branch) || branch.Length>1024 || branch.Any(c=>c<32) || files==null || files.Length==0)throw new GitCodeException("invalid-request");
        foreach(var file in files) {
            if(file==null || !Sha.IsMatch(file.Sha??"") || (file.Mode!="100644" && file.Mode!="100755") || file.Length<0 || file.Length>100L*1024*1024 || String.IsNullOrEmpty(file.Target) || file.Target.StartsWith("/",StringComparison.Ordinal) || file.Target.IndexOf('\\')>=0 || file.Target.Any(c=>c<32 || c==':') || file.Target.Split('/').Any(p=>p=="" || p=="." || p==".." || p.Equals(".git",StringComparison.OrdinalIgnoreCase) || p.Equals(".githubsync",StringComparison.OrdinalIgnoreCase)))throw new GitCodeException("invalid-request");
        }
        string manager=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(git)),"mingw64","bin","git-credential-manager.exe");
        if(!File.Exists(git) || !File.Exists(manager))throw new GitCodeException("missing-runtime");
        string parent=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GitHubSync","code-transfers");
        WindowsPathSafety.AssertNoLinks(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));Directory.CreateDirectory(Path.GetDirectoryName(parent));WindowsPathSafety.AssertNoLinks(Path.GetDirectoryName(parent));Directory.CreateDirectory(parent);WindowsPathSafety.AssertNoLinks(parent);
        string session=Path.Combine(parent,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(session);
        string objects=Path.Combine(session,"repository.git"),hooks=Path.Combine(session,"hooks"),template=Path.Combine(session,"template");Directory.CreateDirectory(objects);Directory.CreateDirectory(hooks);Directory.CreateDirectory(template);
        string url="https://github.com/"+repository+".git", reference="refs/heads/"+branch, commit="";
        var progress=new Progress(progressPath);runner=runner??new GitCodeNativeRunner();
        string[] config={"-c","core.hooksPath="+hooks,"-c","credential.helper=","-c","credential.helper=!"+ShellQuote(manager.Replace('\\','/')),"-c","credential.username="+login,"-c","credential.useHttpPath=false","-c","credential.gitHubAccountFiltering=true","-c","credential.interactive=false","-c","http.sslVerify=true","-c","http.followRedirects=false","-c","protocol.allow=never","-c","protocol.https.allow=always","-c","commit.gpgSign=false","-c","user.name="+login,"-c","user.email="+accountId.ToString(System.Globalization.CultureInfo.InvariantCulture)+"+"+login+"@users.noreply.github.com","-c","core.autocrlf=false"};
        Func<string[],Stream,bool,string> run=delegate(string[] command,Stream input,bool pushing) {
            GitCommandResult response;
            try{response=runner.Run(git,session,config.Concat(new[]{"--git-dir="+objects}).Concat(command).ToArray(),input,seconds,pushing ? (Action<string>)progress.Line : null);}
            catch(GitCodeException e){throw new GitCodeException(e.Category,pushing ? commit : "",e.HttpStatus);}
            catch{throw new GitCodeException("connection",pushing ? commit : "");}
            if(response.ExitCode!=0)throw new GitCodeException(Classification(response),pushing ? commit : "",HttpStatus(response));
            return (response.Output??"").Trim();
        };
        try {
            progress.Stage("preparing");
            run(new[]{"check-ref-format",reference},null,false);
            run(new[]{"init","--bare","--object-format=sha1","--template="+template,objects},null,false);
            run(new[]{"fetch","--no-tags","--no-recurse-submodules","--depth=1",url,reference},null,false);
            // Fetch only this branch's current snapshot, never checkout user data.
            if(run(new[]{"rev-parse","FETCH_HEAD"},null,false)!=baseCommit || run(new[]{"rev-parse",baseCommit+"^{tree}"},null,false)!=baseTree)throw new GitCodeException("branch-changed");
            run(new[]{"read-tree",baseTree},null,false);
            var index=new StringBuilder();
            foreach(var file in files) {
                progress.Stage("preparing",file.Name);WindowsPathSafety.AssertNoLinks(file.SourcePath);
                using(var input=new FileStream(file.SourcePath,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                    if(input.Length!=file.Length)throw new GitCodeException("local-changed");
                    if(run(new[]{"hash-object","-w","--no-filters","--stdin"},input,false)!=file.Sha)throw new GitCodeException("local-changed");
                }
                index.Append(file.Mode).Append(' ').Append(file.Sha).Append('\t').Append(file.Target).Append('\0');
            }
            using(var input=new MemoryStream(Encoding.UTF8.GetBytes(index.ToString())))run(new[]{"update-index","--add","-z","--index-info"},input,false);
            string tree=run(new[]{"write-tree"},null,false);if(!Sha.IsMatch(tree))throw new GitCodeException("invalid-response");
            if(tree==baseTree)return new GitCodeResult {Commit=baseCommit,NoChanges=true};
            using(var input=new MemoryStream(Encoding.UTF8.GetBytes("Upload files with GitHubSync\n")))commit=run(new[]{"commit-tree",tree,"-p",baseCommit},input,false);
            if(!Sha.IsMatch(commit))throw new GitCodeException("invalid-response");
            // The hook checks the head advertised by receive-pack, even if it was
            // rewound after preview. The server then atomically compares that head.
            // No --force / lease / hook bypass / deletion refspec is used.
            File.WriteAllText(Path.Combine(hooks,"pre-push"),"#!/bin/sh\ncount=0\nwhile read local_ref local_sha remote_ref remote_sha; do\n  if [ \"$remote_ref\" != "+ShellQuote(reference)+" ] || [ \"$remote_sha\" != "+ShellQuote(baseCommit)+" ] || [ \"$local_sha\" != "+ShellQuote(commit)+" ]; then\n    echo GitHubSync-base-changed >&2\n    exit 1\n  fi\n  count=$((count+1))\ndone\n[ \"$count\" = 1 ] || exit 1\n",new UTF8Encoding(false));
            progress.Stage("sending");
            run(new[]{"push","--porcelain","--progress","--verify","--no-follow-tags","--recurse-submodules=no",url,commit+":"+reference},null,true);
            progress.Stage("confirming");
            return new GitCodeResult {Commit=commit,NoChanges=false}; // Worker still verifies the chosen branch through GET.
        } finally {
            // Only this generated, exact session is eligible for cleanup. If a
            // link appeared, leave it alone rather than following/deleting it.
            try {
                WindowsPathSafety.AssertNoLinks(session);var directories=new Queue<string>();var entries=new List<string>();directories.Enqueue(session);
                while(directories.Count>0)foreach(string entry in Directory.EnumerateFileSystemEntries(directories.Dequeue())) {
                    if(WindowsPathSafety.IsUnsafeReparsePoint(entry))throw new IOException();
                    if(Directory.Exists(entry))directories.Enqueue(entry);
                    else entries.Add(entry);
                }
                // Git object files can be read-only. Only clear that attribute
                // inside this fully checked, generated session.
                foreach(string entry in entries){WindowsPathSafety.AssertNoLinks(entry);File.SetAttributes(entry,File.GetAttributes(entry)&~FileAttributes.ReadOnly);}
                Directory.Delete(session,true);
            }catch{}
        }
    }
}
