using System;
internal static class FakeGit
{
    private static int Main()
    {
        var request = Console.In.ReadToEnd().Replace("\r\n", "\n");
        if (!request.Contains("protocol=https") || !request.Contains("host=github.com") || !request.EndsWith("\n\n")) return 2;
        Console.WriteLine("password=synthetic-test-value");
        return 0;
    }
}
