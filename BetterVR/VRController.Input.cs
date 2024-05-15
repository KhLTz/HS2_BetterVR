using TMPro;
using HTC.UnityPlugin.Pointer3D;
using HTC.UnityPlugin.Vive;
using Illusion.Extensions;
using System;
using System.Reflection;
using UnityEngine;

namespace BetterVR
{
    public static class VRControllerInput
    {
        internal static ViveRoleProperty roleH { get; private set; } = ViveRoleProperty.New(DeviceRole.Hmd);
        internal static ViveRoleProperty roleR { get; private set; } = ViveRoleProperty.New(HandRole.RightHand);
        internal static ViveRoleProperty roleL { get; private set; } = ViveRoleProperty.New(HandRole.LeftHand);
        private static ControllerManager _controllerManager;
        internal static ControllerManager controllerManager {
            get { return _controllerManager ?? (_controllerManager = GameObject.FindObjectOfType<ControllerManager>()); }
            set { _controllerManager = value; }
        }

        internal static bool inHandTrackingMode { get; private set; }
        internal static bool isDraggingScale { get { return twoHandedWorldGrab != null && twoHandedWorldGrab.canScale; } }
        private static TwoHandedWorldGrab _twoHandedWorldGrab;
        private static TwoHandedWorldGrab twoHandedWorldGrab
        {
            get
            {
                if (_twoHandedWorldGrab == null || _twoHandedWorldGrab.gameObject == null)
                {
                    _twoHandedWorldGrab = new GameObject("WorldGrabScale").AddComponent<TwoHandedWorldGrab>();
                    _twoHandedWorldGrab.enabled = false;
                }
                return _twoHandedWorldGrab;
            }
        }
        private static bool leftHandTriggerAndGrip;
        private static bool rightHandTriggerAndGrip;
        private static Vector3? originalLeftRaycasterPosition;
        private static Vector3? originalRightRaycasterPosition;

        internal static Vector3 handMidpointLocal
        {
            get { return Vector3.Lerp(VivePose.GetPose(roleL).pos, VivePose.GetPose(roleR).pos, 0.5f); }
        }

        internal static float handDistanceLocal
        {
            get
            {
                return Vector3.Distance(VivePose.GetPose(VRControllerInput.roleL).pos, VivePose.GetPose(VRControllerInput.roleR).pos);
            }
        }
        
        internal static Pointer3DRaycaster leftRaycaster
        {
            get { return BetterVRPluginHelper.GetLeftHand()?.GetComponentInChildren<Pointer3DRaycaster>(true); }
        }

        internal static Pointer3DRaycaster rightRaycaster
        {
            get { return BetterVRPluginHelper.GetRightHand()?.GetComponentInChildren<Pointer3DRaycaster>(true); }
        }

        internal static HandRole GetHandRole(ViveRoleProperty roleProperty)
        {
            if (roleProperty == roleL) return HandRole.LeftHand;
            if (roleProperty == roleR) return HandRole.RightHand;
            return HandRole.Invalid;
        }

