using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using YARG.Core.Song;
using YARG.Menu.Main;

namespace YARG.Settings.Metadata
{
    /// <summary>The General tab's side panel: which Discord account the leaderboards use.</summary>
    public class LeaderboardAccountPanelBuilder : IPreviewBuilder
    {
        public UniTask BuildPreviewWorld(Transform worldContainer) => UniTask.CompletedTask;

        public UniTask BuildPreviewUI(Transform uiContainer)
        {
            // Borrow the settings menu's own font
            var font = uiContainer.GetComponentInParent<Canvas>().GetComponentInChildren<TextMeshProUGUI>(true).font;

            // The container stacks its children in a column and is see-through to the (empty) 3D preview behind it,
            // so lay out on a solid panel of our own that the column ignores
            var panel = new GameObject("Leaderboard Account", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            panel.GetComponent<LayoutElement>().ignoreLayout = true;
            var image = panel.GetComponent<Image>();
            image.color = new Color(0.012f, 0.055f, 0.086f);
            image.raycastTarget = false;
            var panelRect = image.rectTransform;
            panelRect.SetParent(uiContainer, false);
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
            uiContainer = panelRect;

            Line(uiContainer, font, "LEADERBOARDS", 22f, MainMenuUI.Accent, FontStyles.Bold, 40f).characterSpacing = 4f;
            if (Leaderboards.SignedIn)
            {
                Line(uiContainer, font, "Signed in as", 26f, new Color(0.72f, 0.7f, 0.84f), FontStyles.Normal, 80f);
                Line(uiContainer, font, Leaderboards.PlayerName ?? "your Discord account", 40f, Color.white,
                    FontStyles.Bold, 116f);
                Line(uiContainer, font, "Your scores on online songs go to the leaderboards.", 24f,
                    new Color(0.72f, 0.7f, 0.84f), FontStyles.Normal, 180f).enableWordWrapping = true;
            }
            else
            {
                Line(uiContainer, font, "Not signed in", 40f, Color.white, FontStyles.Bold, 80f);
                Line(uiContainer, font, "Sign in with Discord to put your scores on the leaderboards.", 24f,
                    new Color(0.72f, 0.7f, 0.84f), FontStyles.Normal, 144f).enableWordWrapping = true;
            }
            return UniTask.CompletedTask;
        }

        private static TextMeshProUGUI Line(Transform parent, TMP_FontAsset font, string text, float size, Color color,
            FontStyles style, float top)
        {
            var line = MainMenuUI.Text(text, parent, font, size, color, style);
            line.text = text;
            var rect = line.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(40f, -top - size * 2.6f); // room for two lines when wrapping
            rect.offsetMax = new Vector2(-40f, -top);
            line.verticalAlignment = VerticalAlignmentOptions.Top;
            return line;
        }
    }
}
