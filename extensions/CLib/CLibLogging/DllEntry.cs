using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace CLibLogging {
    // ReSharper disable once UnusedMember.Global
    public class DllEntry {
        private static readonly string StartTime;

        static DllEntry() {
            StartTime = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
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
        // ReSharper disable once UnusedParameter.Global
#pragma warning disable IDE0060 // Remove unused parameter
        public static void RVExtension(StringBuilder output, int outputSize, [MarshalAs(UnmanagedType.LPStr)] string input) {
#pragma warning restore IDE0060 // Remove unused parameter
            if (input.ToLower() != "version")
                return;

            output.Append(GetVersion());
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

        [DllExport("Log")]
        // ReSharper disable once UnusedMember.Global
        public static string Log(string input) {
            var inputParts = input.Split(new[] { ':' }, 2);

            var path = Path.Combine(Environment.CurrentDirectory, "CLib_Logs", StartTime.Replace("-", ""));
            if (!File.Exists(path))
                Directory.CreateDirectory(path);

            // TODO let the user define the File format
            using (var file = new StreamWriter(path + $"\\CLibLog_{StartTime}_{inputParts[0]}.log", true)) {
                var log = DateTime.Now.ToString("HH-mm-ss") + inputParts[1];
                file.WriteLine(log);
            }
            return "";
        }
    }
}
