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
        Ultimate,
    }

    public enum SkillAimPhase
    {
        /// <summary> Палец на кнопке скилла: прицел обновляется. </summary>
        Held,
        /// <summary> Палец отпущен: команда скилла уходит по последнему прицелу. </summary>
        Released,
        /// <summary> Палец отпущен в зоне отмены (или касание прервано): скилла не будет. </summary>
        Canceled,
    }

    /// <summary>
    /// Прицел скилла — непрерывный ввод, как джойстик: публикуется при нажатии, каждом сдвиге и отпускании пальца.
    /// Aim — мировое направление × доля дальности (0..1); ноль — автоприцел (быстрый каст).
    /// </summary>
    public readonly struct SkillAimInputEvent
    {
        public readonly int Slot;
        public readonly SkillAimPhase Phase;
        public readonly Vector2 Aim;
        public readonly bool InCancelZone;

        public SkillAimInputEvent(int slot, SkillAimPhase phase, Vector2 aim, bool inCancelZone)
        {
            Slot = slot;
            Phase = phase;
            Aim = aim;
            InCancelZone = inCancelZone;
        }
    }

    /// <summary>
    /// Команда от слоя ввода. Содержит тип и (опционально) направление.
    /// Направление нужно для свайпов (dodge) и каста направленных способностей; из экрана в мир его переводит
    /// слой ввода, поэтому бой (а позже детерминированная симуляция) не зависит от камеры.
    /// </summary>
    public readonly struct InputCommand
    {
        public readonly CommandType Type;
        /// <summary>
        /// Мировое направление в плоскости XZ (x = мировой X, y = мировой Z); ноль — без направления. У уклонения
        /// единичное, у скилла — прицел (направление × доля дальности).
        /// </summary>
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