using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.RuntimeSnapshotParser;
using Microsoft.Diagnostics.RuntimeSnapshotParser.Common.GC;
using Microsoft.Diagnostics.RuntimeSnapshotParser.NativeAOT;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using Reader = Microsoft.FileFormats.Reader;
using SizeT = Microsoft.FileFormats.SizeT;

namespace Microsoft.Diagnostics.ExtensionCommands.NativeAOT
{
    [Command(Name = "aotdumpasync", Help = "Displays state associated with async objects.")]
    public class NativeAOTDumpAsyncCommand : CommandBase
    {
        class AsyncRecord
        {
            public Lazy<IEnumerable<NativeAOTObject>> continuations;
            public Lazy<int> state;
            public bool isTopLevel;
            public bool includeInReport;
        }

        private Lazy<IModuleTypeService> _typeService;

        private Lazy<NativeAOTRuntime> _runtime;

        public ProcessSnapshot Snapshot { get; set; }

        public IModuleService ModuleService { get; set; }

        private NativeAOTRuntime Runtime => _runtime.Value;

        private Reader Reader => Runtime.Reader;

        private IModuleTypeService TypeService => _typeService.Value;

        private Dictionary<NativeAOTObject, AsyncRecord> _asyncRecordsCache = new Dictionary<NativeAOTObject, AsyncRecord>();

        private Dictionary<ulong, NativeAOTObject> _objectsCache = new Dictionary<ulong, NativeAOTObject>();

        // EEType address is the key
        private Dictionary<ulong, Dictionary<string, IField>> _offsetsCache = new Dictionary<ulong, Dictionary<string, IField>>();

        [Option(Name = "--type", Aliases = new string[] { "-t" }, Help = "Only lists those objects whose type name is a substring match of the specified string.")]
        public string Type { get; set; }

        [Option(Name = "--addr", Aliases = new string[] { "-a" }, Help = "Only print the object located at the specified address.")]
        public ulong Address { get; set; }

        [Option(Name = "--tasks", Help = "Print any task derived object.")]
        public bool Tasks { get; set; }

        [Option(Name = "--stacks", Help = "Print async stacks for objects.")]
        public bool Stacks { get; set; }

        [Option(Name = "--completed", Help = "Print async operations that have already completed.")]
        public bool Completed { get; set; }

        [Option(Name = "--userdefined", Aliases= new string[] { "-u" }, Help = "Search for any user defined types that have the right fields to be async objects. This is very slow!")]
        public bool UserDefined { get; set; }

        public override void Invoke()
        {
            if (Snapshot == null)
            {
                WriteLine("Error: no Native AOT runtime detected.");
                return;
            }

            _typeService = new Lazy<IModuleTypeService>(() =>
            {
                IDotNetRuntime runtime = Snapshot.Runtimes.First();
                IModuleTypeService typeService = null;

                ulong runtimeModuleBase = runtime.RuntimeModuleBaseAddress;
                foreach (IModule module in ModuleService.EnumerateModules())
                {
                    if (module.ImageBase == runtimeModuleBase)
                    {
                        typeService = module.Services.GetService<IModuleTypeService>();
                        break;
                    }
                }

                return typeService;
            });

            _runtime = new Lazy<NativeAOTRuntime>(() =>
            {
                return (NativeAOTRuntime)Snapshot.Runtimes.First();
            });

            Stopwatch sw = new Stopwatch();
            sw.Start();

            List<NativeAOTObject> foundObjs = new List<NativeAOTObject>();
            foreach (IRuntimeGCHeap heap in Runtime.GC.Heaps)
            {
                HeapWalker walker = heap.GetHeapWalker();

                foreach (IRuntimeObject iobj in walker.EnumerateHeapObjects())
                {
                    NativeAOTObject obj = (NativeAOTObject)iobj;
                    if (obj.Size < 24)
                    {
                        // Too small to be a state machine or task
                        continue;
                    }

                    if (IsAsyncType(obj))
                    {
                        _objectsCache.Add(obj.Address, obj);
                        bool include = Matches(obj) && (Completed || !IsCompleted(obj));
                        SetIncludeInReport(obj, include);

                        if (Completed || !IsCompleted(obj))
                        {
                            foundObjs.Add(obj);
                        }
                    }
                }
            }

            sw.Stop();
            WriteLine($"Finished enumerating heap in {sw.ElapsedMilliseconds}ms.");
            sw.Restart();

            if (Address == 0)
            {
                PrintStats(foundObjs);
            }

            sw.Stop();
            WriteLine($"Finished printing stats in {sw.ElapsedMilliseconds}ms.");
            sw.Restart();

            if (Stacks && string.IsNullOrEmpty(Type))
            {
                CalculateTopLevelRecords(foundObjs, out int chains);
                WriteLine($"In {chains} chains.");
            }

            sw.Stop();
            WriteLine($"Finished calculating async stacks in {sw.ElapsedMilliseconds}ms.");
            sw.Restart();

            WriteLine();
            WriteLine($"{FormatHelpers.PadByNumberSize("Address", false)} {FormatHelpers.PadByNumberSize("EEtype", false)} {FormatHelpers.PadByNumberSize("State", false)} State/Type");
            PrintTopLevelObjs(foundObjs);

            sw.Stop();
            WriteLine($"Finished printing top level objs in {sw.ElapsedMilliseconds}ms.");
            sw.Restart();

            WriteLine("--------------------------------------------------------------------------------------");
        }

