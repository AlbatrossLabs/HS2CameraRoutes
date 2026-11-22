using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using HS2CameraRoutes.UI;
using KKAPI;
using Studio;
using UnityEngine;

namespace HS2CameraRoutes
{
    public enum SizeMode
    {
        ShareOfSubjectDistance,
        FixedUnits,
    }

    /// <summary>
    /// One-click preset camera moves (push in, pull out, crane, trolley, arcs, orbit) built as ordinary Studio
    /// routes, so they save with the scene and play from the Route Controller like hand-made routes. Each route
    /// can track a subject (a fixed pivot or a character's bone), and every action can be undone.
    /// </summary>
    [BepInPlugin(Guid, PluginName, Version)]
    [BepInProcess("StudioNEOV2")]
    [BepInDependency(KoikatuAPI.GUID)]
    public class CameraRoutesPlugin : BaseUnityPlugin
    {
        public const string Guid = "com.albatrosslabs.hs2.cameraroutes";
        public const string PluginName = "Camera Routes";
        public const string Version = "0.9.2";

        internal static new ManualLogSource Logger;
        internal static CameraRoutesPlugin Instance;

        internal ConfigEntry<KeyboardShortcut> ToggleKey;
        internal ConfigEntry<KeyboardShortcut> UndoKey;
        internal ConfigEntry<bool> ShowControllerButton;
        internal ConfigEntry<bool> ShowToolbarButton;
        internal ConfigEntry<string> WindowPosition;

        internal ConfigEntry<float> Size;
        internal ConfigEntry<float> ArcAngle;
        internal ConfigEntry<float> DutchAngle;
        internal ConfigEntry<float> Handheld;
        internal ConfigEntry<float> Duration;
        internal ConfigEntry<DurationMode> DurationModeSetting;
        internal ConfigEntry<float> Loops;
        internal ConfigEntry<EaseCurve> Curve;
        internal ConfigEntry<EaseMode> Mode;
        internal ConfigEntry<bool> Loop;
        internal ConfigEntry<bool> PlayAfterCreate;

        internal ConfigEntry<SubjectKind> Subject;
        internal ConfigEntry<SubjectBone> Bone;

        internal ConfigEntry<bool> RestartAnimations;
        internal ConfigEntry<bool> RecordAfterCreate;
        internal ConfigEntry<float> PreRoll;
        internal ConfigEntry<float> PostRoll;
        internal ConfigEntry<bool> SeamlessLoops;

        internal ConfigEntry<SizeMode> SizeModeSetting;
        internal ConfigEntry<float> FixedBase;
        internal ConfigEntry<float> FallbackDistance;
        internal ConfigEntry<bool> LookThroughNewCamera;
        internal ConfigEntry<bool> ReplacePrevious;
        internal ConfigEntry<SelectionMode> SelectionMode;
        internal ConfigEntry<bool> KeepFraming;
        internal ConfigEntry<bool> TiltToFollow;
        internal ConfigEntry<KeyboardShortcut> PlayKey;
        internal ConfigEntry<bool> ShowLine;
        internal ConfigEntry<bool> AutoUpdate;
        internal ConfigEntry<bool> HideMarkers;
        internal ConfigEntry<Color> LineColor;

        /// <summary>Character picked with the window's cycle button; -1 = whoever is nearest the line of sight.</summary>
        internal int PickedCharKey = -1;

        private PresetWindow _window;
        private ControllerButton _button;
        internal Recorder Recorder;

