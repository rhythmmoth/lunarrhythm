using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Logging;
using YARG.Core.Song;
using YARG.Helpers;

namespace YARG.Menu.Persistent
{
    /// <summary>
    /// Keeps the game up to date (board NextUpdater): on the first title screen of a run it compares the game's files
    /// with the latest build's list on the CDN (YARG.Core's <see cref="BuildUpdate"/>). Nothing differs: nothing shows.
    /// Something does: an updating screen downloads the build, stages the changed files, and a small script copies them
    /// in once the game has closed and starts it again. Any failure leaves the files as they were; the game carries on.
    /// Not in the editor.
    /// </summary>
    public class AutoUpdater : MonoBehaviour
    {
        private static bool _checked;

        private TextMeshProUGUI _title, _body, _detail;
        private Image _fill;
        private GameObject _bar, _button;

        /// <summary>Once per run, from the title screen.</summary>
        public static void CheckOnce()
        {
            if (_checked || Application.isEditor) return;
            _checked = true;
            Check().Forget();
        }

        private static string GameDir => Directory.GetParent(Application.dataPath)!.FullName;
        private static string CheckedFile => Path.Combine(PathHelper.PersistentDataPath, "update_checked.txt");

        private static async UniTaskVoid Check()
        {
            string json = await BuildUpdate.FetchManifest();
            if (json == null || BuildUpdate.Parse(json) is not { } manifest) return;
            if (!BuildUpdate.IsNewer(manifest, BuildUpdate.ReadStamp(Application.streamingAssetsPath))) return;
            string hash = BuildUpdate.Hash(json);
            // Already checked against this build's list: nothing to compare again
            if (File.Exists(CheckedFile) && File.ReadAllText(CheckedFile).Trim() == hash) return;
            var changed = await UniTask.RunOnThreadPool(() => BuildUpdate.Changed(manifest, GameDir));
            if (changed.Count == 0)
            {
                File.WriteAllText(CheckedFile, hash);
                return;
            }
            YargLogger.LogInfo($"Update: {changed.Count} files differ from the latest build");
            var screen = Create();
            await screen.Run(manifest, changed);
        }

        private async UniTask Run(BuildUpdate.Manifest manifest, System.Collections.Generic.List<string> changed)
        {
            Show("Updating LunarRhythm", "A new version is out. This takes a minute.", true);
            string zip = Path.Combine(Application.temporaryCachePath, "lunarrhythm-latest.zip");
            string staging = Path.Combine(Application.temporaryCachePath, "lunarrhythm-update");
            long done = 0, total = -1;
            var download = Task.Run(() => BuildUpdate.Download(zip, (d, t) => { done = d; total = t; }));
            while (!download.IsCompleted)
            {
                SetProgress(done, total);
                await UniTask.Yield();
            }
            bool ok = await download && await UniTask.RunOnThreadPool(() => BuildUpdate.Stage(zip, manifest, changed, staging));
            TryDelete(zip);
            if (!ok || !StartSwap(staging))
            {
                Show("Couldn't update", "Your game is unchanged. Online play needs the new version: try again later, or get it from Discord.", false);
                _button.SetActive(true);
                return;
            }
            Show("Starting again…", $"Swapping {changed.Count} changed {(changed.Count == 1 ? "file" : "files")}. The game reopens by itself.", false);
            await UniTask.Delay(800, ignoreTimeScale: true);
            Application.Quit();
        }

        // Writes the swap script and starts it: it waits for this process to end, copies the staged files over the
        // game's, removes the staging folder and starts the game again
        private static bool StartSwap(string staging)
        {
            try
            {
                string exe = Process.GetCurrentProcess().MainModule!.FileName;
                string script = Path.Combine(Application.temporaryCachePath, "lunarrhythm-update.ps1");
                File.WriteAllText(script, string.Join("\r\n",
                    "param([int]$GamePid, [string]$Staging, [string]$Target, [string]$Exe)",
                    "Wait-Process -Id $GamePid -ErrorAction SilentlyContinue",
                    "Start-Sleep -Milliseconds 500",
                    "robocopy $Staging $Target /E /R:10 /W:1 /NFL /NDL /NJH /NJS | Out-Null",
                    "Remove-Item -Recurse -Force $Staging -ErrorAction SilentlyContinue",
                    "Start-Process -FilePath $Exe"));
                Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\" -GamePid {Process.GetCurrentProcess().Id} " +
                        $"-Staging \"{staging}\" -Target \"{GameDir}\" -Exe \"{exe}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                return true;
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, "Couldn't start the update");
                return false;
            }
        }

