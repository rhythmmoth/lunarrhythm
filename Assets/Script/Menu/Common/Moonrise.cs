using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using YARG.Settings;

namespace YARG.Menu
{
    public enum MenuAnimationMode
    {
        On,
        Reduced,
        Off
    }

    /// <summary>
    /// LunarRhythm's one menu motion ("Moonrise", board NextTransitions): a menu fades in and rises into place, going
    /// back drops it in from above, dialogs grow in, and scene changes pass through the night instead of a black cut.
    /// Only the incoming side moves; input is never held up. "Reduced" keeps the fades, "Off" keeps nothing.
    /// </summary>
    public static class Moonrise
    {
        public const float MENU = 0.22f;
        public const float POPUP = 0.12f;
        private const float DISTANCE = 24f;
        private const float COVER = 0.25f;
        private const float LIFT = 0.35f;
        private const float SMOOTH_FRAME = 0.05f;

        private static CanvasGroup _veil;
        private static int _covers; // counts covers, so a lift still waiting gives way to a newer cover

        // A menu's motion starts two frames after it opens: that first frame is often a long one (a menu building its
        // rows), and a tween started in it would skip most of its run. What's still waiting is kept here.
        private static readonly Dictionary<CanvasGroup, float> PendingAlpha = new();
        private static readonly Dictionary<RectTransform, Vector2> PendingHome = new();

        private static MenuAnimationMode Mode => SettingsManager.Settings?.MenuAnimations.Value ?? MenuAnimationMode.On;

        /// <summary>Fades a menu in; with full animations it also rises into place (or drops in, going back).</summary>
        public static void Rise(GameObject menu, bool back = false)
        {
            if (menu == null || Mode == MenuAnimationMode.Off) return;
            if (!menu.TryGetComponent(out CanvasGroup group)) group = menu.AddComponent<CanvasGroup>();
            FadeSoon(group, 1f, MENU);
            if (Mode != MenuAnimationMode.On || menu.transform is not RectTransform rect) return;

            rect.DOKill(true); // a rise still running ends first, so its end is the real home
            if (!PendingHome.TryGetValue(rect, out var home)) home = rect.anchoredPosition;
            PendingHome[rect] = home;
            rect.anchoredPosition = home + new Vector2(0f, back ? DISTANCE : -DISTANCE);
            Soon(() =>
            {
                if (!PendingHome.Remove(rect) || rect == null) return;
                rect.DOKill();
                rect.DOAnchorPos(home, MENU).SetEase(Ease.OutCubic).SetUpdate(true).SetLink(rect.gameObject);
            }).Forget();
        }

        /// <summary>A dialog appearing: its card grows from 96% while it fades in.</summary>
        public static void Grow(CanvasGroup group, Transform card)
        {
            if (Mode == MenuAnimationMode.Off)
            {
                group.DOKill();
                group.alpha = 1f;
                return;
            }
            FadeSoon(group, 1f, POPUP);
            if (Mode != MenuAnimationMode.On || card == null) return;
            card.DOKill(true);
            card.localScale = Vector3.one * 0.96f;
            Soon(() =>
            {
                if (card == null) return;
                card.DOScale(1f, POPUP).SetEase(Ease.OutCubic).SetUpdate(true).SetLink(card.gameObject);
            }).Forget();
        }