        private void Awake()
        {
            Logger = base.Logger;
            Instance = this;

            ToggleKey = Config.Bind("1. General", "Open presets window", new KeyboardShortcut(KeyCode.F7, KeyCode.LeftShift),
                "Show or hide the Camera Routes window.");
            UndoKey = Config.Bind("1. General", "Undo", KeyboardShortcut.Empty,
                "Undo the last Camera Routes action (also a button in the window). Not bound by default; Studio's own Ctrl+Z history is cleared whenever objects are added.");
            PlayKey = Config.Bind("1. General", "Play selected route", KeyboardShortcut.Empty,
                "Play the selected route from its start (also the Play button in the window). Not bound by default.");
            ShowControllerButton = Config.Bind("1. General", "Button in Route Controller", true,
                "Adds a presets button next to play / replay / stop in Studio's Route Controller.");
            ShowToolbarButton = Config.Bind("1. General", "Toolbar button", true,
                "A Camera Routes button on Studio's left toolbar, next to the Studio Browser buttons. Takes effect after a restart.");
            WindowPosition = Config.Bind("1. General", "Window position", "",
                new ConfigDescription("Where the window was last closed.", null, new ConfigurationManagerAttributes { Browsable = false }));

            Size = Config.Bind("2. Move", "Size", 1f,
                new ConfigDescription("How far straight moves travel. 1 = each preset's default (e.g. Push In covers 40% of the distance to the subject).",
                    new AcceptableValueRange<float>(0.1f, 4f)));
            ArcAngle = Config.Bind("2. Move", "Arc angle", 45f,
                new ConfigDescription("Degrees for Arc Left / Arc Right.", new AcceptableValueRange<float>(5f, 300f)));
            DutchAngle = Config.Bind("2. Move", "Dutch angle", 12f,
                new ConfigDescription("Degrees Dutch Left / Dutch Right roll by the end of the move.", new AcceptableValueRange<float>(1f, 45f)));
            Handheld = Config.Bind("2. Move", "Handheld", 0f,
                new ConfigDescription("Handheld camera shake added to any move while it plays. 0 = off, 1 = subtle (about ±0.6°), 3 = shaky.", new AcceptableValueRange<float>(0f, 3f)));
            Duration = Config.Bind("2. Move", "Duration", 4f,
                new ConfigDescription("Seconds for the whole move (one lap for Orbit).", new AcceptableValueRange<float>(0.25f, 60f)));
            DurationModeSetting = Config.Bind("2. Move", "Time by", DurationMode.Seconds,
                "Seconds = Duration. AnimationLoops = Loops x one loop of the subject character's current animation (the tracked character, else the one nearest the line of sight), so the move lines up with the animation.");
            Loops = Config.Bind("2. Move", "Loops", 2f,
                new ConfigDescription("Animation loops for the whole move when Time by is AnimationLoops (one lap for Orbit).", new AcceptableValueRange<float>(0.5f, 16f)));
            Curve = Config.Bind("2. Move", "Easing curve", EaseCurve.Quad,
                "Shape of the speed change. Linear = constant speed. Sine is gentlest; Quad, Cubic, Quart, Quint, Expo get progressively sharper. Back overshoots, Elastic wobbles, Bounce bounces.");
            Mode = Config.Bind("2. Move", "Easing mode", EaseMode.InOut,
                "InOut = speed up then slow down. In = start slow. Out = end slow. On arcs the first segment eases in and the last eases out. Orbit 360 always runs at constant speed.");
            Loop = Config.Bind("2. Move", "Loop", false,
                "Straight moves go back and forth. Arcs play once (a looping arc would cut straight back across the scene). Orbit 360 always loops.");
            PlayAfterCreate = Config.Bind("2. Move", "Play after creating", true,
                "Start the route as soon as it's made.");

            Subject = Config.Bind("3. Subject", "Track", SubjectKind.Pivot,
                "What the camera keeps looking at while the route plays. Pivot = the fixed point it was looking at when the route was made (Studio's camera pivot): best for a subject that doesn't move. Character = a character's bone, followed as it moves. Off = straight moves keep their aim; arcs still face the pivot.");
            Bone = Config.Bind("3. Subject", "Character bone", SubjectBone.Head,
                "Which part of the character to follow when Track is Character.");

            RestartAnimations = Config.Bind("5. Recording", "Restart animations", true,
                "Restart every character's animation when a route starts recording, and when a route timed in animation loops starts playing, so loops line up with the camera.");
            RecordAfterCreate = Config.Bind("5. Recording", "Record after creating", false,
                "Record every new route with VideoExport as soon as it's made.");
            PreRoll = Config.Bind("5. Recording", "Lead-in", 0.5f,
                new ConfigDescription("Seconds recorded before the move starts.", new AcceptableValueRange<float>(0f, 5f)));
            PostRoll = Config.Bind("5. Recording", "Tail", 0.5f,
                new ConfigDescription("Seconds recorded after the move ends.", new AcceptableValueRange<float>(0f, 5f)));
            SeamlessLoops = Config.Bind("5. Recording", "Seamless loops", true,
                "Looping routes (Orbit, looping straight moves) record exactly one cycle with no lead-in or tail, so the video loops cleanly. Time it in animation loops for a perfect loop.");

            SizeModeSetting = Config.Bind("4. Advanced", "Size mode", SizeMode.ShareOfSubjectDistance,
                "ShareOfSubjectDistance = moves scale with how far the camera is from what it's looking at, so one preset works on a close-up and a wide shot. FixedUnits = Size x Fixed base distance, in Studio units.");
            FixedBase = Config.Bind("4. Advanced", "Fixed base distance", 10f,
                new ConfigDescription("Studio units for Size 1 in FixedUnits mode (a character is about 16 units tall).", new AcceptableValueRange<float>(0.5f, 200f)));
            FallbackDistance = Config.Bind("4. Advanced", "Fallback subject distance", 20f,
                new ConfigDescription("Used when the subject distance can't be worked out (Studio's camera pivot is behind the camera).", new AcceptableValueRange<float>(1f, 200f)));
            LookThroughNewCamera = Config.Bind("4. Advanced", "Look through new camera", true,
                "Start each session in Route cam (the viewport looks through the active route's camera, and follows it when you switch routes). Off = start in Free cam. The Route cam / Free cam switch in the window changes it any time.");
            ReplacePrevious = Config.Bind("4. Advanced", "Replace previous preset route", true,
                "Applying a preset to a camera that already rides a Camera Routes route deletes that route first, so you can try presets without piling up routes. Undo brings it back. Hand-made routes are never touched.");
            SelectionMode = Config.Bind("4. Advanced", "Selected character or item", HS2CameraRoutes.SelectionMode.LookAtIt,
                "LookAtIt = a selected character, item, light or folder is what the camera looks at; the camera the viewport looks through moves (or a new camera from the view). MoveIt = the selected object itself rides the route (e.g. a character walking a path).");
            TiltToFollow = Config.Bind("3. Subject", "Tilt to follow", true,
                "On = the camera pans and tilts to keep the subject in view. Off = it only pans (turns sideways) and keeps the tilt it starts with, like a camera on a level head.");
            KeepFraming = Config.Bind("3. Subject", "Keep framing", false,
                "Off = the camera turns to face the subject when the route is made and keeps it centred. On = the subject stays where it was in the frame (e.g. off to one side for the rule of thirds).");
            ShowLine = Config.Bind("4. Advanced", "Show motion line", true, "Draw the route's path in the viewport.");
            AutoUpdate = Config.Bind("2. Move", "Update the selected route live", true,
                "Selecting a camera on a Camera Routes route loads that route's settings into the window, and changing a setting rebuilds the route (once you let go of a slider). All the changes count as one Undo step until you click a move or pick another camera.");
            HideMarkers = Config.Bind("4. Advanced", "Hide markers while playing", true,
                "Hide a Camera Routes route's line and point markers while it plays (Play, Play after creating, Route Controller), and every route's while recording, so they don't show in the shot. They come back when it stops.");
            LineColor = Config.Bind("4. Advanced", "Line color", new Color(0.04f, 0.52f, 1f, 1f), "Color of the route's path line.");

            ViewThroughRoute = LookThroughNewCamera.Value;
            _window = new PresetWindow(this);
            _button = new ControllerButton(this);
            Recorder = new Recorder(this);

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(StudioEaseList));
            harmony.PatchAll(typeof(InitScenePatch));
            InputGuard.Install(harmony);
            SceneData.Init();
        }

