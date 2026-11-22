using System;
using System.Reflection;
using BepInEx.Bootstrap;
using Studio;
using UnityEngine;

namespace HS2CameraRoutes
{
    /// <summary>
    /// Records a route with VideoExport (HSPlugins). Starts VideoExport, waits until it is really capturing,
    /// parks the route at its start, then plays it (restarting character animations so loops line up) and
    /// stops after the move plus a short tail. Looping routes record exactly one cycle so the video loops.
    /// VideoExport fixes the game to its capture frame rate while recording, so route timing is frame-exact.
    /// VideoExport is reached by reflection; without it the Record button is disabled.
    /// </summary>
    internal sealed class Recorder
    {
        private const string VideoExportGuid = "com.joan6694.illusionplugins.videoexport";

        private enum State { Idle, Starting, PreRoll, Playing, PostRoll, Finishing }

        private readonly CameraRoutesPlugin _p;
        private object _ve;
        private MethodInfo _record, _stop;
        private PropertyInfo _isRecording, _frame;
        private FieldInfo _limit;
        private bool _looked;

        private State _state = State.Idle;
        private OCIRoute _route;
        private float _cycle, _pre, _post, _t0, _timeout;
        private bool _waitForEnd, _restart, _prevLimit, _limitChanged;

        public Recorder(CameraRoutesPlugin plugin) { _p = plugin; }

        public bool Busy { get { return _state != State.Idle; } }

        public bool Available
        {
            get { Find(); return _ve != null; }
        }

        public string StatusText
        {
            get
            {
                switch (_state)
                {
                    case State.Starting: return "Recording: starting VideoExport…";
                    case State.PreRoll: return "Recording: lead-in…";
                    case State.Playing: return "Recording: " + (Time.time - _t0).ToString("0.0") + (_waitForEnd ? " s (until the route ends)" : " / " + _cycle.ToString("0.0") + " s");
                    case State.PostRoll: return "Recording: tail…";
                    case State.Finishing: return "Recording: VideoExport is encoding…";
                    default: return "";
                }
            }
        }