        private void PrintTopLevelObjs(List<NativeAOTObject> asyncObjs)
        {
            WriteLine();

            foreach (NativeAOTObject obj in asyncObjs)
            {
                if (!IsTopLevel(obj) || !IncludeInReport(obj))
                {
                    continue;
                }

                PrintAsyncRecord(obj);
                WriteLine();

                if (Stacks && GetContinuations(obj).Any())
                {
                    WalkContinuations(obj, (traversalObj, depth) =>
                    {
                        for (int i = 0; i < depth; ++i)
                        {
                            Write(".");
                        }

                        PrintAsyncRecord(traversalObj);
                        WriteLine();
                    });
                }

                WriteLine();
            }
        }

        private bool IsTopLevel(NativeAOTObject obj)
        {
            AsyncRecord record = GetOrCreateAsyncRecord(obj);
            return record.isTopLevel;
        }

        private void SetTopLevel(NativeAOTObject innerObj, bool topLevel)
        {
            AsyncRecord record = GetOrCreateAsyncRecord(innerObj);
            record.isTopLevel = topLevel;
        }

        private bool IncludeInReport(NativeAOTObject obj)
        {
            AsyncRecord record = GetOrCreateAsyncRecord(obj);
            return record.includeInReport;
        }

        private void SetIncludeInReport(NativeAOTObject innerObj, bool topLevel)
        {
            AsyncRecord record = GetOrCreateAsyncRecord(innerObj);
            record.includeInReport = topLevel;
        }

        private void PrintAsyncRecord(NativeAOTObject obj)
        {
            NativeAOTType eeType = (NativeAOTType)obj.Type;
            int state = GetState(obj);
            Write($"{FormatHelpers.PadByNumberSize(obj.Address)} {FormatHelpers.PadByNumberSize(eeType.Address)} {FormatHelpers.PadByNumberSize(state)} {DumpState(state)} {eeType.FullName}");
        }

        private void PrintStats(List<NativeAOTObject> asyncObjs)
        {
            HeapStats stats = new HeapStats((x) => Write(x));
            foreach (NativeAOTObject obj in asyncObjs)
            {
                stats.Add((NativeAOTType)obj.Type, obj.Size);
            }

            stats.Sort();
            stats.Print();
        }

        private void CalculateTopLevelRecords(List<NativeAOTObject> asyncObjs, out int chains)
        {
            int numChains = asyncObjs.Count;
            foreach (NativeAOTObject obj in asyncObjs)
            {
                WalkContinuations(obj, (continuationObj, _) =>
                {
                    foreach (NativeAOTObject innerObj in asyncObjs)
                    {
                        if (IsTopLevel(innerObj) && innerObj == continuationObj)
                        {
                            SetTopLevel(innerObj, false);
                            --numChains;
                        }
                    }
                });
            }

            chains = numChains;
        }

