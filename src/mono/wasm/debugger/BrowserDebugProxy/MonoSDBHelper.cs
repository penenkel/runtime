// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;

namespace Microsoft.WebAssembly.Diagnostics
{
    internal enum TokenType
    {
        MdtModule               = 0x00000000,       //
        MdtTypeRef              = 0x01000000,       //
        MdtTypeDef              = 0x02000000,       //
        MdtFieldDef             = 0x04000000,       //
        MdtMethodDef            = 0x06000000,       //
        MdtParamDef             = 0x08000000,       //
        MdtInterfaceImpl        = 0x09000000,       //
        MdtMemberRef            = 0x0a000000,       //
        MdtCustomAttribute      = 0x0c000000,       //
        MdtPermission           = 0x0e000000,       //
        MdtSignature            = 0x11000000,       //
        MdtEvent                = 0x14000000,       //
        MdtProperty             = 0x17000000,       //
        MdtModuleRef            = 0x1a000000,       //
        MdtTypeSpec             = 0x1b000000,       //
        MdtAssembly             = 0x20000000,       //
        MdtAssemblyRef          = 0x23000000,       //
        MdtFile                 = 0x26000000,       //
        MdtExportedType         = 0x27000000,       //
        MdtManifestResource     = 0x28000000,       //
        MdtGenericParam         = 0x2a000000,       //
        MdtMethodSpec           = 0x2b000000,       //
        MdtGenericParamConstraint = 0x2c000000,
        MdtString               = 0x70000000,       //
        MdtName                 = 0x71000000,       //
        MdtBaseType             = 0x72000000,       // Leave this on the high end value. This does not correspond to metadata table
    }

    internal enum CommandSet {
        Vm = 1,
        ObjectRef = 9,
        StringRef = 10,
        Thread = 11,
        ArrayRef = 13,
        EventRequest = 15,
        StackFrame = 16,
        AppDomain = 20,
        Assembly = 21,
        Method = 22,
        Type = 23,
        Module = 24,
        Field = 25,
        Event = 64,
        Pointer = 65
    }

    internal enum EventKind {
        VmStart = 0,
        VmDeath = 1,
        ThreadStart = 2,
        ThreadDeath = 3,
        AppDomainCreate = 4,
        AppDomainUnload = 5,
        MethodEntry = 6,
        MethodExit = 7,
        AssemblyLoad = 8,
        AssemblyUnload = 9,
        Breakpoint = 10,
        Step = 11,
        TypeLoad = 12,
        Exception = 13,
        KeepAlive = 14,
        UserBreak = 15,
        UserLog = 16,
        Crash = 17,
        EnC = 18,
        MethodUpdate = 19
    }

    internal enum ModifierKind {
        Count = 1,
        ThreadOnly = 3,
        LocationOnly = 7,
        ExceptionOnly = 8,
        Step = 10,
        AssemblyOnly = 11,
        SourceFileOnly = 12,
        TypeNameOnly = 13
    }


    internal enum SuspendPolicy {
        None = 0,
        EventThread = 1,
        All = 2
    }

    internal enum CmdVM {
        Version = 1,
        AllThreads = 2,
        Suspend = 3,
        Resume = 4,
        Exit = 5,
        Dispose = 6,
        InvokeMethod = 7,
        SetProtocolVersion = 8,
        AbortInvoke = 9,
        SetKeepAlive = 10,
        GetTypesForSourceFile = 11,
        GetTypes = 12,
        InvokeMethods = 13,
        StartBuffering = 14,
        StopBuffering = 15,
        VmReadMemory = 16,
        VmWriteMemory = 17,
        GetAssemblyByName = 18
    }

    internal enum CmdFrame {
        GetValues = 1,
        GetThis = 2,
        SetValues = 3,
        GetDomain = 4,
        SetThis = 5,
        GetArgument = 6,
        GetArguments = 7
    }

    internal enum CmdEvent {
        Composite = 100
    }

    internal enum CmdThread {
        GetFrameInfo = 1,
        GetName = 2,
        GetState = 3,
        GetInfo = 4,
        /* FIXME: Merge into GetInfo when the major protocol version is increased */
        GetId = 5,
        /* Ditto */
        GetTid = 6,
        SetIp = 7,
        GetElapsedTime = 8
    }

    internal enum CmdEventRequest {
        Set = 1,
        Clear = 2,
        ClearAllBreakpoints = 3
    }

    internal enum CmdAppDomain {
        GetRootDomain = 1,
        GetFriendlyName = 2,
        GetAssemblies = 3,
        GetEntryAssembly = 4,
        CreateString = 5,
        GetCorLib = 6,
        CreateBoxedValue = 7,
        CreateByteArray = 8,
    }

    internal enum CmdAssembly {
        GetLocation = 1,
        GetEntryPoint = 2,
        GetManifestModule = 3,
        GetObject = 4,
        GetType = 5,
        GetName = 6,
        GetDomain = 7,
        GetMetadataBlob = 8,
        GetIsDynamic = 9,
        GetPdbBlob = 10,
        GetTypeFromToken = 11,
        GetMethodFromToken = 12,
        HasDebugInfo = 13,
    }

    internal enum CmdModule {
        GetInfo = 1,
        ApplyChanges = 2,
    }

    internal enum CmdPointer{
        GetValue = 1
    }

    internal enum CmdMethod {
        GetName = 1,
        GetDeclaringType = 2,
        GetDebugInfo = 3,
        GetParamInfo = 4,
        GetLocalsInfo = 5,
        GetInfo = 6,
        GetBody = 7,
        ResolveToken = 8,
        GetCattrs = 9,
        MakeGenericMethod = 10,
        Token = 11,
        Assembly = 12,
        ClassToken = 13,
        AsyncDebugInfo = 14,
        GetNameFull = 15
    }

    internal enum CmdType {
        GetInfo = 1,
        GetMethods = 2,
        GetFields = 3,
        GetValues = 4,
        GetObject = 5,
        GetSourceFiles = 6,
        SetValues = 7,
        IsAssignableFrom = 8,
        GetProperties = 9,
        GetCattrs = 10,
        GetFieldCattrs = 11,
        GetPropertyCattrs = 12,
        /* FIXME: Merge into GetSourceFiles when the major protocol version is increased */
        GetSourceFiles2 = 13,
        /* FIXME: Merge into GetValues when the major protocol version is increased */
        GetValues2 = 14,
        GetMethodsByNameFlags = 15,
        GetInterfaces = 16,
        GetInterfacesMap = 17,
        IsInitialized = 18,
        CreateInstance = 19,
        GetValueSize = 20,
        GetValuesICorDbg = 21,
        GetParents = 22,
        Initialize = 23,
    }

    internal enum CmdArray {
        GetLength = 1,
        GetValues = 2,
        SetValues = 3,
        RefGetType = 4
    }


    internal enum CmdField {
        GetInfo = 1
    }

    internal enum CmdString {
        GetValue = 1,
        GetLength = 2,
        GetChars = 3
    }

    internal enum CmdObject {
        RefGetType = 1,
        RefGetValues = 2,
        RefIsCollected = 3,
        RefGetAddress = 4,
        RefGetDomain = 5,
        RefSetValues = 6,
        RefGetInfo = 7,
        GetValuesICorDbg = 8,
        RefDelegateGetMethod = 9,
        RefIsDelegate = 10
    }

    internal enum ElementType {
        End             = 0x00,
        Void            = 0x01,
        Boolean         = 0x02,
        Char            = 0x03,
        I1              = 0x04,
        U1              = 0x05,
        I2              = 0x06,
        U2              = 0x07,
        I4              = 0x08,
        U4              = 0x09,
        I8              = 0x0a,
        U8              = 0x0b,
        R4              = 0x0c,
        R8              = 0x0d,
        String          = 0x0e,
        Ptr             = 0x0f,
        ByRef           = 0x10,
        ValueType       = 0x11,
        Class           = 0x12,
        Var             = 0x13,
        Array           = 0x14,
        GenericInst     = 0x15,
        TypedByRef      = 0x16,
        I               = 0x18,
        U               = 0x19,
        FnPtr           = 0x1b,
        Object          = 0x1c,
        SzArray         = 0x1d,
        MVar            = 0x1e,
        CModReqD        = 0x1f,
        CModOpt         = 0x20,
        Internal        = 0x21,
        Modifier        = 0x40,
        Sentinel        = 0x41,
        Pinned          = 0x45,

        Type            = 0x50,
        Boxed           = 0x51,
        Enum            = 0x55
    }

    internal enum ValueTypeId {
        Null = 0xf0,
        Type = 0xf1,
        VType = 0xf2,
        FixedArray = 0xf3
    }
    internal enum MonoTypeNameFormat{
        FormatIL,
        FormatReflection,
        FullName,
        AssemblyQualified
    }

    internal enum StepFilter {
        None = 0,
        StaticCtor = 1,
        DebuggerHidden = 2,
        DebuggerStepThrough = 4,
        DebuggerNonUserCode = 8
    }

    internal enum StepSize
    {
        Minimal,
        Line
    }

