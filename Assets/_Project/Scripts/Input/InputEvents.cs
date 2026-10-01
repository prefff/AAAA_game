using UnityEngine;

namespace Game.Input
{
    /// <summary> Команды, выдаваемые слоем ввода. Состояния персонажа подписываются на них. </summary>
    public enum CommandType
    {
        None,
        LightAttack,
        HeavyAttack,
        BlockStart,
        BlockEnd,
        Parry,
        Dodge,
        Ability1,
        Ability2,
        Ultimate
    }

    /// <summary>
    /// Команда от слоя ввода. Содержит тип и (опционально) направление.
    /// Направление нужно для свайпов (dodge) и каста направленных способностей; из экрана в мир его переводит
    /// слой ввода, поэтому бой (а позже детерминированная симуляция) не зависит от камеры.
    /// </summary>
    public readonly struct InputCommand
    {
        public readonly CommandType Type;
        /// <summary> Нормализованное мировое направление в плоскости XZ (x = мировой X, y = мировой Z); ноль — без направления. </summary>
        public readonly Vector2 Direction;
        public readonly float Timestamp;
        /// <summary> Начало действия игрока (касание; для BlockEnd — отпускание). Шкала Time.realtimeSinceStartupAsDouble; 0 — неизвестно. </summary>
        public readonly double InputTime;
        /// <summary> Событие ввода, на котором жест распознан (та же шкала); 0 — неизвестно. </summary>
        public readonly double RecognizedTime;

        public InputCommand(CommandType type, Vector2 direction, float timestamp,
                            double inputTime = 0.0, double recognizedTime = 0.0)
        {
            Type = type;
            Direction = direction;
            Timestamp = timestamp;
            InputTime = inputTime;
            RecognizedTime = recognizedTime;
        }
    }

    /// <summary> Изменилось значение левого джойстика (движение). </summary>
    public readonly struct MoveInputEvent
    {
        public readonly Vector2 Direction; // -1..1 по обоим осям
        public MoveInputEvent(Vector2 direction) => Direction = direction;
    }

    /// <summary> Игрок выдал команду (жест распознан). </summary>
    public readonly struct CommandInputEvent
    {
        public readonly InputCommand Command;
        public CommandInputEvent(InputCommand command) => Command = command;
    }
}