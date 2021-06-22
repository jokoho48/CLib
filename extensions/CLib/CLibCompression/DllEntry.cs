using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CLibCompression {
    // ReSharper disable once UnusedMember.Global
    public class DllEntry {
        private const int WindowSize = 1 << 11;
        private const int MinMatchLength = 2;
        private const uint MaxMatchLength = (1 << 4) - MinMatchLength;

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
        // ReSharper disable once UnusedParameter.Global
#pragma warning disable IDE0060 // Remove unused parameter
        public static void RVExtension(StringBuilder output, int outputSize, [MarshalAs(UnmanagedType.LPStr)] string input) {
#pragma warning restore IDE0060 // Remove unused parameter
            if (input != "version")
                return;

            output.Append(GetVersion());
        }

        private static string GetVersion() {
            var executingAssembly = Assembly.GetExecutingAssembly();
            try
            {
                var location = executingAssembly.Location;
                return FileVersionInfo.GetVersionInfo(location).FileVersion;
            } catch (Exception) {
                // ignored
            }

            return "0.0.0.0";
        }

        [DllExport("Compress")]
        // ReSharper disable once UnusedMember.Global
        public static string Compress(string input) {
            var output = new StringBuilder();
            var writeBuffer = new List<char>();
            var symbolsWritten = 0;
            var encodeFlag = 1;

            output.Append(input.Substring(0, MinMatchLength));

            for (var inputPosition = MinMatchLength; inputPosition < input.Length; inputPosition++) {
                var currentChar = input[inputPosition];

                var searchSteps = Math.Min(WindowSize, inputPosition);
                var bestMatchLength = 0;
                var bestMatchOffset = 0;

                for (var windowPosition = 1; windowPosition <= searchSteps; windowPosition++) {
                    if (currentChar != input[inputPosition - windowPosition])
                        continue;

                    var currentMatchLength = 1;
                    while (inputPosition + currentMatchLength < input.Length && input[inputPosition - searchSteps + (searchSteps - windowPosition + currentMatchLength) % searchSteps] == input[inputPosition + currentMatchLength] && currentMatchLength < MaxMatchLength) {
                        currentMatchLength++;
                    }

                    if (currentMatchLength <= bestMatchLength)
                        continue;

                    bestMatchLength = currentMatchLength;
                    bestMatchOffset = windowPosition;
                }

                symbolsWritten++;

                if (bestMatchLength > MinMatchLength) {
                    inputPosition += bestMatchLength - 1;
                    encodeFlag |= 1 << symbolsWritten;
                    writeBuffer.Add((char)(((bestMatchOffset >> 4) << 1) | 0x1));
                    writeBuffer.Add((char)(((bestMatchOffset & 0xF) << 4) | (bestMatchLength - MinMatchLength)));
                } else {
                    writeBuffer.Add(currentChar);
                }

                if (symbolsWritten < 7)
                    continue;

                output.Append((char)encodeFlag);
                output.Append(writeBuffer.ToArray());

                encodeFlag = 1;
                symbolsWritten = 0;
                writeBuffer.Clear();
            }

            if (writeBuffer.Count > 0) {
                output.Append((char)encodeFlag);
                output.Append(writeBuffer.ToArray());
            }

            return Encoding.Default.GetString(Encoding.Convert(Encoding.Unicode, Encoding.UTF8, Encoding.Unicode.GetBytes(output.ToString())));
        }
    }
}
