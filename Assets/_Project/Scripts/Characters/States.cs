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
            var cam = Camera.main;
            Vector3 fwd, right;
            if (cam != null)
            {
                fwd = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
                right = cam.transform.right; right.y = 0f; right.Normalize();
            }
            else
            {
                fwd = Vector3.forward;
                right = Vector3.right;
            }

            var worldDir = (right * input.x + fwd * input.y);
            if (worldDir.sqrMagnitude > 1f) worldDir.Normalize();

            var rb = Fighter.Body;
            var velocity = worldDir * Fighter.MoveSpeed;
            velocity.y = rb.linearVelocity.y;
            rb.linearVelocity = velocity;

            // поворот в сторону движения
            if (worldDir.sqrMagnitude > 0.0001f)
            {
                var targetRot = Quaternion.LookRotation(worldDir, Vector3.up);
                Fighter.transform.rotation = Quaternion.RotateTowards(
                    Fighter.transform.rotation, targetRot, Fighter.RotationSpeed * fdt);
            }
        }

        public override FighterState HandleCommand(InputCommand cmd) => CommonCommandRouter.Route(Fighter, cmd);
    }

    // ===== ATTACK =====
    public class AttackState : FighterState
    {
        private AttackData _attack;
        private float _elapsed;
        private enum Phase { Startup, Active, Recovery }
        private Phase _phase;

        public void Configure(AttackData attack)
        {
            _attack = attack;
        }

        public override void OnEnter()
        {
            if (_attack == null) _attack = Fighter.LightAttack;
            if (_attack == null)
            {
                // Не настроен AttackData — выходим в Idle, чтобы не упасть.
                Fighter.FSM.Change(Fighter.FSM.Get<IdleState>());
                return;
            }
            _elapsed = 0f;
            _phase = Phase.Startup;

            // Остановить горизонтальное движение (атака с места)
            var v = Fighter.Body.linearVelocity; v.x = 0f; v.z = 0f; Fighter.Body.linearVelocity = v;

            if (Fighter.Hurtbox != null)
            {
                Fighter.Hurtbox.IsBlocking = false;
                Fighter.Hurtbox.IsParryActive = false;
                Fighter.Hurtbox.HasIFrames = false;
            }
        }

        public override void Tick(float dt)
        {
            if (_attack == null) return;
            _elapsed += dt;
            switch (_phase)
            {
                case Phase.Startup:
                    if (_elapsed >= _attack.StartupSeconds)
                    {
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
            // На recovery разрешаем cancel в парирование/уклонение (фишка глубокой обороны)
            if (_phase == Phase.Recovery)
            {
                if (cmd.Type == CommandType.Parry) return Fighter.FSM.Get<ParryState>();
                if (cmd.Type == CommandType.Dodge)
                {
                    var dodge = Fighter.FSM.Get<DodgeState>();
                    dodge.Direction = cmd.Direction;
                    return dodge;
                }
            }
            return null;
        }
    }

    // ===== BLOCK =====
    public class BlockState : FighterState
    {
        public override void OnEnter()
        {
            if (Fighter.Hurtbox != null) Fighter.Hurtbox.IsBlocking = true;
            // Остановиться в блоке
            var v = Fighter.Body.linearVelocity; v.x = 0f; v.z = 0f; Fighter.Body.linearVelocity = v;
        }

        public override void OnExit()
        {
            if (Fighter.Hurtbox != null) Fighter.Hurtbox.IsBlocking = false;
        }

        public override FighterState HandleCommand(InputCommand cmd)
        {
            if (cmd.Type == CommandType.BlockEnd) return Fighter.FSM.Get<IdleState>();
            if (cmd.Type == CommandType.Parry) return Fighter.FSM.Get<ParryState>();
            if (cmd.Type == CommandType.Dodge)
            {
                var dodge = Fighter.FSM.Get<DodgeState>();
                dodge.Direction = cmd.Direction;
                return dodge;
            }
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
            // Подписываемся на успешное парирование, чтобы наградить игрока
            EventBus.Subscribe<AttackParriedEvent>(OnParried);
        }

        public override void Tick(float dt)
        {
            _elapsed += dt;
            if (_elapsed >= Fighter.ParryWindow)
                Fighter.FSM.Change(Fighter.FSM.Get<IdleState>());
        }

        public override void OnExit()
        {
            if (Fighter.Hurtbox != null) Fighter.Hurtbox.IsParryActive = false;
            EventBus.Unsubscribe<AttackParriedEvent>(OnParried);
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
        /// <summary> Направление уклонения (экранные XY, x=право, y=верх). </summary>
        public Vector2 Direction;
        private float _elapsed;
        private int _iframesLeft;

        public override void OnEnter()
        {
            // Проверка стамины
            if (Fighter.Stamina != null && !Fighter.Stamina.TrySpend(Fighter.DodgeStaminaCost))
            {
                Fighter.FSM.Change(Fighter.FSM.Get<IdleState>());
                return;
            }

            _elapsed = 0f;
            _iframesLeft = Fighter.DodgeIFrames;
            if (Fighter.Hurtbox != null) Fighter.Hurtbox.HasIFrames = true;

            var dir3 = new Vector3(Direction.x, 0f, Direction.y);
            if (dir3.sqrMagnitude < 0.0001f) dir3 = -Fighter.transform.forward; // дефолт — назад
            dir3.Normalize();
            Fighter.Body.linearVelocity = dir3 * Fighter.DodgeSpeed + Vector3.up * Fighter.Body.linearVelocity.y;
        }

        public override void Tick(float dt)
        {
            _elapsed += dt;
            // i-frames истекают через DodgeIFrames кадров (~ 60fps)
            _iframesLeft--;
            if (_iframesLeft <= 0 && Fighter.Hurtbox != null) Fighter.Hurtbox.HasIFrames = false;

            if (_elapsed >= Fighter.DodgeDuration)
                Fighter.FSM.Change(Fighter.FSM.Get<IdleState>());
        }

        public override void OnExit()
        {
            if (Fighter.Hurtbox != null) Fighter.Hurtbox.HasIFrames = false;
            var v = Fighter.Body.linearVelocity; v.x = 0f; v.z = 0f; Fighter.Body.linearVelocity = v;
        }
    }

    // ===== HITSTUN =====
    public class HitstunState : FighterState
    {
        private float _remaining;
        public void SetDuration(float seconds) => _remaining = seconds;

        public override void OnEnter()
        {
            if (Fighter.Hurtbox != null)
            {
                Fighter.Hurtbox.IsBlocking = false;
                Fighter.Hurtbox.IsParryActive = false;
            }
            if (Fighter.Hitbox != null) Fighter.Hitbox.Deactivate();
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
                    var atkL = f.FSM.Get<AttackState>();
                    atkL.Configure(f.LightAttack);
                    return atkL;
                case CommandType.HeavyAttack:
                    var atkH = f.FSM.Get<AttackState>();
                    atkH.Configure(f.HeavyAttack != null ? f.HeavyAttack : f.LightAttack);
                    return atkH;
                case CommandType.BlockStart:
                    return f.FSM.Get<BlockState>();
                case CommandType.Parry:
                    return f.FSM.Get<ParryState>();
                case CommandType.Dodge:
                    var dodge = f.FSM.Get<DodgeState>();
                    dodge.Direction = cmd.Direction;
                    return dodge;
            }
            return null;
        }
    }
}