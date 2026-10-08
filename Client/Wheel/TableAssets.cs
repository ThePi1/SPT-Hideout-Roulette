using System.IO;
using System.Reflection;
using UnityEngine;

namespace HideoutRoulette.Client.Wheel
{
    /// <summary>
    ///     Modelled assets from the bundle shipped next to the plugin dll
    /// </summary>
    public static class TableAssets
    {
        public const string BundleFileName = "hideoutroulette.bundle";
        private const string TablePrefabName = "RouletteTable";

        // Kept loaded for the life of the game, the prefab is instantiated on every hideout visit
        private static AssetBundle _bundle;
        private static GameObject _tablePrefab;
        private static bool _loadAttempted;

        /// <summary>
        ///     The modelled table, null when the bundle is missing or unreadable
        /// </summary>
        public static GameObject GetTablePrefab()
        {
            if (_loadAttempted)
            {
                return _tablePrefab;
            }

            _loadAttempted = true;

            var path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), BundleFileName);
            if (!File.Exists(path))
            {
                Plugin.Log.LogError($"{BundleFileName} not found next to the plugin");

                return null;
            }

            _bundle = AssetBundle.LoadFromFile(path);
            if (_bundle == null)
            {
                Plugin.Log.LogError($"Could not load {path}");

                return null;
            }

            _tablePrefab = _bundle.LoadAsset<GameObject>(TablePrefabName);
            if (_tablePrefab == null)
            {
                Plugin.Log.LogError($"{BundleFileName} has no {TablePrefabName} prefab");
            }

            return _tablePrefab;
        }

        /// <summary>
        ///     A sound from the bundle by name, null when it is not there
        /// </summary>
        public static AudioClip GetClip(string name)
        {
            // Unity null check, the bundle is only set once the table prefab has been asked for
            var clip = _bundle != null ? _bundle.LoadAsset<AudioClip>(name) : null;
            if (clip == null)
            {
                Plugin.Log.LogWarning($"{BundleFileName} has no sound named {name}");
            }

            return clip;
        }
    }
}
