using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AntigravityPersian
{
    /// <summary>
    /// Lightweight, zero-dependency Electron ASAR archive extractor and repacker.
    /// Uses .NET built-in JavaScriptSerializer.
    /// </summary>
    public static class AsarHelper
    {
        public static void ExtractAll(string asarPath, string outDir)
        {
            using (var fs = new FileStream(asarPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                br.ReadUInt32();
                br.ReadUInt32();
                br.ReadUInt32();
                uint headerSize = br.ReadUInt32();
                byte[] headerBytes = br.ReadBytes((int)headerSize);
                string json = Encoding.UTF8.GetString(headerBytes);

                var jss = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = jss.Deserialize<Dictionary<string, object>>(json);
                long baseOffset = 16 + headerSize;

                ExtractDict((Dictionary<string, object>)root["files"], fs, baseOffset, outDir);
            }
        }

        private static void ExtractDict(Dictionary<string, object> files, FileStream fs, long baseOffset, string currentDir)
        {
            if (!Directory.Exists(currentDir)) Directory.CreateDirectory(currentDir);

            foreach (var kvp in files)
            {
                string name = kvp.Key;
                var info = kvp.Value as Dictionary<string, object>;
                if (info == null) continue;

                string fullPath = Path.Combine(currentDir, name);

                if (info.ContainsKey("files"))
                {
                    ExtractDict((Dictionary<string, object>)info["files"], fs, baseOffset, fullPath);
                }
                else if (info.ContainsKey("size"))
                {
                    if (info.ContainsKey("unpacked") && Convert.ToBoolean(info["unpacked"]))
                    {
                        continue;
                    }

                    long size = Convert.ToInt64(info["size"]);
                    long offset = Convert.ToInt64(info["offset"]);

                    string dir = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    fs.Seek(baseOffset + offset, SeekOrigin.Begin);
                    using (var outFs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buffer = new byte[Math.Min(65536, size)];
                        long remaining = size;
                        while (remaining > 0)
                        {
                            int read = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                            if (read <= 0) break;
                            outFs.Write(buffer, 0, read);
                            remaining -= read;
                        }
                    }
                }
            }
        }

        public static void PackAll(string srcDir, string asarPath)
        {
            var filesList = new List<Tuple<string, Dictionary<string, object>>>();
            var rootFiles = BuildTree(srcDir, filesList);

            long curOffset = 0;
            foreach (var item in filesList)
            {
                item.Item2["offset"] = curOffset.ToString();
                curOffset += Convert.ToInt64(item.Item2["size"]);
            }

            var rootObj = new Dictionary<string, object> { { "files", rootFiles } };
            var jss = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string json = jss.Serialize(rootObj);
            byte[] headerBytes = Encoding.UTF8.GetBytes(json);
            uint headerSize = (uint)headerBytes.Length;

            using (var outFs = new FileStream(asarPath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(outFs))
            {
                bw.Write((uint)4);
                bw.Write((uint)(headerSize + 8));
                bw.Write((uint)(headerSize + 4));
                bw.Write((uint)(headerSize));
                bw.Write(headerBytes);

                byte[] buffer = new byte[65536];
                foreach (var item in filesList)
                {
                    long size = Convert.ToInt64(item.Item2["size"]);
                    if (size == 0) continue;

                    using (var inFs = new FileStream(item.Item1, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        int read;
                        while ((read = inFs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            bw.Write(buffer, 0, read);
                        }
                    }
                }
            }
        }

        private static Dictionary<string, object> BuildTree(string dir, List<Tuple<string, Dictionary<string, object>>> filesList)
        {
            var res = new Dictionary<string, object>();
            var dirInfo = new DirectoryInfo(dir);

            var dirs = new List<DirectoryInfo>(dirInfo.GetDirectories());
            dirs.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            var files = new List<FileInfo>(dirInfo.GetFiles());
            files.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            foreach (var sub in dirs)
            {
                var subFiles = BuildTree(sub.FullName, filesList);
                res[sub.Name] = new Dictionary<string, object> { { "files", subFiles } };
            }

            foreach (var f in files)
            {
                var fEntry = new Dictionary<string, object>
                {
                    { "size", f.Length },
                    { "offset", "0" }
                };
                res[f.Name] = fEntry;
                filesList.Add(Tuple.Create(f.FullName, fEntry));
            }

            return res;
        }

        public static bool CheckIfEngineInstalled(string asarPath)
        {
            try
            {
                if (!File.Exists(asarPath)) return false;
                using (var fs = new FileStream(asarPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var br = new BinaryReader(fs))
                {
                    br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32();
                    uint headerSize = br.ReadUInt32();
                    byte[] headerBytes = br.ReadBytes((int)headerSize);
                    string json = Encoding.UTF8.GetString(headerBytes);

                    var jss = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var root = jss.Deserialize<Dictionary<string, object>>(json);
                    var files = (Dictionary<string, object>)root["files"];
                    if (!files.ContainsKey("dist")) return false;
                    var dist = (Dictionary<string, object>)((Dictionary<string, object>)files["dist"])["files"];
                    if (!dist.ContainsKey("preload.js")) return false;

                    var preloadInfo = (Dictionary<string, object>)dist["preload.js"];
                    long size = Convert.ToInt64(preloadInfo["size"]);
                    long offset = Convert.ToInt64(preloadInfo["offset"]);
                    long baseOffset = 16 + headerSize;

                    fs.Seek(baseOffset + offset, SeekOrigin.Begin);
                    byte[] pBytes = new byte[size];
                    fs.Read(pBytes, 0, (int)size);
                    string content = Encoding.UTF8.GetString(pBytes);

                    return content.Contains("ANTIGRAVITY-PERSIAN-RTL-ENGINE");
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
