using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.RuntimeSnapshotParser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.ExtensionCommands.NativeAOT
{

    [Command(Name = "aotthreads", Help = "Displays threads or sets the current thread.")]
    public class NativeAOTThreadsCommand : CommandBase
    {
        public ProcessSnapshot Snapshot { get; set; }

        public override void Invoke()
        {
            if (Snapshot == null)
            {
                WriteLine("Error: no Native AOT runtime detected.");
                return;
            }

            foreach (IRuntimeThread thread in Snapshot.Runtimes.First().Threads)
            {
                WriteLine($"Saw thread with id {thread.ID}");
            }
        }
    }
}
