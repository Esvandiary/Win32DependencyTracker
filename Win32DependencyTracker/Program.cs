using CommandLine;
using Dependencies;
using Dependencies.ClrPh;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
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
        public enum OutputFormat
        {
            None,
            Text,
            JSON
        }

        public class Options
        {
            [Option('f', "format", Default = OutputFormat.Text)]
            public OutputFormat Format { get; set; }

            [Option('v', "verbose", Default = false)]
            public bool Verbose { get; set; }

            [Option("rebuild-symbol-cache", Default = false)]
            public bool RebuildSymbolCache { get; set; }
            [Option("api-doc-zip", Default = null)]
            public string APIDocZipPath { get; set; }

            [Option("max-expected", Default = null)]
            public string MaxExpectedVersionOrBuild { get; set; }

            [Value(0, Required = true)]
            public string Path { get; set; }
        }

        static void Main(string[] args)
        {
            var parser = new Parser(cfg => {
                cfg.CaseInsensitiveEnumValues = true;
            });
            parser.ParseArguments<Options>(args)
                .WithParsed((opts) => _ = RunProcess(opts))
                .WithNotParsed((errs) => OnCommandLineParseFailed(errs));
        }

        public static void OnCommandLineParseFailed(IEnumerable<Error> errors)
        {
            Console.WriteLine("Invalid arguments:");
            foreach (var error in errors)
                Console.WriteLine($"    {error}");
        }

        class APIVersionResult
        {
            [JsonProperty("dll_path")]
            public string DLLPath { get; set; }
            [JsonProperty("dll_name")]
            public string DLLName { get; set; }
            [JsonProperty("symbol")]
            public string Symbol { get; set; }
            [JsonProperty("os_version")]
            [JsonConverter(typeof(StringEnumConverter))]
            public WindowsVersion Version { get; set; }
            [JsonProperty("os_version_number")]
            public int VersionNumber { get; set; }
            [JsonConverter(typeof(StringEnumConverter))]
            [JsonProperty("os_build")]
            public WindowsBuild Build { get; set; }
            [JsonProperty("os_build_number")]
            public int BuildNumber { get; set; }
        }
        
        class OutputResult
        {
            [JsonProperty("result")]
            public string Result { get; set; }
            [JsonProperty("os_version")]
            [JsonConverter(typeof(StringEnumConverter))]
            public WindowsVersion Version { get; set; }
            [JsonProperty("os_version_number")]
            public int VersionNumber { get; set; }
            [JsonProperty("os_build")]
            [JsonConverter(typeof(StringEnumConverter))]
            public WindowsBuild Build { get; set; }
            [JsonProperty("os_build_number")]
            public int BuildNumber { get; set; }
            [JsonProperty("symbols")]
            public List<APIVersionResult> Symbols { get; set; }
            [JsonProperty("symbols_at_max_version")]
            public List<APIVersionResult> MaxVersionSymbols { get; set; }
            [JsonProperty("unexpected_symbols")]
            public List<APIVersionResult> UnexpectedSymbols { get; set; }

        }

        static async Task RunProcess(Options options)
        {
            Log.Enabled = options.Verbose;

            Phlib.InitializePhLib();
            BinaryCache.InitializeBinaryCache(false);

            string filename = options.Path;
            if (!NativeFile.Exists(filename))
            {
                Log.Debug("Could not find file {0:s} on disk", filename);
                return;
            }

            var symbols = !options.RebuildSymbolCache && File.Exists("symbols.db")
                ? await APISymbolCache.Load("symbols.db")
                : await APISymbolCache.Create("symbols.db", options.APIDocZipPath);

            var dependencies = DependencyWalker.Walk(filename, (path) => !path.Contains("system32"));
            var flatDeps = DependencyWalker.Aggregate(dependencies);

            var results = new List<APIVersionResult>();
            foreach (var dependency in flatDeps)
            {
                var apiResult = symbols.LookupFunction(dependency.DLLPath, dependency.Function);
                if (apiResult != null)
                {
                    results.Add(new APIVersionResult
                    {
                        DLLPath = dependency.DLLPath,
                        DLLName = Path.GetFileName(dependency.DLLPath),
                        Symbol = dependency.Function,
                        Version = apiResult.MinVersion,
                        VersionNumber = (int)apiResult.MinVersion,
                        Build = apiResult.MinBuild,
                        BuildNumber = (int)(apiResult.MinBuild) & 0x0FFFFF,
                    });
                }
            }

            if (results.Any())
            {
                var highVer = results.OrderByDescending(t => t.Version).ThenByDescending(t => t.Build).First();
                var highSymbols = results.Where(t => t.Version == highVer.Version).ToList();

                List<APIVersionResult> unexpectedResults = null;
                bool hasMaxVersion = Enum.TryParse<WindowsVersion>(options.MaxExpectedVersionOrBuild, out var maxVersion);
                bool hasMaxBuild = Enum.TryParse<WindowsBuild>(options.MaxExpectedVersionOrBuild, out var maxBuild);
                if (hasMaxBuild)
                    unexpectedResults = results.Where(t => t.Build > maxBuild).ToList();
                else if (hasMaxVersion)
                    unexpectedResults = results.Where(t => t.Version > maxVersion).ToList();

                if (unexpectedResults != null && unexpectedResults.Any())
                    Environment.ExitCode = unexpectedResults.Count;

                bool isOK = (unexpectedResults?.Any() != true);
                if (options.Format == OutputFormat.JSON)
                {
                    var apiResult = new OutputResult
                    {
                        Result = isOK ? "ok" : "fail",
                        Version = highVer.Version,
                        VersionNumber = highVer.VersionNumber,
                        Build = highVer.Build,
                        BuildNumber = highVer.BuildNumber,
                        Symbols = results,
                        MaxVersionSymbols = highSymbols,
                        UnexpectedSymbols = unexpectedResults ?? new List<APIVersionResult>(),
                    };
                    var json = JsonConvert.SerializeObject(apiResult, Formatting.Indented);
                    Console.WriteLine(json);
                }
                else if (options.Format == OutputFormat.Text)
                {
                    Console.WriteLine($"File: {options.Path}");
                    Console.WriteLine($"Result: {(isOK ? "OK" : "FAIL")}");
                    Console.WriteLine($"Total symbols checked: {results.Count}");
                    Console.WriteLine($"Required OS version: {highVer.Version} ({highVer.Build}, build {highVer.BuildNumber})");
                    Console.WriteLine();
                    Console.WriteLine($"Symbols at that OS version ({highSymbols.Count}):");
                    PrintSymbolList(highSymbols);
                    if (!isOK)
                    {
                        Console.WriteLine();
                        Console.WriteLine($"Symbols above expected max OS {(hasMaxBuild ? $"build {maxBuild}" : $"version {maxVersion}")} ({unexpectedResults.Count}):");
                        PrintSymbolList(unexpectedResults);
                    }
                    else
                    {
                        Console.WriteLine();
                        Console.WriteLine($"No symbols found above expected max OS {(hasMaxBuild ? $"build {maxBuild}" : $"version {maxVersion}")}");
                    }
                }
            }
            else
            {
                Environment.ExitCode = -1;

                if (options.Format == OutputFormat.JSON)
                {
                    var apiResult = new OutputResult
                    {
                        Result = "no_symbols",
                        Version = WindowsVersion.None,
                        VersionNumber = 0,
                        Build = WindowsBuild.None,
                        BuildNumber = 0,
                        Symbols = new List<APIVersionResult>(),
                        MaxVersionSymbols = new List<APIVersionResult>(),
                        UnexpectedSymbols = new List<APIVersionResult>(),
                    };
                    var json = JsonConvert.SerializeObject(apiResult, Formatting.Indented);
                    Console.WriteLine(json);
                }
                else if (options.Format == OutputFormat.Text)
                {
                    Console.WriteLine($"File: {options.Path}");
                    Console.WriteLine($"Result: no symbols found");
                }
            }
        }

        private static void PrintSymbolList(List<APIVersionResult> results)
        {
            int dlen = results.Max(t => t.DLLName.Length);
            int nlen = results.Max(t => t.Symbol.Length);
            int vlen = results.Max(t => t.Version.ToString().Length);
            int blen = results.Max(t => t.Build.ToString().Length);

            string fmtstr = $"    {{0,-{dlen + 3}}}{{1,-{nlen + 3}}}{{2,-{vlen + 3}}}{{3,-{blen + 3}}}{{4}}";
            foreach (var s in results)
                Console.WriteLine(string.Format(fmtstr, s.DLLName, s.Symbol, s.Version.ToString(), s.Build.ToString(), s.BuildNumber));
        }
    }
}
