using UnityEngine;

namespace Game.Characters
{
    /// <summary>
    /// Камера как в MLBB: закреплена на своём персонаже, фиксированный наклон и угол, не вращается.
    /// Следует жёстко, без сглаживания: отставание камеры ощущается как задержка управления.
    /// Второй игрок (красная сторона) по умолчанию видит арену развёрнутой на 180°, чтобы его база была снизу экрана;
    /// направления ввода всё равно переводятся в мир по этой камере (<see cref="Game.Input.InputSpace"/>).
    /// Работает и в редакторе без Play — удобно подбирать ракурс.
    /// </summary>
    [ExecuteAlways]
    [DefaultExecutionOrder(200)] // после видов бойцов
    public sealed class MobaCamera : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [Tooltip("Наклон камеры, градусы от горизонта.")]
        [SerializeField, Range(20f, 90f)] private float _pitch = 48f;
        [Tooltip("Расстояние от точки взгляда, м.")]
        [SerializeField, Min(1f)] private float _distance = 15f;
        [Tooltip("Поворот вокруг вертикали. 0 — синяя сторона, 180 — красная.")]
        [SerializeField] private float _yaw;
        [Tooltip("Куда смотреть относительно цели (уровень груди).")]
        [SerializeField] private Vector3 _lookAtOffset = new(0f, 1f, 0f);
        [Tooltip("Разворачивать камеру на 180° для второго игрока (как красная сторона в MLBB).")]
        [SerializeField] private bool _rotateForSecondPlayer = true;

        public Transform Target => _target;
        public float Yaw => _yaw;

        public void SetTarget(Transform target)
        {
            _target = target;
            Snap();
        }

        /// <summary> Сторона игрока: 0 — смотрим на +Z, 1 — на −Z (если включён разворот). </summary>
        public void SetSide(int playerIndex)
        {
            _yaw = playerIndex == 1 && _rotateForSecondPlayer ? 180f : 0f;
            Snap();
        }

        private void LateUpdate() => Snap();

        private void OnValidate() => Snap();

        private void Snap()
        {
            if (_target == null) return;
            var rot = Quaternion.Euler(_pitch, _yaw, 0f);
            var focus = _target.position + _lookAtOffset;
            transform.SetPositionAndRotation(focus - rot * Vector3.forward * _distance, rot);
        }
    }
}
