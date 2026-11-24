using System;
using System.Globalization;
using HS2CameraRoutes.UI;
using KKAPI.Utilities;
using Studio;
using UnityEngine;

namespace HS2CameraRoutes
{
    /// <summary>
    /// The Camera Routes window, in the Albatross Labs look shared with Studio Browser and Camera Sweep: numbered
    /// fold-out sections with a one-line summary each, a scroll area, and a footer with the actions and a status
    /// line (which also shows what the control under the pointer does). Clicking a move builds it right away.
    /// </summary>
    internal sealed class PresetWindow
    {
        private const int WindowId = 0x43525431; // "CRT1"
        private const float Width = 400f;
        private const float Inner = Width - 44f; // content width inside the scroll area (room for the scrollbar)

        private readonly CameraRoutesPlugin _p;
        private Rect _rect;
        private bool _rectLoaded;
        private bool _visible;
        private Vector2 _scroll;
        private float _contentHeight;

        private bool _showMoves = true, _showSettings = true, _showEasing, _showSubject, _showRecord;

        private string _targetText = "";
        private string _currentText = "";
        private string _charName = "";
        private string _loopsText = "";
        private string _trackInfo = "";
        private bool _reframing;
        private bool _canRecord, _recorderAvailable, _canPlay, _timelineAvailable;
        private OCIRoute _route;            // the selected route (Play / Stop / scrub)
        private float _routeTotal;          // its length in seconds, as Studio plays it
        private bool _pingPong;             // a looping two-point route goes there and back
        private float _scrubTime;
        private OCIRoute _active;                                   // the route the moves change
        private string _activePresetId = "";
        private bool _lookingThrough;
        private readonly System.Collections.Generic.List<OCIRoute> _routes = new System.Collections.Generic.List<OCIRoute>();
        private readonly System.Collections.Generic.List<string> _routeLabels = new System.Collections.Generic.List<string>();
        private int _pickedGroup;
        private string _pickedPreset = "push_in";
        private static string[] _groupNames;
        private static readonly string[] ViewNames = { "Route cam", "Free cam" };
        private float _nextRefresh;

        private string _status;
        private float _statusUntil;

        private Texture2D _preview;
        private EaseCurve _previewCurve = (EaseCurve)(-1);
        private EaseMode _previewMode;

        private static readonly string[] SubjectNames = { "Off", "Pivot", "Character" };
        private static readonly string[] BoneNames = { "Head", "Chest", "Hips" };
        private static readonly string[] ModeNames = { "In-Out", "In", "Out" };
        private static readonly string[] TimeNames = { "Seconds", "Animation loops" };
        private static string[] _curveNames;

        public PresetWindow(CameraRoutesPlugin plugin) { _p = plugin; }

        public bool Visible
        {
            get { return _visible; }
            set
            {
                if (_visible && !value) SaveRect();
                _visible = value;
                if (value) _nextRefresh = 0f;
                else InputGuard.Set(this, false);
            }
        }

        public void SetStatus(string text)
        {
            _status = text;
            _statusUntil = Time.unscaledTime + 6f;
            _nextRefresh = 0f;
        }

