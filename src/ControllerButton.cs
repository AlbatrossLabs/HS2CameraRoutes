using System.Reflection;
using Studio;
using UnityEngine;
using UnityEngine.UI;

namespace HS2CameraRoutes
{
    /// <summary>
    /// Adds a presets button beside the play-all / replay-all / stop-all buttons at the bottom of Studio's
    /// Route Controller. It's a copy of the stop button, so it picks up whatever look the panel has
    /// (including Studio Theme's), with its own icon and click handler.
    /// </summary>
    internal sealed class ControllerButton
    {
        private static readonly FieldInfo StopField = typeof(RouteControl).GetField("buttonStopAll", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo ReplayField = typeof(RouteControl).GetField("buttonReAll", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private readonly CameraRoutesPlugin _p;
        private GameObject _button;
        private RouteControl _owner;
        private bool _failed;
        private Sprite _icon;

        public ControllerButton(CameraRoutesPlugin plugin) { _p = plugin; }

        public void TryInject()
        {
            if (_failed) return;
            RouteControl rc = Studio.Studio.Instance.routeControl;
            if (rc == null) return;
            if (_button != null && _owner == rc) return;
            if (!rc.gameObject.activeInHierarchy) return; // wait until the panel is opened, after any reskin

            try
            {
                Inject(rc);
            }
            catch (System.Exception e)
            {
                _failed = true;
                CameraRoutesPlugin.Logger.LogWarning("Couldn't add the Route Controller button (use Shift+F7 instead): " + e);
            }
        }

        private void Inject(RouteControl rc)
        {
            var stop = StopField != null ? StopField.GetValue(rc) as Button : null;
            var replay = ReplayField != null ? ReplayField.GetValue(rc) as Button : null;
            if (stop == null) { _failed = true; CameraRoutesPlugin.Logger.LogWarning("Route Controller stop button not found."); return; }

            GameObject go = Object.Instantiate(stop.gameObject, stop.transform.parent, false);
            go.name = "CameraRoutesPresetsButton";

            var rt = (RectTransform)go.transform;
            var stopRt = (RectTransform)stop.transform;
            if (go.transform.parent.GetComponent<LayoutGroup>() == null)
            {
                // Same gap as between the replay and stop buttons.
                Vector2 step = replay != null
                    ? stopRt.anchoredPosition - ((RectTransform)replay.transform).anchoredPosition
                    : new Vector2(stopRt.rect.width + 6f, 0f);
                if (step.sqrMagnitude < 1f) step = new Vector2(stopRt.rect.width + 6f, 0f);
                rt.anchoredPosition = stopRt.anchoredPosition + step;
            }
            go.transform.SetSiblingIndex(stop.transform.GetSiblingIndex() + 1);

            var button = go.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(_p.ToggleWindow);
            button.interactable = true;

            SetIcon(go, button);

            _button = go;
            _owner = rc;
            CameraRoutesPlugin.Logger.LogDebug("Added presets button to the Route Controller.");
        }

        private void SetIcon(GameObject go, Button button)
        {
            if (_icon == null) _icon = MakeIcon(32);

            // The icon is usually a child image; if the button has none, its own image is the icon.
            Image target = null;
            foreach (var img in go.GetComponentsInChildren<Image>(true))
            {
                if (img.gameObject == go) continue;
                target = img;
                break;
            }
            if (target == null) target = button.targetGraphic as Image;
            if (target == null) return;

            target.sprite = _icon;
            target.overrideSprite = null;
            target.type = Image.Type.Simple;
            target.preserveAspect = true;
        }

        /// <summary>A route icon: two end dots joined by a dotted curve.</summary>
        private static Sprite MakeIcon(int size)
        {
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var clear = new Color(1f, 1f, 1f, 0f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    t.SetPixel(x, y, clear);

            float s = size / 32f;
            Vector2 a = new Vector2(7f, 8f) * s, b = new Vector2(25f, 24f) * s;
            Vector2 c1 = new Vector2(24f, 4f) * s, c2 = new Vector2(8f, 28f) * s;

            // Dotted cubic between the end dots.
            for (int i = 1; i < 7; i++)
            {
                float k = i / 7f;
                Vector2 p = Bezier(a, c1, c2, b, k);
                Dot(t, p, 1.5f * s);
            }
            Dot(t, a, 3.6f * s);
            Dot(t, b, 3.6f * s);
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Vector2 Bezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float k)
        {
            float u = 1f - k;
            return u * u * u * p0 + 3f * u * u * k * p1 + 3f * u * k * k * p2 + k * k * k * p3;
        }

        private static void Dot(Texture2D t, Vector2 c, float r)
        {
            int x0 = Mathf.FloorToInt(c.x - r - 1), x1 = Mathf.CeilToInt(c.x + r + 1);
            int y0 = Mathf.FloorToInt(c.y - r - 1), y1 = Mathf.CeilToInt(c.y + r + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (x < 0 || y < 0 || x >= t.width || y >= t.height) continue;
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c);
                    float a = Mathf.Clamp01(r - d + 0.5f);
                    Color old = t.GetPixel(x, y);
                    if (a > old.a) t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
        }
    }
}
