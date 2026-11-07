// ============================================================================
// REELS MODS — in-game mod menu injector for Reel (com.mostsanestudios.reel)
//
// Loaded as lib/arm64-v8a/libmain.so. Forwards JNI_OnLoad to the original
// libmain_real.so, waits for il2cpp, hooks VRFishGame.Update, and draws a
// world-space TMP menu in front of the camera.
//
// Controls (Quest controllers, legacy Unity input):
//   X          open / close menu
//   Y or A     use selected row
//   up / down  move selection
// ============================================================================
#ifndef _GNU_SOURCE
#define _GNU_SOURCE
#endif
#include <jni.h>
#include <dlfcn.h>
#include <pthread.h>
#include <unistd.h>
#include <string.h>
#include <stdio.h>
#include <stdlib.h>
#include <stdarg.h>
#include <stdint.h>
#include <errno.h>
#include <time.h>
#include <sys/mman.h>
#include <android/log.h>

#ifndef MAP_FIXED_NOREPLACE
#define MAP_FIXED_NOREPLACE 0x100000
#endif

#define TAG "REELS_MODS"
#define LOGI(...) __android_log_print(ANDROID_LOG_INFO,  TAG, __VA_ARGS__)
#define LOGE(...) __android_log_print(ANDROID_LOG_ERROR, TAG, __VA_ARGS__)

// ---------------------------------------------------------------- il2cpp api
typedef void*        (*fn_domain_get)();
typedef const void** (*fn_domain_get_assemblies)(const void* domain, size_t* count);
typedef const void*  (*fn_assembly_get_image)(const void* assembly);
typedef void*        (*fn_class_from_name)(const void* image, const char* ns, const char* name);
typedef const void*  (*fn_class_get_method_from_name)(void* klass, const char* name, int args);
typedef void*        (*fn_class_get_field_from_name)(void* klass, const char* name);
typedef void*        (*fn_class_get_type)(void* klass);
typedef void*        (*fn_type_get_object)(const void* type);
typedef void*        (*fn_object_new)(void* klass);
typedef void*        (*fn_runtime_invoke)(const void* method, void* obj, void** params, void** exc);
typedef void         (*fn_field_set_value)(void* obj, void* field, const void* value);
typedef void         (*fn_field_get_value)(void* obj, void* field, void* value);
typedef void         (*fn_field_static_get_value)(void* field, void* value);
typedef void*        (*fn_string_new)(const char* str);
typedef void         (*fn_thread_attach)(void* domain);
typedef void*        (*fn_object_get_class)(void* obj);
typedef void*        (*fn_object_unbox)(void* obj);
typedef void*        (*fn_array_new)(void* element_klass, size_t length);
typedef const char*  (*fn_method_get_name)(const void* method);

struct Api {
    fn_domain_get                 domain_get;
    fn_domain_get_assemblies      domain_get_assemblies;
    fn_assembly_get_image         assembly_get_image;
    fn_class_from_name            class_from_name;
    fn_class_get_method_from_name class_get_method_from_name;
    fn_class_get_field_from_name  class_get_field_from_name;
    fn_class_get_type             class_get_type;
    fn_type_get_object            type_get_object;
    fn_object_new                 object_new;
    fn_runtime_invoke             runtime_invoke;
    fn_field_set_value            field_set_value;
    fn_field_get_value            field_get_value;
    fn_field_static_get_value     field_static_get_value;
    fn_string_new                 string_new;
    fn_thread_attach              thread_attach;
    fn_object_get_class           object_get_class;
    fn_object_unbox               object_unbox;
    fn_array_new                  array_new;
    fn_method_get_name            method_get_name;
};
static Api api;

// ---------------------------------------------------------------- structs
struct Vector2 { float x, y; };
struct Vector3 { float x, y, z; };
struct Quaternion { float x, y, z, w; };
struct Color { float r, g, b, a; };

// ---------------------------------------------------------------- resolved
static void* g_domain = nullptr;

// classes
static void* kVRFishGame;
static void* kGameProgression;
static void* kPlayerProfile;
static void* kInput;
static void* kGameObject;
static void* kTransform;
static void* kRectTransform;
static void* kCamera;
static void* kObjectClass;
static void* kCollider;
static void* kRenderer;
static void* kMaterial;
static void* kCanvas;
static void* kTMPText;
static void* kTMPSettings;
static void* kTypeClass;

// fields
static void* fInstance;
static void* fProgression;
static void* fProfile;
static void* fChanged;
static void* fCoins;
static void* fXp;
static void* fCrowns;
static void* fCandy;
static void* fMedals;

// methods
static const void* mGetKeyDown;
static const void* mCamMain;
static const void* mGetTransform;
static const void* mCreatePrimitive;
static const void* mGoCtorTypes;
static const void* mAddComponent;
static const void* mGetComponent;
static const void* mSetActive;
static const void* mSetParent;
static const void* mSetLocalPos;
static const void* mSetLocalRot;
static const void* mSetLocalScale;
static const void* mSetRenderMode;
static const void* mSetAnchorMin;
static const void* mSetAnchorMax;
static const void* mSetSizeDelta;
static const void* mSetText;
static const void* mSetFontSize;
static const void* mSetColor;
static const void* mSetAlignment;
static const void* mSetFont;
static const void* mGetDefaultFont;
static const void* mDestroy;
static const void* mGetMaterial;
static const void* mSetMatColor;

static const void* mUpdate;

// menu state (main thread only)
static void* uiCanvasGO;
static void* uiPanelGO;
static void* uiText;
static bool  gOpen;
static int   gSel;
static int   gLastKey = -1;
static int   gStatusFrames;
static char  gStatus[96];
static char  gLastText[2048];
static bool  uiBuilt;
static int   gTick;

static bool build_menu();
static void menu_tick();

// ---------------------------------------------------------------- helpers
static void* find_class(const char* ns, const char* name) {
    if (!g_domain) return nullptr;
    size_t n = 0;
    const void** asms = api.domain_get_assemblies(g_domain, &n);
    if (!asms) return nullptr;
    for (size_t i = 0; i < n; i++) {
        const void* img = api.assembly_get_image(asms[i]);
        if (!img) continue;
        void* k = api.class_from_name(img, ns, name);
        if (k) return k;
    }
    return nullptr;
}

