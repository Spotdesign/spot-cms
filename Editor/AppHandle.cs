// AppHandle.cs — creates/maintains app.json in the Unity project root (the repo root):
//
//     { "handle": "MyExhibit" }
//
// The handle is the name of the built player: MyExhibit.exe + MyExhibit_Data. The update proxy reads
// app.json from the repo to build the client download "<Handle>-<version>.zip" and to tell SpotAdmin
// which exe to launch, so it only has to be set once per project (then commit + push it).
//
// If app.json is missing, a small prompt opens (once per editor session) asking for the handle,
// pre-filled from the Product Name. Menu: Tools > CMS > App Handle (app.json)...
// After each Windows build it also warns if the built exe name doesn't match the handle.

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace SpotCMS
{
    [InitializeOnLoad]
    public static class AppHandle
    {
        private const string PromptedKey = "SpotCMS.AppHandlePrompted";

        public static string FilePath =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "app.json"));

        static AppHandle()
        {
            // Defer so the editor has finished loading, and only nag once per session.
            EditorApplication.delayCall += () =>
            {
                if (File.Exists(FilePath) && !string.IsNullOrEmpty(Read())) return;
                if (SessionState.GetBool(PromptedKey, false)) return;
                SessionState.SetBool(PromptedKey, true);
                AppHandleWindow.Open();
            };
        }

        [MenuItem("Tools/CMS/App Handle (app.json)...")]
        public static void Menu() => AppHandleWindow.Open();

        /// <summary>The saved handle, or "" if app.json is missing/invalid.</summary>
        public static string Read()
        {
            try
            {
                if (!File.Exists(FilePath)) return "";
                if (Json.Deserialize(File.ReadAllText(FilePath)) is Dictionary<string, object> d &&
                    d.TryGetValue("handle", out var h) && h != null)
                    return h.ToString().Trim();
            }
            catch { }
            return "";
        }

        // The handle is the player's exe name, so spaces are fine ("Hickory Classic" = Hickory Classic.exe).
        // Reject only characters that can't be in a Windows file name, plus leading/trailing space or dot.
        private const string InvalidChars = "[\\\\/:*?\"<>|\\x00-\\x1F]";

        public static bool IsValid(string handle) =>
            !string.IsNullOrEmpty(handle) && handle.Length <= 120 &&
            handle == handle.Trim(' ', '\t', '.') && !Regex.IsMatch(handle, InvalidChars);

        /// <summary>Product Name (Unity's default build name) minus any illegal file-name characters.</summary>
        public static string SuggestHandle()
        {
            var s = Regex.Replace(PlayerSettings.productName ?? "", InvalidChars, "").Trim(' ', '\t', '.');
            return string.IsNullOrEmpty(s) ? "MyExhibit" : s;
        }

        public static void Save(string handle)
        {
            var d = new Dictionary<string, object> { ["handle"] = handle };
            File.WriteAllText(FilePath, Json.Serialize(d));
            Debug.Log("[CMS] Saved app.json (handle \"" + handle + "\") at " + FilePath +
                      " — commit and push it so the download link can use it.");
        }

        // Warn if the built player's name doesn't match the handle (the app would never find the exe).
        [PostProcessBuild(1)]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.StandaloneWindows && target != BuildTarget.StandaloneWindows64) return;
            var handle = Read();
            if (string.IsNullOrEmpty(handle)) { Debug.LogWarning("[CMS] No app.json handle set. Run Tools > CMS > App Handle (app.json)."); return; }
            var built = Path.GetFileNameWithoutExtension(pathToBuiltProject);
            if (!string.Equals(built, handle, System.StringComparison.OrdinalIgnoreCase))
                Debug.LogWarning("[CMS] Built player is \"" + built + ".exe\" but app.json handle is \"" + handle +
                                 "\". SpotAdmin launches <handle>.exe — rename the build or update the handle.");
        }
    }

    public class AppHandleWindow : EditorWindow
    {
        private string _handle = "";

        public static void Open()
        {
            var w = GetWindow<AppHandleWindow>(true, "App handle", true);
            w.minSize = w.maxSize = new Vector2(440, 170);
            w.ShowUtility();
        }

        private void OnEnable()
        {
            var existing = AppHandle.Read();
            _handle = string.IsNullOrEmpty(existing) ? AppHandle.SuggestHandle() : existing;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "The handle is the name of the built player (MyExhibit.exe / MyExhibit_Data). It is saved in " +
                "app.json in the project root — commit and push it so the download link and SpotAdmin know which exe to launch.",
                MessageType.Info);
            EditorGUILayout.Space(4);
            _handle = EditorGUILayout.TextField("Handle", _handle);

            bool valid = AppHandle.IsValid(_handle.Trim());
            if (!valid)
                EditorGUILayout.HelpBox("Must be a valid file name: no \\ / : * ? \" < > | characters, and no leading/trailing space or dot.", MessageType.Warning);

            EditorGUILayout.Space(6);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Not now", GUILayout.Width(90))) Close();
                using (new EditorGUI.DisabledScope(!valid))
                {
                    if (GUILayout.Button("Save app.json", GUILayout.Width(120)))
                    {
                        AppHandle.Save(_handle.Trim());
                        AssetDatabase.Refresh();
                        Close();
                    }
                }
            }
        }
    }
}
#endif