        /// <summary>
        /// Handles world scaling, rotation, and locomotion when user squeezes the grip
        /// </summary>
        internal static void UpdateSqueezeMovement()
        {
            bool wasInHandTrackingMode = inHandTrackingMode;
            UpdateHandTrackingMode();
            if (inHandTrackingMode != wasInHandTrackingMode)
            {
                UpdateCursorAttachPosition();
                RestoreRaycasters();
            }
            VRControllerPointer.UpdateStabilizer(BetterVRPluginHelper.GetLeftHand(), freeze: TightGrip(HandRole.RightHand));
            VRControllerPointer.UpdateStabilizer(BetterVRPluginHelper.GetRightHand(), freeze: TightGrip(HandRole.LeftHand));

            Transform vrOrigin = BetterVRPluginHelper.VROrigin?.transform;
            if (!vrOrigin) return;

            var wasHoldingLeftHandTriggerAndGrip = leftHandTriggerAndGrip;
            var wasHoldingRightHandTriggerAndGrip = rightHandTriggerAndGrip;

            leftHandTriggerAndGrip =
                ViveInput.GetPressEx<HandRole>(HandRole.LeftHand, ControllerButton.Trigger) &&
                ViveInput.GetPressEx<HandRole>(HandRole.LeftHand, ControllerButton.Grip);
            rightHandTriggerAndGrip =
                ViveInput.GetPressEx<HandRole>(HandRole.RightHand, ControllerButton.Trigger) &&
                ViveInput.GetPressEx<HandRole>(HandRole.RightHand, ControllerButton.Grip);
            bool bothGrips =
                ViveInput.GetPressEx<HandRole>(HandRole.LeftHand, ControllerButton.Grip) &&
                ViveInput.GetPressEx<HandRole>(HandRole.RightHand, ControllerButton.Grip);

            bool twoHandedTurn = BetterVRPlugin.IsTwoHandedTurnEnabled() && bothGrips;
            bool shouldScale = inHandTrackingMode ?
                CanScaleByGesture() : (leftHandTriggerAndGrip && rightHandTriggerAndGrip);

            twoHandedWorldGrab.enabled = shouldScale || twoHandedTurn;
            twoHandedWorldGrab.canScale = shouldScale;

            bool allowOneHandedWorldGrab =
                !twoHandedWorldGrab.enabled && (BetterVRPlugin.IsOneHandedTurnEnabled() || BetterVRPlugin.IsTwoHandedTurnEnabled());

            if (BetterVRPluginHelper.leftControllerCenter)
            {
                var leftHandWorldGrab = BetterVRPluginHelper.leftControllerCenter.GetOrAddComponent<OneHandedWorldGrab>();
                if (!leftHandTriggerAndGrip || rightHandTriggerAndGrip || !allowOneHandedWorldGrab)
                {
                    leftHandWorldGrab.enabled = false;
                }
                else if (leftHandWorldGrab.enabled == false)
                {
                    if ((!inHandTrackingMode || CanScaleOrMoveByGesture(HandRole.RightHand))  ||
                        !MenuAutoGrab.hasMenu ||
                        (!wasHoldingLeftHandTriggerAndGrip && IsCloseToWaist(BetterVRPluginHelper.leftControllerCenter.position)))
                    {
                        leftHandWorldGrab.enabled = true;
                    }
                }
            }

            if (BetterVRPluginHelper.rightControllerCenter)
            {
                var rightHandWorldGrab = BetterVRPluginHelper.rightControllerCenter.GetOrAddComponent<OneHandedWorldGrab>();
                if (!rightHandTriggerAndGrip || leftHandTriggerAndGrip || !allowOneHandedWorldGrab)
                {
                    rightHandWorldGrab.enabled = false;
                }
                else if (rightHandWorldGrab.enabled == false)
                {
                    if ((!inHandTrackingMode || CanScaleOrMoveByGesture(HandRole.LeftHand)) ||
                        !MenuAutoGrab.hasMenu ||
                        (!wasHoldingRightHandTriggerAndGrip && IsCloseToWaist(BetterVRPluginHelper.rightControllerCenter.position)))
                    {
                        rightHandWorldGrab.enabled = true;
                    }
                }
            }

            if (!isDraggingScale && bothGrips &&
                ViveInput.GetPressEx<HandRole>(HandRole.LeftHand, ControllerButton.AKey) &&
                ViveInput.GetPressEx<HandRole>(HandRole.RightHand, ControllerButton.AKey))
            {
                twoHandedWorldGrab.enabled = false;
                ResetWorldScale();
            }
        }

        internal static void ResetWorldScale()
        {
            var vrOrigin = BetterVRPluginHelper.VROrigin?.transform;
            if (!vrOrigin) return;

            var handMidpoint = vrOrigin.TransformPoint(handMidpointLocal);

            BetterVRPlugin.PlayerLogScale.Value = (float)BetterVRPlugin.PlayerLogScale.DefaultValue;

            RestoreHandMidpointWorldPosition(handMidpoint);
        }

        internal static void RestoreHandMidpointWorldPosition(Vector3? desiredWorldPosition)
        {
            var vrOrigin = BetterVRPluginHelper.VROrigin?.transform;
            if (desiredWorldPosition == null || vrOrigin == null) return;
            vrOrigin.Translate((Vector3)desiredWorldPosition - vrOrigin.TransformPoint(handMidpointLocal), Space.World);
        }

