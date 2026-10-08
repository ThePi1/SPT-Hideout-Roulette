using System.Collections.Generic;
using UnityEngine;

namespace HideoutRoulette.Client.Wheel
{
    /// <summary>
    ///     Builds a playable table from the modelled prefab: adds colliders, betting spots and the play camera, and hooks
    ///     the moving parts up to RouletteTable. Sizes come from marker objects in the model, see Art/roulette_table.py.
    ///     Local space: origin on the floor under the table centre, the player stands on the -Z side with +X to their right.
    /// </summary>
    public static class TableFactory
    {
        private const int ChipTiers = 4;

        // Slightly inside the rail, so the player can stand right up against the table
        private const float CollisionInset = 0.02f;

        private const float SpotHeight = 0.03f;
        private const float SpotGap = 0.004f;
        private const float WheelSpotRadius = 0.37f;
        private const float WheelSpotHeight = 0.18f;
        private const float WheelRimAboveFelt = 0.1f;

        // From the outside of the table to where swept chips end up
        private const float EdgeInset = 0.09f;

        // From the centre of the table top, far enough to fit the whole table on a 16:10 screen
        private const float CameraDistance = 1.5f;

        private static readonly string[] _requiredParts =
        {
            "Rotor", "Ball", "Dolly", "TableMin", "TableMax", "LayoutOrigin", "LayoutFarCorner", "WheelCentre", "BallTrack",
        };

        /// <returns>The table, or null when the model could not be loaded</returns>
        public static RouletteTable Create()
        {
            var prefab = TableAssets.GetTablePrefab();
            if (prefab == null)
            {
                return null;
            }

            // Created at the world origin, so world positions read below are also positions in the tables local space
            var rootObject = new GameObject("HideoutRouletteTable");
            var root = rootObject.transform;
            var model = Object.Instantiate(prefab, root, false);
            model.name = "Model";

            var parts = new Dictionary<string, Transform>();
            foreach (var part in model.GetComponentsInChildren<Transform>(true))
            {
                parts[part.name] = part;
            }

            if (!HasAllParts(parts))
            {
                Object.Destroy(rootObject);

                return null;
            }

            UseGameShader(model);

            var table = rootObject.AddComponent<RouletteTable>();
            var interactiveLayer = LayerMask.NameToLayer("Interactive");
            var tableMin = parts["TableMin"].position;
            var tableMax = parts["TableMax"].position;
            var feltHeight = parts["LayoutOrigin"].position.y;

            BuildCollision(root, tableMin, tableMax, feltHeight);
            BuildSpots(root, table, parts["LayoutOrigin"].position, parts["LayoutFarCorner"].position, interactiveLayer);
            BuildWheel(root, table, parts, interactiveLayer);
            BuildProps(root, table, parts);
            BuildAudio(rootObject, table);

            // Just inside the rail on either side
            table.NearEdgeZ = tableMin.z + EdgeInset;
            table.FarEdgeZ = tableMax.z - EdgeInset;
            BuildPlayMode(root, table, new Vector3((tableMin.x + tableMax.x) / 2f, feltHeight, (tableMin.z + tableMax.z) / 2f));

            return table;
        }

        private static bool HasAllParts(Dictionary<string, Transform> parts)
        {
            var names = new List<string>(_requiredParts);
            for (var number = 0; number < RouletteLayout.PocketCount; number++)
            {
                names.Add($"Pocket_{number}");
            }

            for (var tier = 0; tier < ChipTiers; tier++)
            {
                names.Add($"Chip_{tier}");
            }

            foreach (var name in names)
            {
                if (!parts.ContainsKey(name))
                {
                    Plugin.Log.LogError($"Roulette table model has no part named {name}");

                    return false;
                }
            }

            return true;
        }

        /// <summary>
        ///     Swap bundled materials onto the games own copy of the shader, which is known to render correctly in the hideout
        /// </summary>
        private static void UseGameShader(GameObject model)
        {
            var shader = Shader.Find("Standard");
            if (shader == null)
            {
                Plugin.Log.LogWarning("Game has no Standard shader, keeping the shaders from the asset bundle");

                return;
            }

            // Shared materials, so chips copied from the templates later use them too
            foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
            {
                foreach (var material in renderer.sharedMaterials)
                {
                    material.shader = shader;
                }
            }
        }

