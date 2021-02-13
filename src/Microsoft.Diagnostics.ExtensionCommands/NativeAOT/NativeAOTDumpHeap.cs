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
                WriteLine($"{FormatHelpers.PadByNumberSize("Address", false)} {FormatHelpers.PadByNumberSize("EEtype", false)} {FormatHelpers.PadByNumberSize("Size", false)}");
                WriteLine("--------------------------------------------------------------------------------------");
            }

            HeapStats stats = new HeapStats((str) => Write(str));
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

                        stats.Add(eeType, obj.Size);
                    }
                }
            }

            stats.Print();
        }

        private void PrintObject(IRuntimeObject obj, NativeAOTType eeType)
        {
            if (Short)
            {
                WriteLine($"0x{obj.Address:X}");
            }
            else
            {
                WriteLine($"{FormatHelpers.PadByNumberSize(obj.Address)} {FormatHelpers.PadByNumberSize(eeType.Address)} {FormatHelpers.PadByNumberSize(obj.Size)}");
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
    }
}

