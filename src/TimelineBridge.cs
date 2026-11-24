using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HS2CameraRoutes.UI;
using Studio;
using UnityEngine;

namespace HS2CameraRoutes
{
    /// <summary>
    /// Writes keyframes into Timeline (HSPlugins, by Joan6694) through reflection, so there's no build reference to
    /// it. Uses Timeline's own global "Camera Position" and "Camera Rotation" interpolables, which drive Studio's
    /// viewport camera, so the result is an ordinary Timeline track that saves with the scene and edits in Timeline.
    /// </summary>
    internal static class TimelineBridge
    {
        private const string Owner = "Timeline";
        private static bool _looked;
        private static Type _keyframeType;
        private static FieldInfo _self, _models, _duration, _durationInput, _keyframes;
        private static FieldInfo _modelOwner, _modelId;
        private static MethodInfo _addInterpolable, _removeInterpolable, _updateGrid, _getAll;
        private static PropertyInfo _playbackTime;

        private static void Find()
        {
            if (_looked) return;
            _looked = true;
            try
            {
                Type tl = null;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    tl = a.GetType("Timeline.Timeline", false);
                    if (tl != null) break;
                }
                if (tl == null) return;
                const BindingFlags any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
                Assembly asm = tl.Assembly;
                _keyframeType = asm.GetType("Timeline.Keyframe");
                Type model = asm.GetType("Timeline.InterpolableModel");
                Type interp = asm.GetType("Timeline.Interpolable");
                _self = tl.GetField("_self", any);
                _models = tl.GetField("_interpolableModelsList", any);
                _duration = tl.GetField("_duration", any);
                _durationInput = tl.GetField("_durationInputField", any);
                _addInterpolable = tl.GetMethod("AddInterpolable", any, null, new[] { model }, null);
                _removeInterpolable = tl.GetMethod("RemoveInterpolable", any, null, new[] { interp }, null);
                _updateGrid = tl.GetMethod("UpdateGrid", any, null, Type.EmptyTypes, null);
                _getAll = tl.GetMethod("GetAllInterpolables", any);
                _playbackTime = tl.GetProperty("playbackTime", any);
                _keyframes = interp != null ? interp.GetField("keyframes", any) : null;
                _modelOwner = model != null ? model.GetField("owner", any) : null;
                _modelId = model != null ? model.GetField("id", any) : null;

                if (_keyframeType == null || _self == null || _models == null || _addInterpolable == null || _keyframes == null
                    || _modelOwner == null || _modelId == null || _getAll == null)
                {
                    CameraRoutesPlugin.Logger.LogWarning("Timeline was found but its layout isn't the one Camera Routes knows; Send to Timeline is off.");
                    _self = null;
                }
            }
            catch (Exception e)
            {
                CameraRoutesPlugin.Logger.LogWarning("Couldn't hook into Timeline: " + e.Message);
                _self = null;
            }
        }

        private static object Self { get { return _self != null ? _self.GetValue(null) : null; } }

        /// <summary>Timeline is installed and has finished starting up.</summary>
        public static bool Available
        {
            get { Find(); return _self != null && Self != null; }
        }

        /// <summary>Timeline's cursor (where its playback head sits), within its duration.</summary>
        public static float Cursor
        {
            get
            {
                try
                {
                    float t = _playbackTime != null ? (float)_playbackTime.GetValue(null, null) : 0f;
                    float d = Duration;
                    return d > 0f ? Mathf.Repeat(t, d) : t;
                }
                catch { return 0f; }
            }
        }

        private static float Duration
        {
            get { return _duration != null ? (float)_duration.GetValue(Self) : 0f; }
        }

        private static void SetDuration(float d)
        {
            object self = Self;
            _duration.SetValue(self, d);
            if (_durationInput != null)
            {
                var field = _durationInput.GetValue(self) as UnityEngine.UI.InputField;
                if (field != null) field.text = Mathf.FloorToInt(d / 60f).ToString("00") + ":" + (d % 60f).ToString("00.00");
            }
        }

