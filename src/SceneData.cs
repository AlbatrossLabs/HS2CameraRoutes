using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;

namespace HS2CameraRoutes
{
    /// <summary>
    /// Saves route records inside the scene file through ExtensibleSaveFormat (part of BepisPlugins, in every
    /// BetterRepack / HF Patch install). It's reached by reflection, so Camera Routes still runs without it;
    /// records then last only until the scene is closed.
    /// </summary>
    internal static class SceneData
    {
        private const string DataKey = "records";
        private static Type _ext, _pluginData;
        public static bool Available { get; private set; }

        public static void Init()
        {
            try
            {
                _ext = AccessTools.TypeByName("ExtensibleSaveFormat.ExtendedSave");
                _pluginData = AccessTools.TypeByName("ExtensibleSaveFormat.PluginData");
                if (_ext == null || _pluginData == null)
                {
                    CameraRoutesPlugin.Logger.LogWarning("ExtensibleSaveFormat not found: Camera Routes data won't be saved with scenes.");
                    return;
                }
                Subscribe("SceneBeingSaved", "OnSaving");
                Subscribe("SceneBeingLoaded", "OnLoading");
                Subscribe("SceneBeingImported", "OnImporting");
                Available = true;
            }
            catch (Exception e)
            {
                CameraRoutesPlugin.Logger.LogWarning("Couldn't hook scene saving: " + e);
            }
        }

        private static void Subscribe(string eventName, string handler)
        {
            EventInfo ev = _ext.GetEvent(eventName, BindingFlags.Public | BindingFlags.Static);
            if (ev == null) throw new MissingMemberException(_ext.Name, eventName);
            MethodInfo m = typeof(SceneData).GetMethod(handler, BindingFlags.NonPublic | BindingFlags.Static);
            ev.AddEventHandler(null, Delegate.CreateDelegate(ev.EventHandlerType, m));
        }

        private static void OnSaving(string path)
        {
            try
            {
                // Always written (empty when there are no routes) so an earlier scene's records can't carry over.
                string payload = RouteRegistry.SerializeAll();
                object pd = Activator.CreateInstance(_pluginData);
                AccessTools.Field(_pluginData, "version").SetValue(pd, 1);
                var data = (IDictionary)AccessTools.Field(_pluginData, "data").GetValue(pd);
                data[DataKey] = payload;
                AccessTools.Method(_ext, "SetSceneExtendedDataById").Invoke(null, new[] { CameraRoutesPlugin.Guid, pd });
            }
            catch (Exception e) { CameraRoutesPlugin.Logger.LogError("Saving Camera Routes data failed: " + e); }
        }

        private static void OnLoading(string path)
        {
            try
            {
                RouteRegistry.LoadAll(Read());
                UndoStack.Clear();
            }
            catch (Exception e) { CameraRoutesPlugin.Logger.LogError("Loading Camera Routes data failed: " + e); }
        }

        private static void OnImporting(string path)
        {
            try { RouteRegistry.AddPendingImport(Read()); }
            catch (Exception e) { CameraRoutesPlugin.Logger.LogError("Importing Camera Routes data failed: " + e); }
        }

        private static string Read()
        {
            object pd = AccessTools.Method(_ext, "GetSceneExtendedDataById").Invoke(null, new object[] { CameraRoutesPlugin.Guid });
            if (pd == null) return null;
            var data = AccessTools.Field(_pluginData, "data").GetValue(pd) as IDictionary;
            if (data == null || !data.Contains(DataKey)) return null;
            return data[DataKey] as string;
        }
    }

    /// <summary>New scene (and the start of every load): forget the old scene's records and undo history.</summary>
    [HarmonyPatch(typeof(Studio.Studio), "InitScene")]
    internal static class InitScenePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            RouteRegistry.Clear();
            UndoStack.Clear();
            Tracker.Rescan();
        }
    }
}
