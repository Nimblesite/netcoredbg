using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using NetcoreDbgTest;
using NetcoreDbgTest.VSCode;
using NetcoreDbgTest.Script;
using NetcoreDbgTest.GetDeltaApi;

using Newtonsoft.Json;

namespace NetcoreDbgTest.Script
{
    class Context
    {
        public void PrepareStart(string caller_trace)
        {
            InitializeRequest initializeRequest = new InitializeRequest();
            initializeRequest.arguments.clientID = "vscode";
            initializeRequest.arguments.clientName = "Visual Studio Code";
            initializeRequest.arguments.adapterID = "coreclr";
            initializeRequest.arguments.pathFormat = "path";
            initializeRequest.arguments.linesStartAt1 = true;
            initializeRequest.arguments.columnsStartAt1 = true;
            initializeRequest.arguments.supportsVariableType = true;
            initializeRequest.arguments.supportsVariablePaging = true;
            initializeRequest.arguments.supportsRunInTerminalRequest = true;
            initializeRequest.arguments.locale = "en-us";
            var initializeResult = VSCodeDebugger.Request(initializeRequest);
            Assert.True(initializeResult.Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
            // A client can only discover the applyDeltas request through this
            // capability, so its absence is a protocol regression.
            Assert.True(initializeResult.ResponseStr.Contains("\"supportsHotReload\":true"),
                        @"__FILE__:__LINE__"+"\n"+caller_trace);

            LaunchRequest launchRequest = new LaunchRequest();
            launchRequest.arguments.name = ".NET Core Launch (console) with pipeline";
            launchRequest.arguments.type = "coreclr";
            launchRequest.arguments.preLaunchTask = "build";
            launchRequest.arguments.program = ControlInfo.TargetAssemblyPath;
            launchRequest.arguments.cwd = "";
            launchRequest.arguments.console = "internalConsole";
            launchRequest.arguments.stopAtEntry = true;
            launchRequest.arguments.internalConsoleOptions = "openOnSessionStart";
            launchRequest.arguments.__sessionId = Guid.NewGuid().ToString();
            // Deltas are refused unless the debuggee was launched for Hot
            // Reload (DOTNET_MODIFIABLE_ASSEMBLIES must reach the runtime).
            launchRequest.arguments.hotReload = true;
            Assert.True(VSCodeDebugger.Request(launchRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void PrepareEnd(string caller_trace)
        {
            ConfigurationDoneRequest configurationDoneRequest = new ConfigurationDoneRequest();
            Assert.True(VSCodeDebugger.Request(configurationDoneRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void WasEntryPointHit(string caller_trace)
        {
            Func<string, bool> filter = (resJSON) => {
                if (VSCodeDebugger.isResponseContainProperty(resJSON, "event", "stopped")
                    && VSCodeDebugger.isResponseContainProperty(resJSON, "reason", "entry")) {
                    threadId = Convert.ToInt32(VSCodeDebugger.GetResponsePropertyValue(resJSON, "threadId"));
                    return true;
                }
                return false;
            };

            Assert.True(VSCodeDebugger.IsEventReceived(filter), @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void WasBreakpointHit(string caller_trace, int bpNumLine)
        {
            Func<string, bool> filter = (resJSON) => {
                if (VSCodeDebugger.isResponseContainProperty(resJSON, "event", "stopped")
                    && VSCodeDebugger.isResponseContainProperty(resJSON, "reason", "breakpoint")) {
                    threadId = Convert.ToInt32(VSCodeDebugger.GetResponsePropertyValue(resJSON, "threadId"));
                    return true;
                }
                return false;
            };

            Assert.True(VSCodeDebugger.IsEventReceived(filter), @"__FILE__:__LINE__"+"\n"+caller_trace);

            StackTraceRequest stackTraceRequest = new StackTraceRequest();
            stackTraceRequest.arguments.threadId = threadId;
            stackTraceRequest.arguments.startFrame = 0;
            stackTraceRequest.arguments.levels = 20;
            var ret = VSCodeDebugger.Request(stackTraceRequest);
            Assert.True(ret.Success, @"__FILE__:__LINE__"+"\n"+caller_trace);

            StackTraceResponse stackTraceResponse =
                JsonConvert.DeserializeObject<StackTraceResponse>(ret.ResponseStr);

            if (stackTraceResponse.body.stackFrames[0].line == bpNumLine)
                return;

            throw new ResultNotSuccessException(@"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void WasBreakHit(string caller_trace, int bpNumLine)
        {
            Func<string, bool> filter = (resJSON) => {
                if (VSCodeDebugger.isResponseContainProperty(resJSON, "event", "stopped")
                    && VSCodeDebugger.isResponseContainProperty(resJSON, "reason", "pause")) {
                    threadId = Convert.ToInt32(VSCodeDebugger.GetResponsePropertyValue(resJSON, "threadId"));
                    return true;
                }
                return false;
            };

            Assert.True(VSCodeDebugger.IsEventReceived(filter), @"__FILE__:__LINE__"+"\n"+caller_trace);

            StackTraceRequest stackTraceRequest = new StackTraceRequest();
            stackTraceRequest.arguments.threadId = threadId;
            stackTraceRequest.arguments.startFrame = 0;
            stackTraceRequest.arguments.levels = 20;
            var ret = VSCodeDebugger.Request(stackTraceRequest);
            Assert.True(ret.Success, @"__FILE__:__LINE__"+"\n"+caller_trace);

            StackTraceResponse stackTraceResponse =
                JsonConvert.DeserializeObject<StackTraceResponse>(ret.ResponseStr);

            if (stackTraceResponse.body.stackFrames[0].line == bpNumLine)
                return;

            throw new ResultNotSuccessException(@"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void SetBreakpoints(string caller_trace, string bpFileName, params int[] bpNumLines)
        {
            SetBreakpointsRequest setBreakpointsRequest = new SetBreakpointsRequest();
            setBreakpointsRequest.arguments.source.name = bpFileName;
            setBreakpointsRequest.arguments.source.path = ControlInfo.SourceFilesPath;
            foreach (int line in bpNumLines) {
                setBreakpointsRequest.arguments.lines.Add(line);
                setBreakpointsRequest.arguments.breakpoints.Add(new SourceBreakpoint(line, null));
            }
            setBreakpointsRequest.arguments.sourceModified = false;
            Assert.True(VSCodeDebugger.Request(setBreakpointsRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void Continue(string caller_trace)
        {
            ContinueRequest continueRequest = new ContinueRequest();
            continueRequest.arguments.threadId = threadId;
            Assert.True(VSCodeDebugger.Request(continueRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void WasExit(string caller_trace)
        {
            bool wasExited = false;
            int ?exitCode = null;
            bool wasTerminated = false;

            Func<string, bool> filter = (resJSON) => {
                if (VSCodeDebugger.isResponseContainProperty(resJSON, "event", "exited")) {
                    wasExited = true;
                    ExitedEvent exitedEvent = JsonConvert.DeserializeObject<ExitedEvent>(resJSON);
                    exitCode = exitedEvent.body.exitCode;
                }
                if (VSCodeDebugger.isResponseContainProperty(resJSON, "event", "terminated")) {
                    wasTerminated = true;
                }
                if (wasExited && exitCode == 0 && wasTerminated)
                    return true;

                return false;
            };

            Assert.True(VSCodeDebugger.IsEventReceived(filter), @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void DebuggerExit(string caller_trace)
        {
            DisconnectRequest disconnectRequest = new DisconnectRequest();
            disconnectRequest.arguments = new DisconnectArguments();
            disconnectRequest.arguments.restart = false;
            Assert.True(VSCodeDebugger.Request(disconnectRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void CheckHostRuntimeVersion(string caller_trace)
        {
            Assert.True(GetDeltaApi.CheckRuntimeVersion(), @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void CheckHostOS(string caller_trace)
        {
            Assert.True(RuntimeInformation.IsOSPlatform(OSPlatform.Linux), @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void StartGenDeltaSession(string caller_trace)
        {
            string projectPath = Path.GetDirectoryName(ControlInfo.SourceFilesPath);
            Assert.True(GetDeltaApi.StartGenDeltaSession(projectPath), @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void EndGenDeltaSession(string caller_trace)
        {
            Assert.True(GetDeltaApi.EndGenDeltaSession(), @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void SdbPush(string caller_trace, string hostFullPath, string targetPath)
        {
            System.Diagnostics.Process process = new System.Diagnostics.Process();
            process.StartInfo.WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden;
            process.StartInfo.RedirectStandardInput = true;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.UseShellExecute = false;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                process.StartInfo.FileName = "bash";
                process.StartInfo.Arguments = "-c \"" + ControlInfo.SDB + " push " + hostFullPath + " " + targetPath + "\"";
            }
            else
                throw new Exception("Host OS not supported. " + @"__FILE__:__LINE__"+"\n"+caller_trace);

            process.Start();
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void WriteDeltas(string caller_trace, string fileName)
        {
            string hostPath;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                hostPath = Path.Combine(@"/tmp", fileName);
            else
                throw new Exception("Host OS not supported. " + @"__FILE__:__LINE__"+"\n"+caller_trace);

            string targetPath = @"/tmp";
            Assert.True(GetDeltaApi.WriteDeltas(hostPath), @"__FILE__:__LINE__"+"\n"+caller_trace);
            SdbPush(@"__FILE__:__LINE__"+"\n"+caller_trace, hostPath + ".metadata", targetPath);
            SdbPush(@"__FILE__:__LINE__"+"\n"+caller_trace, hostPath + ".il", targetPath);
            SdbPush(@"__FILE__:__LINE__"+"\n"+caller_trace, hostPath + ".pdb", targetPath);
            SdbPush(@"__FILE__:__LINE__"+"\n"+caller_trace, hostPath + ".bin", targetPath);
        }

        public void GetDelta(string caller_trace, string source, string sourceFileName)
        {
            Assert.True(GetDeltaApi.GetDeltas(source, sourceFileName, "", false), @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public void ApplyDeltas(string caller_trace, string fileName)
        {
            string targetPath = Path.Combine(@"/tmp", fileName);
            ApplyDeltasRequest applyDeltasRequest = new ApplyDeltasRequest();
            applyDeltasRequest.arguments.dllFileName = Path.GetFileName(ControlInfo.TargetAssemblyPath);
            applyDeltasRequest.arguments.metadataDeltaFile = targetPath + ".metadata";
            applyDeltasRequest.arguments.ilDeltaFile = targetPath + ".il";
            applyDeltasRequest.arguments.pdbDeltaFile = targetPath + ".pdb";
            applyDeltasRequest.arguments.lineUpdatesFile = targetPath + ".bin";
            // The regression: without the applyDeltas DAP request the adapter
            // answers E_NOTIMPL (0x80004001) and Hot Reload cannot work.
            Assert.True(VSCodeDebugger.Request(applyDeltasRequest).Success, @"__FILE__:__LINE__"+"\n"+caller_trace);
        }

        public Context(ControlInfo controlInfo, NetcoreDbgTestCore.DebuggerClient debuggerClient)
        {
            ControlInfo = controlInfo;
            VSCodeDebugger = new VSCodeDebugger(debuggerClient);
            GetDeltaApi = new GetDeltaApi.GetDeltaApi();
        }

        ControlInfo ControlInfo;
        VSCodeDebugger VSCodeDebugger;
        GetDeltaApi.GetDeltaApi GetDeltaApi;
        int threadId = -1;
    }
}

namespace VSCodeTestHotReload
{
    class Program
    {
        static void Main(string[] args)
        {
            Label.Checkpoint("init", "delta_applied", (Object context) => {
                Context Context = (Context)context;
                Context.CheckHostRuntimeVersion(@"__FILE__:__LINE__");
                Context.CheckHostOS(@"__FILE__:__LINE__");
                Context.PrepareStart(@"__FILE__:__LINE__");
                Context.SetBreakpoints(@"__FILE__:__LINE__", @"Program.cs", 8);
                Context.PrepareEnd(@"__FILE__:__LINE__");
                Context.WasEntryPointHit(@"__FILE__:__LINE__");
                Context.Continue(@"__FILE__:__LINE__");
                Context.WasBreakpointHit(@"__FILE__:__LINE__", 8);
                Context.StartGenDeltaSession(@"__FILE__:__LINE__");

                Context.GetDelta(@"__FILE__:__LINE__",
                @"using System;
                using System.Diagnostics;
                namespace TestAppHotReload
                {
                    class Program
                    {
                        static void Main(string[] args)
                        {
                            Console.WriteLine(""Hello World!"");
                            HotReloadTest();
                        }
                        static void HotReloadTest()
                        {
                            Console.WriteLine(""Updated string."");
                            HotReloadBreakTest1();
                            Debugger.Break();                                                                // line 16
                        }
                        static void HotReloadBreakTest1()
                        {
                            Console.WriteLine(""Added breakpoint line."");                                   // line 20
                        }
                    }
                }", @"Program.cs");
                Context.WriteDeltas(@"__FILE__:__LINE__", "tmp_delta_vscode1");
                Context.ApplyDeltas(@"__FILE__:__LINE__", "tmp_delta_vscode1");

                // A breakpoint set in code that only exists after the deltas
                // proves the runtime accepted them.
                Context.SetBreakpoints(@"__FILE__:__LINE__", @"Program.cs", 8, 20);
                Context.Continue(@"__FILE__:__LINE__");
            });

            Label.Checkpoint("delta_applied", "outdated_break", (Object context) => {
                Context Context = (Context)context;
                Context.WasBreakpointHit(@"__FILE__:__LINE__", 20);
                Context.Continue(@"__FILE__:__LINE__");
            });

            Label.Checkpoint("outdated_break", "finish", (Object context) => {
                Context Context = (Context)context;
                Context.WasBreakHit(@"__FILE__:__LINE__", 16);
                Context.Continue(@"__FILE__:__LINE__");
            });

            Label.Checkpoint("finish", "", (Object context) => {
                Context Context = (Context)context;
                Context.EndGenDeltaSession(@"__FILE__:__LINE__");
                Context.WasExit(@"__FILE__:__LINE__");
                Context.DebuggerExit(@"__FILE__:__LINE__");
            });
        }
    }
}
