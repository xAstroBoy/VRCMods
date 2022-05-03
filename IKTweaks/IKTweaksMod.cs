using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using IKTweaks;
using MelonLoader;
using RootMotionNew.FinalIK;
using UIExpansionKit.API;
using UIExpansionKit.API.Controls;
using UIExpansionKit.Components;
using UnhollowerBaseLib;
using UnhollowerRuntimeLib;
using UnhollowerRuntimeLib.XrefScans;
using UnityEngine;
using Valve.VR;
using Delegate = Il2CppSystem.Delegate;
using Object = UnityEngine.Object;

[assembly:MelonInfo(typeof(IKTweaksMod), "IKTweaks", "1.0.25", "knah", "https://github.com/knah/VRCMods")]
[assembly:MelonGame("VRChat", "VRChat")]
[assembly:MelonOptionalDependencies("UIExpansionKit")]

namespace IKTweaks
{
    internal partial class IKTweaksMod : MelonMod
    {
        private static readonly Queue<Action> ourToMainThreadQueue = new Queue<Action>();
        private static readonly Queue<Action> ourToIKLateUpdateQueue = new Queue<Action>();
        private static Func<VRCAvatarManager, float> ourGetEyeHeightDelegate;
        
        internal static GameObject ourRandomPuck;
        private GameObject myAfkIcon;

        public override void OnApplicationStart()
        {
            if (!CheckWasSuccessful || !MustStayTrue || MustStayFalse) return;
            
            IkTweaksSettings.RegisterSettings();

            BundleHolder.Init();

            ClassInjector.RegisterTypeInIl2Cpp<VRIK_New>();
            ClassInjector.RegisterTypeInIl2Cpp<TwistRelaxer_New>();

            VrIkHandling.HookVrIkInit(HarmonyInstance);
            FullBodyHandling.HookFullBodyController(HarmonyInstance);
            
            Camera.onPreRender = Delegate.Combine(Camera.onPreRender, (Camera.CameraCallback) OnVeryLateUpdate).Cast<Camera.CameraCallback>();
            
            DoAfterUiManagerInit(OnUiManagerInit);

            ourGetEyeHeightDelegate = (Func<VRCAvatarManager, float>) System.Delegate.CreateDelegate(typeof(Func<VRCAvatarManager, float>), typeof(VRCAvatarManager)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Single(it =>
                    it.GetParameters().Length == 0 && it.ReturnType == typeof(float) && !it.Name.Contains("_PDM") &&
                    XrefScanner.UsedBy(it).Any(jt => jt.TryResolve()?.DeclaringType == typeof(IkController))));

            HarmonyInstance.Patch(typeof(VRCAvatarManager)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Single(it =>
                    it.GetParameters().Length == 0 && it.ReturnType == typeof(float) && !it.Name.Contains("_PDM") &&
                    XrefScanner.UsedBy(it).Any(jt =>
                        jt.TryResolve()?.DeclaringType == typeof(VRCTrackingManager))),
                new HarmonyMethod(typeof(IKTweaksMod), nameof(WingspanPatch)));
            
            if (MelonHandler.Mods.Any(it => it.Info.Name == "UI Expansion Kit" && !it.Info.Version.StartsWith("0.1."))) 
                AddUixActions();
        }
        
        internal static float GetEyeHeight(VRCAvatarManager manager) => ourGetEyeHeightDelegate(manager);