        public void Tick()
        {
            if (!_visible)
            {
                InputGuard.Set(this, false);
                return;
            }
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 0.25f;
            Selection sel = RouteBuilder.Resolve();
            string looking = RouteBuilder.SubjectName(sel);
            _targetText = RouteBuilder.Describe(sel.Target) + (looking != null ? "</b>" + Theme.Hint("  looking at  ") + "<b>" + looking : "");
            RouteRecord rec = _p.CurrentRecord();
            _currentText = rec != null ? rec.Summary() : "";
            _charName = _p.Subject.Value == SubjectKind.Character ? _p.SubjectCharacterName() : "";
            _loopsText = _p.DurationModeSetting.Value == DurationMode.AnimationLoops ? _p.LoopsPreview() : "";
            _trackInfo = _p.SubjectTrackingInfo();
            _reframing = _p.Reframing;
            _recorderAvailable = _p.Recorder.Available;
            _timelineAvailable = _p.Baker.Available;
            OCIRoute active = _p.ActiveRoute;
            if (active != _active)
            {
                // A different route became active: show its move in the picker.
                RouteRecord ar = RouteRegistry.Get(active);
                _activePresetId = ar != null ? ar.PresetId : "";
                int dir;
                int g = MoveGroup.Find(_activePresetId, out dir);
                if (g >= 0) { _pickedGroup = g; _pickedPreset = _activePresetId; }
            }
            else
            {
                RouteRecord ar = RouteRegistry.Get(active);
                _activePresetId = ar != null ? ar.PresetId : "";
            }
            _active = active;
            _lookingThrough = _p.LookingThroughActive;
            _routes.Clear();
            _routeLabels.Clear();
            foreach (OCIRoute r in _p.OwnRoutes())
            {
                _routes.Add(r);
                ObjectCtrlInfo rider = r.routeInfo.child.Count > 0 ? Studio.Studio.GetCtrlInfo(r.routeInfo.child[0].dicKey) : null;
                string who = rider != null ? RouteBuilder.Describe(rider) : "(empty)";
                _routeLabels.Add(who + "   ·   " + r.name.Replace(RouteBuilder.NamePrefix, ""));
            }

            OCIRoute route = _p.SelectedRoute();
            if (route != _route) _scrubTime = 0f;
            _route = route;
            _routeTotal = route != null ? RoutePath.From(route).Total : 0f;
            _pingPong = route != null && route.routeInfo.loop && route.listPoint.Count == 2;
            _canPlay = route != null;
            _canRecord = _recorderAvailable && _canPlay;
        }

        // ================================================================ window

        public void Draw()
        {
            if (!_visible) return;
            var old = GUI.skin;
            GUI.skin = Theme.Skin;
            try { DrawFrame(); }
            finally { GUI.skin = old; }
        }

        private void DrawFrame()
        {
            if (!_rectLoaded) LoadRect();
            var e = Event.current;
            if (e.type == EventType.Repaint) InputGuard.Set(this, _rect.Contains(e.mousePosition));

            _rect.width = Width;
            // GUILayout.Window only ever grows. Start it small in the layout pass (the only pass that re-measures)
            // so it shrinks back to fit when a section folds; the other passes keep the measured size.
            if (e.type == EventType.Layout) _rect.height = 10f;
            _rect = GUILayout.Window(WindowId, _rect, DrawContents, "Camera Routes " + CameraRoutesPlugin.Version);
            _rect.x = Mathf.Clamp(_rect.x, 0f, Mathf.Max(0f, Gui.Width - _rect.width));
            _rect.y = Mathf.Clamp(_rect.y, 0f, Mathf.Max(0f, Gui.Height - 40f));
        }

        private void DrawContents(int id)
        {
            if (GUI.Button(new Rect(_rect.width - 30, 7, 22, 20), "x")) Visible = false;

            GUILayout.Label(Theme.Hint("Target  ") + "<b>" + _targetText + "</b>", Theme.RichSingle, GUILayout.Width(Width - 20), GUILayout.Height(20));
            GUILayout.Label(_currentText.Length > 0 ? Theme.Hint("Route  ") + _currentText : Theme.Hint("Select a camera to move it, or nothing to make a new camera from the view."),
                Theme.RichSingle, GUILayout.Width(Width - 20), GUILayout.Height(18));

            // A GUILayout scroll view doesn't size itself to its content, so measure the content on each repaint
            // and use that height (up to most of the screen; past that it scrolls).
            float maxHeight = Mathf.Max(220f, Gui.Height - 220f);
            float height = _contentHeight > 0f ? Mathf.Min(_contentHeight + 6f, maxHeight) : maxHeight;
            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.Height(height));
            GUILayout.BeginVertical(GUILayout.Width(Inner));

            if (Section("1  Routes", RoutesSummary(), ref _showMoves)) DrawRoutes();
            if (Section("2  Move", MoveSummary(), ref _showSettings)) DrawSettings();
            if (Section("3  Easing", EasingSummary(), ref _showEasing)) DrawEasing();
            if (Section("4  Track subject", SubjectSummary(), ref _showSubject)) DrawSubject();
            if (Section("5  Record", RecordSummary(), ref _showRecord)) DrawRecord();

            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint) _contentHeight = GUILayoutUtility.GetLastRect().height;
            GUILayout.EndScrollView();

            DrawFooter();

