using System;
using System.IO;
using System.Reflection;
using System.Threading;

#if UPDATED
[assembly: AssemblyVersion("0.2.1.0")]
#else
[assembly: AssemblyVersion("0.2.0.0")]
#endif

internal static class UpdateFixture
{
    private static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--wait")
        {
            File.WriteAllText(args[1], "started");
            Thread.Sleep(1000);
        }
        else if (args.Length == 1)
            File.WriteAllText(args[0], typeof(UpdateFixture).Assembly.GetName().Version.ToString());
    }
}