        private static bool WingspanPatch(VRCAvatarManager __instance, ref float __result)
        {
            switch (IkTweaksSettings.MeasureModeParsed)
            {
                case MeasureAvatarMode.Default:
                    return true;
                case MeasureAvatarMode.Height:
                    __result = ourGetEyeHeightDelegate(__instance) * 0.4537f;
                    return false;
                case MeasureAvatarMode.ImprovedWingspan:
                {
                    var avatarRoot = __instance.transform.Find("Avatar");
                    if (avatarRoot == null) return true;
                    var animator = avatarRoot.GetComponent<Animator>();
                    if (animator == null || !animator.isHuman) return true;
                    var leftHand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
                    var rightHand = animator.GetBoneTransform(HumanBodyBones.RightHand);
                    var leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                    var rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                    var leftElbow = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
                    var rightElbow = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);

                    if (leftHand == null || rightHand == null || leftUpperArm == null || rightUpperArm == null ||
                        leftElbow == null || rightElbow == null)
                        return true;

                    var leftElbowPos = leftElbow.position;
                    var rightElbowPos = rightElbow.position;
                    var leftShoulderPos = leftUpperArm.position;
                    var rightShoulderPos = rightUpperArm.position;
                    var measuredRawWingspan = Vector3.Distance(leftHand.position, leftElbowPos) +
                                              Vector3.Distance(rightHand.position, rightElbowPos) +
                                              Vector3.Distance(leftElbowPos, leftShoulderPos) +
                                              Vector3.Distance(rightElbowPos, rightShoulderPos) +
                                              Vector3.Distance(leftShoulderPos, rightShoulderPos);

                    // this measured wingspan doesn't include hand-to-fingertip length, but eye height doesn't include the rest of the height above eyes either
                    __result = measuredRawWingspan * 0.4537f * Mathf.Clamp(IkTweaksSettings.WingspanMeasurementAdjustFactor.Value, 0.1f, 2.0f);
                    return false;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AddUixActions()
        {
            ExpansionKitApi.GetExpandedMenu(ExpandedMenu.SettingsMenu).AddSimpleButton("More IKTweaks...", ShowIKTweaksMenu);

            ExpansionKitApi.RegisterSettingAsStringEnum(IkTweaksSettings.IkTweaksCategory,
                nameof(IkTweaksSettings.IgnoreAnimationsMode),
                new[]
                {
                    (nameof(IgnoreAnimationsMode.None), "Play all animations"),
                    (nameof(IgnoreAnimationsMode.Head), "Ignore head animations"),
                    (nameof(IgnoreAnimationsMode.Hands), "Ignore hands animations"),
                    (nameof(IgnoreAnimationsMode.HandAndHead), "Ignore head and hands"),
                    (nameof(IgnoreAnimationsMode.All), "Ignore all (always slide around)")
                });

            ExpansionKitApi.RegisterSettingAsStringEnum(IkTweaksSettings.IkTweaksCategory, nameof(IkTweaksSettings.MeasureMode), new[]
            {
                (nameof(MeasureAvatarMode.Default), "VRC default"),
                (nameof(MeasureAvatarMode.ImprovedWingspan), "Wingspan (accurate)"),
                (nameof(MeasureAvatarMode.Height), "Height"),
            });
        }

        private static void ShowIKTweaksMenu()
        {
            var menu = ExpansionKitApi.CreateCustomFullMenuPopup(LayoutDescription.WideSlimList);

            menu.AddSimpleButton("Clear per-avatar stored calibrations", CalibrationManager.ClearNonUniversal);
            menu.AddSpacer();
            menu.AddSimpleButton("Open documentation in browser", () => Process.Start("https://github.com/knah/VRCMods#iktweaks"));
            menu.AddSpacer();

            menu.AddSimpleButton("Adjust hand offsets",
                () => ShowHandsCalibrationMenu(IkTweaksSettings.HandPositionOffset, IkTweaksSettings.DefaultHandOffset,
                    0.001f, "Offsets:", CalibrationManager.ApplyHandOffsets));
            menu.AddSimpleButton("Adjust hand angles",
                () => ShowHandsCalibrationMenu(IkTweaksSettings.HandAngleOffset, IkTweaksSettings.DefaultHandAngle, 1,
                    "Angles:", CalibrationManager.ModifyStoredHandAnglesAndApply));
            menu.AddSpacer();
            menu.AddSimpleButton("Close", menu.Hide);
            
            menu.Show();
        }

        private static void ShowHandsCalibrationMenu(MelonPreferences_Entry<Vector3> entry, Vector3 defaultValue, float moveStep, string label, Action<Vector3, Vector3> apply)
        {
            var menu = ExpansionKitApi.CreateCustomFullMenuPopup(LayoutDescription.QuickMenu4Columns);

            var highPrecisionMoves = true;
            var offset = entry.Value;
            var prevOffset = offset;
            
            void SetHandAngles()
            {
                apply(prevOffset, offset);
                prevOffset = offset;
            }
            
            menu.OnContentRootCreated += go =>
            {
                (go.GetComponent<EnableDisableListener>() ?? go.AddComponent<EnableDisableListener>()).OnDisabled +=
                    () =>
                    {
                        MelonDebug.Msg("Menu closed, cleaning up");

                        entry.Value = offset;
                        IkTweaksSettings.Category.SaveToFile();
                    };
            };

            IMenuLabel xLabel = null;
            IMenuLabel yLabel = null;
            IMenuLabel zLabel = null;

            void DoMove(Vector3 direction)
            {
                offset += direction * (highPrecisionMoves ? moveStep : moveStep * 10);
                SetHandAngles();

                xLabel.SetText($"X:\n{offset.x:F3}");
                yLabel.SetText($"Y:\n{offset.y:F3}");
                zLabel.SetText($"Z:\n{offset.z:F3}");
            }
            
            menu.AddSimpleButton("+Y", () => DoMove(Vector3.up));
            menu.AddSimpleButton("+Z", () => DoMove(Vector3.forward));
            menu.AddSimpleButton("+X", () => DoMove(Vector3.right));
            menu.AddToggleButton("High precision", b => highPrecisionMoves = b, () => highPrecisionMoves);

            menu.AddSimpleButton("-Y", () => DoMove(Vector3.down));
            menu.AddSimpleButton("-Z", () => DoMove(Vector3.back));
            menu.AddSimpleButton("-X", () => DoMove(Vector3.left));
            menu.AddSpacer();

            menu.AddLabel(label).SetAnchor(TextAnchor.MiddleCenter);
            xLabel = menu.AddLabel("X:").SetAnchor(TextAnchor.MiddleCenter);
            yLabel = menu.AddLabel("Y:").SetAnchor(TextAnchor.MiddleCenter);
            zLabel = menu.AddLabel("Z:").SetAnchor(TextAnchor.MiddleCenter);

            menu.AddSimpleButton("Reset", () =>
            {
                offset = defaultValue;
                DoMove(Vector3.zero);
            });
            menu.AddSpacer();
            menu.AddSpacer();
            menu.AddSimpleButton("Back", menu.Hide);
            
            menu.Show(true);
            
            DoMove(Vector3.zero);
        }

        private static void CalibratePrefix(MethodBase __originalMethod)
        {
            MelonDebug.Msg("Called calibrate from " + __originalMethod);
            
            if (!IkTweaksSettings.FullBodyVrIk.Value) return;
            
            MelonLogger.Msg("Clearing stored calibrations due to calibrate button press");
            
            if (IkTweaksSettings.CalibrateUseUniversal.Value)
                CalibrationManager.Clear();
            else
                CalibrationManager.Clear(VRCPlayer.field_Internal_Static_VRCPlayer_0.prop_VRCAvatarManager_0.field_Private_ApiAvatar_0.id);
        }

        public void OnUiManagerInit()
        {
            foreach (var methodInfo in typeof(VRCTrackingManager).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                if (!methodInfo.Name.StartsWith("Method_Public_Virtual_Final_New_Void_") || methodInfo.GetParameters().Length != 0) continue;

                var callees = XrefScanner.XrefScan(methodInfo).Where(it => it.Type == XrefType.Method)
                    .Select(it => it.TryResolve()).Where(it => it != null).ToList();

                if (callees.Count != 1) continue;
                if (callees[0].DeclaringType != typeof(VRCTrackingManager) || callees[0] is not MethodInfo mi || mi.ReturnType != typeof(bool))
                    continue;
                
                HarmonyInstance.Patch(methodInfo, new HarmonyMethod(typeof(IKTweaksMod), nameof(CalibratePrefix)));
            }

            var steamVrControllerManager = CalibrationManager.GetControllerManager();
            var puckPrefab = steamVrControllerManager.field_Public_ArrayOf_GameObject_0.First(it =>
                it != steamVrControllerManager.field_Public_GameObject_1 && it != steamVrControllerManager.field_Public_GameObject_0);
            var newPucks = new Il2CppReferenceArray<GameObject>(5 + 6);
            var newUints = new Il2CppStructArray<uint>(5 + 6);
            for (var i = 0; i < 5; i++)
            {
                newPucks[i] = steamVrControllerManager.field_Public_ArrayOf_GameObject_0[i];
                newUints[i] = steamVrControllerManager.field_Private_ArrayOf_UInt32_0[i];
            }

            ourRandomPuck = puckPrefab;

            var trackersParent = puckPrefab.transform.parent;
            for (var i = 0; i < 6; i++)
            {
                var newPuck = Object.Instantiate(puckPrefab, trackersParent, true);
                newPuck.name = "Puck" + (i + 4);
                newPuck.GetComponent<SteamVR_TrackedObject>().field_Public_EIndex_0 = SteamVR_TrackedObject.EIndex.None;
                newPuck.SetActive(false);
                newPucks[i + 5] = newPuck;
                newUints[i + 5] = UInt32.MaxValue;
            }

            steamVrControllerManager.field_Public_ArrayOf_GameObject_0 = newPucks;
            steamVrControllerManager.field_Private_ArrayOf_UInt32_0 = newUints;

            // only one of them is the correct type, so just try all of them 
            steamVrControllerManager.field_Private_Action_0.TryCast<SteamVR_Events.Action<VREvent_t>>()?.action?.Invoke(new VREvent_t());
            steamVrControllerManager.field_Private_Action_1.TryCast<SteamVR_Events.Action<VREvent_t>>()?.action?.Invoke(new VREvent_t());
            steamVrControllerManager.field_Private_Action_2.TryCast<SteamVR_Events.Action<VREvent_t>>()?.action?.Invoke(new VREvent_t());

            myAfkIcon = GameObject.Find("UserInterface/UnscaledUI/HudContent/Hud/AFK").transform.Find("Icon").gameObject;
        }

        public static bool IsAfk { get; private set; }

        private int myAfkUpdateCounter = 45;
        private static bool ourHadUpdateThisFrame = false;
        public override void OnUpdate()
        {
            VrIkHandling.Update();
            ourHadUpdateThisFrame = false;
            // Only check AFK icon once per 45 frames - integer math is fast, touching il2cpp is slow
            if (myAfkUpdateCounter-- > 0) return;
            
            myAfkUpdateCounter = 45;
            if (!ReferenceEquals(myAfkIcon, null)) 
                IsAfk = myAfkIcon.activeSelf;

        }

        public void OnVeryLateUpdate(Camera _)
        {
            if (ourHadUpdateThisFrame) return;
            
            ourHadUpdateThisFrame = true;

            ProcessQueue(ourToMainThreadQueue);
        }

        internal static void ProcessIKLateUpdateQueue()
        {
            ProcessQueue(ourToIKLateUpdateQueue);
        }

        private static void ProcessQueue(Queue<Action> queue)
        {
            var toRun = queue.ToList();
            queue.Clear();

            foreach (var action in toRun)
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    MelonLogger.Error(ex.ToString());
                }
            }
        }

        public static YieldVeryLateUpdateAwaitable AwaitVeryLateUpdate() => new(ourToMainThreadQueue);
        public static YieldVeryLateUpdateAwaitable AwaitIKLateUpdate() => new(ourToIKLateUpdateQueue);

        public struct YieldVeryLateUpdateAwaitable : INotifyCompletion
        {
            private readonly Queue<Action> myQueue;

            public YieldVeryLateUpdateAwaitable(Queue<Action> queue)
            {
                myQueue = queue;
            }

            public bool IsCompleted => false;

            public YieldVeryLateUpdateAwaitable GetAwaiter() => this;

            public void GetResult() { }

            public void OnCompleted(Action continuation)
            {
                myQueue.Enqueue(continuation);
            }
        }
    }
}