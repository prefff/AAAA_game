using Game.Input;

namespace Game.Characters
{
    /// <summary>
    /// Буфер ввода на одну команду: команда, которую боец не смог принять (recovery, hitstun, активная фаза удара),
    /// хранится WindowSeconds и выполняется в первый кадр, когда это разрешено. Новая команда вытесняет старую —
    /// выполняется последнее намерение игрока. Время передаётся явно (тестируется без сцены).
    /// </summary>
    public sealed class InputBuffer
    {
        public double WindowSeconds;

        private InputCommand _command;
        private double _expiresAt;

        public bool HasCommand { get; private set; }
        public InputCommand Command => _command;

        public InputBuffer(double windowSeconds) => WindowSeconds = windowSeconds;

        /// <summary> Можно ли буферизовать команду. Отпускание блока не буферизуется — оно отменяет буферный блок. </summary>
        public static bool IsBufferable(CommandType type) =>
            type == CommandType.LightAttack || type == CommandType.HeavyAttack || type == CommandType.Dodge ||
            type == CommandType.Parry || type == CommandType.BlockStart;

        /// <summary> Положить отклонённую команду. Возвращает вытесненную (если была), чтобы учесть её как отброшенную. </summary>
        public bool Push(InputCommand cmd, double now, out InputCommand replaced)
        {
            replaced = _command;
            bool hadOld = HasCommand;
            _command = cmd;
            _expiresAt = now + WindowSeconds;
            HasCommand = WindowSeconds > 0.0;
            return hadOld;
        }

        /// <summary> Убрать команду, если её срок вышел. Возвращает true и просроченную команду. </summary>
        public bool Expire(double now, out InputCommand expired)
        {
            expired = _command;
            if (!HasCommand || now <= _expiresAt) return false;
            HasCommand = false;
            return true;
        }

        public void Clear() => HasCommand = false;
    }
}
