using UnityEngine;

namespace Game.Combat
{
    /// <summary>
    /// Hurtbox — уязвимая зона сущности. Висит на дочерних коллайдерах (триггерах) бойца.
    /// Содержит ссылку на корневой Fighter, флаги защиты (block / parry / iframes),
    /// чтобы Hitbox при попадании знал, что делать.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Hurtbox : MonoBehaviour
    {
        [Tooltip("Корневой объект бойца (для обработки урона / отправки событий).")]
        [SerializeField] private GameObject _owner;
        [SerializeField] private Health _health;

        public GameObject Owner => _owner != null ? _owner : gameObject;
        public Health Health => _health;

        // Флаги выставляются состояниями персонажа (BlockState, ParryState, DodgeState).
        public bool IsBlocking { get; set; }
        public bool IsParryActive { get; set; }
        public bool HasIFrames { get; set; }

        /// <summary> Множитель урона при блоке (0..1). </summary>
        [Range(0f, 1f)] public float BlockDamageMultiplier = 0.3f;

        private void Reset()
        {
            _owner = transform.root != null ? transform.root.gameObject : gameObject;
            _health = GetComponentInParent<Health>();
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
        }
    }
}