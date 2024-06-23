using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Win32DependencyTracker
{
    internal static class Log
    {
        public static bool Enabled { get; set; } = false;

        public static void Debug(string fmtstr, params object[] args)
        {
            if (Enabled)
                Console.WriteLine(fmtstr, args);
        }
    }
}
