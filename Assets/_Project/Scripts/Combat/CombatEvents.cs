using UnityEngine;

namespace Game.Combat
{
    /// <summary> Урон успешно нанесён. </summary>
    public readonly struct DamageDealtEvent
    {
        public readonly GameObject Attacker;
        public readonly GameObject Target;
        public readonly float Damage;
        public readonly Vector3 HitPoint;

        public DamageDealtEvent(GameObject attacker, GameObject target, float damage, Vector3 hitPoint)
        {
            Attacker = attacker;
            Target = target;
            Damage = damage;
            HitPoint = hitPoint;
        }
    }

    /// <summary> Удар заблокирован (поднята защита). </summary>
    public readonly struct AttackBlockedEvent
    {
        public readonly GameObject Attacker;
        public readonly GameObject Defender;
        public readonly float BlockedDamage;

        public AttackBlockedEvent(GameObject attacker, GameObject defender, float blockedDamage)
        {
            Attacker = attacker;
            Defender = defender;
            BlockedDamage = blockedDamage;
        }
    }

    /// <summary> Удар успешно спарирован — атакующий уходит в recovery, защитник получает окно для контратаки. </summary>
    public readonly struct AttackParriedEvent
    {
        public readonly GameObject Attacker;
        public readonly GameObject Defender;
        public readonly Vector3 HitPoint;

        public AttackParriedEvent(GameObject attacker, GameObject defender, Vector3 hitPoint)
        {
            Attacker = attacker;
            Defender = defender;
            HitPoint = hitPoint;
        }
    }

    /// <summary> Сущность умерла. </summary>
    public readonly struct DeathEvent
    {
        public readonly GameObject Entity;
        public DeathEvent(GameObject entity) => Entity = entity;
    }
}