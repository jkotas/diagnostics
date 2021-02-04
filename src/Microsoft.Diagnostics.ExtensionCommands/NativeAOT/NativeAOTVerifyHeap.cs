using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.RuntimeSnapshotParser;
using Microsoft.Diagnostics.RuntimeSnapshotParser.Common.GC;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.ExtensionCommands.NativeAOT
{
    [Command(Name = "aotverifyheap", Help = "Verifies objects on the GC heap(s).")]
    public class NativeAOTVerifyHeap : CommandBase
    {
        public ProcessSnapshot Snapshot { get; set; }

        public override void Invoke()
        {
            if (Snapshot == null)
            {
                WriteLine("Error: no Native AOT runtime detected.");
                return;
            }

            int count = 0;
            int heaps = 0;
            IDotNetRuntime runtime = Snapshot.Runtimes.First();
            foreach (IRuntimeGCHeap heap in runtime.GC.Heaps)
            {
                HeapWalker walker = heap.GetHeapWalker();
                ++heaps;

                IRuntimeObject lastGood = null;
                foreach (IRuntimeObject obj in walker.EnumerateHeapObjects())
                {
                    ++count;

                    if (!obj.Verify())
                    {
                        DisplayCorruptedObject(obj, lastGood);
                    }
                    else
                    {
                        lastGood = obj;
                    }
                }
            }

            WriteLine($"Found {heaps} heap(s) with {count} objects.");
        }

        private void DisplayCorruptedObject(IRuntimeObject obj, IRuntimeObject lastGoodObj)
        {
            Write($"Object at address 0x{obj.Address:X} is not a valid object");

            if (lastGoodObj != null)
            {
                Write($"Last good object address 0x{lastGoodObj.Address:X} type {lastGoodObj.Type.FullName}");
            }

            WriteLine("");
        }
    }
}