        internal static bool IsILUGesture()
        {
            return IsILUGesture(HandRole.LeftHand) || IsILUGesture(HandRole.RightHand);
        }

        internal static bool IsILUGesture(HandRole handRole)
        {
            return inHandTrackingMode &&
                !ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.AKeyTouch) &&
                !ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.BkeyTouch) &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.IndexCurl) < 0.3f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.MiddleCurl) > 0.8f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.RingCurl) > 0.8f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.PinkyCurl) < 0.3f;
        }

        internal static bool IsChillGesture(HandRole handRole)
        {
            return inHandTrackingMode &&
                !ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.AKeyTouch) &&
                !ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.BkeyTouch) &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.IndexCurl) > 0.8f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.MiddleCurl) > 0.8f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.RingCurl) > 0.8f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.PinkyCurl) < 0.3f;
        }

        internal static bool IsPeaceGesture(HandRole handRole)
        {
            if (!inHandTrackingMode) return false;
            if (!ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.AKeyTouch) &&
                !ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.BkeyTouch)) return false;

            return
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.IndexCurl) < 0.3f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.MiddleCurl) < 0.3f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.RingCurl) > 0.8f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.PinkyCurl) > 0.8f;
        }

        internal static bool TightGrip(HandRole handRole)
        {
            return inHandTrackingMode &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.MiddleCurl) > 0.8f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.RingCurl) > 0.8f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.PinkyCurl) > 0.8f;
        }

        internal static bool TightGrip(ViveRoleProperty handRole)
        {
            return inHandTrackingMode &&
                ViveInput.GetAxis(handRole, ControllerAxis.MiddleCurl) > 0.8f &&
                ViveInput.GetAxis(handRole, ControllerAxis.RingCurl) > 0.8f &&
                ViveInput.GetAxis(handRole, ControllerAxis.PinkyCurl) > 0.8f;
        }

        internal static bool CanStripUsingGesture(ViveRoleProperty handRole, bool wasGrabbing)
        {
            if (!inHandTrackingMode || ViveInput.GetAxis(handRole, ControllerAxis.MiddleCurl) < 0.5f) return false;
            if (wasGrabbing) return true;

            return ViveInput.GetAxis(handRole, ControllerAxis.IndexCurl) < 0.5f &&
                ViveInput.GetAxis(handRole, ControllerAxis.MiddleCurl) > 0.6f &&
                ViveInput.GetAxis(handRole, ControllerAxis.PinkyCurl) < 0.4f;
        }

        internal static bool CanOpenMenuByGesture(HandRole handRole)
        {
            if (!inHandTrackingMode) return false;
            if (!ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.AKeyTouch) &&
                !ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.BkeyTouch)) return false;
            return ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.IndexCurl) < 0.3f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.MiddleCurl) < 0.3f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.RingCurl) > 0.3f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.PinkyCurl) < 0.3f;
        }

        internal static bool CanCloseMenuByGesture(HandRole handRole)
        {
            return handRole != HandRole.Invalid && IsChillGesture(handRole);
        }

        internal static bool CanScaleByGesture()
        {
            return CanScaleOrMoveByGesture(HandRole.LeftHand) && CanScaleOrMoveByGesture(HandRole.RightHand);
        }

        internal static bool CanScaleOrMoveByGesture(HandRole handRole)
        {
            return inHandTrackingMode &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.IndexCurl) < 0.4f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.MiddleCurl) > 0.7f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.RingCurl) < 0.4f &&
                ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.PinkyCurl) > 0.7f;
        }

        private static void UpdateHandTrackingMode()
        {
            if (!BetterVRPlugin.UseFingerTrackingGestures.Value)
            {
                inHandTrackingMode = false;
                return;
            }

            const float MIN_CURL_DIFF = 0.5f;

            if (inHandTrackingMode)
            {
                if (ViveInput.GetPressEx<HandRole>(HandRole.LeftHand, ControllerButton.AKey) ||
                    ViveInput.GetPressEx<HandRole>(HandRole.LeftHand, ControllerButton.BKey) ||
                    ViveInput.GetPressEx<HandRole>(HandRole.RightHand, ControllerButton.AKey) ||
                    ViveInput.GetPressEx<HandRole>(HandRole.RightHand, ControllerButton.AKey))
                {
                    inHandTrackingMode = false;
                }
            }
            else
            {
                if (GetCurlDiff(HandRole.LeftHand) > MIN_CURL_DIFF || GetCurlDiff(HandRole.RightHand) > MIN_CURL_DIFF)
                {
                    inHandTrackingMode = true;
                }
            }
        }

        private static void UpdateCursorAttachPosition()
        {
            if (BetterVRPluginHelper.leftCursorAttach != null)
            {
                if (inHandTrackingMode && BetterVRPluginHelper.leftGlove != null)
                {
                    BetterVRPluginHelper.leftCursorAttach.position = BetterVRPluginHelper.leftGlove.transform.TransformPoint(new Vector3(-3f, 0.25f, 0.75f));
                }
                else {
                    BetterVRPluginHelper.leftCursorAttach.localPosition = new Vector3(0, 0.0625f, 0.125f);
                }
            }

            if (BetterVRPluginHelper.rightCursorAttach != null)
            {
                if (inHandTrackingMode && BetterVRPluginHelper.rightGlove != null) {
                    BetterVRPluginHelper.rightCursorAttach.position = BetterVRPluginHelper.rightGlove.transform.TransformPoint(new Vector3(3f, 0.25f, 0.75f));
                }
                else {
                    BetterVRPluginHelper.rightCursorAttach.localPosition = new Vector3(0, 0.0625f, 0.125f);
                }
            }
        }

        internal static void PlaceTouchModeRaycasters()
        {
            if (leftRaycaster)
            {
                if (originalLeftRaycasterPosition == null) originalLeftRaycasterPosition = leftRaycaster.transform.localPosition;
                leftRaycaster.FarDistance = inHandTrackingMode ? 0.08f : 20f;
                if (inHandTrackingMode && !TightGrip(HandRole.RightHand)) {
                    var index = BetterVRPluginHelper.leftGlove?.GetComponent<FingerPoseUpdater>()?.index;
                    if (index) leftRaycaster.transform.position = index.position;
                }
            }
            if (rightRaycaster)
            {
                if (originalRightRaycasterPosition == null) originalRightRaycasterPosition = rightRaycaster.transform.localPosition;
                rightRaycaster.FarDistance = inHandTrackingMode ? 0.08f : 20f;
                if (inHandTrackingMode && !TightGrip(HandRole.LeftHand))
                {
                    var index = BetterVRPluginHelper.rightGlove?.GetComponent<FingerPoseUpdater>()?.index;
                    if (index) rightRaycaster.transform.position = index.position;
                }
            }
        }

        internal static void RestoreRaycasters()
        {
            if (leftRaycaster)
            {
                leftRaycaster.FarDistance = 20f;
                if (originalLeftRaycasterPosition != null) leftRaycaster.transform.localPosition = originalLeftRaycasterPosition.Value;
            }
            if (rightRaycaster)
            {
                rightRaycaster.FarDistance = 20f;
                if (originalRightRaycasterPosition != null) rightRaycaster.transform.localPosition = originalRightRaycasterPosition.Value;
            }
        }

        private static float GetCurlDiff(HandRole handRole)
        {
            var middleCurl = ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.MiddleCurl);
            var pinkyCurl = ViveInput.GetAxisEx<HandRole>(handRole, ControllerAxis.PinkyCurl);
            return Mathf.Abs(middleCurl - pinkyCurl);
        }

        private static bool IsCloseToWaist(Vector3 position)
        {
            var range = BetterVRPlugin.PlayerScale * 0.25f;
            Collider[] colliders = Physics.OverlapSphere(position, range, 1 << StripUpdater.H_CAMERA_LAYER);
            foreach (Collider collider in colliders)
            {
                InteractionCollider interactionCollider = collider.GetComponent<InteractionCollider>();
                if (interactionCollider != null && interactionCollider.IsCharacterVisible() && interactionCollider.name.Contains("osi")) return true;
            }
            return false;
        }

        public class TwoHandedWorldGrab : MonoBehaviour
        {
            private float scaleDraggingFactor;
            private Vector3? desiredHandMidpointWorldCoordinates;
            private static Vector3? lastHandPositionDifference = null;
            private static TextMeshPro _scaleIndicator;
            private static TextMeshPro scaleIndicator
            {
                get
                {
                    if (!_scaleIndicator || !_scaleIndicator.gameObject) _scaleIndicator = CreateScaleIndicator();
                    return _scaleIndicator;
                }
            }
            private bool _canScale = false;
            internal bool canScale
            {
                get { return _canScale; }
                set
                {
                    if (_canScale != value) InitializeScaleDraggingFactor();
                    _canScale = value;
                }
            }

            void OnEnable()
            {
                if (canScale) InitializeScaleDraggingFactor();

                var vrOrigin = BetterVRPluginHelper.VROrigin;
                if (vrOrigin == null)
                {
                    desiredHandMidpointWorldCoordinates = null;
                    lastHandPositionDifference = null;
                }
                else
                {
                    desiredHandMidpointWorldCoordinates = vrOrigin.transform.TransformPoint(handMidpointLocal);
                    lastHandPositionDifference = VivePose.GetPose(roleR).pos - VivePose.GetPose(roleL).pos;
                }
            }

            void OnDisable()
            {
                _canScale = false;
                if (scaleIndicator) scaleIndicator.enabled = false;
            }

            void OnRenderObject()
            {
                var vrOrigin = BetterVRPluginHelper.VROrigin?.transform;
                if (!vrOrigin) return;

                if (BetterVRPlugin.IsTwoHandedTurnEnabled())
                {
                    Vector3 handPositionDifference = VivePose.GetPose(roleR).pos - VivePose.GetPose(roleL).pos;
                    if (lastHandPositionDifference != null)
                    {
                        Quaternion localRotationDelta =
                            Quaternion.FromToRotation(handPositionDifference, (Vector3)lastHandPositionDifference);

                        if (BetterVRPlugin.AllowVerticalRotation.Value)
                        {
                            vrOrigin.rotation = vrOrigin.rotation * localRotationDelta;
                        }
                        else
                        {
                            vrOrigin.Rotate(0, localRotationDelta.eulerAngles.y, 0, Space.Self);
                        }
                    }
                    lastHandPositionDifference = handPositionDifference;
                }

                scaleIndicator.enabled = canScale;

                if (canScale)
                {
                    var scale = scaleDraggingFactor / VRControllerInput.handDistanceLocal;
                    BetterVRPlugin.PlayerScale = scale;
                    scaleIndicator?.SetText("" + String.Format("{0:0.000}", scale));
                }

                VRControllerInput.RestoreHandMidpointWorldPosition(desiredHandMidpointWorldCoordinates);
            }

            private void InitializeScaleDraggingFactor()
            {
                scaleDraggingFactor = handDistanceLocal * BetterVRPlugin.PlayerScale;
            }

            private static TextMeshPro CreateScaleIndicator()
            {
                var camera = BetterVRPluginHelper.VRCamera;
                if (!camera) return null;
                var textMesh =
                    new GameObject().AddComponent<Canvas>().gameObject.AddComponent<TextMeshPro>();
                textMesh.transform.SetParent(camera.transform);
                textMesh.transform.localPosition = new Vector3(0, 0.25f, 0.75f);
                textMesh.transform.localRotation = Quaternion.identity;
                textMesh.transform.localScale = Vector3.one * 0.1f;
                textMesh.fontSize = 16;
                textMesh.color = Color.blue;
                textMesh.alignment = TextAlignmentOptions.Center;
                return textMesh;
            }
        }

        public class OneHandedWorldGrab : MonoBehaviour
        {
            Transform worldPivot;
            Transform vrOrginPlacer;
            Transform stabilizer;
            Vector3 desiredControllerPosition;

            void Awake()
            {
                (worldPivot = new GameObject().transform).parent = new GameObject("RotationStabilizedController").transform;
                worldPivot.parent.parent = transform;
                worldPivot.parent.localPosition = Vector3.zero;
                worldPivot.parent.localRotation = Quaternion.identity;
                (vrOrginPlacer = new GameObject().transform).parent = new GameObject().transform;
            }

            void OnEnable()
            {
                // Place the world pivot at neutral rotation.
                worldPivot.rotation = Quaternion.identity;
                // Pivot the world around the controller.
                worldPivot.localPosition = Vector3.zero;

                desiredControllerPosition = worldPivot.position;
            }

            void OnRenderObject()
            {
                if (stabilizer) worldPivot.parent.rotation = stabilizer.rotation;

                var vrOrigin = BetterVRPluginHelper.VROrigin;
                if (!vrOrigin) return;

                if (!BetterVRPlugin.IsOneHandedTurnEnabled())
                {
                    worldPivot.rotation = Quaternion.identity;
                }
                else if (!BetterVRPlugin.AllowVerticalRotation.Value)
                {
                    // Remove vertical rotation.
                    var angles = worldPivot.rotation.eulerAngles;
                    worldPivot.rotation = Quaternion.Euler(0, angles.y, 0);
                }

                // Make sure the position and rotation of the vrOriginPlacer's parent is the same as teh world pivot.
                vrOrginPlacer.parent.SetPositionAndRotation(worldPivot.transform.position, worldPivot.transform.rotation);

                // Use vrOrginPlacer to record the current vrOrigin rotation and position
                vrOrginPlacer.SetPositionAndRotation(vrOrigin.transform.position, vrOrigin.transform.rotation);

                // Move the vrOriginPlacer's parent to where the controller should be to see how that affects vrOriginPlacer.
                vrOrginPlacer.parent.SetPositionAndRotation(desiredControllerPosition, Quaternion.identity);

                // Move and rotate vrOrgin to restore the original position and rotation of the controller.
                vrOrigin.transform.SetPositionAndRotation(vrOrginPlacer.position, vrOrginPlacer.rotation);
            }
        }

        public class PreventMovement : MonoBehaviour
        {
            private const float EXPIRATION_TIME = 16;
            private Vector3 persistentPosition;
            private Quaternion persitionRotation;
            private float activeTime;

            void OnEnable()
            {
                persistentPosition = transform.position;
                persitionRotation = transform.rotation;
                activeTime = 0;
            }

            void Update()
            {
                activeTime += Time.deltaTime;

                // Stop attempting to restore camera transform if there is any input that might move the camera.
                if (activeTime > EXPIRATION_TIME ||
                    ViveInput.GetPressEx<HandRole>(HandRole.LeftHand, ControllerButton.Grip) ||
                    ViveInput.GetPressEx<HandRole>(HandRole.RightHand, ControllerButton.Grip) ||
                    Mathf.Abs(BetterVRPluginHelper.GetLeftHandPadStickCombinedOutput().x) > 0.25f ||
                    Mathf.Abs(BetterVRPluginHelper.GetRightHandPadStickCombinedOutput().x) > 0.25f ||
                    !Manager.HSceneManager.isHScene)
                {
                    this.enabled = false;
                    return;
                }

                // Force restoring last known camera transform before animation change
                // since vanilla game erroneously resets camera after changing animation
                // even if the camera init option is toggled off.
                transform.SetPositionAndRotation(persistentPosition, persitionRotation);
            }
        }

        // Attaches a small menu to the hand after long pressing Y/B
        public class MenuAutoGrab : MonoBehaviour
        {
            private static FieldInfo cgMenuField;
            private float BUTTON_PRESS_TIME_THRESHOLD = 0.5f;
            private float leftButtonPressTime = 0;
            private float rightButtonPressTime = 0;
            private Transform controllerCenter;
            private Vector3? originalScale;
            private float? originalLaserWidth;
            private CanvasGroup selectMenu;
            private CanvasGroup menu;
            private Transform selectMenuOriginalTransform;
            private bool isInUse = false;
            internal HandRole handRole { get; private set; } = HandRole.Invalid;
            internal static bool hasMenu { get; private set; } 

            void Awake()
            {
                if (cgMenuField == null) cgMenuField = typeof(HS2VR.OpenUICrtl).GetField("cgMenu", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            void OnRenderObject()
            {
                if (isInUse) VRControllerInput.PlaceTouchModeRaycasters();
            }

            void Update()
            {
                if (menu == null)
                {
                    // The controller manager might become stale later, clear the cache.
                    controllerManager = null;
                    var ctrl = GetComponent<HS2VR.OpenUICrtl>();
                    if (ctrl != null) menu = (CanvasGroup)cgMenuField.GetValue(ctrl);
                    if (menu == null) menu = selectMenu = FindSelectMenu();
                }

                var camera = BetterVRPluginHelper.VRCamera;

                if (menu == null || camera == null)
                {
                    // Allow toggling right hand laser in title scene.
                    if (CanOpenMenuByGesture(HandRole.LeftHand))
                    {
                        controllerManager?.SetRightLaserPointerActive(true);
                        controllerManager?.UpdateActivity();
                    }
                    else if (CanOpenMenuByGesture(HandRole.RightHand))
                    {
                        controllerManager?.SetLeftLaserPointerActive(true);
                        controllerManager?.UpdateActivity();
                    }
                    else if (CanCloseMenuByGesture(HandRole.LeftHand) || CanCloseMenuByGesture(HandRole.RightHand))
                    {
                        controllerManager?.SetLeftLaserPointerActive(false);
                        controllerManager?.SetRightLaserPointerActive(false);
                        controllerManager?.UpdateActivity();
                    }
                    // The controller manager might become stale later, clear the cache.
                    controllerManager = null;
                    hasMenu = false;
                    return;
                }
                hasMenu = true;

                if (ViveInput.GetPressEx<HandRole>(HandRole.LeftHand, ControllerButton.Menu) || CanOpenMenuByGesture(HandRole.LeftHand))
                {
                    leftButtonPressTime += Time.deltaTime;
                }
                else
                {
                    leftButtonPressTime = 0;
                }

                if (ViveInput.GetPressEx<HandRole>(HandRole.RightHand, ControllerButton.Menu) || CanOpenMenuByGesture(HandRole.RightHand))
                {
                    rightButtonPressTime += Time.deltaTime;
                }
                else
                {
                    rightButtonPressTime = 0;
                }

                if (isInUse) 
                {
                    if (menu.alpha < 0.9f)
                    {
                        // Reset menu scale to vanilla size and close it.
                        if (originalScale != null && menu != selectMenu) menu.transform.localScale = (Vector3)originalScale;
                        if (originalLaserWidth != null) SetLaserWidths((float)originalLaserWidth);
                        isInUse = false;

                        controllerManager?.SetLeftLaserPointerActive(false);
                        controllerManager?.SetRightLaserPointerActive(false);
                        controllerManager?.UpdateActivity();
                        handRole = HandRole.Invalid;
                        VRControllerInput.RestoreRaycasters();
                        return;
                    }
                }

                var previousHandRole = handRole;
                if (leftButtonPressTime >= BUTTON_PRESS_TIME_THRESHOLD)
                {
                    handRole = HandRole.LeftHand;
                    controllerCenter = BetterVRPluginHelper.leftControllerCenter;
                }
                else if (rightButtonPressTime >= BUTTON_PRESS_TIME_THRESHOLD)
                {
                    handRole = HandRole.RightHand;
                    controllerCenter = BetterVRPluginHelper.rightControllerCenter;                    
                }
                else
                {
                    if (CanCloseMenuByGesture(HandRole.LeftHand) || CanCloseMenuByGesture(HandRole.RightHand))
                    {
                        if (menu != selectMenu)
                        {
                            menu.Enable(false);
                        }
                        else if (selectMenuOriginalTransform != null)
                        {
                            controllerManager?.SetLeftLaserPointerActive(false);
                            controllerManager?.SetRightLaserPointerActive(false);
                            controllerManager?.UpdateActivity();
                            menu.transform.SetPositionAndRotation(selectMenuOriginalTransform.position, selectMenuOriginalTransform.rotation);
                            menu.transform.localScale = selectMenuOriginalTransform.localScale;
                        }
                    }
                    handRole = HandRole.Invalid;
                }

                if (handRole == HandRole.Invalid || !controllerCenter) return;

                isInUse = true;

                if (handRole != previousHandRole)
                {
                    // Open the menu.
                    menu.Enable(true, true, false);

                    // Scale to the right size.
                    if (menu != selectMenu) {
                        if (originalScale == null) originalScale = menu.transform.localScale;
                    }
                    else if (selectMenuOriginalTransform == null)
                    {
                        selectMenuOriginalTransform = new GameObject("SelectMenuOriginalTransform").transform;
                        selectMenuOriginalTransform.parent = selectMenu.transform;
                        selectMenuOriginalTransform.localPosition = Vector3.zero;
                        selectMenuOriginalTransform.localRotation = Quaternion.identity;
                        selectMenuOriginalTransform.localScale = Vector3.one;
                        selectMenuOriginalTransform.SetParent(selectMenu.transform.parent, true);
                    }
                    Vector3 newScale = controllerCenter.lossyScale / 4096f;
                    menu.transform.localScale =
                        menu.transform.parent == null ? newScale : newScale / menu.transform.parent.lossyScale.x;

                    SetLaserWidths(BetterVRPlugin.PlayerScale / 2f);

                    if (inHandTrackingMode)
                    {
                        controllerManager?.SetLeftLaserPointerActive(true);
                        controllerManager?.SetRightLaserPointerActive(true);
                    }
                    else
                    {
                        // Hide the laser on the laser hand and show the laser on the other hand.
                        controllerManager?.SetLeftLaserPointerActive(handRole != HandRole.LeftHand);
                        controllerManager?.SetRightLaserPointerActive(handRole != HandRole.RightHand);
                    }
                    controllerManager?.UpdateActivity();
                }

                if (CanOpenMenuByGesture(HandRole.LeftHand)) {
                    var glove = BetterVRPluginHelper.leftGlove.transform;
                    // Move the menu with the hand.
                    menu.transform.SetPositionAndRotation(
                        glove.TransformPoint(-3, -1, 0),
                        glove.rotation * Quaternion.Euler(-45, -90, 180));
                } 
                else if (CanOpenMenuByGesture(HandRole.RightHand))
                {
                    var glove = BetterVRPluginHelper.rightGlove.transform;
                    // Move the menu with the hand.
                    menu.transform.SetPositionAndRotation(
                        glove.TransformPoint(3, -1, 0),
                        glove.rotation * Quaternion.Euler(-45, 90, 180));
                }
                else if (ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.Menu))
                {
                    // Move the menu with the hand.
                    menu.transform.SetPositionAndRotation(
                        controllerCenter.TransformPoint(0, 1f / 32, 3f / 16),
                        controllerCenter.rotation * Quaternion.Euler(90, 0, 0));
                }
            }

            internal static bool CanClickByTouch(HandRole handRole)
            {
                return InTouchMode(handRole) && ViveInput.GetPressEx<HandRole>(handRole, ControllerButton.Grip);
            }

            internal static bool InTouchMode(HandRole handRole)
            {
                if (!VRControllerInput.inHandTrackingMode || !hasMenu) return false;
                var otherHandRole = handRole == HandRole.LeftHand ? HandRole.RightHand : HandRole.LeftHand;
                return !TightGrip(otherHandRole);
            }

            private CanvasGroup FindSelectMenu()
            {
                var systemButton = GameObject.Find("btnOption");
                if (systemButton == null) return null;
                for (var t = systemButton.transform.parent; t != null; t = t.parent)
                {
                    if (t.name == "MainCanvas") return t.GetComponent<CanvasGroup>();
                }
                return null;
            }

            private void SetLaserWidths(float width)
            {
                SetLaserWidth(BetterVRPluginHelper.GetLeftHand(), (float)width);
                SetLaserWidth(BetterVRPluginHelper.GetRightHand(), (float)width);
            }

            private void SetLaserWidth(GameObject hand, float width)
            {
                if (hand == null) return;
                var lineRenderer = hand.transform.Find("LaserPointer")?.GetComponentInChildren<LineRenderer>(true);
                if (lineRenderer == null) return;
                if (originalLaserWidth == null) originalLaserWidth = lineRenderer.widthMultiplier;
                lineRenderer.widthMultiplier = width;
            }
        }
    }
}
