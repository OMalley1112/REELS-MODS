using System;
using UnityEngine;
using UnityEngine.UI;

namespace ReelsMods
{
    /// <summary>
    /// A single row in the list: background bar, label, ON/OFF pill.
    /// Rows are pooled and re-texted on tab/page change, never destroyed and
    /// rebuilt - that was the other half of the RIFTIX churn.
    /// </summary>
    public class MenuRow : MonoBehaviour
    {
        public RectTransform root;
        public Image bg;
        public Text label;
        public Image pill;
        public Text pillText;

        public ModDef def;
        public Action<MenuRow> onClick;

        private Vector3 _baseScale = Vector3.one;
        private float _pop;      // 0..1 pop animation clock
        private bool _hover;

        /// <summary>
        /// The MenuRow component lives on the row root itself, so a raycast
        /// that lands on the background or the label finds it via
        /// GetComponentInParent.
        /// </summary>
        public void Build(Vector2 pos)
        {
            root = (RectTransform)transform;
            root.sizeDelta = new Vector2(Theme.CanvasW - 120f, Theme.RowH);
            root.anchoredPosition = pos;
            root.localScale = Vector3.one;
            _baseScale = root.localScale;

            bg = root.gameObject.AddComponent<Image>();
            bg.sprite = Ui.Rounded;
            bg.type = Image.Type.Sliced;
            bg.color = Theme.RowBg;
            bg.raycastTarget = true;

            label = Ui.Label(root, "", 30, TextAnchor.MiddleLeft, Theme.TextMain,
                new Vector2(-Theme.RowH * 0.5f, 0f),
                new Vector2(Theme.CanvasW - 340f, Theme.RowH - 20f));
            label.horizontalOverflow = HorizontalWrapMode.Overflow;

            // pill
            var pillRt = Ui.Rect("pill", root, new Vector2(170f, 62f),
                new Vector2((Theme.CanvasW - 120f) * 0.5f - 110f, 0f));
            pill = pillRt.gameObject.AddComponent<Image>();
            pill.sprite = Ui.Rounded;
            pill.type = Image.Type.Sliced;
            pill.color = Theme.ToggleOff;
            pill.raycastTarget = false;

            pillText = Ui.Label(pillRt, "OFF", 26, TextAnchor.MiddleCenter, Color.white,
                Vector2.zero, new Vector2(170f, 62f));
            pillText.fontStyle = FontStyle.Bold;

            var btn = root.gameObject.AddComponent<Button>();
            btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => { if (onClick != null) onClick(this); });
        }

        public void Bind(ModDef d)
        {
            def = d;
            label.text = d.label;
            SetVisual(d.on);
            root.gameObject.SetActive(true);
        }

        public void Hide()
        {
            root.gameObject.SetActive(false);
        }

        public void SetVisual(bool on)
        {
            pill.color = on ? Theme.Accent : Theme.ToggleOff;
            pillText.text = on ? "ON" : "OFF";
            pillText.color = on ? new Color(0.07f, 0.05f, 0.12f) : new Color(0.82f, 0.80f, 0.88f);
        }

        public void Toggle()
        {
            if (def == null) return;
            def.on = !def.on;
            SetVisual(def.on);
            _pop = 1f;
        }

        public void SetHover(bool h)
        {
            if (_hover == h) return;
            _hover = h;
            bg.color = h ? Theme.RowBgHover : Theme.RowBg;
        }

        private void Update()
        {
            if (_pop > 0f)
            {
                _pop -= Time.unscaledDeltaTime / 0.16f;
                if (_pop < 0f) _pop = 0f;
            }
            float k = _pop > 0f ? Mathf.Sin(_pop * Mathf.PI) * 0.06f : 0f;
            root.localScale = _baseScale * (1f + k);
        }
    }
}
