using Game.Characters;
using Game.Input;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary>
    /// Автоматически собирает UI на старте сцены: Canvas + EventSystem + виртуальный джойстик + HUD.
    /// Также добавляет GestureRecognizer и TouchInputProvider, если их нет на сцене.
    /// Один компонент на сцену — экономит 90% ручной возни с UI.
    /// </summary>
    public class ArenaBootstrap : MonoBehaviour
    {
        [Header("Local player")]
        [Tooltip("Локальный игрок (Fighter). Если null — найдём по IsLocalPlayer.")]
        [SerializeField] private Fighter _localPlayer;

        [Header("UI tuning")]
        [SerializeField] private Vector2 _joystickAnchor = new(180f, 180f);
        [SerializeField] private float _joystickBgSize = 220f;
        [SerializeField] private float _joystickHandleSize = 100f;
        [SerializeField] private float _hpBarWidth = 320f;
        [SerializeField] private float _hpBarHeight = 24f;

        private void Awake()
        {
            if (_localPlayer == null) _localPlayer = FindLocalPlayer();

            EnsureEventSystem();
            var canvas = CreateCanvas();
            CreateJoystick(canvas.transform);
            if (_localPlayer != null) CreateHud(canvas.transform);
            EnsureGestureBackend();
        }

        // ---------- UI ----------

        private void EnsureEventSystem()
        {
            if (FindAnyObjectByType<EventSystem>() != null) return;
            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            // Чтобы тач корректно работал и в редакторе через мышь, StandaloneInputModule достаточно.
        }

        private Canvas CreateCanvas()
        {
            var go = new GameObject("HUD_Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;

            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        private void CreateJoystick(Transform parent)
        {
            // Фон джойстика
            var bgGo = new GameObject("Joystick_BG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            bgGo.transform.SetParent(parent, false);
            var bgRT = (RectTransform)bgGo.transform;
            bgRT.anchorMin = bgRT.anchorMax = new Vector2(0f, 0f);
            bgRT.pivot = new Vector2(0.5f, 0.5f);
            bgRT.anchoredPosition = _joystickAnchor;
            bgRT.sizeDelta = new Vector2(_joystickBgSize, _joystickBgSize);
            var bgImage = bgGo.GetComponent<Image>();
            bgImage.color = new Color(1f, 1f, 1f, 0.18f);
            bgImage.sprite = MakeCircleSprite();
            bgImage.type = Image.Type.Simple;
            bgImage.raycastTarget = true;

            // Рукоятка
            var hGo = new GameObject("Joystick_Handle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            hGo.transform.SetParent(bgGo.transform, false);
            var hRT = (RectTransform)hGo.transform;
            hRT.anchorMin = hRT.anchorMax = new Vector2(0.5f, 0.5f);
            hRT.pivot = new Vector2(0.5f, 0.5f);
            hRT.anchoredPosition = Vector2.zero;
            hRT.sizeDelta = new Vector2(_joystickHandleSize, _joystickHandleSize);
            var hImage = hGo.GetComponent<Image>();
            hImage.color = new Color(1f, 1f, 1f, 0.6f);
            hImage.sprite = MakeCircleSprite();
            hImage.raycastTarget = false;

            // Компонент джойстика
            var joy = bgGo.AddComponent<VirtualJoystick>();
            // Через рефлексию ставим приватные поля (или сделаем публичные сеттеры — но рефлексия проще для bootstrap)
            var t = joy.GetType();
            t.GetField("_background", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(joy, bgRT);
            t.GetField("_handle", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(joy, hRT);
            t.GetField("_radius", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(joy, _joystickBgSize * 0.5f);
        }

        private void CreateHud(Transform parent)
        {
            // HP-бар
            var hpBg = new GameObject("HP_BG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            hpBg.transform.SetParent(parent, false);
            var bgRT = (RectTransform)hpBg.transform;
            bgRT.anchorMin = bgRT.anchorMax = new Vector2(0f, 1f);
            bgRT.pivot = new Vector2(0f, 1f);
            bgRT.anchoredPosition = new Vector2(40f, -40f);
            bgRT.sizeDelta = new Vector2(_hpBarWidth, _hpBarHeight);
            var hpBgImg = hpBg.GetComponent<Image>();
            hpBgImg.color = new Color(0f, 0f, 0f, 0.6f);
            hpBgImg.sprite = MakeWhiteSprite();

            var hpFillGo = new GameObject("HP_Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            hpFillGo.transform.SetParent(hpBg.transform, false);
            var fillRT = (RectTransform)hpFillGo.transform;
            fillRT.anchorMin = new Vector2(0f, 0f); fillRT.anchorMax = new Vector2(1f, 1f);
            fillRT.offsetMin = new Vector2(2f, 2f); fillRT.offsetMax = new Vector2(-2f, -2f);
            var hpFill = hpFillGo.GetComponent<Image>();
            hpFill.sprite = MakeWhiteSprite();
            hpFill.color = new Color(0.85f, 0.15f, 0.15f, 1f);
            hpFill.type = Image.Type.Filled;
            hpFill.fillMethod = Image.FillMethod.Horizontal;
            hpFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            hpFill.fillAmount = 1f;
            hpFill.raycastTarget = false;

            // Stamina-бар
            var stBg = new GameObject("ST_BG", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            stBg.transform.SetParent(parent, false);
            var stBgRT = (RectTransform)stBg.transform;
            stBgRT.anchorMin = stBgRT.anchorMax = new Vector2(0f, 1f);
            stBgRT.pivot = new Vector2(0f, 1f);
            stBgRT.anchoredPosition = new Vector2(40f, -40f - _hpBarHeight - 6f);
            stBgRT.sizeDelta = new Vector2(_hpBarWidth * 0.75f, _hpBarHeight * 0.6f);
            var stBgImg = stBg.GetComponent<Image>();
            stBgImg.color = new Color(0f, 0f, 0f, 0.6f);
            stBgImg.sprite = MakeWhiteSprite();

            var stFillGo = new GameObject("ST_Fill", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            stFillGo.transform.SetParent(stBg.transform, false);
            var stFillRT = (RectTransform)stFillGo.transform;
            stFillRT.anchorMin = new Vector2(0f, 0f); stFillRT.anchorMax = new Vector2(1f, 1f);
            stFillRT.offsetMin = new Vector2(2f, 2f); stFillRT.offsetMax = new Vector2(-2f, -2f);
            var stFill = stFillGo.GetComponent<Image>();
            stFill.sprite = MakeWhiteSprite();
            stFill.color = new Color(0.95f, 0.8f, 0.2f, 1f);
            stFill.type = Image.Type.Filled;
            stFill.fillMethod = Image.FillMethod.Horizontal;
            stFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            stFill.fillAmount = 1f;
            stFill.raycastTarget = false;

            // HUD-биндер — создаём ВЫКЛЮЧЕННЫМ, биндим ссылки, включаем.
            // Так OnEnable() сработает один раз и сразу с правильными полями.
            var hudGo = new GameObject("HUD_Binder");
            hudGo.transform.SetParent(parent, false);
            hudGo.SetActive(false);
            var hud = hudGo.AddComponent<FighterHUD>();
            hud.Bind(_localPlayer, hpFill, stFill);
            hudGo.SetActive(true);
        }

        // ---------- Input backend ----------

        private void EnsureGestureBackend()
        {
            var existing = FindAnyObjectByType<GestureRecognizer>();
            if (existing == null)
            {
                var go = new GameObject("_Input");
                var recognizer = go.AddComponent<GestureRecognizer>();
                var provider = go.AddComponent<TouchInputProvider>();
                var pT = provider.GetType();
                pT.GetField("_gestureRecognizer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(provider, recognizer);
            }
        }

        // ---------- Helpers ----------

        private Fighter FindLocalPlayer()
        {
            var fighters = FindObjectsByType<Fighter>(FindObjectsSortMode.None);
            foreach (var f in fighters)
                if (f.IsLocalPlayer) return f;
            return null;
        }

        private static Sprite _whiteSprite;
        internal static Sprite MakeWhiteSprite()
        {
            if (_whiteSprite != null) return _whiteSprite;
            const int size = 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            _whiteSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _whiteSprite.name = "WhiteSprite_Generated";
            return _whiteSprite;
        }

        private static Sprite _circleSprite;
        private static Sprite MakeCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - r;
                float dy = y + 0.5f - r;
                bool inside = dx * dx + dy * dy <= r * r;
                pixels[y * size + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _circleSprite;
        }
    }
}