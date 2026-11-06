using UnityEngine;
using UnityEngine.UI;

namespace ReelsMods
{
    /// <summary>
    /// Vertical vertex gradient for a uGUI Text.
    ///
    /// Lesson taken from the RIFTIX font-atlas blowups: this touches vertex
    /// colours only. It never changes the string, the font, the size or the
    /// rect, so it cannot force the dynamic font to rebuild its texture.
    ///
    /// Derives from BaseMeshEffect (not IMeshModifier directly) so the
    /// obsolete ModifyMesh(Mesh) overload is handled for us.
    /// </summary>
    public class GradientText : BaseMeshEffect
    {
        public Color top = new Color(1f, 1f, 1f, 1f);
        public Color bottom = new Color(0.780f, 0.608f, 1f, 1f);

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh == null)
                return;

            int n = vh.currentVertCount;
            if (n == 0)
                return;

            var v = new UIVertex();
            float minY = 0f;
            float maxY = 0f;

            for (int i = 0; i < n; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float y = v.position.y;
                if (i == 0)
                {
                    minY = y;
                    maxY = y;
                }
                else
                {
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }

            float span = maxY - minY;
            if (span < 0.0001f) span = 1f;

            Color32 t = top;
            Color32 b = bottom;

            for (int i = 0; i < n; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                float k = (v.position.y - minY) / span;
                v.color = Color32.Lerp(b, t, k);
                vh.SetUIVertex(v, i);
            }
        }
    }
}
