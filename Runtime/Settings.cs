// Settings.cs — typed access to StreamingAssets/settings.json (flat name -> number map).
// Usage: AppController.Instance.Settings.GetInt("timeOutLength", 30);

using System.IO;
using UnityEngine;

namespace SpotCMS
{
    public class Settings
    {
        private readonly JSON _json;

        private Settings(JSON json) { _json = json; }

        public static Settings Load(string path)
        {
            try
            {
                var json = File.Exists(path) ? JSON.Parse(File.ReadAllText(path)) : new JSON(null);
                return new Settings(json);
            }
            catch (System.Exception e)
            {
                Debug.LogError("[CMS] Failed to read settings.json: " + e.Message);
                return new Settings(new JSON(null));
            }
        }

        public int GetInt(string key, int fallback = 0) => _json.getInt(key, fallback);
        public float GetFloat(string key, float fallback = 0f) => _json.getFloat(key, fallback);

        public string GetString(string key, string fallback = "")
        {
            var v = _json.getString(key);
            return string.IsNullOrEmpty(v) ? fallback : v;
        }

        public bool Has(string key) => _json.Has(key);
    }
}
