using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.ExtensionCommands.NativeAOT
{
    public static class StringExtensions
    {
        // Why is this not part of the BCL at this point?
        public static bool Contains(this string str, string other, StringComparison comparison)
        {
            return str.IndexOf(other, comparison) >= 0;
        }
    }
}