        /// <summary>
        ///     Stops the player walking through the table. On the layer the games character movement collides with,
        ///     which the interaction ray ignores.
        /// </summary>
        private static void BuildCollision(Transform root, Vector3 tableMin, Vector3 tableMax, float feltHeight)
        {
            var collision = new GameObject("Collision");
            collision.layer = LayerMask.NameToLayer("LowPolyCollider");
            collision.transform.SetParent(root, false);

            var height = feltHeight - 0.04f;
            var box = collision.AddComponent<BoxCollider>();
            box.center = new Vector3((tableMin.x + tableMax.x) / 2f, height / 2f, (tableMin.z + tableMax.z) / 2f);
            box.size = new Vector3(tableMax.x - tableMin.x - CollisionInset * 2f, height, tableMax.z - tableMin.z - CollisionInset * 2f);
        }

        private static void BuildSpots(Transform root, RouletteTable table, Vector3 layoutOrigin, Vector3 layoutFarCorner, int interactiveLayer)
        {
            var cellWidth = (layoutFarCorner.x - layoutOrigin.x) / RouletteLayout.GridColumns;
            var cellDepth = (layoutFarCorner.z - layoutOrigin.z) / RouletteLayout.GridRows;

            foreach (var definition in RouletteLayout.CreateSpots())
            {
                var spotWidth = definition.ColumnSpan * cellWidth;
                var spotDepth = definition.RowSpan * cellDepth;

                var spotObject = new GameObject($"Spot {definition.Label}");
                spotObject.layer = interactiveLayer;
                spotObject.transform.SetParent(root, false);
                spotObject.transform.localPosition = new Vector3(
                    layoutOrigin.x + definition.Column * cellWidth + spotWidth / 2f,
                    layoutOrigin.y,
                    layoutOrigin.z + definition.Row * cellDepth + spotDepth / 2f
                );

                var box = spotObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, SpotHeight / 2f, 0f);
                box.size = new Vector3(spotWidth - SpotGap, SpotHeight, spotDepth - SpotGap);

                var spot = spotObject.AddComponent<BetSpot>();
                spot.Table = table;
                spot.Definition = definition;
                spot.Size = new Vector2(spotWidth, spotDepth);
                table.Spots.Add(spot);
            }
        }

        private static void BuildWheel(Transform root, RouletteTable table, Dictionary<string, Transform> parts, int interactiveLayer)
        {
            var wheelCentre = parts["WheelCentre"].position;
            table.WheelCentre = new Vector3(wheelCentre.x, 0f, wheelCentre.z);

            var wheelSpotObject = new GameObject("Wheel spot");
            wheelSpotObject.layer = interactiveLayer;
            wheelSpotObject.transform.SetParent(root, false);
            wheelSpotObject.transform.localPosition = wheelCentre;
            var box = wheelSpotObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, WheelSpotHeight / 2f, 0f);
            box.size = new Vector3(WheelSpotRadius * 2f, WheelSpotHeight, WheelSpotRadius * 2f);
            wheelSpotObject.AddComponent<WheelSpot>().Table = table;

            // Angles are measured the way RouletteTable places the ball: clockwise from the tables +Z
            var pocketAngles = new float[RouletteLayout.PocketCount];
            for (var number = 0; number < pocketAngles.Length; number++)
            {
                var offset = parts[$"Pocket_{number}"].position - table.WheelCentre;
                pocketAngles[number] = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
                if (number == 0)
                {
                    table.PocketRadius = new Vector2(offset.x, offset.z).magnitude;
                    table.PocketHeight = offset.y;
                }
            }

            var trackOffset = parts["BallTrack"].position - table.WheelCentre;
            table.TrackRadius = new Vector2(trackOffset.x, trackOffset.z).magnitude;
            table.TrackHeight = trackOffset.y;
            table.PocketAngles = pocketAngles;

