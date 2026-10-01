using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    /// <summary> Процедурные элементы UI без ассетов: прямоугольники, полоски, тексты, спрайты. </summary>
    public static class UiFactory
    {
        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = position;
            rt.sizeDelta = size;
            return rt;
        }

        /// <summary> Полоска ресурса: фон + Filled Image. Возвращает заливку. </summary>
        public static Image Bar(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, Color color, bool fromRight)
        {
            var bg = Rect(name + "_BG", parent, anchor, anchor, position, size);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.color = new Color(0f, 0f, 0f, 0.6f);
            bgImg.sprite = WhiteSprite();
            bgImg.raycastTarget = false;

            var fillGo = new GameObject(name + "_Fill", typeof(RectTransform));
            fillGo.transform.SetParent(bg, false);
            var fillRT = (RectTransform)fillGo.transform;
            fillRT.anchorMin = Vector2.zero;
            fillRT.anchorMax = Vector2.one;
            fillRT.offsetMin = new Vector2(2f, 2f);
            fillRT.offsetMax = new Vector2(-2f, -2f);
            var fill = fillGo.AddComponent<Image>();
            fill.sprite = WhiteSprite();
            fill.color = color;
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = (int)(fromRight ? Image.OriginHorizontal.Right : Image.OriginHorizontal.Left);
            fill.fillAmount = 1f;
            fill.raycastTarget = false;
            return fill;
        }

        public static Text Label(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, int fontSize, TextAnchor align)
        {
            var rt = Rect(name, parent, anchor, new Vector2(0.5f, 0.5f), position, size);
            var text = rt.gameObject.AddComponent<Text>();
            text.font = DefaultFont();
            text.fontSize = fontSize;
            text.alignment = align;
            text.color = Color.white;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var outline = rt.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(2f, -2f);
            return text;
        }

        private static Font _font;
        public static Font DefaultFont()
        {
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return _font;
        }

        private static Sprite _white;
        public static Sprite WhiteSprite()
        {
            if (_white != null) return _white;
            const int size = 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(pixels);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.Apply();
            _white = Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _white.name = "WhiteSprite_Generated";
            return _white;
        }

        private static Sprite _circle;
        public static Sprite CircleSprite()
        {
            if (_circle != null) return _circle;
            _circle = Generate(128, (x, y, s) =>
            {
                float r = s * 0.5f, dx = x + 0.5f - r, dy = y + 0.5f - r;
                return dx * dx + dy * dy <= r * r;
            });
            return _circle;
        }

        private static Sprite _arrow;
        /// <summary> Треугольник, остриём вправо (+X). </summary>
        public static Sprite ArrowSprite()
        {
            if (_arrow != null) return _arrow;
            _arrow = Generate(64, (x, y, s) =>
            {
                float u = (x + 0.5f) / s, v = (y + 0.5f) / s;
                return Mathf.Abs(v - 0.5f) <= 0.5f * (1f - u);
            });
            return _arrow;
        }

        private static Sprite Generate(int size, System.Func<int, int, int, bool> inside)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = inside(x, y, size) ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            tex.SetPixels32(pixels);
            tex.Apply();
            return Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