    internal class MonoBinaryReader : BinaryReader
    {
        public MonoBinaryReader(Stream stream) : base(stream) {}

        internal static unsafe void PutBytesBE (byte *dest, byte *src, int count)
        {
            int i = 0;

            if (BitConverter.IsLittleEndian){
                dest += count;
                for (; i < count; i++)
                    *(--dest) = *src++;
            } else {
                for (; i < count; i++)
                    *dest++ = *src++;
            }
        }

        public override string ReadString()
        {
            var valueLen = ReadInt32();
            char[] value = new char[valueLen];
            Read(value, 0, valueLen);
            return new string(value);
        }
        public unsafe long ReadLong()
        {
            byte[] data = new byte[8];
            Read(data, 0, 8);

            long ret;
            fixed (byte *src = &data[0]){
                PutBytesBE ((byte *) &ret, src, 8);
            }

            return ret;
        }
        public override unsafe sbyte ReadSByte()
        {
            byte[] data = new byte[4];
            Read(data, 0, 4);

            int ret;
            fixed (byte *src = &data[0]){
                PutBytesBE ((byte *) &ret, src, 4);
            }
            return (sbyte)ret;
        }

        public unsafe byte ReadUByte()
        {
            byte[] data = new byte[4];
            Read(data, 0, 4);

            int ret;
            fixed (byte *src = &data[0]){
                PutBytesBE ((byte *) &ret, src, 4);
            }
            return (byte)ret;
        }

        public override unsafe int ReadInt32()
        {
            byte[] data = new byte[4];
            Read(data, 0, 4);
            int ret;
            fixed (byte *src = &data[0]){
                PutBytesBE ((byte *) &ret, src, 4);
            }
            return ret;
        }

        public override unsafe double ReadDouble()
        {
            byte[] data = new byte[8];
            Read(data, 0, 8);

            double ret;
            fixed (byte *src = &data[0]){
                PutBytesBE ((byte *) &ret, src, 8);
            }
            return ret;
        }

        public override unsafe uint ReadUInt32()
        {
            byte[] data = new byte[4];
            Read(data, 0, 4);

            uint ret;
            fixed (byte *src = &data[0]){
                PutBytesBE ((byte *) &ret, src, 4);
            }
            return ret;
        }
        public unsafe ushort ReadUShort()
        {
            byte[] data = new byte[4];
            Read(data, 0, 4);

            uint ret;
            fixed (byte *src = &data[0]){
                PutBytesBE ((byte *) &ret, src, 4);
            }
            return (ushort)ret;
        }
    }

    internal class MonoBinaryWriter : BinaryWriter
    {
        public MonoBinaryWriter(Stream stream) : base(stream) {}
        public void WriteString(string val)
        {
            Write(val.Length);
            Write(val.ToCharArray());
        }
        public void WriteLong(long val)
        {
            Write((int)((val >> 32) & 0xffffffff));
            Write((int)((val >> 0) & 0xffffffff));
        }
        public override void Write(int val)
        {
            byte[] bytes = BitConverter.GetBytes(val);
            Array.Reverse(bytes, 0, bytes.Length);
            Write(bytes);
        }
        public void WriteObj(DotnetObjectId objectId, MonoSDBHelper SdbHelper)
        {
            if (objectId.Scheme == "object")
            {
                Write((byte)ElementType.Class);
                Write(int.Parse(objectId.Value));
            }
            if (objectId.Scheme == "valuetype")
            {
                Write(SdbHelper.valueTypes[int.Parse(objectId.Value)].valueTypeBuffer);
            }
        }
        public async Task<bool> WriteConst(SessionId sessionId, LiteralExpressionSyntax constValue, MonoSDBHelper SdbHelper, CancellationToken token)
        {
            switch (constValue.Kind())
            {
                case SyntaxKind.NumericLiteralExpression:
                {
                    Write((byte)ElementType.I4);
                    Write((int)constValue.Token.Value);
                    return true;
                }
                case SyntaxKind.StringLiteralExpression:
                {
                    int stringId = await SdbHelper.CreateString(sessionId, (string)constValue.Token.Value, token);
                    Write((byte)ElementType.String);
                    Write((int)stringId);
                    return true;
                }
                case SyntaxKind.TrueLiteralExpression:
                {
                    Write((byte)ElementType.Boolean);
                    Write((int)1);
                    return true;
                }
                case SyntaxKind.FalseLiteralExpression:
                {
                    Write((byte)ElementType.Boolean);
                    Write((int)0);
                    return true;
                }
                case SyntaxKind.NullLiteralExpression:
                {
                    Write((byte)ValueTypeId.Null);
                    Write((byte)0); //not used
                    Write((int)0);  //not used
                    return true;
                }
                case SyntaxKind.CharacterLiteralExpression:
                {
                    Write((byte)ElementType.Char);
                    Write((int)(char)constValue.Token.Value);
                    return true;
                }
            }
            return false;
        }

        public async Task<bool> WriteJsonValue(SessionId sessionId, JObject objValue, MonoSDBHelper SdbHelper, CancellationToken token)
        {
            switch (objValue["type"].Value<string>())
            {
                case "number":
                {
                    Write((byte)ElementType.I4);
                    Write(objValue["value"].Value<int>());
                    return true;
                }
                case "string":
                {
                    int stringId = await SdbHelper.CreateString(sessionId, objValue["value"].Value<string>(), token);
                    Write((byte)ElementType.String);
                    Write((int)stringId);
                    return true;
                }
                case "boolean":
                {
                    Write((byte)ElementType.Boolean);
                    if (objValue["value"].Value<bool>())
                        Write((int)1);
                    else
                        Write((int)0);
                    return true;
                }
                case "object":
                {
                    DotnetObjectId.TryParse(objValue["objectId"]?.Value<string>(), out DotnetObjectId objectId);
                    WriteObj(objectId, SdbHelper);
                    return true;
                }
            }
            return false;
        }
    }
    internal class FieldTypeClass
    {
        public int Id { get; }
        public string Name { get; }
        public int TypeId { get; }
        public FieldTypeClass(int id, string name, int typeId)
        {
            Id = id;
            Name = name;
            TypeId = typeId;
        }
    }
    internal class ValueTypeClass
    {
        public byte[] valueTypeBuffer;
        public JArray valueTypeJson;
        public JArray valueTypeJsonProps;
        public int typeId;
        public JArray valueTypeProxy;
        public string valueTypeVarName;
        public bool valueTypeAutoExpand;
        public int Id;
        public ValueTypeClass(string varName, byte[] buffer, JArray json, int id, bool expand_properties, int valueTypeId)
        {
            valueTypeBuffer = buffer;
            valueTypeJson = json;
            typeId = id;
            valueTypeJsonProps = null;
            valueTypeProxy = null;
            valueTypeVarName = varName;
            valueTypeAutoExpand = expand_properties;
            Id = valueTypeId;
        }
    }
    internal class PointerValue
    {
        public long address;
        public int typeId;
        public string varName;
        public PointerValue(long address, int typeId, string varName)
        {
            this.address = address;
            this.typeId = typeId;
            this.varName = varName;
        }

    }
    internal class MonoSDBHelper
    {
        internal Dictionary<int, ValueTypeClass> valueTypes = new Dictionary<int, ValueTypeClass>();
        internal Dictionary<int, PointerValue> pointerValues = new Dictionary<int, PointerValue>();
        private static int debugger_object_id;
        private static int cmd_id;
        private static int GetId() {return cmd_id++;}
        private MonoProxy proxy;
        private static int MINOR_VERSION = 61;
        private static int MAJOR_VERSION = 2;
        private readonly ILogger logger;

        public MonoSDBHelper(MonoProxy proxy, ILogger logger)
        {
            this.proxy = proxy;
            this.logger = logger;
        }

        public void ClearCache()
        {
            valueTypes = new Dictionary<int, ValueTypeClass>();
            pointerValues = new Dictionary<int, PointerValue>();
        }

        public async Task<bool> SetProtocolVersion(SessionId sessionId, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(MAJOR_VERSION);
            commandParamsWriter.Write(MINOR_VERSION);
            commandParamsWriter.Write((byte)0);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdVM>(sessionId, CmdVM.SetProtocolVersion, commandParams, token);
            return true;
        }

        public async Task<bool> EnableReceiveRequests(SessionId sessionId, EventKind eventKind, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write((byte)eventKind);
            commandParamsWriter.Write((byte)SuspendPolicy.None);
            commandParamsWriter.Write((byte)0);
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdEventRequest>(sessionId, CmdEventRequest.Set, commandParams, token);
            return true;
        }

        internal async Task<MonoBinaryReader> SendDebuggerAgentCommandInternal(SessionId sessionId, int command_set, int command, MemoryStream parms, CancellationToken token)
        {
            Result res = await proxy.SendMonoCommand(sessionId, MonoCommands.SendDebuggerAgentCommand(GetId(), command_set, command, Convert.ToBase64String(parms.ToArray())), token);
            if (res.IsErr) {
                throw new Exception($"SendDebuggerAgentCommand Error - {(CommandSet)command_set} - {command}");
            }
            byte[] newBytes = Convert.FromBase64String(res.Value?["result"]?["value"]?["value"]?.Value<string>());
            var retDebuggerCmd = new MemoryStream(newBytes);
            var retDebuggerCmdReader = new MonoBinaryReader(retDebuggerCmd);
            return retDebuggerCmdReader;
        }

