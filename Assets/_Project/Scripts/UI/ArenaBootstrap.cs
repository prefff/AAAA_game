using Game.Characters;
using Game.Combat;
using Game.Core;
using Game.Input;
using Game.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Собирает сцену боя на старте: арена и бойцы (<see cref="ArenaBuilder"/>), Canvas, джойстик, HUD бойцов и матча,
    /// индикатор противника за краем экрана, распознаватель жестов и оверлей разработчика.
    /// Один компонент на сцену.
    /// </summary>
    public class ArenaBootstrap : MonoBehaviour
    {
        [Header("Бойцы")]
        [Tooltip("Пусто — Resources/Fighters/Fighter_Default.")]
        [SerializeField] private FighterDefinition _player;
        [SerializeField] private FighterDefinition _opponent;
        [Tooltip("Префабы видов (необязательно): пусто — капсулы собираются процедурно.")]
        [SerializeField] private FighterView _playerView;
        [SerializeField] private FighterView _opponentView;
        [Tooltip("Каким бойцом управляет этот телефон: 0 — синяя сторона, 1 — красная (камера развёрнута).")]
        [SerializeField, Range(0, 1)] private int _localPlayerIndex;

        [Header("Тренировка")]
        [SerializeField] private BotMode _botMode = BotMode.Idle;
        [SerializeField] private bool _training;

        [Header("UI tuning")]
        [SerializeField] private Vector2 _joystickAnchor = new(180f, 180f);
        [SerializeField] private float _joystickBgSize = 220f;
        [SerializeField] private float _joystickHandleSize = 100f;
        [SerializeField] private float _hpBarWidth = 320f;
        [SerializeField] private float _hpBarHeight = 24f;

        [Header("Performance")]
        [Tooltip("Целевой FPS. 0 = максимальная частота экрана (60/90/120 Гц).")]
        [SerializeField] private int _targetFps = 0;
        [Tooltip("Принудительно отключить VSync. По умолчанию VSync сохраняется — предпочтительно для мобилок и редактора.")]
        [SerializeField] private bool _disableVSync = false;

        [Header("Diagnostics")]
        [Tooltip("Оверлей задержки ввода, состояния бойца, преимущества по кадрам и панель тренировки.")]
        [SerializeField] private bool _latencyOverlay = true;

        private ArenaBuilder.Result _arena;

        public ArenaBuilder.Result Arena => _arena;

        private void Awake()
        {
            // Принудительно landscape — страховка на случай, если Player Settings вдруг сбросились.
            Screen.orientation = ScreenOrientation.LandscapeLeft;
            Screen.autorotateToLandscapeLeft = true;
            Screen.autorotateToLandscapeRight = true;
            Screen.autorotateToPortrait = false;
            Screen.autorotateToPortraitUpsideDown = false;

            // Снимаем дефолтный 30 FPS на мобилках, используем максимальную частоту экрана.
            FrameRateBooster.Apply(_targetFps, _disableVSync);

            _arena = ArenaBuilder.Build(_player, _opponent, _localPlayerIndex, _botMode, _training, _playerView, _opponentView);

            EnsureEventSystem();
            var canvas = CreateCanvas();
            CreateJoystick(canvas.transform);
            CreateHud(canvas.transform);
            CreateMatchHud(canvas.transform);
            CreateOffscreenIndicator(canvas.transform);
            EnsureGestureBackend();
            if (_latencyOverlay) CreateLatencyOverlay();
        }

        // ---------- UI ----------

        private static void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            _ = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static Canvas CreateCanvas()
        {
            var go = new GameObject("HUD_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            // Match by width — стабильный масштаб landscape-UI на разных плотностях пикселей.
            scaler.matchWidthOrHeight = 0f;
            return canvas;
        }

        private void CreateJoystick(Transform parent)
        {
            var bgRT = UiFactory.Rect("Joystick_BG", parent, new Vector2(0f, 0f), new Vector2(0.5f, 0.5f), _joystickAnchor,
                new Vector2(_joystickBgSize, _joystickBgSize));
            var bgImage = bgRT.gameObject.AddComponent<Image>();
            bgImage.color = new Color(1f, 1f, 1f, 0.18f);
            bgImage.sprite = UiFactory.CircleSprite();

            var hRT = UiFactory.Rect("Joystick_Handle", bgRT, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(_joystickHandleSize, _joystickHandleSize));
            var hImage = hRT.gameObject.AddComponent<Image>();
            hImage.color = new Color(1f, 1f, 1f, 0.6f);
            hImage.sprite = UiFactory.CircleSprite();
            hImage.raycastTarget = false;

            bgRT.gameObject.AddComponent<VirtualJoystick>().Setup(bgRT, hRT, _joystickBgSize * 0.5f);
        }

        private void CreateHud(Transform parent)
        {
            var runner = _arena.Runner;

            // Свой боец — слева сверху: HP, стамина, мана.
            float y = -40f;
            var hp = UiFactory.Bar("HP", parent, new Vector2(0f, 1f), new Vector2(40f, y), new Vector2(_hpBarWidth, _hpBarHeight),
                new Color(0.85f, 0.15f, 0.15f), fromRight: false);
            y -= _hpBarHeight + 6f;
            var st = UiFactory.Bar("ST", parent, new Vector2(0f, 1f), new Vector2(40f, y), new Vector2(_hpBarWidth * 0.75f, _hpBarHeight * 0.6f),
                new Color(0.95f, 0.8f, 0.2f), fromRight: false);
            y -= _hpBarHeight * 0.6f + 4f;
            var mp = UiFactory.Bar("MP", parent, new Vector2(0f, 1f), new Vector2(40f, y), new Vector2(_hpBarWidth * 0.75f, _hpBarHeight * 0.6f),
                new Color(0.3f, 0.55f, 1f), fromRight: false);
            Bind(parent, "HUD_Local", runner, runner.LocalPlayer, hp, st, mp);

            // Противник — справа сверху, зеркально.
            var ohp = UiFactory.Bar("HP_Opponent", parent, new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(_hpBarWidth, _hpBarHeight),
                new Color(0.85f, 0.15f, 0.15f), fromRight: true);
            Bind(parent, "HUD_Opponent", runner, runner.Opponent, ohp, null, null);

            // И полоска над головой противника — её видно, когда смотришь на бой, а не на угол экрана.
            var bar = _arena.Opponent.gameObject.AddComponent<WorldSpaceHealthBar>();
            int opp = runner.Opponent;
            bar.Bind(() => runner.State.Fighters[opp].Health.ToFloat() / runner.Sim.Setup.Fighters[opp].MaxHealth.ToFloat());
        }

        private static void Bind(Transform parent, string name, MatchRunner runner, int index, Image hp, Image st, Image mp)
        {
            // Создаём выключенным, биндим, включаем — OnEnable сработает уже с правильными полями.
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.SetActive(false);
            go.AddComponent<FighterHUD>().Bind(runner, index, hp, st, mp);
            go.SetActive(true);
        }

        private void CreateMatchHud(Transform parent)
        {
            var go = new GameObject("MatchHUD", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            go.SetActive(false);
            go.AddComponent<MatchHUD>().Bind(_arena.Runner);
            go.SetActive(true);
        }

        private void CreateOffscreenIndicator(Transform parent)
        {
            var go = new GameObject("OffscreenIndicator", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.SetActive(false);
            var indicator = go.AddComponent<OffscreenIndicator>();
            int opp = _arena.Runner.Opponent;
            var runner = _arena.Runner;
            indicator.Bind(_arena.Opponent.transform, ArenaBuilder.OpponentColor, () => runner.State.Fighters[opp].IsAlive);
            go.SetActive(true);
        }

        private void CreateLatencyOverlay()
        {
            var go = new GameObject("_LatencyOverlay");
            go.SetActive(false);
            go.AddComponent<LatencyOverlay>().Bind(_arena.Runner);
            go.SetActive(true);
        }

        // ---------- Input backend ----------

        private static void EnsureGestureBackend()
        {
            if (FindAnyObjectByType<TouchInputProvider>() == null)
                new GameObject("_Input").AddComponent<TouchInputProvider>(); // распознаватель жестов создаётся внутри
        }
    }
}
