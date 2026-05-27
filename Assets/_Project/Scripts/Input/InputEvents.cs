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
    /// Команда от слоя ввода. Содержит тип и (опционально) направление в мировых/экранных координатах.
    /// Направление нужно для свайпов (dodge влево/вправо) и каста направленных способностей.
    /// </summary>
    public readonly struct InputCommand
    {
        public readonly CommandType Type;
        public readonly Vector2 Direction; // нормализованная, в экранных координатах (x=право, y=верх)
        public readonly float Timestamp;

        public InputCommand(CommandType type, Vector2 direction, float timestamp)
        {
            Type = type;
            Direction = direction;
            Timestamp = timestamp;
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