using System;

namespace Game.Simulation
{
    /// <summary>
    /// Частота симуляции. Frame data задаётся в кадрах 60 Гц и переводится в тики здесь, поэтому переход
    /// на 120 Гц — смена одной константы (решение — по замерам на устройстве, вопрос 2 плана).
    /// </summary>
    public static class SimTime
    {
        public const int TickRate = 60;
        public const double TickSeconds = 1.0 / TickRate;

        /// <summary> Кадры 60 Гц → тики. </summary>
        public static int Frames(int frames60) => frames60 * TickRate / 60;

        /// <summary> Секунды → тики (округление). Только для данных. </summary>
        public static int Seconds(float seconds) => (int)Math.Round(seconds * (double)TickRate);

        /// <summary> Скорость, м/с → м/тик. Только для данных. </summary>
        public static Fix PerSecond(float perSecond) => Fix.FromFloat(perSecond) / TickRate;

        /// <summary> Ускорение, м/с² → (м/тик)/тик. Только для данных. </summary>
        public static Fix PerSecondSquared(float perSecondSq) => Fix.FromFloat(perSecondSq) / (TickRate * TickRate);
    }

    public enum CommandKind : byte
    {
        None,
        LightAttack,
        HeavyAttack,
        BlockStart,
        BlockEnd,
        Parry,
        Dodge,
        /// <summary> Начать матч заново (кнопка после конца матча). Идёт через ввод, чтобы в сети рестарт был синхронным. </summary>
        Restart,
        /// <summary>
        /// Скиллы по слотам. Направление команды — прицел: единичный вектор × доля дальности (0..1);
        /// ноль — быстрый каст (автоприцел). Каст стартует сразу, прицел уточняется до выхода скилла (см. TickInput.Aim).
        /// </summary>
        Skill1,
        Skill2,
        Ultimate,
        /// <summary> Отменить каст, пока скилл не вышел (и скилл в буфере). Мана и перезарядка не тратятся. </summary>
        SkillCancel,
    }

    /// <summary> Прицел скилла в вводе тика: как джойстик, непрерывный. </summary>
    public enum AimState : byte
    {
        /// <summary> Пальца на кнопке скилла нет (и бот): прицел берётся из команды скилла. </summary>
        None,
        /// <summary> Палец держит кнопку: прицел обновляется, каста ещё нет — боец свободен. </summary>
        Held,
        /// <summary> Палец отпущен в этом тике: последний прицел (команда скилла приходит в этом же тике). </summary>
        Released,
    }

    /// <summary>
    /// Команда игрока на тик. Направление — мировое (XZ), квантовано в sbyte: ввод должен передаваться по сети
    /// компактно и одинаково читаться на обеих сторонах. Id нужен только слою ввода (замер задержки).
    /// </summary>
    public readonly struct SimCommand
    {
        public readonly CommandKind Kind;
        public readonly sbyte DirX;
        public readonly sbyte DirY;
        public readonly ushort Id;

        public SimCommand(CommandKind kind, sbyte dirX = 0, sbyte dirY = 0, ushort id = 0)
        {
            Kind = kind;
            DirX = dirX;
            DirY = dirY;
            Id = id;
        }

        public static SimCommand Of(CommandKind kind, float dirX, float dirY, ushort id = 0) =>
            new(kind, TickInput.Quantize(dirX), TickInput.Quantize(dirY), id);

        /// <summary> Метка в DirX: прицел скилла не в команде, а в непрерывном вводе (палец на кнопке, TickInput.Aim). </summary>
        public const sbyte InputAimMarker = sbyte.MinValue; // Quantize даёт только −127..127

        /// <summary> Нажатие кнопки скилла пальцем: прицел придёт непрерывным вводом и уточняется до выхода скилла. </summary>
        public static SimCommand SkillWithInputAim(CommandKind kind, ushort id = 0) => new(kind, InputAimMarker, 0, id);

        public bool UsesInputAim => DirX == InputAimMarker;

        public FixVec2 Direction => UsesInputAim ? FixVec2.Zero : TickInput.Dequantize(DirX, DirY);
    }

    /// <summary> Ввод одного игрока на один тик: джойстик, прицел скилла и до 4 команд в порядке поступления. </summary>
    public struct TickInput
    {
        public const int MaxCommands = 4;

        public sbyte MoveX;
        public sbyte MoveY;
        /// <summary> Прицел скилла: мировое направление × доля дальности (0..1). Значим, только если Aim != None. </summary>
        public sbyte AimX;
        public sbyte AimY;
        public AimState Aim;
        public byte Count;
        private SimCommand _c0, _c1, _c2, _c3;

        public FixVec2 Move => Dequantize(MoveX, MoveY);
        public FixVec2 AimVector => Dequantize(AimX, AimY);

        public void SetMove(float x, float y)
        {
            MoveX = Quantize(x);
            MoveY = Quantize(y);
        }

