using UnityEngine;

namespace HideoutRoulette.Client.Wheel
{
    /// <summary>
    ///     Builds the table, wheel and betting layout in code. The only class to replace when a modelled prefab is used instead.
    ///     Local space: origin on the floor under the table centre, the player stands on the -Z side with +X to their right.
    /// </summary>
    public static class TableFactory
    {
        private const float TableHeight = 0.95f;
        private const float TableMinX = -1.05f;
        private const float TableMaxX = 1.14f;
        private const float TableDepth = 0.76f;
        private const float TopThickness = 0.04f;

        // Smaller than the table so the player can lean in far enough to reach the far row
        private const float CollisionInset = 0.1f;

        private const float CellSize = 0.09f;
        private const float FeltMinX = -0.2f;
        private const float FeltMinZ = -0.36f;

        private const float WheelX = -0.62f;
        private const float BowlRadius = 0.36f;
        private const float BowlHeight = 0.06f;
        private const float DiscRadius = 0.3f;
        private const float DiscLift = 0.002f;
        private const int DiscSegments = 72;
        private const float BallRadius = 0.011f;

        // Above the table top, high enough to fit the whole table on a 16:10 screen
        private const float CameraHeight = 1.3f;

        private static readonly Color _darkWood = new Color(0.13f, 0.08f, 0.05f);
        private static readonly Color _wood = new Color(0.27f, 0.16f, 0.08f);
        private static readonly Color _gold = new Color(0.7f, 0.58f, 0.27f);

        // Not every shader survives the games build stripping, first one found is used
        private static readonly string[] _shaderNames = { "Standard", "Legacy Shaders/Diffuse", "Sprites/Default", "Hidden/Internal-Colored" };
        private static Shader _shader;

        public static RouletteTable Create()
        {
            var root = new GameObject("HideoutRouletteTable");
            var table = root.AddComponent<RouletteTable>();
            var interactiveLayer = LayerMask.NameToLayer("Interactive");

            BuildBody(root.transform);
            BuildFelt(root.transform, table, interactiveLayer);
            BuildWheel(root.transform, table, interactiveLayer);
            BuildPlayMode(root.transform, table);

            return table;
        }

        /// <summary>
        ///     Overhead camera, lamp and hover highlight used while the player is betting
        /// </summary>
        private static void BuildPlayMode(Transform root, RouletteTable table)
        {
            var centreX = (TableMinX + TableMaxX) / 2f;

            // Looking straight down with the far side of the table at the top of the screen
            var cameraObject = new GameObject("Play camera");
            cameraObject.transform.SetParent(root, false);
            cameraObject.transform.localPosition = new Vector3(centreX, TableHeight + CameraHeight, 0f);
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
            lamp.range = CameraHeight + 1f;
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

        private static void BuildBody(Transform root)
        {
            var width = TableMaxX - TableMinX;
            var centreX = (TableMinX + TableMaxX) / 2f;
            var baseHeight = TableHeight - TopThickness;

            var body = CreatePrimitive(PrimitiveType.Cube, "Body", root, _darkWood);
            body.localPosition = new Vector3(centreX, baseHeight / 2f, 0f);
            body.localScale = new Vector3(width - 0.1f, baseHeight, TableDepth - 0.1f);

            var top = CreatePrimitive(PrimitiveType.Cube, "Top", root, _wood);
            top.localPosition = new Vector3(centreX, baseHeight + TopThickness / 2f, 0f);
            top.localScale = new Vector3(width, TopThickness, TableDepth);

            // Stops the player walking through the table. Kept below the top so it never blocks the interaction ray.
            var collision = new GameObject("Collision");
            collision.layer = LayerMask.NameToLayer("HighPolyCollider");
            collision.transform.SetParent(root, false);
            var box = collision.AddComponent<BoxCollider>();
            box.center = new Vector3(centreX, baseHeight / 2f, 0f);
            box.size = new Vector3(width - CollisionInset * 2f, baseHeight, TableDepth - CollisionInset * 2f);
        }

        private static void BuildFelt(Transform root, RouletteTable table, int interactiveLayer)
        {
            var definitions = RouletteLayout.CreateSpots();
            var width = RouletteLayout.GridColumns * CellSize;
            var depth = RouletteLayout.GridRows * CellSize;

            // Quads face -Z, tipping one forward lays it flat with the textures bottom edge towards the player
            var felt = CreatePrimitive(PrimitiveType.Quad, "Felt", root, Color.white);
            felt.localPosition = new Vector3(FeltMinX + width / 2f, TableHeight + 0.001f, FeltMinZ + depth / 2f);
            felt.localRotation = Quaternion.Euler(90f, 0f, 0f);
            felt.localScale = new Vector3(width, depth, 1f);
            felt.GetComponent<Renderer>().material.mainTexture = TableTextures.CreateFelt(definitions);

            foreach (var definition in definitions)
            {
                var spotWidth = definition.ColumnSpan * CellSize;
                var spotDepth = definition.RowSpan * CellSize;

                var spotObject = new GameObject($"Spot {definition.Label}");
                spotObject.layer = interactiveLayer;
                spotObject.transform.SetParent(root, false);
                spotObject.transform.localPosition = new Vector3(
                    FeltMinX + definition.Column * CellSize + spotWidth / 2f,
                    TableHeight,
                    FeltMinZ + definition.Row * CellSize + spotDepth / 2f
                );

                var box = spotObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 0.015f, 0f);
                box.size = new Vector3(spotWidth - 0.004f, 0.03f, spotDepth - 0.004f);

                var chips = CreatePrimitive(PrimitiveType.Cylinder, "Chips", spotObject.transform, Color.white);
                chips.gameObject.SetActive(false);

                var spot = spotObject.AddComponent<BetSpot>();
                spot.Table = table;
                spot.Definition = definition;
                spot.ChipStack = chips;
                table.Spots.Add(spot);
            }

            // Marker dropped on the winning number
            var dolly = CreatePrimitive(PrimitiveType.Cylinder, "Dolly", root, _gold);
            dolly.localScale = new Vector3(0.03f, 0.025f, 0.03f);
            dolly.gameObject.SetActive(false);
            table.Dolly = dolly;
        }

