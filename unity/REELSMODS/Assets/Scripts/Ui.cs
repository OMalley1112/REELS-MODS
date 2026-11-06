using UnityEngine;
using UnityEngine.UI;

namespace ReelsMods
{
    /// <summary>
    /// Everything the panel is built from, created at runtime so the scene
    /// file stays a one-line bootstrap.
    /// </summary>
    public static class Ui
    {
        private static Sprite _rounded;

        /// <summary>9-slice white rounded rect. One sprite for the whole menu.</summary>
        public static Sprite Rounded
        {
            get
            {
                if (_rounded != null) return _rounded;

                const int size = 64;
                const float radius = 14f;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;

                var clear = new Color(0, 0, 0, 0);
                var solid = new Color(1, 1, 1, 1);
                var px = new Color[size * size];

                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        // distance to the nearest corner, in corner space
                        float cx = Mathf.Max(Mathf.Max(radius - x, x - (size - 1 - radius)), 0f);
                        float cy = Mathf.Max(Mathf.Max(radius - y, y - (size - 1 - radius)), 0f);
                        float d = Mathf.Sqrt(cx * cx + cy * cy);
                        float a = Mathf.Clamp01(radius - d + 0.5f);
                        px[y * size + x] = a >= 0.999f ? solid : (a <= 0.001f ? clear : new Color(1, 1, 1, a));
                    }
                }

                tex.SetPixels(px);
                tex.Apply(false, false);
                _rounded = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16));
                _rounded.name = "RoundedRect";
                return _rounded;
            }
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            rt.localScale = Vector3.one;
            return rt;
        }

        public static Image Panel(string name, Transform parent, Vector2 size, Vector2 pos, Color color, float alpha = 1f)
        {
            var rt = Rect(name, parent, size, pos);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = Rounded;
            img.type = Image.Type.Sliced;
            img.color = color * new Color(1f, 1f, 1f, alpha);
            img.raycastTarget = false;
            return img;
        }

        /// <summary>
        /// Text with an explicit size range. No best-fit, ever: unbounded
        /// best-fit is what made the dynamic font atlas overflow in RIFTIX.
        /// </summary>
        public static Text Label(Transform parent, string content, int size, TextAnchor anchor, Color color, Vector2 pos, Vector2 box)
        {
            var rt = Rect("label", parent, box, pos);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Theme.Font;
            t.text = content;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.resizeTextForBestFit = false;
            t.supportRichText = false;
            t.raycastTarget = false;
            return t;
        }

        public static void Fit(Text t, Vector2 box, Vector2 pos)
        {
            var rt = (RectTransform)t.transform;
            rt.sizeDelta = box;
            rt.anchoredPosition = pos;
        }
    }
}