            // The table turns the pivot, so whatever rotation the rotor was exported with is preserved underneath it
            var pivot = new GameObject("Rotor pivot").transform;
            pivot.SetParent(root, false);
            pivot.localPosition = wheelCentre;
            parts["Rotor"].SetParent(pivot, true);
            parts["Ball"].SetParent(root, true);

            table.Disc = pivot;
            table.Ball = parts["Ball"];
        }

        /// <summary>
        ///     Chips and the win marker. The model holds one of each as a template, hidden until copied or needed.
        /// </summary>
        private static void BuildProps(Transform root, RouletteTable table, Dictionary<string, Transform> parts)
        {
            var chipTemplates = new GameObject[ChipTiers];
            for (var tier = 0; tier < ChipTiers; tier++)
            {
                chipTemplates[tier] = parts[$"Chip_{tier}"].gameObject;
                chipTemplates[tier].SetActive(false);
            }

            table.ChipTemplates = chipTemplates;
            table.ChipThickness = chipTemplates[0].GetComponent<MeshFilter>().sharedMesh.bounds.size.y;

            var dolly = parts["Dolly"];
            dolly.SetParent(root, true);
            dolly.gameObject.SetActive(false);
            table.Dolly = dolly;
        }

        private static void BuildAudio(GameObject rootObject, RouletteTable table)
        {
            var audio = rootObject.AddComponent<TableAudio>();
            audio.BallRolls = new[] { TableAssets.GetClip("BallRoll1"), TableAssets.GetClip("BallRoll2"), TableAssets.GetClip("BallRoll3") };
            audio.BallClack = TableAssets.GetClip("BallClack");
            audio.ChipPlace = TableAssets.GetClip("ChipPlace");
            audio.ChipsSweep = TableAssets.GetClip("ChipsSweep");
            audio.Win = TableAssets.GetClip("Win");
            audio.Lose = TableAssets.GetClip("Lose");
            table.Audio = audio;
        }

        /// <summary>
        ///     Camera, lamp and hover highlight used while the player is betting
        /// </summary>
        private static void BuildPlayMode(Transform root, RouletteTable table, Vector3 cameraPivot)
        {
            // TablePlayMode aims it, straight down here with the far side of the table at the top of the screen
            var cameraObject = new GameObject("Play camera");
            cameraObject.transform.SetParent(root, false);
            cameraObject.transform.localPosition = cameraPivot + Vector3.up * CameraDistance;
            cameraObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 6f;
            camera.depth = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;

            // The players own head and arms would otherwise sit over the near row
            camera.cullingMask = ~(1 << LayerMask.NameToLayer("Player"));

            var lamp = cameraObject.AddComponent<Light>();
            lamp.enabled = false;
            lamp.type = LightType.Spot;
            lamp.spotAngle = 110f;
            lamp.range = CameraDistance + 1.5f;
            lamp.intensity = 2.4f;

            var highlight = GameObject.CreatePrimitive(PrimitiveType.Quad);
            highlight.name = "Highlight";
            Object.Destroy(highlight.GetComponent<Collider>());
            highlight.transform.SetParent(root, false);
            highlight.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            highlight.GetComponent<Renderer>().material = CreateHighlightMaterial();
            highlight.SetActive(false);

            var playMode = root.gameObject.AddComponent<TablePlayMode>();
            playMode.Table = table;
            playMode.Camera = camera;
            playMode.Lamp = lamp;
            playMode.Highlight = highlight.transform;
            playMode.CameraPivot = cameraPivot;
            playMode.CameraDistance = CameraDistance;
            playMode.FeltHeight = cameraPivot.y;
            playMode.WheelRadius = WheelSpotRadius;
            playMode.WheelRimHeight = cameraPivot.y + WheelRimAboveFelt;
            table.PlayMode = playMode;
        }

        private static Material CreateHighlightMaterial()
        {
            var color = new Color(1f, 0.9f, 0.3f, 0.18f);

            // Both of these blend by vertex or material alpha without needing lighting
            var shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                return new Material(shader) { color = color };
            }

            var material = new Material(Shader.Find("Hidden/Internal-Colored")) { color = color };
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_ZWrite", 0);

            return material;
        }
    }
}
