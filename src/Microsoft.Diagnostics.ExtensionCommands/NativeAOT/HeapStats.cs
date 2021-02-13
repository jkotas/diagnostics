using Microsoft.Diagnostics.RuntimeSnapshotParser.NativeAOT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Microsoft.Diagnostics.ExtensionCommands.NativeAOT
{
    internal class HeapStats
    {
        private Dictionary<ulong, TypeStats> _typeStats = new Dictionary<ulong, TypeStats>();
        private Action<string> _writer = null;
        private bool _shouldSort = false;

        class TypeStats
        {
            public NativeAOTType type;
            public uint count;
            public ulong totalSizeInBytes;
        }

        public HeapStats(Action<string> writer)
        {
            _writer = writer;
        }

        public void Add(NativeAOTType type, ulong sizeInBytes)
        {
            TypeStats stats = GetOrCreateTypeStats(type);
            stats.count++;
            stats.totalSizeInBytes += sizeInBytes;
        }

        public void Print()
        {
            WriteLine();
            WriteLine("Statistics:");
            WriteLine($"{FormatHelpers.PadByNumberSize("EEType  ", false)} {FormatHelpers.PadByNumberSize("Count  ", false)} {FormatHelpers.PadByNumberSize("TotalSize  ", false)} Class Name");
            WriteLine("--------------------------------------------------------------------------------------");

            IEnumerable<TypeStats> statsList = _typeStats.Values;
            if (_shouldSort)
            {
                statsList = statsList.OrderBy((stat) => stat.totalSizeInBytes);
            }

            uint count = 0;
            foreach (TypeStats stats in statsList)
            {
                count += stats.count;
                WriteLine($"{FormatHelpers.PadByNumberSize(stats.type.Address)} {FormatHelpers.PadByNumberSize(stats.count)} {FormatHelpers.PadByNumberSize(stats.totalSizeInBytes)} {stats.type.FullName}");
            }

            WriteLine($"Total {count} objects");
        }

        public void Sort()
        {
            _shouldSort = true;
        }

        private TypeStats GetOrCreateTypeStats(NativeAOTType eeType)
        {
            ulong eeTypeAddr = eeType.Address;
            if (!_typeStats.ContainsKey(eeTypeAddr))
            {
                TypeStats stats = new TypeStats()
                {
                    type = eeType
                };
                _typeStats.Add(eeTypeAddr, stats);
            }

            return _typeStats[eeTypeAddr];
        }

        private void WriteLine()
        {
            WriteLine("");
        }

        private void WriteLine(string line)
        {
            Write(line);
            Write(Environment.NewLine);
        }

        private void Write(string line)
        {
            _writer(line);
        }
    }
}