        internal CommandSet GetCommandSetForCommand<T>(T command) =>
            command switch {
                CmdVM => CommandSet.Vm,
                CmdObject => CommandSet.ObjectRef,
                CmdString => CommandSet.StringRef,
                CmdThread => CommandSet.Thread,
                CmdArray => CommandSet.ArrayRef,
                CmdEventRequest => CommandSet.EventRequest,
                CmdFrame => CommandSet.StackFrame,
                CmdAppDomain => CommandSet.AppDomain,
                CmdAssembly => CommandSet.Assembly,
                CmdMethod => CommandSet.Method,
                CmdType => CommandSet.Type,
                CmdModule => CommandSet.Module,
                CmdField => CommandSet.Field,
                CmdEvent => CommandSet.Event,
                CmdPointer => CommandSet.Pointer,
                _ => throw new Exception ("Unknown CommandSet")
            };

        internal Task<MonoBinaryReader> SendDebuggerAgentCommand<T>(SessionId sessionId, T command, MemoryStream parms, CancellationToken token) where T : Enum =>
            SendDebuggerAgentCommandInternal(sessionId, (int)GetCommandSetForCommand(command), (int)(object)command, parms, token);

        internal Task<MonoBinaryReader> SendDebuggerAgentCommandWithParms<T>(SessionId sessionId, T command, MemoryStream parms, int type, string extraParm, CancellationToken token) where T : Enum =>
            SendDebuggerAgentCommandWithParmsInternal(sessionId, (int)GetCommandSetForCommand(command), (int)(object)command, parms, type, extraParm, token);

        internal async Task<MonoBinaryReader> SendDebuggerAgentCommandWithParmsInternal(SessionId sessionId, int command_set, int command, MemoryStream parms, int type, string extraParm, CancellationToken token)
        {
            Result res = await proxy.SendMonoCommand(sessionId, MonoCommands.SendDebuggerAgentCommandWithParms(GetId(), command_set, command, Convert.ToBase64String(parms.ToArray()), parms.ToArray().Length, type, extraParm), token);
            if (res.IsErr) {
                throw new Exception("SendDebuggerAgentCommandWithParms Error");
            }
            byte[] newBytes = Convert.FromBase64String(res.Value?["result"]?["value"]?["value"]?.Value<string>());
            var retDebuggerCmd = new MemoryStream(newBytes);
            var retDebuggerCmdReader = new MonoBinaryReader(retDebuggerCmd);
            return retDebuggerCmdReader;
        }

        public async Task<int> CreateString(SessionId sessionId, string value, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdAppDomain>(sessionId, CmdAppDomain.GetRootDomain, commandParams, token);
            var root_domain = retDebuggerCmdReader.ReadInt32();
            commandParamsWriter.Write(root_domain);
            commandParamsWriter.WriteString(value);
            retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdAppDomain>(sessionId, CmdAppDomain.CreateString, commandParams, token);
            return retDebuggerCmdReader.ReadInt32();
        }

        public async Task<int> GetMethodToken(SessionId sessionId, int methodId, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(methodId);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdMethod>(sessionId, CmdMethod.Token, commandParams, token);
            return retDebuggerCmdReader.ReadInt32() & 0xffffff; //token
        }

        public async Task<int> GetMethodIdByToken(SessionId sessionId, int assembly_id, int method_token, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(assembly_id);
            commandParamsWriter.Write(method_token | (int)TokenType.MdtMethodDef);
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdAssembly>(sessionId, CmdAssembly.GetMethodFromToken, commandParams, token);
            return retDebuggerCmdReader.ReadInt32();
        }

        public async Task<int> GetAssemblyIdFromMethod(SessionId sessionId, int methodId, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(methodId);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdMethod>(sessionId, CmdMethod.Assembly, commandParams, token);
            return retDebuggerCmdReader.ReadInt32(); //assembly_id
        }

        public async Task<int> GetAssemblyId(SessionId sessionId, string asm_name, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.WriteString(asm_name);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdVM>(sessionId, CmdVM.GetAssemblyByName, commandParams, token);
            return retDebuggerCmdReader.ReadInt32();
        }

        public async Task<string> GetAssemblyNameFromModule(SessionId sessionId, int moduleId, CancellationToken token)
        {
            var command_params = new MemoryStream();
            var command_params_writer = new MonoBinaryWriter(command_params);
            command_params_writer.Write(moduleId);

            var ret_debugger_cmd_reader = await SendDebuggerAgentCommand<CmdModule>(sessionId, CmdModule.GetInfo, command_params, token);
            ret_debugger_cmd_reader.ReadString();
            return ret_debugger_cmd_reader.ReadString();
        }

        public async Task<string> GetAssemblyName(SessionId sessionId, int assembly_id, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(assembly_id);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdAssembly>(sessionId, CmdAssembly.GetLocation, commandParams, token);
            return retDebuggerCmdReader.ReadString();
        }


        public async Task<string> GetAssemblyNameFull(SessionId sessionId, int assembly_id, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(assembly_id);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdAssembly>(sessionId, CmdAssembly.GetName, commandParams, token);
            var name = retDebuggerCmdReader.ReadString();
            return name.Remove(name.IndexOf(",")) + ".dll";
        }

        public async Task<string> GetMethodName(SessionId sessionId, int methodId, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(methodId);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdMethod>(sessionId, CmdMethod.GetNameFull, commandParams, token);
            var methodName = retDebuggerCmdReader.ReadString();
            return methodName.Substring(methodName.IndexOf(":")+1);
        }

        public async Task<bool> MethodIsStatic(SessionId sessionId, int methodId, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(methodId);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdMethod>(sessionId, CmdMethod.GetInfo, commandParams, token);
            var flags = retDebuggerCmdReader.ReadInt32();
            return (flags & 0x0010) > 0; //check method is static
        }

        public async Task<int> GetParamCount(SessionId sessionId, int methodId, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(methodId);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdMethod>(sessionId, CmdMethod.GetParamInfo, commandParams, token);
            retDebuggerCmdReader.ReadInt32();
            int param_count = retDebuggerCmdReader.ReadInt32();
            return param_count;
        }

        public async Task<string> GetReturnType(SessionId sessionId, int methodId, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(methodId);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdMethod>(sessionId, CmdMethod.GetParamInfo, commandParams, token);
            retDebuggerCmdReader.ReadInt32();
            retDebuggerCmdReader.ReadInt32();
            retDebuggerCmdReader.ReadInt32();
            var retType = retDebuggerCmdReader.ReadInt32();
            var ret = await GetTypeName(sessionId, retType, token);
            return ret;
        }

        public async Task<string> GetParameters(SessionId sessionId, int methodId, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(methodId);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdMethod>(sessionId, CmdMethod.GetParamInfo, commandParams, token);
            retDebuggerCmdReader.ReadInt32();
            var paramCount = retDebuggerCmdReader.ReadInt32();
            retDebuggerCmdReader.ReadInt32();
            var retType = retDebuggerCmdReader.ReadInt32();
            var parameters = "(";
            for (int i = 0 ; i < paramCount; i++)
            {
                var paramType = retDebuggerCmdReader.ReadInt32();
                parameters += await GetTypeName(sessionId, paramType, token);
                parameters = parameters.Replace("System.Func", "Func");
                if (i + 1 < paramCount)
                    parameters += ",";
            }
            parameters += ")";
            return parameters;
        }

        public async Task<int> SetBreakpoint(SessionId sessionId, int methodId, long il_offset, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write((byte)EventKind.Breakpoint);
            commandParamsWriter.Write((byte)SuspendPolicy.None);
            commandParamsWriter.Write((byte)1);
            commandParamsWriter.Write((byte)ModifierKind.LocationOnly);
            commandParamsWriter.Write(methodId);
            commandParamsWriter.WriteLong(il_offset);
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdEventRequest>(sessionId, CmdEventRequest.Set, commandParams, token);
            return retDebuggerCmdReader.ReadInt32();
        }

        public async Task<bool> RemoveBreakpoint(SessionId sessionId, int breakpoint_id, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write((byte)EventKind.Breakpoint);
            commandParamsWriter.Write((int) breakpoint_id);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdEventRequest>(sessionId, CmdEventRequest.Clear, commandParams, token);

            if (retDebuggerCmdReader != null)
                return true;
            return false;
        }