        private void Find()
        {
            if (_looked && _ve != null) return;
            _looked = true;
            BepInEx.PluginInfo info;
            if (!Chainloader.PluginInfos.TryGetValue(VideoExportGuid, out info) || info.Instance == null) return;
            Type t = info.Instance.GetType();
            _record = t.GetMethod("RecordVideo", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            _stop = t.GetMethod("StopRecording", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
            _isRecording = t.GetProperty("isRecording", BindingFlags.Instance | BindingFlags.Public);
            _frame = t.GetProperty("currentRecordingFrame", BindingFlags.Instance | BindingFlags.Public);
            _limit = t.GetField("_limitDuration", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (_record != null && _stop != null && _isRecording != null) _ve = info.Instance;
            else CameraRoutesPlugin.Logger.LogWarning("VideoExport found, but not the members Camera Routes uses; recording is off.");
        }

        private bool IsRecording { get { return (bool)_isRecording.GetValue(_ve, null); } }
        private int Frame { get { return _frame != null ? Convert.ToInt32(_frame.GetValue(_ve, null)) : 1; } }

        /// <summary>Starts recording the route. Returns a status line.</summary>
        public string Start(OCIRoute route)
        {
            Find();
            if (_ve == null) return "VideoExport isn't installed.";
            if (Busy) return "Already recording.";
            if (route == null) return "Select a camera on a route (or the route) to record.";
            if (IsRecording) return "VideoExport is already recording.";

            RouteRecord rec = RouteRegistry.Get(route);
            bool loops = route.routeInfo.loop;
            float duration = rec != null ? rec.Duration : 0f;

            if (loops)
            {
                // One cycle, timed from the route itself as Studio will play it: Orbit/Sway = one lap; a looping
                // two-point move ping-pongs, so there and back.
                float lap = RoutePath.From(route).Total;
                if (lap <= 0f) lap = duration;
                if (lap <= 0f) return "Couldn't work out this route's cycle length.";
                _cycle = route.listPoint.Count == 2 ? lap * 2f : lap;
                _waitForEnd = false;
            }
            else
            {
                // One-shot routes record until the route has really finished (hand edits can change its length).
                // The expected length only caps it, generously, in case the route never reports its end.
                float expected = RoutePath.From(route).Total;
                if (expected <= 0f) expected = duration;
                _cycle = expected > 0f ? expected * 2f + 5f : 300f;
                _waitForEnd = true;
            }

            bool seamless = loops && _p.SeamlessLoops.Value;
            _pre = seamless ? 0f : _p.PreRoll.Value;
            _post = seamless ? 0f : _p.PostRoll.Value;
            _restart = _p.RestartAnimations.Value;
            _route = route;

            route.Stop(true); // parked at the start
            if (_limit != null)
            {
                // VideoExport's own length limit would cut the recording short (or let it run on).
                _prevLimit = (bool)_limit.GetValue(_ve);
                _limit.SetValue(_ve, false);
                _limitChanged = true;
            }

            try { _record.Invoke(_ve, null); }
            catch (Exception e)
            {
                Restore();
                CameraRoutesPlugin.Logger.LogError(e);
                return "VideoExport didn't start: " + e.Message;
            }

            _state = State.Starting;
            _timeout = Time.realtimeSinceStartup + 15f;
            return "Recording " + route.name.Replace(RouteBuilder.NamePrefix, "") + "…";
        }

        public void Cancel()
        {
            if (!Busy) return;
            if (_ve != null && IsRecording) _stop.Invoke(_ve, null);
            _state = _ve != null && IsRecording ? State.Finishing : State.Idle;
            if (_state == State.Idle) Restore();
        }

        public void Update()
        {
            if (_state == State.Idle) return;
            try { Step(); }
            catch (Exception e)
            {
                CameraRoutesPlugin.Logger.LogError(e);
                Cancel();
            }
        }

        private void Step()
        {
            switch (_state)
            {
                case State.Starting:
                    if (IsRecording && Frame >= 1)
                    {
                        _state = State.PreRoll;
                        _t0 = Time.time;
                    }
                    else if (Time.realtimeSinceStartup > _timeout)
                    {
                        CameraRoutesPlugin.Logger.LogWarning("VideoExport never started capturing; recording cancelled.");
                        Cancel();
                    }
                    break;

                case State.PreRoll:
                    if (Time.time - _t0 >= _pre - 1e-4f)
                    {
                        if (_route == null || _route.objectItem == null) { Cancel(); return; }
                        if (_restart) AnimLoops.RestartAll();
                        _route.Play();
                        _t0 = Time.time;
                        _state = State.Playing;
                    }
                    break;

                case State.Playing:
                    {
                        // Time.deltaTime is one capture frame while recording. Stop half a frame early so a
                        // looping recording doesn't repeat its first frame at the end.
                        float elapsed = Time.time - _t0 + Time.deltaTime * 0.5f;
                        bool done = elapsed >= _cycle;
                        if (_waitForEnd && _route != null && !_route.isPlay && Time.time - _t0 > 0.05f) done = true;
                        if (_route == null || _route.objectItem == null) done = true;
                        if (done)
                        {
                            _state = State.PostRoll;
                            _t0 = Time.time;
                        }
                    }
                    break;

                case State.PostRoll:
                    if (Time.time - _t0 + Time.deltaTime * 0.5f >= _post)
                    {
                        _stop.Invoke(_ve, null);
                        _state = State.Finishing;
                    }
                    break;

                case State.Finishing:
                    if (!IsRecording)
                    {
                        Restore();
                        _state = State.Idle;
                        _route = null;
                    }
                    break;
            }

            // Recording stopped from VideoExport's own window or hotkey.
            if (_state != State.Starting && _state != State.Finishing && _state != State.Idle && !IsRecording)
            {
                Restore();
                _state = State.Idle;
            }
        }

        private void Restore()
        {
            if (_limitChanged && _limit != null && _ve != null) _limit.SetValue(_ve, _prevLimit);
            _limitChanged = false;
        }
    }
}
