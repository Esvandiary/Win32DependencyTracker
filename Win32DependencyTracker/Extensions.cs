using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Win32DependencyTracker
{
    internal static class SQLiteConnectionExtensions
    {
        public static int ExecuteNonQuery(this SQLiteTransaction tx, string command)
        {
            using (var cmd = tx.Connection.CreateCommand())
            {
                cmd.CommandText = command;
                cmd.Transaction = tx;
                return cmd.ExecuteNonQuery();
            }
        }

        public static T ExecuteScalar<T>(this SQLiteTransaction tx, string command)
        {
            using (var cmd = tx.Connection.CreateCommand())
            {
                cmd.CommandText = command;
                cmd.Transaction = tx;
                return (T)cmd.ExecuteScalar();
            }
        }

        public static T ExecuteScalar<T>(this SQLiteConnection conn, string command)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = command;
                return (T)cmd.ExecuteScalar();
            }
        }
    }
}