        public async Task<bool> Step(SessionId sessionId, int thread_id, StepKind kind, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write((byte)EventKind.Step);
            commandParamsWriter.Write((byte)SuspendPolicy.None);
            commandParamsWriter.Write((byte)1);
            commandParamsWriter.Write((byte)ModifierKind.Step);
            commandParamsWriter.Write(thread_id);
            commandParamsWriter.Write((int)StepSize.Line);
            commandParamsWriter.Write((int)kind);
            commandParamsWriter.Write((int)(StepFilter.StaticCtor | StepFilter.DebuggerHidden)); //filter
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdEventRequest>(sessionId, CmdEventRequest.Set, commandParams, token);
            if (retDebuggerCmdReader == null)
                return false;
            var isBPOnManagedCode = retDebuggerCmdReader.ReadInt32();
            if (isBPOnManagedCode == 0)
                return false;
            return true;
        }

        public async Task<bool> ClearSingleStep(SessionId sessionId, int req_id, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write((byte)EventKind.Step);
            commandParamsWriter.Write((int) req_id);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdEventRequest>(sessionId, CmdEventRequest.Clear, commandParams, token);

            if (retDebuggerCmdReader != null)
                return true;
            return false;
        }

        public async Task<JObject> GetFieldValue(SessionId sessionId, int typeId, int fieldId, CancellationToken token)
        {
            var ret = new List<FieldTypeClass>();
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(typeId);
            commandParamsWriter.Write(1);
            commandParamsWriter.Write(fieldId);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdType>(sessionId, CmdType.GetValues, commandParams, token);
            return await CreateJObjectForVariableValue(sessionId, retDebuggerCmdReader, "", false, -1, token);
        }

        public async Task<int> TypeIsInitialized(SessionId sessionId, int typeId, CancellationToken token)
        {
            var ret = new List<FieldTypeClass>();
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(typeId);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdType>(sessionId, CmdType.IsInitialized, commandParams, token);
            return retDebuggerCmdReader.ReadInt32();
        }

        public async Task<int> TypeInitialize(SessionId sessionId, int typeId, CancellationToken token)
        {
            var ret = new List<FieldTypeClass>();
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(typeId);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdType>(sessionId, CmdType.Initialize, commandParams, token);
            return retDebuggerCmdReader.ReadInt32();
        }

        public async Task<List<FieldTypeClass>> GetTypeFields(SessionId sessionId, int type_id, CancellationToken token)
        {
            var ret = new List<FieldTypeClass>();
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(type_id);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdType>(sessionId, CmdType.GetFields, commandParams, token);
            var nFields = retDebuggerCmdReader.ReadInt32();

            for (int i = 0 ; i < nFields; i++)
            {
                int fieldId = retDebuggerCmdReader.ReadInt32(); //fieldId
                string fieldNameStr = retDebuggerCmdReader.ReadString();
                int typeId = retDebuggerCmdReader.ReadInt32(); //typeId
                retDebuggerCmdReader.ReadInt32(); //attrs
                int isSpecialStatic = retDebuggerCmdReader.ReadInt32(); //is_special_static
                if (isSpecialStatic == 1)
                    continue;
                if (fieldNameStr.Contains("k__BackingField"))
                {
                    fieldNameStr = fieldNameStr.Replace("k__BackingField", "");
                    fieldNameStr = fieldNameStr.Replace("<", "");
                    fieldNameStr = fieldNameStr.Replace(">", "");
                }
                ret.Add(new FieldTypeClass(fieldId, fieldNameStr, typeId));
            }
            return ret;
        }

        public string ReplaceCommonClassNames(string className)
        {
            className = className.Replace("System.String", "string");
            className = className.Replace("System.Boolean", "bool");
            className = className.Replace("System.Char", "char");
            className = className.Replace("System.Int32", "int");
            className = className.Replace("System.Object", "object");
            className = className.Replace("System.Void", "void");
            className = className.Replace("System.Byte", "byte");
            return className;
        }

        public async Task<string> GetDebuggerDisplayAttribute(SessionId sessionId, int objectId, int typeId, CancellationToken token)
        {
            string expr = "";
            var invokeParams = new MemoryStream();
            var invokeParamsWriter = new MonoBinaryWriter(invokeParams);
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(typeId);
            commandParamsWriter.Write(0);
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdType>(sessionId, CmdType.GetCattrs, commandParams, token);
            var count = retDebuggerCmdReader.ReadInt32();
            if (count == 0)
                return null;
            for (int i = 0 ; i < count; i++)
            {
                var methodId = retDebuggerCmdReader.ReadInt32();
                if (methodId == 0)
                    continue;
                commandParams = new MemoryStream();
                commandParamsWriter = new MonoBinaryWriter(commandParams);
                commandParamsWriter.Write(methodId);
                var retDebuggerCmdReader2 = await SendDebuggerAgentCommand<CmdMethod>(sessionId, CmdMethod.GetDeclaringType, commandParams, token);
                var customAttributeTypeId = retDebuggerCmdReader2.ReadInt32();
                var customAttributeName = await GetTypeName(sessionId, customAttributeTypeId, token);
                if (customAttributeName == "System.Diagnostics.DebuggerDisplayAttribute")
                {
                    invokeParamsWriter.Write((byte)ValueTypeId.Null);
                    invokeParamsWriter.Write((byte)0); //not used
                    invokeParamsWriter.Write(0); //not used
                    var parmCount = retDebuggerCmdReader.ReadInt32();
                    invokeParamsWriter.Write((int)1);
                    for (int j = 0; j < parmCount; j++)
                    {
                        invokeParamsWriter.Write((byte)retDebuggerCmdReader.ReadByte());
                        invokeParamsWriter.Write(retDebuggerCmdReader.ReadInt32());
                    }
                    var retMethod = await InvokeMethod(sessionId, invokeParams.ToArray(), methodId, "methodRet", token);
                    DotnetObjectId.TryParse(retMethod?["value"]?["objectId"]?.Value<string>(), out DotnetObjectId dotnetObjectId);
                    var displayAttrs = await GetObjectValues(sessionId, int.Parse(dotnetObjectId.Value), true, false, false, false, token);
                    var displayAttrValue = displayAttrs.FirstOrDefault(attr => attr["name"].Value<string>().Equals("Value"));
                    try {
                        ExecutionContext context = proxy.GetContext(sessionId);
                        var objectValues = await GetObjectValues(sessionId, objectId, true, false, false, false, token);

                        var thisObj = CreateJObject<string>("", "object", "", false, objectId:$"dotnet:object:{objectId}");
                        thisObj["name"] = "this";
                        objectValues.Add(thisObj);

                        var resolver = new MemberReferenceResolver(proxy, context, sessionId, objectValues, logger);
                        var dispAttrStr = displayAttrValue["value"]?["value"]?.Value<string>();
                        //bool noQuotes = false;
                        if (dispAttrStr.Contains(", nq"))
                        {
                            //noQuotes = true;
                            dispAttrStr = dispAttrStr.Replace(", nq", "");
                        }
                        expr = "$\"" + dispAttrStr + "\"";
                        JObject retValue = await resolver.Resolve(expr, token);
                        if (retValue == null)
                            retValue = await EvaluateExpression.CompileAndRunTheExpression(expr, resolver, token);
                        return retValue?["value"]?.Value<string>();
                    }
                    catch (Exception)
                    {
                        logger.LogDebug($"Could not evaluate DebuggerDisplayAttribute - {expr}");
                        return null;
                    }
                }
                else
                {
                    var parmCount = retDebuggerCmdReader.ReadInt32();
                    for (int j = 0; j < parmCount; j++)
                    {
                        //to read parameters
                        await CreateJObjectForVariableValue(sessionId, retDebuggerCmdReader, "varName", false, -1, token);
                    }
                }
            }
            return null;
        }

        public async Task<string> GetTypeName(SessionId sessionId, int type_id, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(type_id);
            commandParamsWriter.Write((int) MonoTypeNameFormat.FormatReflection);
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdType>(sessionId, CmdType.GetInfo, commandParams, token);

            retDebuggerCmdReader.ReadString();

            retDebuggerCmdReader.ReadString();

            string className = retDebuggerCmdReader.ReadString();

            className = className.Replace("+", ".");
            className = Regex.Replace(className, @"`\d+", "");
            className = className.Replace("[]", "__SQUARED_BRACKETS__");
            className = className.Replace("[", "<");
            className = className.Replace("]", ">");
            className = className.Replace("__SQUARED_BRACKETS__", "[]");
            className = className.Replace(",", ", ");
            className = ReplaceCommonClassNames(className);
            return className;
        }

        public async Task<string> GetStringValue(SessionId sessionId, int string_id, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(string_id);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdString>(sessionId, CmdString.GetValue, commandParams, token);
            var isUtf16 = retDebuggerCmdReader.ReadByte();
            if (isUtf16 == 0) {
                return retDebuggerCmdReader.ReadString();
            }
            return null;
        }
        public async Task<int> GetArrayLength(SessionId sessionId, int object_id, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(object_id);
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdArray>(sessionId, CmdArray.GetLength, commandParams, token);
            var length = retDebuggerCmdReader.ReadInt32();
            length = retDebuggerCmdReader.ReadInt32();
            return length;
        }
        public async Task<List<int>> GetTypeIdFromObject(SessionId sessionId, int object_id, bool withParents, CancellationToken token)
        {
            List<int> ret = new List<int>();
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(object_id);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdObject>(sessionId, CmdObject.RefGetType, commandParams, token);
            var type_id = retDebuggerCmdReader.ReadInt32();
            ret.Add(type_id);
            if (withParents)
            {
                commandParams = new MemoryStream();
                commandParamsWriter = new MonoBinaryWriter(commandParams);
                commandParamsWriter.Write(type_id);
                retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdType>(sessionId, CmdType.GetParents, commandParams, token);
                var parentsCount = retDebuggerCmdReader.ReadInt32();
                for (int i = 0 ; i < parentsCount; i++)
                {
                    ret.Add(retDebuggerCmdReader.ReadInt32());
                }
            }
            return ret;
        }

