using System;
using System.Collections.Generic;

namespace Game.Core
{
    /// <summary>
    /// Простая типобезопасная событийная шина (in-process).
    /// Используется для развязки систем: Combat не знает про UI, UI не знает про Input и т.д.
    /// Пример:
    ///   EventBus.Subscribe&lt;DamageDealtEvent&gt;(OnDamage);
    ///   EventBus.Raise(new DamageDealtEvent(target, 25f));
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, Delegate> _handlers = new();

        public static void Subscribe<T>(Action<T> handler) where T : struct
        {
            var type = typeof(T);
            if (_handlers.TryGetValue(type, out var existing))
                _handlers[type] = Delegate.Combine(existing, handler);
            else
                _handlers[type] = handler;
        }

        public static void Unsubscribe<T>(Action<T> handler) where T : struct
        {
            var type = typeof(T);
            if (!_handlers.TryGetValue(type, out var existing)) return;
            var updated = Delegate.Remove(existing, handler);
            if (updated == null) _handlers.Remove(type);
            else _handlers[type] = updated;
        }

        public static void Raise<T>(T evt) where T : struct
        {
            if (_handlers.TryGetValue(typeof(T), out var existing))
                ((Action<T>)existing)?.Invoke(evt);
        }

        /// <summary> Очистить всё (вызывать при смене сцены/арены). </summary>
        public static void Clear() => _handlers.Clear();
    }
}