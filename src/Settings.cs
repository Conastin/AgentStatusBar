using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AgentStatusBar
{
    /// <summary>极简 INI 配置：%APPDATA%\AgentStatusBar\config.ini</summary>
    static class Settings
    {
        static readonly Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AgentStatusBar", "config.ini");

        static Settings()
        {
            try
            {
                if (File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string l = lines[i];
                        if (l == null || l.Trim().Length == 0 || l.StartsWith(";")) continue;
                        int eq = l.IndexOf('=');
                        if (eq <= 0) continue;
                        map[l.Substring(0, eq).Trim()] = l.Substring(eq + 1).Trim();
                    }
                }
            }
            catch { }
        }

        public static string Get(string key, string def)
        {
            string v;
            return map.TryGetValue(key, out v) ? v : def;
        }

        public static int GetInt(string key, int def)
        {
            int v;
            return Int32.TryParse(Get(key, null), out v) ? v : def;
        }

        public static bool GetBool(string key, bool def)
        {
            bool b;
            return Boolean.TryParse(Get(key, null), out b) ? b : def;
        }

        public static void Set(string key, string value)
        {
            map[key] = value;
            Save();
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                StringBuilder sb = new StringBuilder();
                foreach (KeyValuePair<string, string> kv in map)
                    sb.Append(kv.Key).Append('=').Append(kv.Value).Append("\r\n");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            }
            catch { }
        }
    }
}
