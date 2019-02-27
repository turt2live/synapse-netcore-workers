using System.Collections.Generic;

namespace Matrix.SynapseInterop.Common
{
    public static class CollectionUtils
    {
        public static Queue<T> Clone<T>(this Queue<T> original)
        {
            return new Queue<T>(original);
        }

        public static List<T> Clone<T>(this List<T> original)
        {
            return new List<T>(original);
        }
    }
}
