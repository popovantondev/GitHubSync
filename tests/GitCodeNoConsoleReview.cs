using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

internal static class GitCodeNoConsoleReview
{
    [DllImport("kernel32.dll")] private static extern IntPtr GetConsoleWindow();
    [STAThread] private static int Main(string[] args)
    {
        try {
            if(GetConsoleWindow()!=IntPtr.Zero)throw new Exception("Fixture unexpectedly has a console");
            var prior=Console.InputEncoding;
            byte[] bytes=new byte[] {0,255,239,187,191,10,13,128,1};
            byte[] header=Encoding.ASCII.GetBytes("blob "+bytes.Length+"\0");
            string expected;
            using(var hash=SHA1.Create()) {
                hash.TransformBlock(header,0,header.Length,header,0);
                hash.TransformFinalBlock(bytes,0,bytes.Length);
                expected=BitConverter.ToString(hash.Hash).Replace("-","").ToLowerInvariant();
            }
            using(var input=new MemoryStream(bytes)) {
                var response=new GitCodeNativeRunner().Run(args[0],args[1],new[]{"hash-object","--stdin"},input,20,null);
                if(response.ExitCode!=0 || response.Output.Trim()!=expected)throw new Exception("Raw binary input changed without console");
            }
            if(Console.InputEncoding.CodePage!=prior.CodePage)throw new Exception("Console encoding changed");
            File.WriteAllText(args[2],"PASS: no console, exact binary SHA, encoding unchanged");
            return 0;
        } catch(Exception error) {
            File.WriteAllText(args[2],"FAIL: "+error);
            return 1;
        }
    }
}
