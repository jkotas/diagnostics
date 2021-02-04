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

        public NativeAOTRuntime(ITarget target, IDataReader dataReader)
        {
            var tuple = GetDotNetRuntime(target, dataReader);
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

        public int Id => throw new NotImplementedException();

        public RuntimeType RuntimeType => RuntimeType.NativeAOT;

        public IModule RuntimeModule => throw new NotImplementedException();

        public ProcessSnapshot Snapshot => _snapshot;

        public string GetDacFilePath()
        {
            throw new ArgumentException("Native AOT does not have a DAC.");
        }

        public string GetDbiFilePath()
        {
            throw new ArgumentException("Native AOT does not have a DBI.");
        }

        public static bool HasNativeAOTRuntime(ITarget target, IDataReader dataReader)
        {
            return GetDotNetRuntime(target, dataReader).Item2 != null;
        }

        private static (ProcessSnapshot, IDotNetRuntime) GetDotNetRuntime(ITarget target, IDataReader dataReader)
        {
            ProcessSnapshotDataSourceWrapper dataSource = new ProcessSnapshotDataSourceWrapper(dataReader, target.Services);

            try
            {
                IModuleService moduleService = target.Services.GetService<IModuleService>();
                foreach (Module module in moduleService.EnumerateModules())
                {
                    ProcessSnapshot snapshot = new ProcessSnapshot(dataSource);
                    foreach (IDotNetRuntime runtime in snapshot.Runtimes)
                    {
                        if (runtime is SnapshotParserNativeAOTRuntime)
                        {
                            return (snapshot, runtime);
                        }
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