static const void* find_method(void* klass, const char* name, int argc) {
    if (!klass) return nullptr;
    return api.class_get_method_from_name(klass, name, argc);
}

static void* find_field(void* klass, const char* name) {
    if (!klass) return nullptr;
    return api.class_get_field_from_name(klass, name);
}

static void* inv(const void* m, void* obj, void* a1 = nullptr, void* a2 = nullptr, void* a3 = nullptr) {
    if (!m) return nullptr;
    void* params[3] = { a1, a2, a3 };
    void* exc = nullptr;
    void* r = api.runtime_invoke(m, obj, params, &exc);
    if (exc) {
        LOGE("exception in %s", api.method_get_name ? api.method_get_name(m) : "?");
        return nullptr;
    }
    return r;
}

static void* typeof_k(void* klass) {
    if (!klass) return nullptr;
    return api.type_get_object(api.class_get_type(klass));
}

static bool key_down(int key) {
    if (!mGetKeyDown) return false;
    int k = key;
    void* params[1] = { &k };
    void* exc = nullptr;
    void* r = api.runtime_invoke(mGetKeyDown, nullptr, params, &exc);
    if (exc) {
        static bool logged = false;
        if (!logged) { LOGE("GetKeyDown threw - legacy input likely disabled"); logged = true; }
        return false;
    }
    if (!r) return false;
    return *(bool*)api.object_unbox(r);
}

// ---------------------------------------------------------------- profile
static void* get_instance() {
    if (!fInstance) return nullptr;
    void* inst = nullptr;
    api.field_static_get_value(fInstance, &inst);
    return inst;
}

static void* get_progression() {
    void* inst = get_instance();
    if (!inst || !fProgression) return nullptr;
    void* p = nullptr;
    api.field_get_value(inst, fProgression, &p);
    return p;
}

static void* get_profile() {
    void* prog = get_progression();
    if (!prog || !fProfile) return nullptr;
    void* prof = nullptr;
    api.field_get_value(prog, fProfile, &prof);
    return prof;
}

static int prof_int(void* prof, void* field) {
    int v = 0;
    if (prof && field) api.field_get_value(prof, field, &v);
    return v;
}

static void fire_changed() {
    void* prog = get_progression();
    if (!prog || !fChanged) return;
    void* del = nullptr;
    api.field_get_value(prog, fChanged, &del);
    if (!del) return;
    void* klass = api.object_get_class(del);
    const void* m = find_method(klass, "Invoke", 0);
    if (m) inv(m, del);
}

static void set_status(const char* fmt, ...) {
    va_list ap;
    va_start(ap, fmt);
    vsnprintf(gStatus, sizeof(gStatus), fmt, ap);
    va_end(ap);
    gStatusFrames = 240;
}

// ---------------------------------------------------------------- actions
static void give(void* field, int delta, int cap, const char* label) {
    void* prof = get_profile();
    if (!prof || !field) { set_status("NO PROFILE YET"); return; }
    int v = 0;
    api.field_get_value(prof, field, &v);
    long long nv = (long long)v + delta;
    if (nv > cap) nv = cap;
    if (nv < 0) nv = 0;
    int out = (int)nv;
    api.field_set_value(prof, field, &out);
    fire_changed();
    set_status("%s -> %d", label, out);
    LOGI("give %s: %d -> %d", label, v, out);
}

static void max_all() {
    void* prof = get_profile();
    if (!prof) { set_status("NO PROFILE YET"); return; }
    struct { void* f; int v; const char* n; } sets[] = {
        { fCoins,  1000000000, "COINS"  },
        { fCrowns, 1000000,    "CROWNS" },
        { fCandy,  10000000,   "CANDY"  },
        { fMedals, 10000000,   "MEDALS" },
        { fXp,     10000000,   "XP"     },
    };
    for (size_t i = 0; i < sizeof(sets) / sizeof(sets[0]); i++) {
        if (sets[i].f) api.field_set_value(prof, sets[i].f, &sets[i].v);
    }
    fire_changed();
    set_status("ALL MAXED");
    LOGI("max_all applied");
}

static void activate() {
    switch (gSel) {
        case 0: give(fCoins,  1000000, 1000000000, "COINS");  break;
        case 1: give(fCrowns, 1000,    1000000,    "CROWNS"); break;
        case 2: give(fCandy,  10000,   10000000,   "CANDY");  break;
        case 3: give(fMedals, 10000,   10000000,   "MEDALS"); break;
        case 4: give(fXp,     50000,   10000000,   "XP");     break;
        case 5: max_all(); break;
    }
}

// ---------------------------------------------------------------- text
static void fmt_int(char* dst, int v) {
    bool neg = v < 0;
    unsigned u = (unsigned)(neg ? -v : v);
    char tmp[16];
    int i = 0;
    if (u == 0) tmp[i++] = '0';
    while (u > 0) { tmp[i++] = (char)('0' + (u % 10)); u /= 10; }
    int out = 0;
    if (neg) dst[out++] = '-';
    for (int g = 0; g < i; g++) {
        if (g > 0 && ((i - g) % 3) == 0) dst[out++] = ',';
        dst[out++] = tmp[i - 1 - g];
    }
    dst[out] = 0;
}

static const char* kRows[6] = {
    "GIVE COINS  +1,000,000",
    "GIVE CROWNS +1,000",
    "GIVE CANDY  +10,000",
    "GIVE MEDALS +10,000",
    "GIVE XP     +50,000",
    "MAX EVERYTHING",
};