        private void Start()
        {
            if (!ShowToolbarButton.Value) return;
            try { ToolbarButton.Add(this, PluginName, RoutesIcon.Create(), ToggleWindow); }
            catch (Exception e) { Logger.LogWarning("Couldn't add the toolbar button: " + e.Message); }
        }

        private void Update()
        {
            if (!RouteBuilder.StudioReady) return;

            if (ToggleKey.Value.IsDown()) ToggleWindow();
            if (UndoKey.Value.MainKey != KeyCode.None && UndoKey.Value.IsDown()) _window.SetStatus(Undo());
            if (PlayKey.Value.MainKey != KeyCode.None && PlayKey.Value.IsDown()) _window.SetStatus(PlaySelected());
            if (ShowControllerButton.Value) _button.TryInject();
            StartPendingPlay();
            Recorder.Update();
            SyncActiveFromSelection();
            FollowActiveView();
            AutoUpdateTick();
            _window.Tick();
        }

        private void LateUpdate()
        {
            if (!RouteBuilder.StudioReady) return;
            Tracker.LateUpdate();
        }

        private void OnGUI()
        {
            if (!RouteBuilder.StudioReady) return;
            _window.Draw();
        }

        internal void ToggleWindow()
        {
            _window.Visible = !_window.Visible;
        }

        // ---------------------------------------------------------------- subject

        /// <summary>The character a new route would track: the picked one, else the nearest to the line of sight.</summary>
        internal OCIChar SubjectCharacter(ViewFrame frame)
        {
            OCIChar picked = Subjects.ByKey(PickedCharKey);
            if (picked != null) return picked;
            PickedCharKey = -1;
            return Subjects.Nearest(frame.ViewPosition, frame.Forward, Bone.Value);
        }

        internal void PickNextCharacter()
        {
            ObjectCtrlInfo target = RouteBuilder.ResolveTarget();
            ViewFrame frame = RouteBuilder.MakeFrame(target, FallbackDistance.Value);
            OCIChar next = Subjects.Next(SubjectCharacter(frame));
            PickedCharKey = next != null ? next.objectInfo.dicKey : -1;
        }

        internal string SubjectCharacterName()
        {
            Selection sel = RouteBuilder.Resolve();
            if (sel.SubjectChar != null) return Subjects.Name(sel.SubjectChar) + " (selected)";
            ObjectCtrlInfo target = sel.Target;
            ViewFrame frame = RouteBuilder.MakeFrame(target, FallbackDistance.Value);
            OCIChar c = SubjectCharacter(frame);
            return c != null ? Subjects.Name(c) : "(no characters)";
        }

        /// <summary>
        /// Seconds the move should take. In AnimationLoops mode: Loops x the loop length of the subject character
        /// (falls back to Duration when there's no animated character). <paramref name="note"/> explains it.
        /// </summary>
        internal float EffectiveDuration(ViewFrame frame, OCIChar subjectChar, out float loopsUsed, out string note)
        {
            loopsUsed = 0f;
            note = "";
            if (DurationModeSetting.Value != DurationMode.AnimationLoops) return Duration.Value;

            OCIChar c = subjectChar ?? SubjectCharacter(frame);
            float loop = AnimLoops.LoopSeconds(c);
            if (loop <= 0f)
            {
                note = c == null ? "No character to time loops from; used " + Duration.Value.ToString("0.##") + " s."
                    : Subjects.Name(c) + " isn't animating; used " + Duration.Value.ToString("0.##") + " s.";
                return Duration.Value;
            }
            loopsUsed = Loops.Value;
            note = Subjects.Name(c) + ": " + loop.ToString("0.00") + " s per loop";
            return Mathf.Max(0.05f, Loops.Value * loop);
        }

        /// <summary>For the window: what Loops works out to right now.</summary>
        internal string LoopsPreview()
        {
            Selection sel = RouteBuilder.Resolve();
            ViewFrame frame = RouteBuilder.MakeFrame(sel.Target, FallbackDistance.Value);
            OCIChar c = sel.SubjectChar ?? (Subject.Value == SubjectKind.Character ? SubjectCharacter(frame) : null);
            float used; string note;
            float d = EffectiveDuration(frame, c, out used, out note);
            return used > 0f ? "= " + d.ToString("0.00") + " s  (" + note + ")" : note;
        }

        /// <summary>The route that is selected, or that the selected camera rides.</summary>
        internal OCIRoute SelectedRoute()
        {
            var sel = Studio.Studio.Instance.treeNodeCtrl.selectObjectCtrl;
            if (sel != null && sel.Length > 0)
            {
                ObjectCtrlInfo o = sel[0];
                var point = o as OCIRoutePoint;
                if (point != null) return point.route;
                OCIRoute r = o as OCIRoute ?? o.parentInfo as OCIRoute;
                if (r != null) return r; // any route, hand-made ones too
            }
            return ActiveRoute;
        }