        public void SetAim(AimState state, float x, float y)
        {
            Aim = state;
            AimX = Quantize(x);
            AimY = Quantize(y);
        }

        public SimCommand this[int i] => i switch
        {
            0 => _c0,
            1 => _c1,
            2 => _c2,
            3 => _c3,
            _ => throw new ArgumentOutOfRangeException(nameof(i)),
        };

        /// <summary> Добавить команду; false — в этом тике уже MaxCommands (остальные — в следующий тик). </summary>
        public bool Add(SimCommand cmd)
        {
            switch (Count)
            {
                case 0: _c0 = cmd; break;
                case 1: _c1 = cmd; break;
                case 2: _c2 = cmd; break;
                case 3: _c3 = cmd; break;
                default: return false;
            }
            Count++;
            return true;
        }

        public bool Contains(CommandKind kind)
        {
            for (int i = 0; i < Count; i++)
                if (this[i].Kind == kind) return true;
            return false;
        }

        public static sbyte Quantize(float v)
        {
            if (v > 1f) v = 1f;
            else if (v < -1f) v = -1f;
            return (sbyte)Math.Round(v * 127f);
        }

        public static FixVec2 Dequantize(sbyte x, sbyte y) =>
            new(Fix.FromRaw(x * Fix.OneRaw / 127), Fix.FromRaw(y * Fix.OneRaw / 127));
    }

    public enum ActionState : byte
    {
        Idle,
        Move,
        Attack,
        Block,
        Parry,
        /// <summary> Парирование не встретило удар — короткое окно уязвимости. </summary>
        ParryRecovery,
        Dodge,
        Hitstun,
        /// <summary> Удар спарирован: атакующий оглушён, у защитника окно контратаки. </summary>
        ParryStunned,
        Dead,
        /// <summary> Каст скилла: startup (прицел ещё уточняется, отменяется уклонением) → выход скилла → recovery. </summary>
        Cast,
        /// <summary> Стамина кончилась под ударами в блоке: блок пробит, боец оглушён. </summary>
        GuardBroken,
    }

    public enum AttackKind : byte { None, Light, Heavy }

    /// <summary> Слот скилла: два обычных и ультимейт. </summary>
    public enum SkillSlot : byte { Skill1, Skill2, Ultimate }

    public enum SkillKind : byte
    {
        /// <summary> Снаряд по направлению: летит до дальности, гаснет о препятствия, парирование отражает его. </summary>
        Projectile,
        /// <summary> Телепорт в точку (сквозь препятствия), с неуязвимостью в начале каста. </summary>
        Blink,
        /// <summary> Область в точке: видимое предупреждение, потом взрыв по радиусу. </summary>
        Zone,
    }

    /// <summary> Объект скилла на арене (снаряд или область). </summary>
    public enum SkillObjectKind : byte { None, Projectile, Zone }

    public enum AttackPhase : byte { None, Startup, Active, Recovery }

    public enum MatchPhase : byte
    {
        /// <summary> Отсчёт перед раундом: бойцы стоят на местах. </summary>
        Countdown,
        Fight,
        /// <summary> Пауза после KO / конца времени. </summary>
        RoundOver,
        MatchOver,
    }

    public enum SimEventType : byte
    {
        CommandAccepted,
        CommandDropped,
        Hit,
        Blocked,
        Parried,
        /// <summary> Удар прошёл сквозь неуязвимость уклонения. </summary>
        Evaded,
        KO,
        /// <summary> Блок пробит: стамина защитника кончилась. </summary>
        GuardBreak,
        /// <summary> Скилл вышел (Slot, Position — точка/начало). </summary>
        SkillFired,
        /// <summary> Снаряд погас о препятствие или край арены (Position). </summary>
        ProjectileExpired,
        /// <summary> Снаряд отражён парированием: Actor — новый владелец. </summary>
        ProjectileReflected,
        /// <summary> Область взорвалась (Position, Slot). </summary>
        ZoneDetonated,
        RoundStarted,
        RoundEnded,
        MatchEnded,
    }

    /// <summary>
    /// Событие тика для слоя отображения и отладки (вспышки, звуки, замер задержки). На состояние не влияет.
    /// Actor — кто действовал (атакующий; игрок команды), Target — по кому. Winner: -1 — ничья.
    /// FrameAdvantage — преимущество Actor в тиках после контакта (для Parried — преимущество защитника).
    /// Combo — номер попадания в серии (1 — первое). Slot — скилл (−1 — удар), Caster — чей это скилл;
    /// Position — где (для эффектов).
    /// </summary>
    public struct SimEvent
    {
        public SimEventType Type;
        public int Tick;
        public int Actor;
        public int Target;
        public CommandKind Command;
        public ushort CommandId;
        public Fix Amount;
        public int FrameAdvantage;
        public int Winner;
        public int Combo;
        public int Slot;
        /// <summary> Чей скилл Slot (у отражённого снаряда — не Actor). </summary>
        public int Caster;
        public FixVec2 Position;
    }
}
