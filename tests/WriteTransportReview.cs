using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal sealed class WriteHttp : HttpMessageHandler
{
    public int Calls;
    public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Reply;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) { Calls++; return Reply(request, cancel); }
}
internal sealed class JsonSink : Stream
{
    private readonly string prefix="{\"encoding\":\"base64\",\"content\":\"", data, suffix="\"}";
    public long Count; public int LargestChunk;
    public JsonSink(string value) { data=value; }
    public override void Write(byte[] buffer, int offset, int count)
    {
        LargestChunk=Math.Max(count,LargestChunk);
        for (int i=0;i<count;i++,Count++) {
            long position=Count;
            char expected=position<prefix.Length ? prefix[(int)position] : position<prefix.Length+(long)data.Length ? data[(int)(position-prefix.Length)] : suffix[(int)(position-prefix.Length-data.Length)];
            if(buffer[offset+i]!=(byte)expected) throw new Exception("Streaming JSON changed bytes");
        }
    }
    public override bool CanRead { get { return false; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return true; } }
    public override long Length { get { return Count; } } public override long Position { get { return Count; } set { throw new NotSupportedException(); } }
    public override void Flush() { } public override int Read(byte[] b,int o,int c) { throw new NotSupportedException(); }
    public override long Seek(long o,SeekOrigin s) { throw new NotSupportedException(); } public override void SetLength(long n) { throw new NotSupportedException(); }
}
internal static class WriteTransportReview
{
    private static int checks;
    private static void Assert(bool value,string message) { checks++;if(!value)throw new Exception(message); }
    private static HttpResponseMessage Response(int status,string body="{\"sha\":\"confirmed\"}") { var response=new HttpResponseMessage((HttpStatusCode)status){Content=new StringContent(body)};response.Headers.Add("X-GitHub-Request-Id","ABCD:1234");return response; }
    private static void Main(string[] args)
    {
        string fixture=args[0];Directory.CreateDirectory(fixture);var json=new JavaScriptSerializer();
        foreach(int length in new[]{0,1,2,3,17,61*1024*1024+2}) {
            byte[] bytes=new byte[length];for(int i=0;i<bytes.Length;i++)bytes[i]=(byte)(i%251);
            string encoded=Convert.ToBase64String(bytes);string progress=Path.Combine(fixture,"write-progress.json");
            File.WriteAllText(progress,json.Serialize(new{state="uploading",mode="code",file="artificial.bin",fileBytes=length,fileSent=0,confirmedFiles=new string[0]}));
            var http=new WriteHttp{Reply=async (request,cancel)=>{
                Assert(request.Method==HttpMethod.Post,"POST blob");Assert(request.Headers.Authorization.Parameter=="memory-only-fixture","Token stays in header");
                using(var sink=new JsonSink(encoded)) { await request.Content.CopyToAsync(sink);Assert(sink.Count==request.Content.Headers.ContentLength,"Exact JSON Content-Length");Assert(sink.LargestChunk<=65536,"Bounded streaming chunks"); }
                return Response(201);
            }};
            string body=GitHubWrite.SendBlob("https://api.github.com/repos/example/artificial/git/blobs",encoded,"memory-only-fixture",10800,progress,http);
            Assert(body.Contains("confirmed") && http.Calls==1,"One acknowledged request, no mutation retries");
            if(length>0) {var state=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(progress));Assert(Convert.ToInt64(state["fileSent"])==length,"Progress reports raw bytes including base64 padding");Assert((bool)state["fileProgressKnown"],"Known Code progress");}
            Assert(!File.ReadAllText(progress).Contains("memory-only-fixture"),"No token in snapshot");
        }
        var patch=new WriteHttp{Reply=async (request,cancel)=>{Assert(request.Method.Method=="PATCH","PATCH not coerced to POST");Assert((await request.Content.ReadAsStringAsync())=="{\"draft\":false}","Publication JSON untouched");return Response(200);}};
        GitHubWrite.SendJson("PATCH","https://api.github.com/repos/example/artificial/releases/1","{\"draft\":false}","",10800,patch);
        foreach(var item in new[]{Tuple.Create(401,"authentication"),Tuple.Create(403,"permission"),Tuple.Create(404,"not-found"),Tuple.Create(409,"conflict"),Tuple.Create(413,"size"),Tuple.Create(422,"validation"),Tuple.Create(429,"rate-limit"),Tuple.Create(500,"uncertain"),Tuple.Create(302,"http-error")}) {
            var http=new WriteHttp{Reply=(request,cancel)=>Task.FromResult(Response(item.Item1,"{\"message\":\"payload-and-token-must-not-leak\"}"))};
            bool failed=false;try{GitHubWrite.SendJson("POST","https://api.github.com/repos/example/artificial/git/trees","{}","memory-only-fixture",10800,http);}catch(GitHubWriteException e){failed=true;Assert(e.Category==item.Item2 && e.Status==item.Item1,"HTTP classification");Assert(!e.Message.Contains("payload-and-token") && !e.Message.Contains("memory-only-fixture"),"Error cannot leak server echoes");Assert(e.RequestId=="ABCD:1234","Safe diagnostic request ID");}
            Assert(failed && http.Calls==1,"No redirect or retry of rejected mutation");
        }
        var slow=new WriteHttp{Reply=async (request,cancel)=>{await Task.Delay(1800,cancel);return Response(201);}};
        Assert(GitHubWrite.SendJson("POST","https://api.github.com/repos/example/artificial/git/trees","{}","",3,slow).Contains("confirmed"),"Slow request respects caller deadline");
        var timeout=new WriteHttp{Reply=async(request,cancel)=>{await Task.Delay(4000,cancel);return Response(201);}};
        bool timedOut=false;try{GitHubWrite.SendJson("POST","https://api.github.com/repos/example/artificial/git/trees","{}","",1,timeout);}catch(GitHubWriteException e){timedOut=e.Category=="timeout";}
        Assert(timedOut && timeout.Calls==1,"Timeout does not repeat a mutation");
        Console.WriteLine("Write transport passed: "+checks+" assertions, synthetic 61 MiB binary, streaming progress, HTTP rejection/timeout/no retries. No network.");
    }
}