        /// <summary>Plays the selected route from its start (restarting animations for routes timed in loops).</summary>
        internal string PlaySelected()
        {
            OCIRoute route = SelectedRoute();
            if (route == null) return "Select a camera on a route (or the route) to play it.";
            RouteBuilder.Park(route);
            Tracker.ApplyRotation(route, 0f);
            RouteRecord rec = RouteRegistry.Get(route);
            if (rec != null && rec.Loops > 0f && RestartAnimations.Value) AnimLoops.RestartAll();
            route.Play();
            Tracker.Rescan();
            RouteBuilder.RefreshRouteControl();
            return "Playing " + route.name.Replace(RouteBuilder.NamePrefix, "") + ".";
        }

        /// <summary>Rebuilds the selected route with the window's current settings (the manual form of live update).</summary>
        internal string RebuildSelected()
        {
            RouteRecord rec = CurrentRecord();
            if (rec == null || rec.Preset == null) return "Select a camera on a Camera Routes route to rebuild it.";
            return Apply(rec.Preset, true);
        }

        internal string StopSelected()
        {
            OCIRoute route = SelectedRoute();
            if (route == null) return "Select a camera on a route (or the route) to stop it.";
            RouteBuilder.Park(route);
            RouteBuilder.RefreshRouteControl();
            return "Stopped " + route.name.Replace(RouteBuilder.NamePrefix, "") + ".";
        }

        internal string RecordSelected()
        {
            return Recorder.Start(SelectedRoute());
        }

        /// <summary>The record of the route the current target rides, if any.</summary>
        internal RouteRecord CurrentRecord()
        {
            ObjectCtrlInfo target = RouteBuilder.ResolveTarget();
            var route = target != null ? target.parentInfo as OCIRoute : null;
            return RouteRegistry.Get(route);
        }

        // ---------------------------------------------------------------- actions

        /// <summary>Builds the preset on the selected camera (or a new one). Returns a status line for the window.</summary>
        // ---------------------------------------------------------------- active route

        /// <summary>The Camera Routes route the move buttons change. Follows the workspace selection and the routes list.</summary>
        internal int ActiveRouteKey = -1;
        private ObjectCtrlInfo _lastSelected;

        internal OCIRoute ActiveRoute
        {
            get
            {
                var r = ActiveRouteKey >= 0 ? Studio.Studio.GetCtrlInfo(ActiveRouteKey) as OCIRoute : null;
                if (RouteBuilder.IsOwn(r)) return r;
                // Gone (deleted, new scene): fall back to the newest Camera Routes route, if any.
                r = null;
                foreach (var oci in Studio.Studio.Instance.dicObjectCtrl.Values)
                {
                    var c = oci as OCIRoute;
                    if (RouteBuilder.IsOwn(c) && (r == null || c.objectInfo.dicKey > r.objectInfo.dicKey)) r = c;
                }
                ActiveRouteKey = r != null ? r.objectInfo.dicKey : -1;
                return r;
            }
        }

        internal ObjectCtrlInfo ActiveRider()
        {
            OCIRoute r = ActiveRoute;
            if (r == null || r.routeInfo.child.Count == 0) return null;
            return Studio.Studio.GetCtrlInfo(r.routeInfo.child[0].dicKey);
        }

        /// <summary>Every Camera Routes route in the scene, oldest first.</summary>
        internal System.Collections.Generic.List<OCIRoute> OwnRoutes()
        {
            var list = new System.Collections.Generic.List<OCIRoute>();
            foreach (var oci in Studio.Studio.Instance.dicObjectCtrl.Values)
            {
                var r = oci as OCIRoute;
                if (RouteBuilder.IsOwn(r)) list.Add(r);
            }
            list.Sort((a, b) => a.objectInfo.dicKey.CompareTo(b.objectInfo.dicKey));
            return list;
        }

        /// <summary>Picking a Camera Routes route, one of its points or its camera in the workspace makes it active.</summary>
        private void SyncActiveFromSelection()
        {
            var sel = Studio.Studio.Instance.treeNodeCtrl.selectObjectCtrl;
            ObjectCtrlInfo o = sel != null && sel.Length > 0 ? sel[0] : null;
            if (o == _lastSelected) return;
            _lastSelected = o;
            if (o == null) return;
            var point = o as OCIRoutePoint;
            OCIRoute r = point != null ? point.route : o as OCIRoute ?? o.parentInfo as OCIRoute;
            if (RouteBuilder.IsOwn(r)) ActiveRouteKey = r.objectInfo.dicKey;
        }

        /// <summary>Makes a route active (from the routes list) and selects it, so the window edits it.</summary>
        internal void SetActive(OCIRoute route)
        {
            if (route == null) return;
            ActiveRouteKey = route.objectInfo.dicKey;
            Studio.Studio.Instance.treeNodeCtrl.SelectSingle(route.treeNodeObject, true);
            _lastSelected = route;
            ResyncSelection();
        }

        /// <summary>Adds a new route: a new camera at the current view, with the given move.</summary>
        internal string AddRoute(Preset preset)
        {
            return Apply(preset, false, null, Quaternion.identity, true);
        }

        /// <summary>
        /// Route cam (true) or Free cam (false), as picked in the window. In Route cam the viewport follows the active
        /// route: whenever another route becomes active, it switches to that route's camera.
        /// </summary>
        internal bool ViewThroughRoute = true;
        private int _viewedActiveKey = -1;

        /// <summary>Puts the viewport where the view mode says: the active route's camera, or Studio's free camera.</summary>
        internal void ApplyView()
        {
            var s = Studio.Studio.Instance;
            var cam = ActiveRider() as OCICamera;
            if (ViewThroughRoute)
            {
                if (cam != null && s.ociCamera != cam) s.ChangeCamera(cam, true, true);
            }
            else if (s.ociCamera != null)
            {
                s.ChangeCamera(s.ociCamera, false, true);
            }
        }