        private static object Model(string id)
        {
            var list = _models.GetValue(Self) as IList;
            if (list == null) return null;
            foreach (object m in list)
                if ((string)_modelOwner.GetValue(m) == Owner && (string)_modelId.GetValue(m) == id) return m;
            return null;
        }

        private static bool HasInterpolable(string id)
        {
            var all = _getAll.Invoke(null, new object[] { false }) as IEnumerable;
            if (all == null) return false;
            foreach (object i in all)
                if ((string)_modelOwner.GetValue(i) == Owner && (string)_modelId.GetValue(i) == id) return true;
            return false;
        }

        /// <summary>
        /// Writes one track of keys (times are relative to <paramref name="start"/>), replacing any keys already in
        /// that stretch, and returns how to take them back out again.
        /// </summary>
        private static Action WriteTrack(string id, float start, IList<float> times, IList<object> values)
        {
            object model = Model(id);
            if (model == null) throw new Exception("Timeline has no \"" + id + "\" track.");
            bool existed = HasInterpolable(id);
            object interpolable = _addInterpolable.Invoke(Self, new[] { model });
            if (interpolable == null) throw new Exception("Timeline wouldn't add its \"" + id + "\" track.");
            var keys = (IDictionary)_keyframes.GetValue(interpolable);

            // Clear the stretch the new keys cover (they'd collide or fight), keeping what was there for Undo.
            float end = start + times[times.Count - 1];
            var removed = new List<KeyValuePair<float, object>>();
            // (SortedList's plain enumerator hands out KeyValuePairs; the IDictionary one gives Key / Value.)
            IDictionaryEnumerator en = keys.GetEnumerator();
            while (en.MoveNext())
            {
                float t = (float)en.Key;
                if (t >= start - 1e-3f && t <= end + 1e-3f) removed.Add(new KeyValuePair<float, object>(t, en.Value));
            }
            foreach (var r in removed) keys.Remove(r.Key);

            var added = new List<KeyValuePair<float, object>>();
            for (int i = 0; i < times.Count; i++)
            {
                float t = start + times[i];
                if (keys.Contains(t)) continue;
                object kf = Activator.CreateInstance(_keyframeType, values[i], interpolable, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                keys.Add(t, kf);
                added.Add(new KeyValuePair<float, object>(t, kf));
            }

            return () =>
            {
                var k = (IDictionary)_keyframes.GetValue(interpolable);
                foreach (var a in added)
                    if (k.Contains(a.Key) && ReferenceEquals(k[a.Key], a.Value)) k.Remove(a.Key);
                foreach (var r in removed)
                    if (!k.Contains(r.Key)) k.Add(r.Key, r.Value);
                if (!existed && k.Count == 0 && _removeInterpolable != null)
                    _removeInterpolable.Invoke(Self, new[] { interpolable });
            };
        }

        /// <summary>
        /// Writes the viewport camera's path into Timeline at <paramref name="start"/>: one Camera Position and one
        /// Camera Rotation key per sample. Returns the Undo for it, or throws with a reason.
        /// </summary>
        public static Action Write(float start, IList<float> times, IList<Vector3> pos, IList<Quaternion> rot)
        {
            if (!Available) throw new Exception("Timeline isn't installed.");
            var posValues = new List<object>(pos.Count);
            foreach (Vector3 p in pos) posValues.Add(p);
            var rotValues = new List<object>(rot.Count);
            foreach (Quaternion q in rot) rotValues.Add(q);

            Action undoPos = WriteTrack("cameraPos", start, times, posValues);
            Action undoRot;
            try { undoRot = WriteTrack("cameraRot", start, times, rotValues); }
            catch { undoPos(); throw; }

            float oldDuration = Duration;
            float end = start + times[times.Count - 1];
            bool longer = end > oldDuration + 1e-4f;
            if (longer) SetDuration(end);
            Refresh();

            return () =>
            {
                if (!Available) return;
                undoRot();
                undoPos();
                if (longer && Mathf.Abs(Duration - end) < 1e-3f) SetDuration(oldDuration);
                Refresh();
            };
        }

        private static void Refresh()
        {
            try { if (_updateGrid != null) _updateGrid.Invoke(Self, null); }
            catch (Exception e) { CameraRoutesPlugin.Logger.LogDebug("Timeline UpdateGrid: " + e.Message); }
        }
    }