static void rebuild_text() {
    if (!uiText) return;
    void* prof = get_profile();
    char coins[24] = "-", xp[24] = "-", crowns[24] = "-", candy[24] = "-", medals[24] = "-";
    if (prof) {
        fmt_int(coins,  prof_int(prof, fCoins));
        fmt_int(xp,     prof_int(prof, fXp));
        fmt_int(crowns, prof_int(prof, fCrowns));
        fmt_int(candy,  prof_int(prof, fCandy));
        fmt_int(medals, prof_int(prof, fMedals));
    }

    char buf[2048];
    char* p = buf;
    size_t rem = sizeof(buf);
    #define ADD(...) do { \
        int w = snprintf(p, rem, __VA_ARGS__); \
        if (w < 0) w = 0; \
        if ((size_t)w >= rem) w = (int)rem - 1; \
        p += w; rem -= (size_t)w; \
    } while (0)

    ADD("<color=#00FF66>R E E L S   M O D S</color>\n");
    ADD("COINS <color=#FFD700>%s</color>  CROWNS %s\n", coins, crowns);
    ADD("XP %s  CANDY %s  MEDALS %s\n", xp, candy, medals);
    for (int i = 0; i < 6 && rem > 128; i++) {
        if (i == gSel) ADD("<color=#FFD700>&gt; %s</color>\n", kRows[i]);
        else           ADD("  %s\n", kRows[i]);
    }
    if (gStatusFrames > 0 && rem > 64) ADD("<color=#7FD7FF>%s</color>\n", gStatus);
    if (rem > 40) ADD("X CLOSE  Y USE  UP/DOWN  key:%d", gLastKey);
    #undef ADD

    if (strcmp(buf, gLastText) != 0) {
        strncpy(gLastText, buf, sizeof(gLastText) - 1);
        gLastText[sizeof(gLastText) - 1] = 0;
        void* s = api.string_new(buf);
        inv(mSetText, uiText, s);
    }
}

// ---------------------------------------------------------------- UI build
static void* new_go_types(const char* name, void** types, int ntypes) {
    void* arr = nullptr;
    if (ntypes > 0 && kTypeClass) {
        arr = api.array_new(kTypeClass, (size_t)ntypes);
        if (!arr) { LOGE("array_new failed"); return nullptr; }
        void** elems = (void**)((char*)arr + 0x20);
        for (int i = 0; i < ntypes; i++) elems[i] = types[i];
    }
    void* go = api.object_new(kGameObject);
    if (!go) { LOGE("object_new(GameObject) failed"); return nullptr; }
    void* s = api.string_new(name);
    void* params[2] = { s, arr };
    void* exc = nullptr;
    api.runtime_invoke(mGoCtorTypes, go, params, &exc);
    if (exc) { LOGE("GameObject ctor failed"); return nullptr; }
    return go;
}

static void set_tr(void* tr, float px, float py, float pz, float sx, float sy, float sz) {
    Vector3 p = { px, py, pz };
    Quaternion q = { 0, 0, 0, 1 };
    Vector3 s = { sx, sy, sz };
    inv(mSetLocalPos, tr, &p);
    inv(mSetLocalRot, tr, &q);
    inv(mSetLocalScale, tr, &s);
}

static bool build_menu() {
    void* cam = inv(mCamMain, nullptr);
    if (!cam) { LOGE("Camera.main null"); return false; }
    void* camTr = inv(mGetTransform, cam);
    if (!camTr) { LOGE("camera transform null"); return false; }

    // ---- panel quad (dark backdrop behind the text) ----
    if (mCreatePrimitive) {
        int pt = 5; // PrimitiveType.Quad
        void* panelGO = inv(mCreatePrimitive, nullptr, &pt);
        if (panelGO) {
            void* tr = inv(mGetTransform, panelGO);
            inv(mSetParent, tr, camTr);
            set_tr(tr, 0.0f, -0.02f, 1.45f, 0.95f, 0.62f, 1.0f);
            void* colType = typeof_k(kCollider);
            if (colType && mDestroy) {
                void* col = inv(mGetComponent, panelGO, colType);
                if (col) inv(mDestroy, nullptr, col);
            }
            if (kRenderer && mGetMaterial && mSetMatColor) {
                void* rendType = typeof_k(kRenderer);
                void* rend = inv(mGetComponent, panelGO, rendType);
                if (rend) {
                    void* mat = inv(mGetMaterial, rend);
                    if (mat) {
                        Color c = { 0.02f, 0.03f, 0.05f, 1.0f };
                        inv(mSetMatColor, mat, &c);
                    }
                }
            }
            uiPanelGO = panelGO;
        }
    }
    if (!uiPanelGO) LOGE("panel quad failed (menu will be text only)");

    // ---- canvas (world space, in front of camera) ----
    void* rectType = typeof_k(kRectTransform);
    if (!rectType) { LOGE("RectTransform type null"); return false; }
    void* canvasTypes[1] = { rectType };
    void* canvasGO = new_go_types("reels_mods_canvas", canvasTypes, 1);
    if (!canvasGO) return false;

    {
        void* t = typeof_k(kCanvas);
        void* exc = nullptr;
        void* p[1] = { t };
        void* canvasComp = api.runtime_invoke(mAddComponent, canvasGO, p, &exc);
        if (exc || !canvasComp) { LOGE("AddComponent(Canvas) failed"); return false; }
        int mode = 2; // WorldSpace
        inv(mSetRenderMode, canvasComp, &mode);
    }
    void* cTr = inv(mGetTransform, canvasGO);
    inv(mSetParent, cTr, camTr);
    set_tr(cTr, 0.0f, -0.02f, 1.4f, 0.01f, 0.01f, 0.01f);

    // ---- text ----
    void* textTypes[1] = { rectType };
    void* textGO = new_go_types("reels_mods_text", textTypes, 1);
    if (!textGO) return false;
    void* tTr = inv(mGetTransform, textGO);
    inv(mSetParent, tTr, cTr);
    set_tr(tTr, 0.0f, 0.0f, 0.01f, 1.0f, 1.0f, 1.0f);

    void* tmpType = typeof_k(kTMPText);
    void* exc = nullptr;
    void* p[1] = { tmpType };
    void* tmp = api.runtime_invoke(mAddComponent, textGO, p, &exc);
    if (exc || !tmp) { LOGE("AddComponent(TextMeshProUGUI) failed"); return false; }

    if (mGetDefaultFont) {
        void* font = inv(mGetDefaultFont, nullptr);
        if (font && mSetFont) inv(mSetFont, tmp, font);
        else LOGE("default TMP font null - text may not render");
    }
    float fs = 4.0f;
    inv(mSetFontSize, tmp, &fs);
    Color col = { 0.85f, 0.95f, 1.0f, 1.0f };
    inv(mSetColor, tmp, &col);
    int align = 514; // TextAlignmentOptions.Center
    inv(mSetAlignment, tmp, &align);

    if (mSetAnchorMin && mSetAnchorMax && mSetSizeDelta) {
        void* rect = inv(mGetComponent, textGO, rectType);
        if (rect) {
            Vector2 half = { 0.5f, 0.5f };
            inv(mSetAnchorMin, rect, &half);
            inv(mSetAnchorMax, rect, &half);
            Vector2 size = { 175.0f, 100.0f };
            inv(mSetSizeDelta, rect, &size);
        } else {
            LOGE("text GO has no RectTransform");
        }
    }

    uiCanvasGO = canvasGO;
    uiText = tmp;
    uiBuilt = true;

    bool closed = false;
    inv(mSetActive, uiCanvasGO, &closed);
    if (uiPanelGO) inv(mSetActive, uiPanelGO, &closed);

    LOGI("menu built: canvas=%p text=%p panel=%p", canvasGO, tmp, uiPanelGO);
    return true;
}

