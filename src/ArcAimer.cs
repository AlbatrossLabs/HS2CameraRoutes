using Studio;
using UnityEngine;

namespace HS2CameraRoutes
{
    /// <summary>
    /// Fallback for Arc and Orbit routes without a saved record: keeps the camera facing the circle's centre. While such a route plays, the route's
    /// mover is turned every frame to face along the circle (at a right angle to its centre); the camera rides
    /// it with a fixed offset, so it keeps looking at the centre. The centre comes from the route's own points,
    /// so nothing extra is saved and edited routes still work. Picking a Correct Aim other than None in Route
    /// Settings hands aiming back to Studio.
    /// </summary>
    internal static class ArcAimer
    {
        /// <summary>An Arc/Orbit route with no saved record (made by 0.2, or its scene data is missing).</summary>
        public static bool IsLegacyArc(OCIRoute route)
        {
            string n = route.name;
            return n != null
                && (n == RouteBuilder.NamePrefix + "Arc Left" || n == RouteBuilder.NamePrefix + "Arc Right" || n == RouteBuilder.NamePrefix + "Orbit 360")
                && route.routeInfo.orient == OIRouteInfo.Orient.None;
        }

        public static void Aim(OCIRoute r)
        {
            var pts = r.listPoint;
            if (pts.Count < 2) return;

            Transform p0 = pts[0].objectItem.transform;
            Vector3 a = p0.position;
            Vector3 b = pts[1].objectItem.transform.position;
            Vector3 mid = p0.TransformPoint(pts[0].routePointInfo.aidInfo.changeAmount.pos);

            Vector3 center;
            if (!Circumcenter(a, mid, b, out center)) return;

            // Which way round: turning by +angle about Y moves an offset (x, z) toward (z, -x).
            Vector3 o0 = a - center, o1 = b - center;
            float turn = o0.z * o1.x - o0.x * o1.z;
            if (Mathf.Abs(turn) < 1e-6f) return;
            float sign = Mathf.Sign(turn);

            Vector3 off = r.childRoot.position - center;
            Vector3 tangent = sign * new Vector3(off.z, 0f, -off.x);
            if (tangent.sqrMagnitude < 1e-8f) return;
            r.childRoot.rotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);

            Tracker.SyncViewport(r.childRoot);
        }

        /// <summary>Centre of the circle through three points, on the ground plane.</summary>
        private static bool Circumcenter(Vector3 a, Vector3 b, Vector3 c, out Vector3 center)
        {
            center = Vector3.zero;
            float ax = a.x, az = a.z, bx = b.x, bz = b.z, cx = c.x, cz = c.z;
            float d = 2f * (ax * (bz - cz) + bx * (cz - az) + cx * (az - bz));
            if (Mathf.Abs(d) < 1e-6f) return false;
            float a2 = ax * ax + az * az, b2 = bx * bx + bz * bz, c2 = cx * cx + cz * cz;
            float ux = (a2 * (bz - cz) + b2 * (cz - az) + c2 * (az - bz)) / d;
            float uz = (a2 * (cx - bx) + b2 * (ax - cx) + c2 * (bx - ax)) / d;
            center = new Vector3(ux, a.y, uz);
            return true;
        }
    }
}
