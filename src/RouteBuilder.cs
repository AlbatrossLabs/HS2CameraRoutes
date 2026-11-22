using System.Linq;
using Studio;
using UnityEngine;

namespace HS2CameraRoutes
{
    public enum SelectionMode
    {
        LookAtIt, // a selected character or item is what the camera looks at
        MoveIt,   // a selected character or item rides the route itself
    }

    internal struct Selection
    {
        public ObjectCtrlInfo Target;      // what moves (null = new camera from the view)
        public OCIChar SubjectChar;        // selected character to look at
        public ObjectCtrlInfo SubjectObject; // selected item / light / folder to look at
        public bool HasSubject { get { return SubjectChar != null || SubjectObject != null; } }
    }

    /// <summary>Works out what a preset should move, and turns a <see cref="RoutePlan"/> into a real Studio route.</summary>
    internal static class RouteBuilder
    {
        public const string NamePrefix = "Cam Route: ";

        private static Studio.Studio S { get { return Studio.Studio.Instance; } }

        public static bool StudioReady
        {
            get { return Studio.Studio.IsInstance() && S.treeNodeCtrl != null && S.cameraCtrl != null; }
        }

        // ---------------------------------------------------------------- target

        /// <summary>
        /// The object a preset will move. Selected camera or item = that object. Selected route (or one of its
        /// points) = the first thing riding on it. Nothing usable = null, which means "new camera from the view".
        /// </summary>
        public static ObjectCtrlInfo ResolveTarget() { return Resolve().Target; }

        /// <summary>
        /// What a preset moves and what it looks at, from the workspace selection:
        /// - a camera: that camera moves;
        /// - a route or one of its points: the camera riding it moves;
        /// - a character, item, light or folder: it's the subject the camera looks at. The camera the viewport
        ///   looks through moves, or a new camera from the view if there is none. (F1 can switch this to moving
        ///   the selected object along the route instead.)
        /// - nothing: a new camera from the view.
        /// Target null = make a new camera.
        /// </summary>
        public static Selection Resolve()
        {
            var result = new Selection();
            if (!StudioReady) return result;
            var plugin = CameraRoutesPlugin.Instance;
            var selected = S.treeNodeCtrl.selectObjectCtrl;
            if (selected == null || selected.Length == 0)
            {
                result.Target = plugin != null ? plugin.ActiveRider() : null; // moves change the active route
                return result;
            }
            ObjectCtrlInfo sel = selected[0];

            var point = sel as OCIRoutePoint;
            if (point != null) sel = point.route;

            var route = sel as OCIRoute;
            if (route != null)
            {
                var child = route.routeInfo.child.FirstOrDefault();
                result.Target = child == null ? null : Studio.Studio.GetCtrlInfo(child.dicKey);
                return result;
            }

            if (sel is OCICamera) { result.Target = sel; return result; }

            if (plugin != null && plugin.SelectionMode.Value == SelectionMode.MoveIt)
            {
                result.Target = sel;
                return result;
            }

            result.SubjectChar = sel as OCIChar;
            if (result.SubjectChar == null) result.SubjectObject = sel;
            // The active route's camera moves; with no Camera Routes route yet, the camera being looked through.
            result.Target = (plugin != null ? plugin.ActiveRider() : null) ?? S.ociCamera;
            return result;
        }

        public static string SubjectName(Selection sel)
        {
            if (sel.SubjectChar != null) return Subjects.Name(sel.SubjectChar);
            if (sel.SubjectObject != null && sel.SubjectObject.treeNodeObject != null) return sel.SubjectObject.treeNodeObject.textName;
            return null;
        }

        public static bool IsOwn(OCIRoute route)
        {
            return route != null && route.name != null && route.name.StartsWith(NamePrefix);
        }

        public static string Describe(ObjectCtrlInfo target)
        {
            if (target == null) return "New camera from current view";
            var cam = target as OCICamera;
            if (cam != null) return cam.name;
            var node = target.treeNodeObject;
            return node != null ? node.textName : target.GetType().Name;
        }

        // ---------------------------------------------------------------- view frame

        public static ViewFrame MakeFrame(ObjectCtrlInfo target, float fallbackDistance)
        {
            Transform view = target is OCICamera ? ((OCICamera)target).objectItem.transform : Camera.main.transform;
            Vector3 origin = target != null ? target.guideObject.transformTarget.position : view.position;

            Vector3 fwd = view.forward;
            Vector3 flatFwd = Flat(fwd);
            if (flatFwd.sqrMagnitude < 1e-4f) flatFwd = Flat(view.up); // looking straight down or up
            if (flatFwd.sqrMagnitude < 1e-4f) flatFwd = Vector3.forward;
            flatFwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, flatFwd).normalized;

