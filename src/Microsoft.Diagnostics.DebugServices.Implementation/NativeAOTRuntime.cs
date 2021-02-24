using Microsoft.Diagnostics.DebugServices.Implementation.NativeAOT;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.RuntimeSnapshotParser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using SnapshotParserNativeAOTRuntime = Microsoft.Diagnostics.RuntimeSnapshotParser.NativeAOT.NativeAOTRuntime;

namespace Microsoft.Diagnostics.DebugServices.Implementation
{
    public class NativeAOTRuntime : IRuntime
    {
        SnapshotParserNativeAOTRuntime _runtime;
        ProcessSnapshot _snapshot;
        ServiceProvider _serviceProvider;

        public NativeAOTRuntime(ITarget target, IMemoryService memoryService)
        {
            var tuple = GetDotNetRuntime(target, memoryService);
            _snapshot = tuple.Item1;
            _runtime = (SnapshotParserNativeAOTRuntime)tuple.Item2;

            if (_runtime == null || _snapshot == null)
            {
                throw new ArgumentException("Did not find a Native AOT runtime.");
            }

            _serviceProvider = new ServiceProvider();
            _serviceProvider.AddService<ProcessSnapshot>(_snapshot);
        }

        public IServiceProvider Services => _serviceProvider;

        public int Id => -1;

        public RuntimeType RuntimeType => RuntimeType.NativeAOT;

        public IModule RuntimeModule => null;

        public ProcessSnapshot Snapshot => _snapshot;

        public string GetDacFilePath()
        {
            return null;
        }

        public string GetDbiFilePath()
        {
            return null;
        }

        public static bool HasNativeAOTRuntime(ITarget target, IMemoryService memoryService)
        {
            return GetDotNetRuntime(target, memoryService).Item2 != null;
        }

        private static (ProcessSnapshot, IDotNetRuntime) GetDotNetRuntime(ITarget target, IMemoryService memoryService)
        {
            ProcessSnapshotDataSourceWrapper dataSource = new ProcessSnapshotDataSourceWrapper(memoryService, target, target.Services);

            try
            {
                IModuleService moduleService = target.Services.GetService<IModuleService>();
                ProcessSnapshot snapshot = new ProcessSnapshot(dataSource);
                foreach (IDotNetRuntime runtime in snapshot.Runtimes)
                {
                    if (runtime is SnapshotParserNativeAOTRuntime)
                    {
                        return (snapshot, runtime);
                    }
                }
            }
            catch (Exception)
            {

            }

            return (null, null);
        }
    }
}
