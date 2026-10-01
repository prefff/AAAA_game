using UnityEngine;

namespace Game.Characters
{
    /// <summary>
    /// Линии на полу арены (кольца областей, прицел, вспышки) через LineRenderer — без ассетов, в любом рендер-пайплайне.
    /// </summary>
    public static class WorldLines
    {
        public const float FloorY = 0.03f;
        private const int RingSegments = 48;

        private static Material _material;

        public static Material Material
        {
            get
            {
                if (_material != null) return _material;
                var shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Hidden/Internal-Colored");
                _material = new Material(shader) { name = "WorldLines_Generated" };
                return _material;
            }
        }

        public static LineRenderer Create(string name, Transform parent, float width, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = Material;
            line.useWorldSpace = true;
            line.loop = loop;
            line.widthMultiplier = width;
            line.numCapVertices = loop ? 0 : 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.positionCount = 0;
            go.SetActive(false);
            return line;
        }

        public static void Ring(LineRenderer line, Vector3 center, float radius, Color color)
        {
            if (line.positionCount != RingSegments) line.positionCount = RingSegments;
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i * (Mathf.PI * 2f / RingSegments);
                line.SetPosition(i, new Vector3(center.x + Mathf.Cos(a) * radius, FloorY, center.z + Mathf.Sin(a) * radius));
            }
            SetColor(line, color);
            if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);
        }

        public static void Segment(LineRenderer line, Vector3 from, Vector3 to, Color color)
        {
            if (line.positionCount != 2) line.positionCount = 2;
            line.SetPosition(0, new Vector3(from.x, FloorY, from.z));
            line.SetPosition(1, new Vector3(to.x, FloorY, to.z));
            SetColor(line, color);
            if (!line.gameObject.activeSelf) line.gameObject.SetActive(true);
        }

        public static void Hide(LineRenderer line)
        {
            if (line != null && line.gameObject.activeSelf) line.gameObject.SetActive(false);
        }

        private static void SetColor(LineRenderer line, Color color)
        {
            if (line.startColor != color) line.startColor = line.endColor = color;
        }
    }
}
