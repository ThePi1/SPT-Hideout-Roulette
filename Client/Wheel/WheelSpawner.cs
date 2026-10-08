using EFT;
using EFT.Communications;
using UnityEngine;

namespace HideoutRoulette.Client.Wheel
{
    /// <summary>
    ///     Keeps a single roulette table in the hideout scene at the position saved in the plugin config
    /// </summary>
    public static class WheelSpawner
    {
        // From the player to the centre of the table, leaves them standing at its near edge
        private const float PlacementDistance = 0.9f;

        private static HideoutPlayerOwner _owner;
        private static RouletteTable _table;

        public static void OnHideoutEntered(HideoutPlayerOwner owner)
        {
            _owner = owner;

            if (!Plugin.WheelPlaced.Value)
            {
                NotificationManagerClass.DisplayMessageNotification(
                    $"Roulette: press {Plugin.PlaceWheelKey.Value} in the hideout to place the wheel",
                    ENotificationDurationType.Long
                );

                return;
            }

            EnsureWheel();
        }

        public static void PlaceInFrontOfPlayer()
        {
            // Unity null check, the owner is destroyed when the hideout unloads
            if (_owner == null || _owner.Player == null)
            {
                return;
            }

            var playerTransform = _owner.Player.gameObject.transform;
            var forward = Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up).normalized;

            Plugin.WheelPosition.Value = playerTransform.position + forward * PlacementDistance;
            Plugin.WheelYaw.Value = Quaternion.LookRotation(forward, Vector3.up).eulerAngles.y;
            Plugin.WheelPlaced.Value = true;

            EnsureWheel();
            Plugin.Log.LogInfo($"Wheel placed at {Plugin.WheelPosition.Value}, yaw {Plugin.WheelYaw.Value}");
        }

        private static void EnsureWheel()
        {
            if (_table == null)
            {
                _table = TableFactory.Create();
            }

            _table.transform.SetPositionAndRotation(Plugin.WheelPosition.Value, Quaternion.Euler(0f, Plugin.WheelYaw.Value, 0f));
        }
    }
}
