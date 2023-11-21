using HarmonyLib;
using HS2VR;
using Manager;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

namespace BetterVR
{ 
    internal static class VRMenuHooks
    {

        internal static BetterVRPlugin pluginInstance;


        public static void InitHooks(Harmony harmonyInstance, BetterVRPlugin _pluginInstance)
        {
            pluginInstance = _pluginInstance;
            harmonyInstance.PatchAll(typeof(VRMenuHooks));
        }


        /// <summary>
        /// When VRSelectScene is activated 
        /// </summary>
        [HarmonyPostfix, HarmonyPatch(typeof(VRSelectScene), "Start")]
        internal static void VRSelectScene_Start(VRSelectScene __instance)
        {                   
            //If the pointer game object is active, then set the cursor angle
            if (BetterVRPlugin.debugLog) BetterVRPlugin.Logger.LogInfo($" VRSelectScene_Start ");   

            //Get the character card data
            VRSelectManager vrMgr = Singleton<VRSelectManager>.Instance;

            //Add Random button to GUI, next to optional button
            VRMenuRandom.AppendRandomButton(__instance); 
            VRMenuRandom.VRSelectSceneStart();         
        }

        [HarmonyPostfix, HarmonyPatch(typeof(GripMoveCrtl), "Start")]
        internal static void FindVrOrigin(GripMoveCrtl __instance)
        {
            GameObject objVROrigin = (GameObject) typeof(GripMoveCrtl).GetField("objVROrigin", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance);
            if (objVROrigin)
            {
                BetterVRPluginHelper.Init(objVROrigin);
            }
        }

        [HarmonyPostfix, HarmonyPatch(typeof(VRSettingUI), "Start")]
        internal static void FindResetViewButton(VRSettingUI __instance)
        {
            Button recenterButton = (Button) typeof(VRSettingUI).GetField("btnRecenter", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(__instance);
            if (recenterButton != null)
            {
                BetterVRPluginHelper.recenterVR = recenterButton.onClick;
            }
        }
    }
}
