using Dependencies;
using Dependencies.ClrPh;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Win32DependencyTracker
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine($"Usage: {Assembly.GetEntryAssembly().GetName().Name} <print|json> <path>");
                return;
            }

            Log.Enabled = true;

            Phlib.InitializePhLib();
            BinaryCache.InitializeBinaryCache(false);

            string filename = args[1];
            if (!NativeFile.Exists(filename))
            {
                Log.Debug("Could not find file {0:s} on disk", filename);
                return;
            }

            var symbols = File.Exists("symbols.db")
                ? await APISymbolCache.Load("symbols.db")
                : await APISymbolCache.Create("symbols.db");

            var dependencies = DependencyWalker.Walk(filename, (path) => !path.Contains("system32"));
            var flat = DependencyWalker.Aggregate(dependencies);

            var versions = new Dictionary<string, (WindowsVersion Version, WindowsBuild Build)>();
            foreach (var dependency in flat)
            {
                var apiResult = symbols.LookupFunction(dependency.DLLPath, dependency.Function);
                if (apiResult != null)
                    versions[$"{dependency.DLLPath}@{dependency.Function}"] = (apiResult.MinVersion, apiResult.MinBuild);
            }

            var maxVer = versions.OrderByDescending(t => t.Value.Version).ThenByDescending(t => t.Value.Build).First();
            Log.Debug($"Max version: {maxVer.Value.Version} ({maxVer.Value.Build})");
            foreach (var kv in versions.Where(t => t.Value.Version == maxVer.Value.Version))
            {
                Log.Debug("    {0}: {1} ({2})", kv.Key, kv.Value.Version, kv.Value.Build);
            }

            Console.ReadLine();
        }
    }
}