            // How far away the subject is: along the view to Studio's camera pivot, which is what was framed.
            Vector3 pivot = S.cameraCtrl.targetPos;
            float dist = Vector3.Dot(pivot - view.position, fwd);
            if (dist < 0.5f) dist = fallbackDistance;

            Vector3 subject = view.position + fwd * dist;
            if (!(target is OCICamera) && target != null)
            {
                // Moving an item or character: circle it around the pivot, and size moves from its distance.
                subject = pivot;
                dist = Mathf.Max(0.5f, Vector3.Distance(view.position, pivot));
            }

            return new ViewFrame
            {
                Origin = origin,
                ViewPosition = view.position,
                Up = view.up,
                Forward = fwd,
                FlatForward = flatFwd,
                Right = right,
                Subject = subject,
                SubjectDistance = dist,
            };
        }

        private static Vector3 Flat(Vector3 v) { return new Vector3(v.x, 0f, v.z); }

        // ---------------------------------------------------------------- camera

        /// <summary>Adds a Studio camera object at the current view, optionally looking through it.</summary>
        public static OCICamera AddCameraAtView(bool lookThrough)
        {
            Transform view = Camera.main.transform;
            Vector3 pos = view.position;
            Quaternion rot = view.rotation;

            S.AddCamera();
            OCICamera cam = S.dicObjectCtrl.Values.OfType<OCICamera>()
                .OrderByDescending(c => c.objectInfo.dicKey).FirstOrDefault();
            if (cam == null) return null;

            SetWorldPose(cam, pos, rot);
            if (lookThrough) S.ChangeCamera(cam, true, false);
            return cam;
        }