            GUI.DragWindow(new Rect(0, 0, _rect.width - 34, 28));
            IMGUIUtils.EatInputInRect(_rect);
        }

        /// <summary>Fold-out section header with a one-line summary, as in Camera Sweep. Returns true when open.</summary>
        private static bool Section(string title, string summary, ref bool open)
        {
            GUILayout.Space(4);
            if (GUILayout.Button((open ? "▾" : "▸") + " <b>" + title + "</b>   " + Theme.Hint(summary), Theme.Section, GUILayout.Width(Inner)))
            {
                open = !open;
                Gui.Relayout();
            }
            return open;
        }

        // ================================================================ sections

        /// <summary>
        /// Section 1: the scene's Camera Routes routes (pick the one the move buttons change), Add route, which camera
        /// the viewport looks through, and testing the active route (Play / Stop / Scrub / Reframe start).
        /// </summary>
        private void DrawRoutes()
        {
            if (_routes.Count == 0)
            {
                GUILayout.Label(Theme.Hint("No routes yet. Pick a move below, or Add route, to make one from the current view."), Theme.Rich, GUILayout.Width(Inner));
            }
            for (int i = 0; i < _routes.Count; i++)
            {
                OCIRoute r = _routes[i];
                if (r == null || r.objectItem == null) continue;
                bool active = r == _active;
                GUILayout.BeginHorizontal();
                if (GUILayout.Toggle(active, _routeLabels[i], Theme.Choice, GUILayout.Width(Inner - 30), GUILayout.Height(22)) && !active)
                {
                    _p.SetActive(r);
                    _nextRefresh = 0f;
                }
                if (GUILayout.Button(new GUIContent("x", "Remove this route (the camera stays; Undo brings the route back)"), GUILayout.Width(26), GUILayout.Height(22)))
                {
                    SetStatus(_p.RemoveRoute(r));
                    _nextRefresh = 0f;
                    Gui.Relayout();
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(new GUIContent("Add route", "New camera at the current view with the move picked below"), GUILayout.Width(110), GUILayout.Height(24)))
            {
                Preset preset = MoveGroup.PresetById(_pickedPreset) ?? Preset.All[0];
                SetStatus(_p.AddRoute(preset));
                _nextRefresh = 0f;
                Gui.Relayout();
            }
            GUI.enabled = _active != null;
            int view = GUILayout.Toolbar(_lookingThrough ? 0 : 1, ViewNames, Theme.Choice, GUILayout.Width(Inner - 114), GUILayout.Height(24));
            GUI.enabled = true;
            if (view != (_lookingThrough ? 0 : 1))
            {
                SetStatus(_p.SetView(view == 0));
                _lookingThrough = view == 0;
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            GUI.enabled = _canPlay;
            if (GUILayout.Button(new GUIContent("Play", "Play the active route from its start"), GUILayout.Width(80), GUILayout.Height(24))) SetStatus(_p.PlaySelected());
            if (GUILayout.Button(new GUIContent("Stop", "Stop the active route and put the camera back at its start"), GUILayout.Width(80), GUILayout.Height(24))) SetStatus(_p.StopSelected());
            GUI.enabled = true;
            if (_reframing)
            {
                if (GUILayout.Button(new GUIContent("Use this view", "Rebuild the route starting from the viewport's view"), Theme.Primary, GUILayout.Height(24)))
                    SetStatus(_p.UseThisView());
                if (GUILayout.Button("Cancel", GUILayout.Width(60), GUILayout.Height(24))) SetStatus(_p.CancelReframe());
            }
            else
            {
                GUI.enabled = _currentText.Length > 0;
                if (GUILayout.Button(new GUIContent("Reframe start", "Step out into the free camera at the starting shot, frame a new start, then Use this view"), GUILayout.Height(24)))
                    SetStatus(_p.ReframeStart());
                GUI.enabled = true;
            }
            GUILayout.EndHorizontal();
            DrawScrubber();
        }

        /// <summary>The move picker at the top of section 2: a type, then a direction (which builds it on the active route).</summary>
        private void DrawMovePicker()
        {
            if (_groupNames == null) _groupNames = Array.ConvertAll(MoveGroup.All, g => g.Name);
            int g0 = _pickedGroup;
            int g1 = GUILayout.SelectionGrid(g0, _groupNames, 5, Theme.Choice, GUILayout.Width(Inner));
            if (g1 != g0) _pickedGroup = g1;

            MoveGroup group = MoveGroup.All[Mathf.Clamp(_pickedGroup, 0, MoveGroup.All.Length - 1)];
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            float w = (Inner - 4f * (group.Directions.Length - 1)) / group.Directions.Length;
            for (int d = 0; d < group.Directions.Length; d++)
            {
                Preset preset = MoveGroup.PresetById(group.PresetIds[d]);
                bool current = _activePresetId == group.PresetIds[d];
                if (GUILayout.Button(new GUIContent(group.Directions[d], preset != null ? preset.Hint : ""), current ? Theme.Primary : GUI.skin.button,
                        GUILayout.Width(w), GUILayout.Height(28)) && preset != null)
                {
                    _pickedPreset = preset.Id;
                    SetStatus(_p.Apply(preset));
                    _nextRefresh = 0f;
                    Gui.Relayout();
                }
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(Theme.Hint(_active != null ? "Changes the active route." : "Makes your first route from the current view."), Theme.RichSingle, GUILayout.Width(Inner), GUILayout.Height(18));
            GUILayout.Space(2);
        }

        /// <summary>Time slider for the selected route: drag to move the camera through it; follows playback.</summary>
        private void DrawScrubber()
        {
            bool ok = _route != null && _route.objectItem != null && _routeTotal > 0f;
            float shown = _scrubTime;
            if (ok)
            {
                float elapsed = Tracker.Elapsed(_route);
                if (elapsed >= 0f)
                {
                    if (_route.routeInfo.loop)
                    {
                        float cycle = _pingPong ? _routeTotal * 2f : _routeTotal;
                        elapsed %= cycle;
                        if (_pingPong && elapsed > _routeTotal) elapsed = cycle - elapsed;
                    }
                    shown = Mathf.Min(elapsed, _routeTotal);
                }
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Scrub", "Drag to move the camera through the selected route (stops playback)"), GUILayout.Width(84));
            GUI.enabled = ok;
            float v = GUILayout.HorizontalSlider(ok ? shown : 0f, 0f, ok ? _routeTotal : 1f, GUILayout.Width(Inner - 84 - 82));
            GUI.enabled = true;
            GUILayout.Label(ok ? shown.ToString("0.00") + " / " + _routeTotal.ToString("0.0") + " s" : "", GUILayout.Width(76));
            GUILayout.EndHorizontal();

            if (ok && !Mathf.Approximately(v, shown))
            {
                _scrubTime = v;
                _p.Scrub(v);
            }
        }

        private void DrawSettings()
        {
            DrawMovePicker();
            _p.AutoUpdate.Value = GUILayout.Toggle(_p.AutoUpdate.Value, new GUIContent("Update the selected route live",
                "Selecting a camera on a route loads its settings here; changing one rebuilds the route when you let go"));
            GUILayout.Space(2);
            bool fixedMode = _p.SizeModeSetting.Value == SizeMode.FixedUnits;
            string sizeText = fixedMode
                ? (_p.Size.Value * _p.FixedBase.Value).ToString("0.0") + " u"
                : Mathf.RoundToInt(_p.Size.Value * 100f) + "%";
            _p.Size.Value = Slider("Size", "How far straight moves and spirals travel (100% = each move's default)", _p.Size.Value, 0.1f, 4f, sizeText, 0.05f);
            _p.ArcAngle.Value = Slider("Arc angle", "Degrees for Arc Left/Right/Up/Down; Sway swings ± half of it", _p.ArcAngle.Value, 5f, 300f, Mathf.RoundToInt(_p.ArcAngle.Value) + "°", 5f);
            _p.DutchAngle.Value = Slider("Dutch angle", "How far Dutch Left/Right roll by the end", _p.DutchAngle.Value, 1f, 45f, Mathf.RoundToInt(_p.DutchAngle.Value) + "°", 1f);
            _p.Handheld.Value = Slider("Handheld", "Handheld camera shake on any move (1 ≈ ±0.6°)", _p.Handheld.Value, 0f, 3f, _p.Handheld.Value <= 0f ? "Off" : _p.Handheld.Value.ToString("0.0"), 0.1f);

            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent("Time by", "Seconds, or loops of the subject character's current animation"), GUILayout.Width(84));
            int mode = GUILayout.Toolbar((int)_p.DurationModeSetting.Value, TimeNames, Theme.Choice, GUILayout.Width(Inner - 88), GUILayout.Height(22));
            if (mode != (int)_p.DurationModeSetting.Value) { _p.DurationModeSetting.Value = (DurationMode)mode; _nextRefresh = 0f; }
            GUILayout.EndHorizontal();

            if (_p.DurationModeSetting.Value == DurationMode.AnimationLoops)
            {
                float before = _p.Loops.Value;
                _p.Loops.Value = Slider("Loops", "Animation loops for the whole move (one lap for Orbit)", _p.Loops.Value, 0.5f, 16f,
                    _p.Loops.Value.ToString("0.#") + (_p.Loops.Value == 1f ? " loop" : " loops"), 0.5f);
                if (!Mathf.Approximately(before, _p.Loops.Value)) _nextRefresh = 0f;
                GUILayout.Label(Theme.Hint(_loopsText), Theme.RichSingle, GUILayout.Width(Inner), GUILayout.Height(18));
            }
            else
            {
                _p.Duration.Value = Slider("Duration", "Seconds for the whole move (one lap for Orbit)", _p.Duration.Value, 0.25f, 30f, _p.Duration.Value.ToString("0.0#") + " s", 0.25f);
            }

            GUILayout.Space(2);
            _p.PlayAfterCreate.Value = GUILayout.Toggle(_p.PlayAfterCreate.Value, new GUIContent("Play after creating", "Start the route as soon as it's made"));
            _p.Loop.Value = GUILayout.Toggle(_p.Loop.Value, new GUIContent("Loop", "Straight moves and Hold go back and forth; arcs play once; Orbit and Sway always loop"));
            _p.HideMarkers.Value = GUILayout.Toggle(_p.HideMarkers.Value, new GUIContent("Hide markers while playing", "Hide the route's line and point markers while it plays, and every route's while recording"));
        }

        private void DrawEasing()
        {
            if (_curveNames == null) _curveNames = Array.ConvertAll(Easing.Curves, c => c.ToString());

            bool linear = _p.Curve.Value == EaseCurve.Linear;
            GUI.enabled = !linear;
            int mode = GUILayout.Toolbar((int)_p.Mode.Value, ModeNames, Theme.Choice, GUILayout.Width(Inner), GUILayout.Height(22));
            if (mode != (int)_p.Mode.Value) _p.Mode.Value = (EaseMode)mode;
            GUI.enabled = true;

            GUILayout.Space(2);
            int curve = GUILayout.SelectionGrid((int)_p.Curve.Value, _curveNames, 4, Theme.Choice, GUILayout.Width(Inner));
            if (curve != (int)_p.Curve.Value) _p.Curve.Value = (EaseCurve)curve;

            GUILayout.Space(4);
            Rect r = GUILayoutUtility.GetRect(Inner, 64f, GUILayout.Width(Inner));
            if (Event.current.type == EventType.Repaint)
            {
                UpdatePreview();
                GUI.DrawTexture(r, _preview, ScaleMode.StretchToFill);
            }
            GUILayout.Label(Theme.Hint(linear ? "Constant speed." : "Speed over the move. On curved moves the first segment eases in and the last eases out."),
                Theme.Rich, GUILayout.Width(Inner));
        }

        private void DrawSubject()
        {
            int s = GUILayout.Toolbar((int)_p.Subject.Value, SubjectNames, Theme.Choice, GUILayout.Width(Inner), GUILayout.Height(22));
            if (s != (int)_p.Subject.Value) { _p.Subject.Value = (SubjectKind)s; _nextRefresh = 0f; }

            bool character = _p.Subject.Value == SubjectKind.Character;
            GUILayout.Space(2);
            GUILayout.BeginHorizontal();
            GUI.enabled = character;
            int b = GUILayout.Toolbar((int)_p.Bone.Value, BoneNames, Theme.Choice, GUILayout.Width(180), GUILayout.Height(22));
            if (b != (int)_p.Bone.Value) _p.Bone.Value = (SubjectBone)b;
            string who = character ? (_charName.Length > 0 ? _charName : "…") + "  ›" : "Character";
            if (GUILayout.Button(new GUIContent(who, "Pick the next character"), GUILayout.Width(Inner - 184), GUILayout.Height(22)))
            {
                _p.PickNextCharacter();
                _nextRefresh = 0f;
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            string hint;
            switch (_p.Subject.Value)
            {
                case SubjectKind.Pivot: hint = "Keeps looking at the fixed point it was framed on (Studio's camera pivot). Best for a subject that stays put."; break;
                case SubjectKind.Character: hint = "Follows the character's " + _p.Bone.Value.ToString().ToLowerInvariant() + " as it moves. Arcs circle that character."; break;
                default: hint = "Straight moves keep their aim. Curved moves still face the pivot."; break;
            }
            GUILayout.Space(2);
            _p.TiltToFollow.Value = GUILayout.Toggle(_p.TiltToFollow.Value, new GUIContent("Tilt to follow", "On: pan and tilt to keep the subject in view. Off: pan only, keeping the starting tilt"));
            _p.KeepFraming.Value = GUILayout.Toggle(_p.KeepFraming.Value, new GUIContent("Keep framing", "Keep the subject where it is in the frame instead of turning to centre it"));
            if (_trackInfo.Length > 0)
                GUILayout.Label(_trackInfo.StartsWith("!") ? Theme.Warn(_trackInfo.Substring(1)) : Theme.Hint(_trackInfo), Theme.Rich, GUILayout.Width(Inner));
            GUILayout.Label(Theme.Hint(hint), Theme.Rich, GUILayout.Width(Inner));
        }

        private void DrawRecord()
        {
            DrawVideo();
            GUILayout.Space(6);
            DrawTimeline();
        }

        private void DrawTimeline()
        {
            if (!_timelineAvailable)
            {
                GUILayout.Label(Theme.Hint("Timeline isn't installed, so Send to Timeline is off."), Theme.Rich, GUILayout.Width(Inner));
                return;
            }
            GUILayout.BeginHorizontal();
            if (_p.Baker.Busy)
            {
                if (GUILayout.Button("Stop", Theme.Primary, GUILayout.Width(150), GUILayout.Height(26))) { _p.Baker.Cancel(); SetStatus("Send to Timeline cancelled."); }
                GUILayout.Label(_p.Baker.StatusText, Theme.RichSingle, GUILayout.Height(26));
            }
            else
            {
                GUI.enabled = _canPlay && !_p.Recorder.Busy;
                if (GUILayout.Button(new GUIContent("Send to Timeline", "Write the selected route's camera path into Timeline as Camera Position and Rotation keys, starting at Timeline's cursor. Undo takes them out again."), Theme.Primary, GUILayout.Width(150), GUILayout.Height(26)))
                    SetStatus(_p.SendToTimeline());
                GUI.enabled = true;
                GUILayout.Label(Theme.Hint(_canPlay ? "Keys start at Timeline's cursor" : "Select a camera on a route"), Theme.RichSingle, GUILayout.Height(26));
            }
            GUILayout.EndHorizontal();
            _p.TimelineKeyRate.Value = Slider("Keys / sec", "Keys per second Send to Timeline writes: more follow curves and shake more closely, fewer are easier to edit by hand", _p.TimelineKeyRate.Value, 2f, 30f, Mathf.RoundToInt(_p.TimelineKeyRate.Value) + " /s", 1f);
        }

        private void DrawVideo()
        {
            if (!_recorderAvailable)
            {
                GUILayout.Label(Theme.Warn("VideoExport isn't installed, so recording is off."), Theme.Rich, GUILayout.Width(Inner));
                return;
            }

            GUILayout.BeginHorizontal();
            if (_p.Recorder.Busy)
            {
                if (GUILayout.Button("Stop recording", Theme.Primary, GUILayout.Width(150), GUILayout.Height(26))) _p.Recorder.Cancel();
                GUILayout.Label(_p.Recorder.StatusText, Theme.RichSingle, GUILayout.Height(26));
            }
            else
            {
                GUI.enabled = _canRecord;
                if (GUILayout.Button(new GUIContent("Record route", "Record the selected route with VideoExport: lead-in, the move, tail"), Theme.Primary, GUILayout.Width(150), GUILayout.Height(26)))
                    SetStatus(_p.RecordSelected());
                GUI.enabled = true;
                GUILayout.Label(Theme.Hint(_canRecord ? "VideoExport's own format and folder" : "Select a camera on a route"), Theme.RichSingle, GUILayout.Height(26));
            }
            GUILayout.EndHorizontal();

            _p.RecordAfterCreate.Value = GUILayout.Toggle(_p.RecordAfterCreate.Value, new GUIContent("Record every new route", "Each move you click is recorded as soon as it's built"));
            _p.RestartAnimations.Value = GUILayout.Toggle(_p.RestartAnimations.Value, new GUIContent("Restart animations", "Restart every character's animation when recording starts, so loops line up"));
            _p.SeamlessLoops.Value = GUILayout.Toggle(_p.SeamlessLoops.Value, new GUIContent("Seamless loops", "Looping routes record exactly one cycle with no lead-in or tail"));
            _p.PreRoll.Value = Slider("Lead-in", "Seconds recorded before the move starts", _p.PreRoll.Value, 0f, 5f, _p.PreRoll.Value.ToString("0.0") + " s", 0.1f);
            _p.PostRoll.Value = Slider("Tail", "Seconds recorded after the move ends", _p.PostRoll.Value, 0f, 5f, _p.PostRoll.Value.ToString("0.0") + " s", 0.1f);
        }

        private void DrawFooter()
        {
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            GUI.enabled = UndoStack.Count > 0;
            string undoTip = UndoStack.Count > 0 ? "Undo " + UndoStack.NextLabel + " (up to 30 steps)" : "Nothing to undo";
            if (GUILayout.Button(new GUIContent("Undo", undoTip), GUILayout.Width(110), GUILayout.Height(24))) SetStatus(_p.Undo());
            GUI.enabled = _currentText.Length > 0;
            if (GUILayout.Button(new GUIContent("Remove route", "Take the camera off the active route and delete the route (undoable)"), GUILayout.Height(24))) SetStatus(_p.RemoveRoute());
            if (GUILayout.Button(new GUIContent("Rebuild", "Rebuild the selected route with the settings above (live update does this for you)"), GUILayout.Height(24))) SetStatus(_p.RebuildSelected());
            GUI.enabled = true;
            GUILayout.EndHorizontal();

            string line;
            if (!string.IsNullOrEmpty(_status) && Time.unscaledTime < _statusUntil) line = _status;
            else if (_p.Recorder.Busy) line = _p.Recorder.StatusText;
            else if (_p.Baker.Busy) line = _p.Baker.StatusText;
            else if (!string.IsNullOrEmpty(GUI.tooltip)) line = Theme.Hint(GUI.tooltip);
            else line = Theme.Hint(_p.ToggleKey.Value + " opens and closes this window");
            GUILayout.Label(line, Theme.RichSingle, GUILayout.Width(Width - 20), GUILayout.Height(18));
        }

        // ================================================================ controls

        private static float Slider(string label, string tip, float value, float min, float max, string shown, float snap)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(new GUIContent(label, tip), GUILayout.Width(84));
            float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(Inner - 84 - 62));
            GUILayout.Label(shown, GUILayout.Width(56));
            GUILayout.EndHorizontal();
            if (!Mathf.Approximately(v, value)) v = Mathf.Round(v / snap) * snap;
            return Mathf.Clamp(v, min, max);
        }

        // ================================================================ summaries

        private string MoveSummary()
        {
            string time = _p.DurationModeSetting.Value == DurationMode.AnimationLoops
                ? _p.Loops.Value.ToString("0.#") + " loops" : _p.Duration.Value.ToString("0.##") + " s";
            string s = Mathf.RoundToInt(_p.Size.Value * 100f) + "%  ·  " + Mathf.RoundToInt(_p.ArcAngle.Value) + "°  ·  " + time;
            if (_p.Handheld.Value > 0f) s += "  ·  handheld";
            if (_p.Loop.Value) s += "  ·  loop";
            return s;
        }

        private string RoutesSummary()
        {
            if (_routes.Count == 0) return "none yet";
            string active = _active != null ? _active.name.Replace(RouteBuilder.NamePrefix, "") : "";
            return _routes.Count + (_routes.Count == 1 ? " route" : " routes") + (active.Length > 0 ? "  ·  " + active : "");
        }

        private string EasingSummary()
        {
            if (_p.Curve.Value == EaseCurve.Linear) return "Linear";
            return _p.Curve.Value + "  ·  " + ModeNames[(int)_p.Mode.Value];
        }

        private string SubjectSummary()
        {
            switch (_p.Subject.Value)
            {
                case SubjectKind.Pivot: return "pivot";
                case SubjectKind.Character: return (_charName.Length > 0 ? _charName : "character") + "  ·  " + _p.Bone.Value.ToString().ToLowerInvariant();
                default: return "off";
            }
        }

        private string RecordSummary()
        {
            if (_p.Recorder.Busy) return "recording…";
            if (_p.Baker.Busy) return "capturing for Timeline…";
            string video = !_recorderAvailable ? "" : _p.RecordAfterCreate.Value ? "records every new route" : "VideoExport";
            string timeline = _timelineAvailable ? "Timeline" : "";
            if (video.Length > 0 && timeline.Length > 0) return video + "  ·  " + timeline;
            if (video.Length + timeline.Length == 0) return "VideoExport and Timeline not found";
            return video + timeline;
        }

        // ================================================================ easing preview

        private void UpdatePreview()
        {
            EaseCurve curve = _p.Curve.Value;
            EaseMode mode = _p.Mode.Value;
            if (_preview != null && curve == _previewCurve && mode == _previewMode) return;
            _previewCurve = curve;
            _previewMode = mode;

            const int w = 356, h = 64, pad = 6;
            if (_preview == null)
                _preview = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };

            Color bg = Theme.Col(Theme.FieldHex), edge = Theme.Col(Theme.BorderHex), guide = Theme.Col(Theme.BorderHex), ink = Theme.Col(Theme.NeonHex);
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    _preview.SetPixel(x, y, x == 0 || y == 0 || x == w - 1 || y == h - 1 ? edge : bg);

            // Value range leaves room for Back/Elastic overshoot.
            const float lo = -0.3f, hi = 1.3f;
            Func<float, float> toY = v => pad + (v - lo) / (hi - lo) * (h - 2 * pad);
            for (int x = pad; x < w - pad; x++)
            {
                _preview.SetPixel(x, Mathf.RoundToInt(toY(0f)), guide);
                _preview.SetPixel(x, Mathf.RoundToInt(toY(1f)), guide);
            }

            int samples = (w - 2 * pad) * 3;
            for (int i = 0; i <= samples; i++)
            {
                float t = (float)i / samples;
                Stamp(_preview, new Vector2(pad + t * (w - 2 * pad), toY(Easing.Evaluate(curve, mode, t))), 1.3f, ink);
            }
            _preview.Apply();
        }

        private static void Stamp(Texture2D t, Vector2 c, float r, Color color)
        {
            int x0 = Mathf.FloorToInt(c.x - r - 1), x1 = Mathf.CeilToInt(c.x + r + 1);
            int y0 = Mathf.FloorToInt(c.y - r - 1), y1 = Mathf.CeilToInt(c.y + r + 1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (x < 1 || y < 1 || x >= t.width - 1 || y >= t.height - 1) continue;
                    float a = Mathf.Clamp01(r - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c) + 0.5f);
                    if (a > 0f) t.SetPixel(x, y, Color.Lerp(t.GetPixel(x, y), color, a));
                }
        }

        // ================================================================ position

        private void LoadRect()
        {
            _rectLoaded = true;
            _rect = new Rect(Gui.Width * 0.08f, Gui.Height * 0.12f, Width, 10f);
            var parts = (_p.WindowPosition.Value ?? "").Split(',');
            float x, y;
            if (parts.Length == 2 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
            {
                _rect.x = x;
                _rect.y = y;
            }
        }

        private void SaveRect()
        {
            if (!_rectLoaded) return;
            _p.WindowPosition.Value = Mathf.Round(_rect.x).ToString(CultureInfo.InvariantCulture) + "," + Mathf.Round(_rect.y).ToString(CultureInfo.InvariantCulture);
        }
    }
}
