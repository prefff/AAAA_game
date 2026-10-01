using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Core
{
    /// <summary>
    /// Простая типобезопасная событийная шина (in-process).
    /// Используется для развязки систем: Combat не знает про UI, UI не знает про Input и т.д.
    /// Пример:
    ///   EventBus.Subscribe<DamageDealtEvent>(this, OnDamage);
    ///   EventBus.Raise(new DamageDealtEvent(attacker, target, 25f, hitPoint));
    /// </summary>
    public static class EventBus
    {
        private static readonly Dictionary<Type, Delegate> _handlers = new();
        private static readonly Dictionary<object, List<Subscription>> _ownerSubscriptions = new();

        /// <summary>
        /// Подписаться на событие с автоматическим контролем владельца.
        /// Предпочтительный API — позволяет отписать все события объекта через <see cref="UnsubscribeAll(object)"/>.
        /// </summary>
        public static void Subscribe<T>(object owner, Action<T> handler) where T : struct
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            var type = typeof(T);
            if (_handlers.TryGetValue(type, out var existing))
                _handlers[type] = Delegate.Combine(existing, handler);
            else
                _handlers[type] = handler;

            if (!_ownerSubscriptions.TryGetValue(owner, out var list))
            {
                list = new List<Subscription>();
                _ownerSubscriptions[owner] = list;
            }
            list.Add(new Subscription(type, handler));
        }

        /// <summary>
        /// Отписать все события, зарегистрированные указанным владельцем.
        /// Безопасно вызывать из OnDisable / OnDestroy.
        /// </summary>
        public static void UnsubscribeAll(object owner)
        {
            if (owner == null) return;
            if (!_ownerSubscriptions.TryGetValue(owner, out var list)) return;

            foreach (var sub in list)
                RemoveHandler(sub.Type, sub.Handler);

            _ownerSubscriptions.Remove(owner);
        }

        public static void Raise<T>(T evt) where T : struct
        {
            if (_handlers.TryGetValue(typeof(T), out var existing))
                ((Action<T>)existing)?.Invoke(evt);
        }

        /// <summary> Очистить всё (вызывать при смене сцены/арены). </summary>
        // Сброс при входе в Play Mode: статика переживает запуск, если в Enter Play Mode Options отключён Domain Reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            _handlers.Clear();
            _ownerSubscriptions.Clear();
        }

        /// <summary>
        /// Отладочный счётчик подписчиков для конкретного типа события.
        /// </summary>
        public static int GetSubscriberCount<T>() where T : struct
        {
            if (_handlers.TryGetValue(typeof(T), out var existing))
                return existing?.GetInvocationList().Length ?? 0;
            return 0;
        }

        private static void RemoveHandler(Type type, Delegate handler)
        {
            if (!_handlers.TryGetValue(type, out var existing)) return;
            var updated = Delegate.Remove(existing, handler);
            if (updated == null) _handlers.Remove(type);
            else _handlers[type] = updated;
        }

        private readonly struct Subscription
        {
            public readonly Type Type;
            public readonly Delegate Handler;

            public Subscription(Type type, Delegate handler)
            {
                Type = type;
                Handler = handler;
            }
        }
    }
}
