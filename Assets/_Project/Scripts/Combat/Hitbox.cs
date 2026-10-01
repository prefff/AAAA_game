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
        [SerializeField] private LayerMask _hurtboxLayers;
        [SerializeField] private bool _startInactive = true;
        [Tooltip("Логировать каждое попадание в Console.")]
        [SerializeField] private bool _debugLog = false;
        [Tooltip("Рисовать gizmo хитбокса в Scene-view (красный когда активен, серый когда нет).")]
        [SerializeField] private bool _debugDrawGizmo = true;

        /// <summary> Доля knockback, которая проходит через блок. </summary>
        private const float BlockKnockbackMultiplier = 0.3f;

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
            EnsureCollider();
            _collider.isTrigger = true;
            if (_hurtboxLayers == 0)
                _hurtboxLayers = LayerMask.GetMask("Hurtbox");
            if (_hurtboxLayers == 0)
                Debug.LogError("[Hitbox] Слой 'Hurtbox' не найден (Project Settings → Tags and Layers) — удары не будут регистрироваться.", this);
            if (_startInactive) _collider.enabled = false;
        }

        /// <summary> Программная настройка владельца (для процедурной сборки бойца). </summary>
        public void SetOwner(GameObject owner) => _owner = owner;

        /// <summary>
        /// Активировать хитбокс на time секунд с указанной AttackData.
        /// Вызывать из состояния атаки в начале Active-фазы; отключение — по таймеру или вручную.
        /// </summary>
        public void Activate(AttackData attack)
        {
            EnsureCollider();
            _currentAttack = attack;
            _alreadyHit.Clear();
            _collider.enabled = true;

            // Дополнительно: сразу же проверяем всех, кто уже внутри хитбокса
            // (OnTriggerEnter не сработает, если коллайдеры пересеклись ещё до активации).
            TryHitOverlapping();
        }

        public void Deactivate()
        {
            EnsureCollider();
            _collider.enabled = false;
            _currentAttack = null;
            _alreadyHit.Clear();
        }

        // Fighter (на корне) может вызвать Deactivate из OnEnable раньше, чем отработает Awake этого дочернего объекта.
        private void EnsureCollider()
        {
            if (_collider == null) _collider = GetComponent<Collider>();
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

            // --- 3) Блок: уменьшаем урон и knockback, вместо hitstun — blockstun ---
            if (hurt.IsBlocking)
            {
                float passed = _currentAttack.Damage * hurt.BlockDamageMultiplier;
                float blocked = _currentAttack.Damage - passed;
                if (hurt.Health != null && passed > 0f)
                    hurt.Health.TakeDamage(passed, _currentAttack, Owner, hitPoint);

                ApplyKnockback(other, hurt, _currentAttack.Knockback * BlockKnockbackMultiplier);
                EventBus.Raise(new HitstunRequestedEvent(hurt.Owner, _currentAttack.BlockstunFrames / 60f, isBlockstun: true));
                EventBus.Raise(new AttackBlockedEvent(Owner, hurt.Owner, blocked));
                return;
            }

            // --- 4) Полный урон ---
            if (hurt.Health != null)
            {
                hurt.Health.TakeDamage(_currentAttack.Damage, _currentAttack, Owner, hitPoint);
                if (_debugLog) Debug.Log($"[Hitbox] {Owner.name} нанёс {_currentAttack.Damage} урона по {hurt.Owner.name}. HP: {hurt.Health.Current}/{hurt.Health.Max}", hurt.Owner);
            }
            else
            {
                if (_debugLog) Debug.LogWarning($"[Hitbox] Попал по {hurt.Owner.name}, но у него нет Health!", hurt.Owner);
            }

            ApplyKnockback(other, hurt, _currentAttack.Knockback);
            if (hurt.Health == null || hurt.Health.IsAlive)
                EventBus.Raise(new HitstunRequestedEvent(hurt.Owner, _currentAttack.HitstunFrames / 60f, isBlockstun: false));
        }

        /// <summary> Отбросить цель от атакующего. speed — добавочная скорость в м/с (не зависит от массы). </summary>
        private void ApplyKnockback(Collider hurtCollider, Hurtbox hurt, float speed)
        {
            if (speed <= 0f) return;
            var rb = hurtCollider.attachedRigidbody;
            if (rb == null || rb.isKinematic) return;

            Vector3 dir = rb.position - Owner.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f)
                dir = Owner.transform.forward;
            dir.Normalize();

            var preVel = rb.linearVelocity;
            rb.AddForce(dir * speed, ForceMode.VelocityChange);
            if (_debugLog)
                Debug.Log($"[Hitbox] Knockback {hurt.Owner.name}: speed={speed:F2} dir={dir} preVel={preVel:F2}", hurt.Owner);
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
