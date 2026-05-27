using Game.Characters;
using Game.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Простой биндер HUD: показывает HP / стамину одного бойца.
    /// Привязать к Canvas, в инспекторе указать ссылки на _fighter и два Image (Filled).
    /// Использует и события (для моментального отклика), и polling каждый кадр (на случай если
    /// Image настроен не как Filled или события не сработали).
    /// </summary>
    public class FighterHUD : MonoBehaviour
    {
        [SerializeField] private Fighter _fighter;
        [SerializeField] private Image _healthFill;
        [SerializeField] private Image _staminaFill;

        /// <summary> Программный способ привязать ссылки до OnEnable. </summary>
        public void Bind(Fighter fighter, Image healthFill, Image staminaFill)
        {
            _fighter = fighter;
            _healthFill = healthFill;
            _staminaFill = staminaFill;
            // Если уже включён — переподписаться.
            if (isActiveAndEnabled) { OnDisable(); OnEnable(); }
        }

        private void OnEnable()
        {
            TryAutoFindFighter();
            // Подписываемся на события — для моментального отклика.
            if (_fighter == null)
            {
                Debug.LogWarning("[FighterHUD] _fighter не назначен и не найден.", this);
                return;
            }
            if (_fighter.Health != null)
            {
                _fighter.Health.OnHealthChanged.AddListener(SetHealth);
                SetHealth(_fighter.Health.Normalized);
            }
            if (_fighter.Stamina != null)
            {
                _fighter.Stamina.OnStaminaChanged.AddListener(SetStamina);
                SetStamina(_fighter.Stamina.Normalized);
            }
            ForceFilled(_healthFill);
            ForceFilled(_staminaFill);

            Debug.Log($"[FighterHUD] Fighter={_fighter?.name}, HealthFill={(_healthFill ? _healthFill.name : "NULL")}, StaminaFill={(_staminaFill ? _staminaFill.name : "NULL")}", this);
        }

        private void TryAutoFindFighter()
        {
            if (_fighter != null) return;
            var fighters = FindObjectsByType<Fighter>(FindObjectsSortMode.None);
            foreach (var f in fighters)
                if (f.IsLocalPlayer) { _fighter = f; return; }
        }

        private void OnDisable()
        {
            if (_fighter == null) return;
            if (_fighter.Health != null) _fighter.Health.OnHealthChanged.RemoveListener(SetHealth);
            if (_fighter.Stamina != null) _fighter.Stamina.OnStaminaChanged.RemoveListener(SetStamina);
        }

        private float _debugTimer;
        private void Update()
        {
            // Подстраховка: каждый кадр синхронизируем полоски с реальным значением.
            // Это бесплатно по производительности и страхует от любых "молчаливых" сценариев.
            if (_fighter == null) return;
            if (_fighter.Health  != null) SetHealth(_fighter.Health.Normalized);
            if (_fighter.Stamina != null) SetStamina(_fighter.Stamina.Normalized);

            // Раз в секунду логируем фактические значения для диагностики.
            _debugTimer += Time.deltaTime;
            if (_debugTimer >= 1f)
            {
                _debugTimer = 0f;
                var stam = _fighter.Stamina;
                var stamGo = stam != null ? stam.gameObject.name : "NULL";
                var hp = _fighter.Health;
                var fa = _staminaFill != null ? _staminaFill.fillAmount.ToString("F2") : "NO_IMG";
                Debug.Log($"[FighterHUD] HP={(hp != null ? hp.Current.ToString("F0") : "?")}/{(hp != null ? hp.Max.ToString("F0") : "?")}, " +
                          $"Stam={(stam != null ? stam.Current.ToString("F1") : "?")}/{(stam != null ? stam.Max.ToString("F0") : "?")} on '{stamGo}', " +
                          $"ST_Fill.fillAmount={fa}");
            }
        }

        private void SetHealth(float n)
        {
            if (_healthFill != null) _healthFill.fillAmount = Mathf.Clamp01(n);
        }

        private void SetStamina(float n)
        {
            if (_staminaFill != null) _staminaFill.fillAmount = Mathf.Clamp01(n);
        }

        /// <summary>
        /// Если поле Image не настроено как Filled — fillAmount ничего не делает.
        /// Принудительно переключаем тип на Filled / Horizontal, чтобы полоска работала.
        /// Также назначаем fallback белый sprite, если sprite не задан — без sprite
        /// инспектор не показывает Fill Amount и Image вообще не рендерится.
        /// </summary>
        private static void ForceFilled(Image img)
        {
            if (img == null) return;
            if (img.sprite == null)
            {
                img.sprite = ArenaBootstrap.MakeWhiteSprite();
                Debug.LogWarning($"[FighterHUD] У '{img.name}' не был задан Sprite — назначен fallback белый. Без sprite Filled-Image не отображается.", img);
            }
            if (img.type != Image.Type.Filled)
            {
                img.type = Image.Type.Filled;
                img.fillMethod = Image.FillMethod.Horizontal;
                img.fillOrigin = (int)Image.OriginHorizontal.Left;
            }
        }
    }
}
