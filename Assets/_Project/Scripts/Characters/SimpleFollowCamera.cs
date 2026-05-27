using UnityEngine;

namespace Game.Characters
{
    /// <summary>
    /// Простая изометрическая камера: фиксированный угол + плавное следование за целью.
    /// Без Cinemachine, чтобы прототип запускался "из коробки".
    /// Работает и в режиме редактора (ExecuteAlways) — удобно подбирать ракурс.
    /// </summary>
    [ExecuteAlways]
    public class SimpleFollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [Tooltip("Смещение камеры относительно цели в мировых координатах.")]
        [SerializeField] private Vector3 _offset = new(0f, 12f, -10f);
        [Tooltip("Точка, в которую смотрит камера (target.position + LookAtOffset).")]
        [SerializeField] private Vector3 _lookAtOffset = new(0f, 1f, 0f);
        [Tooltip("Скорость сглаживания позиции (в Play). В Edit-режиме применяется мгновенно.")]
        [SerializeField] private float _smooth = 8f;
        [Tooltip("Если цель не задана — искать Fighter с IsLocalPlayer.")]
        [SerializeField] private bool _autoFindLocalPlayer = true;

        public void SetTarget(Transform t) => _target = t;
        public Vector3 Offset { get => _offset; set => _offset = value; }

        private void OnEnable()
        {
            if (_target == null && _autoFindLocalPlayer) FindLocalPlayer();
            SnapToTarget();
        }

        private void Update()
        {
            // В edit-mode моментально следуем за целью (удобно подбирать offset в инспекторе).
            if (!Application.isPlaying)
            {
                if (_target == null && _autoFindLocalPlayer) FindLocalPlayer();
                SnapToTarget();
            }
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying) return;

            if (_target == null)
            {
                if (_autoFindLocalPlayer) FindLocalPlayer();
                if (_target == null) return;
            }

            var desired = _target.position + _offset;
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-_smooth * Time.deltaTime));
            transform.LookAt(_target.position + _lookAtOffset);
        }

        private void SnapToTarget()
        {
            if (_target == null) return;
            transform.position = _target.position + _offset;
            transform.LookAt(_target.position + _lookAtOffset);
        }

        private void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            if (_target == null && _autoFindLocalPlayer) FindLocalPlayer();
            SnapToTarget();
        }

        private void FindLocalPlayer()
        {
            var fighters = FindObjectsByType<Fighter>(FindObjectsSortMode.None);
            foreach (var f in fighters)
            {
                if (f.IsLocalPlayer) { _target = f.transform; return; }
            }
        }
    }
}