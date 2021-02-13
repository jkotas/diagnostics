using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.ExtensionCommands.NativeAOT
{
    internal static class IEnumerableExtensions
    {
        public static void ForEach<T>(this IEnumerable<T> enumerable, Action<T> action)
        {
            foreach (T item in enumerable)
            {
                action(item);
            }
        }
    }
}
