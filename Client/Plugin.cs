using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HideoutRoulette.Client.Patches;
using HideoutRoulette.Client.Wheel;
using UnityEngine;

namespace HideoutRoulette.Client
{
    [BepInPlugin("com.calculusman.hideoutroulette", "HideoutRoulette", "0.1.0")]
    [BepInDependency("com.SPT.core")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        internal static ConfigEntry<KeyboardShortcut> PlaceWheelKey;
        internal static ConfigEntry<bool> WheelPlaced;
        internal static ConfigEntry<Vector3> WheelPosition;
        internal static ConfigEntry<float> WheelYaw;

        private void Awake()
        {
            Log = Logger;

            PlaceWheelKey = Config.Bind(
                "Placement",
                "Place wheel here",
                new KeyboardShortcut(KeyCode.F9),
                "While walking around the hideout, moves the roulette wheel to the spot in front of you"
            );
            WheelPlaced = Config.Bind("Placement", "Wheel placed", false, "Has the wheel been given a position in the hideout");
            WheelPosition = Config.Bind("Placement", "Wheel position", Vector3.zero, "World position of the wheel in the hideout");
            WheelYaw = Config.Bind("Placement", "Wheel rotation", 0f, "Rotation of the wheel around the vertical axis, in degrees");

            new HideoutPlayerOwnerInitPatch().Enable();
            new HideoutPlayerOwnerTranslateCommandPatch().Enable();
        }

        private void Update()
        {
            if (PlaceWheelKey.Value.IsDown())
            {
                WheelSpawner.PlaceInFrontOfPlayer();
            }
        }
    }
}
