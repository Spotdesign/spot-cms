// appAdmin.cs — Spot CMS component. Drop it on the SAME GameObject as your existing
// AppController. It owns the localised text + settings, loads content from persistentDataPath
// (seeded/merged from StreamingAssets by ContentBootstrap), reads languages from schema.json,
// and serves strings to textNode. Portable: nothing else in your project needs to reference it.
//
// Usage:
//   appAdmin.instance.SetLang("cy");                       // switch language
//   appAdmin.instance.CycleLanguage();                     // next language in schema order
//   appAdmin.instance.Settings.GetInt("timeOutLength", 30);
//   appAdmin.instance.Settings.GetFloat("fadeSpeed", 0.5f);

using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SpotCMS
{
    public struct LanguageInfo
    {
        public string code;
        public string name;
        public bool rtl;
    }

    public class appAdmin : MonoBehaviour
    {
        public static appAdmin instance { get; private set; }

        [Tooltip("Optional. Overrides schema.json's default language on start (e.g. en / cy / ar).")]
        public string startLanguageOverride = "";

        [Tooltip("Show [Page-Field] in place of missing text (handy while authoring).")]
        public bool showKeysWhenMissing = false;

        public string CurrentLang { get; private set; } = "en";
        public string DefaultLang { get; private set; } = "en";
        public List<LanguageInfo> Languages { get; private set; } = new List<LanguageInfo>();
        public Settings Settings { get; private set; }

        public readonly Dictionary<string, textNode> allText = new Dictionary<string, textNode>();
        private readonly List<textNode> _nodes = new List<textNode>();

        private JSON _root;

        void Awake()
        {
            // Shares a GameObject with AppController, so only remove the duplicate COMPONENT.
            if (instance != null && instance != this) { Destroy(this); return; }
            instance = this;

            var dataPath = ContentBootstrap.EnsureData();
            var settingsPath = ContentBootstrap.EnsureSettings();
            var schemaPath = ContentBootstrap.EnsureSchema();

            LoadSchema(schemaPath);
            _root = ParseFile(dataPath);
            Settings = SpotCMS.Settings.Load(settingsPath);

            CurrentLang = string.IsNullOrEmpty(startLanguageOverride) ? DefaultLang : startLanguageOverride;
        }

        void LoadSchema(string path)
        {
            Languages.Clear();

            // schema.json: { "defaultLanguage": "en", "languages": [ { code, name, rtl }, ... ] }
            var raw = Json.Deserialize(SafeRead(path)) as Dictionary<string, object>;
            DefaultLang = raw != null && raw.TryGetValue("defaultLanguage", out var dl) ? dl?.ToString() : "";

            if (raw != null && raw.TryGetValue("languages", out var lo) && lo is List<object> arr)
            {
                foreach (var item in arr)
                {
                    if (item is Dictionary<string, object> d)
                    {
                        var li = new LanguageInfo
                        {
                            code = d.TryGetValue("code", out var c) ? c?.ToString() : "",
                            name = d.TryGetValue("name", out var n) ? n?.ToString() : "",
                            rtl = d.TryGetValue("rtl", out var r) && r is bool b && b,
                        };
                        if (!string.IsNullOrEmpty(li.code)) Languages.Add(li);
                    }
                }
            }

            if (Languages.Count == 0)
                Languages.Add(new LanguageInfo { code = "en", name = "English", rtl = false });
            if (string.IsNullOrEmpty(DefaultLang)) DefaultLang = Languages[0].code;
        }

        private static string SafeRead(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : "{}"; }
            catch { return "{}"; }
        }

        private static JSON ParseFile(string path) => JSON.Parse(SafeRead(path));

        public void Register(textNode node)
        {
            if (node == null) return;
            if (!node.IsCopy) allText[node.Key] = node;   // copies mirror a master; no key of their own
            if (!_nodes.Contains(node)) _nodes.Add(node); // still tracked so it refreshes on language change
        }

        public void Unregister(textNode node)
        {
            if (node == null) return;
            _nodes.Remove(node);
            if (allText.TryGetValue(node.Key, out var existing) && existing == node)
                allText.Remove(node.Key);
        }

        /// <summary>Resolve "Page-Field" to the string for the current language.</summary>
        public string getText(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            string[] cats = key.Split('-');
            if (cats.Length < 2) return showKeysWhenMissing ? "[" + key + "]" : "";

            JSON node = _root.getJSON(cats[0]).getJSON(cats[1]);
            string value = node.getString(CurrentLang);

            if (string.IsNullOrEmpty(value) && CurrentLang != DefaultLang)
                value = node.getString(DefaultLang);

            if (string.IsNullOrEmpty(value) && showKeysWhenMissing)
                return "[" + key + "]";

            return value;
        }

        public bool IsCurrentRtl()
        {
            foreach (var l in Languages) if (l.code == CurrentLang) return l.rtl;
            return false;
        }

        /// <summary>Switch language and refresh every registered textNode.</summary>
        public void SetLang(string lang)
        {
            CurrentLang = lang;
            for (int i = 0; i < _nodes.Count; i++)
                if (_nodes[i] != null) _nodes[i].Refresh();
        }

        /// <summary>Advance to the next language in schema order (handy for a single toggle button).</summary>
        public void CycleLanguage()
        {
            if (Languages.Count == 0) return;
            int idx = Languages.FindIndex(l => l.code == CurrentLang);
            idx = (idx + 1) % Languages.Count;
            SetLang(Languages[idx].code);
        }
    }
}
