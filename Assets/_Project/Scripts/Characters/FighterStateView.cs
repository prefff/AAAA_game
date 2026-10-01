using UnityEngine;

namespace Game.Characters
{
    /// <summary>
    /// Временный «вид» бойца: красит меш по состоянию FSM, пока нет анимаций. Нужен, чтобы отклик на команду
    /// был виден в кадре касания и чтобы читались фазы удара (startup / active / recovery).
    /// Только читает состояние; цвет ставится в LateUpdate — после ввода, логики и физики этого кадра.
    /// </summary>
    [RequireComponent(typeof(Fighter))]
    public class FighterStateView : MonoBehaviour
    {
        [SerializeField] private Renderer _renderer;
        [SerializeField] private Color _startup = new(1f, 0.85f, 0.2f);
        [SerializeField] private Color _active = new(1f, 0.25f, 0.15f);
        [SerializeField] private Color _recovery = new(0.55f, 0.4f, 0.3f);
        [SerializeField] private Color _block = new(0.3f, 0.45f, 1f);
        [SerializeField] private Color _parry = new(1f, 1f, 1f);
        [SerializeField] private Color _dodge = new(0.3f, 1f, 0.6f);
        [SerializeField] private Color _hitstun = new(0.85f, 0.2f, 0.85f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private Fighter _fighter;
        private MaterialPropertyBlock _props;
        private int _colorId = -1;
        private Color _original;
        private Color _shown;

        private void Awake()
        {
            _fighter = GetComponent<Fighter>();
            if (_renderer == null) _renderer = GetComponentInChildren<Renderer>();
            var mat = _renderer != null ? _renderer.sharedMaterial : null;
            if (mat == null) return;

            if (mat.HasProperty(BaseColorId)) _colorId = BaseColorId;
            else if (mat.HasProperty(ColorId)) _colorId = ColorId;
            if (_colorId == -1) return;

            // Исходный цвет — из PropertyBlock (ArenaBuilder красит через него) или из материала.
            _props = new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_props);
            _original = _props.HasColor(_colorId) ? _props.GetColor(_colorId) : mat.GetColor(_colorId);
            _shown = _original;
        }

        private void LateUpdate()
        {
            if (_colorId == -1) return;
            var color = ColorFor(_fighter.FSM?.Current);
            if (color == _shown) return;
            _shown = color;
            _renderer.GetPropertyBlock(_props);
            _props.SetColor(_colorId, color);
            _renderer.SetPropertyBlock(_props);
        }

        private Color ColorFor(FighterState state)
        {
            switch (state)
            {
                case AttackState attack:
                    return attack.CurrentPhase switch
                    {
                        AttackState.Phase.Startup => _startup,
                        AttackState.Phase.Active => _active,
                        _ => _recovery,
                    };
                case BlockState: return _block;
                case ParryState: return _parry;
                case DodgeState: return _dodge;
                case HitstunState: return _hitstun;
                default: return _original;
            }
        }
    }
}