static void set_open(bool open) {
    gOpen = open;
    if (!uiBuilt) return;
    inv(mSetActive, uiCanvasGO, &open);
    if (uiPanelGO) inv(mSetActive, uiPanelGO, &open);
    if (open) {
        gLastText[0] = 0;
        rebuild_text();
    }
    LOGI("menu %s", open ? "OPEN" : "CLOSED");
}

static void menu_tick() {
    gTick++;
    if (gStatusFrames > 0) gStatusFrames--;

    if (key_down(120)) { // X
        if (!gOpen) {
            if (uiBuilt || build_menu()) set_open(true);
            else LOGE("build_menu failed, will retry on next X");
        } else {
            set_open(false);
        }
    }
    if (!gOpen) return;

    // debug: remember last pressed key
    for (int k = 0; k <= 400; k++) {
        if (k != 120 && key_down(k)) gLastKey = k;
    }

    if (key_down(121) || key_down(97) || key_down(350)) { // Y, A, joystick0
        activate();
        gLastText[0] = 0;
        rebuild_text();
    }
    if (key_down(274) || key_down(277)) gSel = (gSel + 1) % 6;   // down / right
    if (key_down(273) || key_down(276)) gSel = (gSel + 5) % 6;   // up / left

    if ((gTick % 6) == 0 || gStatusFrames == 1) rebuild_text();
}

// ---------------------------------------------------------------- hook
typedef void (*UpdateFn)(void* self);
static UpdateFn g_tramp = nullptr;

static bool is_pcrel(uint32_t ins) {
    if ((ins & 0x1F000000) == 0x10000000) return true;              // ADR
    if ((ins & 0x9F000000) == 0x90000000) return true;              // ADRP
    uint32_t op = ins & 0x7F000000;
    if (op == 0x14000000) return true;                              // B / BL
    if ((ins & 0xFF000010) == 0x54000000) return true;              // B.cond
    if ((ins & 0x7E000000) == 0x34000000) return true;              // CBZ/CBNZ
    if ((ins & 0x7E000000) == 0x36000000) return true;              // TBZ/TBNZ
    if ((ins & 0x3B000000) == 0x18000000) return true;              // LDR/PRFM literal
    return false;
}

// Absolute address a PC-relative instruction refers to. Used to REWRITE those
// instructions as position-independent code, so the trampoline may live
// anywhere in the address space (see hooked_update trampoline notes).
static bool pcrel_target(uint32_t ins, uintptr_t pc, uint64_t* out) {
    if ((ins & 0x9F000000u) == 0x90000000u) {                      // ADRP
        int64_t imm = (int64_t)((((ins >> 5) & 0x7FFFF) << 2) | ((ins >> 29) & 3));
        imm = (imm << 43) >> 43;
        *out = (uint64_t)((pc & ~0xFFFULL) + ((uintptr_t)imm << 12));
        return true;
    }
    if ((ins & 0x1F000000u) == 0x10000000u) {                      // ADR
        int64_t imm = (int64_t)((((ins >> 5) & 0x7FFFF) << 2) | ((ins >> 29) & 3));
        imm = (imm << 43) >> 43;
        *out = (uint64_t)((int64_t)pc + imm);
        return true;
    }
    if ((ins & 0x3B000000u) == 0x18000000u) {                      // LDR literal
        int64_t imm = (int64_t)((ins >> 5) & 0x7FFFF);
        imm = (imm << 45) >> 45;
        *out = (uint64_t)((int64_t)pc + (imm << 2));
        return true;
    }
    return false;
}

// ---------------------------------------------------------------- auto grant
// The standalone Reel Mods APK only launches Reel - the money has to be
// granted from inside the game. As soon as the profile object exists we
// max every currency automatically, then re-apply twice more (10 s / 30 s
// later) to outrun a server sync that might roll the wallet back.
static int gAutoFrame   = 0;
static int gAutoPasses  = 0;
static int gAutoNextAt  = 0;
static int gAutoDone    = 0;

static void auto_grant_tick() {
    if (gAutoDone) return;
    gAutoFrame++;
    if (!get_profile()) return;                       // progression not up yet
    if (gAutoFrame < gAutoNextAt) return;             // waiting out a re-apply delay

    max_all();                                        // logs "max_all applied"
    gAutoPasses++;
    set_status("ALL COINS GRANTED");
    LOGI("auto grant pass %d at frame %d", gAutoPasses, gAutoFrame);

    if (gAutoPasses >= 3) {
        gAutoDone = 1;
        LOGI("auto grant complete: coins/crowns/candy/medals/xp maxed");
    } else {
        // ~600 frames = 10 s, then ~1800 = 30 s (at 60 fps)
        gAutoNextAt = gAutoFrame + (gAutoPasses == 1 ? 600 : 1800);
    }
}

static bool resolve_core();
static bool g_coreOk = false;

// VRFishGame.Update hook body. This is the ONLY place il2cpp is ever called
// from (bug #7): it runs on UnityMain *inside* a managed method, so the
// runtime is provably initialised. The worker used to probe il2cpp from a
// fixed 4 s timer, and on a cold start il2cpp_init was still running when
// the timer expired - the first domain_get() segfaulted (reels-worker,
// x8=0x135, null deref) and took the whole game down. It worked on warm
// starts and died on cold ones: a race we can no longer lose.
static void hooked_update(void* self) {
    if (g_tramp) g_tramp(self);          // original Update first
    if (!g_coreOk) g_coreOk = resolve_core();
    if (g_coreOk) {
        menu_tick();
        auto_grant_tick();
    }
}

