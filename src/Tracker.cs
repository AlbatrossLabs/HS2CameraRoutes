using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Studio;
using UnityEngine;

namespace HS2CameraRoutes
{
    /// <summary>
    /// Turns whatever rides a Camera Routes route while the route plays:
    /// - aims it at its subject (a fixed pivot, or a character's bone followed as it moves), keeping the framing
    ///   offset recorded when the route was made. Curved moves always aim (at the pivot when Track is Off);
    /// - rolls it toward the Dutch angle as it travels;
    /// - adds handheld shake.
    /// Routes without a record (made by 0.2 or with the data missing) fall back to ArcAimer.
    /// </summary>
    internal static class Tracker
    {
        private static readonly List<OCIRoute> Routes = new List<OCIRoute>();    // Camera Routes routes
        private static readonly List<OCIRoute> AllRoutes = new List<OCIRoute>(); // every route in the scene
        private static readonly HashSet<OCIRoute> Hidden = new HashSet<OCIRoute>();
        private static readonly MethodInfo SetVisibleMethod = AccessTools.Method(typeof(OCIRoute), "SetVisible", new[] { typeof(bool) });

        /// <summary>Studio's own (non-public) route visibility: line and point markers together, not saved.</summary>
        private static void SetRouteVisible(OCIRoute r, bool visible)
        {
            if (SetVisibleMethod != null) SetVisibleMethod.Invoke(r, new object[] { visible });
        }
        private static readonly Dictionary<int, float> PlayStart = new Dictionary<int, float>();
        private static float _nextScan;

        public static void Rescan() { _nextScan = 0f; }

        public static void LateUpdate()
        {
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.5f;
                Routes.Clear();
                AllRoutes.Clear();
                foreach (var oci in Studio.Studio.Instance.dicObjectCtrl.Values)
                {
                    var r = oci as OCIRoute;
                    if (r == null) continue;
                    AllRoutes.Add(r);
                    if (r.name != null && r.name.StartsWith(RouteBuilder.NamePrefix)) Routes.Add(r);
                }
                Hidden.RemoveWhere(r => r == null || r.objectItem == null);
            }

            UpdateMarkers();

            for (int i = 0; i < Routes.Count; i++)
            {
                OCIRoute r = Routes[i];
                if (r == null || r.objectItem == null || r.childRoot == null) continue;
                int key = r.objectInfo.dicKey;
                if (!r.isPlay)
                {
                    PlayStart.Remove(key);
                    continue;
                }
                if (!PlayStart.ContainsKey(key)) PlayStart[key] = Time.time;
                if (r.routeInfo.orient != OIRouteInfo.Orient.None) continue; // Correct Aim picked: Studio aims

                RouteRecord rec = RouteRegistry.Get(r);
                if (rec != null) Track(r, rec, Time.time - PlayStart[key]);
                else if (ArcAimer.IsLegacyArc(r)) ArcAimer.Aim(r);
            }
        }

        /// <summary>
        /// Hides route lines and point markers while a Camera Routes route plays, and every route's while recording,
        /// so they don't show in the shot. Uses Studio's own route visibility, which isn't saved, and puts each back
        /// to its Motion Line setting afterwards.
        /// </summary>
        private static void UpdateMarkers()
        {
            var p = CameraRoutesPlugin.Instance;
            bool enabled = p != null && p.HideMarkers.Value;
            bool recording = p != null && p.Recorder != null && p.Recorder.Busy;

            for (int i = 0; i < AllRoutes.Count; i++)
            {
                OCIRoute r = AllRoutes[i];
                if (r == null || r.objectItem == null) continue;
                bool ours = r.name != null && r.name.StartsWith(RouteBuilder.NamePrefix);
                bool hide = enabled && (recording || ours && r.isPlay);
                bool hidden = Hidden.Contains(r);
                if (hide && !hidden)
                {
                    SetRouteVisible(r, false);
                    Hidden.Add(r);
                }
                else if (!hide && hidden)
                {
                    SetRouteVisible(r, r.routeInfo.visibleLine);
                    Hidden.Remove(r);
                }
            }
        }

