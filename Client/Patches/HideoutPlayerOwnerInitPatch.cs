using System.Reflection;
using EFT;
using HarmonyLib;
using HideoutRoulette.Client.Wheel;
using SPT.Reflection.Patching;

namespace HideoutRoulette.Client.Patches
{
    /// <summary>
    ///     Runs once the first person hideout player exists, which is when the wheel can be put in the scene
    /// </summary>
    public class HideoutPlayerOwnerInitPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HideoutPlayerOwner), nameof(HideoutPlayerOwner.Init));
        }

        [PatchPostfix]
        public static void PatchPostfix(HideoutPlayerOwner __instance)
        {
            WheelSpawner.OnHideoutEntered(__instance);
        }
    }
}