        public async Task<string> GetClassNameFromObject(SessionId sessionId, int object_id, CancellationToken token)
        {
            var type_id = await GetTypeIdFromObject(sessionId, object_id, false, token);
            return await GetTypeName(sessionId, type_id[0], token);
        }

        public async Task<int> GetTypeIdFromToken(SessionId sessionId, int assemblyId, int typeToken, CancellationToken token)
        {
            var ret = new List<string>();
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write((int)assemblyId);
            commandParamsWriter.Write((int)typeToken);
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdAssembly>(sessionId, CmdAssembly.GetTypeFromToken, commandParams, token);
            return retDebuggerCmdReader.ReadInt32();
        }

        public async Task<int> GetMethodIdByName(SessionId sessionId, int type_id, string method_name, CancellationToken token)
        {
            var ret = new List<string>();
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write((int)type_id);
            commandParamsWriter.WriteString(method_name);
            commandParamsWriter.Write((int)(0x10 | 4 | 0x20)); //instance methods
            commandParamsWriter.Write((int)1); //case sensitive
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdType>(sessionId, CmdType.GetMethodsByNameFlags, commandParams, token);
            var nMethods = retDebuggerCmdReader.ReadInt32();
            return retDebuggerCmdReader.ReadInt32();
        }

        public async Task<bool> IsDelegate(SessionId sessionId, int objectId, CancellationToken token)
        {
            var ret = new List<string>();
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write((int)objectId);
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdObject>(sessionId, CmdObject.RefIsDelegate, commandParams, token);
            return retDebuggerCmdReader.ReadByte() == 1;
        }

        public async Task<int> GetDelegateMethod(SessionId sessionId, int objectId, CancellationToken token)
        {
            var ret = new List<string>();
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write((int)objectId);
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdObject>(sessionId, CmdObject.RefDelegateGetMethod, commandParams, token);
            return retDebuggerCmdReader.ReadInt32();
        }

        public async Task<string> GetDelegateMethodDescription(SessionId sessionId, int objectId, CancellationToken token)
        {
            var methodId = await GetDelegateMethod(sessionId, objectId, token);

            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(methodId);
            //Console.WriteLine("methodId - " + methodId);
            if (methodId == 0)
                return "";
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdMethod>(sessionId, CmdMethod.GetName, commandParams, token);
            var methodName = retDebuggerCmdReader.ReadString();

            var returnType = await GetReturnType(sessionId, methodId, token);
            var parameters = await GetParameters(sessionId, methodId, token);

            return $"{returnType} {methodName} {parameters}";
        }
        public async Task<JObject> InvokeMethod(SessionId sessionId, byte[] valueTypeBuffer, int methodId, string varName, CancellationToken token)
        {
            MemoryStream parms = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(parms);
            commandParamsWriter.Write(methodId);
            commandParamsWriter.Write(valueTypeBuffer);
            commandParamsWriter.Write(0);
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdVM>(sessionId, CmdVM.InvokeMethod, parms, token);
            retDebuggerCmdReader.ReadByte(); //number of objects returned.
            return await CreateJObjectForVariableValue(sessionId, retDebuggerCmdReader, varName, false, -1, token);
        }

        public async Task<int> GetPropertyMethodIdByName(SessionId sessionId, int typeId, string propertyName, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(typeId);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdType>(sessionId, CmdType.GetProperties, commandParams, token);
            var nProperties = retDebuggerCmdReader.ReadInt32();
            for (int i = 0 ; i < nProperties; i++)
            {
                retDebuggerCmdReader.ReadInt32(); //propertyId
                string propertyNameStr = retDebuggerCmdReader.ReadString();
                var getMethodId = retDebuggerCmdReader.ReadInt32();
                retDebuggerCmdReader.ReadInt32(); //setmethod
                var attrs = retDebuggerCmdReader.ReadInt32(); //attrs
                if (propertyNameStr == propertyName)
                {
                    return getMethodId;
                }
            }
            return -1;
        }

        public async Task<JArray> CreateJArrayForProperties(SessionId sessionId, int typeId, byte[] object_buffer, JArray attributes, bool isAutoExpandable, string objectId, bool isOwn, CancellationToken token)
        {
            JArray ret = new JArray();
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(typeId);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdType>(sessionId, CmdType.GetProperties, commandParams, token);
            var nProperties = retDebuggerCmdReader.ReadInt32();
            for (int i = 0 ; i < nProperties; i++)
            {
                retDebuggerCmdReader.ReadInt32(); //propertyId
                string propertyNameStr = retDebuggerCmdReader.ReadString();
                var getMethodId = retDebuggerCmdReader.ReadInt32();
                retDebuggerCmdReader.ReadInt32(); //setmethod
                var attrs = retDebuggerCmdReader.ReadInt32(); //attrs
                if (getMethodId == 0 || await GetParamCount(sessionId, getMethodId, token) != 0 || await MethodIsStatic(sessionId, getMethodId, token))
                    continue;
                JObject propRet = null;
                if (attributes.Where(attribute => attribute["name"].Value<string>().Equals(propertyNameStr)).Any())
                    continue;
                if (isAutoExpandable)
                {
                    try {
                        propRet = await InvokeMethod(sessionId, object_buffer, getMethodId, propertyNameStr, token);
                    }
                    catch (Exception)
                    {
                        continue;
                    }
                }
                else
                {
                    propRet = JObject.FromObject(new {
                            get = new
                            {
                                type = "function",
                                objectId = $"{objectId}:methodId:{getMethodId}",
                                className = "Function",
                                description = "get " + propertyNameStr + " ()",
                                methodId = getMethodId,
                                objectIdValue = objectId
                            },
                            name = propertyNameStr
                        });
                }
                if (isOwn)
                    propRet["isOwn"] = true;
                ret.Add(propRet);
            }
            return ret;
        }
        public async Task<JObject> GetPointerContent(SessionId sessionId, int pointerId, CancellationToken token)
        {
            var ret = new List<string>();
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.WriteLong(pointerValues[pointerId].address);
            commandParamsWriter.Write(pointerValues[pointerId].typeId);
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdPointer>(sessionId, CmdPointer.GetValue, commandParams, token);
            var varName = pointerValues[pointerId].varName;
            if (int.TryParse(varName, out _))
                varName = $"[{varName}]";
            return await CreateJObjectForVariableValue(sessionId, retDebuggerCmdReader, "*" + varName, false, -1, token);
        }
        public async Task<JArray> GetPropertiesValuesOfValueType(SessionId sessionId, int valueTypeId, CancellationToken token)
        {
            JArray ret = new JArray();
            var valueType = valueTypes[valueTypeId];
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(valueType.typeId);
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdType>(sessionId, CmdType.GetParents, commandParams, token);
            var parentsCount = retDebuggerCmdReader.ReadInt32();
            List<int> typesToGetProperties = new List<int>();
            typesToGetProperties.Add(valueType.typeId);
            for (int i = 0 ; i < parentsCount; i++)
            {
                typesToGetProperties.Add(retDebuggerCmdReader.ReadInt32());
            }
            for (int i = 0 ; i < typesToGetProperties.Count; i++)
            {
                var properties = await CreateJArrayForProperties(sessionId, typesToGetProperties[i], valueType.valueTypeBuffer, valueType.valueTypeJson, valueType.valueTypeAutoExpand, $"dotnet:valuetype:{valueType.Id}", i == 0, token);
                ret = new JArray(ret.Union(properties));
            }

            return ret;
        }

        public bool AutoExpandable(string className) {
            if (className == "System.DateTime" ||
                className == "System.DateTimeOffset" ||
                className == "System.TimeSpan")
                return true;
            return false;
        }

        public bool AutoInvokeToString(string className) {
            if (className == "System.DateTime" ||
                className == "System.DateTimeOffset" ||
                className == "System.TimeSpan" ||
                className == "System.Decimal"  ||
                className == "System.Guid")
                return true;
            return false;
        }

        public JObject CreateJObject<T>(T value, string type, string description, bool writable, string className = null, string objectId = null, string __custom_type = null, string subtype = null, bool isValueType = false, bool expanded = false, bool isEnum = false)
        {
            var ret = JObject.FromObject(new {
                    value = new
                    {
                        type,
                        value,
                        description
                    },
                    writable
                });
            if (__custom_type != null)
                ret["value"]["__custom_type"] = __custom_type;
            if (className != null)
                ret["value"]["className"] = className;
            if (objectId != null)
                ret["value"]["objectId"] = objectId;
            if (subtype != null)
                ret["value"]["subtype"] = subtype;
            if (isValueType)
                ret["value"]["isValueType"] = isValueType;
            if (expanded)
                ret["value"]["expanded"] = expanded;
            if (isEnum)
                ret["value"]["isEnum"] = isEnum;
            return ret;

        }
        public JObject CreateJObjectForBoolean(int value)
        {
            return CreateJObject<bool>(value == 0 ? false : true, "boolean", value == 0 ? "false" : "true", true);
        }

