using System;
using System.Collections.Generic;
using Studio;
using UnityEngine;

namespace HS2CameraRoutes
{
    /// <summary>
    /// A route's path and timing, worked out the same way Studio's tween engine (StudioTween, a copy of iTween)
    /// plays it: each segment is a Catmull-Rom spline through its nodes (point, curve aid, next point), its
    /// length is measured with iTween's PathLength (20 samples per node), it takes length / (speed × 10)
    /// seconds, and its ease is applied to the spline parameter. Used to set speeds exactly, and to scrub a
    /// route to any moment.
    /// </summary>
    internal sealed class RoutePath
    {
        private sealed class Segment
        {
            public Vector3[] Control;
            public float Start, Time;
            public Func<float, float> Ease;
        }

        private readonly List<Segment> _segments = new List<Segment>();
        public float Total { get; private set; }

        /// <summary>Reads a live Studio route (points, curve aids, eases, speeds), including hand edits.</summary>
        public static RoutePath From(OCIRoute route)
        {
            var path = new RoutePath();
            var pts = route.listPoint;
            int n = pts.Count;
            if (n < 2) return path;
            int segs = route.routeInfo.loop && n > 2 ? n : n - 1; // a two-point loop ping-pongs: one way is the cycle
            float t = 0f;
            for (int i = 0; i < segs; i++)
            {
                OCIRoutePoint a = pts[i], b = pts[(i + 1) % n];
                Transform ta = a.objectItem.transform;
                var nodes = new List<Vector3> { ta.position };
                if (a.routePointInfo.connection == OIRoutePointInfo.Connection.Curve)
                    nodes.Add(ta.TransformPoint(a.routePointInfo.aidInfo.changeAmount.pos));
                nodes.Add(b.objectItem.transform.position);

                float length = Length(nodes.ToArray());
                float speed = Mathf.Max(0.0001f, a.routePointInfo.speed) * 10f;
                var seg = new Segment
                {
                    Control = ControlPoints(nodes.ToArray()),
                    Start = t,
                    Time = length / speed,
                    Ease = Easing.FromStudio(a.routePointInfo.easeType),
                };
                path._segments.Add(seg);
                t += seg.Time;
            }
            path.Total = t;
            return path;
        }

        /// <summary>Where the rider is <paramref name="time"/> seconds after the route starts (clamped to the end).</summary>
        public Vector3 Evaluate(float time)
        {
            if (_segments.Count == 0) return Vector3.zero;
            time = Mathf.Clamp(time, 0f, Total);
            Segment s = _segments[_segments.Count - 1];
            for (int i = 0; i < _segments.Count; i++)
            {
                if (time <= _segments[i].Start + _segments[i].Time) { s = _segments[i]; break; }
            }
            float local = s.Time > 1e-5f ? Mathf.Clamp01((time - s.Start) / s.Time) : 1f;
            return Interp(s.Control, s.Ease(local));
        }

        // ---------------------------------------------------------------- iTween's spline maths

        /// <summary>iTween.PathLength: Catmull-Rom path through the nodes, sampled 20 times per node.</summary>
        public static float Length(Vector3[] nodes)
        {
            Vector3[] pts = ControlPoints(nodes);
            Vector3 prev = Interp(pts, 0f);
            int smooth = nodes.Length * 20;
            float length = 0f;
            for (int i = 1; i <= smooth; i++)
            {
                Vector3 cur = Interp(pts, (float)i / smooth);
                length += Vector3.Distance(prev, cur);
                prev = cur;
            }
            return length;
        }

        /// <summary>iTween.PathControlPointGenerator: the nodes plus an extrapolated point at each end.</summary>
        private static Vector3[] ControlPoints(Vector3[] nodes)
        {
            int n = nodes.Length;
            var pts = new Vector3[n + 2];
            Array.Copy(nodes, 0, pts, 1, n);
            pts[0] = pts[1] + (pts[1] - pts[2]);
            pts[n + 1] = pts[n] + (pts[n] - pts[n - 1]);
            if (pts[1] == pts[n])
            {
                // Closed path: wrap the end points like iTween does.
                pts[0] = pts[n - 1];
                pts[n + 1] = pts[2];
            }
            return pts;
        }

        /// <summary>iTween.Interp: Catmull-Rom position at t (0..1) along the whole path.</summary>
        private static Vector3 Interp(Vector3[] pts, float t)
        {
            int sections = pts.Length - 3;
            int cur = Mathf.Min(Mathf.FloorToInt(t * sections), sections - 1);
            float u = t * sections - cur;
            Vector3 a = pts[cur], b = pts[cur + 1], c = pts[cur + 2], d = pts[cur + 3];
            return 0.5f * ((-a + 3f * b - 3f * c + d) * (u * u * u)
                         + (2f * a - 5f * b + 4f * c - d) * (u * u)
                         + (-a + c) * u
                         + 2f * b);
        }
    }
}
