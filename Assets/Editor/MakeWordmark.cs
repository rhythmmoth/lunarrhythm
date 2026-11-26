using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// Draws the crescent wordmark (board NextLogo) into Resources/Wordmark.png, which the title and loading screens
    /// show. The name is set by TextMeshPro with the game's own Unbounded, as the menus draw it: Pillow fills this
    /// font's overlapping strokes the wrong way, so tools/make_logo.py only makes the icons.
    /// </summary>
    public static class MakeWordmark
    {
        private const string FONT = "Assets/Art/Fonts/Unbounded.asset";
        private const string OUTPUT = "Assets/Resources/Wordmark.png";
        private const int HEIGHT = 360;
        private const int LAYER = 31;

        private static readonly Color Moon = new(0xF3 / 255f, 0xE8 / 255f, 0xCC / 255f);
        private static readonly Color Text = new(0xEE / 255f, 0xF0 / 255f, 0xFF / 255f);

        [MenuItem("LunarRhythm/Make the wordmark")]
        public static void Make()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FONT);
            if (font == null)
            {
                EditorUtility.DisplayDialog("Make the wordmark", "There's no " + FONT + ".", "OK");
                return;
            }

            float fontSize = HEIGHT * 0.62f;
            float moon = fontSize * 1.26f, gap = fontSize * 0.355f;
            var root = new GameObject("Wordmark (temporary)");
            try
            {
                // The name, as a white-on-black coverage mask: read back linear, so a pixel's value is its coverage
                var camera = new GameObject("Camera", typeof(Camera)).GetComponent<Camera>();
                camera.transform.SetParent(root.transform, false);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 1 << LAYER;
                camera.orthographic = true;
                camera.allowHDR = false;
                camera.allowMSAA = false;

                var canvas = new GameObject("Canvas", typeof(Canvas)).GetComponent<Canvas>();
                canvas.transform.SetParent(root.transform, false);
                canvas.gameObject.layer = LAYER;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;

                var text = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
                text.transform.SetParent(canvas.transform, false);
                text.gameObject.layer = LAYER;
                text.font = font;
                text.fontSize = fontSize;
                text.fontWeight = FontWeight.Medium;
                text.characterSpacing = -1f; // the boards' -0.01em
                text.enableWordWrapping = false;
                text.color = Color.white;
                text.text = "LunarRhythm";
                text.alignment = TextAlignmentOptions.BaselineLeft;
                text.ForceMeshUpdate();
                int width = Mathf.CeilToInt(moon + gap + text.preferredWidth + 16f);

                var target = RenderTexture.GetTemporary(width, HEIGHT, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                camera.targetTexture = target;
                canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(width, HEIGHT);
                Canvas.ForceUpdateCanvases();

                // Place the baseline so the capitals sit centred on the image's middle, where the moon is
                var rect = text.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 0.5f);
                rect.sizeDelta = new Vector2(width, HEIGHT);
                text.ForceMeshUpdate();
                var capital = text.textInfo.characterInfo[0];
                float capMiddle = (capital.topLeft.y + capital.baseLine) / 2f;
                rect.anchoredPosition = new Vector2(moon + gap - capital.bottomLeft.x, -capMiddle);
                Canvas.ForceUpdateCanvases();
                text.ForceMeshUpdate();

                camera.Render();
                var mask = new Texture2D(width, HEIGHT, TextureFormat.RGBA32, false, true);
                var previous = RenderTexture.active;
                RenderTexture.active = target;
                mask.ReadPixels(new Rect(0, 0, width, HEIGHT), 0, 0);
                RenderTexture.active = previous;
                camera.targetTexture = null;
                RenderTexture.ReleaseTemporary(target);

                // The menus' crescent (its SVG path is two circles: a disc of 7.5 at (11, 9) in an 18-unit box, with a
                // disc of 6 at (13.75, 7.8) cut out; y is flipped here), then the name beside it
                float unit = moon / 18f, top = HEIGHT / 2f + moon / 2f;
                var outer = new Vector2(11f * unit, top - 9f * unit);
                var cut = new Vector2(13.75f * unit, top - 7.8f * unit);
                var output = new Texture2D(width, HEIGHT, TextureFormat.RGBA32, false);
                var pixels = new Color[width * HEIGHT];
                var coverage = mask.GetPixels();
                for (int y = 0; y < HEIGHT; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        var p = new Vector2(x + 0.5f, y + 0.5f);
                        float inMoon = Mathf.Clamp01(7.5f * unit - Vector2.Distance(p, outer) + 0.5f)
                            * Mathf.Clamp01(Vector2.Distance(p, cut) - 6f * unit + 0.5f);
                        int i = y * width + x;
                        float inText = coverage[i].r;
                        pixels[i] = inMoon > 0f ? new Color(Moon.r, Moon.g, Moon.b, inMoon) : new Color(Text.r, Text.g, Text.b, inText);
                    }
                }
                output.SetPixels(pixels);
                output.Apply();
                File.WriteAllBytes(OUTPUT, output.EncodeToPNG());
                Object.DestroyImmediate(mask);
                Object.DestroyImmediate(output);
                AssetDatabase.ImportAsset(OUTPUT);
                // A sprite at its own size: a plain texture is scaled to powers of two (and capped at 2048), which
                // changes its shape and stretches the mark
                var importer = (TextureImporter) AssetImporter.GetAtPath(OUTPUT);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 4096;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true; // shown well under its size: mipmaps keep the edges smooth
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                Debug.Log($"Made {OUTPUT} ({width}×{HEIGHT})");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