        public JObject CreateJObjectForNumber<T>(T value)
        {
            return CreateJObject<T>(value, "number", value.ToString(), true);
        }

        public JObject CreateJObjectForChar(int value)
        {
            var description = $"{value.ToString()} '{Convert.ToChar(value)}'";
            return CreateJObject<string>(description, "symbol", description, true);
        }

        public async Task<JObject> CreateJObjectForPtr(SessionId sessionId, ElementType etype, MonoBinaryReader retDebuggerCmdReader, string name, CancellationToken token)
        {
            string type;
            string value;
            long valueAddress = retDebuggerCmdReader.ReadLong();
            var typeId = retDebuggerCmdReader.ReadInt32();
            var className = "";
            if (etype == ElementType.FnPtr)
                className = "(*())"; //to keep the old behavior
            else
                className = "(" + await GetTypeName(sessionId, typeId, token) + ")";

            int pointerId = 0;
            if (valueAddress != 0 && className != "(void*)")
            {
                pointerId = Interlocked.Increment(ref debugger_object_id);
                type = "object";
                value =  className;
                pointerValues[pointerId] = new PointerValue(valueAddress, typeId, name);
            }
            else
            {
                type = "symbol";
                value = className + " " + valueAddress;
            }
            return CreateJObject<string>(value, type, value, false, className, $"dotnet:pointer:{pointerId}", "pointer");
        }

        public async Task<JObject> CreateJObjectForString(SessionId sessionId, MonoBinaryReader retDebuggerCmdReader, CancellationToken token)
        {
            var string_id = retDebuggerCmdReader.ReadInt32();
            var value = await GetStringValue(sessionId, string_id, token);
            return CreateJObject<string>(value, "string", value, false);
        }

        public async Task<JObject> CreateJObjectForArray(SessionId sessionId, MonoBinaryReader retDebuggerCmdReader, CancellationToken token)
        {
            var objectId = retDebuggerCmdReader.ReadInt32();
            var value = await GetClassNameFromObject(sessionId, objectId, token);
            var length = await GetArrayLength(sessionId, objectId, token);
            return CreateJObject<string>(null, "object", $"{value.ToString()}({length})", false, value.ToString(), "dotnet:array:" + objectId, null, "array");
        }

        public async Task<JObject> CreateJObjectForObject(SessionId sessionId, MonoBinaryReader retDebuggerCmdReader, int typeIdFromAttribute, CancellationToken token)
        {
            var objectId = retDebuggerCmdReader.ReadInt32();
            var className = "";
            var type_id = await GetTypeIdFromObject(sessionId, objectId, false, token);
            className = await GetTypeName(sessionId, type_id[0], token);
            var debuggerDisplayAttribute = await GetDebuggerDisplayAttribute(sessionId, objectId, type_id[0], token);
            var description = className.ToString();

            if (debuggerDisplayAttribute != null)
                description = debuggerDisplayAttribute;

            if (await IsDelegate(sessionId, objectId, token))
            {
                if (typeIdFromAttribute != -1)
                {
                    className = await GetTypeName(sessionId, typeIdFromAttribute, token);
                }

                description = await GetDelegateMethodDescription(sessionId, objectId, token);
                if (description == "")
                {
                    return CreateJObject<string>(className.ToString(), "symbol", className.ToString(), false);
                }
            }
            return CreateJObject<string>(null, "object", description, false, className, $"dotnet:object:{objectId}");
        }

        public async Task<JObject> CreateJObjectForValueType(SessionId sessionId, MonoBinaryReader retDebuggerCmdReader, string name, long initialPos, CancellationToken token)
        {
            JObject fieldValueType = null;
            var isEnum = retDebuggerCmdReader.ReadByte();
            var isBoxed = retDebuggerCmdReader.ReadByte() == 1;
            var typeId = retDebuggerCmdReader.ReadInt32();
            var className = await GetTypeName(sessionId, typeId, token);
            var description = className;
            var numFields = retDebuggerCmdReader.ReadInt32();
            var fields = await GetTypeFields(sessionId, typeId, token);
            JArray valueTypeFields = new JArray();
            if (className.IndexOf("System.Nullable<") == 0) //should we call something on debugger-agent to check???
            {
                retDebuggerCmdReader.ReadByte(); //ignoring the boolean type
                var isNull = retDebuggerCmdReader.ReadInt32();
                var value = await CreateJObjectForVariableValue(sessionId, retDebuggerCmdReader, name, false, -1, token);
                if (isNull != 0)
                    return value;
                else
                    return CreateJObject<string>(null, "object", className, false, className, null, null, "null", true);
            }
            for (int i = 0; i < numFields ; i++)
            {
                fieldValueType = await CreateJObjectForVariableValue(sessionId, retDebuggerCmdReader, fields.ElementAt(i).Name, true, fields.ElementAt(i).TypeId, token);
                valueTypeFields.Add(fieldValueType);
            }

            long endPos = retDebuggerCmdReader.BaseStream.Position;
            var valueTypeId = Interlocked.Increment(ref debugger_object_id);

            retDebuggerCmdReader.BaseStream.Position = initialPos;
            byte[] valueTypeBuffer = new byte[endPos - initialPos];
            retDebuggerCmdReader.Read(valueTypeBuffer, 0, (int)(endPos - initialPos));
            retDebuggerCmdReader.BaseStream.Position = endPos;
            valueTypes[valueTypeId] = new ValueTypeClass(name, valueTypeBuffer, valueTypeFields, typeId, AutoExpandable(className), valueTypeId);
            if (AutoInvokeToString(className) || isEnum == 1) {
                int methodId = await GetMethodIdByName(sessionId, typeId, "ToString", token);
                var retMethod = await InvokeMethod(sessionId, valueTypeBuffer, methodId, "methodRet", token);
                description = retMethod["value"]?["value"].Value<string>();
                if (className.Equals("System.Guid"))
                    description = description.ToUpper(); //to keep the old behavior
            }
            else if (isBoxed && numFields == 1) {
                return fieldValueType;
            }
            return CreateJObject<string>(null, "object", description, false, className, $"dotnet:valuetype:{valueTypeId}", null, null, true, true, isEnum == 1);
        }

        public async Task<JObject> CreateJObjectForNull(SessionId sessionId, MonoBinaryReader retDebuggerCmdReader, CancellationToken token)
        {
            string className = "";
            ElementType variableType = (ElementType)retDebuggerCmdReader.ReadByte();
            switch (variableType)
            {
                case ElementType.String:
                case ElementType.Class:
                {
                    var type_id = retDebuggerCmdReader.ReadInt32();
                    className = await GetTypeName(sessionId, type_id, token);
                    break;

                }
                case ElementType.SzArray:
                case ElementType.Array:
                {
                    ElementType byte_type = (ElementType)retDebuggerCmdReader.ReadByte();
                    var rank = retDebuggerCmdReader.ReadInt32();
                    if (byte_type == ElementType.Class) {
                        var internal_type_id = retDebuggerCmdReader.ReadInt32();
                    }
                    var type_id = retDebuggerCmdReader.ReadInt32();
                    className = await GetTypeName(sessionId, type_id, token);
                    break;
                }
                default:
                {
                    var type_id = retDebuggerCmdReader.ReadInt32();
                    className = await GetTypeName(sessionId, type_id, token);
                    break;
                }
            }
            return CreateJObject<string>(null, "object", className, false, className, null, null, "null");
        }

