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

            [Option("max-expected-version", Default = null)]
            public string MaxExpectedVersionOrBuild { get; set; }

            [Option("disallowed-dlls", Separator = ',', Default = null)]
            public IEnumerable<string> DisallowedDLLs { get; set; }

            [Value(0, Required = false)]
            public string Path { get; set; }
        }

        static void Main(string[] args)
        {
            var parser = new Parser(cfg => {
                cfg.CaseInsensitiveEnumValues = true;
            });
            parser.ParseArguments<Options>(args)
                .WithParsed((opts) => RunProcessAsync(opts).Wait())
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
            [JsonProperty("disallowed_dll_symbols")]
            public List<APIVersionResult> DisallowedDLLSymbols { get; set; }
            [JsonProperty("missing_dependent_dlls")]
            public List<string> MissingDependentDLLs { get; set; }

        }

        static async Task RunProcessAsync(Options options)
        {
            Log.Enabled = options.Verbose;

            options.DisallowedDLLs = options.DisallowedDLLs?.Select(t => DLLName.Normalise(t)).ToList();

            bool hasSecondaryActions = (options.RebuildSymbolCache);

            Phlib.InitializePhLib();
            BinaryCache.InitializeBinaryCache(false);

            var symbols = !options.RebuildSymbolCache && File.Exists("symbols.db")
                ? await APISymbolCache.Load("symbols.db")
                : await APISymbolCache.Create("symbols.db", options.APIDocZipPath);

            string filename = options.Path;

            if (string.IsNullOrEmpty(filename))
            {
                if (!hasSecondaryActions)
                    Log.Error("No file specified!");
                return;
            }

            if (!NativeFile.Exists(filename))
            {
                Log.Error("Could not find file {0:s} on disk", filename);
                return;
            }

            var dependencies = DependencyWalker.Walk(filename, shouldRecurse: (path) => !path.Contains("system32"));
            var flatDeps = DependencyWalker.Aggregate(dependencies);
            var missingDeps = DependencyWalker.GetMissingDLLs(dependencies);

            var results = new List<APIVersionResult>();
            foreach (var dependency in flatDeps)
            {
                var apiResult = symbols.LookupFunction(dependency.DLLPath, dependency.Function);
                if (apiResult != null)
                {
                    results.Add(new APIVersionResult
                    {
                        DLLPath = dependency.DLLPath,
                        DLLName = DLLName.Normalise(dependency.DLLPath),
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

                List<APIVersionResult> unexpectedVersionResults = new List<APIVersionResult>();
                List<APIVersionResult> disallowedDLLResults = new List<APIVersionResult>();
                bool hasMaxVersion = Enum.TryParse<WindowsVersion>(options.MaxExpectedVersionOrBuild, out var maxVersion);
                bool hasMaxBuild = Enum.TryParse<WindowsBuild>(options.MaxExpectedVersionOrBuild, out var maxBuild);
                bool hasDisallowedDLLs = options.DisallowedDLLs?.Any() == true;
                if (hasMaxBuild)
                    unexpectedVersionResults = results.Where(t => t.Build > maxBuild).ToList();
                else if (hasMaxVersion)
                    unexpectedVersionResults = results.Where(t => t.Version > maxVersion).ToList();

                if (hasDisallowedDLLs)
                    disallowedDLLResults = results.Where(t => options.DisallowedDLLs.Contains(t.DLLName)).ToList();

                bool isOK = !missingDeps.Any() && !unexpectedVersionResults.Any() && !disallowedDLLResults.Any();
                if (!isOK)
                    Environment.ExitCode = missingDeps.Count + unexpectedVersionResults.Count + disallowedDLLResults.Count;

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
                        UnexpectedSymbols = unexpectedVersionResults,
                        DisallowedDLLSymbols = disallowedDLLResults,
                        MissingDependentDLLs = missingDeps.ToList(),
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
                        if (missingDeps.Any())
                        {
                            Console.WriteLine();
                            Console.WriteLine($"Dependent DLLs could not be found ({missingDeps.Count}):");
                            foreach (var name in missingDeps)
                                Console.WriteLine($"    {name}");
                        }
                        if (unexpectedVersionResults.Any())
                        {
                            Console.WriteLine();
                            Console.WriteLine($"Symbols above expected max OS {(hasMaxBuild ? $"build {maxBuild}" : $"version {maxVersion}")} ({unexpectedVersionResults.Count}):");
                            PrintSymbolList(unexpectedVersionResults);
                        }
                        if (disallowedDLLResults.Any())
                        {
                            Console.WriteLine();
                            Console.WriteLine($"Symbols from disallowed DLLs ({disallowedDLLResults.Count}):");
                            PrintSymbolList(disallowedDLLResults);
                        }
                    }
                    else if (hasMaxVersion || hasMaxBuild || hasDisallowedDLLs)
                    {
                        if (hasMaxVersion || hasMaxBuild)
                        {
                            Console.WriteLine();
                            Console.WriteLine($"No symbols found above expected max OS {(hasMaxBuild ? $"build {maxBuild}" : $"version {maxVersion}")}");
                        }
                        if (hasDisallowedDLLs)
                        {
                            Console.WriteLine();
                            Console.WriteLine($"No symbols found from disallowed DLLs");
                        }
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

            string fmtstr = $"    {{0,-{dlen + 3}}}{{1,-{nlen + 3}}}{{2,-{vlen + 3}}}{{3}}";
            foreach (var s in results)
                Console.WriteLine(string.Format(fmtstr, s.DLLName, s.Symbol, s.Version.ToString(), $"{s.Build} ({s.BuildNumber})"));
        }
    }
}
