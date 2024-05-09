using BepInEx;
using BepInEx.Logging;
using Manager;
using HTC.UnityPlugin.Vive;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BetterVR 
{
    [BepInPlugin(GUID, GUID, Version)]
    [BepInProcess("HoneySelect2VR")]
    public partial class BetterVRPlugin : BaseUnityPlugin 
    {
        public const string GUID = "BetterVR";
        public const string Version = "0.53";
        internal static new ManualLogSource Logger { get; private set; }

#if DEBUG
        internal static bool debugLog = true;
#else
        internal static bool debugLog = false;
#endif

        private static StripUpdater leftHandStripUpdater;
        private static StripUpdater rightsHandStripUpdater;

        internal static float ILUTimer { get; private set; } = 0;
        internal static bool ILUCooldown { get; private set; } = false;

        internal void Start() 
        {
            Logger = base.Logger;
            // DebugTools.logger = Logger;

            PluginConfigInit();

            //Harmony init.  It's magic!
            Harmony harmony_controller = new Harmony(GUID + "_controller");                        
            VRControllerHooks.InitHooks(harmony_controller, this);

            Harmony harmony_menu = new Harmony(GUID + "_menu");
            VRMenuHooks.InitHooks(harmony_menu, this);

            //Potentially important Hs2 classes
            //ControllerManager  has button input triggers, and the laser pointer
            //ControllerManagerSample   same thing?
            //ShowMenuOnClick   shows controller GUI
            //vrTest
            // internal static bool isOculus = XRDevice.model.Contains("Oculus");

            BetterVRPluginHelper.UpdatePrivacyScreen(Color.white);
        }

        // Check for controller input changes
        internal void Update()
        {
            if (leftHandStripUpdater == null) leftHandStripUpdater = new StripUpdater(VRControllerInput.roleL);
            leftHandStripUpdater?.CheckStrip(BetterVRPlugin.GestureStrip.Value == "Left hand");

            if (rightsHandStripUpdater == null) rightsHandStripUpdater = new StripUpdater(VRControllerInput.roleR);
            rightsHandStripUpdater?.CheckStrip(BetterVRPlugin.GestureStrip.Value == "Right hand");

            BetterVRPluginHelper.TryInitializeGloves();

            if (ViveInput.GetPressDownEx<HandRole>(HandRole.LeftHand, ControllerButton.Trigger) ||
                ViveInput.GetPressDownEx<HandRole>(HandRole.RightHand, ControllerButton.Trigger) &&
                Time.timeScale == 0)
            {
                // Fix the bug that time scale becomes zero after opening BepInex config and closing game settings
                Time.timeScale = 1;
            }

            CheckRadialMenu(BetterVRPluginHelper.leftRadialMenu, HandRole.LeftHand);
            CheckRadialMenu(BetterVRPluginHelper.rightRadialMenu, HandRole.RightHand);

            // if (BetterVRPlugin.debugLog && Time.frameCount % 10 == 0) BetterVRPlugin.Logger.LogInfo($" SqueezeToTurn {SqueezeToTurn.Value} VRControllerInput.VROrigin {VRControllerInput.VROrigin}");        

            VRControllerInput.UpdateSqueezeMovement();

            if (VRControllerInput.IsILUGesture(HandRole.LeftHand) || VRControllerInput.IsILUGesture(HandRole.RightHand))
            {
                ILUTimer += Time.deltaTime;
                if (!ILUCooldown && ILUTimer > 3f)
                {
                    BetterVRPluginHelper.FinishH();
                    ILUCooldown = true;
                }
            }
            else
            {
                ILUTimer = 0;
                ILUCooldown = false;
            }
        }

        internal static AIChara.ChaControl GetPlayer()
        {
            return Singleton<HSceneManager>.Instance?.Hscene?.GetMales()?[0];
        }

        private static void CheckRadialMenu(RadialMenu radialMenu, HandRole handRole)
        {
            bool shouldActivateMenu =
                VRControllerInput.IsChillGesture(handRole) ||
                ViveInput.GetPressDownEx<HandRole>(handRole, ControllerButton.AKey);

            bool shouldKeepMenuActivated =
                (VRControllerInput.inHandTrackingMode && ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.Grip)) ||
                ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.AKey);

            bool menuShouldBeActive = shouldActivateMenu || (shouldKeepMenuActivated && radialMenu.isActiveAndEnabled);
            if (menuShouldBeActive && !radialMenu.gameObject.activeSelf)
            {
                radialMenu.gameObject.SetActive(true);
                radialMenu.captions = new string[]
                {
                    "Toy",
                    "",
                    "Finish H loop stage",
                    "Scale reset (press trigger)",
                    "P show/hide",
                    "View reset (press trigger)",
                    "Male show/hide",
                    "Glove posing (other hand)"
                };
            }

            if (!radialMenu.isActiveAndEnabled) return;

            int selectedItemIndex = radialMenu.selectedItemIndex;
            // bool isTriggerDown = ViveInput.GetPressDownEx<HandRole>(handRole, ControllerButton.Trigger);
            bool isTriggerUp = ViveInput.GetPressUpEx<HandRole>(handRole, ControllerButton.Trigger);
            if (!menuShouldBeActive) radialMenu.gameObject.SetActive(false);

            if (menuShouldBeActive && !isTriggerUp) return;

            switch (selectedItemIndex)
            {
                case 0:
                    BetterVRPluginHelper.handHeldToy.CycleMode(handRole == HandRole.RightHand);
                    BetterVRPluginHelper.UpdateControllersVisibilty();
                    VRControllerCollider.UpdateDynamicBoneColliders();
                    break;
                case 1:
                    if (VRMenuHooks.ShouldReloadScene)
                    {
                        VRMenuHooks.ShouldReloadScene = false;
                        Scene.LoadReserve(new Scene.Data
                        {
                            levelName = "VRHScene",
                            fadeType = FadeCanvas.Fade.In
                        }, true);
                        return;

                    } else
                    {
                        break;
                    }


                    // InteractionCollider.shouldVisualizeColliders = !InteractionCollider.shouldVisualizeColliders;
                    var hScene = Singleton<HSceneManager>.Instance?.Hscene;
                    if (hScene == null) break;
                    var ctrls = hScene.ctrlHitObjectFemales;
                    if (ctrls == null || ctrls.Length == 0) break;
                    var objs = (List<GameObject>)typeof(HitObjectCtrl).GetField("lstObject", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(ctrls[0]);
                    if (objs == null) break;
                    foreach (var obj in objs)
                    {
                        if (obj == null)
                        {
                            Logger.LogWarning("Clearing hit objects... ");
                            objs.Clear();
                            break;
                        }
                    }


                    VRControllerCollider.UpdateDynamicBoneColliders();
                    break;
                case 2:
                    BetterVRPluginHelper.FinishH();
                    break;
                case 3:
                    if (isTriggerUp) VRControllerInput.ResetWorldScale();
                    break;
                case 4:
                    BetterVRPluginHelper.CyclePlayerPDisplayMode();
                    VRControllerCollider.UpdateDynamicBoneColliders();
                    break;
                case 5:
                    if (!isTriggerUp) break;
                    radialMenu.gameObject.SetActive(false);
                    BetterVRPluginHelper.ResetView();
                    BetterVRPluginHelper.UpdateControllersVisibilty();
                    VRControllerCollider.UpdateDynamicBoneColliders();
                    break;
                case 6:
                    // Toggle player body visibility.
                    Manager.Config.HData.Visible = !Manager.Config.HData.Visible;
                    BetterVRPluginHelper.UpdatePlayerColliderActivity();
                    VRControllerCollider.UpdateDynamicBoneColliders();
                    break;
                case 7:
                    if (!isTriggerUp) break;
                    if (handRole == HandRole.LeftHand) {
                        BetterVRPluginHelper.rightGlove?.StartRepositioning();
                    }
                    else
                    {
                        BetterVRPluginHelper.leftGlove?.StartRepositioning();
                    }
                    break;
                default:
                    break;
            }
        }
    }
}
