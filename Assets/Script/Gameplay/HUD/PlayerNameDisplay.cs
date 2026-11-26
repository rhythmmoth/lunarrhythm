using DG.Tweening;
using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;
using YARG.Core;
using YARG.Core.Logging;
using YARG.Gameplay.Player;
using YARG.Helpers.Extensions;
using YARG.Player;
using YARG.Settings;

namespace YARG.Gameplay.HUD
{
    public class PlayerNameDisplay : GameplayBehaviour
    {
        [SerializeField]
        private TextMeshProUGUI _playerName;
        [SerializeField]
        private Image _instrumentIcon;
        [SerializeField]
        private RawImage _needleIcon;

        private CanvasGroup _canvasGroup;

        public float DisplayTime = 3.0f;
        public float FadeDuration = 0.5f;

        protected override void GameplayAwake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
        }

        public void ShowPlayer(YargPlayer player)
        {
            if (!ShouldShowPlayer(player))
            {
                return;
            }

            var profile = player.Profile;
            bool lobby = Menu.Online.LobbyPlay.Playing;
            _playerName.text = profile.Name;

            var spriteName = player.GetInstrumentSprite();
            _instrumentIcon.sprite = Addressables
                .LoadAssetAsync<Sprite>(spriteName)
                .WaitForCompletion();

            gameObject.SetActive(true);
            StopAllCoroutines();
            if (lobby)
            {
                _canvasGroup.alpha = 1f;
                return;
            }
            StartCoroutine(FadeoutCoroutine());
        }

        public void ShowPlayer(YargPlayer player, int needleId)
        {
            if (!ShouldShowPlayer(player))
            {
                return;
            }

            var textureNeedle = $"VocalNeedleTexture/{needleId}";
            _needleIcon.texture = Addressables.LoadAssetAsync<Texture2D>(textureNeedle).WaitForCompletion();
            _instrumentIcon.color = player.GetHarmonyColor();
            ShowPlayer(player);
        }

        // In a lobby only the others' names show, kept up so everyone knows whose highway is whose; yours would be in your way
        private bool ShouldShowPlayer(YargPlayer player)
        {
            return Menu.Online.LobbyPlay.Playing
                ? player.IsRemote
                : !GameManager.IsPractice && SettingsManager.Settings.ShowPlayerNameWhenStartingSong.Value;
        }

        private IEnumerator FadeoutCoroutine()
        {
            _canvasGroup.alpha = 1f;
            yield return new WaitForSeconds(DisplayTime);
            yield return _canvasGroup.DOFade(0f, FadeDuration).WaitForCompletion();

            gameObject.SetActive(false);
        }
    }
}
