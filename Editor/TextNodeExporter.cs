// TextNodeExporter.cs — editor tool that scans the open scene(s) for TextNode components
// and writes/updates the DEFAULT data.json in Assets/StreamingAssets. It only ADDS keys
// (page / field / language) that are missing — existing text is never overwritten — so you
// can run it any time you add new TextNodes. Languages come from schema.json.
//
// Menu: Tools > CMS > Export Text Keys to StreamingAssets
//       Tools > CMS > Create Default schema.json + settings.json

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SpotCMS
{
    public static class TextNodeExporter
    {
        private const string Dir = "Assets/StreamingAssets";

        [MenuItem("Tools/CMS/Export Text Keys to StreamingAssets")]
        public static void ExportKeys() => ExportKeysCore(false);

        [MenuItem("Tools/CMS/Export Text Keys (Overwrite Default Language from Scene)")]
        public static void ExportKeysOverwrite()
        {
            if (!EditorUtility.DisplayDialog("Overwrite from scene",
                "Replace the default-language text in data.json with each TextMeshPro's current text?\n\n" +
                "New keys are still added; other languages' translations are kept.",
                "Overwrite", "Cancel"))
                return;
            ExportKeysCore(true);
        }

        private static void ExportKeysCore(bool overwriteDefault)
        {
            Directory.CreateDirectory(Dir);

            var languages = ReadLanguageCodes(); // from schema.json (or default en)
            var defaultLang = ReadDefaultLanguage();
            var existing = ReadObject(Path.Combine(Dir, "data.json"));
            // Rebuilt in scene (hierarchy) order so pages/fields match Unity's child order.
            var ordered = new Dictionary<string, object>();

            int scanned = 0, newPages = 0, newFields = 0, warnings = 0, seeded = 0, overwritten = 0;

            foreach (var node in FindSceneTextNodes())
            {
                scanned++;
                var t = node.transform;
                if (!page.Resolve(t, out var category, out var field))
                {
                    Debug.LogWarning($"[CMS] '{node.name}' has no page above it — add a `page` component to a parent. Skipped.", node);
                    warnings++;
                    continue;
                }

                if (category.Contains("-") || field.Contains("-"))
                {
                    Debug.LogWarning($"[CMS] '{category}-{field}' contains '-'. Keys split on '-', so avoid dashes in names.", node);
                    warnings++;
                }

                if (!(ordered.TryGetValue(category, out var po) && po is Dictionary<string, object> pageObj))
                {
                    pageObj = new Dictionary<string, object>();
                    ordered[category] = pageObj;
                    if (!ExistingHasPage(existing, category)) newPages++;
                }

                if (!(pageObj.TryGetValue(field, out var fo) && fo is Dictionary<string, object> fieldObj))
                {
                    // Carry over existing values for this key (preserves translations); else start fresh.
                    fieldObj = ExistingField(existing, category, field) ?? new Dictionary<string, object>();
                    pageObj[field] = fieldObj;
                    if (ExistingField(existing, category, field) == null) newFields++;
                }

                // Seed any NEW language slot with the text currently on the TMP component, so a
                // freshly-added field/language starts populated instead of blank. Existing values kept.
                var tmp = node.GetComponent<TMP_Text>();
                string src = tmp != null ? tmp.text : "";
                foreach (var code in languages)
                {
                    bool isDefault = string.Equals(code, defaultLang, System.StringComparison.OrdinalIgnoreCase);
                    if (!fieldObj.ContainsKey(code))
                    {
                        fieldObj[code] = src;
                        if (!string.IsNullOrEmpty(src)) seeded++;
                    }
                    else if (overwriteDefault && isDefault && (fieldObj[code] as string) != src)
                    {
                        fieldObj[code] = src; // sync default-language text from the scene
                        overwritten++;
                    }
                }
            }

            // Preserve any keys that aren't represented in the scene (CMS-only / globals without a
            // node) by appending them after the scene-ordered keys. Use Clean Unused Keys to prune.
            foreach (var pe in existing)
            {
                if (!(pe.Value is Dictionary<string, object> exFields)) continue;
                if (!(ordered.TryGetValue(pe.Key, out var oo) && oo is Dictionary<string, object> orderedFields))
                {
                    ordered[pe.Key] = pe.Value;
                    continue;
                }
                foreach (var fe in exFields)
                    if (!orderedFields.ContainsKey(fe.Key))
                        orderedFields[fe.Key] = fe.Value;
            }

            File.WriteAllText(Path.Combine(Dir, "data.json"), Json.Serialize(ordered));
            AssetDatabase.Refresh();
            Debug.Log($"[CMS] Export done. Scanned {scanned} TextNode(s); added {newPages} page(s), {newFields} field(s), seeded {seeded} value(s), overwrote {overwritten} default value(s) from TMP; {warnings} warning(s). Order matches the scene hierarchy. Languages: {string.Join(", ", languages)}");
        }

        [MenuItem("Tools/CMS/Export Content Bundle to File…")]
        public static void ExportContentBundle()
        {
            var path = EditorUtility.SaveFilePanel("Export content bundle", "", "spot-content.json", "json");
            if (string.IsNullOrEmpty(path)) return;

            var bundle = new Dictionary<string, object>
            {
                ["handle"] = Application.productName,
                ["exported"] = System.DateTime.UtcNow.ToString("o"),
                ["data"] = ReadObject(Path.Combine(Dir, "data.json")),
                ["settings"] = ReadObject(Path.Combine(Dir, "settings.json")),
                ["schema"] = ReadObject(Path.Combine(Dir, "schema.json")),
            };
            File.WriteAllText(path, Json.Serialize(bundle));
            Debug.Log("[CMS] Content bundle exported to " + path);
            EditorUtility.RevealInFinder(path);
        }

        [MenuItem("Tools/CMS/Fill All Languages from Default (overwrite)")]
        public static void FillAllFromDefault()
        {
            var path = Path.Combine(Dir, "data.json");
            if (!File.Exists(path)) { EditorUtility.DisplayDialog("Fill all languages", "No data.json found.", "OK"); return; }

            var defaultLang = ReadDefaultLanguage();
            if (!EditorUtility.DisplayDialog("Fill all languages",
                $"Copy the '{defaultLang}' text into EVERY other language for every field, overwriting what's there?\n\n" +
                "This replaces all translations with the default-language text (use to clear stale content).",
                "Overwrite all", "Cancel"))
                return;

            var languages = ReadLanguageCodes();
            var data = ReadObject(path);
            int fields = 0, filled = 0;

            foreach (var pageKv in data)
            {
                if (!(pageKv.Value is Dictionary<string, object> pageObj)) continue;
                foreach (var fieldKv in pageObj)
                {
                    if (!(fieldKv.Value is Dictionary<string, object> langObj)) continue;
                    fields++;
                    string def = langObj.TryGetValue(defaultLang, out var dv) && dv != null ? dv.ToString() : "";
                    foreach (var code in languages)
                    {
                        if (code == defaultLang) continue;
                        var cur = langObj.TryGetValue(code, out var cv) ? (cv?.ToString() ?? "") : null;
                        if (cur != def) { langObj[code] = def; filled++; }
                    }
                }
            }

            File.WriteAllText(path, Json.Serialize(data));
            AssetDatabase.Refresh();
            Debug.Log($"[CMS] Filled {filled} value(s) across {fields} field(s) with '{defaultLang}' text.");
        }

        [MenuItem("Tools/CMS/Import Content Bundle from File…")]
        public static void ImportContentBundle()
        {
            var path = EditorUtility.OpenFilePanel("Import content bundle (spot-content.json)", "", "json");
            if (string.IsNullOrEmpty(path)) return;

            Dictionary<string, object> root;
            try { root = Json.Deserialize(File.ReadAllText(path)) as Dictionary<string, object>; }
            catch (System.Exception e) { EditorUtility.DisplayDialog("Import bundle", "Couldn't read file:\n" + e.Message, "OK"); return; }

            if (root == null || (!root.ContainsKey("data") && !root.ContainsKey("settings") && !root.ContainsKey("schema")))
            {
                EditorUtility.DisplayDialog("Import bundle", "That doesn't look like a Spot content bundle (no data/settings/schema).", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Import bundle",
                "Overwrite this project's StreamingAssets (data / settings / schema) with the content from:\n\n" + path +
                "\n\nThen review, bump the version(s) and build to release.",
                "Overwrite", "Cancel"))
                return;

            Directory.CreateDirectory(Dir);
            WriteIf(root, "data", "data.json");
            WriteIf(root, "settings", "settings.json");
            WriteIf(root, "schema", "schema.json");
            AssetDatabase.Refresh();
            Debug.Log("[CMS] Imported content bundle into StreamingAssets from " + path + ". Bump version(s) and build to release.");
        }

        private static void WriteIf(Dictionary<string, object> root, string key, string file)
        {
            if (root.TryGetValue(key, out var v) && v is Dictionary<string, object> obj)
                File.WriteAllText(Path.Combine(Dir, file), Json.Serialize(obj));
        }

        [MenuItem("Tools/CMS/Clean Unused Keys in data.json")]
        public static void CleanUnusedKeys()
        {
            var path = Path.Combine(Dir, "data.json");
            if (!File.Exists(path)) { EditorUtility.DisplayDialog("CMS Clean", "No data.json found.", "OK"); return; }
            var data = ReadObject(path);

            // Valid keys = every page->field currently present in the open scene(s).
            var valid = new Dictionary<string, HashSet<string>>();
            foreach (var node in FindSceneTextNodes())
            {
                if (!page.Resolve(node.transform, out var category, out var field)) continue;
                if (!valid.TryGetValue(category, out var set)) { set = new HashSet<string>(); valid[category] = set; }
                set.Add(field);
            }

            // Find orphans: pages/fields in data.json with no matching node in the scene.
            var orphanPages = new List<string>();
            var orphanFields = new List<string>(); // "Page-field"
            foreach (var pageEntry in data.ToList())
            {
                if (!(pageEntry.Value is Dictionary<string, object> fields)) continue;
                if (!valid.TryGetValue(pageEntry.Key, out var validFields))
                {
                    orphanPages.Add(pageEntry.Key);
                    continue;
                }
                foreach (var fieldName in fields.Keys.ToList())
                    if (!validFields.Contains(fieldName))
                        orphanFields.Add(pageEntry.Key + "-" + fieldName);
            }

            if (orphanPages.Count == 0 && orphanFields.Count == 0)
            {
                EditorUtility.DisplayDialog("CMS Clean", "Nothing to clean — data.json matches the scene.", "OK");
                return;
            }

            var msg = "These keys are in data.json but not in the open scene, so they'll be removed (their text will be lost):\n\n";
            if (orphanPages.Count > 0) msg += "Whole pages: " + string.Join(", ", orphanPages) + "\n\n";
            if (orphanFields.Count > 0) msg += "Fields: " + string.Join(", ", orphanFields) + "\n\n";
            msg += "Tip: keys authored only in the CMS (with no Unity node) will also be flagged. Continue?";

            if (!EditorUtility.DisplayDialog("CMS Clean — confirm", msg, "Remove", "Cancel")) return;

            foreach (var p in orphanPages) data.Remove(p);
            foreach (var key in orphanFields)
            {
                var dash = key.IndexOf('-');
                var pageName = key.Substring(0, dash);
                var fieldName = key.Substring(dash + 1);
                if (data.TryGetValue(pageName, out var po) && po is Dictionary<string, object> fields)
                {
                    fields.Remove(fieldName);
                    if (fields.Count == 0) data.Remove(pageName); // drop now-empty page
                }
            }

            File.WriteAllText(path, Json.Serialize(data));
            AssetDatabase.Refresh();
            Debug.Log($"[CMS] Clean done. Removed {orphanPages.Count} page(s) and {orphanFields.Count} field(s) from data.json.");
        }

        [MenuItem("Tools/CMS/Import Live Content into StreamingAssets")]
        public static void ImportLiveContent()
        {
            var live = Application.persistentDataPath;
            var liveData = Path.Combine(live, "data.json");
            if (!File.Exists(liveData))
            {
                EditorUtility.DisplayDialog("Import live content",
                    "No live data.json found at:\n" + live + "\n\nEdit content in the CMS (or run the app) first.", "OK");
                return;
            }

            if (!EditorUtility.DisplayDialog("Import live content",
                "Copy the live edited content (data / settings / schema) from persistentDataPath into this project's " +
                "StreamingAssets, and bump contentVersion so the next release pushes it to all machines?\n\nFrom:\n" + live,
                "Import & bump", "Cancel"))
                return;

            Directory.CreateDirectory(Dir);
            CopyIfExists(Path.Combine(live, "data.json"), Path.Combine(Dir, "data.json"));
            CopyIfExists(Path.Combine(live, "settings.json"), Path.Combine(Dir, "settings.json"));
            CopyIfExists(Path.Combine(live, "schema.json"), Path.Combine(Dir, "schema.json"));
            int v = BumpContentVersion();
            AssetDatabase.Refresh();
            Debug.Log($"[CMS] Imported live content into StreamingAssets. contentVersion is now {v}. Build & release to distribute.");
        }

        [MenuItem("Tools/CMS/Clear Live Content (reseed on next Play)")]
        public static void ClearLiveContent()
        {
            var dir = Application.persistentDataPath;
            if (!EditorUtility.DisplayDialog("Clear live content",
                "Delete this machine's live content (data / settings / schema + marker) so it reseeds " +
                "straight from StreamingAssets on the next Play? Use this while developing instead of " +
                "juggling versions.\n\n" + dir,
                "Clear", "Cancel"))
                return;

            foreach (var f in new[] { "data.json", "settings.json", "schema.json", ".cms_seed.json" })
            {
                var p = Path.Combine(dir, f);
                try { if (File.Exists(p)) File.Delete(p); } catch { }
            }
            Debug.Log("[CMS] Cleared live content in " + dir + " — reseeds from StreamingAssets on next Play.");
        }

        [MenuItem("Tools/CMS/Bump Content Version")]
        public static void BumpContentVersionMenu()
        {
            int v = BumpMeta("contentVersion");
            AssetDatabase.Refresh();
            Debug.Log($"[CMS] contentVersion is now {v}. Build & release to push content to all machines.");
        }

        [MenuItem("Tools/CMS/Bump Settings Version")]
        public static void BumpSettingsVersionMenu()
        {
            int v = BumpMeta("settingsVersion");
            AssetDatabase.Refresh();
            Debug.Log($"[CMS] settingsVersion is now {v}. Build & release to push settings to all machines.");
        }

        // ---- Force content replace: when ticked, content.meta.json gets "contentReplace": true, so on the
        // next release every machine REPLACES its data.json with the build's copy (translations included)
        // the first time it runs the new contentVersion. When unticked, the default structural merge is
        // used and venues keep their own translations. Stays set until you untick it. ----
        private const string ReplaceMenu = "Tools/CMS/Force Content Replace On Update";

        [MenuItem(ReplaceMenu)]
        public static void ToggleContentReplace()
        {
            bool on = !GetMetaBool("contentReplace");
            SetMetaBool("contentReplace", on);
            AssetDatabase.Refresh();
            Debug.Log(on
                ? "[CMS] Force content replace is ON. The next release (after a contentVersion bump) overwrites ALL content on every machine, translations included."
                : "[CMS] Force content replace is OFF. Updates keep each machine's own translations (structural merge).");
        }

        [MenuItem(ReplaceMenu, true)]
        public static bool ToggleContentReplaceValidate()
        {
            Menu.SetChecked(ReplaceMenu, GetMetaBool("contentReplace"));
            return true;
        }

        private static Dictionary<string, object> LoadMeta()
        {
            var path = Path.Combine(Dir, "content.meta.json");
            try
            {
                if (File.Exists(path) && Json.Deserialize(File.ReadAllText(path)) is Dictionary<string, object> d)
                    return d;
            }
            catch { }
            return new Dictionary<string, object>();
        }

        public static bool GetMetaBool(string key)
        {
            var meta = LoadMeta();
            if (!meta.TryGetValue(key, out var v) || v == null) return false;
            if (v is bool b) return b;
            return string.Equals(v.ToString(), "true", System.StringComparison.OrdinalIgnoreCase);
        }

        public static void SetMetaBool(string key, bool value)
        {
            var meta = LoadMeta();
            meta[key] = value;
            if (!meta.ContainsKey("contentVersion")) meta["contentVersion"] = 1;
            if (!meta.ContainsKey("settingsVersion")) meta["settingsVersion"] = 1;
            Directory.CreateDirectory(Dir);
            File.WriteAllText(Path.Combine(Dir, "content.meta.json"), Json.Serialize(meta));
        }

        private static void CopyIfExists(string src, string dst)
        {
            if (File.Exists(src)) File.Copy(src, dst, true);
        }

        private static int BumpContentVersion() => BumpMeta("contentVersion");

        // Bump one version key independently (just +1 on that key), preserving the other.
        public static int BumpMeta(string key)
        {
            var path = Path.Combine(Dir, "content.meta.json");
            var meta = new Dictionary<string, object>();
            try
            {
                if (File.Exists(path) && Json.Deserialize(File.ReadAllText(path)) is Dictionary<string, object> d)
                    meta = d;
            }
            catch { }

            int Cur(string k) => meta.TryGetValue(k, out var x) && x != null ? System.Convert.ToInt32(x) : 0;
            int v = Cur(key) + 1;

            meta[key] = v;
            if (!meta.ContainsKey("contentVersion")) meta["contentVersion"] = key == "contentVersion" ? v : 1;
            if (!meta.ContainsKey("settingsVersion")) meta["settingsVersion"] = key == "settingsVersion" ? v : 1;
            File.WriteAllText(path, Json.Serialize(meta));
            return v;
        }

        [MenuItem("Tools/CMS/Create Default schema.json + settings.json")]
        public static void CreateDefaults()
        {
            Directory.CreateDirectory(Dir);

            var schemaPath = Path.Combine(Dir, "schema.json");
            if (!File.Exists(schemaPath))
            {
                var schema = new Dictionary<string, object>
                {
                    ["defaultLanguage"] = "en",
                    ["languages"] = new List<object>
                    {
                        new Dictionary<string, object> { ["code"] = "en", ["name"] = "English" },
                        new Dictionary<string, object> { ["code"] = "cy", ["name"] = "Cymraeg" },
                    }
                };
                File.WriteAllText(schemaPath, Json.Serialize(schema));
            }

            var settingsPath = Path.Combine(Dir, "settings.json");
            if (!File.Exists(settingsPath))
            {
                var settings = new Dictionary<string, object>
                {
                    ["timeOutLength"] = 30,
                    ["attractLoopSeconds"] = 60,
                    ["fadeSpeed"] = 0.5,
                };
                File.WriteAllText(settingsPath, Json.Serialize(settings));
            }

            var metaPath = Path.Combine(Dir, "content.meta.json");
            if (!File.Exists(metaPath))
                File.WriteAllText(metaPath, Json.Serialize(new Dictionary<string, object>
                {
                    ["contentVersion"] = 1,
                    ["settingsVersion"] = 1,
                }));

            AssetDatabase.Refresh();
            Debug.Log("[CMS] Default schema.json / settings.json / content.meta.json ensured in " + Dir);
        }

        private static List<string> ReadLanguageCodes()
        {
            var codes = new List<string>();
            try
            {
                var path = Path.Combine(Dir, "schema.json");
                if (File.Exists(path) &&
                    Json.Deserialize(File.ReadAllText(path)) is Dictionary<string, object> root &&
                    root.TryGetValue("languages", out var lo) && lo is List<object> arr)
                {
                    foreach (var item in arr)
                        if (item is Dictionary<string, object> d && d.TryGetValue("code", out var c) && c != null)
                            codes.Add(c.ToString());
                }
            }
            catch { }
            if (codes.Count == 0) codes.Add("en");
            return codes;
        }

        private static string ReadDefaultLanguage()
        {
            try
            {
                var path = Path.Combine(Dir, "schema.json");
                if (File.Exists(path) &&
                    Json.Deserialize(File.ReadAllText(path)) is Dictionary<string, object> root &&
                    root.TryGetValue("defaultLanguage", out var dl) && dl != null)
                    return dl.ToString();
            }
            catch { }
            return "en";
        }

        private static bool ExistingHasPage(Dictionary<string, object> existing, string page)
            => existing.TryGetValue(page, out var v) && v is Dictionary<string, object>;

        private static Dictionary<string, object> ExistingField(Dictionary<string, object> existing, string page, string field)
        {
            if (existing.TryGetValue(page, out var po) && po is Dictionary<string, object> fields &&
                fields.TryGetValue(field, out var fo) && fo is Dictionary<string, object> f)
                return f;
            return null;
        }

        private static Dictionary<string, object> ReadObject(string path)
        {
            try
            {
                if (File.Exists(path) && Json.Deserialize(File.ReadAllText(path)) is Dictionary<string, object> d)
                    return d;
            }
            catch { }
            return new Dictionary<string, object>();
        }

        // Depth-first over the loaded scene(s) so keys are written in Unity hierarchy / child order.
        private static IEnumerable<textNode> FindSceneTextNodes()
        {
            var list = new List<textNode>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (var root in scene.GetRootGameObjects())
                    CollectTextNodes(root.transform, list);
            }
            return list;
        }

        private static void CollectTextNodes(Transform t, List<textNode> list)
        {
            var node = t.GetComponent<textNode>();
            if (node != null && !node.IsCopy) list.Add(node); // copies mirror a master -> no CMS entry
            for (int i = 0; i < t.childCount; i++)
                CollectTextNodes(t.GetChild(i), list);
        }
    }
}
#endif