        private void WalkContinuations(NativeAOTObject obj, Action<NativeAOTObject, int> callback)
        {
            Stack<Tuple<int, NativeAOTObject>> continuations = new Stack<Tuple<int, NativeAOTObject>>();
            GetContinuations(obj).ForEach(obj => continuations.Push(Tuple.Create(1, obj)));

            HashSet<NativeAOTObject> seenObjs = new HashSet<NativeAOTObject>();
            while (continuations.Count > 0)
            {
                (int depth, NativeAOTObject traversalObj) = continuations.Pop();

                if (seenObjs.Contains(traversalObj))
                {
                    continue;
                }

                seenObjs.Add(traversalObj);

                IEnumerable<NativeAOTObject> continuationObjs = GetContinuations(traversalObj);
                continuationObjs.ForEach(obj => continuations.Push(Tuple.Create(depth + 1, obj)));

                callback(traversalObj, depth);
            }
        }

        private string DumpState(int state)
        {
            StringBuilder builder = new StringBuilder();

            builder.Append("( ");
            // TaskCreationOptions.*
            if ((state & 0x01) != 0) builder.Append("PreferFairness ");
            if ((state & 0x02) != 0) builder.Append("LongRunning ");
            if ((state & 0x04) != 0) builder.Append("AttachedToParent ");
            if ((state & 0x08) != 0) builder.Append("DenyChildAttach ");
            if ((state & 0x10) != 0) builder.Append("HideScheduler ");
            if ((state & 0x40) != 0) builder.Append("RunContinuationsAsynchronously ");

            // InternalTaskOptions.*
            if ((state & 0x0200) != 0) builder.Append("ContinuationTask ");
            if ((state & 0x0400) != 0) builder.Append("PromiseTask ");
            if ((state & 0x1000) != 0) builder.Append("LazyCancellation ");
            if ((state & 0x2000) != 0) builder.Append("QueuedByRuntime ");
            if ((state & 0x4000) != 0) builder.Append("DoNotDispose ");

            // TASK_STATE_*
            if ((state & 0x10000) != 0) builder.Append("STARTED ");
            if ((state & 0x20000) != 0) builder.Append("DELEGATE_INVOKED ");
            if ((state & 0x40000) != 0) builder.Append("DISPOSED ");
            if ((state & 0x80000) != 0) builder.Append("EXCEPTIONOBSERVEDBYPARENT ");
            if ((state & 0x100000) != 0) builder.Append("CANCELLATIONACKNOWLEDGED ");
            if ((state & 0x200000) != 0) builder.Append("FAULTED ");
            if ((state & 0x400000) != 0) builder.Append("CANCELED ");
            if ((state & 0x800000) != 0) builder.Append("WAITING_ON_CHILDREN ");
            if ((state & 0x1000000) != 0) builder.Append("RAN_TO_COMPLETION ");
            if ((state & 0x2000000) != 0) builder.Append("WAITINGFORACTIVATION ");
            if ((state & 0x4000000) != 0) builder.Append("COMPLETION_RESERVED ");
            if ((state & 0x8000000) != 0) builder.Append("THREAD_WAS_ABORTED ");
            if ((state & 0x10000000) != 0) builder.Append("WAIT_COMPLETION_NOTIFICATION ");
            if ((state & 0x20000000) != 0) builder.Append("EXECUTIONCONTEXT_IS_NULL ");
            if ((state & 0x40000000) != 0) builder.Append("TASKSCHEDULED_WAS_FIRED ");
            builder.Append(")");

            return builder.ToString();
        }

        private bool IsCompleted(NativeAOTObject obj)
        {
            int TASK_STATE_COMPLETED_MASK = 0x1600000;
            int state = GetState(obj); 
            return (state & TASK_STATE_COMPLETED_MASK) != 0;
        }

        private int GetState(NativeAOTObject obj)
        {
            AsyncRecord record = GetOrCreateAsyncRecord(obj);
            return record.state.Value;
        }

        private IEnumerable<NativeAOTObject> GetContinuations(NativeAOTObject obj)
        {
            AsyncRecord record = GetOrCreateAsyncRecord(obj);
            return record.continuations.Value;
        }

        private AsyncRecord GetOrCreateAsyncRecord(NativeAOTObject obj)
        {
            if (!_asyncRecordsCache.ContainsKey(obj))
            {
                AsyncRecord record = new AsyncRecord()
                {
                    continuations = new Lazy<IEnumerable<NativeAOTObject>>(() => ParseContinuations(obj)),
                    state = new Lazy<int>(() => ParseState(obj)),
                    isTopLevel = true,
                    includeInReport = Matches(obj)
                };

                _asyncRecordsCache.Add(obj, record);
            }

            return _asyncRecordsCache[obj];
        }

