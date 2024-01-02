using HTC.UnityPlugin.Vive;
using HS2VR;
using IllusionUtility.GetUtility;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;

namespace BetterVR
{
    public class VRGlove : MonoBehaviour
    {
        private static GameObject gloves;
        private static SkinnedMeshRenderer gloveRenderer;

        private HandRole handRole;
        private bool isRepositioning = false;

        internal static bool isShowingGloves { get { return gloveRenderer && gloveRenderer.gameObject.activeSelf ; } }

        internal static VRGlove CreateLeftGlove()
        {
            return CreateGlove(HandRole.LeftHand, "cf_J_ArmLow01_L", 1);
        }

        internal static VRGlove CreateRightGlove()
        {
            return CreateGlove(HandRole.RightHand, "cf_J_ArmLow01_R", -1);
        }

        internal void StartRepositioning()
        {
            isRepositioning = true;
        }

        void Update()
        {
            Vector3 center;
            Transform controllerModel =
                handRole == HandRole.RightHand ?
                BetterVRPluginHelper.FindRightControllerRenderModel(out center) :
                BetterVRPluginHelper.FindLeftControllerRenderModel(out center);

            bool shouldShowHand =
                (BetterVRPlugin.HandDisplay.Value == "Gloves" || BetterVRPlugin.HandDisplay.Value == "GlovesAndControllers") &&
                controllerModel != null;
            bool isShowingHand = gloveRenderer.gameObject.activeSelf;

            gloveRenderer.gameObject.SetActive(shouldShowHand);

            if (shouldShowHand != isShowingHand) BetterVRPluginHelper.UpdateControllersVisibilty();

            if (!shouldShowHand) return;

            var camera = BetterVRPluginHelper.VRCamera;
            if (camera && gloveRenderer.transform.parent != camera.transform)
            {
                // Parent the renderer to camera to make sure that the gloves stay in the renderer's bounds and do not get culled.
                gloveRenderer.transform.parent = camera.transform;
                gloveRenderer.transform.localPosition = Vector3.zero;
            }

            if (isRepositioning)
            {
                if (ViveInput.GetPressEx<HandRole>(HandRole.LeftHand, ControllerButton.Grip) ||
                    ViveInput.GetPressEx<HandRole>(HandRole.RightHand, ControllerButton.Grip))
                {
                    if (transform.parent != controllerModel.parent) transform.SetParent(controllerModel.parent, worldPositionStays: true);
                }
                else
                {
                    if (transform.parent != null) transform.SetParent(null, worldPositionStays: true);
                }

                if (ViveInput.GetPressDownEx<HandRole>(HandRole.LeftHand, ControllerButton.Trigger) ||
                    ViveInput.GetPressDownEx<HandRole>(HandRole.LeftHand, ControllerButton.AKey) ||
                    ViveInput.GetPressDownEx<HandRole>(HandRole.RightHand, ControllerButton.Trigger) ||
                    ViveInput.GetPressDownEx<HandRole>(HandRole.RightHand, ControllerButton.AKey))
                {
                    isRepositioning = false;
                    transform.SetParent(controllerModel.parent);
                    var offset = controllerModel.InverseTransformVector(transform.position - center);
                    if (handRole == HandRole.LeftHand)
                    {
                        BetterVRPlugin.LeftGloveRotation.Value = transform.localRotation;
                        BetterVRPlugin.LeftGloveOffset.Value = offset;
                    }
                    else
                    {
                        BetterVRPlugin.RightGloveRotation.Value = transform.localRotation;
                        BetterVRPlugin.RightGloveOffset.Value = offset;
                    }
                    BetterVRPlugin.Logger.LogInfo("Set hand offset: " + offset + " rotation: " + transform.localRotation.eulerAngles);
                }

                return;
            }

            if (transform.parent != controllerModel.parent) transform.parent = controllerModel.parent;

            if (transform.parent == null) return;

            // The render model may have been changed by the system so the simple renderer may need to be repositioned too.
            transform.localScale = Vector3.one * BetterVRPlugin.GloveScale.Value;
            transform.localRotation =
                handRole == HandRole.RightHand ? BetterVRPlugin.RightGloveRotation.Value : BetterVRPlugin.LeftGloveRotation.Value;
            Vector3 offsetFromCenter =
                handRole == HandRole.RightHand ? BetterVRPlugin.RightGloveOffset.Value : BetterVRPlugin.LeftGloveOffset.Value;
            transform.position = center + controllerModel.transform.TransformVector(offsetFromCenter);
        }

        private static void LoadGloves()
        {
            if (gloves != null) return;

            GameObject prefab;
            try
            {
                prefab = AssetBundleManager.LoadAssetBundle(AssetBundleNames.Chara00Mo_Gloves_00)?.Bundle?.LoadAsset<GameObject>(
                    "assets/illusion/assetbundle/prefabs/chara/male/00/mo_gloves_00/p_cm_glove_gunte.prefab");
            }
            catch
            {
                BetterVRPlugin.Logger.LogWarning("Cannot find gloves asset, may try again later.");
                return;
            }

            if (!prefab) return;
            BetterVRPlugin.Logger.LogInfo("Found gloves asset");

            gloves = GameObject.Instantiate(prefab);

            gloveRenderer = gloves.GetComponentInChildren<SkinnedMeshRenderer>();
            if (!gloveRenderer) return;

            gloveRenderer.GetOrAddComponent<BetterVRPluginHelper.SilhouetteMaterialSetter>();
        }

