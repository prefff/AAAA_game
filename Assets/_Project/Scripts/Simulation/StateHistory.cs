using System.Collections.Generic;

namespace Game.Simulation
{
    /// <summary>
    /// Кольцо снимков состояния по тикам — основа rollback: восстановить тик T и пересчитать вперёд с исправленным
    /// вводом. Снимки переиспользуются, в игре аллокаций нет.
    /// </summary>
    public sealed class StateHistory
    {
        private readonly GameState[] _states;

        public StateHistory(int capacity)
        {
            _states = new GameState[capacity];
        }

        public int Capacity => _states.Length;

        public void Save(GameState s)
        {
            int slot = s.Tick % _states.Length;
            (_states[slot] ??= new GameState()).CopyFrom(s);
        }

        /// <summary> Восстановить состояние на конец тика tick; false — его уже вытеснили или не было. </summary>
        public bool TryLoad(int tick, GameState into)
        {
            if (tick < 0) return false;
            var slot = _states[tick % _states.Length];
            if (slot == null || slot.Tick != tick) return false;
            into.CopyFrom(slot);
            return true;
        }
    }

    /// <summary> Запись матча: начальное состояние и вводы обоих игроков по тикам. Бой проигрывается из неё одинаково. </summary>
    public sealed class InputRecording
    {
        public readonly GameState Start;
        private readonly List<TickInput> _p0 = new();
        private readonly List<TickInput> _p1 = new();

        public InputRecording(GameState start)
        {
            Start = start.Clone();
        }

        public int Count => _p0.Count;

        public void Add(in TickInput p0, in TickInput p1)
        {
            _p0.Add(p0);
            _p1.Add(p1);
        }

        public TickInput Get(int index, int player) => player == 0 ? _p0[index] : _p1[index];

        /// <summary> Проиграть запись с начала; onTick получает состояние после каждого тика. </summary>
        public GameState Play(FightSimulation sim, System.Action<GameState> onTick = null)
        {
            var s = Start.Clone();
            for (int i = 0; i < _p0.Count; i++)
            {
                sim.Step(s, _p0[i], _p1[i]);
                onTick?.Invoke(s);
            }
            return s;
        }
    }
}