        /// <summary>Results: the stars land one by one once the screen has lifted.</summary>
        public static void Land(Transform star, int index)
        {
            if (Mode != MenuAnimationMode.On) return;
            star.localScale = Vector3.zero;
            star.DOScale(1f, 0.28f).SetDelay(LIFT + 0.1f + index * 0.09f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(star.gameObject);
        }

        /// <summary>The alpha a part of the menu really has (a fade still running or waiting to start ends first).
        /// Overlays save this before hiding the menu under them, so closing them never leaves it half see-through.</summary>
        public static float Settle(CanvasGroup group)
        {
            if (PendingAlpha.Remove(group, out float target)) group.alpha = target;
            group.DOKill(true);
            return group.alpha;
        }

        /// <summary>Brings back a part of the menu an overlay hid, fading it in.</summary>
        public static void Return(CanvasGroup group, float alpha)
        {
            if (Mode == MenuAnimationMode.Off || alpha <= 0f)
            {
                PendingAlpha.Remove(group);
                group.DOKill();
                group.alpha = alpha;
                return;
            }
            FadeSoon(group, alpha, MENU);
        }

        /// <summary>Covers the screen in night, then runs <paramref name="then"/> (the scene change).</summary>
        public static void Cover(Action then)
        {
            if (Mode == MenuAnimationMode.Off)
            {
                then();
                return;
            }
            // A second change during the cover replaces the first: only the newest scene loads
            FadeVeil(1f, COVER, Ease.InQuad).OnComplete(() => then());
        }

        /// <summary>Covers the screen in night and waits until it's covered.</summary>
        public static async UniTask CoverAsync()
        {
            if (Mode == MenuAnimationMode.Off) return;
            var fade = FadeVeil(1f, COVER, Ease.InQuad);
            await UniTask.WaitWhile(() => fade.IsActive() && fade.IsPlaying());
        }

        /// <summary>Lifts the night once the frames underneath run smoothly (the next scene has settled).</summary>
        public static void Lift(float maxWait = 2f) => LiftWhenSmooth(_covers, maxWait).Forget();

        /// <summary>Waits until a few frames in a row run smoothly (and, in the editor, no shader is still compiling),
        /// so what comes next isn't shown mid-hitch; never longer than <paramref name="maxSeconds"/>.</summary>
        public static async UniTask WaitUntilSmooth(float maxSeconds = 2f)
        {
            int smooth = 0;
            float waited = 0f;
            while (smooth < 3 && waited < maxSeconds)
            {
                await UniTask.NextFrame();
                waited += Time.unscaledDeltaTime;
                bool busy = Time.unscaledDeltaTime > SMOOTH_FRAME;
#if UNITY_EDITOR
                busy |= UnityEditor.ShaderUtil.anythingCompiling;
#endif
                smooth = busy ? 0 : smooth + 1;
            }
        }

        private static async UniTaskVoid LiftWhenSmooth(int cover, float maxWait)
        {
            if (_veil == null || _veil.alpha <= 0f) return;
            await WaitUntilSmooth(maxWait);
            if (_veil == null || cover != _covers) return;
            _veil.DOKill();
            _veil.DOFade(0f, LIFT).SetEase(Ease.OutQuad).SetUpdate(true).OnComplete(() => _veil.blocksRaycasts = false);
        }

        private static Tween FadeVeil(float to, float duration, Ease ease)
        {
            var veil = Veil();
            _covers++;
            veil.DOKill();
            veil.blocksRaycasts = true;
            return veil.DOFade(to, duration * Mathf.Abs(to - veil.alpha)).SetEase(ease).SetUpdate(true);
        }

        /// <summary>Hides <paramref name="group"/> now and fades it to <paramref name="to"/> two frames later.</summary>
        private static void FadeSoon(CanvasGroup group, float to, float duration)
        {
            group.DOKill();
            group.alpha = 0f;
            PendingAlpha[group] = to;
            Soon(() =>
            {
                if (!PendingAlpha.Remove(group) || group == null) return;
                group.DOFade(to, duration).SetEase(Ease.OutCubic).SetUpdate(true).SetLink(group.gameObject);
            }).Forget();
        }

        private static async UniTaskVoid Soon(Action start)
        {
            await UniTask.DelayFrame(2);
            start();
        }

        private static CanvasGroup Veil()
        {
            if (_veil != null) return _veil;
            var go = new GameObject("Moonrise Veil", typeof(Canvas), typeof(CanvasGroup), typeof(Image));
            UnityEngine.Object.DontDestroyOnLoad(go);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            go.GetComponent<Image>().color = Moonlight.Night;
            _veil = go.GetComponent<CanvasGroup>();
            _veil.alpha = 0f;
            _veil.blocksRaycasts = false;
            return _veil;
        }
    }
}
