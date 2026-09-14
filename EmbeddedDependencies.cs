using System;
using System.IO;
using System.Reflection;

namespace CommStudio
{
    internal static class EmbeddedDependencies
    {
        private static readonly object Sync = new object();
        private static bool initialized;
        public static void Initialize()
        {
            lock (Sync)
            {
                if (initialized) return;
                AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
                {
                    if (new AssemblyName(args.Name).Name != "MQTTnet") return null;
                    using (Stream stream = typeof(EmbeddedDependencies).Assembly.GetManifestResourceStream("CommStudio.MQTTnet.dll"))
                    {
                        if (stream == null) return null;
                        using (MemoryStream buffer = new MemoryStream())
                        { stream.CopyTo(buffer); return Assembly.Load(buffer.ToArray()); }
                    }
                };
                initialized = true;
            }
        }
    }
}
