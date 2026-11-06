using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ReelsMods
{
    /// <summary>
    /// The REELS MODS panel.
    ///
    /// Built entirely in code: one world-space canvas, a pooled row list that
    /// gets re-texted on tab/page change (never rebuilt), one shared font,
    /// fixed font sizes with best-fit disabled.
    /// </summary>
    public class MenuManager : MonoBehaviour
    {
        public static MenuManager Instance;

        private Rig rig;
        private Canvas canvas;
        private RectTransform panel;
        private RectTransform rowsRoot;
        private Text footer;
        private Text toastText;
        private GameObject toastGo;
        private LineRenderer laser;
        private EventSystem eventSystem;

        private readonly MenuRow[] rows = new MenuRow[Theme.RowsPerPage];
        private readonly RectTransform[] tabRects = new RectTransform[3];
        private readonly Text[] tabTexts = new Text[3];
        private Image underline;

        private string tab = Tabs.Reels;
        private int page;
        private int pageCount = 1;

        private bool open = true;
        private float openT = 1f;
        private float openTarget = 1f;

        private float toastTimer;
        private MenuRow hovered;

        private bool prevLeftPrimary, prevLeftSecondary;
        private bool prevRightPrimary, prevRightSecondary;
        private bool prevLeftTrigger, prevRightTrigger;

        private bool laserEnabled = true;
        private bool popEnabled = true;

        private static readonly List<RaycastResult> Results = new List<RaycastResult>();

        private bool SettingOn(string id)
        {
            var d = System.Array.Find(Mods.All, m => m.id == id);
            return d != null && d.on;
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            rig = Rig.Create();

            var esGo = new GameObject("EventSystem", typeof(EventSystem));
            esGo.AddComponent<StandaloneInputModule>();
            eventSystem = esGo.GetComponent<EventSystem>();

            BuildCanvas();
            BuildPanel();
            BuildRows();
            Rebuild();

            laser = new GameObject("Laser", typeof(LineRenderer)).GetComponent<LineRenderer>();
            laser.positionCount = 2;
            laser.startWidth = 0.004f;
            laser.endWidth = 0.001f;
            laser.material = new Material(Shader.Find("Sprites/Default"));
            laser.startColor = Theme.Accent;
            laser.endColor = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.15f);
            laser.useWorldSpace = true;
            laser.enabled = false;
        }

        // ---------------------------------------------------------------- build

        private void BuildCanvas()
        {
            var go = new GameObject("MenuCanvas", typeof(RectTransform));
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = rig.cam;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = new Vector2(Theme.CanvasW, Theme.CanvasH);
            rt.localScale = Vector3.one * Theme.CanvasScale;

            go.AddComponent<GraphicRaycaster>();
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.dynamicPixelsPerUnit = 12f;
        }

        private void BuildPanel()
        {
            panel = Ui.Rect("panel", canvas.transform, new Vector2(Theme.CanvasW, Theme.CanvasH), Vector2.zero);
            var bg = panel.gameObject.AddComponent<Image>();
            bg.sprite = Ui.Rounded;
            bg.type = Image.Type.Sliced;
            bg.color = Theme.PanelBg;
            bg.raycastTarget = true;

            // accent frame
            var frame = Ui.Rect("frame", panel, new Vector2(Theme.CanvasW - 8f, Theme.CanvasH - 8f), Vector2.zero);
            var fimg = frame.gameObject.AddComponent<Image>();
            fimg.sprite = Ui.Rounded;
            fimg.type = Image.Type.Sliced;
            fimg.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.55f);
            fimg.raycastTarget = false;
            frame.SetAsFirstSibling();

            // title
            var title = Ui.Label(panel, Theme.Title, 48, TextAnchor.MiddleCenter, Color.white,
                new Vector2(0f, Theme.TitleY), new Vector2(Theme.CanvasW - 100f, Theme.TitleH));
            title.gameObject.AddComponent<GradientText>();
            title.transform.SetSiblingIndex(1);

            // tabs
            float tabW = 260f;
            float[] xs = { -290f, 0f, 290f };
            for (int i = 0; i < 3; i++)
            {
                var rt = Ui.Rect("tab_" + Tabs.All[i], panel, new Vector2(tabW, Theme.TabH),
                    new Vector2(xs[i], Theme.TabY));
                var img = rt.gameObject.AddComponent<Image>();
                img.sprite = Ui.Rounded;
                img.type = Image.Type.Sliced;
                img.color = Theme.TabBg;
                img.raycastTarget = true;

                tabTexts[i] = Ui.Label(rt, Tabs.All[i], 30, TextAnchor.MiddleCenter, Theme.TextMuted,
                    Vector2.zero, new Vector2(tabW, Theme.TabH));
                tabTexts[i].fontStyle = FontStyle.Bold;

                var captured = Tabs.All[i];
                var btn = rt.gameObject.AddComponent<Button>();
                btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(() => SwitchTab(captured));

                tabRects[i] = rt;
            }

            underline = Ui.Panel("underline", panel, new Vector2(220f, 7f), Vector2.zero, Theme.Accent);
            underline.raycastTarget = false;

            rowsRoot = Ui.Rect("rows", panel,
                new Vector2(Theme.CanvasW - 120f, Theme.RowsPerPage * (Theme.RowH + Theme.RowGap) - Theme.RowGap),
                new Vector2(0f, Theme.RowTop - (Theme.RowsPerPage * (Theme.RowH + Theme.RowGap) - Theme.RowGap) * 0.5f));

            // footer: arrows + page counter
            BuildFooter();

            // toast
            toastGo = new GameObject("toast", typeof(RectTransform));
            var trt = (RectTransform)toastGo.transform;
            trt.SetParent(panel, false);
            trt.sizeDelta = new Vector2(Theme.CanvasW - 240f, Theme.ToastH);
            trt.anchoredPosition = new Vector2(0f, Theme.ToastY);
            var tbg = toastGo.AddComponent<Image>();
            tbg.sprite = Ui.Rounded;
            tbg.type = Image.Type.Sliced;
            tbg.color = Theme.ToastBg;
            tbg.raycastTarget = false;
            toastText = Ui.Label(trt, "", 30, TextAnchor.MiddleCenter, Theme.Accent,
                Vector2.zero, new Vector2(Theme.CanvasW - 300f, Theme.ToastH));
            toastGo.SetActive(false);
        }

        private void BuildFooter()
        {
            var prev = Ui.Rect("prev", panel, new Vector2(110f, Theme.FooterH),
                new Vector2(-380f, Theme.FooterY));
            var pimg = prev.gameObject.AddComponent<Image>();
            pimg.sprite = Ui.Rounded;
            pimg.type = Image.Type.Sliced;
            pimg.color = Theme.TabBg;
            Ui.Label(prev, "<", 40, TextAnchor.MiddleCenter, Theme.Accent,
                Vector2.zero, new Vector2(110f, Theme.FooterH));
            var pbtn = prev.gameObject.AddComponent<Button>();
            pbtn.transition = Selectable.Transition.None;
            pbtn.onClick.AddListener(PrevPage);

            var next = Ui.Rect("next", panel, new Vector2(110f, Theme.FooterH),
                new Vector2(380f, Theme.FooterY));
            var nimg = next.gameObject.AddComponent<Image>();
            nimg.sprite = Ui.Rounded;
            nimg.type = Image.Type.Sliced;
            nimg.color = Theme.TabBg;
            Ui.Label(next, ">", 40, TextAnchor.MiddleCenter, Theme.Accent,
                Vector2.zero, new Vector2(110f, Theme.FooterH));
            var nbtn = next.gameObject.AddComponent<Button>();
            nbtn.transition = Selectable.Transition.None;
            nbtn.onClick.AddListener(NextPage);

            footer = Ui.Label(panel, "", 30, TextAnchor.MiddleCenter, Theme.TextMuted,
                new Vector2(0f, Theme.FooterY), new Vector2(560f, Theme.FooterH));
        }

        private void BuildRows()
        {
            float total = Theme.RowsPerPage * (Theme.RowH + Theme.RowGap) - Theme.RowGap;
            for (int i = 0; i < Theme.RowsPerPage; i++)
            {
                var rt = Ui.Rect("row_" + i, rowsRoot,
                    new Vector2(Theme.CanvasW - 120f, Theme.RowH),
                    new Vector2(0f, total * 0.5f - Theme.RowH * 0.5f - i * (Theme.RowH + Theme.RowGap)));
                var row = rt.gameObject.AddComponent<MenuRow>();
                row.Build(rt.anchoredPosition);
                row.onClick = OnRowClick;
                rows[i] = row;
            }
        }

        // ------------------------------------------------------------- content

        private void Rebuild()
        {
            var visible = new List<ModDef>();
            for (int i = 0; i < Mods.All.Length; i++)
            {
                var m = Mods.All[i];
                if (System.Array.IndexOf(m.pages, tab) >= 0) visible.Add(m);
            }

            pageCount = Mathf.Max(1, Mathf.CeilToInt(visible.Count / (float)Theme.RowsPerPage));
            if (page >= pageCount) page = pageCount - 1;
            if (page < 0) page = 0;

            for (int i = 0; i < Theme.RowsPerPage; i++)
            {
                int idx = page * Theme.RowsPerPage + i;
                if (idx < visible.Count) rows[i].Bind(visible[idx]);
                else rows[i].Hide();
            }

            footer.text = tab + "      " + (page + 1).ToString("00") + " / " + pageCount.ToString("00");

            for (int i = 0; i < 3; i++)
            {
                bool active = Tabs.All[i] == tab;
                tabTexts[i].color = active ? Theme.Accent : Theme.TextMuted;
                tabTexts[i].fontSize = active ? 32 : 28;
            }

            var activeRt = tabRects[System.Array.IndexOf(Tabs.All, tab)];
            if (activeRt != null && underline != null)
            {
                underline.rectTransform.anchoredPosition = new Vector2(activeRt.anchoredPosition.x,
                    Theme.TabY - Theme.TabH * 0.5f - 8f);
                underline.rectTransform.sizeDelta = new Vector2(activeRt.sizeDelta.x - 30f, 7f);
            }
        }

        private void SwitchTab(string t)
        {
            if (tab == t) return;
            tab = t;
            page = 0;
            Rebuild();
        }

        private void NextPage() { if (page + 1 < pageCount) { page++; Rebuild(); } else ShowToast("Last page"); }
        private void PrevPage() { if (page > 0) { page--; Rebuild(); } else ShowToast("First page"); }

        private void OnRowClick(MenuRow r)
        {
            if (r == null || r.def == null || !open) return;

            switch (r.def.id)
            {
                case "close_menu":
                    SetOpen(false);
                    return;
                case "page_next":
                    NextPage();
                    return;
                case "page_prev":
                    PrevPage();
                    return;
                case "about":
                    ShowToast("REELS MODS 0.2.0 - standalone menu");
                    return;
                case "save_loadout":
                    ShowToast("Loadout saved");
                    return;
                case "reset_loadout":
                    ShowToast("Loadout reset");
                    return;
            }

            r.Toggle();

            switch (r.def.id)
            {
                case "set_hand":
                    rig.RightHanded = r.def.on;
                    ShowToast(r.def.label + ": " + (r.def.on ? "RIGHT" : "LEFT"));
                    return;
                case "set_laser":
                    laserEnabled = r.def.on;
                    break;
                case "set_pop":
                    popEnabled = r.def.on;
                    if (!popEnabled) for (int i = 0; i < rows.Length; i++) rows[i].enabled = false;
                    else for (int i = 0; i < rows.Length; i++) rows[i].enabled = true;
                    break;
                case "set_big":
                    canvas.transform.localScale = Vector3.one * (r.def.on ? Theme.CanvasScale * 1.35f : Theme.CanvasScale);
                    break;
                case "set_notif":
                    if (!r.def.on) HideToast();
                    break;
            }

            ShowToast(r.def.label + ": " + (r.def.on ? "ON" : "OFF"));
        }

        // --------------------------------------------------------------- toast

        private void ShowToast(string msg)
        {
            if (!SettingOn("set_notif")) return;
            toastText.text = msg;
            toastTimer = 1.8f;
            toastGo.SetActive(true);
            var rt = (RectTransform)toastGo.transform;
            rt.anchoredPosition = new Vector2(0f, Theme.ToastY - 40f);
            var img = toastGo.GetComponent<Image>();
            img.color = new Color(Theme.ToastBg.r, Theme.ToastBg.g, Theme.ToastBg.b, 0f);
        }

        private void HideToast()
        {
            toastTimer = 0f;
            toastGo.SetActive(false);
        }

        private void UpdateToast()
        {
            if (toastTimer <= 0f || !toastGo.activeSelf) return;
            toastTimer -= Time.unscaledDeltaTime;

            float k = Mathf.Clamp01(toastTimer / 1.8f);
            float slide = Mathf.Lerp(0f, 1f, Mathf.Clamp01((1.8f - toastTimer) / 0.25f));
            float alpha = toastTimer < 0.35f ? toastTimer / 0.35f : 1f;

            var rt = (RectTransform)toastGo.transform;
            rt.anchoredPosition = new Vector2(0f, Mathf.Lerp(Theme.ToastY - 40f, Theme.ToastY, slide));
            var img = toastGo.GetComponent<Image>();
            img.color = new Color(Theme.ToastBg.r, Theme.ToastBg.g, Theme.ToastBg.b, 0.97f * alpha);
            toastText.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, alpha);

            if (toastTimer <= 0f) HideToast();
        }

        // --------------------------------------------------------------- input

        private void SetOpen(bool v)
        {
            open = v;
            openTarget = v ? 1f : 0f;
            if (v)
            {
                panel.gameObject.SetActive(true);
                openT = Mathf.Max(openT, 0.02f);
            }
        }

        private void ToggleOpen() { SetOpen(!open); }

        private void Update()
        {
            UpdateToast();

            // open / close tween
            if (!Mathf.Approximately(openT, openTarget))
            {
                float speed = Time.unscaledDeltaTime / 0.18f;
                openT = Mathf.MoveTowards(openT, openTarget, speed);
                float e = 1f - Mathf.Pow(1f - openT, 3f);
                panel.localScale = Vector3.one * Mathf.Lerp(0.82f, 1f, e);
                if (!open && openT <= 0.001f)
                {
                    panel.gameObject.SetActive(false);
                    openT = 0f;
                }
            }

            ReadButtons();
            UpdatePointer();

            // panel floats just below eye line, facing the head
            if (panel != null && panel.gameObject.activeSelf && rig != null)
            {
                Vector3 pos = rig.HeadPos + rig.HeadRot * new Vector3(0f, -0.06f, 0.55f);
                panel.position = pos;
                Vector3 away = pos - rig.HeadPos;
                if (away.sqrMagnitude > 0.00001f)
                    panel.rotation = Quaternion.LookRotation(away, rig.HeadRot * Vector3.up);
            }
        }

        private void ReadButtons()
        {
            if (rig == null) return;

            // X or Y on the left hand, A or B on the right hand -> open/close
            bool leftEdge = (rig.LeftPrimary && !prevLeftPrimary) || (rig.LeftSecondary && !prevLeftSecondary);
            bool rightEdge = (rig.RightPrimary && !prevRightPrimary) || (rig.RightSecondary && !prevRightSecondary);

            if (leftEdge || rightEdge) ToggleOpen();

            if (open)
            {
                bool lt = rig.LeftTrigger && !prevLeftTrigger;
                bool rt = rig.RightTrigger && !prevRightTrigger;
                if (lt || rt) ActivateHovered();
            }

            // keyboard fallback for desk testing
            if (Input.GetKeyDown(KeyCode.Y) || Input.GetKeyDown(KeyCode.X)) ToggleOpen();
            if (Input.GetKeyDown(KeyCode.Escape) && open) SetOpen(false);
            if (open && Input.GetKeyDown(KeyCode.RightArrow)) NextPage();
            if (open && Input.GetKeyDown(KeyCode.LeftArrow)) PrevPage();

            prevLeftPrimary = rig.LeftPrimary;
            prevLeftSecondary = rig.LeftSecondary;
            prevRightPrimary = rig.RightPrimary;
            prevRightSecondary = rig.RightSecondary;
            prevLeftTrigger = rig.LeftTrigger;
            prevRightTrigger = rig.RightTrigger;
        }

        private void UpdatePointer()
        {
            if (!open || rig == null || eventSystem == null)
            {
                SetLaser(false);
                return;
            }

            Vector2 screen;
            bool useMouse = !rig.xrActive;

            if (useMouse)
            {
                screen = Input.mousePosition;
                if (Input.GetMouseButtonDown(0)) { HoverAt(screen); ActivateHovered(); return; }
            }
            else
            {
                var ray = new Ray(rig.HandPos, rig.HandRot * Vector3.forward);
                var plane = new Plane(panel.forward, panel.position);
                if (!plane.Raycast(ray, out float enter))
                {
                    SetHovered(null);
                    SetLaser(false);
                    return;
                }
                Vector3 hit = ray.GetPoint(enter);
                screen = rig.cam.WorldToScreenPoint(hit);
                SetLaser(true, rig.HandPos, hit);
            }

            HoverAt(screen);
        }

        private void HoverAt(Vector2 screen)
        {
            var ped = new PointerEventData(eventSystem) { position = screen };
            Results.Clear();
            eventSystem.RaycastAll(ped, Results);

            MenuRow found = null;
            for (int i = 0; i < Results.Count; i++)
            {
                var r = Results[i].gameObject.GetComponentInParent<MenuRow>();
                if (r != null) { found = r; break; }
            }
            SetHovered(found);
        }

        private void SetHovered(MenuRow r)
        {
            if (hovered == r) return;
            if (hovered != null) hovered.SetHover(false);
            hovered = r;
            if (hovered != null) hovered.SetHover(true);
        }

        private void ActivateHovered()
        {
            if (hovered != null && hovered.gameObject.activeInHierarchy)
                hovered.onClick?.Invoke(hovered);
        }

        private void SetLaser(bool on, Vector3? from = null, Vector3? to = null)
        {
            if (laser == null) return;
            bool show = on && laserEnabled && rig != null && rig.xrActive;
            if (laser.enabled != show) laser.enabled = show;
            if (!show) return;
            if (from.HasValue && to.HasValue)
            {
                laser.SetPosition(0, from.Value);
                laser.SetPosition(1, to.Value);
            }
        }
    }
}