        private IEnumerable<NativeAOTObject> ParseContinuations(NativeAOTObject obj)
        {
            List<NativeAOTObject> continuationObjects = new List<NativeAOTObject>();
            if (IsList(obj))
            {
                IField listItemsField = GetField(obj, "_items");
                ulong arrayAddrAddr = obj.Address + listItemsField.Offset;
                NativeAOTObject backingArray = GetObjectFromPtrToRef(arrayAddrAddr);
                for (uint i = 0; i < backingArray.ComponentSize; ++i)
                {
                    ulong continuationAddrAddr = backingArray.Address + (i * Reader.SizeOf<SizeT>());
                    NativeAOTObject continuationObj = GetObjectFromPtrToRef(continuationAddrAddr);
                    if (continuationObj != null)
                    {
                        continuationObjects.Add(continuationObj);
                    }
                }
            }
            else
            {
                IField continuationField;
                if (TryGetField(obj, "m_continuationObject", out continuationField))
                {
                    ulong continuationObjAddrAddr = obj.Address + continuationField.Offset;
                    NativeAOTObject continuationObj = GetObjectFromPtrToRef(continuationObjAddrAddr);
                    if (continuationObj != null)
                    {
                        continuationObjects.Add(continuationObj);
                    }
                }
            }

            return continuationObjects.Select(obj => ResolveContinuation(obj));
        }

        private int ParseState(NativeAOTObject obj)
        {
            IField stateField;
            if (TryGetField(obj, "m_stateFlags", out stateField))
            {
                int state = Reader.Read<int>(obj.Address + stateField.Offset);
                return state;
            }

            return 0;
        }

        private NativeAOTObject GetOrCreateObject(ulong address)
        {
            if (!_objectsCache.ContainsKey(address))
            {
                _objectsCache.Add(address, new NativeAOTObject(address, Runtime));
            }

            return _objectsCache[address];
        }

        private NativeAOTObject ResolveContinuation(NativeAOTObject continuationObj)
        {
            NativeAOTObject tempObj;
            if (!TryGetField(continuationObj, "StateMachine", out _))
            {
                IField innerField;
                if (TryGetField(continuationObj, "m_task", out innerField))
                {
                    ulong taskAddrAddr = continuationObj.Address + innerField.Offset;
                    tempObj = GetObjectFromPtrToRef(taskAddrAddr);
                    if (tempObj != null)
                    {
                        continuationObj = tempObj;
                    }
                }
                else
                {
                    if (TryGetField(continuationObj, "m_action", out innerField))
                    {
                        ulong actionAddrAddr = continuationObj.Address + innerField.Offset;
                        tempObj = GetObjectFromPtrToRef(actionAddrAddr);
                        if (tempObj != null)
                        {
                            continuationObj = tempObj;
                        }
                    }

                    if (TryGetField(continuationObj, "_target", out innerField))
                    {
                        ulong targetAddrAddr = continuationObj.Address + innerField.Offset;
                        tempObj = GetObjectFromPtrToRef(targetAddrAddr);
                        if (tempObj != null)
                        {
                            continuationObj = tempObj;

                            if (continuationObj.Type.FullName.StartsWith("S_P_CoreLib_System_Runtime_CompilerServices_AsyncMethodBuilderCore_ContinuationWrapper")
                                && TryGetField(continuationObj, "_continuation", out innerField))
                            {
                                ulong wrapperAddrAddr = continuationObj.Address + innerField.Offset;
                                tempObj = GetObjectFromPtrToRef(wrapperAddrAddr);
                                if (tempObj != null)
                                {
                                    continuationObj = tempObj;
                                }
                            }
                        }
                    }
                }
            }

            return continuationObj;
        }

        private NativeAOTObject GetObjectFromPtrToRef(ulong objAddrAddr)
        {
            ulong objAddr = Reader.Read<SizeT>(objAddrAddr);
            if (objAddr == 0)
            {
                return null;
            }

            return GetOrCreateObject(objAddr);
        }

