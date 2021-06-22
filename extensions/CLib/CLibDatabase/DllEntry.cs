using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using SimpleJSON;

namespace CLibDatabase {
    // ReSharper disable once UnusedMember.Global
    public class DllEntry {
        private static string _loadedDatabaseFilename = "";
        private static string _databaseFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CLibDatabase");
        private static Dictionary<string, string> _database = new Dictionary<string, string>();

        static DllEntry() {
            if (!Directory.Exists(_databaseFolder))
                Directory.CreateDirectory(_databaseFolder);
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
        [DllExport("SetExportPath")]
        public static string SetExportPath(string path) {
            _databaseFolder = path;
            if (!Directory.Exists(_databaseFolder))
                Directory.CreateDirectory(_databaseFolder);
            return "true";
        }

        [DllExport("KeyExists")]
        // ReSharper disable once UnusedMember.Global
        public static string KeyExists(string key) {
            return _database.ContainsKey(key).ToString();
        }

        [DllExport("Get")]
        // ReSharper disable once UnusedMember.Global
        public static string Get(string key) {
            return !_database.ContainsKey(key) ? "ERROR" : _database[key];
        }

        [DllExport("Set")]
        // ReSharper disable once UnusedMember.Global
        public static string Set(string input) {
            var keyAndValue = input.Split(new[] { "~>" }, StringSplitOptions.RemoveEmptyEntries);
            if (!_database.ContainsKey(keyAndValue[0]))
                _database.Add(keyAndValue[0], keyAndValue[1]);
            else
                _database[keyAndValue[0]] = keyAndValue[1];

            return "true";
        }

        [DllExport("Load")]
        // ReSharper disable once UnusedMember.Global
        public static string Load(string filename) {
            if (_loadedDatabaseFilename == filename)
                return "true";

            using (var fs = File.OpenRead(Path.Combine(_databaseFolder, filename + ".clibdata")))
            using (var gZipStream = new GZipStream(fs, CompressionMode.Decompress))
            using (var reader = new BinaryReader(gZipStream)) {
                var count = reader.ReadInt32();
                _database = new Dictionary<string, string>(count);
                for (var i = 0; i < count; i++) {
                    var key = reader.ReadString();
                    var value = reader.ReadString();
                    _database.Add(key, value);
                }
            }

            _loadedDatabaseFilename = filename;
            return "true";
        }

        [DllExport("Save")]
        // ReSharper disable once UnusedMember.Global
        // ReSharper disable once MemberCanBePrivate.Global
        // ReSharper disable once UnusedMethodReturnValue.Global
        public static string Save(string filename) {
            using (var fs = File.OpenWrite(Path.Combine(_databaseFolder, filename + ".clibdata")))
            using (var gZipStream = new GZipStream(fs, CompressionMode.Compress))
            using (var writer = new BinaryWriter(gZipStream)) {
                writer.Write(_database.Count);
                foreach (var pair in _database) {
                    writer.Write(pair.Key);
                    writer.Write(pair.Value);
                }
                return "true";
            }
        }

        private static JSONNode ConvertToJSON() {
            var json = new JSONObject();
            foreach (var item in _database)
                json.Add(item.Key, item.Value);
            return json;
        }

        private static void ConvertToDictionary(JSONNode json) {
            _database.Clear();
            foreach (var item in json.Linq)
                _database.Add(item.Key, item.Value.Value);
        }

        private static void ConvertToDictionary(XContainer xml) {
            _database.Clear();
            foreach (XElement item in xml.Elements()) {
                _database.Add(item.Name.LocalName, item.Value);
            }
        }
        private static XDocument ConvertToXML() {
            XDocument xml = new XDocument();
            foreach (KeyValuePair<string, string> item in _database) {
                xml.Add(item.Key, new JSONString(item.Value));
            }
            return xml;
        }

        #region Import/Export
        [DllExport("ExportJSON")]
        // ReSharper disable once UnusedMember.Global
        public static string ExportJSON(string filename) {
            var json = ConvertToJSON();
            var exportStringBuilder = new StringBuilder();
            json.WriteToStringBuilder(exportStringBuilder, 0, 4, JSONTextMode.Indent);
            File.WriteAllText(Path.Combine(_databaseFolder, filename + ".json"), exportStringBuilder.ToString());
            return "true";
        }

        [DllExport("ExportJSONBinary")]
        // ReSharper disable once UnusedMember.Global
        public static string ExportJsonBinary(string filename) {
            var json = ConvertToJSON();
            json.SaveToCompressedFile(Path.Combine(_databaseFolder, filename + ".bson"));
            return "true";
        }

        [DllExport("ExportXML")]
        public static string ExportXML(string filePath) {
            XDocument xml = ConvertToXML();
            xml.Save(filePath, SaveOptions.OmitDuplicateNamespaces);
            return "true";
        }

        [DllExport("ImportJSON")]
        // ReSharper disable once UnusedMember.Global
        public static string ImportJSON(string filename) {
            var jsonStr = File.ReadAllText(Path.Combine(_databaseFolder, filename + ".json"));
            var json = JSON.Parse(jsonStr);
            ConvertToDictionary(json);
            return "true";
        }

        [DllExport("ImportJSONBinary")]
        // ReSharper disable once UnusedMember.Global
        public static string ImportJSONBinary(string filename) {
            var json = JSONNode.LoadFromCompressedFile(Path.Combine(_databaseFolder, filename + ".bson"));
            ConvertToDictionary(json);
            return "true";
        }

        [DllExport("ImportXML")]
        public static string ImportXML(string filePath) {
            XDocument xml = XDocument.Load(filePath);
            ConvertToDictionary(xml);
            return "true";
        }
        #endregion Import/Export

        ~DllEntry() {
            Save(_loadedDatabaseFilename);
        }
    }
}
