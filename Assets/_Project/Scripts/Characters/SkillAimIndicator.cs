using Game.Input;
using Game.Simulation;
using UnityEngine;

namespace Game.Characters
{
    /// <summary>
    /// Прицел скилла на полу (как в MLBB), пока палец держит кнопку: кольцо дальности вокруг своего бойца, линия
    /// направления и, для телепорта и области, точка приземления / круг взрыва. Направление и дальность считает сама
    /// симуляция (<see cref="FightSimulation.PreviewAim"/>) по тому же квантованному прицелу, что уйдёт в тик, — индикатор
    /// показывает ровно то, что произойдёт. В зоне отмены прицел краснеет.
    /// </summary>
    [DefaultExecutionOrder(120)]
    public sealed class SkillAimIndicator : MonoBehaviour
    {
        [SerializeField] private Color _color = new(0.4f, 0.85f, 1f, 0.9f);
        [SerializeField] private Color _cancelColor = new(1f, 0.25f, 0.2f, 0.9f);
        [SerializeField] private Color _rangeColor = new(1f, 1f, 1f, 0.35f);

        private MatchRunner _runner;
        private LineRenderer _range;
        private LineRenderer _line;
        private LineRenderer _target;

        public bool IsShown => _range != null && _range.gameObject.activeSelf;
        /// <summary> Куда указывает прицел (мир, XZ) — для тестов. </summary>
        public Vector3 TargetPoint { get; private set; }

        public void Bind(MatchRunner runner) => _runner = runner;

        private void Awake()
        {
            _range = WorldLines.Create("AimRange", transform, 0.05f, loop: true);
            _line = WorldLines.Create("AimLine", transform, 0.2f, loop: false);
            _target = WorldLines.Create("AimTarget", transform, 0.08f, loop: true);
        }

        private void LateUpdate()
        {
            if (_runner == null || _runner.State == null || !_runner.IsAiming || _runner.AimSlot < 0)
            {
                Hide();
                return;
            }

            int me = _runner.LocalPlayer;
            var spec = _runner.Sim.Setup.Fighters[me];
            var sk = spec.Skill(_runner.AimSlot);
            if (sk == null || !_runner.State.Fighters[me].IsAlive)
            {
                Hide();
                return;
            }

            // Тот же прицел, что получит симуляция: квантованный, как в TickInput.
            var a = _runner.AimInput;
            var aim = TickInput.Dequantize(TickInput.Quantize(a.x), TickInput.Quantize(a.y));
            var dirFix = _runner.Sim.PreviewAim(_runner.State, me, _runner.AimSlot, aim, out var distFix);

            ref readonly var f = ref _runner.State.Fighters[me];
            var origin = new Vector3(f.Position.X.ToFloat(), 0f, f.Position.Y.ToFloat());
            var dir = new Vector3(dirFix.X.ToFloat(), 0f, dirFix.Y.ToFloat());
            float range = sk.Range.ToFloat();
            var color = _runner.AimInCancelZone ? _cancelColor : _color;

            WorldLines.Ring(_range, origin, range, _runner.AimInCancelZone ? _cancelColor : _rangeColor);

            if (sk.Kind == SkillKind.Projectile)
            {
                TargetPoint = origin + dir * range;
                _line.widthMultiplier = sk.ProjectileRadius.ToFloat() * 2f;
                WorldLines.Segment(_line, origin, TargetPoint, color);
                WorldLines.Hide(_target);
                return;
            }

            TargetPoint = origin + dir * distFix.ToFloat();
            _line.widthMultiplier = 0.08f;
            WorldLines.Segment(_line, origin, TargetPoint, color);
            float r = sk.Kind == SkillKind.Zone ? sk.ZoneRadius.ToFloat() : spec.BodyRadius.ToFloat();
            WorldLines.Ring(_target, TargetPoint, r, color);
        }

        private void Hide()
        {
            WorldLines.Hide(_range);
            WorldLines.Hide(_line);
            WorldLines.Hide(_target);
        }
    }
}
