using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.6.2")]

internal sealed class SyntheticHttp : HttpMessageHandler
{
    internal Func<HttpRequestMessage,HttpResponseMessage> Reply;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancel) {cancel.ThrowIfCancellationRequested();return Task.FromResult(Reply(request));}
}
internal sealed class InterruptedContent : HttpContent
{
    private readonly byte[] bytes; private readonly string control;
    internal InterruptedContent(byte[] value,string path){bytes=value;control=path;Headers.ContentLength=value.Length;}
    protected override Task SerializeToStreamAsync(Stream stream,TransportContext context){return stream.WriteAsync(bytes,0,bytes.Length);}
    protected override bool TryComputeLength(out long length){length=bytes.Length;return true;}
    protected override Task<Stream> CreateContentReadStreamAsync(){return Task.FromResult<Stream>(new InterruptedStream(bytes,control));}
}
internal sealed class InterruptedStream : MemoryStream
{
    private readonly string control;
    internal InterruptedStream(byte[] bytes,string path):base(bytes){control=path;}
    public override Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken cancel){if(Position>0){File.WriteAllText(control,"pause");throw new OperationCanceledException();}return base.ReadAsync(buffer,offset,count,cancel);}
}
internal static class SyncDownloadReview
{
    private static int checks;
    private static string fixture;
    private static readonly byte[] Data=Enumerable.Range(0,220000).Select(n=>(byte)(n%251)).ToArray();
    private static void Assert(bool value,string message){checks++;if(!value)throw new InvalidOperationException(message);}
    private static void Reject(Action action,string message){bool failed=false;try{action();}catch{failed=true;}Assert(failed,message);}
    private static SyncPlan Plan(string sub,string name,byte[] data)
    {
        string directory=Path.Combine(fixture,sub);Directory.CreateDirectory(directory);string expected=Path.Combine(fixture,"hash-source.bin");File.WriteAllBytes(expected,data);
        return new SyncPlan {repository="example/artificial",contentKind="release",sourceId="fixture-version",directory=directory,resultUrl="https://github.com/example/artificial/releases/tag/test",
            entries=new[]{new SyncEntry {name=name,length=data.Length,hashKind="sha256",digest=SyncTransfer.HashFile(expected,"sha256"),accept="application/octet-stream",url="https://api.github.com/repos/example/artificial/releases/assets/1",revision="fixture-v1"}}};
    }
    private static HttpResponseMessage Response(byte[] bytes,int status=200,long offset=0,long total=0)
    {
        var result=new HttpResponseMessage((HttpStatusCode)status){Content=new ByteArrayContent(bytes)};result.Headers.ETag=new EntityTagHeaderValue("\"fixture-etag\"");
        if(status==206)result.Content.Headers.ContentRange=new ContentRangeHeaderValue(offset,offset+bytes.Length-1,total);
        return result;
    }
    private static string Run(SyncPlan plan,SyntheticHttp handler,string control="") {return SyncTransfer.Run(plan,"synthetic-memory-only",Path.Combine(fixture,"progress.json"),control,handler);}
    private static string Pause(SyncPlan plan,string control)
    {
        SyncTransfer.Preview(plan);
        Assert(Run(plan,new SyntheticHttp{Reply=request=>{var response=Response(Data);response.Content=new InterruptedContent(Data,control);return response;}},control)=="paused","Synthetic pause checkpoint");
        File.Delete(control);
        return Directory.GetFiles(Path.Combine(plan.directory,".githubsync","partials"),"*.part",SearchOption.AllDirectories).Single();
    }
    private static void Main(string[] args)
    {
        fixture=Path.GetFullPath(args[0]);
        // Test bookkeeping must also support a long OneDrive fixture root.
        if(!fixture.StartsWith(@"\\?\",StringComparison.Ordinal)) fixture=fixture.StartsWith(@"\\",StringComparison.Ordinal) ? @"\\?\UNC\"+fixture.Substring(2) : @"\\?\"+fixture;
        Directory.CreateDirectory(fixture);
        var plan=Plan("fresh","sub/данные.bin",Data);SyncTransfer.Preview(plan);Assert(plan.entries[0].action=="add","Nested binary add preview");
        int gets=0;var http=new SyntheticHttp{Reply=request=>{gets++;Assert(request.Method==HttpMethod.Get,"Download is GET only");Assert(request.Headers.Authorization.Parameter=="synthetic-memory-only","Token only in request memory");return Response(Data);}};
        Assert(Run(plan,http)=="completed","Confirmed completion");Assert(File.ReadAllBytes(Path.Combine(plan.directory,"sub","данные.bin")).SequenceEqual(Data),"Unicode and binary bytes");
        File.WriteAllText(Path.Combine(plan.directory,"keep.txt"),"retained");SyncTransfer.Preview(plan);Assert(plan.entries[0].action=="same","Matching hash skipped");
        Run(plan,new SyntheticHttp{Reply=request=>{throw new Exception("No GET for identical file");}});Assert(File.Exists(Path.Combine(plan.directory,"keep.txt")),"Unselected local files retained");
        File.WriteAllText(Path.Combine(plan.directory,"sub","данные.bin"),"old local content");SyncTransfer.Preview(plan);Assert(plan.entries[0].action=="update","Explicit replacement preview");Run(plan,new SyntheticHttp{Reply=request=>Response(Data)});
        var backups=Directory.GetFiles(Path.Combine(plan.directory,".githubsync","backups"),"*",SearchOption.AllDirectories);Assert(backups.Length==1 && File.ReadAllText(backups[0])=="old local content","Backup before replacement");
        SyncTransfer.Preview(plan);File.WriteAllText(Path.Combine(plan.directory,"sub","данные.bin"),"changed after consent");Reject(()=>Run(plan,new SyntheticHttp{Reply=request=>Response(Data)}),"Local change after preview stops operation");
        foreach(string path in new[]{"../escape","/absolute","folder/../../escape","a:stream","CON.txt","NUL","a./b",".githubsync/state",".git/config","a//b","a\\..\\b"})Reject(()=>SyncTransfer.SafePath(plan.directory,path),"Unsafe path rejected: "+path);
        var duplicate=Plan("case","A.txt",Data);duplicate.entries=new[]{duplicate.entries[0],new SyncEntry{name="a.txt",length=0}};Reject(()=>SyncTransfer.Preview(duplicate),"Case collisions rejected before writes");
        var blockedParent=Plan("parent-collision","a",Data);blockedParent.entries=new[]{blockedParent.entries[0],new SyncEntry{name="a/b.txt",length=0}};Reject(()=>SyncTransfer.Preview(blockedParent),"Selected file/folder collision rejected before writes");
        Assert(!Directory.Exists(Path.Combine(blockedParent.directory,".githubsync")),"Collision creates no partials");
        Reject(()=>SyncTransfer.SafePath(plan.directory,"a\\b"),"Literal Git backslash cannot alias a Windows folder path");
        var paused=Plan("resume","archive.zip",Data);SyncTransfer.Preview(paused);string control=Path.Combine(fixture,"pause-control");
        var interrupted=new SyntheticHttp{Reply=request=>{var response=Response(Data);response.Content=new InterruptedContent(Data,control);return response;}};
        Assert(Run(paused,interrupted,control)=="paused","Interrupted download checkpoints as paused");
        string part=Directory.GetFiles(Path.Combine(paused.directory,".githubsync","partials"),"*.part",SearchOption.AllDirectories).Single();long saved=new FileInfo(part).Length;Assert(saved>0 && saved<Data.Length,"Partial bytes retained");Assert(!File.Exists(Path.Combine(paused.directory,"archive.zip")),"Incomplete file not installed");File.Delete(control);
        var resumed=new SyntheticHttp{Reply=request=>{Assert(request.Headers.Range.Ranges.Single().From==saved,"Resume from actual on-disk offset");Assert(request.Headers.Contains("If-Range"),"Strong validator sent");return Response(Data.Skip((int)saved).ToArray(),206,saved,Data.Length);}};
        Assert(Run(paused,resumed)=="completed","206 resume completes");Assert(File.ReadAllBytes(Path.Combine(paused.directory,"archive.zip")).SequenceEqual(Data),"Resume checksum confirmed");
        var noRange=Plan("no-range","archive.zip",Data);SyncTransfer.Preview(noRange);Run(noRange,new SyntheticHttp{Reply=request=>{var response=Response(Data);response.Content=new InterruptedContent(Data,control);return response;}},control);File.Delete(control);
        Run(noRange,new SyntheticHttp{Reply=request=>{Assert(request.Headers.Range!=null,"Fallback initially asks to resume");return Response(Data);}});Assert(new FileInfo(Path.Combine(noRange.directory,"archive.zip")).Length==Data.Length,"200 response restarts, never appends");
        var changed=Plan("etag-changed","file.bin",Data);string changedPart=Pause(changed,control);
        Reject(()=>Run(changed,new SyntheticHttp{Reply=request=>{long offset=request.Headers.Range.Ranges.Single().From.Value;var response=Response(Data.Skip((int)offset).ToArray(),206,offset,Data.Length);response.Headers.ETag=new EntityTagHeaderValue("\"changed\"");return response;}}),"Changed ETag cannot append another representation");
        Assert(!File.Exists(Path.Combine(changed.directory,"file.bin")),"Changed representation is never installed");
        var invalidRange=Plan("range-invalid","file.bin",Data);Pause(invalidRange,control);
        Reject(()=>Run(invalidRange,new SyntheticHttp{Reply=request=>Response(new byte[]{1},206,0,Data.Length)}),"Wrong Content-Range stops before append");
        var range416=Plan("range-416","file.bin",Data);string part416=Pause(range416,control);long before416=new FileInfo(part416).Length;
        Reject(()=>Run(range416,new SyntheticHttp{Reply=request=>new HttpResponseMessage((HttpStatusCode)416){Content=new ByteArrayContent(new byte[0])}}),"416 cannot mark incomplete content as completed");
        Assert(new FileInfo(part416).Length==before416 && !File.Exists(Path.Combine(range416.directory,"file.bin")),"416 retains partial and leaves target absent");
        var oversize=Plan("partial-invalid","file.bin",Data);string oversizedPart=Pause(oversize,control);File.WriteAllBytes(oversizedPart,new byte[Data.Length+1]);
        Run(oversize,new SyntheticHttp{Reply=request=>{Assert(request.Headers.Range==null,"Oversized partial restarts from zero");return Response(Data);}});Assert(File.ReadAllBytes(Path.Combine(oversize.directory,"file.bin")).SequenceEqual(Data),"Oversized private partial can recover on explicit retry");
        var bad=Plan("bad-hash","data.bin",Data);SyncTransfer.Preview(bad);byte[] altered=(byte[])Data.Clone();altered[5]^=1;Reject(()=>Run(bad,new SyntheticHttp{Reply=request=>Response(altered)}),"Wrong checksum not accepted");Assert(!File.Exists(Path.Combine(bad.directory,"data.bin")),"Bad checksum leaves target absent");
        Run(bad,new SyntheticHttp{Reply=request=>{Assert(request.Headers.Range==null,"Complete corrupt partial restarts from zero");return Response(Data);}});Assert(File.Exists(Path.Combine(bad.directory,"data.bin")),"Explicit retry recovers corrupted completed partial");
        var redirect=Plan("redirect","file.bin",Data);SyncTransfer.Preview(redirect);int requests=0;
        Run(redirect,new SyntheticHttp{Reply=request=>{requests++;if(request.RequestUri.Host=="api.github.com"){var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=new Uri("https://release-assets.githubusercontent.com/artificial?temporary-signature=synthetic");return response;}Assert(request.Headers.Authorization==null,"Authorization stripped on CDN redirect");return Response(Data);}});
        Assert(requests==2,"API redirect followed safely");
        var unsafeRedirect=Plan("redirect-unsafe","file.bin",Data);SyncTransfer.Preview(unsafeRedirect);Reject(()=>Run(unsafeRedirect,new SyntheticHttp{Reply=request=>{var response=new HttpResponseMessage(HttpStatusCode.Redirect);response.Headers.Location=new Uri("http://untrusted.invalid/file");return response;}}),"Unsafe redirect blocked");
        var empty=Plan("empty","empty.txt",new byte[0]);SyncTransfer.Preview(empty);Run(empty,new SyntheticHttp{Reply=request=>Response(new byte[0])});Assert(new FileInfo(Path.Combine(empty.directory,"empty.txt")).Length==0,"Zero-byte download verified");
        var publicRead=Plan("expired-token","file.bin",Data);SyncTransfer.Preview(publicRead);int authRequests=0;
        Run(publicRead,new SyntheticHttp{Reply=request=>{authRequests++;if(authRequests==1)return new HttpResponseMessage(HttpStatusCode.Unauthorized){Content=new ByteArrayContent(new byte[0])};Assert(request.Headers.Authorization==null,"Expired token falls back to public anonymous read");return Response(Data);}});Assert(authRequests==2,"Public fallback tries once without opening login");
        var code=Plan("code-hash","docs/file.bin",Data);code.contentKind="code";code.entries[0].hashKind="git";code.entries[0].digest=SyncTransfer.HashFile(Path.Combine(fixture,"hash-source.bin"),"git");SyncTransfer.Preview(code);Run(code,new SyntheticHttp{Reply=request=>Response(Data)});SyncTransfer.Preview(code);Assert(code.entries[0].action=="same","Code Git blob identity confirms binary bytes");
        var lfs=Plan("lfs","large.bin",Encoding.UTF8.GetBytes("version https://git-lfs.github.com/spec/v1\noid sha256:artificial\nsize 1000\n"));lfs.contentKind="code";SyncTransfer.Preview(lfs);Reject(()=>Run(lfs,new SyntheticHttp{Reply=request=>Response(File.ReadAllBytes(Path.Combine(fixture,"hash-source.bin")))}),"LFS pointer is not silently presented as real data");Assert(!File.Exists(Path.Combine(lfs.directory,"large.bin")),"LFS target not installed");
        var sizeOnly=Plan("size-only","file.bin",Data);sizeOnly.entries[0].digest="";SyncTransfer.Preview(sizeOnly);Run(sizeOnly,new SyntheticHttp{Reply=request=>Response(Data)});var snapshot=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(fixture,"progress.json")));Assert((string)snapshot["verification"]=="size","No invented checksum guarantee");
        var huge=Plan("large-counter","big.bin",Data);huge.entries[0].length=5L*1024*1024*1024;Assert(huge.entries[0].length>Int32.MaxValue,"Large sizes remain Int64");huge.contentKind="code";SyncTransfer.Preview(huge);Reject(()=>Run(huge,new SyntheticHttp{Reply=request=>Response(Data)}),"Large Code file rejected before writes");
        var longPath=Plan("long-path",new string('x',110)+"/"+new string('y',110)+"/данные.bin",Data);SyncTransfer.Preview(longPath);Run(longPath,new SyntheticHttp{Reply=request=>Response(Data)});Assert(File.ReadAllBytes(SyncTransfer.SafePath(longPath.directory,longPath.entries[0].name)).SequenceEqual(Data),"Nested path longer than MAX_PATH downloads intact");
        foreach(var metadata in Directory.GetFiles(Path.GetDirectoryName(SyncTransfer.SafePath(fixture,"hash-source.bin")),"*.json",SearchOption.AllDirectories)){string text=File.ReadAllText(metadata);Assert(!text.Contains("synthetic-memory-only") && !text.Contains("temporary-signature"),"No token or signed URL in persistent metadata");}
        Console.WriteLine("Download checks passed: "+checks+" assertions; synthetic HTTP only, no network or GitHub writes.");
    }
}
