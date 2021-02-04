using Microsoft.Diagnostics.RuntimeSnapshotParser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.DebugServices.NativeAOT
{

    [Command(Name = "aotstresslog", Help = "Displays stress log entries.")]
    public class NativeAOTStressLogCommand : CommandBase
    {
        public ProcessSnapshot Snapshot { get; set; }

        public override void Invoke()
        {
            if (Snapshot == null)
            {
                WriteLine("Error: no Native AOT runtime detected.");
                return;
            }

            var entries = Snapshot.Runtimes.First().StressLogEntries;
            foreach (IRuntimeThread thread in entries.Keys)
            {
                WriteLine($"Printing stress log messages for thread ID={thread.ID}");

                foreach (IStressLogEntry entry in entries[thread])
                {
                    Write($"    Timestamp={entry.Timestamp} Message={entry.Message} ");
                    if (entry.Args.Count > 0)
                    {
                        Write("        Args =");
                    }
                    foreach (ulong arg in entry.Args)
                    {
                        Write($" 0x{arg.ToString("x")}");
                    }

                    WriteLine();
                }
            }
        }
    }
}