        public async Task<JObject> CreateJObjectForVariableValue(SessionId sessionId, MonoBinaryReader retDebuggerCmdReader, string name, bool isOwn, int typeIdFromAttribute, CancellationToken token)
        {
            long initialPos = retDebuggerCmdReader == null ? 0 : retDebuggerCmdReader.BaseStream.Position;
            ElementType etype = (ElementType)retDebuggerCmdReader.ReadByte();
            JObject ret = null;
            switch (etype) {
                case ElementType.I:
                case ElementType.U:
                case ElementType.Void:
                case (ElementType)ValueTypeId.Type:
                case (ElementType)ValueTypeId.VType:
                case (ElementType)ValueTypeId.FixedArray:
                    ret = JObject.FromObject(new {
                        value = new
                        {
                            type = "void",
                            value = "void",
                            description = "void"
                        }});
                    break;
                case ElementType.Boolean:
                {
                    var value = retDebuggerCmdReader.ReadInt32();
                    ret = CreateJObjectForBoolean(value);
                    break;
                }
                case ElementType.I1:
                {
                    var value = retDebuggerCmdReader.ReadSByte();
                    ret = CreateJObjectForNumber<int>(value);
                    break;
                }
                case ElementType.I2:
                case ElementType.I4:
                {
                    var value = retDebuggerCmdReader.ReadInt32();
                    ret = CreateJObjectForNumber<int>(value);
                    break;
                }
                case ElementType.U1:
                {
                    var value = retDebuggerCmdReader.ReadUByte();
                    ret = CreateJObjectForNumber<int>(value);
                    break;
                }
                case ElementType.U2:
                {
                    var value = retDebuggerCmdReader.ReadUShort();
                    ret = CreateJObjectForNumber<int>(value);
                    break;
                }
                case ElementType.U4:
                {
                    var value = retDebuggerCmdReader.ReadUInt32();
                    ret = CreateJObjectForNumber<uint>(value);
                    break;
                }
                case ElementType.R4:
                {
                    float value = BitConverter.Int32BitsToSingle(retDebuggerCmdReader.ReadInt32());
                    ret = CreateJObjectForNumber<float>(value);
                    break;
                }
                case ElementType.Char:
                {
                    var value = retDebuggerCmdReader.ReadInt32();
                    ret = CreateJObjectForChar(value);
                    break;
                }
                case ElementType.I8:
                {
                    long value = retDebuggerCmdReader.ReadLong();
                    ret = CreateJObjectForNumber<long>(value);
                    break;
                }
                case ElementType.U8:
                {
                    ulong high = (ulong) retDebuggerCmdReader.ReadInt32();
                    ulong low = (ulong) retDebuggerCmdReader.ReadInt32();
                    var value = ((high << 32) | low);
                    ret = CreateJObjectForNumber<ulong>(value);
                    break;
                }
                case ElementType.R8:
                {
                    double value = retDebuggerCmdReader.ReadDouble();
                    ret = CreateJObjectForNumber<double>(value);
                    break;
                }
                case ElementType.FnPtr:
                case ElementType.Ptr:
                {
                    ret = await CreateJObjectForPtr(sessionId, etype, retDebuggerCmdReader, name, token);
                    break;
                }
                case ElementType.String:
                {
                    ret = await CreateJObjectForString(sessionId, retDebuggerCmdReader, token);
                    break;
                }
                case ElementType.SzArray:
                case ElementType.Array:
                {
                    ret = await CreateJObjectForArray(sessionId, retDebuggerCmdReader, token);
                    break;
                }
                case ElementType.Class:
                case ElementType.Object:
                {
                    ret = await CreateJObjectForObject(sessionId, retDebuggerCmdReader, typeIdFromAttribute, token);
                    break;
                }
                case ElementType.ValueType:
                {
                    ret = await CreateJObjectForValueType(sessionId, retDebuggerCmdReader, name, initialPos, token);
                    break;
                }
                case (ElementType)ValueTypeId.Null:
                {
                    ret = await CreateJObjectForNull(sessionId, retDebuggerCmdReader, token);
                    break;
                }
            }
            if (isOwn)
                ret["isOwn"] = true;
            ret["name"] = name;
            return ret;
        }

        public async Task<bool> IsAsyncMethod(SessionId sessionId, int methodId, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(methodId);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdMethod>(sessionId, CmdMethod.AsyncDebugInfo, commandParams, token);
            return retDebuggerCmdReader.ReadByte() == 1 ; //token
        }

        public async Task<JArray> StackFrameGetValues(SessionId sessionId, MethodInfo method, int thread_id, int frame_id, VarInfo[] varIds, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            MonoBinaryReader retDebuggerCmdReader = null;
            commandParamsWriter.Write(thread_id);
            commandParamsWriter.Write(frame_id);
            commandParamsWriter.Write(varIds.Length);
            foreach (var var in varIds)
            {
                commandParamsWriter.Write(var.Index);
            }

            if (await IsAsyncMethod(sessionId, method.DebuggerId, token))
            {
                retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdFrame>(sessionId, CmdFrame.GetThis, commandParams, token);
                retDebuggerCmdReader.ReadByte(); //ignore type
                var objectId = retDebuggerCmdReader.ReadInt32();
                var asyncLocals = await GetObjectValues(sessionId, objectId, true, false, false, false, token);
                asyncLocals = new JArray(asyncLocals.Where( asyncLocal => !asyncLocal["name"].Value<string>().Contains("<>") || asyncLocal["name"].Value<string>().EndsWith("__this")));
                foreach (var asyncLocal in asyncLocals)
                {
                    if (asyncLocal["name"].Value<string>().EndsWith("__this"))
                        asyncLocal["name"] = "this";
                    else if (asyncLocal["name"].Value<string>().Contains('<'))
                        asyncLocal["name"] = Regex.Match(asyncLocal["name"].Value<string>(), @"\<([^)]*)\>").Groups[1].Value;
                }
                return asyncLocals;
            }

            JArray locals = new JArray();
            retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdFrame>(sessionId, CmdFrame.GetValues, commandParams, token);
            foreach (var var in varIds)
            {
                var var_json = await CreateJObjectForVariableValue(sessionId, retDebuggerCmdReader, var.Name, false, -1, token);
                locals.Add(var_json);
            }
            if (!method.IsStatic())
            {
                retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdFrame>(sessionId, CmdFrame.GetThis, commandParams, token);
                var var_json = await CreateJObjectForVariableValue(sessionId, retDebuggerCmdReader, "this", false, -1, token);
                var_json.Add("fieldOffset", -1);
                locals.Add(var_json);
            }
            return locals;

        }

        public async Task<JArray> GetValueTypeValues(SessionId sessionId, int valueTypeId, bool accessorPropertiesOnly, CancellationToken token)
        {
            if (valueTypes[valueTypeId].valueTypeJsonProps == null)
            {
                valueTypes[valueTypeId].valueTypeJsonProps = await GetPropertiesValuesOfValueType(sessionId, valueTypeId, token);
            }
            if (accessorPropertiesOnly)
                return valueTypes[valueTypeId].valueTypeJsonProps;
            var ret = new JArray(valueTypes[valueTypeId].valueTypeJson.Union(valueTypes[valueTypeId].valueTypeJsonProps));
            return ret;
        }

        public async Task<JArray> GetValueTypeProxy(SessionId sessionId, int valueTypeId, CancellationToken token)
        {
            if (valueTypes[valueTypeId].valueTypeProxy != null)
                return valueTypes[valueTypeId].valueTypeProxy;
            valueTypes[valueTypeId].valueTypeProxy = new JArray(valueTypes[valueTypeId].valueTypeJson);

            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(valueTypes[valueTypeId].typeId);

            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdType>(sessionId, CmdType.GetProperties, commandParams, token);
            var nProperties = retDebuggerCmdReader.ReadInt32();

            for (int i = 0 ; i < nProperties; i++)
            {
                retDebuggerCmdReader.ReadInt32(); //propertyId
                string propertyNameStr = retDebuggerCmdReader.ReadString();

                var getMethodId = retDebuggerCmdReader.ReadInt32();
                retDebuggerCmdReader.ReadInt32(); //setmethod
                retDebuggerCmdReader.ReadInt32(); //attrs
                if (await MethodIsStatic(sessionId, getMethodId, token))
                    continue;
                var command_params_to_proxy = new MemoryStream();
                var command_params_writer_to_proxy = new MonoBinaryWriter(command_params_to_proxy);
                command_params_writer_to_proxy.Write(getMethodId);
                command_params_writer_to_proxy.Write(valueTypes[valueTypeId].valueTypeBuffer);
                command_params_writer_to_proxy.Write(0);
                valueTypes[valueTypeId].valueTypeProxy.Add(JObject.FromObject(new {
                            get = JObject.FromObject(new {
                                commandSet = CommandSet.Vm,
                                command = CmdVM.InvokeMethod,
                                buffer = Convert.ToBase64String(command_params_to_proxy.ToArray()),
                                length = command_params_to_proxy.ToArray().Length
                                }),
                            name = propertyNameStr
                        }));
            }
            return valueTypes[valueTypeId].valueTypeProxy;
        }

