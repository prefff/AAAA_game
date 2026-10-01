using Game.Core;
using NUnit.Framework;

namespace Game.Tests
{
    public class LatencyLogTests
    {
        [Test]
        public void Sample_SplitsTotalIntoGestureAndEngine()
        {
            var log = new LatencyLog(4);
            log.Record("LightAttack", "AttackState", inputTime: 10.000, recognizedTime: 10.080, frame: 5);
            log.MarkRendered(10.100, 5);

            var s = log.Last;
            Assert.IsTrue(s.Accepted);
            Assert.AreEqual(100.0, s.TotalMs, 1e-6);
            Assert.AreEqual(80.0, s.GestureMs, 1e-6);
            Assert.AreEqual(20.0, s.EngineMs, 1e-6);
        }

        [Test]
        public void MarkRendered_StampsOnlyPendingSamples()
        {
            var log = new LatencyLog(4);
            log.Record("A", "S", 1.0, 1.0, 1);
            log.MarkRendered(1.5, 1);
            log.Record("B", null, 2.0, 2.0, 2);
            log.MarkRendered(2.25, 2);

            Assert.AreEqual(1.5, log.Get(0).RenderedTime);
            Assert.AreEqual(2.25, log.Get(1).RenderedTime);
            Assert.IsFalse(log.Get(1).Accepted, "Команда без смены состояния — отброшена");
        }

        [Test]
        public void RingBuffer_KeepsNewest_AndStillMarksPendingAfterWrap()
        {
            var log = new LatencyLog(3);
            for (int i = 0; i < 5; i++) log.Record("C" + i, "S", i, i, i);
            log.MarkRendered(100.0, 9);

            Assert.AreEqual(3, log.Count);
            Assert.AreEqual(5, log.TotalRecorded);
            Assert.AreEqual("C2", log.Get(0).Label);
            Assert.AreEqual("C4", log.Last.Label);
            for (int i = 0; i < log.Count; i++) Assert.IsTrue(log.Get(i).IsRendered);
        }

        [Test]
        public void Median_IgnoresRejectedAndOtherLabels()
        {
            var log = new LatencyLog(8);
            log.Record("Dodge", "DodgeState", 0.0, 0.0, 0);
            log.Record("Dodge", "DodgeState", 0.0, 0.0, 0);
            log.Record("Dodge", null, 0.0, 0.0, 0);         // отброшена
            log.Record("Parry", "ParryState", 0.0, 0.0, 0); // другая метка
            log.MarkRendered(0.010, 0);
            log.Record("Dodge", "DodgeState", 1.0, 1.0, 1);
            log.MarkRendered(1.030, 1);

            Assert.IsTrue(log.TryGetMedian("Dodge", out double total, out double engine, out int n));
            Assert.AreEqual(3, n);
            Assert.AreEqual(10.0, total, 1e-6);
            Assert.AreEqual(10.0, engine, 1e-6);
            Assert.IsFalse(log.TryGetMedian("HeavyAttack", out _, out _, out _));
        }
    }
}