    /// <summary>
    /// Send to Timeline: samples where the route's camera is and where it looks over one run of the route (tracking,
    /// roll and handheld included) and writes it as Timeline keys at Timeline's cursor. Routes whose aim depends on
    /// something that moves (a character subject, Studio's own Correct Aim, routes with no record) are captured live
    /// as the route plays; everything else is worked out at once.
    /// </summary>
    internal sealed class TimelineBaker
    {
        private readonly CameraRoutesPlugin _p;
        private OCIRoute _route;
        private OCICamera _cam;
        private RoutePath _path;
        private float _cycle, _lap, _start, _t0;
        private bool _pingPong, _loops;
        private readonly List<float> _sampleT = new List<float>();
        private readonly List<Vector3> _sampleP = new List<Vector3>();
        private readonly List<Quaternion> _sampleR = new List<Quaternion>();

        public bool Busy { get; private set; }
        public string StatusText { get { return Busy ? "Capturing for Timeline… " + Mathf.Min(Time.time - _t0, _cycle).ToString("0.0") + " / " + _cycle.ToString("0.0") + " s" : ""; } }
        public bool Available { get { return TimelineBridge.Available; } }

        public TimelineBaker(CameraRoutesPlugin p) { _p = p; }

        public string Start(OCIRoute route)
        {
            if (!TimelineBridge.Available) return "Timeline isn't installed.";
            if (Busy) return "Already sending a route to Timeline.";
            if (_p.Recorder.Busy) return "Wait for the recording to finish.";
            if (route == null) return "Select a camera on a route (or the route) to send it to Timeline.";
            ObjectCtrlInfo rider = route.routeInfo.child.Count > 0 ? Studio.Studio.GetCtrlInfo(route.routeInfo.child[0].dicKey) : null;
            _cam = rider as OCICamera;
            if (_cam == null || _cam.objectItem == null) return "Send to Timeline needs a route that carries a camera.";

            _path = RoutePath.From(route);
            _lap = _path.Total;
            if (_lap <= 0f) return "This route has no length to send.";
            _loops = route.routeInfo.loop;
            _pingPong = _loops && route.listPoint.Count == 2;
            _cycle = _pingPong ? _lap * 2f : _lap;
            _route = route;
            _start = TimelineBridge.Cursor;

            RouteRecord rec = RouteRegistry.Get(route);
            bool live = rec == null || route.routeInfo.orient != OIRouteInfo.Orient.None || rec.Subject == SubjectKind.Character;
            _sampleT.Clear(); _sampleP.Clear(); _sampleR.Clear();

            if (!live)
            {
                // Everything the aim depends on stands still: work out each key straight away.
                try
                {
                    foreach (float t in KeyTimes()) { Pose(t); Sample(t); }
                }
                finally
                {
                    RouteBuilder.Park(route);
                    Tracker.ApplyRotation(route, 0f);
                }
                return Finish();
            }

            // Live: play the route and record the camera every frame, after tracking has aimed it.
            RouteBuilder.Park(route);
            Tracker.ApplyRotation(route, 0f);
            if (_p.RestartAnimations.Value) AnimLoops.RestartAll();
            Sample(0f);
            route.Play();
            Tracker.Rescan();
            _t0 = Time.time;
            Busy = true;
            return "Capturing " + Name(route) + " for Timeline (" + _cycle.ToString("0.0") + " s)…";
        }

        public void Cancel()
        {
            if (!Busy) return;
            Busy = false;
            if (_route != null && _route.objectItem != null) RouteBuilder.Park(_route);
            _route = null;
        }

