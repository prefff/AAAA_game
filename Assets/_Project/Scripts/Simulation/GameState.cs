using System;

namespace Game.Simulation
{
    /// <summary>
    /// Состояние бойца — только значения (структура), поэтому снимок для rollback — простое копирование.
    /// Всё, что влияет на будущее, обязано быть здесь: ни таймеров в MonoBehaviour, ни ссылок на объекты.
    /// </summary>
    public struct FighterSim
    {
        public FixVec2 Position;
        /// <summary> Куда смотрит боец (единичный вектор). </summary>
        public FixVec2 Facing;
        /// <summary> Отбрасывание, м/тик; гаснет с торможением. </summary>
        public FixVec2 Velocity;
        /// <summary> Джойстик в мировых осях, длина 0..1. </summary>
        public FixVec2 MoveInput;

        public ActionState State;
        /// <summary> Тиков в текущем состоянии (0 — тик входа). </summary>
        public int StateTicks;

        public AttackKind Attack;
        /// <summary> Удар уже сработал (попал, заблокирован, спарирован или прошёл сквозь i-frames) — второй раз не бьёт. </summary>
        public bool AttackResolved;
        /// <summary> Удар попал или заблокирован — можно отменить в следующий удар (комбо). </summary>
        public bool AttackConnected;

        /// <summary> Оставшийся hitstun / blockstun / оглушение после парирования. </summary>
        public int StunTicks;
        /// <summary> Стоп-кадр после контакта: таймеры и движение стоят. </summary>
        public int HitstopTicks;

        public FixVec2 DodgeDirection;
        public bool DodgePaid;

        /// <summary> Палец держит блок: после оглушения/удара боец сам возвращается в блок. </summary>
        public bool BlockHeld;

        public Fix Health;
        public Fix Stamina;
        public int StaminaRegenDelay;
        public Fix Mana;

        public SimCommand Buffered;
        /// <summary> Сколько ещё тиков буферная команда ждёт; 0 — буфер пуст. </summary>
        public int BufferTicks;

        /// <summary> Слот скилла в касте (State == Cast). </summary>
        public SkillSlot CastSlot;
        /// <summary> Скилл уже вышел — идёт recovery. </summary>
        public bool SkillFired;
        /// <summary> Прицел: направление × доля дальности; ноль — автоприцел. </summary>
        public FixVec2 SkillAim;
        public int Cooldown0;
        public int Cooldown1;
        public int Cooldown2;

        /// <summary> Сколько попаданий подряд получено в текущей серии (для затухания комбо). </summary>
        public int ComboHits;

        public bool IsAlive => State != ActionState.Dead;

        public int Cooldown(int slot) => slot switch { 0 => Cooldown0, 1 => Cooldown1, 2 => Cooldown2, _ => 0 };

        public void SetCooldown(int slot, int ticks)
        {
            switch (slot)
            {
                case 0: Cooldown0 = ticks; break;
                case 1: Cooldown1 = ticks; break;
                case 2: Cooldown2 = ticks; break;
            }
        }

        internal void Hash(ref StateHasher h)
        {
            h.Add(Position); h.Add(Facing); h.Add(Velocity); h.Add(MoveInput);
            h.Add((int)State); h.Add(StateTicks);
            h.Add((int)Attack); h.Add(AttackResolved); h.Add(AttackConnected);
            h.Add(StunTicks); h.Add(HitstopTicks);
            h.Add(DodgeDirection); h.Add(DodgePaid);
            h.Add(BlockHeld);
            h.Add(Health); h.Add(Stamina); h.Add(StaminaRegenDelay); h.Add(Mana);
            h.Add((int)Buffered.Kind); h.Add(Buffered.DirX); h.Add(Buffered.DirY); h.Add(Buffered.Id); h.Add(BufferTicks);
            h.Add((int)CastSlot); h.Add(SkillFired); h.Add(SkillAim);
            h.Add(Cooldown0); h.Add(Cooldown1); h.Add(Cooldown2);
            h.Add(ComboHits);
        }
    }