        /// <summary>Each frame: when the active route changes (list, workspace, Add route), follow it in Route cam.</summary>
        private void FollowActiveView()
        {
            if (Reframing) return;
            OCIRoute active = ActiveRoute;
            int key = active != null ? active.objectInfo.dicKey : -1;
            if (key == _viewedActiveKey) return;
            _viewedActiveKey = key;
            if (key >= 0) ApplyView();
        }

        internal bool LookingThroughActive
        {
            get
            {
                var cam = ActiveRider() as OCICamera;
                return cam != null && Studio.Studio.Instance.ociCamera == cam;
            }
        }

        /// <summary>Switches the viewport between the active route's camera and Studio's free camera.</summary>
        internal string SetView(bool throughRoute)
        {
            ViewThroughRoute = throughRoute;
            var cam = ActiveRider() as OCICamera;
            if (throughRoute && cam == null) return "The active route has no camera to look through.";
            ApplyView();
            return throughRoute ? "Route cam: looking through " + cam.name + "." : "Free cam.";
        }

        private int _pendingPlayKey = -1;
        private bool _pendingRestart;

        /// <summary>Starts a route made last frame (Play after creating), from its start, aimed at its subject.</summary>
        private void StartPendingPlay()
        {
            if (_pendingPlayKey < 0) return;
            var route = Studio.Studio.GetCtrlInfo(_pendingPlayKey) as OCIRoute;
            _pendingPlayKey = -1;
            if (route == null || route.objectItem == null) return;
            RouteBuilder.Park(route);
            Tracker.ApplyRotation(route, 0f);
            if (_pendingRestart) AnimLoops.RestartAll();
            route.Play();
            Tracker.Rescan();
            RouteBuilder.RefreshRouteControl();
        }

        internal string Apply(Preset preset) { return Apply(preset, false, null, Quaternion.identity); }

        internal string Apply(Preset preset, bool adjust) { return Apply(preset, adjust, null, Quaternion.identity); }

        /// <param name="adjust">A live settings change: merges into the previous adjust step and never records.</param>
        /// <param name="startPos">Start the move from this pose instead of where the camera is (Reframe start).</param>
        internal string Apply(Preset preset, bool adjust, Vector3? startPos, Quaternion startRot) { return Apply(preset, adjust, startPos, startRot, false); }

