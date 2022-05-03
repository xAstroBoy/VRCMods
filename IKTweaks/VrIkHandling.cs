using System;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using MelonLoader;
using RootMotion.FinalIK;
using UnhollowerBaseLib;
using UnhollowerRuntimeLib.XrefScans;
using VRC.Core;

namespace IKTweaks
{
    public static class VrIkHandling
    {
        internal static VRIK LastInitializedIk;

        internal static void Update()
        {
            if (LastInitializedIk != null) 
                ApplyVrIkSettings(LastInitializedIk);
        }

        public static void HookVrIkInit(HarmonyLib.Harmony harmony)
        {
            var vrikInitMethod = typeof(VRCVrIkController).GetMethod(nameof(VRCVrIkController
                .Method_Public_Virtual_Final_New_Boolean_VRC_AnimationController_Animator_VRCPlayer_Boolean_0))!;

            NativePatchUtils.NativePatch(vrikInitMethod, out ourOriginalVrIkInit, VrIkInitReplacement);
        }
        
        private delegate byte VrIkInit(IntPtr a, IntPtr b, IntPtr c, IntPtr d, byte e, IntPtr n);

        private static VrIkInit ourOriginalVrIkInit;

        private static byte VrIkInitReplacement(IntPtr thisPtr, IntPtr vrcAnimController, IntPtr animatorPtr, IntPtr playerPtr, byte isLocalPlayer, IntPtr nativeMethod)
        {
            var __instance = new VRCVrIkController(thisPtr);
            var __2 = playerPtr == IntPtr.Zero ? null : new VRCPlayer(playerPtr);
            var result = ourOriginalVrIkInit(thisPtr, vrcAnimController, animatorPtr, playerPtr, isLocalPlayer, nativeMethod);
            VrikInitPatch(__instance, __2);
            return result;
        }

        private static void VrikInitPatch(VRCVrIkController __instance, VRCPlayer? __2)
        {
            if (__2 != null && __2.prop_Player_0?.prop_APIUser_0?.id == APIUser.CurrentUser?.id)
            {
                FullBodyHandling.LastCalibrationWasInCustomIk = false;
                LastInitializedIk = __instance.field_Private_VRIK_0;
            }
        }

        private static void ApplyVrIkSettings(VRIK ik)
        {
            var ikSolverVr = ik.solver;
            var shoulderMode = IkTweaksSettings.ShoulderMode;
            ikSolverVr.leftArm.shoulderRotationMode = shoulderMode;
            ikSolverVr.rightArm.shoulderRotationMode = shoulderMode;
            ikSolverVr.plantFeet = IkTweaksSettings.PlantFeet.Value;
            ikSolverVr.spine.headClampWeight = IkTweaksSettings.Unrestrict3PointHeadRotation.Value ? 0 : 0.6f;
        }
    }
}