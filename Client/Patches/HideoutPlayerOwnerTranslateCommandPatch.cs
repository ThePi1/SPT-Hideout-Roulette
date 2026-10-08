using System.Reflection;
using EFT;
using EFT.InputSystem;
using HarmonyLib;
using HideoutRoulette.Client.Wheel;
using SPT.Reflection.Patching;

namespace HideoutRoulette.Client.Patches
{
    /// <summary>
    ///     While the table is being played, keeps key presses away from the player and makes Escape leave the table
    ///     instead of leaving the first person hideout
    /// </summary>
    public class HideoutPlayerOwnerTranslateCommandPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(HideoutPlayerOwner), nameof(HideoutPlayerOwner.TranslateCommand));
        }

        [PatchPrefix]
        public static bool PatchPrefix(ECommand command, ref InputNode.ETranslateResult __result)
        {
            var playMode = TablePlayMode.Current;
            if (playMode == null)
            {
                return true;
            }

            if (command == ECommand.Escape)
            {
                playMode.Exit();
            }

            __result = InputNode.ETranslateResult.BlockAll;

            return false;
        }
    }
}
