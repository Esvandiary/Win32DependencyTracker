using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Win32DependencyTracker
{
    internal class APIFunctionEntry
    {
        public string DLLName { get; set; }
        public string Name { get; set; }
        public string APIType { get; set; }
        public WindowsVersion MinVersion { get; set; }
        public WindowsBuild MinBuild { get; set; }
    }

    internal class APISymbolDB : IDisposable
    {
        private SQLiteConnection _conn;

        public APISymbolDB(SQLiteConnection conn)
            => _conn = conn;

        public void Dispose() => Dispose(true);
        ~APISymbolDB() => Dispose(false);
        protected virtual void Dispose(bool disposing)
        {
            _conn?.Dispose();
            _conn = null;

            if (disposing)
                GC.SuppressFinalize(this);
        }

        public void CreateTables()
        {
            using (var tx = _conn.BeginTransaction())
            {
                tx.ExecuteNonQuery("CREATE TABLE functions (dll TEXT, name TEXT, apitype TEXT, winver NUMBER, winbuild NUMBER)");
                tx.Commit();
            }
        }

        public void CreateIndexes()
        {
            using (var tx = _conn.BeginTransaction())
            {
                tx.ExecuteNonQuery("CREATE INDEX idx_functions_dll_name ON functions (dll, name)");
                tx.Commit();
            }
        }

        public long CountAPIFunctionEntries()
        {
            return _conn.ExecuteScalar<long>("SELECT COUNT(*) FROM functions");
        }

        public IEnumerable<APIFunctionEntry> LookupFunction(string filename, string function)
        {
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "SELECT dll, name, apitype, winver, winbuild FROM functions WHERE dll=$dll AND name=$name";
                cmd.Parameters.AddWithValue("$dll", filename.ToLower());
                cmd.Parameters.AddWithValue("$name", function);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        yield return new APIFunctionEntry
                        {
                            DLLName = reader.GetString(0),
                            Name = reader.GetString(1),
                            APIType = reader.GetString(2),
                            MinVersion = (WindowsVersion)reader.GetInt32(3),
                            MinBuild = (WindowsBuild)reader.GetInt32(4),
                        };
                    }
                }
            }
        }

        public void InsertAPIFunctionEntries(IEnumerable<APIFunctionEntry> entries)
        {
            using (var tx = _conn.BeginTransaction())
            using (var cmd = tx.Connection.CreateCommand())
            {
                cmd.CommandText = "INSERT INTO functions (dll, name, apitype, winver, winbuild) VALUES ($dll, $name, $apitype, $winver, $winbuild)";
                var dllParam = cmd.Parameters.Add("$dll", System.Data.DbType.String);
                var nameParam = cmd.Parameters.Add("$name", System.Data.DbType.String);
                var apitypeParam = cmd.Parameters.Add("$apitype", System.Data.DbType.String);
                var winverParam = cmd.Parameters.Add("$winver", System.Data.DbType.UInt32);
                var winbuildParam = cmd.Parameters.Add("$winbuild", System.Data.DbType.UInt32);
 
                foreach (var entry in entries)
                {
                    if (string.IsNullOrEmpty(entry.DLLName) || string.IsNullOrEmpty(entry.Name))
                        continue;

                    dllParam.Value = entry.DLLName.ToLower();
                    nameParam.Value = entry.Name; // case sensitive
                    apitypeParam.Value = entry.APIType;
                    winverParam.Value = (uint)entry.MinVersion;
                    winbuildParam.Value = (uint)entry.MinBuild;

                    cmd.ExecuteNonQuery();
                }

                tx.Commit();
            }
        }
    }
}
