using Game.Combat;
using Game.Core;
using Game.Input;
using UnityEngine;

namespace Game.Characters
{
    // ===== IDLE =====
    public class IdleState : FighterState
    {
        public override void OnEnter()
        {
            if (Fighter.Hurtbox != null)
            {
                Fighter.Hurtbox.IsBlocking = false;
                Fighter.Hurtbox.IsParryActive = false;
                Fighter.Hurtbox.HasIFrames = false;
            }
            if (Fighter.Hitbox != null) Fighter.Hitbox.Deactivate();
            // Гасим остаточную скорость от бега / уклонения / knockback, чтобы боец не скользил.
            Fighter.StopHorizontal();
        }

        public override void Tick(float dt)
        {
            if (Fighter.MoveInput.sqrMagnitude > 0.0001f)
                Fighter.FSM.Change(Fighter.FSM.Get<MoveState>());
        }

        public override FighterState HandleCommand(InputCommand cmd) => CommonCommandRouter.Route(Fighter, cmd);
    }

    // ===== MOVE =====
    public class MoveState : FighterState
    {
        public override void FixedTick(float fdt)
        {
            var input = Fighter.MoveInput;
            if (input.sqrMagnitude < 0.0001f)
            {
                Fighter.FSM.Change(Fighter.FSM.Get<IdleState>());
                return;
            }

            // Camera-relative движение: "вверх по джойстику" = от камеры.
            var worldDir = Fighter.ToWorldDirection(input);
            if (worldDir.sqrMagnitude > 1f) worldDir.Normalize();

            var rb = Fighter.Body;
            var velocity = worldDir * Fighter.MoveSpeed;
            velocity.y = rb.linearVelocity.y;
            rb.linearVelocity = velocity;

            // поворот в сторону движения
            if (worldDir.sqrMagnitude > 0.0001f)
            {
                var targetRot = Quaternion.LookRotation(worldDir, Vector3.up);
                rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRot, Fighter.RotationSpeed * fdt));
            }
        }

        public override FighterState HandleCommand(InputCommand cmd) => CommonCommandRouter.Route(Fighter, cmd);
    }

    // ===== ATTACK =====
    /// <summary>
    /// Удар по frame data. Startup — окно распознавания жеста: в нём удар бесплатно (стамина списывается только
    /// при выходе хитбокса) отменяется в уклонение, парирование, блок или тяжёлый удар.
    /// </summary>
    public class AttackState : FighterState
    {
        public enum Phase { Startup, Active, Recovery }

        private AttackData _attack;
        private AttackData _pending;
        private float _elapsed;
        private Phase _phase;

        public AttackData Attack => _attack;
        public Phase CurrentPhase => _phase;

        /// <summary> Задать атаку перед входом. Наличие стамины проверяется заранее (см. CommonCommandRouter). </summary>
        public void Configure(AttackData attack)
        {
            _pending = attack;
        }

        public override void OnEnter()
        {
            _attack = _pending != null ? _pending : Fighter.LightAttack;
            _pending = null;

            _elapsed = 0f;
            _phase = Phase.Startup;

            // Остановить горизонтальное движение (атака с места)
            Fighter.StopHorizontal();

            if (Fighter.Hurtbox != null)
            {
                Fighter.Hurtbox.IsBlocking = false;
                Fighter.Hurtbox.IsParryActive = false;
                Fighter.Hurtbox.HasIFrames = false;
            }
        }

        public override void Tick(float dt)
        {
            if (_attack == null)
            {
                // Не настроен AttackData — выходим в Idle, чтобы не зависнуть.
                Fighter.FSM.Change(Fighter.FSM.Get<IdleState>());
                return;
            }

            _elapsed += dt;
            switch (_phase)
            {
                case Phase.Startup:
                    if (_elapsed >= _attack.StartupSeconds)
                    {
                        // Платим за удар, только когда он состоялся: отмена в startup бесплатна.
                        if (Fighter.Stamina != null && !Fighter.Stamina.TrySpend(_attack.StaminaCost))
                        {
                            Fighter.FSM.Change(Fighter.FSM.Get<IdleState>());
                            return;
                        }
                        if (Fighter.Hitbox != null) Fighter.Hitbox.Activate(_attack);
                        _phase = Phase.Active;
                        _elapsed = 0f;
                    }
                    break;
                case Phase.Active:
                    if (_elapsed >= _attack.ActiveSeconds)
                    {
                        if (Fighter.Hitbox != null) Fighter.Hitbox.Deactivate();
                        _phase = Phase.Recovery;
                        _elapsed = 0f;
                    }
                    break;
                case Phase.Recovery:
                    if (_elapsed >= _attack.RecoverySeconds)
                    {
                        Fighter.FSM.Change(Fighter.FSM.Get<IdleState>());
                    }
                    break;
            }
        }

        public override void OnExit()
        {
            if (Fighter.Hitbox != null) Fighter.Hitbox.Deactivate();
            _attack = null;
        }

        public override FighterState HandleCommand(InputCommand cmd)
        {
            switch (_phase)
            {
                // Startup — окно распознавания жеста: касание уже запустило удар, продолжение жеста его уточняет.
                case Phase.Startup:
                    switch (cmd.Type)
                    {
                        case CommandType.Dodge: return CommonCommandRouter.TryDodge(Fighter, cmd.Direction);
                        case CommandType.Parry: return Fighter.FSM.Get<ParryState>();
                        case CommandType.BlockStart: return Fighter.FSM.Get<BlockState>();
                        case CommandType.HeavyAttack:
                            // Лёгкий удар → тяжёлый (вернётся этот же AttackState — FSM перезапустит его).
                            return _attack != Fighter.HeavyAttack ? CommonCommandRouter.TryAttack(Fighter, Fighter.HeavyAttack) : null;
                    }
                    return null;

                // На recovery разрешаем cancel в парирование/уклонение (фишка глубокой обороны)
                case Phase.Recovery:
                    if (cmd.Type == CommandType.Parry) return Fighter.FSM.Get<ParryState>();
                    if (cmd.Type == CommandType.Dodge) return CommonCommandRouter.TryDodge(Fighter, cmd.Direction);
                    return null;
            }
            return null;
        }
    }

    // ===== BLOCK =====
    public class BlockState : FighterState
    {
        private float _blockstunRemaining;
        private bool _releaseQueued;

        public override void OnEnter()
        {
            _blockstunRemaining = 0f;
            _releaseQueued = false;
            if (Fighter.Hurtbox != null) Fighter.Hurtbox.IsBlocking = true;
            // Остановиться в блоке
            Fighter.StopHorizontal();
        }

        /// <summary> Удар в блок: остаёмся в блоке, но на время блок-стана не можем действовать. </summary>
        public void ApplyBlockstun(float seconds)
        {
            _blockstunRemaining = Mathf.Max(_blockstunRemaining, seconds);
        }

        public override void Tick(float dt)
        {
            if (_blockstunRemaining <= 0f) return;
            _blockstunRemaining -= dt;
            if (_blockstunRemaining <= 0f && _releaseQueued)
                Fighter.FSM.Change(Fighter.FSM.Get<IdleState>());
        }

        public override void OnExit()
        {
            if (Fighter.Hurtbox != null) Fighter.Hurtbox.IsBlocking = false;
        }

        public override FighterState HandleCommand(InputCommand cmd)
        {
            if (_blockstunRemaining > 0f)
            {
                // Отпускание блока запоминаем и применяем, когда блок-стан закончится.
                if (cmd.Type == CommandType.BlockEnd) _releaseQueued = true;
                return null;
            }

            if (cmd.Type == CommandType.BlockEnd) return Fighter.FSM.Get<IdleState>();
            if (cmd.Type == CommandType.Parry) return Fighter.FSM.Get<ParryState>();
            if (cmd.Type == CommandType.Dodge) return CommonCommandRouter.TryDodge(Fighter, cmd.Direction);
            return null;
        }
    }

    // ===== PARRY =====
    public class ParryState : FighterState
    {
        private float _elapsed;

        public override void OnEnter()
        {
            _elapsed = 0f;
            if (Fighter.Hurtbox != null)
            {
                Fighter.Hurtbox.IsParryActive = true;
                Fighter.Hurtbox.IsBlocking = false;
            }
            // Подписка живёт только пока активно окно парирования; OnExit гарантированно отписывает.
            EventBus.Subscribe<AttackParriedEvent>(this, OnParried);
        }

        public override void Tick(float dt)
        {
            _elapsed += dt;
            if (_elapsed >= Fighter.ParryWindow)
                Fighter.FSM.Change(Fighter.FSM.Get<IdleState>());
        }

        public override void OnExit()
        {
            EventBus.UnsubscribeAll(this);
            if (Fighter.Hurtbox != null) Fighter.Hurtbox.IsParryActive = false;
        }

        private void OnParried(AttackParriedEvent e)
        {
            if (e.Defender != Fighter.gameObject) return;
            // TODO: дать ресурс ульты, hit-pause, замедление времени
        }
    }

    // ===== DODGE =====
    public class DodgeState : FighterState
    {
        /// <summary> Направление уклонения в мире (x = X, y = Z) — его уже перевёл из экрана слой ввода. </summary>
        public Vector2 Direction;
        private Vector3 _worldDir;
        private float _elapsed;
        private bool _staminaSpent;

        public override void OnEnter()
        {
            _staminaSpent = Fighter.Stamina != null && Fighter.Stamina.TrySpend(Fighter.DodgeStaminaCost);

            _elapsed = 0f;
            if (Fighter.Hurtbox != null) Fighter.Hurtbox.HasIFrames = true;

            _worldDir = new Vector3(Direction.x, 0f, Direction.y);
            if (_worldDir.sqrMagnitude < 0.0001f) _worldDir = -Fighter.transform.forward; // дефолт — назад
            _worldDir.Normalize();
        }

        public override FighterState HandleCommand(InputCommand cmd)
        {
            // Flick распознаётся при отпускании, а сдвиг пальца уже запустил уклонение: это не намерение игрока,
            // а этап распознавания — отменяем в парирование и возвращаем стамину.
            if (cmd.Type == CommandType.Parry && _elapsed <= Fighter.DodgeToParryCancelWindow)
            {
                if (_staminaSpent) Fighter.Stamina.Restore(Fighter.DodgeStaminaCost);
                _staminaSpent = false;
                return Fighter.FSM.Get<ParryState>();
            }
            return null;
        }

        public override void Tick(float dt)
        {
            _elapsed += dt;
            // i-frames считаем во времени, а не в кадрах рендера — иначе на 120 Гц окно сокращается вдвое.
            if (_elapsed >= Fighter.DodgeIFrameSeconds && Fighter.Hurtbox != null) Fighter.Hurtbox.HasIFrames = false;

            if (_elapsed >= Fighter.DodgeDuration)
                Fighter.FSM.Change(Fighter.FSM.Get<IdleState>());
        }

        public override void FixedTick(float fdt)
        {
            // Держим скорость рывка весь DodgeDuration (иначе трение гасит его за пару кадров).
            var rb = Fighter.Body;
            rb.linearVelocity = _worldDir * Fighter.DodgeSpeed + Vector3.up * rb.linearVelocity.y;
        }

        public override void OnExit()
        {
            if (Fighter.Hurtbox != null) Fighter.Hurtbox.HasIFrames = false;
            Fighter.StopHorizontal();
        }
    }

    // ===== HITSTUN =====
    public class HitstunState : FighterState
    {
        private float _remaining;

        /// <summary> Задать/обновить длительность. Повторное попадание во время hitstun продлевает его. </summary>
        public void SetDuration(float seconds) => _remaining = seconds;

        public override void OnEnter()
        {
            if (Fighter.Hurtbox != null)
            {
                Fighter.Hurtbox.IsBlocking = false;
                Fighter.Hurtbox.IsParryActive = false;
            }
            if (Fighter.Hitbox != null) Fighter.Hitbox.Deactivate();
            // Скорость не трогаем — knockback от удара должен доиграть.
        }

        public override void Tick(float dt)
        {
            _remaining -= dt;
            if (_remaining <= 0f) Fighter.FSM.Change(Fighter.FSM.Get<IdleState>());
        }
    }

    // ===== Общая маршрутизация команд (для Idle/Move) =====
    internal static class CommonCommandRouter
    {
        public static FighterState Route(Fighter f, InputCommand cmd)
        {
            switch (cmd.Type)
            {
                case CommandType.LightAttack:
                    return TryAttack(f, f.LightAttack);
                case CommandType.HeavyAttack:
                    return TryAttack(f, f.HeavyAttack != null ? f.HeavyAttack : f.LightAttack);
                case CommandType.BlockStart:
                    return f.FSM.Get<BlockState>();
                case CommandType.Parry:
                    return f.FSM.Get<ParryState>();
                case CommandType.Dodge:
                    return TryDodge(f, cmd.Direction);
            }
            return null;
        }

        /// <summary> Атака, если она настроена и хватает стамины; иначе null (команда игнорируется). </summary>
        public static FighterState TryAttack(Fighter f, AttackData attack)
        {
            if (attack == null || !CanAfford(f, attack.StaminaCost)) return null;
            var state = f.FSM.Get<AttackState>();
            state.Configure(attack);
            return state;
        }

        /// <summary> Уклонение, если хватает стамины; иначе null (команда игнорируется). </summary>
        public static FighterState TryDodge(Fighter f, Vector2 direction)
        {
            if (!CanAfford(f, f.DodgeStaminaCost)) return null;
            var state = f.FSM.Get<DodgeState>();
            state.Direction = direction;
            return state;
        }

        private static bool CanAfford(Fighter f, float cost) => f.Stamina == null || f.Stamina.HasEnough(cost);
    }
}
