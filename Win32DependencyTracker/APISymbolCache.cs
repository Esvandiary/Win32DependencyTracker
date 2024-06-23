using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace Win32DependencyTracker
{
    class APISymbolCache : IDisposable
    {
        private static readonly string RepoURL = "https://github.com/MicrosoftDocs/sdk-api/archive/refs/heads/docs.zip";

        private APISymbolDB _conn;

        private APISymbolCache(APISymbolDB conn) => _conn = conn;

        public void Dispose()
        {
            _conn?.Dispose();
            _conn = null;
        }

        public APIFunctionEntry LookupFunction(string path, string function)
        {
            string filename = Path.GetFileName(path);
            var results = _conn.LookupFunction(filename, function).ToArray();
            return results.FirstOrDefault();
        }

        public static async Task<APISymbolCache> Load(string path)
        {
            Log.Debug("Loading existing symbol cache database");
            SQLiteConnection conn = null;
            APISymbolDB db = null;
            try
            {
                conn = new SQLiteConnection($"Data Source={path};Version=3;UseUTF16Encoding=True;");
                await conn.OpenAsync();
                db = new APISymbolDB(conn);

                Log.Debug("Loaded symbol cache with {0} functions", db.CountAPIFunctionEntries());

                return new APISymbolCache(db);
            }
            catch
            {
                db?.Dispose();
                conn?.Dispose();
                throw;
            }
        }

        public static async Task<APISymbolCache> Create(string path, string zipPath)
        {
            Log.Debug("Creating new symbol cache database");
            bool isUserZipFile = false;
            if (!string.IsNullOrEmpty(zipPath) && File.Exists(zipPath))
            {
                Log.Debug("Using existing local API docs ZIP");
                isUserZipFile = true;
            }
            else
            {
                zipPath = $"{path}.zip";
                using (WebClient client = new WebClient())
                {
                    Log.Debug("Downloading API docs ZIP");
                    using (FileStream fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
                    {
                        Stream s = await client.OpenReadTaskAsync(RepoURL);
                        s.CopyTo(fs);
                    }
                }
            }

            APISymbolDB db = null;
            SQLiteConnection conn = null;
            try
            {
                conn = new SQLiteConnection($"Data Source={path};Version=3;UseUTF16Encoding=True;");
                await conn.OpenAsync();
                db = new APISymbolDB(conn);
                Log.Debug("Creating DB tables");
                db.CreateTables();
                Log.Debug("Loading API function entries");
                db.InsertAPIFunctionEntries(ReadAPIEntries(zipPath).Select(t => GetFunctionEntry(t)));
                Log.Debug("Inserted {0} functions", db.CountAPIFunctionEntries());
                Log.Debug("Creating indexes");
                db.CreateIndexes();

                Log.Debug("Created symbol cache database");
                return new APISymbolCache(db);
            }
            catch
            {
                db?.Dispose();
                conn?.Dispose();
                File.Delete(path);
                throw;
            }
            finally
            {
                if (!isUserZipFile)
                    File.Delete(zipPath);
            }
        }

        private static APIFunctionEntry GetFunctionEntry(APIEntry entry)
        {
            var versionString = entry.MinClientVersion?.Replace('\u00A0', ' ').Trim();
            (var minver, var minbuild) = APIVersionParser.Parse(versionString);
            return new APIFunctionEntry {
                DLLName = (entry.DLLName ?? entry.APILocations.FirstOrDefault(t => t.EndsWith(".dll")))?.Trim(),
                Name = entry.APINames.FirstOrDefault()?.Trim(),
                APIType = entry.APITypes.FirstOrDefault()?.Trim(),
                MinVersion = minver,
                MinBuild = minbuild,
            };
        }

        private static bool IsRelevantZipEntry(string path)
        {
            if (Path.GetExtension(path) != ".md")
                return false;
            string filename = Path.GetFileName(path);
            if (!filename.StartsWith("nf-") && !filename.StartsWith("nn-"))
                return false;
            return true;
        }

        private class APIEntry
        {
            [YamlMember(Alias = "UID")]
            public string UID { get; set; }
            [YamlMember(Alias = "req.header")]
            public string Header { get; set; }
            [YamlMember(Alias = "req.target-min-winverclnt")]
            public string MinClientVersion { get; set; }
            [YamlMember(Alias = "req.target-min-winversvr")]
            public string MinServerVersion { get; set; }
            [YamlMember(Alias = "req.lib")]
            public string ImpLibName { get; set; }
            [YamlMember(Alias = "req.dll")]
            public string DLLName { get; set; }
            [YamlMember(Alias = "api_type")]
            public List<string> APITypes { get; set; }
            [YamlMember(Alias = "api_location")]
            public List<string> APILocations { get; set; }
            [YamlMember(Alias = "api_name")]
            public List<string> APINames { get; set; }
        }

        private static readonly IDeserializer _deserializer = new DeserializerBuilder().IgnoreUnmatchedProperties().Build();
        private static APIEntry ReadAPIStream(TextReader sr)
        {
            var parser = new Parser(sr);
            parser.Consume<StreamStart>();
            parser.Consume<DocumentStart>();
            return _deserializer.Deserialize<APIEntry>(parser);
        }

        private static bool IsRelevantAPIEntry(APIEntry e)
        {
            if (e.APITypes?.Any(t => t == "DllExport") != true)
                return false;
            if (string.IsNullOrEmpty(e.DLLName) && e.APILocations?.Any(t => t.EndsWith(".dll")) != true)
                return false;

            return true;
        }

        private static IEnumerable<APIEntry> ReadAPIEntries(string zipPath)
        {
            using (var fs = File.OpenRead(zipPath))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!IsRelevantZipEntry(entry.FullName))
                        continue;

                    using (var stream = entry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        string content = reader.ReadToEnd();
                        var lolreader = new StringReader(content);

                        var api = ReadAPIStream(lolreader);
                        if (!IsRelevantAPIEntry(api))
                            continue;

                        yield return api;
                    }
                }
            }
        }
    }
}