        private static void BuildWheel(Transform root, RouletteTable table, int interactiveLayer)
        {
            var bowlTop = TableHeight + BowlHeight;

            // Unity cylinders are 2 units tall and 1 unit wide
            var bowl = CreatePrimitive(PrimitiveType.Cylinder, "Bowl", root, _darkWood);
            bowl.localPosition = new Vector3(WheelX, TableHeight + BowlHeight / 2f, 0f);
            bowl.localScale = new Vector3(BowlRadius * 2f, BowlHeight / 2f, BowlRadius * 2f);

            var discObject = new GameObject("Disc");
            discObject.transform.SetParent(root, false);
            discObject.transform.localPosition = new Vector3(WheelX, bowlTop + DiscLift, 0f);
            discObject.AddComponent<MeshFilter>().mesh = CreateDiscMesh();
            var discMaterial = CreateMaterial(Color.white);
            discMaterial.mainTexture = TableTextures.CreateWheel();
            discObject.AddComponent<MeshRenderer>().material = discMaterial;

            var hub = CreatePrimitive(PrimitiveType.Cylinder, "Hub", discObject.transform, _gold);
            hub.localPosition = new Vector3(0f, 0.03f, 0f);
            hub.localScale = new Vector3(0.07f, 0.03f, 0.07f);

            var ball = CreatePrimitive(PrimitiveType.Sphere, "Ball", root, new Color(0.95f, 0.95f, 0.9f));
            ball.localScale = Vector3.one * BallRadius * 2f;

            var wheelSpotObject = new GameObject("Wheel spot");
            wheelSpotObject.layer = interactiveLayer;
            wheelSpotObject.transform.SetParent(root, false);
            wheelSpotObject.transform.localPosition = new Vector3(WheelX, TableHeight, 0f);
            var box = wheelSpotObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.06f, 0f);
            box.size = new Vector3(BowlRadius * 2f, 0.12f, BowlRadius * 2f);
            wheelSpotObject.AddComponent<WheelSpot>().Table = table;

            table.Disc = discObject.transform;
            table.Ball = ball;
            table.WheelCentre = new Vector3(WheelX, 0f, 0f);
            table.TrackRadius = (DiscRadius + BowlRadius) / 2f;
            table.TrackHeight = bowlTop + BallRadius;
            table.PocketRadius = DiscRadius * (TableTextures.PocketRingInner + TableTextures.NumberRingInner) / 2f;
            table.PocketHeight = bowlTop + DiscLift + BallRadius;
        }

        /// <summary>
        ///     Flat disc facing up, unwrapped so the centre of the texture is the centre of the disc
        /// </summary>
        private static Mesh CreateDiscMesh()
        {
            var vertices = new Vector3[DiscSegments + 2];
            var uvs = new Vector2[DiscSegments + 2];
            var normals = new Vector3[DiscSegments + 2];
            var triangles = new int[DiscSegments * 3];

            vertices[0] = Vector3.zero;
            uvs[0] = new Vector2(0.5f, 0.5f);
            normals[0] = Vector3.up;

            for (var i = 0; i <= DiscSegments; i++)
            {
                // Clockwise seen from above, starting at +Z
                var radians = i * 2f * Mathf.PI / DiscSegments;
                var x = Mathf.Sin(radians);
                var z = Mathf.Cos(radians);

                vertices[i + 1] = new Vector3(x * DiscRadius, 0f, z * DiscRadius);
                uvs[i + 1] = new Vector2(0.5f + x / 2f, 0.5f + z / 2f);
                normals[i + 1] = Vector3.up;
            }

            for (var i = 0; i < DiscSegments; i++)
            {
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = i + 2;
            }

            var mesh = new Mesh
            {
                name = "RouletteDisc",
                vertices = vertices,
                uv = uvs,
                normals = normals,
                triangles = triangles,
            };
            mesh.RecalculateBounds();

            return mesh;
        }

        private static Transform CreatePrimitive(PrimitiveType type, string name, Transform parent, Color color)
        {
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            Object.Destroy(primitive.GetComponent<Collider>());
            primitive.transform.SetParent(parent, false);
            primitive.GetComponent<Renderer>().material = CreateMaterial(color);

            return primitive.transform;
        }

        private static Material CreateMaterial(Color color)
        {
            var material = new Material(GetShader()) { color = color };

            // Only present on the Standard shader, keeps the felt and wood from looking wet
            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", 0.15f);
            }

            return material;
        }

        private static Shader GetShader()
        {
            if (_shader != null)
            {
                return _shader;
            }

            foreach (var shaderName in _shaderNames)
            {
                _shader = Shader.Find(shaderName);
                if (_shader != null)
                {
                    Plugin.Log.LogInfo($"Roulette table using shader: {shaderName}");

                    return _shader;
                }
            }

            Plugin.Log.LogWarning("No usable shader found for the roulette table, it will render pink");

            return null;
        }
    }
}