    /// <summary> Снаряд или область скилла на арене. Значение (структура) — снимок копированием, как у бойцов. </summary>
    public struct SkillObject
    {
        public SkillObjectKind Kind;
        /// <summary> Чей объект сейчас: кого он не задевает и кому засчитывается попадание (отражение меняет владельца). </summary>
        public int Owner;
        /// <summary> Чей это скилл: данные берутся из Setup.Fighters[Caster].Skill(Slot) и после отражения. </summary>
        public int Caster;
        public SkillSlot Slot;
        public FixVec2 Position;
        /// <summary> Снаряд: м/тик. </summary>
        public FixVec2 Velocity;
        /// <summary> Снаряд — до исчезновения, область — до взрыва. </summary>
        public int TicksLeft;
        /// <summary> Прошёл сквозь неуязвимость цели — больше её не задевает. </summary>
        public bool Evaded;

        public bool IsActive => Kind != SkillObjectKind.None;

        internal void Hash(ref StateHasher h)
        {
            h.Add((int)Kind);
            if (Kind == SkillObjectKind.None) return;
            h.Add(Owner); h.Add(Caster); h.Add((int)Slot); h.Add(Position); h.Add(Velocity); h.Add(TicksLeft); h.Add(Evaded);
        }
    }

    /// <summary> Полное состояние матча. CopyFrom — снимок/восстановление для rollback, ComputeHash — проверка рассинхрона. </summary>
    public sealed class GameState
    {
        public const int FighterCount = 2;

        public int Tick;
        public MatchPhase Phase;
        /// <summary> Сколько тиков осталось у отсчёта или паузы после раунда. </summary>
        public int PhaseTicks;
        public int RoundTicksLeft;
        public int Round;
        public int Wins0;
        public int Wins1;
        /// <summary> -1 — ничья или раунд ещё не закончен. </summary>
        public int LastRoundWinner = -1;
        public int MatchWinner = -1;

        public readonly FighterSim[] Fighters = new FighterSim[FighterCount];

        /// <summary> Снаряды и области. Фиксированный пул: без аллокаций, снимок — копирование массива. </summary>
        public const int MaxSkillObjects = 12;
        public readonly SkillObject[] Objects = new SkillObject[MaxSkillObjects];

        public int Wins(int fighter) => fighter == 0 ? Wins0 : Wins1;

        public void CopyFrom(GameState other)
        {
            Tick = other.Tick;
            Phase = other.Phase;
            PhaseTicks = other.PhaseTicks;
            RoundTicksLeft = other.RoundTicksLeft;
            Round = other.Round;
            Wins0 = other.Wins0;
            Wins1 = other.Wins1;
            LastRoundWinner = other.LastRoundWinner;
            MatchWinner = other.MatchWinner;
            Array.Copy(other.Fighters, Fighters, FighterCount);
            Array.Copy(other.Objects, Objects, MaxSkillObjects);
        }

        public GameState Clone()
        {
            var copy = new GameState();
            copy.CopyFrom(this);
            return copy;
        }

        public ulong ComputeHash()
        {
            var h = StateHasher.Create();
            h.Add(Tick); h.Add((int)Phase); h.Add(PhaseTicks); h.Add(RoundTicksLeft); h.Add(Round);
            h.Add(Wins0); h.Add(Wins1); h.Add(LastRoundWinner); h.Add(MatchWinner);
            for (int i = 0; i < FighterCount; i++) Fighters[i].Hash(ref h);
            for (int i = 0; i < MaxSkillObjects; i++) Objects[i].Hash(ref h);
            return h.Value;
        }
    }

    /// <summary> FNV-1a 64 по сырым значениям полей. </summary>
    public struct StateHasher
    {
        private const ulong Offset = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public ulong Value;

        public static StateHasher Create() => new() { Value = Offset };

        public void Add(long v)
        {
            for (int i = 0; i < 8; i++)
            {
                Value ^= (byte)(v >> (i * 8));
                Value *= Prime;
            }
        }

        public void Add(int v) => Add((long)v);
        public void Add(bool v) => Add(v ? 1L : 0L);
        public void Add(Fix v) => Add(v.Raw);
        public void Add(FixVec2 v) { Add(v.X.Raw); Add(v.Y.Raw); }
    }
}