        public async Task<JArray> GetArrayValues(SessionId sessionId, int arrayId, CancellationToken token)
        {
            var length = await GetArrayLength(sessionId, arrayId, token);
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write(arrayId);
            commandParamsWriter.Write(0);
            commandParamsWriter.Write(length);
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdArray>(sessionId, CmdArray.GetValues, commandParams, token);
            JArray array = new JArray();
            for (int i = 0 ; i < length ; i++)
            {
                var var_json = await CreateJObjectForVariableValue(sessionId, retDebuggerCmdReader, i.ToString(), false, -1, token);
                array.Add(var_json);
            }
            return array;
        }
        public async Task<bool> EnableExceptions(SessionId sessionId, string state, CancellationToken token)
        {

            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            commandParamsWriter.Write((byte)EventKind.Exception);
            commandParamsWriter.Write((byte)SuspendPolicy.None);
            commandParamsWriter.Write((byte)1);
            commandParamsWriter.Write((byte)ModifierKind.ExceptionOnly);
            commandParamsWriter.Write(0); //exc_class
            if (state == "all")
                commandParamsWriter.Write((byte)1); //caught
            else
                commandParamsWriter.Write((byte)0); //caught
            if (state == "uncaught" || state == "all")
                commandParamsWriter.Write((byte)1); //uncaught
            else
                commandParamsWriter.Write((byte)0); //uncaught
            commandParamsWriter.Write((byte)1);//subclasses
            commandParamsWriter.Write((byte)0);//not_filtered_feature
            commandParamsWriter.Write((byte)0);//everything_else
            var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdEventRequest>(sessionId, CmdEventRequest.Set, commandParams, token);
            return true;
        }
        public async Task<JArray> GetObjectValues(SessionId sessionId, int objectId, bool withProperties, bool withSetter, bool accessorPropertiesOnly, bool ownProperties, CancellationToken token)
        {
            var typeId = await GetTypeIdFromObject(sessionId, objectId, true, token);
            var className = await GetTypeName(sessionId, typeId[0], token);
            JArray ret = new JArray();
            if (await IsDelegate(sessionId, objectId, token))
            {
                var description = await GetDelegateMethodDescription(sessionId, objectId, token);

                var obj = JObject.FromObject(new {
                            value = new
                            {
                                type = "symbol",
                                value = description,
                                description
                            },
                            name = "Target"
                        });
                ret.Add(obj);
                return ret;
            }
            for (int i = 0; i < typeId.Count; i++)
            {
                if (!accessorPropertiesOnly)
                {
                    var fields = await GetTypeFields(sessionId, typeId[i], token);
                    JArray objectFields = new JArray();

                    var commandParams = new MemoryStream();
                    var commandParamsWriter = new MonoBinaryWriter(commandParams);
                    commandParamsWriter.Write(objectId);
                    commandParamsWriter.Write(fields.Count);
                    foreach (var field in fields)
                    {
                            commandParamsWriter.Write(field.Id);
                    }

                    var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdObject>(sessionId, CmdObject.RefGetValues, commandParams, token);

                    foreach (var field in fields)
                    {
                        long initialPos = retDebuggerCmdReader.BaseStream.Position;
                        int valtype = retDebuggerCmdReader.ReadByte();
                        retDebuggerCmdReader.BaseStream.Position = initialPos;
                        var fieldValue = await CreateJObjectForVariableValue(sessionId, retDebuggerCmdReader, field.Name, i == 0, field.TypeId, token);

                        if (ret.Where(attribute => attribute["name"].Value<string>().Equals(fieldValue["name"].Value<string>())).Any()) {
                            continue;
                        }
                        if (withSetter)
                        {
                            var command_params_to_set = new MemoryStream();
                            var command_params_writer_to_set = new MonoBinaryWriter(command_params_to_set);
                            command_params_writer_to_set.Write(objectId);
                            command_params_writer_to_set.Write(1);
                            command_params_writer_to_set.Write(field.Id);

                            fieldValue.Add("set", JObject.FromObject(new {
                                        commandSet = CommandSet.ObjectRef,
                                        command = CmdObject.RefSetValues,
                                        buffer = Convert.ToBase64String(command_params_to_set.ToArray()),
                                        valtype,
                                        length = command_params_to_set.ToArray().Length
                                }));
                        }
                        objectFields.Add(fieldValue);
                    }
                    ret = new JArray(ret.Union(objectFields));
                }
                if (!withProperties)
                    return ret;
                var command_params_obj = new MemoryStream();
                var commandParamsObjWriter = new MonoBinaryWriter(command_params_obj);
                commandParamsObjWriter.WriteObj(new DotnetObjectId("object", $"{objectId}"), this);
                var props = await CreateJArrayForProperties(sessionId, typeId[i], command_params_obj.ToArray(), ret, false, $"dotnet:object:{objectId}", i == 0, token);
                ret = new JArray(ret.Union(props));

                // ownProperties
                // Note: ownProperties should mean that we return members of the klass itself,
                // but we are going to ignore that here, because otherwise vscode/chrome don't
                // seem to ask for inherited fields at all.
                //if (ownProperties)
                    //break;
                /*if (accessorPropertiesOnly)
                    break;*/
            }
            if (accessorPropertiesOnly)
            {
                var retAfterRemove = new JArray();
                List<List<FieldTypeClass>> allFields = new List<List<FieldTypeClass>>();
                for (int i = 0; i < typeId.Count; i++)
                {
                    var fields = await GetTypeFields(sessionId, typeId[i], token);
                    allFields.Add(fields);
                }
                foreach (var item in ret)
                {
                    bool foundField = false;
                    for (int j = 0 ; j <  allFields.Count; j++)
                    {
                        foreach (var field in allFields[j])
                        {
                            if (field.Name.Equals(item["name"].Value<string>())) {
                                if (item["isOwn"] == null || (item["isOwn"].Value<bool>() && j == 0) || !item["isOwn"].Value<bool>())
                                    foundField = true;
                                break;
                            }
                        }
                        if (foundField)
                            break;
                    }
                    if (!foundField) {
                        retAfterRemove.Add(item);
                    }
                }
                ret = retAfterRemove;
            }
            return ret;
        }

        public async Task<JArray> GetObjectProxy(SessionId sessionId, int objectId, CancellationToken token)
        {
            var ret = await GetObjectValues(sessionId, objectId, false, true, false, false, token);
            var typeIds = await GetTypeIdFromObject(sessionId, objectId, true, token);
            foreach (var typeId in typeIds)
            {
                var commandParams = new MemoryStream();
                var commandParamsWriter = new MonoBinaryWriter(commandParams);
                commandParamsWriter.Write(typeId);

                var retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdType>(sessionId, CmdType.GetProperties, commandParams, token);
                var nProperties = retDebuggerCmdReader.ReadInt32();
                for (int i = 0 ; i < nProperties; i++)
                {
                    retDebuggerCmdReader.ReadInt32(); //propertyId
                    string propertyNameStr = retDebuggerCmdReader.ReadString();
                    var getMethodId = retDebuggerCmdReader.ReadInt32();
                    var setMethodId = retDebuggerCmdReader.ReadInt32(); //setmethod
                    var attrValue = retDebuggerCmdReader.ReadInt32(); //attrs
                    //Console.WriteLine($"{propertyNameStr} - {attrValue}");
                    if (ret.Where(attribute => attribute["name"].Value<string>().Equals(propertyNameStr)).Any())
                    {
                        var attr = ret.Where(attribute => attribute["name"].Value<string>().Equals(propertyNameStr)).First();

                        var command_params_to_set = new MemoryStream();
                        var command_params_writer_to_set = new MonoBinaryWriter(command_params_to_set);
                        command_params_writer_to_set.Write(setMethodId);
                        command_params_writer_to_set.Write((byte)ElementType.Class);
                        command_params_writer_to_set.Write(objectId);
                        command_params_writer_to_set.Write(1);
                        if (attr["set"] != null)
                        {
                            attr["set"] = JObject.FromObject(new {
                                        commandSet = CommandSet.Vm,
                                        command = CmdVM.InvokeMethod,
                                        buffer = Convert.ToBase64String(command_params_to_set.ToArray()),
                                        valtype = attr["set"]["valtype"],
                                        length = command_params_to_set.ToArray().Length
                                });
                        }
                        continue;
                    }
                    else
                    {
                        var command_params_to_get = new MemoryStream();
                        var command_params_writer_to_get = new MonoBinaryWriter(command_params_to_get);
                        command_params_writer_to_get.Write(getMethodId);
                        command_params_writer_to_get.Write((byte)ElementType.Class);
                        command_params_writer_to_get.Write(objectId);
                        command_params_writer_to_get.Write(0);

                        ret.Add(JObject.FromObject(new {
                                get = JObject.FromObject(new {
                                    commandSet = CommandSet.Vm,
                                    command = CmdVM.InvokeMethod,
                                    buffer = Convert.ToBase64String(command_params_to_get.ToArray()),
                                    length = command_params_to_get.ToArray().Length
                                    }),
                                name = propertyNameStr
                            }));
                    }
                    if (await MethodIsStatic(sessionId, getMethodId, token))
                        continue;
                }
            }
            return ret;
        }

        public async Task<bool> SetVariableValue(SessionId sessionId, int thread_id, int frame_id, int varId, string newValue, CancellationToken token)
        {
            var commandParams = new MemoryStream();
            var commandParamsWriter = new MonoBinaryWriter(commandParams);
            MonoBinaryReader retDebuggerCmdReader = null;
            commandParamsWriter.Write(thread_id);
            commandParamsWriter.Write(frame_id);
            commandParamsWriter.Write(1);
            commandParamsWriter.Write(varId);
            JArray locals = new JArray();
            retDebuggerCmdReader = await SendDebuggerAgentCommand<CmdFrame>(sessionId, CmdFrame.GetValues, commandParams, token);
            int etype = retDebuggerCmdReader.ReadByte();
            try
            {
                retDebuggerCmdReader = await SendDebuggerAgentCommandWithParms<CmdFrame>(sessionId, CmdFrame.SetValues, commandParams, etype, newValue, token);
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }
    }
}
