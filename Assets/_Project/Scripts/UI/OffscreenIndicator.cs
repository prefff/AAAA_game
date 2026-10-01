using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Стрелка у края экрана в сторону противника, когда он за пределами видимой области (камера держит своего
    /// бойца, арена больше экрана). Без неё соперник, ушедший за край, становится невидимым.
    /// </summary>
    public class OffscreenIndicator : MonoBehaviour
    {
        [Tooltip("Отступ от края экрана (доля), внутри которого противник считается видимым.")]
        [SerializeField, Range(0f, 0.2f)] private float _margin = 0.06f;
        [SerializeField] private float _size = 70f;
        [Tooltip("Точка на теле противника, по которой проверяется видимость (над землёй).")]
        [SerializeField] private float _targetHeight = 1f;

        private Transform _target;
        private Func<bool> _isActive;
        private RectTransform _arrow;
        private Image _image;

        public bool IsShown => _image != null && _image.enabled;
        /// <summary> Позиция стрелки во вьюпорте (0..1). </summary>
        public Vector2 ViewportPosition { get; private set; }

        public void Bind(Transform target, Color color, Func<bool> isActive)
        {
            _target = target;
            _isActive = isActive;
            EnsureArrow();
            _image.color = color;
        }

        private void EnsureArrow()
        {
            if (_arrow != null) return;
            _arrow = UiFactory.Rect("Arrow", transform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(_size, _size));
            _image = _arrow.gameObject.AddComponent<Image>();
            _image.sprite = UiFactory.ArrowSprite();
            _image.raycastTarget = false;
            _image.enabled = false;
        }

        private void LateUpdate()
        {
            var cam = Camera.main;
            if (_target == null || cam == null || _arrow == null) return;

            var vp = cam.WorldToViewportPoint(_target.position + Vector3.up * _targetHeight);
            bool behind = vp.z < 0f;
            var p = new Vector2(vp.x, vp.y);
            if (behind) p = Vector2.one - p; // за камерой проекция зеркальна

            float lo = _margin, hi = 1f - _margin;
            bool inside = !behind && p.x >= lo && p.x <= hi && p.y >= lo && p.y <= hi;
            bool show = !inside && (_isActive == null || _isActive());
            if (_image.enabled != show) _image.enabled = show;
            if (!show) return;

            // Луч из центра экрана к противнику, обрезанный по рамке с отступом.
            var dir = p - new Vector2(0.5f, 0.5f);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector2.down;
            float k = (0.5f - _margin) / Mathf.Max(Mathf.Abs(dir.x), Mathf.Abs(dir.y));
            var edge = new Vector2(0.5f, 0.5f) + dir * k;
            ViewportPosition = edge;

            _arrow.anchorMin = _arrow.anchorMax = edge;
            _arrow.anchoredPosition = Vector2.zero;
            _arrow.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        }
    }
}
