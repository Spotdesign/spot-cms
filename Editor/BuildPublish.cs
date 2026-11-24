// BuildPublish.cs — one-click "Build & Publish Release" for Spot exhibits.
//
// Menu: Tools > CMS > Build & Publish Release...
//
// What it does, in order:
//   1. (optional) Bump contentVersion / settingsVersion and/or set the "force full content replace"
//      flag in Assets/StreamingAssets/content.meta.json — reusing TextNodeExporter's helpers so the
//      numbers stay a single source of truth. These are written BEFORE the build so the built
//      StreamingAssets carry them.
//   2. Builds the player with BuildPipeline (Windows and/or macOS) into a temp folder, named
//      <handle>.exe / <handle>.app from app.json.
//   3. Zips the build CONTENTS at the archive root (SpotAdmin extracts straight into the deploy
//      folder and expects <handle>.exe + <handle>_Data/ at the top level) → Archive-win.zip /
//      Archive-mac.zip.
//   4. Works out the next release name by incrementing the last number of the latest GitHub release
//      (1.2 -> 1.3). Editable before publishing.
//   5. Creates the GitHub release (the tag is auto-created from the current branch — no manual tag
//      step) and uploads the zip(s) as assets.
//
// Repo + owner come from the project's git "origin" remote, so nothing to configure per project.
// The write token is read ONCE from a machine-wide source, never stored in any project:
//      1) environment variable  GITHUB_RELEASE_TOKEN
//      2) fallback file          ~/.spot-release-token.txt   (%USERPROFILE% on Windows)
// Use a fine-grained PAT with Contents: write on the Spotdesign repos.

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SpotCMS
{
    public class BuildPublishWindow : EditorWindow
    {
        private const string Dir = "Assets/StreamingAssets";
        private const string TokenEnv = "GITHUB_RELEASE_TOKEN";
        private const string TokenFileName = ".spot-release-token.txt";

        // options
        private bool _bumpContent;
        private bool _bumpSettings;
        private bool _forceReplace;
        private bool _buildWin = true;
        private bool _buildMac;
        private bool _replaceExisting;
        private string _version = "";

        // discovered context
        private string _owner = "", _repo = "", _branch = "main", _handle = "";
        private string _latestName = "";
        private bool _busy;
        private Vector2 _logScroll;
        private readonly StringBuilder _log = new StringBuilder();

        [MenuItem("Tools/CMS/Build & Publish Release...")]
        public static void Open()
        {
            var w = GetWindow<BuildPublishWindow>(true, "Build & Publish Release", true);
            w.minSize = new Vector2(520, 540);
            w.Discover();
            w.Show();
        }

        private void Discover()
        {
            (_owner, _repo, _branch) = GitOrigin();
            _handle = AppHandle.Read();
            _forceReplace = TextNodeExporter.GetMetaBool("contentReplace");
            // fetch latest release name in the background to suggest the next version
            _ = FetchLatestAsync();
        }

        private async Task FetchLatestAsync()
        {
            try
            {
                var token = ReadToken();
                if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(_repo)) return;
                var latest = await GitHubGetLatestReleaseName(_owner, _repo, token);
                _latestName = latest ?? "";
                if (string.IsNullOrEmpty(_version))
                {
                    _version = string.IsNullOrEmpty(_latestName) ? "1.0" : NextVersion(_latestName);
                    Repaint();
                }
            }
            catch (Exception e) { Log("Could not fetch latest release: " + e.Message); }
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6);

            // context
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Repository", string.IsNullOrEmpty(_repo) ? "— no git 'origin' found —" : _owner + "/" + _repo);
                EditorGUILayout.LabelField("Branch (tag target)", _branch);
                EditorGUILayout.LabelField("Player (app.json handle)", string.IsNullOrEmpty(_handle) ? "— not set (Tools > CMS > App Handle) —" : _handle);
                EditorGUILayout.LabelField("Latest release", string.IsNullOrEmpty(_latestName) ? "—" : _latestName);
                var (cv, sv) = CurrentVersions();
                EditorGUILayout.LabelField("content.meta.json", $"contentVersion {cv}  ·  settingsVersion {sv}");
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Content update", EditorStyles.boldLabel);
            _bumpContent  = EditorGUILayout.ToggleLeft("Bump content version  (pushes text/content to every machine)", _bumpContent);
            _bumpSettings = EditorGUILayout.ToggleLeft("Bump settings version  (pushes settings.json to every machine)", _bumpSettings);
            _forceReplace = EditorGUILayout.ToggleLeft("Force full content replace  (overwrite venue translations too)", _forceReplace);
            if (_forceReplace)
                EditorGUILayout.HelpBox("With this on, the next content bump REPLACES all content on every machine, including locally-added translations. Needs a content bump to take effect.", MessageType.Warning);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Build targets", EditorStyles.boldLabel);
            _buildWin = EditorGUILayout.ToggleLeft("Windows  (Archive-win.zip)", _buildWin);
            _buildMac = EditorGUILayout.ToggleLeft("macOS  (Archive-mac.zip)", _buildMac);

            EditorGUILayout.Space(6);
            _replaceExisting = EditorGUILayout.ToggleLeft(
                "Replace current release (test build — rebuild + swap the zip, no new version)", _replaceExisting);
            if (_replaceExisting)
            {
                _version = _latestName;   // target the existing release, don't bump
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("Replacing release", string.IsNullOrEmpty(_latestName) ? "— none found —" : _latestName);
                EditorGUILayout.HelpBox("Rebuilds and overwrites the assets on the latest release (" +
                    (string.IsNullOrEmpty(_latestName) ? "none" : _latestName) + "). No new tag or version is created.", MessageType.None);
            }
            else
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    _version = EditorGUILayout.TextField("Release name / version", _version);
                    if (GUILayout.Button("Suggest", GUILayout.Width(70)) && !string.IsNullOrEmpty(_latestName))
                        _version = NextVersion(_latestName);
                }
            }

            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(_busy || !CanPublish()))
            {
                var label = _busy ? "Working…" : (_replaceExisting ? "Rebuild & Replace" : "Build & Publish");
                if (GUILayout.Button(label, GUILayout.Height(34)))
                    _ = RunAsync();
            }
            if (!CanPublish() && !_busy)
                EditorGUILayout.HelpBox(ValidationMessage(), MessageType.Info);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);
            _logScroll = EditorGUILayout.BeginScrollView(_logScroll, GUILayout.MinHeight(120));
            EditorGUILayout.TextArea(_log.ToString(), GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        private bool CanPublish() =>
            !string.IsNullOrEmpty(_repo) && !string.IsNullOrEmpty(_handle) &&
            !string.IsNullOrWhiteSpace(_version) && (_buildWin || _buildMac) &&
            !string.IsNullOrEmpty(ReadToken());

        private string ValidationMessage()
        {
            if (string.IsNullOrEmpty(_repo)) return "No git 'origin' remote — open the project from its git checkout.";
            if (string.IsNullOrEmpty(_handle)) return "No app.json handle — run Tools > CMS > App Handle first.";
            if (string.IsNullOrEmpty(ReadToken())) return $"No release token. Set {TokenEnv} or put the PAT in ~/{TokenFileName}.";
            if (!(_buildWin || _buildMac)) return "Pick at least one build target.";
            if (_replaceExisting && string.IsNullOrWhiteSpace(_latestName)) return "No existing release to replace — uncheck to create the first one.";
            if (string.IsNullOrWhiteSpace(_version)) return "Enter a release name / version.";
            return "";
        }

        // ---------- the run ----------
        private async Task RunAsync()
        {
            _busy = true; _log.Clear(); Repaint();
            try
            {
                var token = ReadToken();
                if (string.IsNullOrEmpty(token)) { Log("No release token found. Aborting."); return; }

                // 1) version / flag writes (before building so StreamingAssets carry them)
                TextNodeExporter.SetMetaBool("contentReplace", _forceReplace);
                if (_bumpContent)  Log("contentVersion -> "  + TextNodeExporter.BumpMeta("contentVersion"));
                if (_bumpSettings) Log("settingsVersion -> " + TextNodeExporter.BumpMeta("settingsVersion"));
                AssetDatabase.Refresh();

                var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
                if (scenes.Length == 0) { Log("No enabled scenes in Build Settings. Aborting."); return; }

                var assets = new List<(string name, string path)>();

                if (_buildWin)
                {
                    var zip = BuildAndZip(BuildTarget.StandaloneWindows64, _handle + ".exe", "win", scenes, "Archive-win.zip");
                    if (zip == null) return;
                    assets.Add(("Archive-win.zip", zip));
                }
                if (_buildMac)
                {
                    var zip = BuildAndZip(BuildTarget.StandaloneOSX, _handle + ".app", "mac", scenes, "Archive-mac.zip");
                    if (zip == null) return;
                    assets.Add(("Archive-mac.zip", zip));
                }

                // 3b) dated content backup — bundle the StreamingAssets content JSONs into one file
                //     and attach it to the release, so every release is a restore point (off-machine, per repo).
                var snapshot = WriteContentSnapshot(_version);
                if (snapshot != null) { assets.Add(("content-" + _version + ".json", snapshot)); Log("content backup prepared."); }

                // 4) target a release — either reuse the latest (replace mode) or create a new one
                long releaseId;
                List<(long id, string name)> existingAssets = new List<(long, string)>();
                if (_replaceExisting)
                {
                    var rel = await GitHubGetLatestRelease(_owner, _repo, token);
                    if (rel == null) { Log("No existing release to replace. Aborting."); return; }
                    releaseId = rel.Value.id;
                    existingAssets = rel.Value.assets;
                    Log($"Replacing assets on existing release \"{rel.Value.name}\" (id {releaseId}).");
                }
                else
                {
                    Log($"Creating release \"{_version}\" on {_owner}/{_repo}…");
                    var created = await GitHubCreateRelease(_owner, _repo, token, _version, _branch);
                    if (created == null) { Log("Release creation failed — see errors above."); return; }
                    releaseId = created.Value;
                    Log("Release id " + releaseId);
                }

                // 5) upload assets (deleting any same-named asset first when replacing)
                foreach (var (name, path) in assets)
                {
                    var old = existingAssets.FirstOrDefault(a => a.name == name);
                    if (old.id != 0)
                    {
                        var del = await GitHubDeleteAsset(_owner, _repo, token, old.id);
                        Log(del ? "  removed old " + name : "  could not remove old " + name + " (will try upload anyway)");
                    }
                    Log("Uploading " + name + " (" + ReadableSize(new FileInfo(path).Length) + ")…");
                    var ok = await GitHubUploadAsset(_owner, _repo, token, releaseId, name, path);
                    Log(ok ? "  uploaded " + name : "  FAILED " + name);
                }

                var getUrl = "https://spotd.uk/get/" + _repo;
                var verb = _replaceExisting ? "updated" : "published";
                Log("Done. Release " + _version + " " + verb + ".");
                Log("Client download link: " + getUrl);
                if (EditorUtility.DisplayDialog("Build & Publish",
                        "Release " + _version + " " + verb + " on " + _owner + "/" + _repo + ".\n\n" +
                        "Client download link (for the update email):\n" + getUrl,
                        "Copy link", "OK"))
                {
                    EditorGUIUtility.systemCopyBuffer = getUrl;
                    Log("Download link copied to clipboard.");
                }
                _latestName = _version;
            }
            catch (Exception e) { Log("ERROR: " + e); }
            finally { _busy = false; Repaint(); }
        }

        // Build to a temp dir, zip its CONTENTS at the archive root. Returns zip path or null on failure.
        private string BuildAndZip(BuildTarget target, string playerName, string tag, string[] scenes, string zipName)
        {
            var buildDir = Path.Combine(Path.GetTempPath(), "spotbuild_" + tag + "_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(buildDir);
            var location = Path.Combine(buildDir, playerName);

            Log($"Building {target}…");
            var group = BuildPipeline.GetBuildTargetGroup(target);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = location,
                target = target,
                targetGroup = group,
                options = BuildOptions.None,
            });
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Log($"Build FAILED ({report.summary.result}). " +
                    "If building Windows from macOS, install the 'Windows Build Support' module via Unity Hub.");
                return null;
            }

            var zipPath = Path.Combine(Path.GetTempPath(), zipName);
            if (File.Exists(zipPath)) File.Delete(zipPath);

            if (target == BuildTarget.StandaloneOSX)
            {
                // Preserve the .app bundle (symlinks + exec bits) with ditto, bundle at the zip root.
                if (!RunProcess("/usr/bin/ditto", $"-c -k --sequesterRsrc --keepParent \"{playerName}\" \"{zipPath}\"", buildDir))
                { Log("ditto zip FAILED."); return null; }
            }
            else
            {
                // Contents at the zip root (includeBaseDirectory = false).
                ZipFile.CreateFromDirectory(buildDir, zipPath, System.IO.Compression.CompressionLevel.Optimal, includeBaseDirectory: false);
            }
            Log($"  zipped {zipName} ({ReadableSize(new FileInfo(zipPath).Length)})");
            return zipPath;
        }

        // ---------- GitHub REST (fine-grained PAT, Contents: write) ----------
        private static HttpClient MakeClient(string token)
        {
            var c = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("SpotAdmin-Publisher");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            c.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return c;
        }

        private static async Task<string> GitHubGetLatestReleaseName(string owner, string repo, string token)
        {
            using var c = MakeClient(token);
            var r = await c.GetAsync($"https://api.github.com/repos/{owner}/{repo}/releases/latest");
            if (!r.IsSuccessStatusCode) return null;
            var body = await r.Content.ReadAsStringAsync();
            // name may be null; GitHub falls back to tag_name
            var name = JsonStr(body, "name");
            if (string.IsNullOrEmpty(name)) name = JsonStr(body, "tag_name");
            return name;
        }

        private async Task<long?> GitHubCreateRelease(string owner, string repo, string token, string version, string branch)
        {
            using var c = MakeClient(token);
            var payload = "{" +
                "\"tag_name\":" + JsonQuote(version) + "," +
                "\"target_commitish\":" + JsonQuote(branch) + "," +
                "\"name\":" + JsonQuote(version) + "," +
                "\"draft\":false,\"prerelease\":false}";
            var r = await c.PostAsync($"https://api.github.com/repos/{owner}/{repo}/releases",
                new StringContent(payload, Encoding.UTF8, "application/json"));
            var body = await r.Content.ReadAsStringAsync();
            if (!r.IsSuccessStatusCode) { Log("  create release " + (int)r.StatusCode + ": " + body); return null; }
            var idStr = JsonStr(body, "id");
            return long.TryParse(idStr, out var id) ? id : (long?)null;
        }

        // Latest release with its id, name and asset list (for replace mode).
        private async Task<(long id, string name, List<(long id, string name)> assets)?> GitHubGetLatestRelease(string owner, string repo, string token)
        {
            using var c = MakeClient(token);
            var r = await c.GetAsync($"https://api.github.com/repos/{owner}/{repo}/releases/latest");
            if (!r.IsSuccessStatusCode) return null;
            var body = await r.Content.ReadAsStringAsync();
            if (!(Json.Deserialize(body) is Dictionary<string, object> d)) return null;
            long id = d.TryGetValue("id", out var idv) && idv != null ? Convert.ToInt64(idv) : 0;
            if (id == 0) return null;
            string name = (d.TryGetValue("name", out var nv) && nv != null ? nv.ToString() : null)
                          ?? (d.TryGetValue("tag_name", out var tv) && tv != null ? tv.ToString() : "");
            var assets = new List<(long, string)>();
            if (d.TryGetValue("assets", out var av) && av is List<object> arr)
                foreach (var it in arr)
                    if (it is Dictionary<string, object> ad &&
                        ad.TryGetValue("id", out var aid) && aid != null &&
                        ad.TryGetValue("name", out var an) && an != null)
                        assets.Add((Convert.ToInt64(aid), an.ToString()));
            return (id, name, assets);
        }

        private async Task<bool> GitHubDeleteAsset(string owner, string repo, string token, long assetId)
        {
            using var c = MakeClient(token);
            var r = await c.DeleteAsync($"https://api.github.com/repos/{owner}/{repo}/releases/assets/{assetId}");
            if (!r.IsSuccessStatusCode) { Log("  delete asset " + (int)r.StatusCode + ": " + await r.Content.ReadAsStringAsync()); return false; }
            return true;
        }

        private async Task<bool> GitHubUploadAsset(string owner, string repo, string token, long releaseId, string name, string filePath)
        {
            using var c = MakeClient(token);
            using var fs = File.OpenRead(filePath);
            var content = new StreamContent(fs);
            content.Headers.ContentType = new MediaTypeHeaderValue(
                filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? "application/json" : "application/zip");
            var url = $"https://uploads.github.com/repos/{owner}/{repo}/releases/{releaseId}/assets?name={Uri.EscapeDataString(name)}";
            var r = await c.PostAsync(url, content);
            if (!r.IsSuccessStatusCode) { Log("  upload " + (int)r.StatusCode + ": " + await r.Content.ReadAsStringAsync()); return false; }
            return true;
        }

        // Bundle the StreamingAssets content JSONs into one dated snapshot file for the release.
        // Returns the temp file path, or null if there's no CMS content to back up.
        private string WriteContentSnapshot(string version)
        {
            try
            {
                if (!File.Exists(Path.Combine(Dir, "data.json"))) return null; // no text CMS in this project
                Dictionary<string, object> Read(string f)
                {
                    var p = Path.Combine(Dir, f);
                    if (File.Exists(p) && Json.Deserialize(File.ReadAllText(p)) is Dictionary<string, object> d) return d;
                    return null;
                }
                var snap = new Dictionary<string, object>
                {
                    ["version"]   = version,
                    ["timestamp"] = DateTime.UtcNow.ToString("o"),
                    ["repo"]      = _repo,
                    ["data"]      = Read("data.json"),
                    ["schema"]    = Read("schema.json"),
                    ["settings"]  = Read("settings.json"),
                    ["meta"]      = Read("content.meta.json"),
                };
                var outPath = Path.Combine(Path.GetTempPath(), "content-" + version + ".json");
                File.WriteAllText(outPath, Json.Serialize(snap));
                return outPath;
            }
            catch (Exception e) { Log("content backup skipped: " + e.Message); return null; }
        }

        // ---------- helpers ----------
        private (int content, int settings) CurrentVersions()
        {
            try
            {
                var path = Path.Combine(Dir, "content.meta.json");
                if (File.Exists(path) && Json.Deserialize(File.ReadAllText(path)) is Dictionary<string, object> d)
                {
                    int Get(string k) => d.TryGetValue(k, out var v) && v != null ? Convert.ToInt32(v) : 1;
                    return (Get("contentVersion"), Get("settingsVersion"));
                }
            }
            catch { }
            return (1, 1);
        }

        // Increment the trailing integer: "1.2" -> "1.3", "1.9" -> "1.10", "3" -> "4".
        public static string NextVersion(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "1.0";
            var m = Regex.Match(name.Trim(), @"^(.*?)(\d+)\s*$");
            if (!m.Success) return name.Trim();
            return m.Groups[1].Value + (int.Parse(m.Groups[2].Value) + 1);
        }

        private string ReadToken()
        {
            var env = Environment.GetEnvironmentVariable(TokenEnv);
            if (!string.IsNullOrWhiteSpace(env)) return env.Trim();
            try
            {
                var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), TokenFileName);
                if (File.Exists(file)) return File.ReadAllText(file).Trim();
            }
            catch { }
            return "";
        }

        // owner, repo, branch from .git/config origin + .git/HEAD
        private static (string owner, string repo, string branch) GitOrigin()
        {
            try
            {
                var root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var cfg = Path.Combine(root, ".git", "config");
                string owner = "Spotdesign", repo = "", branch = "main";
                if (File.Exists(cfg))
                {
                    var m = Regex.Match(File.ReadAllText(cfg), @"url\s*=\s*.*github\.com[:/]+([^/\s]+)/([^/\s]+?)(?:\.git)?\s*$",
                        RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    if (m.Success) { owner = m.Groups[1].Value; repo = m.Groups[2].Value; }
                }
                var head = Path.Combine(root, ".git", "HEAD");
                if (File.Exists(head))
                {
                    var h = File.ReadAllText(head).Trim();
                    var hm = Regex.Match(h, @"ref:\s*refs/heads/(.+)$");
                    if (hm.Success) branch = hm.Groups[1].Value.Trim();
                }
                return (owner, repo, branch);
            }
            catch { return ("Spotdesign", "", "main"); }
        }

        private static bool RunProcess(string exe, string args, string workingDir)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                };
                using var p = Process.Start(psi);
                var err = p.StandardError.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0) { Debug.LogWarning("[CMS] " + exe + " exit " + p.ExitCode + ": " + err); return false; }
                return true;
            }
            catch (Exception e) { Debug.LogWarning("[CMS] " + exe + " failed: " + e.Message); return false; }
        }

        // Minimal string extraction from a flat JSON object (top-level keys only; good enough for GitHub fields).
        private static string JsonStr(string json, string key)
        {
            var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(?:\"((?:\\\\.|[^\"\\\\])*)\"|(null)|(-?\\d+))");
            if (!m.Success) return null;
            if (m.Groups[2].Success) return null;         // null
            if (m.Groups[3].Success) return m.Groups[3].Value; // number
            return Regex.Unescape(m.Groups[1].Value);
        }

        private static string JsonQuote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        private static string ReadableSize(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB" };
            double v = bytes; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString(i == 0 ? "0" : "0.0") + " " + u[i];
        }

        private void Log(string line)
        {
            _log.AppendLine(line);
            Debug.Log("[CMS] " + line);
            Repaint();
        }
    }
}
#endif