        private static void Track(OCIRoute r, RouteRecord rec, float elapsed)
        {
            bool aims = rec.Subject != SubjectKind.Off || rec.AlwaysAims;
            if (!aims && rec.Roll == 0f && rec.Shake <= 0f) return;
            if (r.routeInfo.child.Count == 0) return;

            ObjectCtrlInfo rider = Studio.Studio.GetCtrlInfo(r.routeInfo.child[0].dicKey);
            if (rider == null || rider.guideObject == null) return;
            Transform rt = rider.guideObject.transformTarget;
            if (rt == null || rt.parent != r.childRoot) return;

            Quaternion want = rec.StartRot;
            if (aims)
            {
                Vector3 dir = SubjectPoint(rec) - rt.position;
                if (rec.PanOnly) dir.y = 0f; // turn sideways only; the tilt comes from the offset (the starting tilt)
                if (dir.sqrMagnitude < 1e-6f) return;
                dir.Normalize();
                if (Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.999f) return; // straight up/down: no stable heading
                want = Quaternion.LookRotation(dir, Vector3.up) * rec.Offset;
            }
            if (rec.Roll != 0f) want = want * Quaternion.Euler(0f, 0f, rec.Roll * Progress(r));
            if (rec.Shake > 0f) want = want * Handheld(rec.Shake, elapsed, rec.RouteKey);

            r.childRoot.rotation = want * Quaternion.Inverse(rt.localRotation);
            SyncViewport(r.childRoot);
        }

        /// <summary>How far along a straight route the mover is, 0..1 (follows the easing; back and forth on loops).</summary>
        private static float Progress(OCIRoute r)
        {
            var pts = r.listPoint;
            if (pts.Count < 2) return 1f;
            Vector3 a = pts[0].objectItem.transform.position;
            Vector3 b = pts[pts.Count - 1].objectItem.transform.position;
            Vector3 ab = b - a;
            if (ab.sqrMagnitude < 1e-4f) return 1f; // Hold: roll is applied in full
            return Mathf.Clamp01(Vector3.Dot(r.childRoot.position - a, ab) / ab.sqrMagnitude);
        }

        /// <summary>
        /// Handheld shake: slow drift plus a little faster jitter, from smooth noise. Amount 1 is about ±0.6°.
        /// Driven by time since the route started, so a recording shakes the same way every take.
        /// </summary>
        private static Quaternion Handheld(float amount, float t, int seed)
        {
            float s = (seed % 97) * 3.17f;
            float yaw = Noise(t * 0.35f, s) * 0.45f + Noise(t * 1.7f, s + 11f) * 0.15f;
            float pitch = Noise(t * 0.30f, s + 23f) * 0.40f + Noise(t * 1.9f, s + 37f) * 0.12f;
            float roll = Noise(t * 0.25f, s + 51f) * 0.25f;
            return Quaternion.Euler(pitch * amount, yaw * amount, roll * amount);
        }

        private static float Noise(float x, float y) { return (Mathf.PerlinNoise(x, y) - 0.5f) * 2.6f; }

        /// <summary>Seconds since the route started playing, or -1 if it isn't playing.</summary>
        public static float Elapsed(OCIRoute r)
        {
            float start;
            if (r == null || !r.isPlay || !PlayStart.TryGetValue(r.objectInfo.dicKey, out start)) return -1f;
            return Time.time - start;
        }

        /// <summary>Turns the rider as it would be <paramref name="elapsed"/> seconds into playback (for scrubbing).</summary>
        public static void ApplyRotation(OCIRoute r, float elapsed)
        {
            if (r.listPoint.Count > 0) r.childRoot.rotation = r.listPoint[0].objectItem.transform.rotation; // parked heading
            if (r.routeInfo.orient == OIRouteInfo.Orient.None)
            {
                RouteRecord rec = RouteRegistry.Get(r);
                if (rec != null) Track(r, rec, elapsed);
                else if (ArcAimer.IsLegacyArc(r)) ArcAimer.Aim(r);
            }
            SyncViewport(r.childRoot);
        }

        public static Vector3 SubjectPoint(RouteRecord rec)
        {
            if (rec.Subject == SubjectKind.Character)
            {
                OCIChar c = Subjects.ByKey(rec.CharKey);
                if (c == null)
                {
                    c = Subjects.ByName(rec.CharName);
                    if (c != null) rec.CharKey = c.objectInfo.dicKey;
                }
                Transform bone = Subjects.Bone(c, rec.Bone);
                if (bone != null) return bone.position;
            }
            return rec.Pivot;
        }

        /// <summary>If the viewport looks through a camera riding this route, update it now so it doesn't lag a frame.</summary>
        public static void SyncViewport(Transform childRoot)
        {
            var s = Studio.Studio.Instance;
            OCICamera cam = s.ociCamera;
            if (cam != null && cam.objectItem != null && cam.objectItem.transform.IsChildOf(childRoot))
            {
                Transform t = cam.objectItem.transform;
                s.cameraCtrl.SetPositionAndRotation(t.position, t.rotation);
            }
        }
    }
}
