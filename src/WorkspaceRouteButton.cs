using HarmonyLib;
using UnityEngine;

namespace HS2CameraRoutes
{
    /// <summary>What the Route button in Studio's workspace does.</summary>
    public enum RouteButtonMode
    {
        OpenCameraRoutes,
        AddStudioRoute,
    }

    /// <summary>
    /// Studio's workspace Route button (normally: add an empty route) opens the Camera Routes window instead.
    /// Shift+click still adds a plain Studio route, and F1 &gt; Route button in workspace switches it back for good.
    /// </summary>
    [HarmonyPatch(typeof(Studio.WorkspaceCtrl), "OnClickRoute")]
    internal static class WorkspaceRouteButton
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            var p = CameraRoutesPlugin.Instance;
            if (p == null || p.RouteButton.Value != RouteButtonMode.OpenCameraRoutes) return true;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) return true; // Studio's own route
            p.OpenWindow("Shift+click the workspace Route button for an empty Studio route.");
            return false;
        }
    }
}
