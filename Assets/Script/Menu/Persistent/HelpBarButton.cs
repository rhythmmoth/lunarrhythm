using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using YARG.Core.Input;
using YARG.Helpers.Extensions;
using YARG.Menu.Data;
using YARG.Menu.Navigation;

namespace YARG.Menu.Persistent
{
    public class HelpBarButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField]
        private Image _buttonImage;
        [SerializeField]
        private Image _buttonBackground;
        [SerializeField]
        private Image _buttonHoldFill;
        [SerializeField]
        private Image _buttonOutline;

        [SerializeField]
        private Button _button;

        [SerializeField]
        private TextMeshProUGUI _buttonLabel;
        [SerializeField]
        private TextMeshProUGUI _buttonText;

        private HoldTracker _holdTracker;

        private NavigationScheme.Entry? _entry;

        private bool  _isSpecialButton;
        private Color _buttonBackgroundColor;
        private Color _buttonImageColor;
        private Color _buttonBackgroundColorOnDown;
        private Color _buttonFillColor;

        // Visually transparent, but not affected by the mask component
        private readonly Color _maskableClear = new(0f, 0f, 0f, 0.01f);
        private readonly Color _coolGrey      = Moonlight.Soft;
        private readonly Color _darkCoolGrey = new(123 / 255f, 127 / 255f, 154 / 255f, 0.05f);

        private bool _clickable = true;
        private bool _isPointerOver;
        private ButtonState _defaultState = ButtonState.NONE;

        private ButtonState _currentState = ButtonState.NONE;
        private bool IsPointerHolding => _entry?.HasHoldHandler == true && _holdTracker?.IsHolding == true;
        private bool IsDisabledState => _currentState == ButtonState.DISABLED;

        public enum ButtonState
        {
            NONE,
            HOVER,
            PRESS,
            HOLD,
            DISABLED,
        }

        public void SetInfoFromSchemeEntry(NavigationScheme.Entry entry, bool clickable = true)
        {
            _clickable = clickable;
            _entry = entry;

            if (entry.HasHoldHandler)
            {
                ConfigureHoldTracker(entry.HoldSeconds);
            }

            _defaultState = ButtonState.NONE;

            var icons = MenuData.NavigationIcons;
            _buttonBackgroundColor = icons.GetColor(entry.Action).WithAlpha(0.05f);
            _buttonBackgroundColorOnDown = icons.GetColor(entry.Action).WithAlpha(0.02f);
            _buttonFillColor = icons.GetColor(entry.Action).WithAlpha(0.3f);;
            _buttonImageColor = icons.GetColor(entry.Action).WithAlpha(1f);;

            // Label
            _buttonLabel.text = entry.DisplayName;
            StyleMoonlight(entry.Action, icons.GetColor(entry.Action));

            // Show/hide text and transitions
            _isSpecialButton = entry.Action is MenuAction.Select or MenuAction.Start or MenuAction.Left or MenuAction.Right;
            _buttonText.gameObject.SetActive(!_isSpecialButton);
            // No sprite swap: YARG's highlight sprites drew a stray glyph over the Moonlight keycap; hover is shown by colour
            _button.transition = Selectable.Transition.None;

            // Set sprite and fill color, then apply idle state
            _buttonImage.sprite = icons.GetIcon(entry.Action);
            if (_fret)
            {
                // Moonlight keycap: a disc in the fret's colour, so it reads the same on any controller
                _buttonImage.sprite = Moonlight.RoundedSprite(20);
                _buttonImage.type = Image.Type.Sliced;
                _buttonImageColor = icons.GetColor(entry.Action).WithAlpha(1f);
            }
            else
            {
                _buttonImage.type = Image.Type.Simple;
            }
            _buttonHoldFill.color = _buttonFillColor;
            if (_primary)
            {
                PlacePlayIcon();
            }
            ApplyState(_defaultState);
        }

        private bool _fret;
        private bool _styled;

        /// <summary>
        /// Moonlight look, as on the boards' footers: the fret buttons are 39px discs in their fret's colour (not a
        /// pad's letters, which differ between controllers), then the label in sentence case 13.5px after. Arrows
        /// and the Select/Start shapes keep their icon, in the text colour.
        /// </summary>
        private void StyleMoonlight(MenuAction action, Color actionColor)
        {
            if (!_styled)
            {
                _styled = true;
                _buttonImage.preserveAspect = false;
                _buttonLabel.fontStyle = _buttonText.fontStyle = FontStyles.Normal; // the old style forced capitals
            }
            _buttonLabel.fontSize = 22.5f; // every time: gameplay shrinks it
            // The help bar is set up before the main menu hands out the fonts, so catch up whenever they appear
            if (Moonlight.Body != null && _buttonLabel.font != Moonlight.Body)
            {
                _buttonLabel.font = _buttonText.font = Moonlight.Body;
            }
            _fret = action is MenuAction.Green or MenuAction.Red or MenuAction.Yellow or MenuAction.Blue
                or MenuAction.Orange;
            _buttonText.text = string.Empty;
            float width = _fret ? 39f : 36f;
            var rect = _buttonImage.rectTransform;
            rect.sizeDelta = new Vector2(width, width);
            rect.anchoredPosition = new Vector2(0f, rect.anchoredPosition.y); // pivoted on its left edge
            if (!_primary && _buttonOutline.TryGetComponent(out HorizontalLayoutGroup row))
            {
                row.padding = new RectOffset(Mathf.RoundToInt(width + 13.5f), 0, 0, 0);
                _buttonLabel.margin = Vector4.zero; // the prefab's 19/22px margins aren't on the board
            }
            if (!_fret)
            {
                _buttonImageColor = Moonlight.Soft;
            }
        }

        private bool _primary;

        /// <summary>
        /// Moonlight's main call to action: a cream pill with dark text, like the music library's Play button.
        /// </summary>
        public void MakePrimary()
        {
            _primary = true;
            _buttonBackground.sprite = _buttonHoldFill.sprite = Moonlight.RoundedSprite(34);
            _buttonBackground.type = Image.Type.Sliced;
            _buttonOutline.enabled = false;
            // The board's pill: a play triangle and the label centred, no keycap
            _buttonImage.gameObject.SetActive(false);
            if (_buttonOutline.TryGetComponent(out HorizontalLayoutGroup row))
            {
                row.padding = new RectOffset(0, 0, 0, 0);
            }
            _buttonLabel.alignment = TextAlignmentOptions.Center;
            _buttonLabel.fontSize = 25.5f;
            _buttonLabel.fontWeight = FontWeight.Bold;
            _playIcon = new GameObject("Play Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement)).GetComponent<Image>();
            _playIcon.GetComponent<LayoutElement>().ignoreLayout = true;
            _playIcon.transform.SetParent(transform, false);
            _playIcon.sprite = Moonlight.Play;
            _playIcon.color = Moonlight.Night;
            _playIcon.raycastTarget = false;
            _playIcon.rectTransform.anchorMin = _playIcon.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            _playIcon.rectTransform.sizeDelta = new Vector2(21f, 21f);
            PlacePlayIcon();
            ApplyState(_currentState);
        }

        private Image _playIcon;

        /// <summary>The triangle sits 15px left of the label; the two are centred together, like the board's.</summary>
        private void PlacePlayIcon()
        {
            if (_playIcon == null)
            {
                return;
            }
            _buttonLabel.margin = new Vector4(36f, 0f, 0f, 0f); // room for the triangle and its gap
            float text = _buttonLabel.GetPreferredValues(_buttonLabel.text).x;
            _playIcon.rectTransform.anchoredPosition = new Vector2(18f - text / 2f - 15f - 10.5f, 0f);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _isPointerOver = true;
            if (!_clickable)
            {
                return;
            }
            ApplyState(ButtonState.HOVER);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _isPointerOver = false;
            if (!_clickable)
            {
                return;
            }
            ApplyState(_defaultState);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!_clickable)
            {
                return;
            }

            if (_entry?.HasHoldHandler == true)
            {
                _holdTracker.StartHolding();
                ApplyState(ButtonState.HOLD);
            }
            else
            {
                ApplyState(ButtonState.PRESS);
                _entry?.Invoke();
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_clickable)
            {
                return;
            }

            if (IsPointerHolding)
            {
                _holdTracker.StopHolding();
            }

            _buttonHoldFill.fillAmount = 0f;
            ApplyState(_isPointerOver ? ButtonState.HOVER : _defaultState);
            _entry?.InvokeHoldOffHandler();
        }

        public void DisableButton()
        {
            _clickable = false;
            ClearHoldState();
            ApplyState(ButtonState.DISABLED);
        }

        public void EnableButton()
        {
            _clickable = true;
            _currentState = ButtonState.NONE;
            ApplyState(_defaultState);
        }

        public void SetDefaultButtonState(ButtonState state)
        {
            _defaultState = state;
            ApplyState(_defaultState);
        }

        private void HandleHoldProgress(float visualProgress)
        {
            UpdateButtonFillAmount(visualProgress);
        }

        private void HandleClick()
        {
            _entry?.Invoke();
        }

        private void HandleHoldComplete()
        {
            _entry?.InvokeHoldHandler();
            _buttonHoldFill.fillAmount = 0f;
            ApplyState(_isPointerOver ? ButtonState.HOVER : _defaultState);
        }

        private void HandleHoldCancelled()
        {
            // Visual reset happens in OnPointerUp
        }

        private void ConfigureHoldTracker(float holdSeconds)
        {
            _holdTracker ??= new HoldTracker(holdSeconds);
            _holdTracker.ClearEvents();
            _holdTracker.StopHolding();
            _holdTracker.Configure(holdSeconds);
            _holdTracker.OnHoldProgress += HandleHoldProgress;
            _holdTracker.OnClick += HandleClick;
            _holdTracker.OnHoldComplete += HandleHoldComplete;
            _holdTracker.OnHoldCancelled += HandleHoldCancelled;
        }

        private void ClearHoldState()
        {
            _buttonHoldFill.fillAmount = 0f;
            if (_entry?.HasHoldHandler == true)
            {
                ConfigureHoldTracker(_entry.Value.HoldSeconds);
            }
        }

        private void ApplyState(ButtonState state)
        {
            if (IsDisabledState && state != ButtonState.DISABLED)
            {
                return;
            }

            _currentState = state;

            _button.transition = Selectable.Transition.None;
            switch (state)
            {
                case ButtonState.NONE:
                    _buttonBackground.color = _maskableClear;
                    _buttonOutline.color = _maskableClear;
                    _buttonImage.color = _buttonImageColor;
                    _buttonLabel.color = _coolGrey;
                    _buttonText.color = _coolGrey;
                    break;
                case ButtonState.HOVER:
                    _buttonBackground.color = _buttonBackgroundColor;
                    _buttonOutline.color = _buttonBackgroundColor;
                    _buttonImage.color = _buttonImageColor;
                    _buttonLabel.color = Color.white;
                    _buttonText.color = Color.white;
                    break;
                case ButtonState.PRESS:
                    _buttonBackground.color = _buttonBackgroundColorOnDown;
                    _buttonOutline.color = _buttonBackgroundColorOnDown;
                    _buttonImage.color = _buttonImageColor;
                    _buttonLabel.color = Color.white;
                    _buttonText.color = Color.white;
                    break;
                case ButtonState.HOLD:
                    _buttonBackground.color = _buttonBackgroundColor;
                    _buttonOutline.color = _buttonBackgroundColor;
                    _buttonImage.color = _buttonImageColor;
                    _buttonLabel.color = Color.white;
                    _buttonText.color = Color.white;
                    break;
                case ButtonState.DISABLED:
                    _buttonBackground.color = _darkCoolGrey;
                    _buttonOutline.color = _darkCoolGrey;
                    _buttonImage.color = _coolGrey;
                    _buttonLabel.color = _coolGrey;
                    _buttonText.color = _coolGrey;
                    _button.transition = Selectable.Transition.None;
                    break;
            }

            if (_primary)
            {
                _button.transition = Selectable.Transition.None; // the swap sprites are YARG's
                _buttonBackground.color = state is ButtonState.PRESS or ButtonState.HOLD
                    ? Color.Lerp(Moonlight.Moon, Moonlight.Night, 0.12f)
                    : Moonlight.Moon;
                _buttonHoldFill.color = Moonlight.Night.WithAlpha(0.15f);
                _buttonLabel.color = _buttonText.color = Moonlight.Night;
            }
        }

        private void Update()
        {
            if (IsDisabledState)
            {
                return;
            }

            if (_entry?.HasHoldHandler != true)
            {
                return;
            }

            _holdTracker?.Tick();
            if (!IsPointerHolding)
            {
                HandleControllerHold();
            }
        }

        // Controller hold, not pointer hold.  This only handles visuals, the actual hold action is
        // triggered by the Navigator when the hold is complete
        private void HandleControllerHold()
        {
            var rawHoldProgress = _entry.HasValue
                ? Navigator.Instance.GetHoldProgress(_entry.Value.Action)
                : -1f;
            if (rawHoldProgress >= 0f)
            {
                if (_currentState != ButtonState.HOLD)
                {
                    ApplyState(ButtonState.HOLD);
                }
                var visualProgress = _holdTracker.GetVisualHoldProgress(rawHoldProgress);
                UpdateButtonFillAmount(visualProgress);
            }
            else if (_buttonHoldFill.fillAmount > 0f || _currentState == ButtonState.HOLD)
            {
                _buttonHoldFill.fillAmount = 0f;
                ApplyState(_isPointerOver ? ButtonState.HOVER : _defaultState);
            }
        }

        private void UpdateButtonFillAmount(float amount)
        {
            _buttonHoldFill.fillAmount = amount;
            if (amount > 0f && _currentState != ButtonState.HOLD)
            {
                ApplyState(ButtonState.HOLD);
            }
        }
    }
}
