using Game.Combat;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Полоска здоровья в мире — висит над целью, всегда повёрнута к камере (billboard).
    /// Создаёт собственный World-Space Canvas + Filled Image при первом включении.
    /// Подписывается на Health.OnHealthChanged для мгновенного отклика, плюс polling в Update как страховка.
    /// </summary>
    [DisallowMultipleComponent]
    public class WorldSpaceHealthBar : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Health, который отображаем. Если не задан — ищется в parent.")]
        [SerializeField] private Health _health;

        [Header("Placement")]
        [Tooltip("Смещение бара относительно объекта в мировых координатах.")]
        [SerializeField] private Vector3 _worldOffset = new(0f, 2.2f, 0f);
        [Tooltip("Размер бара в мире (метры).")]
        [SerializeField] private Vector2 _size = new(1.2f, 0.16f);

        [Header("Behaviour")]
        [Tooltip("Скрывать бар, когда HP == 100% (показывать только когда нанесли урон).")]
        [SerializeField] private bool _hideWhenFull = false;
        [Tooltip("Скрывать бар, когда HP == 0.")]
        [SerializeField] private bool _hideWhenDead = true;

        [Header("Colors")]
        [SerializeField] private Color _bgColor = new(0f, 0f, 0f, 0.6f);
        [SerializeField] private Color _fillColor = new(0.85f, 0.15f, 0.15f, 1f);

        private Canvas _canvas;
        private Image _fill;
        private Transform _camTransform;

        private void Reset()
        {
            _health = GetComponentInParent<Health>();
        }

        private void Awake()
        {
            if (_health == null) _health = GetComponentInParent<Health>();
            BuildCanvas();
        }

        private void OnEnable()
        {
            if (_health != null)
            {
                _health.OnHealthChanged.AddListener(OnHealthChanged);
                OnHealthChanged(_health.Normalized);
            }
        }

        private void OnDisable()
        {
            if (_health != null)
                _health.OnHealthChanged.RemoveListener(OnHealthChanged);
        }

        private void LateUpdate()
        {
            if (_canvas == null) return;

            // Позиция: над таргетом
            _canvas.transform.position = transform.position + _worldOffset;

            // Billboard: повернуть лицом к камере
            if (_camTransform == null && Camera.main != null) _camTransform = Camera.main.transform;
            if (_camTransform != null)
            {
                _canvas.transform.rotation = Quaternion.LookRotation(
                    _canvas.transform.position - _camTransform.position, Vector3.up);
            }

            // Подстраховка polling
            if (_health != null && _fill != null)
                _fill.fillAmount = Mathf.Clamp01(_health.Normalized);

            // Видимость
            if (_health != null)
            {
                bool visible = true;
                if (_hideWhenFull && _health.Normalized >= 0.999f) visible = false;
                if (_hideWhenDead && !_health.IsAlive) visible = false;
                if (_canvas.gameObject.activeSelf != visible) _canvas.gameObject.SetActive(visible);
            }
        }

        private void OnHealthChanged(float n)
        {
            if (_fill != null) _fill.fillAmount = Mathf.Clamp01(n);
        }

        private void BuildCanvas()
        {
            // Создаём отдельный объект-Canvas как ребёнка, чтобы он не наследовал rotation/scale модели.
            var canvasGo = new GameObject("HealthBar_WS", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(null, false); // не парентим к носителю, чтобы scale модели не влиял на UI
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 5;

            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(100f, 14f); // в "виртуальных" пикселях
            // подбираем масштаб так, чтобы итоговый размер был _size в метрах
            float scaleX = _size.x / rt.sizeDelta.x;
            float scaleY = _size.y / rt.sizeDelta.y;
            float scale = Mathf.Min(scaleX, scaleY);
            rt.localScale = new Vector3(scale, scale, scale);

            // BG
            var bg = new GameObject("BG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bg.transform.SetParent(canvasGo.transform, false);
            var bgRT = (RectTransform)bg.transform;
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
            var bgImg = bg.GetComponent<Image>();
            bgImg.sprite = ArenaBootstrap.MakeWhiteSprite();
            bgImg.color = _bgColor;
            bgImg.raycastTarget = false;

            // Fill
            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            fillGo.transform.SetParent(bg.transform, false);
            var fillRT = (RectTransform)fillGo.transform;
            fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = new Vector2(1.5f, 1.5f);
            fillRT.offsetMax = new Vector2(-1.5f, -1.5f);
            _fill = fillGo.GetComponent<Image>();
            _fill.sprite = ArenaBootstrap.MakeWhiteSprite();
            _fill.color = _fillColor;
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Horizontal;
            _fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _fill.fillAmount = 1f;
            _fill.raycastTarget = false;
        }

        private void OnDestroy()
        {
            if (_canvas != null) Destroy(_canvas.gameObject);
        }
    }
}