        /// <summary>Called from LateUpdate, after the tracker has aimed the camera for this frame.</summary>
        public string LateUpdate()
        {
            if (!Busy) return null;
            try
            {
                if (_route == null || _route.objectItem == null || _cam == null || _cam.objectItem == null)
                {
                    Cancel();
                    return "The route or its camera was deleted; nothing was sent to Timeline.";
                }
                float elapsed = Time.time - _t0;
                Sample(elapsed);
                bool done = elapsed >= _cycle || elapsed > _cycle * 2f + 5f;
                if (!_loops && !_route.isPlay && elapsed > 0.05f) done = true;
                if (!done) return null;

                Busy = false;
                if (!_loops)
                {
                    // The route's last pose, exactly at its end.
                    Pose(_lap);
                    Sample(_lap);
                }
                // Resample the frames onto an even key grid.
                var t = new List<float>(); var p = new List<Vector3>(); var r = new List<Quaternion>();
                foreach (float k in KeyTimes())
                {
                    Vector3 pos; Quaternion rot;
                    At(k, out pos, out rot);
                    t.Add(k); p.Add(pos); r.Add(rot);
                }
                if (_loops && p.Count > 1) { p[p.Count - 1] = p[0]; r[r.Count - 1] = r[0]; } // seamless when Timeline loops
                _sampleT.Clear(); _sampleP.Clear(); _sampleR.Clear();
                _sampleT.AddRange(t); _sampleP.AddRange(p); _sampleR.AddRange(r);
                RouteBuilder.Park(_route);
                Tracker.ApplyRotation(_route, 0f);
                return Finish();
            }
            catch (Exception e)
            {
                CameraRoutesPlugin.Logger.LogError(e);
                Cancel();
                return Theme.Warn("Send to Timeline failed: " + e.Message);
            }
        }

        private string Finish()
        {
            Action undo;
            try { undo = TimelineBridge.Write(_start, _sampleT, _sampleP, _sampleR); }
            catch (Exception e)
            {
                CameraRoutesPlugin.Logger.LogError(e);
                return Theme.Warn("Send to Timeline failed: " + e.Message);
            }
            string name = Name(_route);
            UndoStack.Push(new UndoEntry { Label = "Send to Timeline (" + name + ")", Custom = undo });
            // Timeline drives Studio's own camera, so step out of the route camera for it to show.
            _p.SetView(false);
            float end = _start + _sampleT[_sampleT.Count - 1];
            return "Sent " + name + " to Timeline: " + _sampleT.Count + " keys, " + Clock(_start) + " to " + Clock(end) + ". Free cam is on so Timeline can drive the view.";
        }

        private IEnumerable<float> KeyTimes()
        {
            int n = Mathf.Max(1, Mathf.CeilToInt(_cycle * _p.TimelineKeyRate.Value));
            for (int i = 0; i <= n; i++) yield return _cycle * i / n;
        }

        /// <summary>Puts the route's camera where it is <paramref name="t"/> seconds into the route.</summary>
        private void Pose(float t)
        {
            float tt = _pingPong && t > _lap ? Mathf.Max(0f, 2f * _lap - t) : Mathf.Min(t, _lap);
            _route.Stop(false);
            _route.childRoot.position = _path.Evaluate(tt);
            Tracker.ApplyRotation(_route, t);
        }

        private void Sample(float t)
        {
            Transform ct = _cam.objectItem.transform;
            _sampleT.Add(t);
            _sampleP.Add(ct.position);
            _sampleR.Add(ct.rotation);
        }

        private void At(float t, out Vector3 pos, out Quaternion rot)
        {
            int n = _sampleT.Count;
            if (t <= _sampleT[0]) { pos = _sampleP[0]; rot = _sampleR[0]; return; }
            for (int i = 1; i < n; i++)
            {
                if (_sampleT[i] >= t)
                {
                    float span = _sampleT[i] - _sampleT[i - 1];
                    float f = span > 1e-6f ? (t - _sampleT[i - 1]) / span : 1f;
                    pos = Vector3.Lerp(_sampleP[i - 1], _sampleP[i], f);
                    rot = Quaternion.Slerp(_sampleR[i - 1], _sampleR[i], f);
                    return;
                }
            }
            pos = _sampleP[n - 1]; rot = _sampleR[n - 1];
        }

        private static string Name(OCIRoute r) { return r != null ? r.name.Replace(RouteBuilder.NamePrefix, "") : ""; }
        private static string Clock(float t) { return Mathf.FloorToInt(t / 60f) + ":" + (t % 60f).ToString("00.00"); }
    }
}