        private bool TryGetField(NativeAOTObject obj, string fieldName, out IField field)
        {
            NativeAOTType eeType = (NativeAOTType)obj.Type;
            if (!_offsetsCache.ContainsKey(eeType.Address))
            {
                Dictionary<string, IField> dict = new Dictionary<string, IField>();
                _offsetsCache.Add(eeType.Address, dict);
            }

            Dictionary<string, IField> fields = _offsetsCache[eeType.Address];
            if (!fields.ContainsKey(fieldName))
            {
                try
                {
                    field = GetField(obj, fieldName);
                }
                catch (Exception)
                {
                    field = null;
                }

                fields.Add(fieldName, field);
            }

            field = fields[fieldName];
            return field != null;
        }

        private IField GetField(NativeAOTObject obj, string fieldName)
        {
            IType asyncObjectNativeType = null;
            if (!TypeService.TryGetType(obj.Type.FullName, out asyncObjectNativeType))
            {
                throw new Exception($"Failed to get type information for {obj.Type.FullName}");
            }

            IField stateField = null;
            if (!asyncObjectNativeType.TryGetField(fieldName, out stateField))
            {
                throw new Exception($"Failed to get {fieldName} field for object of type {obj.Type.FullName}.");
            }

            return stateField;
        }

        private bool Matches(NativeAOTObject obj)
        {
            if (Address != 0)
            {
                return obj.Address == Address;
            }
            else
            {
                return IsAsyncType(obj)
                    && (string.IsNullOrEmpty(Type) || obj.Type.FullName.Contains(Type, StringComparison.InvariantCultureIgnoreCase));
            }
        }

        private bool IsList(NativeAOTObject obj)
        {
            return obj.Type.FullName.StartsWith("S_P_CoreLib_System_Collections_Generic_List_1");
        }

        private bool IsAsyncType(NativeAOTObject obj)
        {
            return obj.Type.FullName.StartsWith("S_P_CoreLib_System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_AsyncStateMachineBox_1<")
                    || (Tasks && IsTaskType(obj));
        }

        private bool IsTaskType(NativeAOTObject obj)
        {
            bool systemTask = obj.Type.FullName.StartsWith("S_P_CoreLib_System_Threading_Tasks_Task_1<") || obj.Type.FullName.StartsWith("S_P_CoreLib_System_Threading_Tasks_Task_DelayPromiseWithCancellation")
                                || obj.Type.FullName.StartsWith("S_P_CoreLib_System_Threading_Tasks_Task_WhenAllPromise")
                                || obj.Type.FullName.StartsWith("S_P_CoreLib_System_Threading_Tasks_TaskFactory_CompleteOnInvokePromise")
                                || obj.Type.FullName.StartsWith("S_P_CoreLib_System_Threading_Tasks_UnwrapPromise_1")
                                || obj.Type.FullName.StartsWith("S_P_CoreLib_System_Threading_Tasks_ContinuationResultTaskFromTask_1")
                                || obj.Type.FullName.StartsWith("S_P_CoreLib_System_Threading_Tasks_ContinuationTaskFromResultTask_1")
                                || obj.Type.FullName.StartsWith("S_P_CoreLib_System_Threading_Tasks_Task_TwoTaskWhenAnyPromise_1")
                                || obj.Type.FullName.StartsWith("S_P_CoreLib_System_Threading_Tasks_ContinuationResultTaskFromResultTask_2")
                                || obj.Type.FullName.StartsWith("S_P_CoreLib_System_Threading_Tasks_ContinuationTaskFromTask")
                                || obj.Type.FullName.Equals("S_P_CoreLib_System_Threading_Tasks_Task");

            if (systemTask)
            {
                return true;
            }
            else if (UserDefined)
            {
                return HasTaskMembersWeCareAbout(obj);
            }

            return false;
        }

        private bool HasTaskMembersWeCareAbout(NativeAOTObject obj)
        {
            // On desktop and coreclr we can query metadata to figure out if a type is a 
            // base class of System.Threading.Tasks.Task. We don't have that ability here.
            // The base class information exists in the symbols, but dbgeng doesn't expose it
            // to us. In the future it would be nice if we could use DIA or some other symbol
            // reader to query if it's a subtype of Task. For now we check if it has the 
            // Task fields we care about and assume.
            List<string> fieldsWeCareAbout = new List<string>
            {
                "m_stateFlags",
                "m_continuationObject"
            };

            foreach (string field in fieldsWeCareAbout)
            {
                if (!TryGetField(obj, field, out _))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
