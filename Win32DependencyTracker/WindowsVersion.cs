using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Win32DependencyTracker
{
    public enum WindowsVersion : int
    {
        None = 0,
        Win2000 = 50,
        WinXP = 51,
        WinVista = 60,
        Win7 = 70,
        Win8 = 80,
        Win8_1 = 81,
        Win10 = 100,
        Win11 = 110,
    }

    public enum WindowsBuild : int
    {
        None = 0,
        Unknown = -1,
        Win2000_RTM  = (WindowsVersion.Win2000  << 20) | 2195,
        WinXP_RTM    = (WindowsVersion.WinXP    << 20) | 2600,
        WinVista_RTM = (WindowsVersion.WinVista << 20) | 6000,
        WinVista_SP1 = (WindowsVersion.WinVista << 20) | 6001,
        WinVista_SP2 = (WindowsVersion.WinVista << 20) | 6002,
        Win7_RTM     = (WindowsVersion.Win7     << 20) | 7600,
        Win7_SP1     = (WindowsVersion.Win7     << 20) | 7601,
        Win8_RTM     = (WindowsVersion.Win8     << 20) | 9200,
        Win8_1_RTM   = (WindowsVersion.Win8_1   << 20) | 9600,
        Win10_1507   = (WindowsVersion.Win10    << 20) | 10240,
        Win10_1511   = (WindowsVersion.Win10    << 20) | 10586,
        Win10_1607   = (WindowsVersion.Win10    << 20) | 14393,
        Win10_1703   = (WindowsVersion.Win10    << 20) | 15063,
        Win10_1709   = (WindowsVersion.Win10    << 20) | 16299,
        Win10_1803   = (WindowsVersion.Win10    << 20) | 17134,
        Win10_1809   = (WindowsVersion.Win10    << 20) | 17763,
        Win10_1903   = (WindowsVersion.Win10    << 20) | 18362,
        Win10_1909   = (WindowsVersion.Win10    << 20) | 18363,
        Win10_2004   = (WindowsVersion.Win10    << 20) | 19041,
        Win10_20H2   = (WindowsVersion.Win10    << 20) | 19042,
        Win10_21H1   = (WindowsVersion.Win10    << 20) | 19043,
        Win10_21H2   = (WindowsVersion.Win10    << 20) | 19044,
        Win10_22H2   = (WindowsVersion.Win10    << 20) | 19045,
        Win11_21H2   = (WindowsVersion.Win11    << 20) | 22000,
        Win11_22H2   = (WindowsVersion.Win11    << 20) | 22621,
        Win11_23H2   = (WindowsVersion.Win11    << 20) | 22631,
        Win11_24H2   = (WindowsVersion.Win11    << 20) | 26100,
    }

    public static class APIVersionParser
    {
        private static readonly IReadOnlyDictionary<string, WindowsBuild> Win11Builds = new Dictionary<string, WindowsBuild>()
        {
            { "21H2", WindowsBuild.Win11_21H2 },
            { "22H2", WindowsBuild.Win11_22H2 },
            { "23H2", WindowsBuild.Win11_23H2 },
            { "24H2", WindowsBuild.Win11_24H2 },
        };

        private static readonly IReadOnlyDictionary<string, WindowsBuild> Win10Builds = new Dictionary<string, WindowsBuild>()
        {
            { "1507", WindowsBuild.Win10_1507 },
            { "1511", WindowsBuild.Win10_1511 },
            { "1607", WindowsBuild.Win10_1607 },
            { "1703", WindowsBuild.Win10_1703 },
            { "1709", WindowsBuild.Win10_1709 },
            { "1803", WindowsBuild.Win10_1803 },
            { "1809", WindowsBuild.Win10_1809 },
            { "1903", WindowsBuild.Win10_1903 },
            { "1909", WindowsBuild.Win10_1909 },
            { "2004", WindowsBuild.Win10_2004 },
            { "20H2", WindowsBuild.Win10_20H2 },
            { "21H1", WindowsBuild.Win10_21H1 },
            { "21H2", WindowsBuild.Win10_21H2 },
            { "22H2", WindowsBuild.Win10_22H2 },
        };

        private static readonly Regex WinBuildRegex = new Regex(@"^Windows [bB]uild (?<ver>\d+)");
        private static readonly Regex Win11Regex = new Regex(@"^Windows 11,? version (?<ver>\w+)");
        private static readonly Regex Win10Regex = new Regex(@"^Windows 10,? version (?<ver>\w+)");

        public static (WindowsVersion Version, WindowsBuild Build) Parse(string version)
        {
            if (string.IsNullOrEmpty(version))
                return (WindowsVersion.None, WindowsBuild.None);

            if (version.Contains('['))
                version = version.Substring(0, version.IndexOf('['));
            if (version.StartsWith("Available in "))
                version = version.Remove(0, "Available in ".Length);
            var entries = version.Split(new string[] { ",", " and " }, StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim());

            var results = new List<(WindowsVersion Version, WindowsBuild Build)>();
            foreach (var entry in entries)
            {
                // build numbers
                Match winBuildMatch = WinBuildRegex.Match(entry);
                if (winBuildMatch.Success)
                {
                    // find build number
                    int buildNumber = int.Parse(winBuildMatch.Groups["ver"].Value);
                    var matchingBuilds = Enum.GetValues(typeof(WindowsBuild)).Cast<int>().Where(t => (t & 0x0FFFFF) == buildNumber);
                    if (matchingBuilds.Any())
                        results.Add(((WindowsVersion)(matchingBuilds.First() >> 20), (WindowsBuild)(matchingBuilds.First())));
                }

                // 11
                Match w11Match = Win11Regex.Match(entry);
                if (w11Match.Success)
                    results.Add((WindowsVersion.Win11, Win11Builds.TryGetValue(w11Match.Groups["ver"].Value, out var build) ? build : WindowsBuild.Unknown));
                else if (entry.Contains("Windows 11"))
                    results.Add((WindowsVersion.Win11, WindowsBuild.Win11_21H2));

                // 10
                Match w10Match = Win10Regex.Match(entry);
                if (w10Match.Success)
                    results.Add((WindowsVersion.Win10, Win10Builds.TryGetValue(w10Match.Groups["ver"].Value, out var build) ? build : WindowsBuild.Unknown));
                else if (entry.Contains("Windows 10"))
                    results.Add((WindowsVersion.Win10, WindowsBuild.Win10_1507));

                // older
                if (entry.Contains("Windows 8.1"))
                    results.Add((WindowsVersion.Win8_1, WindowsBuild.Win8_1_RTM));
                else if (entry.Contains("Windows 8"))
                    results.Add((WindowsVersion.Win8, WindowsBuild.Win8_RTM));
                else if (entry.Contains("Windows 7 with SP1") && !entry.Contains("Platform Update"))
                    results.Add((WindowsVersion.Win7, WindowsBuild.Win7_SP1));
                else if (entry.Contains("Windows 7") && !entry.Contains("Platform Update"))
                    results.Add((WindowsVersion.Win7, WindowsBuild.Win7_RTM));
                else if (entry.Contains("Windows Vista") && !entry.Contains("Platform Update"))
                    results.Add((WindowsVersion.WinVista, WindowsBuild.WinVista_RTM));
                else if (entry.Contains("Windows XP"))
                    results.Add((WindowsVersion.WinXP, WindowsBuild.WinXP_RTM));
                else if (entry.Contains("Windows 2000"))
                    results.Add((WindowsVersion.Win2000, WindowsBuild.Win2000_RTM));
            }

            return (results.Any())
                ? results.OrderBy(t => (int)t.Version).ThenBy(t => (int)t.Build).First()
                : (WindowsVersion.None, WindowsBuild.None);
        }
    }
}