        /// <param name="newRoute">Add a new route (new camera at the view) instead of changing the active one.</param>
        internal string Apply(Preset preset, bool adjust, Vector3? startPos, Quaternion startRot, bool newRoute)
        {
            try
            {
                var undo = new UndoEntry { Label = adjust ? "Adjust " + preset.Name : preset.Name, Adjust = adjust };
                int replacedKey = -1;
                int originalParent = -1;

                Selection sel = RouteBuilder.Resolve();
                if (newRoute) sel.Target = null;
                ObjectCtrlInfo target = sel.Target;
                RouteRecord oldRec = null;
                bool made = false;
                if (target == null)
                {
                    target = RouteBuilder.AddCameraAtView(false); // the view mode decides (FollowActiveView)
                    if (target == null) return "Couldn't add a camera.";
                    made = true;
                    undo.CreatedCameraKey = target.objectInfo.dicKey;
                }
                else
                {
                    OCIRoute old = ReplacePrevious.Value ? RouteBuilder.OwnRoute(target) : null;
                    if (old != null)
                    {
                        replacedKey = old.objectInfo.dicKey;
                        undo.Replaced = RouteSnapshot.Take(old);
                        oldRec = undo.Replaced.Record;
                        if (oldRec != null) originalParent = oldRec.ParentKey;
                        RouteBuilder.DetachAndDelete(target, old);
                        // Where the camera sat at the start of that route, for Undo.
                        Transform parked = target.guideObject.transformTarget;
                        undo.PrevPos = parked.position;
                        undo.PrevRot = parked.rotation;
                    }
                    else
                    {
                        originalParent = target.parentInfo != null ? target.parentInfo.objectInfo.dicKey : -1;
                        undo.PrevParentKey = originalParent;
                        Transform t = target.guideObject.transformTarget;
                        undo.PrevPos = t.position;
                        undo.PrevRot = t.rotation;
                    }
                }
                undo.TargetKey = target.objectInfo.dicKey;
                if (startPos.HasValue) RouteBuilder.SetWorldPoseAny(target, startPos.Value, startRot);

                ViewFrame frame = RouteBuilder.MakeFrame(target, FallbackDistance.Value);
                var rec = new RouteRecord
                {
                    PresetId = preset.Id,
                    Subject = Subject.Value,
                    Bone = Bone.Value,
                    Pivot = frame.Subject,
                    ArcSign = preset.ArcSign,
                    ParentKey = originalParent,
                    Size = Size.Value,
                    ArcAngle = ArcAngle.Value,
                    Curve = Curve.Value,
                    Mode = Mode.Value,
                    Loop = Loop.Value,
                };

                // What the camera looks at, first match wins:
                //   1. a character or object selected in the workspace;
                //   2. when replacing a route, the subject that route had (so trying presets keeps the subject);
                //   3. with Track = Character, the picked character, else the one nearest the line of sight;
                //   4. the pivot: what the view was looking at.
                // Arcs circle it, sizes scale from its distance, and the framing offset is measured to it.
                Vector3 subjectPoint = frame.Subject;
                bool explicitPoint = false;
                // The Track setting decides the kind of subject; a selection or the replaced route only says which one.
                OCIChar subjectChar = sel.SubjectChar;
                if (rec.Subject == SubjectKind.Character)
                {
                    if (subjectChar == null && oldRec != null && oldRec.Subject == SubjectKind.Character)
                        subjectChar = Subjects.ByKey(oldRec.CharKey) ?? Subjects.ByName(oldRec.CharName);
                    if (subjectChar == null && sel.SubjectObject == null) subjectChar = SubjectCharacter(frame);
                }
                if (subjectChar == null && sel.SubjectObject != null)
                {
                    subjectPoint = sel.SubjectObject.guideObject.transformTarget.position;
                    explicitPoint = true;
                }
                else if (subjectChar == null && oldRec != null)
                {
                    subjectPoint = oldRec.Pivot; // same fixed point as before (for a character subject: where its bone was)
                    explicitPoint = true;
                }

                if (subjectChar != null)
                {
                    Transform bone = Subjects.Bone(subjectChar, rec.Bone);
                    if (bone != null)
                    {
                        subjectPoint = bone.position;
                        explicitPoint = true;
                        rec.CharKey = subjectChar.objectInfo.dicKey;
                        rec.CharName = Subjects.Name(subjectChar);
                    }
                    else subjectChar = null;
                }
                if (rec.Subject == SubjectKind.Character && subjectChar == null) rec.Subject = SubjectKind.Pivot; // no character to follow

                Transform tt = target.guideObject.transformTarget;

                // Turn to face the subject now, so it's centred from the first frame and tracking keeps it there.
                // (With Keep framing on, only a new camera whose subject is well off-screen turns.)
                bool aims = rec.Subject != SubjectKind.Off || preset.AlwaysAims;
                Vector3 toSubject = subjectPoint - tt.position;
                bool canFace = toSubject.sqrMagnitude > 1e-4f && Mathf.Abs(Vector3.Dot(toSubject.normalized, Vector3.up)) < 0.99f;
                bool face = aims && !KeepFraming.Value
                    || made && sel.HasSubject && Vector3.Angle(tt.forward, toSubject) > 20f;
                if (canFace && face && Vector3.Angle(tt.forward, toSubject) > 0.5f)
                {
                    RouteBuilder.SetWorldRotation(target, Quaternion.LookRotation(toSubject.normalized, Vector3.up));
                    frame = RouteBuilder.MakeFrame(target, FallbackDistance.Value);
                    if (!explicitPoint) frame.Subject = subjectPoint;
                }

                if (explicitPoint)
                {
                    frame.Subject = subjectPoint;
                    frame.SubjectDistance = Mathf.Max(0.5f, Vector3.Distance(tt.position, subjectPoint));
                    rec.Pivot = subjectPoint;
                }
                rec.StartRot = tt.rotation;
                rec.Roll = preset.RollSign * DutchAngle.Value;
                rec.Shake = Handheld.Value;
                rec.PanOnly = !TiltToFollow.Value;
                // The framing offset is measured from "looking straight at the subject"; with pan only, from looking at
                // it level, so the starting tilt is kept for the whole move.
                Vector3 look = subjectPoint - tt.position;
                if (rec.PanOnly) look.y = 0f;
                if (look.sqrMagnitude > 1e-6f && Mathf.Abs(Vector3.Dot(look.normalized, Vector3.up)) < 0.999f)
                    rec.Offset = Quaternion.Inverse(Quaternion.LookRotation(look.normalized, Vector3.up)) * tt.rotation;

                float loopsUsed; string durNote;
                rec.Duration = EffectiveDuration(frame, subjectChar, out loopsUsed, out durNote);
                rec.Loops = loopsUsed;

                var opt = new Planner.Options
                {
                    SizeScale = Size.Value,
                    FixedDistance = SizeModeSetting.Value == SizeMode.FixedUnits,
                    FixedBase = FixedBase.Value,
                    ArcAngle = ArcAngle.Value,
                    Duration = rec.Duration,
                    Curve = Curve.Value,
                    Mode = Mode.Value,
                    Loop = Loop.Value,
                };
                RoutePlan plan = Planner.Build(preset, frame, opt);
                rec.Origin = plan.Origin;

                bool record = !adjust && RecordAfterCreate.Value && Recorder.Available && !Recorder.Busy;
                bool play = PlayAfterCreate.Value && !record;
                // With a subject selected, keep it selected so the next preset looks at it too. The route starts on the
                // next frame (see Update), once its record is in place and Studio has refreshed its panels.
                OCIRoute route = RouteBuilder.Build(plan, target, RouteBuilder.NamePrefix + preset.Name, ShowLine.Value, LineColor.Value, false, !sel.HasSubject);
                if (route == null) return "Couldn't create the route.";
                RouteRegistry.Set(route, rec);
                Tracker.Rescan();
                Tracker.ApplyRotation(route, 0f); // aimed from the very first frame
                if (play)
                {
                    _pendingPlayKey = route.objectInfo.dicKey;
                    _pendingRestart = loopsUsed > 0f && RestartAnimations.Value;
                }

                undo.RouteKey = route.objectInfo.dicKey;
                ActiveRouteKey = route.objectInfo.dicKey;
                UndoEntry top = UndoStack.Peek();
                if (adjust && top != null && top.Adjust && top.TargetKey == undo.TargetKey && top.RouteKey == replacedKey && replacedKey >= 0)
                    top.RouteKey = undo.RouteKey; // same editing pass: one Undo step back to before it started
                else
                    UndoStack.Push(undo);

                if (record) return Recorder.Start(route);
                string who = RouteBuilder.Describe(target);
                string msg = made ? preset.Name + " on new " + who : preset.Name + " on " + who;
                string looking = subjectChar != null ? Subjects.Name(subjectChar) : RouteBuilder.SubjectName(sel);
                msg += looking != null ? ", looking at " + looking + "." : ".";
                if (durNote.Length > 0 && loopsUsed <= 0f) msg += " " + durNote;
                return msg;
            }
            catch (Exception e)
            {
                Logger.LogError(e);
                return "Failed: " + e.Message;
            }
        }

