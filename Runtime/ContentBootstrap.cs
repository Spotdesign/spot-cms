// ContentBootstrap.cs — seeds and maintains the LIVE content in persistentDataPath.
//
// StreamingAssets holds the DEFAULTS shipped in the build (overwritten by updates).
// persistentDataPath holds the LIVE editable copy (never touched by app-binary updates).
//
// Sync modes (data.json / settings.json), decided by content.meta.json's contentVersion:
//   * First run   -> copy defaults -> live.
//   * Authoritative (release wins) -> when the build's contentVersion is higher than the value
//     this machine last applied, default values are overlaid onto live (existing values
//     overwritten), while keys that exist only in live (venue-added) are kept. This is how
//     central text edits reach already-deployed machines.
//   * Additive    -> otherwise, only genuinely new keys are added; existing values untouched.
// schema.json always uses an additive language union.
//
// NOTE: reads StreamingAssets with File.ReadAllText (correct for Windows/macOS standalone).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace SpotCMS
{
    public static class ContentBootstrap
    {
        private static string DefaultsDir => Application.streamingAssetsPath;
        private static string LiveDir => Application.persistentDataPath;
        private static string MarkerPath => Path.Combine(LiveDir, ".cms_seed.json");

        // contentReplace (content.meta.json) => on a contentVersion bump data.json is replaced by the
        // build's copy verbatim (translations included) instead of the structural merge.
        public static string EnsureData()     => SyncVersioned("data.json", "data", !ContentReplace(), ContentVersion());
        public static string EnsureSettings() => SyncVersioned("settings.json", "settings", false, SettingsVersion());
        public static string EnsureSchema()   => SyncUnion("schema.json", "schema");

        // ---- versioned files: on a contentVersion bump, data does a structural merge (match the
        // build's structure/order + default language, keep entered translations); settings match
        // the build exactly. Otherwise both are additive. ----
        private static string SyncVersioned(string fileName, string kind, bool structural, int version)
        {
            Directory.CreateDirectory(LiveDir);
            var livePath = Path.Combine(LiveDir, fileName);
            var defPath = Path.Combine(DefaultsDir, fileName);
            var defText = LoadText(defPath);
            var hash = Hash(defText);
            var marker = LoadMarker();
            // Migrate legacy markers once: older builds stored the content version in the settings
            // slot, so drop it and let the build's settingsVersion apply fresh.
            if (!marker.ContainsKey("markerFormat")) { marker.Remove("settingsVersion"); marker["markerFormat"] = "2"; }
            var applied = marker.TryGetValue(kind + "Version", out var av) &&
                          int.TryParse(av, out var pv) ? pv : -1;

            if (!File.Exists(livePath))
            {
                File.WriteAllText(livePath, string.IsNullOrEmpty(defText) ? "{}" : defText);
                marker[kind] = hash;
                marker[kind + "Version"] = version.ToString(CultureInfo.InvariantCulture);
                SaveMarker(marker);
                return livePath;
            }

            if (version > applied && !string.IsNullOrEmpty(defText))
            {
                if (structural)
                {
                    var build = AsDict(Json.Deserialize(defText));
                    var live = AsDict(Json.Deserialize(LoadText(livePath)));
                    File.WriteAllText(livePath, Json.Serialize(StructuralMergeData(build, live, DefaultLanguage())));
                }
                else
                {
                    File.WriteAllText(livePath, defText); // settings: straight match to build
                }
                marker[kind] = hash;
                marker[kind + "Version"] = version.ToString(CultureInfo.InvariantCulture);
                SaveMarker(marker);
                return livePath;
            }

            if (marker.TryGetValue(kind, out var h) && h == hash) return livePath;

            if (!string.IsNullOrEmpty(defText))
            {
                var live = AsDict(Json.Deserialize(LoadText(livePath)));
                var def = AsDict(Json.Deserialize(defText));
                MergeAddMissing(live, def);
                File.WriteAllText(livePath, Json.Serialize(live));
            }
            marker[kind] = hash;
            SaveMarker(marker);
            return livePath;
        }

        // ---- schema: additive language union, hash-gated ----
        private static string SyncUnion(string fileName, string kind)
        {
            Directory.CreateDirectory(LiveDir);
            var livePath = Path.Combine(LiveDir, fileName);
            var defPath = Path.Combine(DefaultsDir, fileName);
            var defText = LoadText(defPath);
            var hash = Hash(defText);
            var marker = LoadMarker();

            if (!File.Exists(livePath))
            {
                File.WriteAllText(livePath, string.IsNullOrEmpty(defText) ? "{}" : defText);
                marker[kind] = hash;
                SaveMarker(marker);
                return livePath;
            }
            if (marker.TryGetValue(kind, out var h) && h == hash) return livePath;
            if (!string.IsNullOrEmpty(defText))
            {
                var live = AsDict(Json.Deserialize(LoadText(livePath)));
                var def = AsDict(Json.Deserialize(defText));
                MergeSchema(live, def);
                File.WriteAllText(livePath, Json.Serialize(live));
            }
            marker[kind] = hash;
            SaveMarker(marker);
            return livePath;
        }

        // Add keys missing from live; recurse into nested objects. Never overwrites.
        private static void MergeAddMissing(Dictionary<string, object> live, Dictionary<string, object> def)
        {
            foreach (var kv in def)
            {
                if (!live.ContainsKey(kv.Key)) live[kv.Key] = kv.Value;
                else if (live[kv.Key] is Dictionary<string, object> ld && kv.Value is Dictionary<string, object> dd)
                    MergeAddMissing(ld, dd);
            }
        }

        // Rebuild data to match the build's pages/fields and order (new added, unused dropped),
        // taking the default language from the build but keeping entered translations.
        private static Dictionary<string, object> StructuralMergeData(
            Dictionary<string, object> build, Dictionary<string, object> live, string defaultLang)
        {
            var result = new Dictionary<string, object>();
            foreach (var pageKv in build)
            {
                if (!(pageKv.Value is Dictionary<string, object> buildPage)) { result[pageKv.Key] = pageKv.Value; continue; }
                var livePage = live.TryGetValue(pageKv.Key, out var lp) ? lp as Dictionary<string, object> : null;
                var newPage = new Dictionary<string, object>();

                foreach (var fieldKv in buildPage)
                {
                    if (!(fieldKv.Value is Dictionary<string, object> buildField)) { newPage[fieldKv.Key] = fieldKv.Value; continue; }
                    var liveField = livePage != null && livePage.TryGetValue(fieldKv.Key, out var lf) ? lf as Dictionary<string, object> : null;
                    var newField = new Dictionary<string, object>();

                    // Default language from the build (authoritative source text).
                    newField[defaultLang] = buildField.TryGetValue(defaultLang, out var bdef) ? bdef
                        : (liveField != null && liveField.TryGetValue(defaultLang, out var ldef) ? ldef : "");

                    // Other languages: keep locally-entered translation, else whatever the build shipped.
                    void Add(Dictionary<string, object> src)
                    {
                        if (src == null) return;
                        foreach (var l in src)
                        {
                            if (l.Key == defaultLang || newField.ContainsKey(l.Key)) continue;
                            newField[l.Key] = (liveField != null && liveField.TryGetValue(l.Key, out var lv)) ? lv
                                : (buildField.TryGetValue(l.Key, out var bv) ? bv : "");
                        }
                    }
                    Add(buildField);
                    Add(liveField);

                    newPage[fieldKv.Key] = newField;
                }
                result[pageKv.Key] = newPage;
            }
            return result;
        }

        private static string DefaultLanguage()
        {
            try
            {
                var path = Path.Combine(DefaultsDir, "schema.json");
                if (File.Exists(path) &&
                    Json.Deserialize(File.ReadAllText(path)) is Dictionary<string, object> root &&
                    root.TryGetValue("defaultLanguage", out var dl) && dl != null)
                    return dl.ToString();
            }
            catch { }
            return "en";
        }

        // Union the languages array by code; keep live defaultLanguage.
        private static void MergeSchema(Dictionary<string, object> live, Dictionary<string, object> def)
        {
            if (!(live.TryGetValue("languages", out var lo) && lo is List<object> liveLangs))
            {
                liveLangs = new List<object>();
                live["languages"] = liveLangs;
            }
            if (def.TryGetValue("languages", out var doo) && doo is List<object> defLangs)
            {
                var have = new HashSet<string>();
                foreach (var o in liveLangs)
                    if (o is Dictionary<string, object> d && d.TryGetValue("code", out var c) && c != null)
                        have.Add(c.ToString());
                foreach (var o in defLangs)
                    if (o is Dictionary<string, object> d && d.TryGetValue("code", out var c) && c != null && !have.Contains(c.ToString()))
                        liveLangs.Add(o);
            }
            if (!live.ContainsKey("defaultLanguage") && def.TryGetValue("defaultLanguage", out var dv))
                live["defaultLanguage"] = dv;
        }

        private static int ContentVersion()  => MetaInt("contentVersion");
        private static int SettingsVersion() => MetaInt("settingsVersion");

        private static bool ContentReplace()
        {
            try
            {
                var path = Path.Combine(DefaultsDir, "content.meta.json");
                if (File.Exists(path) &&
                    Json.Deserialize(File.ReadAllText(path)) is Dictionary<string, object> d &&
                    d.TryGetValue("contentReplace", out var v) && v != null)
                {
                    if (v is bool b) return b;
                    return string.Equals(v.ToString(), "true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch { }
            return false;
        }

        private static int MetaInt(string key)
        {
            try
            {
                var path = Path.Combine(DefaultsDir, "content.meta.json");
                if (File.Exists(path) &&
                    Json.Deserialize(File.ReadAllText(path)) is Dictionary<string, object> d &&
                    d.TryGetValue(key, out var v) && v != null)
                    return Convert.ToInt32(v, CultureInfo.InvariantCulture);
            }
            catch { }
            return 0;
        }

        private static Dictionary<string, object> AsDict(object o) =>
            o as Dictionary<string, object> ?? new Dictionary<string, object>();

        private static string LoadText(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path) : ""; }
            catch { return ""; }
        }

        private static Dictionary<string, string> LoadMarker()
        {
            var result = new Dictionary<string, string>();
            try
            {
                if (File.Exists(MarkerPath) && Json.Deserialize(File.ReadAllText(MarkerPath)) is Dictionary<string, object> d)
                    foreach (var kv in d) result[kv.Key] = kv.Value?.ToString() ?? "";
            }
            catch { }
            return result;
        }

        private static void SaveMarker(Dictionary<string, string> marker)
        {
            try
            {
                var d = new Dictionary<string, object>();
                foreach (var kv in marker) d[kv.Key] = kv.Value;
                File.WriteAllText(MarkerPath, Json.Serialize(d));
            }
            catch { }
        }

        private static string Hash(string text)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(text ?? ""));
                var sb = new StringBuilder();
                foreach (var b in bytes) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }
}