        private static VRGlove CreateGlove(HandRole handRole, string name, float rotationFactor)
        {
            LoadGloves();
            if (gloves == null) return null;
            var transform = gloves.transform.FindLoop(name);
            if (!transform)
            {
                BetterVRPlugin.Logger.LogWarning("Glove bones not found, trying to reload asset");
                LoadGloves();
                if (!transform) return null;
            }

            var glove = transform.GetOrAddComponent<VRGlove>();
            glove.handRole = handRole;
            var fingerPoses = transform.GetOrAddComponent<FingerPoseUpdater>();
            fingerPoses.Init(handRole, rotationFactor);
            var hSpeedGesture = glove.GetOrAddComponent<HSpeedGesture>();
            hSpeedGesture.roleProperty = handRole == HandRole.LeftHand ? VRControllerInput.roleL : VRControllerInput.roleR;
            hSpeedGesture.capsuleStart = fingerPoses.ring;
            hSpeedGesture.capsuleEnd = fingerPoses.index;
            hSpeedGesture.radius = BetterVRPlugin.ControllerColliderRadius.Value;
            return glove;
        }
    }

    internal class FingerPoseUpdater : MonoBehaviour
    {
        private HandRole handRole;
        private float rotationFactor = 1;
        private Transform thumb;
        public Transform index { get; private set; }
        public Transform middle { get; private set; }
        public Transform ring { get; private set; }
        private Transform pinky;

        public DynamicBoneCollider indexCollider { get; private set; }

        internal void Init(HandRole handRole, float rotationFactor)
        {
            this.handRole = handRole;
            this.rotationFactor = rotationFactor;
        }

        void Awake()
        {
            thumb = FindFirstMatchingTransform(transform, "umb");
            index = FindFirstMatchingTransform(transform, "ndex");
            middle = FindFirstMatchingTransform(transform, "iddle");
            ring = FindFirstMatchingTransform(transform, "ing");
            pinky = FindFirstMatchingTransform(transform, "ittle");

            if (index)
            {
                indexCollider = new GameObject(name + "_indexCollider").AddComponent<DynamicBoneCollider>();
                indexCollider.m_Radius = 0.01f;
                indexCollider.m_Height = 0.15f;
                indexCollider.m_Direction = DynamicBoneColliderBase.Direction.X;
                var colliderParent = index.childCount > 0 ? index.GetChild(0) : index;
                if (colliderParent.childCount > 0) colliderParent = colliderParent.GetChild(0);
                indexCollider.transform.parent = colliderParent;
                indexCollider.transform.localPosition = Vector3.zero;
                indexCollider.transform.localRotation = Quaternion.identity;
            }
        }

        void Update()
        {
            float thumbAngle = 35;
            if (ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.AKeyTouch) ||
                ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.BkeyTouch) ||
                ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.PadTouch) ||
                ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.MenuTouch)) thumbAngle = 15;
            if (thumb && thumb.childCount > 0) thumb.GetChild(0).localRotation = Quaternion.Euler(0, 0, thumbAngle * rotationFactor);

            float indexCurl = ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.IndexCurl);
            float middleCurl = ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.MiddleCurl);
            float ringCurl = ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.RingCurl);
            float pinkyCurl = ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.PinkyCurl);

            if (indexCurl != 0 || middleCurl != 0 || ringCurl != 0 || pinkyCurl != 0)
            {
                UpdateAngle(index, indexCurl * 35);
                UpdateAngle(middle, middleCurl * 60);
                UpdateAngle(ring, ringCurl * 60);
                UpdateAngle(pinky, pinkyCurl * 60);
                return;
            }

            float indexAngle = 10;
            if (ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.TriggerTouch)) indexAngle = 30;
            indexAngle += ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.Trigger) * 5;

            float gripAngle = 10;
            if (ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.TriggerTouch) ||
                ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.GripTouch) ||
                ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.CapSenseGripTouch)) gripAngle = 70;
            gripAngle += ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.CapSenseGrip) * 3;

            UpdateAngle(index, indexAngle);
            UpdateAngle(middle, gripAngle);
            UpdateAngle(ring, gripAngle * 1.15f);
            UpdateAngle(pinky, gripAngle * 1.4f);
        }

        private void UpdateAngle(Transform finger, float angle)
        {
            if (finger == null) return;
            finger.localRotation = Quaternion.Euler(0, 0, angle * rotationFactor);
            if (finger.childCount > 0) UpdateAngle(finger.GetChild(0), angle * 1.0625f);
        }

        private static Transform FindFirstMatchingTransform(Transform transform, string partialName)
        {
            if (transform.name.Contains(partialName)) return transform;

            for (int i = 0; i < transform.childCount; i++)
            {
                Transform result = FindFirstMatchingTransform(transform.GetChild(i), partialName);
                if (result) return result;
            }
            return null;
        }
    }
}