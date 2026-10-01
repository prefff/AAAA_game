using Game.Characters;
using Game.Combat;
using Game.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    public class EventBusTests
    {
        private struct TestEvent { public int Value; }

        [SetUp] public void SetUp() => EventBus.Clear();
        [TearDown] public void TearDown() => EventBus.Clear();

        [Test]
        public void Raise_InvokesSubscriber()
        {
            int received = 0;
            EventBus.Subscribe<TestEvent>(this, e => received = e.Value);

            EventBus.Raise(new TestEvent { Value = 42 });

            Assert.AreEqual(42, received);
        }

        [Test]
        public void UnsubscribeAll_RemovesOnlyOwnersHandlers()
        {
            var ownerA = new object();
            var ownerB = new object();
            int a = 0, b = 0;
            EventBus.Subscribe<TestEvent>(ownerA, _ => a++);
            EventBus.Subscribe<TestEvent>(ownerA, _ => a++);
            EventBus.Subscribe<TestEvent>(ownerB, _ => b++);

            EventBus.UnsubscribeAll(ownerA);
            EventBus.Raise(new TestEvent());

            Assert.AreEqual(0, a);
            Assert.AreEqual(1, b);
            Assert.AreEqual(1, EventBus.GetSubscriberCount<TestEvent>());
        }

        [Test]
        public void UnsubscribeAll_InsideHandler_DoesNotBreakCurrentRaise()
        {
            var owner = new object();
            int calls = 0;
            EventBus.Subscribe<TestEvent>(owner, _ => { calls++; EventBus.UnsubscribeAll(owner); });

            EventBus.Raise(new TestEvent());
            EventBus.Raise(new TestEvent());

            Assert.AreEqual(1, calls);
            Assert.AreEqual(0, EventBus.GetSubscriberCount<TestEvent>());
        }
    }

    public class HealthStaminaTests
    {
        private GameObject _go;

        [SetUp] public void SetUp() { EventBus.Clear(); _go = new GameObject("Test"); }
        [TearDown] public void TearDown() { Object.DestroyImmediate(_go); EventBus.Clear(); }

        private Health MakeHealth()
        {
            // В Edit Mode Awake не вызывается — инициализируем вручную.
            var h = _go.AddComponent<Health>();
            h.OnHealthChanged ??= new UnityEngine.Events.UnityEvent<float>();
            h.OnDied ??= new UnityEngine.Events.UnityEvent();
            h.ResetHealth();
            return h;
        }

        [Test]
        public void TakeDamage_ReducesHealth()
        {
            var h = MakeHealth();
            h.TakeDamage(30f, null, null, Vector3.zero);
            Assert.AreEqual(h.Max - 30f, h.Current, 0.001f);
        }

        [Test]
        public void TakeDamage_Lethal_InvokesOnDiedOnce_AndStopsAtZero()
        {
            var h = MakeHealth();
            int died = 0;
            h.OnDied.AddListener(() => died++);

            h.TakeDamage(h.Max + 50f, null, null, Vector3.zero);
            h.TakeDamage(10f, null, null, Vector3.zero);

            Assert.AreEqual(0f, h.Current);
            Assert.IsFalse(h.IsAlive);
            Assert.AreEqual(1, died);
        }

        [Test]
        public void TrySpend_DoesNotGoNegative()
        {
            var s = _go.AddComponent<Stamina>();
            s.OnStaminaChanged ??= new UnityEngine.Events.UnityEvent<float>();
            s.ResetStamina();

            Assert.IsTrue(s.TrySpend(s.Max * 0.75f));
            Assert.IsFalse(s.TrySpend(s.Max * 0.5f));
            Assert.AreEqual(s.Max * 0.25f, s.Current, 0.001f);
        }
    }

    public class StateMachineTests
    {
        private class ProbeState : FighterState
        {
            public int Enters, Exits;
            public override void OnEnter() => Enters++;
            public override void OnExit() => Exits++;
        }

        private class OtherState : ProbeState { }

        [Test]
        public void Change_CallsExitOnPreviousAndEnterOnNext()
        {
            var fsm = new StateMachine(null);
            var a = fsm.Register(new ProbeState());
            var b = fsm.Register(new OtherState());

            fsm.Change(a);
            fsm.Change(b);

            Assert.AreEqual(1, a.Enters);
            Assert.AreEqual(1, a.Exits);
            Assert.AreEqual(1, b.Enters);
            Assert.AreSame(b, fsm.Current);
        }

        [Test]
        public void Change_ToSameState_IsNoOp()
        {
            var fsm = new StateMachine(null);
            var a = fsm.Register(new ProbeState());

            fsm.Change(a);
            fsm.Change(a);

            Assert.AreEqual(1, a.Enters);
            Assert.AreEqual(0, a.Exits);
        }

        [Test]
        public void Stop_ExitsCurrent_AndAllowsRestart()
        {
            var fsm = new StateMachine(null);
            var a = fsm.Register(new ProbeState());

            fsm.Change(a);
            fsm.Stop();
            Assert.IsNull(fsm.Current);
            Assert.AreEqual(1, a.Exits);

            fsm.Change(a);
            Assert.AreEqual(2, a.Enters);
        }
    }
}
