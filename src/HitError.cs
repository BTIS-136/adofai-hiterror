using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HitError
{
    public static class Startup
    {
        private static GameObject host;
        private static Harmony harmony;

        public static void Load()
        {
            if (host != null) return;

            try
            {
                MethodInfo addHit = HitPatch.FindAddHit();
                if (addHit == null)
                {
                    Debug.LogError("[HitError] AddHit not found");
                    return;
                }

                harmony = new Harmony("HitError.MillisecondDisplay");
                MethodInfo prefix = typeof(HitPatch).GetMethod("Prefix",
                    BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo postfix = typeof(HitPatch).GetMethod("Postfix",
                    BindingFlags.Static | BindingFlags.NonPublic);

                harmony.Patch(addHit, new HarmonyMethod(prefix), new HarmonyMethod(postfix));
                Debug.Log("[HitError] patched AddHit");

                host = new GameObject("HitError_Display");
                UnityEngine.Object.DontDestroyOnLoad(host);
                host.AddComponent<HitErrorDisplay>();
                host.AddComponent<KeyShakeDriver>();
            }
            catch (Exception e)
            {
                Debug.LogError("[HitError] Load failed: " + e);
            }
        }
    }

    internal static class HitPatch
    {
        internal static readonly BindingFlags F =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;

        private static MethodInfo calculateColor;
        private static MethodInfo timeToAngle;
        private static MethodInfo boundary;
        private static bool loggedError;
        internal static int logCount;

        internal enum HitKind { Red, Orange, Green, Unknown }

        internal struct Sample
        {
            internal double Ms;
            internal Color Color;
            internal bool Valid;
        }

        internal static MethodInfo FindAddHit()
        {
            calculateColor = typeof(scrHitErrorMeter).GetMethod(
                "CalculateTickColor", F, null,
                new Type[] { typeof(float), typeof(float), typeof(scrFloor) }, null);

            timeToAngle = typeof(scrMisc).GetMethod(
                "TimeToAngleInRad", F, null,
                new Type[] { typeof(double), typeof(double), typeof(double), typeof(bool) }, null);

            boundary = typeof(scrMisc).GetMethod(
                "GetAdjustedAngleBoundaryInDeg", F, null,
                new Type[] { typeof(HitMarginGeneral), typeof(double), typeof(double), typeof(double) }, null);

            if (calculateColor == null) Debug.LogError("[HitError] CalculateTickColor missing");
            if (timeToAngle == null) Debug.LogError("[HitError] TimeToAngleInRad missing");
            if (boundary == null) Debug.LogError("[HitError] GetAdjustedAngleBoundaryInDeg missing");

            return typeof(scrHitErrorMeter).GetMethod(
                "AddHit", F, null,
                new Type[] { typeof(float), typeof(float), typeof(scrPlanet), typeof(scrFloor) }, null);
        }

        private static void Prefix(scrHitErrorMeter __instance, float __0, float __1,
            scrPlanet __2, scrFloor __3, out Sample __state)
        {
            __state = new Sample();

            try
            {
                scrConductor conductor = scrConductor.instance;
                if (conductor == null || conductor.song == null) return;

                scrFloor floor = __3;
                if (floor == null && __2 != null && __2.player != null &&
                    __2.player.currFloor != null)
                {
                    floor = __2.player.currFloor.prevfloor;
                }

                float speed = floor == null ? 1f : floor.speed;
                double bpm = (double)(conductor.bpm * speed);
                double pitch = (double)conductor.song.pitch;

                if (!Finite(bpm) || !Finite(pitch) || bpm <= 0.0 || pitch <= 0.0) return;

                float degrees = __0 * -57.29578f;
                if (!Finite(degrees)) return;

                // 涓€鎷嶆椂闀?= 60 / (bpm * speed * pitch)锛屼竴鎷嶈浆 180 搴︺€?                // 瑙掗€熷害(搴?绉? = 180 / (60/(bpm*pitch)) = 3*bpm*pitch
                double bpmEff = bpm * pitch;
                double ms = (double)degrees * 1000.0 / (3.0 * bpmEff);

                if (logCount < 8)
                {
                    logCount++;
                    double radPerSec = 0.0;
                    if (timeToAngle != null)
                    {
                        try
                        {
                            radPerSec = (double)timeToAngle.Invoke(null,
                                new object[] { 1.0, bpm, pitch, false });
                        }
                        catch { }
                    }
                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "[HitError] raw={0:0.######} deg={1:0.######} bpm={2:0.###} speed={3:0.###} pitch={4:0.###} radPerSec={5:0.######} ms={6:0.###}",
                        __0, degrees, conductor.bpm, speed, pitch, radPerSec, ms));
                }

                if (!Finite(ms)) return;

                Color color = Color.white;
                if (calculateColor != null)
                {
                    double counted = 0.0;
                    if (boundary != null)
                    {
                        counted = (double)boundary.Invoke(null,
                            new object[] { (HitMarginGeneral)0, bpm, pitch, (double)__1 });
                    }

                    float normalized = degrees;
                    if (Finite(counted) && counted > 0.0)
                        normalized = degrees * (float)(60.0 / counted);

                    try
                    {
                        color = (Color)calculateColor.Invoke(__instance,
                            new object[] { normalized, __1, __3 });
                    }
                    catch { color = Color.white; }
                }

                __state.Ms = ms;
                __state.Color = color;
                __state.Valid = true;
            }
            catch (Exception e)
            {
                if (!loggedError)
                {
                    loggedError = true;
                    Debug.LogError("[HitError] Capture failed: " + e);
                }
            }
        }

        private static void Postfix(Sample __state)
        {
            if (!__state.Valid) return;
            HitErrorDisplay.Publish(__state.Ms, __state.Color);
        }

        internal static bool Finite(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }

        internal static HitKind Classify(Color value)
        {
            // Match the actual UI palette, rather than hard-coding RGB values.  The
            // seven entries are TooEarly, VeryEarly, LittleEarly, Perfect,
            // LittleLate, VeryLate and TooLate respectively.
            try
            {
                Type type = typeof(scrHitErrorMeter).Assembly.GetType("RDConstants");
                object data = Read(type, null, "data");
                object scheme = Read(data == null ? null : data.GetType(), data, "hitMarginColoursUI");
                if (scheme != null)
                {
                    string[] names = { "colourTooEarly", "colourVeryEarly", "colourLittleEarly",
                        "colourPerfect", "colourLittleLate", "colourVeryLate", "colourTooLate" };
                    int best = -1; float distance = float.PositiveInfinity;
                    for (int i = 0; i < names.Length; i++)
                    {
                        object raw = Read(scheme.GetType(), scheme, names[i]);
                        if (!(raw is Color)) continue;
                        Color c = (Color)raw;
                        float d = (value.r-c.r)*(value.r-c.r) + (value.g-c.g)*(value.g-c.g) + (value.b-c.b)*(value.b-c.b);
                        if (d < distance) { distance = d; best = i; }
                    }
                    if (best == 3) return HitKind.Green;
                    if (best == 0 || best == 6) return HitKind.Red;
                    if (best >= 1 && best <= 5) return HitKind.Orange;
                }
            }
            catch { }
            return HitKind.Unknown;
        }

        private static object Read(Type type, object instance, string name)
        {
            if (type == null) return null;
            PropertyInfo p = type.GetProperty(name, F);
            if (p != null) return p.GetValue(instance, null);
            FieldInfo f = type.GetField(name, F);
            return f == null ? null : f.GetValue(instance);
        }
    }

    public sealed class HitErrorDisplay : MonoBehaviour
    {
        private const BindingFlags F =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;

        private static string label = "";
        private static string comboLabel = "";
        private static Color labelColor = Color.white;
        private static Color comboColor = Color.gray;
        private static float shownAt = -100f;
        private static bool hasSample;
        private static int combo;
        private static int greenStreak;
        private static int nonRedStreak;
        private static float vibStart = -100f;
        internal static float lastHitAt = -100f;
        private static float vibAmp;
        private static float vibFreq;
        private const float VibDuration = 0.30f;
        private static bool comboGold;
        private static bool comboOrange;
        private static bool wasPlaying;
        private static object lastPlayer;
        private static States lastState = States.None;

        private static readonly FieldInfo WrapperField =
            typeof(scrHitErrorMeter).GetField("wrapperRectTransform", F);

        private readonly Vector3[] corners = new Vector3[4];
        private const float MeterCenterBottomOffset = 86f; private const float MeterHeight = 40f; private const float LabelGap = 22f; private const float LabelBottomOffset = MeterCenterBottomOffset + MeterHeight * 0.5f + LabelGap; // Label bottom offset = 128px.
        private readonly GUIContent content = new GUIContent();
        private Font ownedFont;
        private GUIStyle style;
        private GUIStyle comboStyle;
        private Font ownedComboFont;
        private int tick;
        private bool sizeForced;
        private bool loggedColors;
        private bool loggedGui;

        internal static void Publish(double ms, Color color)
        {
            double v = Math.Round(ms, 2, MidpointRounding.AwayFromZero);
            if (v == 0.0) v = 0.0;

            string sign = v >= 0.0 ? "+" : "-";
            double abs = Math.Abs(v);

            label = sign + abs.ToString("0.00", CultureInfo.InvariantCulture) + " ms";
            labelColor = color;
            shownAt = Time.unscaledTime;
            hasSample = true;

            HitPatch.HitKind kind = HitPatch.Classify(color);
            combo++;
            if (kind == HitPatch.HitKind.Red)
            {
                // Red resets everything: the chain restarts as if the level began.
                greenStreak = 0;
                nonRedStreak = 0;
            }
            else
            {
                nonRedStreak++;
                if (kind == HitPatch.HitKind.Green) greenStreak++;
                else greenStreak = 0;
            }

            // Stage 2 colour rules:
            //  * first 20 hits after start/restart are always black.
            //  * 20 consecutive non-red hits -> orange.
            //  * 50 consecutive green hits -> gold (any state).
            //  * yellow only resets the green streak (it is still non-red),
            //    so gold + yellow drops back to orange until 50 greens again.
            //  * red resets both counters and the chain restarts like a new level.
            bool gold = greenStreak >= 50;
            bool orange = !gold && nonRedStreak >= 20;

            lastHitAt = Time.unscaledTime;
            comboGold = combo > 20 && gold;
            comboOrange = combo > 20 && orange;
            if (combo <= 20) comboColor = Color.gray;
            else if (gold) comboColor = new Color(1f, 0.72f, 0f, 1f);
            else if (orange) comboColor = new Color(1f, 0.35f, 0f, 1f);
            else comboColor = Color.gray;

            // Stage 4: small per-hit shake for orange/gold only.
            // Amplitude and frequency grow with the combo, but slower than the
            // font-size milestones; the settle time is constant (VibDuration).
            if (comboGold || comboOrange)
            {
                vibAmp = Mathf.Min(2.5f + combo * 0.015f, 10f);
                vibFreq = Mathf.Min(14f + combo * 0.020f, 26f);
                vibStart = Time.unscaledTime;
            }
            else
            {
                vibStart = -100f;
            }

            comboLabel = "Combo " + combo.ToString(CultureInfo.InvariantCulture);
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[HitError] hit={0} rgb=({1:0.###},{2:0.###},{3:0.###}) combo={4} green={5} nonRed={6}",
                kind, color.r, color.g, color.b, combo, greenStreak, nonRedStreak));
        }

        private void Update()
        {
            tick++;
            if (tick % 20 != 0) return;

            scrController ctrl = scrController.instance;
            if (ctrl == null || !ctrl.gameworld)
            {
                if (wasPlaying) Debug.Log("[HitError] combo reset: gameworld stopped");
                ResetCombo(); wasPlaying = false; hasSample = false; sizeForced = false; return;
            }
            object player = ctrl.playerOne;
            if (!wasPlaying || (player != null && !object.ReferenceEquals(player, lastPlayer)) || ctrl.currentState != lastState)
            {
                if (wasPlaying) Debug.Log("[HitError] combo reset: state/player changed to " + ctrl.currentState);
                ResetCombo(); wasPlaying = true; lastPlayer = player; lastState = ctrl.currentState;
            }
            if (ctrl.currentState == States.Fail || ctrl.currentState == States.Fail2 || ctrl.currentState == States.Won)
                ResetCombo();

            if (!sizeForced)
            {
                sizeForced = true;
                ForceSize();
            }

            scrHitErrorMeter meter = ctrl.errorMeter;
            if (meter == null) return;

            if (!meter.gameObject.activeSelf) meter.gameObject.SetActive(true);

            Transform parent = meter.transform.parent;
            if (parent != null && !parent.gameObject.activeSelf)
                parent.gameObject.SetActive(true);

            if (!loggedColors)
            {
                loggedColors = true;
                TryLogColors();
            }
        }

        private void ForceSize()
        {
            try
            {
                PropertyInfo p = typeof(Persistence).GetProperty("hitErrorMeterSize", F);
                if (p == null || !p.CanWrite) return;

                object cur = p.GetValue(null, null);
                string name = cur == null ? "" : cur.ToString();
                if (name == "Off" || name == "0")
                {
                    object normal = Enum.Parse(p.PropertyType, "Normal");
                    p.SetValue(null, normal, null);
                    Debug.Log("[HitError] Forced hitErrorMeterSize=Normal");
                }
            }
            catch (Exception e)
            {
                Debug.Log("[HitError] ForceSize failed: " + e.Message);
            }
        }

        private void OnGUI()
        {
            if (!hasSample) return;
            if (Event.current.type != EventType.Repaint) return;

            float age = Time.unscaledTime - shownAt;
            if (age > 8f) return;

            scrController ctrl = scrController.instance;
            if (ctrl == null || !ctrl.gameworld) { hasSample = false; sizeForced = false; return; }
            if (ctrl.errorMeter == null) return;
            if (!ctrl.errorMeter.gameObject.activeInHierarchy) return;

            float alpha = 1f;
            if (age > 6f) alpha = Mathf.Clamp01(1f - (age - 6f) / 2f);
            if (alpha <= 0.01f) return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.label);
                style.alignment = TextAnchor.MiddleCenter;
                style.fontStyle = FontStyle.Bold;
                style.richText = false;
                style.wordWrap = false;
                Font font = null;
                string[] candidates = new string[] { "Segoe UI", "Arial" };
                for (int i = 0; i < candidates.Length && font == null; i++)
                {
                    try { font = Font.CreateDynamicFontFromOSFont(candidates[i], 28); } catch { }
                }
                ownedFont = font;
                if (font != null) style.font = font;
            }

            if (comboStyle == null)
            {
                comboStyle = new GUIStyle(GUI.skin.label);
                comboStyle.alignment = TextAnchor.MiddleCenter;
                comboStyle.fontStyle = FontStyle.Bold;
                comboStyle.richText = false;
                comboStyle.wordWrap = false;
                Font comboFont = null;
                string[] comboCandidates = new string[] { "Bahnschrift", "Segoe UI Black", "Arial Black", "Impact" };
                for (int i = 0; i < comboCandidates.Length && comboFont == null; i++)
                {
                    try { comboFont = Font.CreateDynamicFontFromOSFont(comboCandidates[i], 28); } catch { }
                }
                ownedComboFont = comboFont;
                if (comboFont != null) comboStyle.font = comboFont;
            }

            int baseFontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.height / 68f), 13, 24);
            style.fontSize = baseFontSize;

            content.text = label;
            Vector2 size = style.CalcSize(content);
            float w = Mathf.Max(200f, size.x + 30f);
            float h = Mathf.Max(32f, size.y + 8f);

            Rect rect = Locate(ctrl.errorMeter, w, h);
            Rect comboRect = new Rect(rect.x, Mathf.Max(0f, rect.y - h - 4f), rect.width, h);

            if (!loggedGui)
            {
                loggedGui = true;
                Debug.Log("[HitError] gui label=" + label + " screen=" + Screen.width + "x" + Screen.height
                    + " rect=" + rect.x.ToString("0") + "," + rect.y.ToString("0")
                    + "," + rect.width.ToString("0") + "," + rect.height.ToString("0"));
            }

            Matrix4x4 oldMatrix = GUI.matrix;
            Color oldC = GUI.color;
            Color oldContent = GUI.contentColor;

            try
            {
                GUI.matrix = Matrix4x4.identity;
                GUI.color = Color.white;
                GUI.contentColor = Color.white;

                Color shadow = new Color(0f, 0f, 0f, 0.85f * alpha);
                style.normal.textColor = shadow;
                GUI.Label(new Rect(rect.x + 1.5f, rect.y + 1.5f, rect.width, rect.height), content, style);

                Color main = labelColor;
                main.a = alpha;
                style.normal.textColor = main;
                GUI.Label(rect, content, style);

                if (combo > 0)
                {
                    GUIContent comboContent = new GUIContent(comboLabel);
                    // Milestone scaling: 50, 100, 200, 500, 1000.
                    // Grey keeps the smallest size; orange and gold grow from
                    // their own base sizes, gold more noticeably, capped at 2x.
                    int milestone = combo >= 1000 ? 5
                                  : combo >= 500 ? 4
                                  : combo >= 200 ? 3
                                  : combo >= 100 ? 2
                                  : combo >= 50 ? 1 : 0;
                    float comboScale = comboGold
                        ? Mathf.Min(1.4f + 0.12f * milestone, 2f)
                        : comboOrange
                            ? Mathf.Min(1.15f + 0.08f * milestone, 2f)
                            : 1f;
                    comboStyle.fontSize = Mathf.RoundToInt(baseFontSize * comboScale);
                    float comboH = Mathf.Max(h, h * comboScale);
                    // Damped sine shake, constant settle time. A new hit always
                    // restarts from zero, so it snaps home if the last shake had
                    // not finished yet.
                    float shakeY = 0f;
                    float shakeX = 0f;
                    float vibT = Time.unscaledTime - vibStart;
                    if (vibT >= 0f && vibT < VibDuration)
                    {
                        float decay = 1f - vibT / VibDuration;
                        decay *= decay;
                        float phase = 2f * Mathf.PI * vibFreq * vibT;
                        shakeY = vibAmp * Mathf.Sin(phase) * decay;
                        shakeX = vibAmp * 0.35f * Mathf.Sin(phase * 0.5f + Mathf.PI * 0.5f) * decay;
                    }
                    Rect cr = new Rect(comboRect.x + shakeX, Mathf.Max(0f, rect.y - comboH - 4f + shakeY), comboRect.width, comboH);
                    Color comboMain = comboColor; comboMain.a = alpha;
                    comboStyle.normal.textColor = new Color(0f, 0f, 0f, 0.85f * alpha);
                    GUI.Label(new Rect(cr.x + 1.5f, cr.y + 1.5f, cr.width, cr.height), comboContent, comboStyle);
                    comboStyle.normal.textColor = comboMain;
                    GUI.Label(cr, comboContent, comboStyle);
                    comboStyle.fontSize = baseFontSize;
                }
            }
            finally
            {
                GUI.matrix = oldMatrix;
                GUI.color = oldC;
                GUI.contentColor = oldContent;
            }
        }

        private void OnDestroy()
        {
            hasSample = false;
            if (ownedFont != null) UnityEngine.Object.Destroy(ownedFont);
            if (ownedComboFont != null && ownedComboFont != ownedFont) UnityEngine.Object.Destroy(ownedComboFont);
        }

        private static void ResetCombo()
        {
            combo = 0;
            greenStreak = 0;
            nonRedStreak = 0;
            comboGold = false;
            comboOrange = false;
            vibStart = -100f;
            comboLabel = "";
            comboColor = Color.gray;
            lastPlayer = null;
            lastState = States.None;
        }

        private Rect Locate(scrHitErrorMeter meter, float width, float height)
        {
            // Label top = meter top - 22px gap - label height.
            return new Rect(
                (Screen.width - width) * 0.5f,
                Mathf.Clamp(Screen.height - LabelBottomOffset - height, 0f, Mathf.Max(0f, Screen.height - height)),
                width, height);
        }

        private Rect LocateByMeter(scrHitErrorMeter meter, float width, float height)
        {
            Rect fallback = new Rect(
                (Screen.width - width) * 0.5f,
                Screen.height - 112f - height,
                width, height);

            try
            {
                RectTransform rt = null;
                if (WrapperField != null) rt = WrapperField.GetValue(meter) as RectTransform;
                if (rt == null) rt = meter.GetComponent<RectTransform>();
                if (rt == null) return fallback;

                Canvas canvas = rt.GetComponentInParent<Canvas>();
                if (canvas != null) canvas = canvas.rootCanvas;

                Camera cam = null;
                if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    cam = canvas.worldCamera;
                    if (cam == null) cam = Camera.main;
                    if (cam == null) return fallback;
                }

                rt.GetWorldCorners(corners);

                float minX = float.PositiveInfinity;
                float maxX = float.NegativeInfinity;
                float topY = float.NegativeInfinity;

                for (int i = 0; i < corners.Length; i++)
                {
                    Vector2 p = RectTransformUtility.WorldToScreenPoint(cam, corners[i]);
                    if (!HitPatch.Finite(p.x) || !HitPatch.Finite(p.y)) return fallback;
                    minX = Mathf.Min(minX, p.x);
                    maxX = Mathf.Max(maxX, p.x);
                    topY = Mathf.Max(topY, p.y);
                }

                float x = (minX + maxX - width) * 0.5f;
                float y = Screen.height - topY - height + 2f;

                x = Mathf.Clamp(x, 0f, Mathf.Max(0f, Screen.width - width));
                y = Mathf.Clamp(y, 0f, Mathf.Max(0f, Screen.height - height));

                return new Rect(x, y, width, height);
            }
            catch
            {
                return fallback;
            }
        }

        private void TryLogColors()
        {
            try
            {
                Type type = typeof(scrHitErrorMeter).Assembly.GetType("RDConstants");
                if (type == null) return;

                object data = Read(type, null, "data");
                if (data == null) return;

                object colors = Read(data.GetType(), data, "hitMarginColoursUI");
                if (colors == null) return;

                string[] names = { "colourTooEarly", "colourVeryEarly", "colourLittleEarly",
                    "colourPerfect", "colourLittleLate", "colourVeryLate", "colourTooLate" };

                for (int i = 0; i < names.Length; i++)
                {
                    object value = Read(colors.GetType(), colors, names[i]);
                    if (!(value is Color)) continue;
                    Color c = (Color)value;
                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "[HitError] {0}: ({1:0.###}, {2:0.###}, {3:0.###}, {4:0.###})",
                        names[i], c.r, c.g, c.b, c.a));
                }
            }
            catch { }
        }

        internal static bool TryGetShake(out float ox, out float oy)
        {
            ox = 0f; oy = 0f;
            if (!hasSample) return false;
            float t = Time.unscaledTime - vibStart;
            if (t < 0f || t >= VibDuration) return false;
            float decay = 1f - t / VibDuration;
            decay *= decay;
            float phase = 2f * Mathf.PI * vibFreq * t;
            ox = vibAmp * 0.35f * Mathf.Sin(phase * 0.5f + Mathf.PI * 0.5f) * decay;
            oy = vibAmp * Mathf.Sin(phase) * decay;
            return true;
        }

        private static object Read(Type type, object instance, string name)
        {
            PropertyInfo p = type.GetProperty(name, F);
            if (p != null) return p.GetValue(instance, null);
            FieldInfo f = type.GetField(name, F);
            if (f != null) return f.GetValue(instance);
            return null;
        }
    }

    /// <summary>
    /// Drives JipperKeyViewer's key blocks (not the rain) with the same small
    /// shake as the combo label. The rain layer is left untouched.
    /// </summary>
    public sealed class KeyShakeDriver : MonoBehaviour
    {
        private static readonly BindingFlags F =
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;

        private static Type keyViewerType;
        private FieldInfo keysField;
        private FieldInfo shapeField;
        private FieldInfo outlineField;
        private FieldInfo slotField;
        private FieldInfo pressedField;
        private static FieldInfo rectsField;
        private FieldInfo outRectsField;
        private static object shapeLayer;
        private static object outlineLayer;
        private static object[] extraLayers;
        private Array keys;
        private int[] slotOf;
        private bool[] lastPressed;
        private static float[] pressAt;
        private Array shapeRects;
        private Array outRects;
        private Rect[] baseRects;
        private Rect[] outBaseRects;
        private float nextRefresh;
        private bool discovered;
        private static string lastDiag;
        private bool loggedActive;
        private int pressLogCount;
        private static bool patched;
        private static int prefixCount;
        private static int vertexCount;

        private const float KeyVibDuration = 2.0f;
        private const float KeyVibAmp = 40f;
        private const float KeyVibFreq = 4f;

        private void LateUpdate()
        {
            float now = Time.unscaledTime;
            if ((!discovered || keys == null) && now >= nextRefresh)
            {
                nextRefresh = now + 2f;
                Discover();
            }
            if (keys == null || pressedField == null) return;
            EnsureLayers();

            // Detect key-down edges per key; each key shakes only its own block.
            for (int i = 0; i < keys.Length; i++)
            {
                if (keys.GetValue(i) == null) continue;
                bool pressed = (bool)pressedField.GetValue(keys.GetValue(i));
                if (pressed && !lastPressed[i])
                {
                    int slot = slotOf[i];
                    if (slot >= 0 && pressAt != null && slot < pressAt.Length) pressAt[slot] = now;
                    if (pressLogCount < 8)
                    {
                        pressLogCount++;
                        Debug.Log("[HitError] keyshake press key=" + i + " slot=" + slot + " rects=" + (shapeRects == null ? -1 : shapeRects.Length));
                    }
                }
                lastPressed[i] = pressed;
            }

            if (!patched) PatchDraw();

            bool any = false;
            if (pressAt != null)
            {
                for (int s = 0; s < pressAt.Length; s++)
                {
                    float t = now - pressAt[s];
                    if (t >= 0f && t < KeyVibDuration) { any = true; break; }
                }
            }
            if (any && patched)
            {
                Dirty(shapeLayer);
                Dirty(outlineLayer);
                if (extraLayers != null)
                {
                    for (int i = 0; i < extraLayers.Length; i++) Dirty(extraLayers[i]);
                }
                if (!loggedActive)
                {
                    loggedActive = true;
                    Diag("per-key shake active slots=" + (pressAt == null ? 0 : pressAt.Length));
                }
            }
        }

        private static void PatchDraw()
        {
            try
            {
                if (keyViewerType == null) return;
                Type layerType = keyViewerType.Assembly.GetType("JipperKeyViewer.KeyViewer.Rendering.KeyShapeLayer");
                if (layerType == null) { Diag("layer type not found"); return; }
                MethodInfo m = layerType.GetMethod("OnPopulateMesh", F, null,
                    new Type[] { typeof(UnityEngine.UI.VertexHelper) }, null);
                if (m == null) { Diag("OnPopulateMesh not found"); return; }
                Harmony h = new Harmony("HitError.KeyShake.Layer");
                MethodInfo pre = typeof(KeyShakeDriver).GetMethod("LayerPrefix", BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo post = typeof(KeyShakeDriver).GetMethod("LayerPostfixVertices", BindingFlags.Static | BindingFlags.NonPublic);
                h.Patch(m, new HarmonyMethod(pre), new HarmonyMethod(post));
                patched = true;
                Diag("draw patched on " + layerType.FullName);
            }
            catch (Exception e) { Diag("patch error " + e.Message); }
        }

        private static bool LayerPrefix(object __instance, out object __state)
        {
            __state = null;
            try
            {
                if (__instance == null || rectsField == null || pressAt == null) return true;
                Rect[] rects = rectsField.GetValue(__instance) as Rect[];
                if (rects == null) return true;
                Rect[] snapshot = new Rect[rects.Length];
                Array.Copy(rects, snapshot, rects.Length);
                __state = snapshot;
                // Do not touch rects here: the layer reads its layout data during
                // OnPopulateMesh, so moving rects would double-shift the vertices.
                return true;
            }
            catch { return true; }
        }

        private static void LayerPostfix(object __instance, object __state)
        {
            try
            {
                // The layer may rebuild its rects internally during OnPopulateMesh.
                // Apply the shake to the finished vertex stream so that this path
                // remains effective even when rects are only a layout cache.
                if (__state == null || __instance == null || rectsField == null) return;
                Rect[] snapshot = __state as Rect[];
                if (snapshot == null) return;
                Rect[] rects = rectsField.GetValue(__instance) as Rect[];
                if (rects == null || rects.Length != snapshot.Length) return;
                Array.Copy(snapshot, rects, rects.Length);
            }
            catch { }
        }

        private static void LayerPostfixVertices(object __instance, UnityEngine.UI.VertexHelper __0, object __state)
        {
            try
            {
                if (__0 == null || __instance == null || __state == null || pressAt == null) return;
                Rect[] rects = __state as Rect[];
                if (rects == null) return;
                List<UIVertex> verts = new List<UIVertex>();
                __0.GetUIVertexStream(verts);
                bool changed = false;
                int changedCount = 0;
                int[] slotHits = new int[rects.Length];
                float now = Time.unscaledTime;
                for (int i = 0; i < verts.Count; i++)
                {
                    Vector3 p = verts[i].position;
                    int best = -1;
                    float bestArea = float.MaxValue;
                    for (int s = 0; s < rects.Length && s < pressAt.Length; s++)
                    {
                        float t = now - pressAt[s];
                        if (t < 0f || t >= KeyVibDuration) continue;
                        Rect r = rects[s];
                        if (!r.Contains(new Vector2(p.x, p.y))) continue;
                        float area = Mathf.Abs(r.width * r.height);
                        if (area < bestArea) { bestArea = area; best = s; }
                    }
                    if (best < 0) continue;
                    float t2 = now - pressAt[best];
                    float decay = 1f - t2 / KeyVibDuration;
                    decay *= decay;
                    float phase = 2f * Mathf.PI * KeyVibFreq * t2;
                    float oy = KeyVibAmp * Mathf.Sin(phase) * decay;
                    float ox = KeyVibAmp * 0.35f * Mathf.Sin(phase * 0.5f + Mathf.PI * 0.5f) * decay;
                    UIVertex v = verts[i];
                    v.position = new Vector3(v.position.x + ox, v.position.y + oy, v.position.z);
                    verts[i] = v;
                    changed = true;
                    changedCount++;
                    slotHits[best]++;
                }
                if (changed)
                {
                    __0.Clear();
                    // Vertices from GetUIVertexStream are an expanded triangle stream,
                    // they must go back through AddUIVertexTriangleStream.
                    __0.AddUIVertexTriangleStream(verts);
                    if (vertexCount < 12)
                    {
                        vertexCount++;
                        string instName = "";
                        try { Component comp = __instance as Component; if (comp != null) instName = comp.gameObject.name; } catch { }
                        string slotInfo = "";
                        for (int q = 0; q < slotHits.Length; q++) if (slotHits[q] > 0) slotInfo += " s" + q + "=" + slotHits[q];
                        Debug.Log("[HitError] keyshake vertices drawn layer=" + instName + " verts=" + verts.Count + " changed=" + changedCount + slotInfo);
                    }
                }
                // Restore layout data immediately; only the generated vertices move.
                if (rectsField != null)
                {
                    Rect[] current = rectsField.GetValue(__instance) as Rect[];
                    if (current != null && current.Length == rects.Length)
                        Array.Copy(rects, current, current.Length);
                }
            }
            catch { }
        }

        private static void Dirty(object layer)
        {
            if (layer == null) return;
            try
            {
                MethodInfo m = layer.GetType().GetMethod("SetVerticesDirty", F);
                if (m != null) m.Invoke(layer, null);
            }
            catch { }
        }

        private static void DumpTree(Transform t, List<string> outList, int depth)
        {
            if (t == null || outList.Count > 60) return;
            string s = "";
            for (int i = 0; i < depth; i++) s += "  ";
            s += t.gameObject.name;
            UnityEngine.UI.Graphic[] gs = t.GetComponents<UnityEngine.UI.Graphic>();
            if (gs.Length > 0)
            {
                string names = "";
                for (int i = 0; i < gs.Length; i++)
                {
                    names += (i == 0 ? "" : ",") + gs[i].GetType().Name + (gs[i].enabled ? "" : "(off)")
                        + "/a" + gs[i].color.a.ToString("0.0");
                }
                s += "  [" + names + "]";
            }
            outList.Add(s);
            for (int i = 0; i < t.childCount; i++) DumpTree(t.GetChild(i), outList, depth + 1);
        }

        private static void EnsureLayers()
        {
            try
            {
                if (shapeLayer == null) return;
                Component sc = shapeLayer as Component;
                if (sc == null || sc.transform == null) return;
                Transform sizeT = sc.transform.parent;
                if (sizeT == null) return;
                List<object> list = new List<object>();
                for (int i = 0; i < sizeT.childCount; i++)
                {
                    Transform c = sizeT.GetChild(i);
                    Component oc = c.GetComponent(shapeLayer.GetType());
                    if (oc != null) list.Add(oc);
                }
                if (list.Count == 0) return;
                object[] arr = list.ToArray();
                bool dirty = extraLayers == null || extraLayers.Length != arr.Length;
                if (!dirty)
                {
                    for (int i = 0; i < arr.Length; i++) if (!object.ReferenceEquals(extraLayers[i], arr[i])) { dirty = true; break; }
                }
                if (dirty)
                {
                    extraLayers = arr;
                    string names = "";
                    for (int i = 0; i < arr.Length; i++)
                    {
                        Component cc = arr[i] as Component;
                        names += (i == 0 ? "" : ",") + (cc == null ? "?" : cc.gameObject.name);
                    }
                    Diag("bound layers=" + names);
                }
            }
            catch { }
        }

        private static void Diag(string msg)
        {
            if (msg == lastDiag) return;
            lastDiag = msg;
            Debug.Log("[HitError] keyshake: " + msg);
        }

        private void Discover()
        {
            try
            {
                if (keyViewerType == null) keyViewerType = FindKeyViewerType();
                if (keyViewerType == null) { Diag("jkv type not found"); return; }
                if (keysField == null) keysField = keyViewerType.GetField("Keys", F);
                if (shapeField == null) shapeField = keyViewerType.GetField("keyShapeLayer", F);
                if (outlineField == null) outlineField = keyViewerType.GetField("keyOutlineLayer", F);
                if (keysField == null || shapeField == null) { Diag("fields missing on " + keyViewerType.FullName); return; }

                Type keyType = keysField.FieldType.IsArray ? keysField.FieldType.GetElementType() : null;
                if (keyType != null)
                {
                    if (slotField == null) slotField = keyType.GetField("shapeSlot", F);
                    if (pressedField == null) pressedField = keyType.GetField("isPressed", F);
                }
                if (slotField == null || pressedField == null) { Diag("Key fields missing on " + (keyType == null ? "null" : keyType.FullName)); return; }

                UnityEngine.Object[] instances;
                try { instances = UnityEngine.Object.FindObjectsOfType(keyViewerType); }
                catch (Exception e) { Diag("FindObjectsOfType failed: " + e.Message); return; }
                if (instances == null || instances.Length == 0) { Diag("no KeyViewer instance"); return; }

                object inst = instances[0];
                Array keyArr = keysField.GetValue(inst) as Array;
                if (keyArr == null || keyArr.Length == 0) { Diag("keys empty"); return; }

                shapeLayer = shapeField.GetValue(inst);
                outlineLayer = outlineField == null ? null : outlineField.GetValue(inst);
                if (shapeLayer == null) { Diag("shape layer null"); return; }
                if (outlineLayer == null)
                {
                    // KeyViewer.keyOutlineLayer is null on this install; the outline
                    // graphic is a sibling named KeyOutlines under SizeObject.
                    try
                    {
                        Component shapeComp0 = shapeLayer as Component;
                        Transform parentT = shapeComp0.transform.parent;
                        if (parentT != null)
                        {
                            for (int ci = 0; ci < parentT.childCount; ci++)
                            {
                                Transform c = parentT.GetChild(ci);
                                if (c.name == "KeyOutlines")
                                {
                                    Component oc = c.GetComponent(shapeLayer.GetType());
                                    if (oc != null) { outlineLayer = oc; Diag("outline found via sibling " + c.name); }
                                    break;
                                }
                            }
                        }
                    }
                    catch { }
                }
                if (rectsField == null) rectsField = shapeLayer.GetType().GetField("rects", F);
                if (rectsField == null) { Diag("rects field missing"); return; }
                shapeRects = rectsField.GetValue(shapeLayer) as Array;
                if (shapeRects == null || shapeRects.Length == 0) { Diag("rects empty"); return; }
                if (outlineLayer != null && outRectsField == null) outRectsField = outlineLayer.GetType().GetField("rects", F);
                outRects = (outlineLayer != null && outRectsField != null) ? outRectsField.GetValue(outlineLayer) as Array : null;

                keys = keyArr;
                slotOf = new int[keyArr.Length];
                lastPressed = new bool[keyArr.Length];
                for (int i = 0; i < keyArr.Length; i++)
                {
                    object k = keyArr.GetValue(i);
                    slotOf[i] = k == null ? -1 : (int)slotField.GetValue(k);
                    lastPressed[i] = k != null && (bool)pressedField.GetValue(k);
                }

                baseRects = new Rect[shapeRects.Length];
                for (int i = 0; i < shapeRects.Length; i++) baseRects[i] = (Rect)shapeRects.GetValue(i);
                outBaseRects = null;
                if (outRects != null)
                {
                    outBaseRects = new Rect[outRects.Length];
                    for (int i = 0; i < outRects.Length; i++) outBaseRects[i] = (Rect)outRects.GetValue(i);
                }
                pressAt = new float[shapeRects.Length];
                for (int i = 0; i < pressAt.Length; i++) pressAt[i] = -1000f;

                discovered = true;
                try
                {
                    Component shapeComp = shapeLayer as Component;
                    if (shapeComp != null)
                    {
                        Transform tt = shapeComp.transform;
                        string chain = "";
                        int guard = 0;
                        while (tt != null && guard < 12)
                        {
                            chain += tt.gameObject.name + (tt.parent == null ? "" : " <- ");
                            tt = tt.parent;
                            guard++;
                        }
                        Diag("chain: " + chain);
                        List<string> dump = new List<string>();
                        DumpTree(shapeComp.transform.parent, dump, 0);
                        for (int d = 0; d < dump.Count && d < 24; d++) Diag("tree: " + dump[d]);
                    }
                }
                catch { }
                string layerInfo = "";
                try
                {
                    Component sc = shapeLayer as Component;
                    if (sc != null)
                    {
                        layerInfo = " layer=" + sc.gameObject.name + " active=" + sc.gameObject.activeInHierarchy
                            + " parent=" + (sc.transform.parent == null ? "null" : sc.transform.parent.name);
                        UnityEngine.UI.Graphic g = shapeLayer as UnityEngine.UI.Graphic;
                        if (g != null) layerInfo += " alpha=" + g.color.a.ToString("0.00") + " raya=" + g.raycastTarget;
                    }
                }
                catch { }
                Diag("ready keys=" + keyArr.Length + " slots=" + shapeRects.Length + " outline=" + (outRects != null) + layerInfo);
            }
            catch (Exception e) { Diag("error " + e.GetType().Name + ": " + e.Message); }
        }

        private static string Describe(object k)
        {
            Type t = k.GetType();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(t.FullName);
            FieldInfo[] fs = t.GetFields(F);
            for (int i = 0; i < fs.Length; i++)
            {
                string n = fs[i].Name.ToLowerInvariant();
                if (n.IndexOf("rect") >= 0 || n.IndexOf("trans") >= 0)
                    sb.Append(" [F]").Append(fs[i].Name).Append(':').Append(fs[i].FieldType.Name);
            }
            PropertyInfo[] ps = t.GetProperties(F);
            for (int i = 0; i < ps.Length; i++)
            {
                string n = ps[i].Name.ToLowerInvariant();
                if (n.IndexOf("rect") >= 0 || n.IndexOf("trans") >= 0)
                    sb.Append(" [P]").Append(ps[i].Name).Append(':').Append(ps[i].PropertyType.Name);
            }
            return sb.ToString();
        }

        private static RectTransform GetRect(object key)
        {
            try
            {
                Type t = key.GetType();
                PropertyInfo p = t.GetProperty("rectTransform", F);
                if (p != null)
                {
                    RectTransform rp = p.GetValue(key, null) as RectTransform;
                    if (rp != null) return rp;
                }
                FieldInfo f = t.GetField("rectTransform", F);
                if (f != null)
                {
                    RectTransform rf = f.GetValue(key) as RectTransform;
                    if (rf != null) return rf;
                }
                Component comp = key as Component;
                if (comp != null)
                {
                    RectTransform rt = comp.transform as RectTransform;
                    if (rt != null) return rt;
                }
            }
            catch { }
            return null;
        }

        private static Type FindKeyViewerType()
        {
            try
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    string n = asms[i].GetName().Name;
                    if (n == null || n.IndexOf("JipperKeyViewer", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Type t = asms[i].GetType("JipperKeyViewer.KeyViewer.KeyViewer");
                    if (t != null) return t;
                }
            }
            catch { }
            return null;
        }
    }
}
