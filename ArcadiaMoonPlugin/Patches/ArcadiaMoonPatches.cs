using GameNetcodeStuff;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace ArcadiaMoonPlugin.Patches
{
    [HarmonyPatch]
    internal class PlayerControllerBHeatStrokePatch
    {
        private static float prevSprintMeter;
        private static float severityMultiplier = 1f;

        [HarmonyPatch(typeof(PlayerControllerB), "Update")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static void HeatStrokePatchPrefix(PlayerControllerB __instance)
        {
            if (!((NetworkBehaviour)__instance).IsOwner || !__instance.isPlayerControlled)
                return;
            PlayerControllerBHeatStrokePatch.prevSprintMeter = __instance.sprintMeter;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "Update")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void HeatStrokePatchPostfix(PlayerControllerB __instance)
        {
            if (!((NetworkBehaviour)__instance).IsOwner || !__instance.isPlayerControlled)
                return;
            float severity = PlayerHeatEffects.GetHeatSeverity();
            
            if (severity > 0)
            {
                float delta = __instance.sprintMeter - PlayerControllerBHeatStrokePatch.prevSprintMeter;
                if (delta < 0.0) //Stamina consumed
                    __instance.sprintMeter = Mathf.Max(PlayerControllerBHeatStrokePatch.prevSprintMeter + delta * (1 + severity * severityMultiplier), 0.0f);
                else if (delta > 0.0) //Stamina regenerated
                    __instance.sprintMeter = Mathf.Min(PlayerControllerBHeatStrokePatch.prevSprintMeter + delta / (1 + severity * severityMultiplier), 1f);
                // uncomment for debugging if needed
                //Debug.Log($"Severity: {severity}, SprintMeter: {__instance.sprintMeter}");
            }
        }

        [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate")]
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static void HeatStrokePatchLatePrefix(PlayerControllerB __instance)
        {
            if (!((NetworkBehaviour)__instance).IsOwner || !__instance.isPlayerControlled)
                return;
            PlayerControllerBHeatStrokePatch.prevSprintMeter = __instance.sprintMeter;
        }

        [HarmonyPatch(typeof(PlayerControllerB), "LateUpdate")]
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Low)]
        private static void HeatStrokePatchLatePostfix(PlayerControllerB __instance)
        {
            if (!((NetworkBehaviour)__instance).IsOwner || !__instance.isPlayerControlled)
                return;
            float severity = PlayerHeatEffects.GetHeatSeverity();

            if (severity > 0)
            {
                float delta = __instance.sprintMeter - PlayerControllerBHeatStrokePatch.prevSprintMeter;
                if (delta < 0.0) //Stamina consumed
                    __instance.sprintMeter = Mathf.Max(PlayerControllerBHeatStrokePatch.prevSprintMeter + delta * (1 + severity * severityMultiplier), 0.0f);
                else if (delta > 0.0) //Stamina regenerated
                    __instance.sprintMeter = Mathf.Min(PlayerControllerBHeatStrokePatch.prevSprintMeter + delta / (1 + severity * severityMultiplier), 1f);
                // uncomment for debugging if needed
                //Debug.Log($"Severity: {severity}, SprintMeter: {__instance.sprintMeter}");

            }
        }

        
    }
}


