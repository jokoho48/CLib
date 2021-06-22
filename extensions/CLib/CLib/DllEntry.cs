using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CLib {
    // ReSharper disable once UnusedMember.Global
    public class DllEntry {
        private static string _inputBuffer;
        private static string _outputBuffer;
        private static readonly Debugger Debugger;
        private static readonly Dictionary<string, string> AvailableExtensions = new Dictionary<string, string>();
        private static readonly Dictionary<int, Task<string>> Tasks = new Dictionary<int, Task<string>>();

        static DllEntry() {
            Debugger = new Debugger();
            Debugger.Log("Extension framework initializing");

            try {
                DetectExtensions();
            } catch (Exception e) {
                Debugger.Log(e);
            }

            Debugger.Log("Extension framework initialized");
        }

#if WIN64
        [DllExport("RVExtensionVersion")]
#else
        [DllExport("_RVExtensionVersion@8", CallingConvention.StdCall)]
#endif
        // ReSharper disable once InconsistentNaming
        // ReSharper disable once UnusedMember.Global
        // ReSharper disable once UnusedParameter.Global
#pragma warning disable IDE0060 // Remove unused parameter
        public static void RVExtensionVersion(StringBuilder output, int outputSize) {
#pragma warning restore IDE0060 // Remove unused parameter
            output.Append(GetVersion());
        }

#if WIN64
        [DllExport("RVExtension")]
#else
        [DllExport("_RVExtension@12", CallingConvention.StdCall)]
#endif
        // ReSharper disable once InconsistentNaming
        // ReSharper disable once UnusedMember.Global
        public static void RVExtension(StringBuilder output, int outputSize, [MarshalAs(UnmanagedType.LPStr)] string input)
        {
            outputSize--;

            switch (input) {
                case "":
                    return;
                case "debugger":
                    Debugger.Toggle();
                    return;
                case "version":
                    output.Append(GetVersion());
                    break;
                default:
                    switch (input[0])
                    {
                        case ControlCharacter.ACK:
                            output.Append(_outputBuffer);
                            break;
                        case ControlCharacter.ENQ:
                            if (Tasks.Count == 0)
                                break;

                            try
                            {
                                var completedTasksIndices = new List<int>();
                                foreach (var taskEntry in Tasks)
                                {
                                    if (!taskEntry.Value.IsCompleted)
                                        continue;

                                    try
                                    {
                                        output.Append(ControlCharacter.SOH + taskEntry.Key.ToString() + ControlCharacter.STX + taskEntry.Value.Result);
                                    } catch (Exception e) {
                                        output.Append(ControlCharacter.SOH + taskEntry.Key.ToString() + ControlCharacter.STX);
                                        if (e is AggregateException exception) {
                                            var outputTmp = output;
                                            exception.Handle(x => {
                                                outputTmp.Append(x.Message + "\n");
                                                return true;
                                            });
                                        } else {
                                            output.Append(e.Message);
                                        }
                                    }
                                    Debugger.Log("Task result: " + taskEntry.Key);
                                    completedTasksIndices.Add(taskEntry.Key);
                                }

                                foreach (var index in completedTasksIndices) {
                                    Tasks.Remove(index);
                                }

                                output.Append(ControlCharacter.EOT);
                            } catch (Exception e) {
                                Debugger.Log(e);
                                output.Append(e.Message);
                            }
                            break;
                        default:
                            try {
                                _inputBuffer += input;

                                if (_inputBuffer[_inputBuffer.Length - 1] == ControlCharacter.ETX)
                                    output.Append(ExecuteRequest(ArmaRequest.Parse(_inputBuffer)));
                                else
                                    output.Append(ControlCharacter.ACK);
                            } catch (Exception e) {
                                output.Append(e.Message);
                            }
                            break;
                    }
                    break;
            }

            var outputBytes = Encoding.Default.GetBytes(output.ToString()).ToList();
            if (outputBytes.Count <= outputSize)
                return;

            var outputSplitPosition = outputSize;
            while ((outputBytes[outputSplitPosition - 1] & 0xC0) == 0x80) {
                outputSplitPosition--;
            }

            _outputBuffer = Encoding.Default.GetString(outputBytes.GetRange(outputSplitPosition, outputBytes.Count - outputSplitPosition).ToArray());
            output.Remove(output.Length - _outputBuffer.Length, _outputBuffer.Length);
        }

        private static string GetVersion() {
            var executingAssembly = Assembly.GetExecutingAssembly();
            try {
                var location = executingAssembly.Location;
                return FileVersionInfo.GetVersionInfo(location).FileVersion;
            } catch (Exception) {
                // ignored
            }

            return "0.0.0.0";
        }

        private delegate string CLibFuncDelegate(string input);
        private static string ExecuteRequest(ArmaRequest request) {
            _inputBuffer = "";

            if (!AvailableExtensions.ContainsKey(request.ExtensionName))
                throw new ArgumentException($"Extension is not valid: {request.ExtensionName}");

            var function = FunctionLoader.LoadFunction<CLibFuncDelegate>(AvailableExtensions[request.ExtensionName], request.ActionName);

            if (request.TaskId == -1) {
                return ControlCharacter.STX + function(request.Data) + ControlCharacter.EOT;
            }

            var task = Task.Run(() => function(request.Data));
            if (Tasks.ContainsKey(request.TaskId))
                Tasks.Remove(request.TaskId);
            Tasks.Add(request.TaskId, task);
            return ControlCharacter.ACK.ToString();
        }

        private static void DetectExtensions() {
            Debugger.Log($"Current directory is: {Environment.CurrentDirectory}");
            Debugger.Log("Extensions Found:");
            var startParameters = Environment.GetCommandLineArgs();
            foreach (var startParameter in startParameters) {
                // Arma 3 allows start parameters separated by new lines instead of spaces
                foreach (var realStartParameter in startParameter.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)) {
                    var match = Regex.Match(realStartParameter, "^-(?:server)?mod=(.*)", RegexOptions.IgnoreCase);
                    if (!match.Success)
                        continue;

                    foreach (var path in match.Groups[1].Value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)) {
                        var fullPath = path;
                        if (!Path.IsPathRooted(fullPath))
                            fullPath = Path.Combine(Environment.CurrentDirectory, path);

#if WIN64
                        var extensionPaths = Directory.GetFiles(fullPath, "*_x64.dll", SearchOption.AllDirectories);
#else
                        var extensionPaths = Directory.GetFiles(fullPath, "*.dll", SearchOption.AllDirectories);
#endif
                        foreach (var extensionPath in extensionPaths) {
                            try {
                                var exports = FunctionLoader.ExportTable(extensionPath);
#if WIN64
                                if (!exports.Contains("RVExtension"))
#else
                                if (!exports.Contains("_RVExtension@12"))
#endif
                                    continue;

                                var filename = Path.GetFileNameWithoutExtension(extensionPath);
                                if (filename == null)
                                    continue;

#if WIN64
                                filename = filename.Substring(0, filename.Length - 4);
#endif

                                if (AvailableExtensions.ContainsKey(filename)) {
                                    Debugger.Log($"Duplicate: {filename} at: {extensionPath}");
                                } else {
                                    AvailableExtensions.Add(filename, extensionPath);
                                    Debugger.Log($"Added: {filename} at: {extensionPath}");
                                }

                            } catch (Win32Exception e) {
                                // Trying to load an x64 dll within an x86 process fails with an error with no native error code. We can ignore that.
                                if (e.NativeErrorCode != 0)
                                    Debugger.Log(e);
                            } catch (Exception e) {
                                Debugger.Log(e);
                            }
                        }
                    }
                }
            }
        }
    }
}