﻿using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.RuntimeSnapshotParser;
using Microsoft.Diagnostics.RuntimeSnapshotParser.Common.GC;
using Microsoft.Diagnostics.RuntimeSnapshotParser.NativeAOT;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Reader = Microsoft.FileFormats.Reader;
using SizeT = Microsoft.FileFormats.SizeT;

namespace Microsoft.Diagnostics.ExtensionCommands.NativeAOT
{
    [Command(Name = "aotdumpasync", Help = "Displays state associated with async objects.")]
    public class NativeAOTDumpAsyncCommand : CommandBase
    {
        public ProcessSnapshot Snapshot { get; set; }

        public IModuleService ModuleService { get; set; }


        [Option(Name = "--type", Aliases = new string[] { "-t" }, Help = "Only lists those objects whose type name is a substring match of the specified string.")]
        public string Type { get; set; }

        [Option(Name = "--addr", Aliases = new string[] { "-a" }, Help = "Only print the object located at the specified address.")]
        public ulong Address { get; set; }

        [Option(Name = "--tasks", Help = "Print any task derived object.")]
        public bool Tasks { get; set; }

        [Option(Name = "--stacks", Help = "Print async stacks for objects.")]
        public bool Stacks { get; set; }

        public override void Invoke()
        {
            if (Snapshot == null)
            {
                WriteLine("Error: no Native AOT runtime detected.");
                return;
            }

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

            if (typeService == null)
            {
                WriteLine("Failed to request module type service, cannot walk async objects.");
                return;
            }

            WriteLine("--------------------------------------------------------------------------------------");

            foreach (IRuntimeGCHeap heap in runtime.GC.Heaps)
            {
                //throw new Exception("TODO: Stacks view. Filter to unique stacks if no filter is applied.");

                HeapWalker walker = heap.GetHeapWalker();

                foreach (IRuntimeObject obj in walker.EnumerateHeapObjects())
                {
                    if (obj.Size < 24)
                    {
                        // Too small to be a state machine or task
                        continue;
                    }

                    if (Matches(obj))
                    {
                        WriteLine($"Async object @ 0x{obj.Address:X}");
                        WriteLine($"    Type: {obj.Type.FullName}");
                        int state = GetState(typeService, runtime.Reader, obj);
                        DumpState(state);

                        IEnumerable<IRuntimeObject> continuations = GetContinuations(typeService, runtime.Reader, runtime, obj);
                        WriteLine($"    Number of Continuations: {continuations.Count}");
                        foreach (IRuntimeObject continuation in continuations)
                        {
                            Write($"    Continuation object @ 0x{continuation.Address:X} ");
                            if (continuation.Address != 0x0)
                            {
                                Write($"Type: {continuation.Type.FullName}");
                            }
                        }

                        WriteLine();
                        WriteLine();
                    }
                }
            }

            WriteLine("--------------------------------------------------------------------------------------");
        }

        private void DumpState(int state)
        {
            Write($"    State={state:X} ");

            Write("( ");
            // TaskCreationOptions.*
            if ((state & 0x01) != 0) Write("PreferFairness ");
            if ((state & 0x02) != 0) Write("LongRunning ");
            if ((state & 0x04) != 0) Write("AttachedToParent ");
            if ((state & 0x08) != 0) Write("DenyChildAttach ");
            if ((state & 0x10) != 0) Write("HideScheduler ");
            if ((state & 0x40) != 0) Write("RunContinuationsAsynchronously ");

            // InternalTaskOptions.*
            if ((state & 0x0200) != 0) Write("ContinuationTask ");
            if ((state & 0x0400) != 0) Write("PromiseTask ");
            if ((state & 0x1000) != 0) Write("LazyCancellation ");
            if ((state & 0x2000) != 0) Write("QueuedByRuntime ");
            if ((state & 0x4000) != 0) Write("DoNotDispose ");

            // TASK_STATE_*
            if ((state & 0x10000) != 0) Write("STARTED ");
            if ((state & 0x20000) != 0) Write("DELEGATE_INVOKED ");
            if ((state & 0x40000) != 0) Write("DISPOSED ");
            if ((state & 0x80000) != 0) Write("EXCEPTIONOBSERVEDBYPARENT ");
            if ((state & 0x100000) != 0) Write("CANCELLATIONACKNOWLEDGED ");
            if ((state & 0x200000) != 0) Write("FAULTED ");
            if ((state & 0x400000) != 0) Write("CANCELED ");
            if ((state & 0x800000) != 0) Write("WAITING_ON_CHILDREN ");
            if ((state & 0x1000000) != 0) Write("RAN_TO_COMPLETION ");
            if ((state & 0x2000000) != 0) Write("WAITINGFORACTIVATION ");
            if ((state & 0x4000000) != 0) Write("COMPLETION_RESERVED ");
            if ((state & 0x8000000) != 0) Write("THREAD_WAS_ABORTED ");
            if ((state & 0x10000000) != 0) Write("WAIT_COMPLETION_NOTIFICATION ");
            if ((state & 0x20000000) != 0) Write("EXECUTIONCONTEXT_IS_NULL ");
            if ((state & 0x40000000) != 0) Write("TASKSCHEDULED_WAS_FIRED ");
            Write(")");

            WriteLine();
        }

