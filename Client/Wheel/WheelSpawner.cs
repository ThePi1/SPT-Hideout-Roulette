using EFT;
using UnityEngine;

namespace HideoutRoulette.Client.Wheel
{
    /// <summary>
    ///     Keeps a single roulette table in the hideout scene at the position in the plugin config
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

            if (_table == null)
            {
                _table = TableFactory.Create();
                if (_table == null)
                {
                    NotificationManagerClass.DisplayWarningNotification(
                        $"Roulette: table model could not be loaded, check {TableAssets.BundleFileName} is in the plugin folder"
                    );
                }
            }

            ApplyPlacement();
        }

        /// <summary>
        ///     Move the table to the configured position, also runs when the settings are edited or reset
        /// </summary>
        public static void ApplyPlacement()
        {
            // Unity null check, the table is destroyed when the hideout unloads
            if (_table == null)
            {
                return;
            }

            _table.transform.SetPositionAndRotation(Plugin.TablePosition.Value, Quaternion.Euler(0f, Plugin.TableYaw.Value, 0f));
        }

        public static void PlaceInFrontOfPlayer()
        {
            // Unity null check, the owner is destroyed when the hideout unloads
            if (_owner == null || _owner.Player == null || TablePlayMode.Current != null)
            {
                return;
            }

            var playerTransform = _owner.Player.gameObject.transform;
            var forward = Vector3.ProjectOnPlane(playerTransform.forward, Vector3.up).normalized;

            // Each assignment moves the table through the settings changed handlers
            Plugin.TablePosition.Value = playerTransform.position + forward * PlacementDistance;
            Plugin.TableYaw.Value = Quaternion.LookRotation(forward, Vector3.up).eulerAngles.y;

            Plugin.Log.LogInfo($"Table moved to {Plugin.TablePosition.Value}, yaw {Plugin.TableYaw.Value}");
        }
    }
}
