using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.Diagnostics.ExtensionCommands.NativeAOT
{
    internal static class FormatHelpers
    {
        public static string PadByNumberSize<T>(T item, bool prependHexSignifier = true)
        {
            if (prependHexSignifier)
            {
                // 16 = max number of hex bits in a 64 bit number
                return $"0x{item,-16:X}";
            }
            else
            {
                // 18 = max number of hex bits in a 64 bit number + space for 0x
                return $"{item,-18:X}";
            }
        }

    }
}