        private static void TryDelete(string file)
        {
            try
            {
                if (File.Exists(file)) File.Delete(file);
            }
            catch (IOException) { }
        }

        // ---------- the screen ----------

        private static AutoUpdater Create()
        {
            var root = new GameObject("Auto Updater", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            DontDestroyOnLoad(root);
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            var view = root.AddComponent<AutoUpdater>();
            view.Build((RectTransform) root.transform);
            return view;
        }

        private void Build(RectTransform root)
        {
            var sky = Moonlight.Panel("Night", root, 2, new Color(0.008f, 0.024f, 0.102f, 1f));
            Fill(sky.rectTransform);
            sky.raycastTarget = true; // nothing behind it can be clicked meanwhile

            var moon = Moonlight.Panel("Moon", root, 48, Moonlight.Moon);
            Place(moon.rectTransform, new Vector2(0f, 150f), new Vector2(96f, 96f));
            var shade = Moonlight.Panel("Shade", root, 39, new Color(0.008f, 0.024f, 0.102f, 1f));
            Place(shade.rectTransform, new Vector2(-6f, 171f), new Vector2(78f, 78f));

            var font = Moonlight.Heading != null ? Moonlight.Heading : TMP_Settings.defaultFontAsset;
            var body = Moonlight.Body != null ? Moonlight.Body : TMP_Settings.defaultFontAsset;
            _title = Moonlight.Label("Title", root, font, 36f, Moonlight.Text);
            _title.alignment = TextAlignmentOptions.Center;
            Place(_title.rectTransform, new Vector2(0f, 30f), new Vector2(1200f, 54f));
            _body = Moonlight.Label("Body", root, body, 24f, new Color(0.83f, 0.85f, 0.95f));
            _body.alignment = TextAlignmentOptions.Center;
            _body.textWrappingMode = TextWrappingModes.Normal;
            Place(_body.rectTransform, new Vector2(0f, -30f), new Vector2(900f, 66f));

            var bar = Moonlight.Panel("Bar", root, 4, Moonlight.Keycap);
            Place(bar.rectTransform, new Vector2(0f, -100f), new Vector2(420f, 9f));
            _bar = bar.gameObject;
            _fill = Moonlight.Panel("Fill", bar.rectTransform, 4, Moonlight.Moon);
            _fill.rectTransform.anchorMin = Vector2.zero;
            _fill.rectTransform.anchorMax = new Vector2(0f, 1f);
            _fill.rectTransform.offsetMin = _fill.rectTransform.offsetMax = Vector2.zero;
            _detail = Moonlight.Label("Detail", root, body, 20f, Moonlight.Muted);
            _detail.alignment = TextAlignmentOptions.Center;
            Place(_detail.rectTransform, new Vector2(0f, -130f), new Vector2(600f, 30f));

            var button = Moonlight.Panel("Play anyway", root, 30, Moonlight.Moon);
            button.raycastTarget = true;
            Place(button.rectTransform, new Vector2(0f, -120f), new Vector2(240f, 60f));
            button.gameObject.AddComponent<Button>().onClick.AddListener(() => Destroy(gameObject));
            var label = Moonlight.Label("Label", button.rectTransform, body, 22f, new Color(0.043f, 0.063f, 0.149f));
            label.fontWeight = FontWeight.Bold;
            label.alignment = TextAlignmentOptions.Center;
            label.text = "Play anyway";
            Fill(label.rectTransform);
            _button = button.gameObject;
            _button.SetActive(false);
        }

        private void Update()
        {
            // A pad's confirm button also closes the "couldn't update" screen
            if (_button != null && _button.activeSelf
                && (UnityEngine.InputSystem.Keyboard.current?.enterKey.wasPressedThisFrame == true
                    || UnityEngine.InputSystem.Gamepad.current?.buttonSouth.wasPressedThisFrame == true))
            {
                Destroy(gameObject);
            }
        }

        private void Show(string title, string body, bool bar)
        {
            _title.text = title;
            _body.text = body;
            _bar.SetActive(bar);
            _detail.gameObject.SetActive(bar);
        }

        private void SetProgress(long done, long total)
        {
            float part = total > 0 ? (float) done / total : 0f;
            _fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(part), 1f);
            _detail.text = total > 0 ? $"{done / 1048576} of {total / 1048576} MB" : $"{done / 1048576} MB";
        }

        private static void Fill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Place(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
