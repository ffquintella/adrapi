using System;
using System.Collections.Generic;
using System.Linq;

namespace adrapi.Tools
{
    public static class ListHelper
    {
        public static IList<T> Clone<T>(this IList<T> listToClone) where T: ICloneable
        {
            return listToClone.Select(item => (T)item.Clone()).ToList();
        }
    }
}