        /// <summary>Puts an object with no parent at a world pose.</summary>
        public static void SetWorldPose(ObjectCtrlInfo oci, Vector3 pos, Quaternion rot)
        {
            oci.objectInfo.changeAmount.pos = pos;
            oci.objectInfo.changeAmount.rot = rot.eulerAngles;
            oci.objectInfo.changeAmount.OnChange();
            oci.guideObject.transformTarget.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Puts an object at a world pose, whatever it hangs under.</summary>
        public static void SetWorldPoseAny(ObjectCtrlInfo oci, Vector3 pos, Quaternion rot)
        {
            Transform t = oci.guideObject.transformTarget;
            Transform parent = t.parent;
            oci.objectInfo.changeAmount.pos = parent != null ? parent.InverseTransformPoint(pos) : pos;
            oci.objectInfo.changeAmount.rot = (parent != null ? Quaternion.Inverse(parent.rotation) * rot : rot).eulerAngles;
            oci.objectInfo.changeAmount.OnChange();
            t.SetPositionAndRotation(pos, rot);
        }

        /// <summary>Turns an object to a world rotation, whatever it hangs under.</summary>
        public static void SetWorldRotation(ObjectCtrlInfo oci, Quaternion rot)
        {
            Transform t = oci.guideObject.transformTarget;
            Quaternion local = t.parent != null ? Quaternion.Inverse(t.parent.rotation) * rot : rot;
            oci.objectInfo.changeAmount.rot = local.eulerAngles;
            oci.objectInfo.changeAmount.OnChange();
            t.rotation = rot;
        }

        /// <summary>Stops a route and puts its rider back at the start.</summary>
        public static void Park(OCIRoute route)
        {
            route.Stop(true);
            if (route.listPoint.Count > 0)
            {
                Transform start = route.listPoint[0].objectItem.transform;
                route.childRoot.SetPositionAndRotation(start.position, start.rotation);
            }
            Tracker.SyncViewport(route.childRoot);
        }

        public static void RefreshRouteControl()
        {
            if (S.routeControl != null && S.routeControl.visible) S.routeControl.Init();
        }

        // ---------------------------------------------------------------- route

        /// <summary>The Camera Routes route the target rides, if it's the only thing riding it.</summary>
        public static OCIRoute OwnRoute(ObjectCtrlInfo target)
        {
            var route = target != null ? target.parentInfo as OCIRoute : null;
            if (route == null || route.name == null || !route.name.StartsWith(NamePrefix)) return null;
            return route.routeInfo.child.Count == 1 ? route : null;
        }

        /// <summary>Takes the target off its route (leaving it where the route starts) and deletes the route.</summary>
        public static void DetachAndDelete(ObjectCtrlInfo target, OCIRoute route)
        {
            route.Stop(true);
            if (route.listPoint.Count > 0)
            {
                Transform start = route.listPoint[0].objectItem.transform;
                route.childRoot.SetPositionAndRotation(start.position, start.rotation);
            }
            if (target != null && target.parentInfo == route) S.treeNodeCtrl.SetParent(target.treeNodeObject, null);
            RouteRegistry.Remove(route.objectInfo.dicKey);
            S.treeNodeCtrl.DeleteNode(route.treeNodeObject);
            RefreshPanels();
        }

        /// <summary>Hangs the target back under the object it was under before (if that still exists).</summary>
        public static void Reparent(ObjectCtrlInfo target, int parentKey)
        {
            if (parentKey < 0) return;
            ObjectCtrlInfo parent = Studio.Studio.GetCtrlInfo(parentKey);
            if (parent == null || parent == target) return;
            S.treeNodeCtrl.SetParent(target.treeNodeObject, parent.treeNodeObject);
        }

        public static void DeleteObject(ObjectCtrlInfo oci)
        {
            if (oci == null) return;
            var cam = oci as OCICamera;
            if (cam != null && S.ociCamera == cam) S.ChangeCamera(cam, false, true);
            S.treeNodeCtrl.DeleteNode(oci.treeNodeObject);
            RefreshPanels();
        }

        private static void RefreshPanels()
        {
            if (S.routeControl != null && S.routeControl.visible) S.routeControl.Init();
            Singleton<UndoRedoManager>.Instance.Clear();
        }

        public static OCIRoute Build(RoutePlan plan, ObjectCtrlInfo target, string name, bool showLine, Color lineColor, bool play, bool selectRoute = true)
        {
            OCIRoute route = AddObjectRoute.Add();
            if (route == null) return null;

            // Route origin and heading. Points are children of the route, so everything below is route-local.
            route.objectInfo.changeAmount.pos = plan.Origin;
            route.objectInfo.changeAmount.rot = plan.Rotation.eulerAngles;
            route.objectInfo.changeAmount.OnChange();
            route.objectItem.transform.SetPositionAndRotation(plan.Origin, plan.Rotation);

            route.name = name;
            OIRouteInfo info = route.routeInfo;
            info.loop = plan.Loop;
            info.orient = plan.Orient;
            info.color = lineColor;

            // A new route comes with its first point. Add the rest.
            while (route.listPoint.Count < plan.Points.Count)
            {
                if (AddObjectRoute.AddPoint(route) == null) break;
            }

            Quaternion toLocal = Quaternion.Inverse(plan.Rotation);
            for (int i = 0; i < route.listPoint.Count && i < plan.Points.Count; i++)
            {
                OCIRoutePoint p = route.listPoint[i];
                Waypoint wp = plan.Points[i];

                Vector3 local = toLocal * (wp.World - plan.Origin);
                p.objectInfo.changeAmount.pos = local;
                p.objectInfo.changeAmount.rot = Vector3.zero;
                p.objectInfo.changeAmount.OnChange();
                p.objectItem.transform.localPosition = local;
                p.objectItem.transform.localRotation = Quaternion.identity;

                p.speed = wp.Speed;
                p.easeType = wp.Ease;

                if (wp.AidWorld.HasValue)
                {
                    // Curve control point, relative to this point (points carry no rotation of their own).
                    OIRoutePointAidInfo aid = p.routePointInfo.aidInfo;
                    aid.changeAmount.pos = toLocal * (wp.AidWorld.Value - wp.World);
                    aid.isInit = true; // keep Studio from resetting it to the midpoint
                    aid.changeAmount.OnChange();
                    p.connection = OIRoutePointInfo.Connection.Curve;
                }
                else
                {
                    p.connection = OIRoutePointInfo.Connection.Line;
                }
            }

            route.SetLineColor(lineColor);
            route.visibleLine = showLine;
            route.ForceUpdateLine();
            route.Stop(true); // parked: whatever rides the route sits on the first point
            Transform first = route.listPoint[0].objectItem.transform;
            route.childRoot.SetPositionAndRotation(first.position, first.rotation);

            // Hang the target on the route. Studio keeps its world pose, so the camera's aim becomes an offset
            // from the route's heading.
            S.treeNodeCtrl.SetParent(target.treeNodeObject, route.treeNodeObject);

            if (play) route.Play();

            RefreshPanels();
            if (selectRoute) S.treeNodeCtrl.SelectSingle(route.treeNodeObject, true);
            return route;
        }
    }
}
