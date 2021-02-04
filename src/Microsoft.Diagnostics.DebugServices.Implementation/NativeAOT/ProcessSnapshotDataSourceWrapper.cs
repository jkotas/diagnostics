using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.RuntimeSnapshotParser;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Microsoft.Diagnostics.DebugServices.Implementation.NativeAOT
{
    public class SnapshotParserModuleSymbolsWrapper : ISnapshotParserModuleSymbols
    {
        private IModuleSymbols _moduleSymbols;

        public SnapshotParserModuleSymbolsWrapper(IModuleSymbols moduleSymbols)
        {
            _moduleSymbols = moduleSymbols;
        }

        public bool TryGetSymbolAddress(string name, out ulong address)
        {
            return _moduleSymbols.TryGetSymbolAddress(name, out address);
        }

        public bool TryGetSymbolName(ulong address, out string symbol, out ulong displacement)
        {
            return _moduleSymbols.TryGetSymbolName(address, out symbol, out displacement);
        }
    }

    public class SnapshotParserExportSymbolsWrapper : ISnapshotParserExportSymbols
    {
        private IExportSymbols _exportSymbols;

        public SnapshotParserExportSymbolsWrapper(IExportSymbols exportSymbols)
        {
            _exportSymbols = exportSymbols;
        }

        public bool TryGetSymbolAddress(string name, out ulong address)
        {
            return _exportSymbols.TryGetSymbolAddress(name, out address);
        }
    }
    public class ProcessSnapshotDataSourceWrapper : IProcessSnapshotDataSource
    {
        private Diagnostics.Runtime.IDataReader _dataReader;
        private Lazy<IEnumerable<IModule>> _modules;
        private IServiceProvider _services;

        public ProcessSnapshotDataSourceWrapper(Diagnostics.Runtime.IDataReader dataReader, IServiceProvider services)
        {
            _dataReader = dataReader;
            _modules = new Lazy<IEnumerable<IModule>>(EnumerateModulesInner);
            _services = services;
        }

        public System.Runtime.InteropServices.Architecture Architecture => GetArchitecture();

        public IEnumerable<ulong> EnumerateModules()
        {
            return _modules.Value.Select(x => x.ImageBase);
        }

        public ISnapshotParserExportSymbols GetExportSymbolsForModule(ulong moduleBaseAddress)
        {
            IModule module = _modules.Value.Where(x => x.ImageBase == moduleBaseAddress).FirstOrDefault();
            if (module == null)
            {
                throw new ArgumentException("Module base address is not valid.");
            }

            IExportSymbols exportSymbols = module.Services.GetService<IExportSymbols>();
            return new SnapshotParserExportSymbolsWrapper(exportSymbols);
        }

        public ISnapshotParserModuleSymbols GetModuleSymbolsForModule(ulong moduleBaseAddress)
        {
            IModule module = _modules.Value.Where(x => x.ImageBase == moduleBaseAddress).FirstOrDefault();
            if (module == null)
            {
                throw new ArgumentException("Module base address is not valid.");
            }

            IModuleSymbols moduleSymbols = module.Services.GetService<IModuleSymbols>();
            return new SnapshotParserModuleSymbolsWrapper(moduleSymbols);
        }

        public bool ReadMemory(long address, Span<byte> buffer, out int bytesRead)
        {
            bytesRead = _dataReader.Read((ulong)address, buffer);
            return bytesRead > 0;
        }

        private System.Runtime.InteropServices.Architecture GetArchitecture()
        {
            switch (_dataReader.Architecture)
            {
                case Diagnostics.Runtime.Architecture.X86:
                    return System.Runtime.InteropServices.Architecture.X86;
                case Diagnostics.Runtime.Architecture.Amd64:
                    return System.Runtime.InteropServices.Architecture.X64;
                case Diagnostics.Runtime.Architecture.Arm:
                    return System.Runtime.InteropServices.Architecture.Arm;
                case Diagnostics.Runtime.Architecture.Arm64:
                    return System.Runtime.InteropServices.Architecture.Arm64;
                case Diagnostics.Runtime.Architecture.Unknown:
                default:
                    throw new InvalidOperationException("Unknown architecture for data reader.");
            }
        }

        private IEnumerable<IModule> EnumerateModulesInner()
        {
            IModuleService moduleService = _services.GetService<IModuleService>();
            return moduleService.EnumerateModules();
        }
    }
}
