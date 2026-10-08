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
        // Where the table lives unless the player moves it
        private static readonly Vector3 _defaultPosition = new Vector3(0.85f, 0.01f, -0.75f);
        private const float DefaultYaw = 0f;

        internal static ManualLogSource Log;

        internal static ConfigEntry<Vector3> TablePosition;
        internal static ConfigEntry<float> TableYaw;
        internal static ConfigEntry<KeyboardShortcut> MoveTableKey;
        internal static ConfigEntry<float> CameraAngle;
        internal static ConfigEntry<float> Volume;

        private void Awake()
        {
            Log = Logger;

            // Section and key names are kept from the first builds so existing configs carry over
            TablePosition = Config.Bind("Placement", "Wheel position", _defaultPosition, "World position of the roulette table in the hideout");
            TableYaw = Config.Bind("Placement", "Wheel rotation", DefaultYaw, "Rotation of the roulette table around the vertical axis, in degrees");
            MoveTableKey = Config.Bind(
                "Placement",
                "Place wheel here",
                KeyboardShortcut.Empty,
                "Unbound by default. While walking around the hideout, moves the roulette table to the spot in front of you. "
                    + "Reset the position and rotation settings to put it back."
            );

            CameraAngle = Config.Bind(
                "Camera",
                "Camera angle",
                45f,
                new ConfigDescription(
                    "Tilt of the camera while playing, in degrees. 0 looks straight down at the table, higher values view it from the players side.",
                    new AcceptableValueRange<float>(0f, 75f)
                )
            );

            Volume = Config.Bind(
                "Sound",
                "Volume",
                0.7f,
                new ConfigDescription("Loudness of the tables sounds, on top of the games own volume settings", new AcceptableValueRange<float>(0f, 1f))
            );

            TablePosition.SettingChanged += (sender, args) => WheelSpawner.ApplyPlacement();
            TableYaw.SettingChanged += (sender, args) => WheelSpawner.ApplyPlacement();

            new HideoutPlayerOwnerInitPatch().Enable();
            new HideoutPlayerOwnerTranslateCommandPatch().Enable();
        }

        private void Update()
        {
            if (MoveTableKey.Value.IsDown())
            {
                WheelSpawner.PlaceInFrontOfPlayer();
            }
        }
    }
}
