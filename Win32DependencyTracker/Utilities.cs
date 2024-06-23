using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Win32DependencyTracker
{
    class SequenceEqualityComparer<T> : IEqualityComparer<IEnumerable<T>>
    {
        public bool Equals(IEnumerable<T> b1, IEnumerable<T> b2)
        {
            if (ReferenceEquals(b1, b2))
                return true;

            if (b2 is null || b1 is null)
                return false;

            return Enumerable.SequenceEqual(b1, b2);
        }

        public int GetHashCode(IEnumerable<T> arr) => arr.Aggregate(0, (init, val) => init ^ (val?.GetHashCode() ?? 0));
    }
}
