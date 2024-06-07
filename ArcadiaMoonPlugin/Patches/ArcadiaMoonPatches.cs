using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace ArcadiaMoonPlugin.Patches
{
    [HarmonyPatch(typeof(PlayerControllerB))]
    internal class PlayerControllerBPatch
    {
        [HarmonyPatch("Update")]
        [HarmonyPostfix]
        static void HeatzoneStrokePatch(ref float ___sprintMeter, ref float ___sprintTime)
        {
            float severity = PlayerHeatEffects.GetHeatSeverity();
            if (severity > 0)
            {
                // Gradually decrease the maximum sprintMeter value from 1 to 0.6
                float maxSprintMeter = Mathf.Lerp(1f, 0.6f, severity);
                ___sprintMeter = Mathf.Clamp(___sprintMeter, 0, maxSprintMeter);

                // Gradually decrease the maximum sprintTime value from 5 to 2.5
                float maxSprintTime = Mathf.Lerp(5f, 2.5f, severity);
                ___sprintTime = Mathf.Clamp(___sprintTime, 0, maxSprintTime);

                if (severity == 1)
                {
                    Debug.Log("Player has reached maximum heat exhaustion!");
                }
            }
        }
    }
}