// ------------------------------------------------------- trampoline placement
// Bug #6: the trampoline was mmap'd wherever the kernel wanted (~8 GB away
// from libil2cpp). The relocated `adrp x20` then needed an immediate of
// +/-8 GB, but ADRP only encodes +/-4 GB, so the low bit overflowed and x20
// came back page-aligned garbage -> first instruction after the trampoline
// returned died (SIGSEGV SEGV_MAPERR, pc = target+16, x20 = 0x...f000).
// Allocate the page CLOSE to the target instead, and refuse the hook if any
// relocated PC-relative instruction would still be out of range.
static void* mmap_near(uintptr_t near, size_t len) {
    uintptr_t base = near & ~(uintptr_t)0xFFF;
    // Walk outward from the target so the ADRP/ADR immediates stay tiny.
    static const uintptr_t kOff[] = {
        0x100000, 0x200000, 0x400000, 0x800000, 0x1000000, 0x2000000,
        0x4000000, 0x8000000, 0x10000000, 0x20000000, 0x40000000, 0x70000000,
    };
    for (size_t i = 0; i < sizeof(kOff) / sizeof(kOff[0]); i++) {
        uintptr_t cands[2] = { base + kOff[i], base - kOff[i] };
        for (int c = 0; c < 2; c++) {
            uintptr_t a = cands[c] & ~(uintptr_t)0xFFF;
            if (a < 0x10000000 || a > 0x7FFFFFFFFFFFull) continue;
            void* p = mmap((void*)a, len, PROT_READ | PROT_WRITE,
                           MAP_PRIVATE | MAP_ANONYMOUS | MAP_FIXED_NOREPLACE, -1, 0);
            if (p != MAP_FAILED) return p;
            // MAP_FIXED_NOREPLACE unsupported (old kernel) -> plain hint.
            if (errno == EINVAL) {
                p = mmap((void*)a, len, PROT_READ | PROT_WRITE,
                         MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
                if (p != MAP_FAILED && (uintptr_t)p == a) return p;
                if (p != MAP_FAILED) munmap(p, len);
            }
        }
    }
    void* p = mmap(nullptr, len, PROT_READ | PROT_WRITE,
                   MAP_PRIVATE | MAP_ANONYMOUS, -1, 0);
    return (p == MAP_FAILED) ? nullptr : p;
}

// Address the hook was actually installed at - cross-checked against the
// MethodInfo the runtime reports, so an RVA mistake shows up in logcat
// instead of silently doing nothing.
static void* g_hookTarget = nullptr;

static bool install_hook(void* target) {
    uint32_t* t = (uint32_t*)target;
    uint32_t ins[4];
    memcpy(ins, t, sizeof(ins));

    // Prologue of VRFishGame.Update in this build, verified against
    // tools/game/libil2cpp.so at RVA 0x6F7BCD4:
    //   stp  x30, x21, [sp, #-32]!
    //   stp  x20, x19, [sp, #16]
    //   adrp x20, ...
    //   mov  x19, x0
    // (A9BE57FE A9014FF4 90009DB4 AA0003F3)
    // The old check demanded `sub sp, sp, #imm` (0xD10103FF) plus a PC-rel
    // instruction at slot 3 - neither exists here, so install_hook always
    // aborted with "unexpected bytes" and the menu never appeared.
    auto is_stp_to_sp = [](uint32_t i) {
        return (i & 0xFF0003E0u) == 0xA90003E0u;   // 64-bit STP, Rn = sp
    };
    bool ok = is_stp_to_sp(ins[0]) && is_stp_to_sp(ins[1]) &&
              (ins[2] & 0x9F000000u) == 0x90000000u &&   // ADRP (relocatable)
              !is_pcrel(ins[3]);                          // plain ALU / mov
    if (!ok) {
        LOGE("hook abort: unexpected bytes %08X %08X %08X %08X", ins[0], ins[1], ins[2], ins[3]);
        return false;
    }
    // The trampoline rewrites ADR / ADRP / LDR-literal as position-
    // independent code. A PC-rel *branch* in the first 4 instructions cannot
    // be rewritten that way (its destination is code, not a constant we can
    // materialise safely), so refuse it up front - and refuse FP/SIMD
    // literals too, which we do not decode.
    for (int i = 0; i < 4; i++) {
        if (is_pcrel(ins[i]) &&
            !((ins[i] & 0x9F000000u) == 0x90000000u ||   // ADRP
              (ins[i] & 0x1F000000u) == 0x10000000u ||   // ADR
              (ins[i] & 0x3B000000u) == 0x18000000u)) {  // LDR literal
            LOGE("hook abort: unrelocatable PC-rel insn %d (%08X)", i, ins[i]);
            return false;
        }
        if ((ins[i] & 0x3F000000u) == 0x1C000000u) {     // FP/SIMD literal (V=1)
            LOGE("hook abort: FP literal at slot %d (%08X)", i, ins[i]);
            return false;
        }
    }

    uintptr_t oldpc = (uintptr_t)target;
    // Proximity is only a nicety now (bug #6 fix below makes distance
    // irrelevant); mmap_near falls back to any free page if it finds none.
    void* page = mmap_near(oldpc, 4096);
    if (!page) { LOGE("mmap_near failed near %p", target); return false; }

    // Rewrite the 4 displaced instructions as POSITION-INDEPENDENT code.
    // Bug #6: re-encoding them relative to the trampoline's own PC needs an
    // ADRP immediate of +/-4 GB, and mmap landed the trampoline 4.8 GB from
    // libil2cpp - the immediate overflowed, x20 came back page-aligned
    // garbage, and the first instruction after the return died. Rather than
    // hunting for a page in range (there was none within 1.7 GB), each
    // PC-relative target is materialised as an absolute 64-bit literal
    // sitting right next to the instruction that needs it.
    uint32_t* tr = (uint32_t*)page;
    int oi = 0;
    for (int i = 0; i < 4; i++) {
        uint32_t x = ins[i];
        uintptr_t ipc = oldpc + (uintptr_t)i * 4;

        if ((x & 0x9F000000u) == 0x90000000u ||              // ADRP
            (x & 0x1F000000u) == 0x10000000u) {              // ADR
            uint64_t abs = 0;
            pcrel_target(x, ipc, &abs);
            tr[oi++] = 0x58000040u | (x & 0x1Fu);            // ldr xd, [pc, #8]
            tr[oi++] = 0x14000003u;                          // b over the literal
            *(uint64_t*)(tr + oi) = abs; oi += 2;
        } else if ((x & 0x3B000000u) == 0x18000000u) {       // integer LDR literal
            uint32_t opc = (x >> 30) & 3u;
            if (opc == 3u) continue;                         // PRFM: a hint, drop it
            uint64_t abs = 0;
            pcrel_target(x, ipc, &abs);
            tr[oi++] = 0x58000050u;                          // ldr x16, [pc, #8] = addr
            tr[oi++] = 0x14000003u;
            *(uint64_t*)(tr + oi) = abs; oi += 2;
            // literal opc -> unsigned-offset form: 00=W(size10,op01),
            // 01=X(size11,op01), 10=LDRSW(size10,op10)
            uint32_t sz  = (opc == 1u) ? 3u : 2u;
            uint32_t op  = (opc == 2u) ? 2u : 1u;
            tr[oi++] = (sz << 30) | 0x39000000u | (op << 22) |
                       (16u << 5) | (x & 0x1Fu);
        } else {
            tr[oi++] = x;                                    // stp / mov / alu
        }
    }
    tr[oi++] = 0x58000050u;   // ldr x16, [pc, #8]
    tr[oi++] = 0xD61F0200u;   // br  x16
    *(uint64_t*)(tr + oi) = (uint64_t)(oldpc + 16);

    uint32_t patch[4];
    patch[0] = 0x58000050u;                    // ldr x16, [pc, #8]
    patch[1] = 0xD61F0200u;                    // br  x16
    *(uint64_t*)(patch + 2) = (uint64_t)&hooked_update;

    // libil2cpp's text segment is mapped r-x. Writing the patch without
    // mprotect first is a guaranteed SIGSEGV. Make the target page(s) RW
    // (prefer RWX, fall back to RW), write, then restore r-x.
    uintptr_t tpage = (uintptr_t)target & ~(uintptr_t)0xFFF;
    uintptr_t toff  = (uintptr_t)target & 0xFFF;
    size_t tlen = 4096 + ((toff + sizeof(patch) > 4096) ? 4096 : 0);  // patch can straddle
    if (mprotect((void*)tpage, tlen, PROT_READ | PROT_WRITE | PROT_EXEC) != 0 &&
        mprotect((void*)tpage, tlen, PROT_READ | PROT_WRITE) != 0) {
        LOGE("mprotect RW failed: %s", strerror(errno));
        return false;
    }
    // Publish the trampoline and make it executable BEFORE the patch lands.
    // The instant those 16 bytes are overwritten, the next Update can jump
    // into hooked_update - and hooked_update reads the `api` table the
    // worker filled in above. Without a barrier here the compiler is free
    // to sink those stores past the patch write, so a first hooked frame
    // arriving microseconds later could call through a null pointer.
    // (bug #5 was an RW-only trampoline, this is the null-table cousin.)
    __sync_synchronize();
    if (mprotect(page, 4096, PROT_READ | PROT_EXEC) != 0) {
        LOGE("mprotect trampoline RX failed: %s", strerror(errno));
        munmap(page, 4096);
        return false;
    }
    g_tramp = (UpdateFn)page;
    g_hookTarget = target;

    memcpy(t, patch, sizeof(patch));

    // Trampoline can be ~44 bytes (an ADRP expands to 4 words) - flush a
    // generous window so no stale i-cache line survives.
    __builtin___clear_cache((char*)page, (char*)page + 256);
    __builtin___clear_cache((char*)target, (char*)target + 16);

    if (mprotect((void*)tpage, tlen, PROT_READ | PROT_EXEC) != 0) {
        LOGE("mprotect RX restore failed: %s", strerror(errno));
    }

    LOGI("hook installed: target=%p tramp=%p delta=%lldB",
         target, page, (long long)((uintptr_t)page - oldpc));
    return true;
}

// ---------------------------------------------------------------- resolve
static bool resolve_core() {
    static int64_t firstProbeAtMs = 0;
    if (!firstProbeAtMs) {
        struct timespec ts; clock_gettime(CLOCK_MONOTONIC, &ts);
        firstProbeAtMs = (int64_t)ts.tv_sec * 1000 + ts.tv_nsec / 1000000;
    }

    if (!g_domain) {
        // We are called from hooked_update - i.e. already inside managed
        // VRFishGame.Update - so il2cpp is up by construction. The short
        // warm-up below is only belt-and-braces for a hypothetical early
        // first frame; the real gate is the stable assembly count.
        struct timespec ts; clock_gettime(CLOCK_MONOTONIC, &ts);
        int64_t nowMs = (int64_t)ts.tv_sec * 1000 + ts.tv_nsec / 1000000;
        if (nowMs - firstProbeAtMs < 1000) return false;

        void* d = api.domain_get();
        if (!d) return false;

        // il2cpp_domain_get() can return a domain before il2cpp_init() has
        // finished. Attaching our thread at that point SIGSEGVs inside
        // libil2cpp (null deref ~0x135, backtrace worker -> libil2cpp) and
        // takes the whole game down. Assemblies only show up once the
        // metadata is in, so wait until the count is non-zero AND stable
        // across polls before we attach.
        size_t n = 0;
        const void** asms = api.domain_get_assemblies(d, &n);
        if (!asms || n == 0 || n > 4096) return false;

        static int lastCount = -1;
        static int stablePolls = 0;
        if ((int)n != lastCount) { lastCount = (int)n; stablePolls = 0; return false; }
        if (++stablePolls < 3) return false;   // ~600ms of unchanged count

        api.thread_attach(d);
        g_domain = d;
        LOGI("il2cpp domain ready: %zu assemblies", n);
    }

    kVRFishGame = find_class("VRFish.Game", "VRFishGame");
    if (!kVRFishGame) return false;

    mUpdate = find_method(kVRFishGame, "Update", 0);
    if (!mUpdate) { LOGE("VRFishGame.Update not found"); return false; }

    // Confirm the RVA we patched from is the method the runtime reports.
    if (g_hookTarget && *(void**)mUpdate) {
        void* mp = *(void**)mUpdate;
        if ((uintptr_t)mp == (uintptr_t)g_hookTarget)
            LOGI("RVA cross-check OK: hooked %p == runtime Update", mp);
        else
            LOGE("RVA cross-check MISMATCH: hooked %p, runtime says %p",
                 g_hookTarget, mp);
    }

    kGameProgression = find_class("VRFish.Gameplay", "GameProgression");
    kPlayerProfile   = find_class("VRFish.Gameplay", "PlayerProfile");
    kInput           = find_class("UnityEngine", "Input");
    kGameObject      = find_class("UnityEngine", "GameObject");
    kTransform       = find_class("UnityEngine", "Transform");
    kRectTransform   = find_class("UnityEngine", "RectTransform");
    kCamera          = find_class("UnityEngine", "Camera");
    kObjectClass     = find_class("UnityEngine", "Object");
    kCollider        = find_class("UnityEngine", "Collider");
    kRenderer        = find_class("UnityEngine", "Renderer");
    kMaterial        = find_class("UnityEngine", "Material");
    kCanvas          = find_class("UnityEngine", "Canvas");
    kTMPText         = find_class("TMPro", "TMP_Text");
    kTMPSettings     = find_class("TMPro", "TMP_Settings");
    kTypeClass       = find_class("System", "Type");

    if (!kGameProgression || !kPlayerProfile || !kInput || !kGameObject ||
        !kTransform || !kRectTransform || !kCamera || !kObjectClass ||
        !kCanvas || !kTMPText || !kTMPSettings || !kTypeClass) {
        LOGE("class resolve incomplete: prog=%p prof=%p input=%p go=%p tr=%p rect=%p cam=%p obj=%p canvas=%p tmp=%p settings=%p type=%p",
             kGameProgression, kPlayerProfile, kInput, kGameObject, kTransform, kRectTransform,
             kCamera, kObjectClass, kCanvas, kTMPText, kTMPSettings, kTypeClass);
        return false;
    }

    fInstance    = find_field(kVRFishGame, "<Instance>k__BackingField");
    fProgression = find_field(kVRFishGame, "<Progression>k__BackingField");
    fProfile     = find_field(kGameProgression, "<Profile>k__BackingField");
    fChanged     = find_field(kGameProgression, "Changed");
    fCoins       = find_field(kPlayerProfile, "coins");
    fXp          = find_field(kPlayerProfile, "xp");
    fCrowns      = find_field(kPlayerProfile, "crowns");
    fCandy       = find_field(kPlayerProfile, "candy");
    fMedals      = find_field(kPlayerProfile, "medals");
    if (!fInstance || !fProgression || !fProfile || !fCoins || !fXp || !fCrowns || !fCandy || !fMedals) {
        LOGE("field resolve incomplete: inst=%p prog=%p prof=%p changed=%p coins=%p xp=%p crowns=%p candy=%p medals=%p",
             fInstance, fProgression, fProfile, fChanged, fCoins, fXp, fCrowns, fCandy, fMedals);
        return false;
    }

    mGetKeyDown      = find_method(kInput, "GetKeyDown", 1);
    mCamMain         = find_method(kCamera, "get_main", 0);
    mGetTransform    = find_method(kGameObject, "get_transform", 0);
    mCreatePrimitive = find_method(kGameObject, "CreatePrimitive", 1);
    mGoCtorTypes     = find_method(kGameObject, ".ctor", 2);
    mAddComponent    = find_method(kGameObject, "AddComponent", 1);
    mGetComponent    = find_method(kGameObject, "GetComponent", 1);
    mSetActive       = find_method(kGameObject, "SetActive", 1);
    // set_parent has only the 1-arg overload in this game
    // (dump: `public void set_parent(Transform value)`) - the 2-arg
    // worldPositionStays form doesn't exist, and asking for it left this
    // resolve permanently incomplete, so the hook/menu never installed.
    mSetParent       = find_method(kTransform, "set_parent", 1);
    if (!mSetParent) mSetParent = find_method(kTransform, "set_parent", 2);
    mSetLocalPos     = find_method(kTransform, "set_localPosition", 1);
    mSetLocalRot     = find_method(kTransform, "set_localRotation", 1);
    mSetLocalScale   = find_method(kTransform, "set_localScale", 1);
    mSetRenderMode   = find_method(kCanvas, "set_renderMode", 1);
    mSetText         = find_method(kTMPText, "set_text", 1);
    mSetFontSize     = find_method(kTMPText, "set_fontSize", 1);
    mSetColor        = find_method(kTMPText, "set_color", 1);
    mSetAlignment    = find_method(kTMPText, "set_alignment", 1);
    mSetFont         = find_method(kTMPText, "set_font", 1);
    mGetDefaultFont  = find_method(kTMPSettings, "get_defaultFontAsset", 0);
    mDestroy         = find_method(kObjectClass, "Destroy", 1);
    mGetMaterial     = find_method(kRenderer, "get_material", 0);
    mSetMatColor     = find_method(kMaterial, "set_color", 1);
    mSetAnchorMin    = find_method(kRectTransform, "set_anchorMin", 1);
    mSetAnchorMax    = find_method(kRectTransform, "set_anchorMax", 1);
    mSetSizeDelta    = find_method(kRectTransform, "set_sizeDelta", 1);

    if (!mGetKeyDown || !mCamMain || !mGetTransform || !mCreatePrimitive || !mGoCtorTypes ||
        !mAddComponent || !mGetComponent || !mSetActive || !mSetParent || !mSetLocalPos ||
        !mSetLocalRot || !mSetLocalScale || !mSetRenderMode || !mSetText || !mSetFontSize ||
        !mSetColor || !mSetAlignment || !mSetFont || !mGetDefaultFont || !mDestroy ||
        !mSetAnchorMin || !mSetAnchorMax || !mSetSizeDelta) {
        // throttled: resolve retries every 200ms, don't flood logcat
        static int failCount = 0;
        if (failCount++ % 50 == 0) {
            LOGE("method resolve incomplete (try %d): keydown=%p camp=%p gettr=%p prim=%p ctor2=%p addc=%p getc=%p setact=%p setpar=%p setpos=%p setrot=%p setscl=%p setrm=%p settext=%p setfs=%p setcol=%p setal=%p setfont=%p getfont=%p destroy=%p anchmin=%p anchmax=%p sizedelta=%p",
                 failCount, mGetKeyDown, mCamMain, mGetTransform, mCreatePrimitive, mGoCtorTypes, mAddComponent,
                 mGetComponent, mSetActive, mSetParent, mSetLocalPos, mSetLocalRot, mSetLocalScale,
                 mSetRenderMode, mSetText, mSetFontSize, mSetColor, mSetAlignment, mSetFont,
                 mGetDefaultFont, mDestroy, mSetAnchorMin, mSetAnchorMax, mSetSizeDelta);
        }
        return false;
    }

    LOGI("core resolved OK");
    return true;
}

// First (lowest) mapping of `lib` in this process - fallback when dladdr
// cannot tell us where libil2cpp landed.
static uintptr_t map_base_of(const char* lib) {
    FILE* f = fopen("/proc/self/maps", "r");
    if (!f) return 0;
    char line[512];
    uintptr_t base = 0;
    while (fgets(line, sizeof(line), f)) {
        if (!strstr(line, lib)) continue;
        uintptr_t a = (uintptr_t)strtoul(line, nullptr, 16);
        if (!base || a < base) base = a;
    }
    fclose(f);
    return base;
}

// ---------------------------------------------------------------- worker
static void* worker(void*) {
    pthread_setname_np(pthread_self(), "reels-worker");

    void* h = nullptr;
    for (int i = 0; i < 600 && !h; i++) {
        h = dlopen("libil2cpp.so", RTLD_NOW | RTLD_NOLOAD);
        if (!h) usleep(100000);
    }
    if (!h) { LOGE("libil2cpp.so never loaded"); return nullptr; }
    LOGI("libil2cpp handle %p", h);

#define RESOLVE(field, name) \
    api.field = (decltype(api.field))dlsym(h, name); \
    if (!api.field) { LOGE("missing export %s", name); return nullptr; }

    RESOLVE(domain_get, "il2cpp_domain_get");
    RESOLVE(domain_get_assemblies, "il2cpp_domain_get_assemblies");
    RESOLVE(assembly_get_image, "il2cpp_assembly_get_image");
    RESOLVE(class_from_name, "il2cpp_class_from_name");
    RESOLVE(class_get_method_from_name, "il2cpp_class_get_method_from_name");
    RESOLVE(class_get_field_from_name, "il2cpp_class_get_field_from_name");
    RESOLVE(class_get_type, "il2cpp_class_get_type");
    RESOLVE(type_get_object, "il2cpp_type_get_object");
    RESOLVE(object_new, "il2cpp_object_new");
    RESOLVE(runtime_invoke, "il2cpp_runtime_invoke");
    RESOLVE(field_set_value, "il2cpp_field_set_value");
    RESOLVE(field_get_value, "il2cpp_field_get_value");
    RESOLVE(field_static_get_value, "il2cpp_field_static_get_value");
    RESOLVE(string_new, "il2cpp_string_new");
    RESOLVE(thread_attach, "il2cpp_thread_attach");
    RESOLVE(object_get_class, "il2cpp_object_get_class");
    RESOLVE(object_unbox, "il2cpp_object_unbox");
    RESOLVE(array_new, "il2cpp_array_new");
    RESOLVE(method_get_name, "il2cpp_method_get_name");
#undef RESOLVE

    // ---- install the hook with NO il2cpp calls (bug #7) ------------------
    // Everything il2cpp-related now happens inside hooked_update, which only
    // runs from managed VRFishGame.Update on the attached UnityMain thread.
    // Here we just need the address: load base + the verified RVA of
    // VRFishGame.Update (tools/game/libil2cpp.so @ 0x6F7BCD4 =
    // A9BE57FE A9014FF4 90009DB4 AA0003F3). install_hook re-checks those
    // prologue bytes before patching, so a wrong RVA aborts instead of
    // corrupting the game.
    void* base = nullptr;
    Dl_info info;
    if (dladdr((void*)api.domain_get, &info) && info.dli_fbase) base = info.dli_fbase;
    if (!base) base = (void*)map_base_of("/libil2cpp.so");
    if (!base) { LOGE("cannot determine libil2cpp load base"); return nullptr; }

    void* target = (void*)((uintptr_t)base + 0x6F7BCD4);
    LOGI("libil2cpp base %p -> VRFishGame.Update @ %p (RVA 0x6F7BCD4)", base, target);

    if (!install_hook(target)) return nullptr;

    LOGI("REELS MODS ready - press X in game");
    return nullptr;
}

// ---------------------------------------------------------------- load
static volatile int g_started = 0;

static void start_once() {
    if (__sync_lock_test_and_set(&g_started, 1) == 0) {
        pthread_t th;
        pthread_create(&th, nullptr, worker, nullptr);
        pthread_detach(th);
    }
}

__attribute__((constructor)) static void on_load() {
    LOGI("REELS MODS libmain wrapper loaded");
    start_once();
}

extern "C" JNIEXPORT jint JNI_OnLoad(JavaVM* vm, void* reserved) {
    start_once();

    void* real = dlopen("libmain_real.so", RTLD_NOW);
    if (!real) {
        LOGE("dlopen libmain_real.so failed: %s", dlerror());
        return JNI_VERSION_1_6;
    }
    typedef jint (*orig_fn)(JavaVM*, void*);
    orig_fn orig = (orig_fn)dlsym(real, "JNI_OnLoad");
    if (!orig) {
        LOGE("JNI_OnLoad not found in libmain_real.so");
        return JNI_VERSION_1_6;
    }
    jint r = orig(vm, reserved);
    LOGI("original JNI_OnLoad returned %d", (int)r);
    return r;
}
