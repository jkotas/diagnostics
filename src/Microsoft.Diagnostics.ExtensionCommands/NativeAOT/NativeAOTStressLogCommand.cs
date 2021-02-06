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
        class StressLogEntry
        {
            public IRuntimeThread Thread;
            public string Message;
            public ulong Timestamp;
        }

        public ProcessSnapshot Snapshot { get; set; }

        public override void Invoke()
        {
            if (Snapshot == null)
            {
                WriteLine("Error: no Native AOT runtime detected.");
                return;
            }

            WriteLine("Dumping StressLog...");
            WriteLine();
            WriteLine("Thread     Timestamp        Message");
            WriteLine("--------------------------------------------------------------------------------------");
            var entries = Snapshot.Runtimes.First().StressLogEntries;
            foreach (StressLogEntry entry in SortEntries(entries))
            {
                Write($"{entry.Thread.ID,-10:X} {entry.Timestamp,-16:X} {entry.Message}");
                WriteLine();
            }

            WriteLine("--------------------------------------------------------------------------------------");
        }

        private IEnumerable<StressLogEntry> SortEntries(Dictionary<IRuntimeThread, IEnumerable<IStressLogEntry>> entries)
        {
            List<StressLogEntry> allEntries = new List<StressLogEntry>();
            foreach (IRuntimeThread thread in entries.Keys)
            {
                foreach (IStressLogEntry entry in entries[thread])
                {
                    StressLogEntry mergedEntry = new StressLogEntry()
                    {
                        Thread = thread,
                        Message = entry.Message,
                        Timestamp = entry.Timestamp
                    };
                    allEntries.Add(mergedEntry);
                }
            }

            return allEntries.OrderBy(x => x.Timestamp);
        }
    }
}
