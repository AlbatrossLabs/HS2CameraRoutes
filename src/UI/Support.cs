using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using BepInEx;
using HarmonyLib;
using KKAPI.Studio.UI.Toolbars;
using UnityEngine;

namespace HS2CameraRoutes.UI
{
    internal static class Gui
    {
        /// <summary>
        /// Call right after a click that adds or removes controls (unfolding a section). IMGUI lays the window
        /// out once per frame; drawing more controls than were laid out in the same event makes Unity log a
        /// layout error. Ending this event lets the next frame lay out the new controls cleanly.
        /// </summary>
        public static void Relayout()
        {
            var e = Event.current;
            if (e != null && e.type != EventType.Layout && e.type != EventType.Repaint) GUIUtility.ExitGUI();
        }

        /// <summary>Screen size in GUI units (differs from pixels when an IMGUI scaling plugin sets GUI.matrix).</summary>
        public static float Width { get { return Screen.width / Mathf.Max(0.01f, GUI.matrix.m00); } }
        public static float Height { get { return Screen.height / Mathf.Max(0.01f, GUI.matrix.m11); } }
    }

    /// <summary>
    /// Studio's keyboard shortcuts (Delete, F2, Esc, camera keys...) are skipped while Studio thinks you're
    /// typing in one of its fields. This makes Studio think so while the pointer is over the Camera Routes
    /// window, as Studio Browser does for its windows.
    /// </summary>
    internal static class InputGuard
    {
        private static readonly HashSet<object> Active = new HashSet<object>();

        public static void Set(object owner, bool active)
        {
            if (active) Active.Add(owner);
            else Active.Remove(owner);
        }

        public static void Install(Harmony harmony)
        {
            var getter = AccessTools.PropertyGetter(typeof(Studio.Studio), "isInputNow");
            if (getter == null)
            {
                CameraRoutesPlugin.Logger.LogWarning("Studio.isInputNow not found; Studio's shortcut keys will also fire over the Camera Routes window.");
                return;
            }
            try { harmony.Patch(getter, postfix: new HarmonyMethod(typeof(InputGuard), "Postfix")); }
            catch (Exception e) { CameraRoutesPlugin.Logger.LogWarning("Couldn't hook Studio.isInputNow: " + e.Message); }
        }

        private static void Postfix(ref bool __result)
        {
            if (Active.Count > 0) __result = true;
        }
    }

    /// <summary>
    /// The Camera Routes button on Studio's left toolbar, placed like Studio Browser's: it asks for the bottom
    /// row and KKAPI moves it to the first free spot. Dragging it in KKAPI's toolbar edit mode wins. Older
    /// HS2API builds without the toolbar manager get the plain button.
    /// </summary>
    internal static class ToolbarButton
    {
        public static void Add(BaseUnityPlugin owner, string title, Texture2D icon, Action onClick)
        {
            try
            {
                AddPositioned(owner, "cameraroutes", title, icon, onClick);
                return;
            }
            catch (Exception ex)
            {
                if (!(ex is TypeLoadException || ex is MissingMethodException || ex is MissingFieldException)) throw;
                CameraRoutesPlugin.Logger.LogDebug("Toolbar manager not available, using the plain toolbar button: " + ex.Message);
            }
#pragma warning disable 618
            KKAPI.Studio.UI.CustomToolbarButtons.AddLeftToolbarButton(icon, onClick);
#pragma warning restore 618
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AddPositioned(BaseUnityPlugin owner, string id, string title, Texture2D icon, Action onClick)
        {
            var button = new SimpleToolbarButton(id, title, () => icon, owner, _ => onClick());
            button.SetDesiredPosition(0, 0);
            ToolbarManager.AddLeftToolbarControl(button);
        }
    }

    /// <summary>
    /// Toolbar icon, drawn in code like the Studio Browser icons: a movie camera in light grey with a dotted
    /// neon path curving away from it, and the browsers' neon tab along the bottom.
    /// </summary>
    internal static class RoutesIcon
    {
        public static Texture2D Create()
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            var line = new Color(0.92f, 0.92f, 0.92f, 1f);
            var accent = Theme.Col(Theme.NeonHex);

            // Camera body (rounded rectangle outline), lens hood (trapezoid), two reels on top. Top-down coordinates.
            Vector2 bodyMin = new Vector2(4f, 12f), bodyMax = new Vector2(19f, 22f);
            var hood = new[] { new Vector2(19f, 15f), new Vector2(25f, 12.5f), new Vector2(25f, 21.5f), new Vector2(19f, 19f) };
            Vector2 reel1 = new Vector2(8f, 8.5f), reel2 = new Vector2(15f, 8.5f);
            // Dotted path from the lens sweeping up and round.
            var path = new[] { new Vector2(27.5f, 10f), new Vector2(28.5f, 6.5f), new Vector2(26f, 3.8f), new Vector2(22.5f, 3.2f) };

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    var p = new Vector2(x + 0.5f, size - (y + 0.5f));
                    float d = RoundRectEdge(p, bodyMin, bodyMax, 2.5f);
                    for (int i = 0; i < hood.Length; i++) d = Mathf.Min(d, Seg(p, hood[i], hood[(i + 1) % hood.Length]));
                    d = Mathf.Min(d, Mathf.Abs((p - reel1).magnitude - 2.6f));
                    d = Mathf.Min(d, Mathf.Abs((p - reel2).magnitude - 2.6f));
                    float cover = Mathf.Clamp01(1.4f - d);
                    var c = cover > 0f ? new Color(line.r, line.g, line.b, cover * 0.9f) : new Color(0f, 0f, 0f, 0f);

                    float dot = float.MaxValue;
                    for (int i = 0; i < path.Length; i++) dot = Mathf.Min(dot, (p - path[i]).magnitude);
                    float dotCover = Mathf.Clamp01(1.9f - dot);
                    if (dotCover > 0f) c = Color.Lerp(c, accent, dotCover);

                    if (p.x >= 9f && p.x <= 23f && p.y >= 28f && p.y <= 30f) c = accent; // the browsers' neon tab
                    tex.SetPixel(x, y, c);
                }
            tex.Apply();
            return tex;
        }

        private static float RoundRectEdge(Vector2 p, Vector2 min, Vector2 max, float r)
        {
            Vector2 c = (min + max) * 0.5f, h = (max - min) * 0.5f;
            float qx = Mathf.Abs(p.x - c.x) - (h.x - r), qy = Mathf.Abs(p.y - c.y) - (h.y - r);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return Mathf.Abs(outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r);
        }

        private static float Seg(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - a - ab * t).magnitude;
        }
    }
}
