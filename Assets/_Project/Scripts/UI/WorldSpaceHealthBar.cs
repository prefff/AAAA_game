using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Полоска здоровья в мире — висит над бойцом и всегда повёрнута к камере (billboard).
    /// Значение берёт из делегата (состояние симуляции), собственного состояния не хранит.
    /// </summary>
    [DisallowMultipleComponent]
    public class WorldSpaceHealthBar : MonoBehaviour
    {
        [Header("Placement")]
        [Tooltip("Смещение бара относительно объекта в мировых координатах.")]
        [SerializeField] private Vector3 _worldOffset = new(0f, 2.4f, 0f);
        [Tooltip("Размер бара в мире (метры).")]
        [SerializeField] private Vector2 _size = new(1.2f, 0.16f);

        [Header("Behaviour")]
        [Tooltip("Скрывать бар, когда HP == 100%.")]
        [SerializeField] private bool _hideWhenFull = false;
        [Tooltip("Скрывать бар, когда HP == 0.")]
        [SerializeField] private bool _hideWhenDead = true;

        [Header("Colors")]
        [SerializeField] private Color _bgColor = new(0f, 0f, 0f, 0.6f);
        [SerializeField] private Color _fillColor = new(0.85f, 0.15f, 0.15f, 1f);

        private Func<float> _health;
        private Canvas _canvas;
        private Image _fill;
        private Transform _camTransform;

        /// <summary> Источник здоровья 0..1. </summary>
        public void Bind(Func<float> normalizedHealth) => _health = normalizedHealth;

        private void Awake() => BuildCanvas();

        private void LateUpdate()
        {
            if (_canvas == null || _health == null) return;
            float hp = Mathf.Clamp01(_health());

            bool visible = !(_hideWhenFull && hp >= 0.999f) && !(_hideWhenDead && hp <= 0f);
            if (_canvas.gameObject.activeSelf != visible) _canvas.gameObject.SetActive(visible);
            if (!visible) return;

            _canvas.transform.position = transform.position + _worldOffset;
            if (_camTransform == null && Camera.main != null) _camTransform = Camera.main.transform;
            if (_camTransform != null) _canvas.transform.rotation = _camTransform.rotation; // параллельно экрану
            _fill.fillAmount = hp;
        }

        private void BuildCanvas()
        {
            // Отдельный корневой объект: поворот и масштаб модели (KO — боец лежит) не должны влиять на UI.
            var canvasGo = new GameObject("HealthBar_WS", typeof(Canvas));
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 5;

            var rt = (RectTransform)canvasGo.transform;
            rt.sizeDelta = new Vector2(100f, 14f); // «виртуальные» пиксели
            float scale = Mathf.Min(_size.x / rt.sizeDelta.x, _size.y / rt.sizeDelta.y);
            rt.localScale = new Vector3(scale, scale, scale);

            var bg = new GameObject("BG", typeof(RectTransform));
            bg.transform.SetParent(canvasGo.transform, false);
            var bgRT = (RectTransform)bg.transform;
            bgRT.anchorMin = Vector2.zero; bgRT.anchorMax = Vector2.one;
            bgRT.offsetMin = Vector2.zero; bgRT.offsetMax = Vector2.zero;
            var bgImg = bg.AddComponent<Image>();
            bgImg.sprite = UiFactory.WhiteSprite();
            bgImg.color = _bgColor;
            bgImg.raycastTarget = false;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(bg.transform, false);
            var fillRT = (RectTransform)fillGo.transform;
            fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = new Vector2(1.5f, 1.5f);
            fillRT.offsetMax = new Vector2(-1.5f, -1.5f);
            _fill = fillGo.AddComponent<Image>();
            _fill.sprite = UiFactory.WhiteSprite();
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
