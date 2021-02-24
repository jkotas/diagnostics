using Microsoft.Diagnostics.RuntimeSnapshotParser;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.DebugServices.NativeAOT
{

    [Command(Name = "aotdumplog", Help = "Displays stress log entries.")]
    public class NativeAOTStressLogCommand : CommandBase
    {
        class StressLogEntry
        {
            public IRuntimeThread Thread;
            public string Message;
            public ulong Timestamp;
        }

        public ProcessSnapshot Snapshot { get; set; }

        [Option(Name = "--file", Aliases = new string[] { "-f" }, Help = "Name of the file to dump StressLog contents to.")]
        public string FileName { get; set; }

        public override void Invoke()
        {
            if (Snapshot == null)
            {
                WriteLine("Error: no Native AOT runtime detected.");
                return;
            }

            string outFileName = "StressLog.txt";
            if (!string.IsNullOrEmpty(FileName))
            {
                outFileName = FileName;
            }

            WriteLine("Dumping StressLog...");

            using (StreamWriter writer = new StreamWriter(new FileStream(outFileName, FileMode.Create)))
            {
                writer.WriteLine("Thread     Timestamp        Message");
                writer.WriteLine("--------------------------------------------------------------------------------------");
                var entries = Snapshot.Runtimes.First().StressLogEntries;

                if (entries.Count == 0)
                {
                    WriteLine("StressLog does not exist, no entries will be dumped.");
                }

                foreach (StressLogEntry entry in SortEntries(entries))
                {
                    writer.Write($"{entry.Thread.ID,-10:X} {entry.Timestamp,-16:X} {entry.Message}");
                    writer.WriteLine();
                }

                writer.WriteLine("--------------------------------------------------------------------------------------");
            }

            WriteLine("Done");
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
