using UnityEngine;
using System.Collections;

namespace BetterVR
{    
    public static class VRControllerPointer
    {
        /// <summary>
        /// Sets the angle of the laser pointer after some time to let the game objects settle
        /// </summary>
        public static IEnumerator SetLaserAngleWithDelay(BetterVRPluginHelper.VR_Hand hand)
        {
            yield return new WaitForSeconds(0.01f);
            GetHandAndSetAngle(hand);
        }

        public static void UpdateAngles()
        {
            GetHandAndSetAngle(BetterVRPluginHelper.VR_Hand.left);
            GetHandAndSetAngle(BetterVRPluginHelper.VR_Hand.right);
        }

        /// <summary>
        /// Sets the angle of the laser pointer on the VR controller to the users configured value
        /// </summary>
        private static void GetHandAndSetAngle(BetterVRPluginHelper.VR_Hand vrHand)
        {
            var controller = BetterVRPluginHelper.GetHand(vrHand);
            var controllerCenter =
                vrHand == BetterVRPluginHelper.VR_Hand.left ?
                BetterVRPluginHelper.leftControllerCenter :
                BetterVRPluginHelper.rightControllerCenter;

            if (controller == null || controllerCenter == null) return;
            var raycaster = controller.GetComponentInChildren<HTC.UnityPlugin.Pointer3D.Pointer3DRaycaster>(true);
            if (raycaster == null) return;

            // BetterVRPluginHelper.GetRightHand()?.GetComponentInChildren<GuideLineDrawer>();
            // if (raycaster.transform.parent?.parent != controllerModel)

            if (controller.transform.Find("LaserPointer") == null) return;

            var oldAngles = raycaster.transform.localRotation.eulerAngles;

            // Leave the unpatched state available as an option in case there is some problem with the patch.
            if (-BetterVRPlugin.SetVRControllerPointerAngle.Value == oldAngles.x) return;

            // Rotate the laser pointer to the desired angle.
            raycaster.transform.localRotation = Quaternion.Euler(-BetterVRPlugin.SetVRControllerPointerAngle.Value, 0, 0);

            BetterVRPlugin.Logger.LogInfo("Updated laser pointer rotation: " + oldAngles + " -> " + raycaster.transform.localRotation.eulerAngles);

            UpdateStabilizer(controller, false);
        }

        internal static void UpdateStabilizer(GameObject controller, bool freeze)
        {
            var stabilizer = 
                controller?.
                GetComponentInChildren<HTC.UnityPlugin.Pointer3D.Pointer3DRaycaster>(true)?.
                GetComponentInParent<HTC.UnityPlugin.PoseTracker.PoseStablizer>();
            if (!stabilizer) return;

            if (freeze)
            {
                stabilizer.positionThreshold = 1 / 32f;
                stabilizer.rotationThreshold = 30;
            }
            else
            {
                // The vanilla laser pointer stabilization is too aggressive and causes a laggy feel.
                // Reduce the thresholds for a better balance between stability and responsiveness.
                stabilizer.positionThreshold = 0;
                // Hand tracking can flicker a lot and needs more stabilization.
                stabilizer.rotationThreshold = VRControllerInput.inHandTrackingMode ? 1f : 0.5f;
            }
        }
    }
}
