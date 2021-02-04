using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.RuntimeSnapshotParser;
using Microsoft.Diagnostics.RuntimeSnapshotParser.Common.GC;
using Microsoft.Diagnostics.RuntimeSnapshotParser.NativeAOT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.ExtensionCommands.NativeAOT
{
    [Command(Name = "aotdumpheap", Help = "Verifies objects on the GC heap(s).")]
    public class NativeAOTDumpHeap : CommandBase
    {
        class TypeStats
        {
            public NativeAOTType type;
            public uint count;
            public ulong totalSizeInBytes;
        }

        public ProcessSnapshot Snapshot { get; set; }

        [Option(Name = "--short", Help = "Restrict output to just the address.")]
        public bool Short { get; set; }

        [Option(Name = "--stat", Aliases = new string[] { "-s" }, Help = "Display a summary instead of individual objects.")]
        public bool Stats { get; set; }

        [Option(Name = "--type", Aliases = new string[] { "-t" }, Help = "Only lists those objects whose type name is a substring match of the specified string.")]
        public string Type { get; set; }

        public override void Invoke()
        {
            if (Snapshot == null)
            {
                WriteLine("Error: no Native AOT runtime detected.");
                return;
            }

            if (!Stats && !Short)
            {
                WriteLine($"{PadByNumberSize("Address", false)} {PadByNumberSize("EEtype", false)} {PadByNumberSize("Size", false)}");
            }

            Dictionary<ulong, TypeStats> typeStats = new Dictionary<ulong, TypeStats>();
            IDotNetRuntime runtime = Snapshot.Runtimes.First();
            foreach (IRuntimeGCHeap heap in runtime.GC.Heaps)
            {
                HeapWalker walker = heap.GetHeapWalker();

                foreach (IRuntimeObject obj in walker.EnumerateHeapObjects())
                {
                    NativeAOTType eeType = (NativeAOTType)obj.Type;

                    if (Matches(eeType))
                    {
                        if (!Stats)
                        {
                            PrintObject(obj, eeType);
                        }

                        TypeStats stats = GetOrCreateTypeStats(typeStats, eeType);
                        stats.count++;
                        stats.totalSizeInBytes += GetObjectRealSize(obj, eeType);
                    }
                }
            }

            PrintStats(typeStats);
        }

        private void PrintStats(Dictionary<ulong, TypeStats> typeStats)
        {
            WriteLine();
            WriteLine($"{PadByNumberSize("EEType  ", false)} {PadByNumberSize("Count  ", false)} {PadByNumberSize("TotalSize  ", false)} Class Name");
            
            foreach (TypeStats stats in typeStats.Values)
            {
                WriteLine($"{PadByNumberSize(stats.type.Address)} {PadByNumberSize(stats.count)} {PadByNumberSize(stats.totalSizeInBytes)} {stats.type.FullName}");
            }
        }

        private void PrintObject(IRuntimeObject obj, NativeAOTType eeType)
        {
            if (Short)
            {
                WriteLine($"0x{obj.Address:X}");
            }
            else
            {
                WriteLine($"{PadByNumberSize(obj.Address)} {PadByNumberSize(eeType.Address)} {PadByNumberSize(GetObjectRealSize(obj, eeType))}");
            }
        }

        private string PadByNumberSize<T>(T item, bool prependHexSignifier = true)
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

        private bool Matches(NativeAOTType eeType)
        {
            if (!string.IsNullOrEmpty(Type))
            {
                return eeType.FullName.Contains(Type, StringComparison.InvariantCultureIgnoreCase);
            }

            // No filter, everything matches
            return true;
        }

        private static ulong GetObjectRealSize(IRuntimeObject obj, NativeAOTType eeType)
        {
            return (ulong)(eeType.BaseSize + (eeType.ComponentSize * obj.ArraySize));
        }

        TypeStats GetOrCreateTypeStats(Dictionary<ulong, TypeStats> dictionary, NativeAOTType eeType)
        {
            ulong eeTypeAddr = (ulong)eeType.Address;
            if (!dictionary.ContainsKey(eeTypeAddr))
            {
                TypeStats stats = new TypeStats()
                {
                    type = eeType
                };
                dictionary.Add(eeTypeAddr, stats);
            }

            return dictionary[eeTypeAddr];
        }
    }

    public static class StringExtensions
    {
        // Why is this not part of the BCL at this point?
        public static bool Contains(this string str, string other, StringComparison comparison)
        {
            return str.IndexOf(other, comparison) >= 0;
        }
    }
}

