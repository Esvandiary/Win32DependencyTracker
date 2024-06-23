using Dependencies;
using Dependencies.ClrPh;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Win32DependencyTracker
{
    internal class Dependencies
    {
        private static readonly IEqualityComparer<IEnumerable<string>> _sequenceComparer = new SequenceEqualityComparer<string>();

        private Dictionary<string, (PeImport Function, List<string[]> UsagePaths)> _usedFunctions
            = new Dictionary<string, (PeImport Function, List<string[]> UsagePaths)>();

        public Dependencies(string path) => Path = path;
        public string Path { get; private set; }
        public Dictionary<string, Dependencies> DirectDependencies { get; } = new Dictionary<string, Dependencies>();
        public string[] Functions { get => _usedFunctions.Keys.ToArray(); }
        public string[][] UsagePaths
        {
            get => _usedFunctions.Values
                .SelectMany(t => t.UsagePaths)
                .Distinct(_sequenceComparer)
                .Select(t => t.ToArray())
                .ToArray();
        }

        public void AddUsedFunction(PeImport import, string[] path)
        {
            string name = import.Name ?? $"Ordinal_{import.Ordinal}";
            if (!_usedFunctions.ContainsKey(name))
                _usedFunctions[name] = (import, new List<string[]>());
            _usedFunctions[name].UsagePaths.Add(path);
        }

        public void AddDirectDependency(Dependencies dep, string[] usagePath, IEnumerable<PeImport> usedFunctions)
        {
            if (!DirectDependencies.ContainsKey(dep.Path))
                DirectDependencies[dep.Path] = dep;

            foreach (var fn in usedFunctions)
                DirectDependencies[dep.Path].AddUsedFunction(fn, usagePath);
        }
    }

    internal static class DependencyWalker
    {
        public static Dependencies Walk(string filename, Func<string, bool> shouldRecurse)
            => WalkInternal(filename, shouldRecurse, new List<string>() { filename }, new Dictionary<string, Dependencies>());

        private static Dependencies WalkInternal(string filename, Func<string, bool> shouldRecurse, List<string> currentPath, Dictionary<string, Dependencies> cache)
        {
            if (cache.TryGetValue(filename, out Dependencies dependencies))
                return dependencies;

            dependencies = cache[filename] = new Dependencies(filename);

            if (!shouldRecurse(filename))
                return dependencies;

            currentPath.Add(filename);

            try
            {
                PE pe = new PE(filename);
                if (!pe.Load())
                {
                    Log.Debug("Could not load file {0:s} as a PE", filename);
                    return null;
                }

                var imports = pe.GetImports();

                foreach (var import in imports)
                {
                    (var strategy, var path) = FindPe.FindPeFromDefault(pe, import.Name);
                    if (strategy == ModuleSearchStrategy.NOT_FOUND)
                        continue;

                    if (!cache.TryGetValue(path, out Dependencies childDeps))
                        childDeps = WalkInternal(path, shouldRecurse, currentPath, cache);

                    dependencies.AddDirectDependency(childDeps, currentPath.ToArray(), import.ImportList);
                }

                return dependencies;
            }
            finally
            {
                currentPath.RemoveAt(currentPath.Count - 1);
            }
        }

        public static HashSet<(string DLLPath, string Function)> Aggregate(Dependencies root)
        {
            var result = new HashSet<(string DLLPath, string Function)>();
            AggregateInternal(root, result);
            return result;
        }

        private static void AggregateInternal(Dependencies deps, HashSet<(string DLLPath, string Function)> state)
        {
            foreach (var fn in deps.Functions)
                state.Add((deps.Path, fn));

            foreach (var child in deps.DirectDependencies.Values)
                AggregateInternal(child, state);
        }
    }
}