        internal string Undo()
        {
            UndoEntry e = UndoStack.Pop();
            if (e == null) return "Nothing to undo.";
            ResyncSelection(); // the window shows the restored route's settings
            try
            {
                ObjectCtrlInfo target = e.TargetKey >= 0 ? Studio.Studio.GetCtrlInfo(e.TargetKey) : null;

                // 1. Remove the route the action made.
                var route = e.RouteKey >= 0 ? Studio.Studio.GetCtrlInfo(e.RouteKey) as OCIRoute : null;
                if (route != null) RouteBuilder.DetachAndDelete(target, route);

                // 2. Put back what was there before.
                if (e.CreatedCameraKey >= 0)
                {
                    RouteBuilder.DeleteObject(Studio.Studio.GetCtrlInfo(e.CreatedCameraKey));
                }
                else if (target != null)
                {
                    if (e.Replaced != null)
                    {
                        if (target.parentInfo != null) Studio.Studio.Instance.treeNodeCtrl.SetParent(target.treeNodeObject, null);
                        RouteBuilder.SetWorldPose(target, e.PrevPos, e.PrevRot); // where it sat at the start of that route
                        OCIRoute back = RouteBuilder.Build(e.Replaced.Plan, target, e.Replaced.Name, e.Replaced.ShowLine, e.Replaced.Color, false);
                        if (back != null && e.Replaced.Record != null) RouteRegistry.Set(back, e.Replaced.Record.Clone());
                    }
                    else
                    {
                        RouteBuilder.SetWorldPose(target, e.PrevPos, e.PrevRot);
                        RouteBuilder.Reparent(target, e.PrevParentKey);
                    }
                }
                Tracker.Rescan();
                return "Undid " + e.Label + ".";
            }
            catch (Exception ex)
            {
                Logger.LogError(ex);
                return "Undo failed: " + ex.Message;
            }
        }

        /// <summary>Takes the target off its Camera Routes route and deletes the route (undoable).</summary>
        internal string RemoveRoute() { return RemoveRoute(ActiveRoute); }

        /// <summary>Takes the camera off a Camera Routes route and deletes the route (undoable). The camera stays.</summary>
        internal string RemoveRoute(OCIRoute route)
        {
            try
            {
                if (!RouteBuilder.IsOwn(route) || route.routeInfo.child.Count == 0) return "Pick a Camera Routes route first.";
                ObjectCtrlInfo target = Studio.Studio.GetCtrlInfo(route.routeInfo.child[0].dicKey);
                if (target == null) return "That route has no camera on it.";

                var undo = new UndoEntry { Label = "Remove route", TargetKey = target.objectInfo.dicKey, Replaced = RouteSnapshot.Take(route) };
                int parent = undo.Replaced.Record != null ? undo.Replaced.Record.ParentKey : -1;
                RouteBuilder.DetachAndDelete(target, route);
                RouteBuilder.Reparent(target, parent);
                UndoStack.Push(undo);
                Tracker.Rescan();
                return "Removed the route from " + RouteBuilder.Describe(target) + ".";
            }
            catch (Exception e)
            {
                Logger.LogError(e);
                return "Failed: " + e.Message;
            }
        }

        /// <summary>Copies the selected route's settings into the window, to tweak and re-apply.</summary>
        internal string LoadSettings()
        {
            RouteRecord rec = CurrentRecord();
            if (rec == null) return "The selection isn't on a Camera Routes route.";
            LoadSettingsFrom(rec);
            var p = rec.Preset;
            return "Loaded settings from " + (p != null ? p.Name : "the route") + ".";
        }

        private void LoadSettingsFrom(RouteRecord rec)
        {
            Size.Value = rec.Size;
            ArcAngle.Value = rec.ArcAngle;
            Duration.Value = rec.Duration;
            DurationModeSetting.Value = rec.Loops > 0f ? DurationMode.AnimationLoops : DurationMode.Seconds;
            if (rec.Loops > 0f) Loops.Value = rec.Loops;
            Curve.Value = rec.Curve;
            Mode.Value = rec.Mode;
            Loop.Value = rec.Loop;
            Subject.Value = rec.Subject;
            Handheld.Value = rec.Shake;
            if (rec.Roll != 0f) DutchAngle.Value = Mathf.Abs(rec.Roll);
            Bone.Value = rec.Bone;
            PickedCharKey = rec.Subject == SubjectKind.Character ? rec.CharKey : -1;
            TiltToFollow.Value = !rec.PanOnly;
            _lastSig = Signature(); // loading isn't an edit
        }

        // ---------------------------------------------------------------- live update

        private string _lastSig;
        private int _lastTargetKey = int.MinValue;
        private bool _pendingRebuild;
        private float _changedAt;

        /// <summary>Forget the current selection so the next tick reloads the selected route's settings.</summary>
        internal void ResyncSelection() { _lastTargetKey = int.MinValue; }

        private string Signature()
        {
            return string.Join("|", new[]
            {
                Size.Value.ToString("R"), ArcAngle.Value.ToString("R"), DutchAngle.Value.ToString("R"), Handheld.Value.ToString("R"),
                Duration.Value.ToString("R"), DurationModeSetting.Value.ToString(), Loops.Value.ToString("R"),
                Curve.Value.ToString(), Mode.Value.ToString(), Loop.Value.ToString(), Subject.Value.ToString(), Bone.Value.ToString(),
                PickedCharKey.ToString(), KeepFraming.Value.ToString(), TiltToFollow.Value.ToString(),
            });
        }

