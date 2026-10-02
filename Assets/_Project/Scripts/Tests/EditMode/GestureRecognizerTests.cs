using System.Collections.Generic;
using System.Linq;
using Game.Characters;
using Game.Input;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests
{
    /// <summary> Распознаватель на записанных касаниях: (позиция, время) → ожидаемые команды. </summary>
    public class GestureRecognizerTests
    {
        private const float PxPerMm = 10f; // порог уклонения 6 мм = 60 px
        private static readonly Vector2 Start = new(1000f, 500f);

        private GestureSettings _settings;
        private GestureRecognizer _r;
        private readonly List<InputCommand> _out = new();

        [SetUp]
        public void SetUp()
        {
            _out.Clear();
            _settings = ScriptableObject.CreateInstance<GestureSettings>();
            _r = new GestureRecognizer(_settings, _out.Add) { PixelsPerMm = PxPerMm };
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_settings);

        private static Vector2 At(float dx, float dy = 0f) => Start + new Vector2(dx, dy);
        private CommandType[] Types => _out.Select(c => c.Type).ToArray();

        [Test]
        public void TouchDown_StartsLightAttackImmediately()
        {
            _r.TouchBegan(Start, 10.0);

            Assert.AreEqual(new[] { CommandType.LightAttack }, Types);
            Assert.AreEqual(10.0, _out[0].InputTime);
            Assert.AreEqual(10.0, _out[0].RecognizedTime, "Удар не ждёт отпускания пальца");
        }

        [Test]
        public void Tap_ReleaseAddsNothing()
        {
            _r.TouchBegan(Start, 10.0);
            _r.TouchMoved(At(3f), 10.03);
            _r.TouchEnded(At(3f), 10.06);

            Assert.AreEqual(new[] { CommandType.LightAttack }, Types);
        }

        [Test]
        public void Jitter_BelowThreshold_DoesNotDodge()
        {
            _r.TouchBegan(Start, 10.0);
            _r.TouchMoved(At(50f), 10.02); // 5 мм < 6 мм

            Assert.AreEqual(new[] { CommandType.LightAttack }, Types);
        }

        [Test]
        public void Swipe_DodgesAtThresholdCrossing_BeforeRelease()
        {
            _r.TouchBegan(Start, 10.0);
            _r.TouchMoved(At(30f), 10.02);
            _r.TouchMoved(At(70f), 10.04);

            Assert.AreEqual(new[] { CommandType.LightAttack, CommandType.Dodge }, Types);
            Assert.AreEqual(Vector2.right, _out[1].Direction);
            Assert.AreEqual(10.0, _out[1].InputTime);
            Assert.AreEqual(10.04, _out[1].RecognizedTime, 1e-9);

            _r.TouchMoved(At(200f), 10.15);
            _r.TouchEnded(At(200f), 10.25); // отпускание после уклонения ничего не добавляет
            Assert.AreEqual(new[] { CommandType.LightAttack, CommandType.Dodge }, Types);
        }

        [Test]
        public void Swipe_DirectionIsConvertedToWorld()
        {
            _r.ScreenToWorld = d => new Vector2(-d.y, d.x); // камера, повёрнутая на 90°
            _r.TouchBegan(Start, 10.0);
            _r.TouchMoved(At(80f), 10.03);

            Assert.AreEqual(new Vector2(0f, 1f), _out[1].Direction);
        }

        [Test]
        public void FastShortSwipe_StaysDodge_NeverParry()
        {
            // Рядом с противником перекат делают резко — раньше такой свайп превращался в парирование.
            _r.TouchBegan(Start, 10.0);
            _r.TouchMoved(At(80f), 10.03);
            _r.TouchEnded(At(150f), 10.08); // 15 мм за 80 мс

            Assert.AreEqual(new[] { CommandType.LightAttack, CommandType.Dodge }, Types);
        }

        [Test]
        public void FastRelease_WithoutMoveEvents_IsDodge()
        {
            _r.TouchBegan(Start, 10.0);
            _r.TouchEnded(At(0f, 150f), 10.06);

            Assert.AreEqual(new[] { CommandType.LightAttack, CommandType.Dodge }, Types);
            Assert.AreEqual(Vector2.up, _out[1].Direction);
        }

        [Test]
        public void FarRelease_WithoutMoveEvents_SlowIsDodge()
        {
            _r.TouchBegan(Start, 10.0);
            _r.TouchEnded(At(0f, 150f), 10.2);

            Assert.AreEqual(new[] { CommandType.LightAttack, CommandType.Dodge }, Types);
            Assert.AreEqual(Vector2.up, _out[1].Direction);
        }

        [Test]
        public void Hold_BlocksAtThreshold_ReleaseEndsBlock()
        {
            _r.TouchBegan(Start, 10.0);
            _r.Update(10.0 + _settings.HoldThreshold * 0.5);
            Assert.AreEqual(1, _out.Count);

            _r.Update(10.0 + _settings.HoldThreshold + 0.01);
            Assert.AreEqual(new[] { CommandType.LightAttack, CommandType.BlockStart }, Types);
            Assert.AreEqual(10.0 + _settings.HoldThreshold, _out[1].RecognizedTime, 1e-6,
                "Опоздание проверки удержания до кадра — задержка игры, а не жеста");

            _r.TouchEnded(Start, 10.5);
            Assert.AreEqual(CommandType.BlockEnd, _out[2].Type);
            Assert.AreEqual(10.5, _out[2].InputTime, "Отпускание блока меряется от момента отпускания");
        }

        [Test]
        public void HoldThenSwipe_DodgesFromBlock_AndReleasesBlock()
        {
            _r.TouchBegan(Start, 10.0);
            _r.Update(10.1);
            _r.TouchMoved(At(-80f), 10.3);
            _r.TouchEnded(At(-120f), 10.4);

            Assert.AreEqual(new[] { CommandType.LightAttack, CommandType.BlockStart, CommandType.Dodge, CommandType.BlockEnd }, Types);
            Assert.AreEqual(Vector2.left, _out[2].Direction);
        }

        [Test]
        public void SecondFinger_IsHeavyAttack_AndConsumesPrimary()
        {
            _r.TouchBegan(Start, 10.0);
            _r.SecondaryTouchBegan(10.03);
            _r.Update(10.5);
            _r.TouchMoved(At(200f), 10.6);
            _r.TouchEnded(At(200f), 10.7);

            Assert.AreEqual(new[] { CommandType.LightAttack, CommandType.HeavyAttack }, Types);
            Assert.AreEqual(10.03, _out[1].InputTime);
        }

        [Test]
        public void Cancel_DuringHold_ReleasesBlock()
        {
            _r.TouchBegan(Start, 10.0);
            _r.Update(10.2);
            _r.TouchCanceled(10.3);

            Assert.AreEqual(new[] { CommandType.LightAttack, CommandType.BlockStart, CommandType.BlockEnd }, Types);
            Assert.IsFalse(_r.IsTracking);
        }
    }

    public class InputBufferTests
    {
        private static InputCommand Cmd(CommandType t) => new(t, Vector2.zero, 0f);

        [Test]
        public void Command_LivesForWindow_ThenExpires()
        {
            var b = new InputBuffer(0.1);
            b.Push(Cmd(CommandType.LightAttack), 5.0, out _);

            Assert.IsFalse(b.Expire(5.09, out _));
            Assert.IsTrue(b.HasCommand);
            Assert.IsTrue(b.Expire(5.11, out var expired));
            Assert.AreEqual(CommandType.LightAttack, expired.Type);
            Assert.IsFalse(b.HasCommand);
        }

        [Test]
        public void NewCommand_ReplacesOld()
        {
            var b = new InputBuffer(0.1);
            Assert.IsFalse(b.Push(Cmd(CommandType.LightAttack), 5.0, out _));
            Assert.IsTrue(b.Push(Cmd(CommandType.Dodge), 5.05, out var replaced));

            Assert.AreEqual(CommandType.LightAttack, replaced.Type);
            Assert.AreEqual(CommandType.Dodge, b.Command.Type);
            Assert.IsFalse(b.Expire(5.14, out _), "Окно считается от прихода новой команды");
        }

        [Test]
        public void ZeroWindow_DisablesBuffer()
        {
            var b = new InputBuffer(0.0);
            b.Push(Cmd(CommandType.Parry), 1.0, out _);
            Assert.IsFalse(b.HasCommand);
        }

        [Test]
        public void BlockEnd_IsNotBufferable()
        {
            Assert.IsFalse(InputBuffer.IsBufferable(CommandType.BlockEnd));
            Assert.IsTrue(InputBuffer.IsBufferable(CommandType.BlockStart));
        }
    }
}