        private int GetState(IModuleTypeService typeService, Reader reader, IRuntimeObject obj)
        {
            IField stateField = GetField(typeService, obj, "m_stateFlags");
            int state = reader.Read<int>(obj.Address + stateField.Offset);
            return state;
        }

        private IEnumerable<IRuntimeObject> GetContinuations(IModuleTypeService typeService, Reader reader, NativeAOTRuntime runtime, IRuntimeObject obj)
        {
            List<IRuntimeObject> continuationObjects = new List<IRuntimeObject>();
            if (IsList(obj))
            {
                IField listItemsField = GetField(typeService, obj, "_items");
                ulong arrayAddrAddr = obj.Address + listItemsField.Offset;
                ulong arrayAddr = reader.Read<SizeT>(arrayAddrAddr);
                NativeAOTObject backingArray = new NativeAOTObject(arrayAddr, runtime);
                for (uint i = 0; i < backingArray.ComponentSize; ++i)
                {
                    ulong continuationAddrAddr = arrayAddr + (i * reader.SizeOf<SizeT>());
                    ulong continuationAddr = reader.Read<SizeT>(continuationAddrAddr);
                    if (continuationAddr != 0)
                    {
                        continuationObjects.Add(new NativeAOTObject(continuationAddr, runtime));
                    }
                }
            }
            else
            {
                IField continuationField = GetField(typeService, obj, "m_continuationObject");
                ulong continuationsObjAddr = reader.Read<SizeT>(obj.Address + continuationField.Offset);
                continuationObjects.Add(new NativeAOTObject(continuationsObjAddr, runtime));
            }

            return continuationObjects.Select(obj => ResolveContinuation(typeService, reader, obj));
        }

        private IRuntimeObject ResolveContinuation(IModuleTypeService typeService, Reader reader, NativeAOTRuntime runtime, IRuntimeObject continuationObj)
        {
            if (!TryGetField(typeService, continuationObj, "StateMachine", out _))
            {
                IField innerField;
                if (TryGetField(typeService, continuationObj, "m_task", out innerField))
                {
                    ulong taskAddrAddr = continuationObj.Address + innerField.Offset;
                    continuationObj = GetObjectFromPtrToPtr(reader, runtime, taskAddrAddr);
                }
                else
                {
                    if (TryGetField(typeService, continuationObj, "m_action", out innerField))
                    {

                    }
                }
            }

            return continuationObj;
        }

        private bool IsList(IRuntimeObject obj)
        {
            return obj.Type.FullName.StartsWith("S_P_CoreLib_System_Collections_Generic_List_1");
        }

        private static bool TryGetField(IModuleTypeService typeService, IRuntimeObject obj, string fieldName, out IField field)
        {
            try
            {
                field = GetField(typeService, obj, fieldName);
            }
            catch (Exception)
            {
                field = null;
                return false;
            }

            return true;
        }

        private static IField GetField(IModuleTypeService typeService, IRuntimeObject obj, string fieldName)
        {
            IType asyncObjectNativeType = null;
            if (!typeService.TryGetType(obj.Type.FullName, out asyncObjectNativeType))
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

        private bool Matches(IRuntimeObject obj)
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

        private bool IsAsyncType(IRuntimeObject obj)
        {
            return (Tasks && IsTaskType(obj)) || obj.Type.FullName.StartsWith("S_P_CoreLib_System_Runtime_CompilerServices_AsyncTaskMethodBuilder_1_AsyncStateMachineBox_1<");
        }

        private static bool IsTaskType(IRuntimeObject obj)
        {
            // TODO: the desktop/coreclr dumpasync will detect subtypes of task and report those too
            return obj.Type.FullName.StartsWith("S_P_CoreLib_System_Threading_Tasks_Task_1<")
                                  || obj.Type.FullName.Equals("S_P_CoreLib_System_Threading_Tasks_Task");
        }

        private static IRuntimeObject GetObjectFromPtrToPtr(Reader reader, NativeAOTRuntime runtime, ulong objAddrAddr)
        {
            ulong objAddr = reader.Read<SizeT>(objAddrAddr);
            IRuntimeObject obj = new NativeAOTObject(objAddr, runtime);
            return obj;
        }

    }
}
