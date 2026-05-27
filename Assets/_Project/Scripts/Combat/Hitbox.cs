using System.Collections.Generic;
using Game.Core;
using UnityEngine;

namespace Game.Combat
{
    /// <summary>
    /// Hitbox — атакующий объём. Активируется на время Active-кадров атаки.
    /// При пересечении с Hurtbox противника обрабатывает блок / парирование / iframe / урон.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Hitbox : MonoBehaviour
    {
        [SerializeField] private GameObject _owner;
        [SerializeField] private LayerMask _hurtboxLayers = ~0;
        [SerializeField] private bool _startInactive = true;
        [Tooltip("Логировать каждое попадание в Console.")]
        [SerializeField] private bool _debugLog = false;
        [Tooltip("Рисовать gizmo хитбокса в Scene-view (красный когда активен, серый когда нет).")]
        [SerializeField] private bool _debugDrawGizmo = true;

        private AttackData _currentAttack;
        private Collider _collider;
        private readonly HashSet<Hurtbox> _alreadyHit = new();

        public GameObject Owner => _owner != null ? _owner : transform.root.gameObject;
        public bool IsActive => _collider != null && _collider.enabled;

        private void Reset()
        {
            _owner = transform.root != null ? transform.root.gameObject : gameObject;
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            _collider.isTrigger = true;
            if (_startInactive) _collider.enabled = false;
        }

        /// <summary>
        /// Активировать хитбокс на time секунд с указанной AttackData.
        /// Вызывать из состояния атаки в начале Active-фазы; отключение — по таймеру или вручную.
        /// </summary>
        public void Activate(AttackData attack)
        {
            _currentAttack = attack;
            _alreadyHit.Clear();
            _collider.enabled = true;

            // Дополнительно: сразу же проверяем всех, кто уже внутри хитбокса
            // (OnTriggerEnter не сработает, если коллайдеры пересеклись ещё до активации).
            TryHitOverlapping();
        }

        public void Deactivate()
        {
            _collider.enabled = false;
            _currentAttack = null;
            _alreadyHit.Clear();
        }

        private void TryHitOverlapping()
        {
            // Используем Physics.Overlap по форме коллайдера.
            Collider[] overlaps = null;
            if (_collider is BoxCollider box)
            {
                var center = transform.TransformPoint(box.center);
                var halfExt = Vector3.Scale(box.size * 0.5f, transform.lossyScale);
                overlaps = Physics.OverlapBox(center, halfExt, transform.rotation, _hurtboxLayers, QueryTriggerInteraction.Collide);
            }
            else if (_collider is SphereCollider sph)
            {
                var center = transform.TransformPoint(sph.center);
                var scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
                overlaps = Physics.OverlapSphere(center, sph.radius * scale, _hurtboxLayers, QueryTriggerInteraction.Collide);
            }
            else if (_collider is CapsuleCollider cap)
            {
                var center = transform.TransformPoint(cap.center);
                var halfHeight = Mathf.Max(0f, cap.height * 0.5f - cap.radius) * transform.lossyScale.y;
                Vector3 axis = cap.direction == 0 ? transform.right : (cap.direction == 1 ? transform.up : transform.forward);
                var p1 = center + axis * halfHeight;
                var p2 = center - axis * halfHeight;
                var radius = cap.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.z);
                overlaps = Physics.OverlapCapsule(p1, p2, radius, _hurtboxLayers, QueryTriggerInteraction.Collide);
            }

            if (overlaps == null) return;
            foreach (var c in overlaps) TryApplyHit(c);
        }

        private void OnTriggerEnter(Collider other) => TryApplyHit(other);
        private void OnTriggerStay(Collider other) => TryApplyHit(other);

        private void TryApplyHit(Collider other)
        {
            if (!IsActive || _currentAttack == null) return;
            if ((_hurtboxLayers.value & (1 << other.gameObject.layer)) == 0) return;

            var hurt = other.GetComponent<Hurtbox>();
            if (hurt == null) return;
            if (hurt.Owner == Owner) return;          // не бьём себя
            if (_alreadyHit.Contains(hurt)) return;   // один удар = одно срабатывание
            _alreadyHit.Add(hurt);

            var hitPoint = other.ClosestPoint(transform.position);

            // --- 1) Парирование имеет высший приоритет ---
            if (hurt.IsParryActive)
            {
                EventBus.Raise(new AttackParriedEvent(Owner, hurt.Owner, hitPoint));
                return;
            }

            // --- 2) i-frames (уклонение): полный игнор ---
            if (hurt.HasIFrames) return;

            // --- 3) Блок: уменьшаем урон ---
            if (hurt.IsBlocking)
            {
                float blocked = _currentAttack.Damage * (1f - hurt.BlockDamageMultiplier);
                float passed = _currentAttack.Damage - blocked;
                if (hurt.Health != null && passed > 0f)
                    hurt.Health.TakeDamage(passed, Owner, hitPoint);
                EventBus.Raise(new AttackBlockedEvent(Owner, hurt.Owner, blocked));
                return;
            }

            // --- 4) Полный урон ---
            if (hurt.Health != null)
            {
                hurt.Health.TakeDamage(_currentAttack.Damage, Owner, hitPoint);
                if (_debugLog) Debug.Log($"[Hitbox] {Owner.name} нанёс {_currentAttack.Damage} урона по {hurt.Owner.name}. HP: {hurt.Health.Current}/{hurt.Health.Max}", hurt.Owner);
            }
            else
            {
                if (_debugLog) Debug.LogWarning($"[Hitbox] Попал по {hurt.Owner.name}, но у него нет Health!", hurt.Owner);
            }
        }

        private void OnDrawGizmos()
        {
            if (!_debugDrawGizmo) return;
            var col = _collider != null ? _collider : GetComponent<Collider>();
            if (col == null) return;

            Gizmos.color = (Application.isPlaying && IsActive) ? new Color(1f, 0f, 0f, 0.7f) : new Color(0.5f, 0.5f, 0.5f, 0.4f);

            var prev = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
            if (col is BoxCollider box)
                Gizmos.DrawWireCube(box.center, box.size);
            else if (col is SphereCollider sph)
                Gizmos.DrawWireSphere(sph.center, sph.radius);
            else if (col is CapsuleCollider cap)
                Gizmos.DrawWireSphere(cap.center, cap.radius);
            Gizmos.matrix = prev;
        }
    }
}
