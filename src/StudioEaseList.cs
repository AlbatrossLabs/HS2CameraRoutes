using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Studio;
using UnityEngine.UI;

namespace HS2CameraRoutes
{
    /// <summary>
    /// Studio's Route Point Settings "Behavior" dropdown lists 6 of the ~30 eases its tween engine supports.
    /// This adds the rest after Studio's own six (so saved scenes and existing indexes are unchanged), which
    /// also makes the dropdown show the right name for points Camera Routes sets to other eases.
    /// </summary>
    [HarmonyPatch(typeof(MPRoutePointCtrl), "Awake")]
    internal static class StudioEaseList
    {
        private static readonly string[] Extra =
        {
            "easeInOutSine:Smooth (Sine)",
            "easeInOutQuad:Smooth (Quad)",
            "easeInOutCubic:Smooth (Cubic)",
            "easeInOutQuart:Smooth (Quart)",
            "easeInOutQuint:Smooth (Quint)",
            "easeInOutExpo:Smooth (Expo)",
            "easeInOutCirc:Smooth (Circ)",
            "easeInSine:Faster (Sine)",
            "easeInCubic:Faster (Cubic)",
            "easeInQuint:Faster (Quint)",
            "easeInExpo:Faster (Expo)",
            "easeInCirc:Faster (Circ)",
            "easeOutSine:Slower (Sine)",
            "easeOutCubic:Slower (Cubic)",
            "easeOutQuint:Slower (Quint)",
            "easeOutExpo:Slower (Expo)",
            "easeOutCirc:Slower (Circ)",
            "easeInBack:Back (in)",
            "easeOutBack:Back (out)",
            "easeInOutBack:Back (in-out)",
            "easeInElastic:Elastic (in)",
            "easeOutElastic:Elastic (out)",
            "easeInOutElastic:Elastic (in-out)",
            "easeInBounce:Bounce (in)",
            "easeInOutBounce:Bounce (in-out)",
            "spring:Spring",
        };

        [HarmonyPostfix]
        private static void Postfix(MPRoutePointCtrl __instance)
        {
            try
            {
                Extend(__instance);
            }
            catch (Exception e)
            {
                CameraRoutesPlugin.Logger.LogWarning("Couldn't extend Studio's ease list: " + e.Message);
            }
        }

        private static void Extend(MPRoutePointCtrl ctrl)
        {
            var listField = AccessTools.Field(typeof(MPRoutePointCtrl), "listEase");
            var dropField = AccessTools.Field(typeof(MPRoutePointCtrl), "dropdownEase");
            var infoType = AccessTools.Inner(typeof(MPRoutePointCtrl), "EaseInfo");
            if (listField == null || dropField == null || infoType == null) return;

            var list = listField.GetValue(ctrl) as IList;
            var dropdown = dropField.GetValue(ctrl) as Dropdown;
            if (list == null || dropdown == null) return;

            var nameProp = AccessTools.Property(infoType, "name");
            var easeProp = AccessTools.Property(infoType, "ease");
            var present = new HashSet<StudioTween.EaseType>();
            foreach (var info in list) present.Add((StudioTween.EaseType)easeProp.GetValue(info, null));

            foreach (string entry in Extra)
            {
                string[] parts = entry.Split(':');
                var ease = (StudioTween.EaseType)Enum.Parse(typeof(StudioTween.EaseType), parts[0]);
                if (present.Contains(ease)) continue;
                list.Add(Activator.CreateInstance(infoType, AccessTools.all, null, new object[] { parts[1], ease }, null));
                present.Add(ease);
            }

            var options = new List<Dropdown.OptionData>();
            foreach (var info in list) options.Add(new Dropdown.OptionData((string)nameProp.GetValue(info, null)));
            dropdown.options = options;
        }
    }
}