        /// <summary>
        /// Keeps the window and the selected route in step: picking a camera on a Camera Routes route loads its
        /// settings; changing a setting rebuilds it (after a short pause, and not while a slider is held).
        /// </summary>
        private void AutoUpdateTick()
        {
            if (!AutoUpdate.Value || Recorder.Busy) { _pendingRebuild = false; return; }

            ObjectCtrlInfo target = RouteBuilder.ResolveTarget();
            int key = target != null && target.objectInfo != null ? target.objectInfo.dicKey : -1;
            RouteRecord rec = target != null ? RouteRegistry.Get(target.parentInfo as OCIRoute) : null;

            if (key != _lastTargetKey)
            {
                _lastTargetKey = key;
                _pendingRebuild = false;
                if (rec != null) LoadSettingsFrom(rec);
                _lastSig = Signature();
                return;
            }

            string sig = Signature();
            if (sig != _lastSig)
            {
                _lastSig = sig;
                if (rec != null)
                {
                    _pendingRebuild = true;
                    _changedAt = Time.unscaledTime;
                }
            }

            if (_pendingRebuild && Time.unscaledTime - _changedAt > 0.3f && !Input.GetMouseButton(0))
            {
                _pendingRebuild = false;
                if (rec != null && rec.Preset != null) _window.SetStatus(Apply(rec.Preset, true));
            }
        }

        // ---------------------------------------------------------------- reframe start

        private int _reframeKey = -1;      // camera being reframed

        internal bool Reframing { get { return _reframeKey >= 0 && Studio.Studio.GetCtrlInfo(_reframeKey) != null; } }

        /// <summary>
        /// Step 1: park the selected route's camera, step the viewport out of it into Studio's free camera at the
        /// same view, so the starting shot can be reframed with the normal camera controls.
        /// </summary>
        internal string ReframeStart()
        {
            OCIRoute route = SelectedRoute();
            RouteRecord rec = RouteRegistry.Get(route);
            if (route == null || rec == null || route.routeInfo.child.Count == 0) return "Select a camera on a Camera Routes route to reframe its start.";
            var cam = Studio.Studio.GetCtrlInfo(route.routeInfo.child[0].dicKey) as OCICamera;
            if (cam == null) return "Reframing works on camera routes.";

            RouteBuilder.Park(route);
            var s = Studio.Studio.Instance;
            Transform t = cam.objectItem.transform;
            if (s.ociCamera != null) s.ChangeCamera(s.ociCamera, false, true); // whichever camera it is, step out of it

            // Free camera at the camera's view: pivot out in front at the subject distance.
            float dist = Mathf.Clamp(Vector3.Distance(t.position, Tracker.SubjectPoint(rec)), 1f, 200f);
            s.cameraCtrl.SetCamera(t.position + t.forward * dist, t.rotation, dist, true, false);
            _reframeKey = cam.objectInfo.dicKey;
            return "Frame the new start with the viewport camera, then click Use this view.";
        }

        /// <summary>Step 2: rebuild the route starting from the viewport's view.</summary>
        internal string UseThisView()
        {
            var cam = _reframeKey >= 0 ? Studio.Studio.GetCtrlInfo(_reframeKey) as OCICamera : null;
            _reframeKey = -1;
            if (cam == null) return "The camera being reframed is gone.";
            var route = cam.parentInfo as OCIRoute;
            RouteRecord rec = RouteRegistry.Get(route);
            if (rec == null || rec.Preset == null) return "That camera isn't on a Camera Routes route any more.";

            Transform view = Camera.main.transform;
            Vector3 pos = view.position;
            Quaternion rot = view.rotation;
            Studio.Studio.Instance.treeNodeCtrl.SelectSingle(route.treeNodeObject, true);
            string msg = Apply(rec.Preset, false, pos, rot);
            ApplyView();
            ResyncSelection();
            return "Reframed: " + msg;
        }

        internal string CancelReframe()
        {
            _reframeKey = -1;
            ApplyView();
            return "Reframe cancelled.";
        }

        internal string SubjectTrackingInfo()
        {
            if (Subject.Value != SubjectKind.Character) return "";
            Selection sel = RouteBuilder.Resolve();
            OCIChar c = sel.SubjectChar;
            if (c == null)
            {
                RouteRecord rec = CurrentRecord();
                if (rec != null && rec.Subject == SubjectKind.Character) c = Subjects.ByKey(rec.CharKey) ?? Subjects.ByName(rec.CharName);
            }
            if (c == null) c = SubjectCharacter(RouteBuilder.MakeFrame(sel.Target, FallbackDistance.Value));
            if (c == null) return "";
            string bone = Subjects.Describe(c, Bone.Value);
            if (bone == null) return "";
            return bone.StartsWith("!") ? "!" + Subjects.Name(c) + " has no " + Bone.Value.ToString().ToLowerInvariant() + " bone; following its root (" + bone.Substring(1) + ")"
                : "Following " + Subjects.Name(c) + "'s " + bone;
        }

        // ---------------------------------------------------------------- scrub

        /// <summary>Puts the selected route's rider where it would be <paramref name="time"/> seconds into playback.</summary>
        internal void Scrub(float time)
        {
            OCIRoute route = SelectedRoute();
            if (route == null || route.childRoot == null) return;
            // Stop without Studio's hold: a stopped route snaps its rider back to the start every frame, which would undo
            // the scrub. Stop(false) also ends that hold. Play puts it back at the start as usual.
            route.Stop(false);
            RoutePath path = RoutePath.From(route);
            route.childRoot.position = path.Evaluate(time);
            Tracker.ApplyRotation(route, time);
        }
    }
}
