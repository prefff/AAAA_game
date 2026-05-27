using System.Collections.Generic;

namespace Game.Characters
{
    /// <summary> Базовое состояние конечного автомата. </summary>
    public abstract class FighterState
    {
        protected Fighter Fighter;

        public virtual void Bind(Fighter fighter) { Fighter = fighter; }

        public virtual void OnEnter() { }
        public virtual void Tick(float dt) { }
        public virtual void FixedTick(float fdt) { }
        public virtual void OnExit() { }

        /// <summary> Получили команду — вернуть новое состояние или null если не реагируем. </summary>
        public virtual FighterState HandleCommand(Game.Input.InputCommand cmd) => null;
    }

    /// <summary> Простейший FSM-контейнер. </summary>
    public class StateMachine
    {
        private readonly Fighter _fighter;
        private readonly Dictionary<System.Type, FighterState> _cache = new();
        public FighterState Current { get; private set; }

        public StateMachine(Fighter fighter) { _fighter = fighter; }

        public T Register<T>(T state) where T : FighterState
        {
            state.Bind(_fighter);
            _cache[typeof(T)] = state;
            return state;
        }

        public T Get<T>() where T : FighterState
        {
            _cache.TryGetValue(typeof(T), out var s);
            return s as T;
        }

        public void Change(FighterState next)
        {
            if (next == null || next == Current) return;
            Current?.OnExit();
            Current = next;
            Current.OnEnter();
        }

        public void Tick(float dt) => Current?.Tick(dt);
        public void FixedTick(float fdt) => Current?.FixedTick(fdt);

        public void HandleCommand(Game.Input.InputCommand cmd)
        {
            if (Current == null) return;
            var next = Current.HandleCommand(cmd);
            if (next != null) Change(next);
        }
    }
}