using System;
using System.Collections.Generic;

namespace Matrix.SynapseInterop.Common.Extensions
{
    public static class CollectionExtensions
    {
        public static Queue<T> Clone<T>(this Queue<T> original)
        {
            return new Queue<T>(original);
        }

        public static List<T> Clone<T>(this List<T> original)
        {
            return new List<T>(original);
        }

        public static void ForEach<T>(this IEnumerable<T> items, Action<T> action) where T : class
        {
            foreach (var item in items) action(item);
        }
    }
}
