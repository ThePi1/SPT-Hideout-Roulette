using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
///     Turns the exported table model into a prefab and builds the asset bundle the plugin loads.
///     Run headless: Unity.exe -batchmode -quit -projectPath Unity -executeMethod RouletteBuild.Build
/// </summary>
public static class RouletteBuild
{
    private const string ModelPath = "Assets/Roulette/RouletteTable.fbx";
    private const string MaterialsJsonPath = "Assets/Roulette/RouletteTable.materials.json";
    private const string MaterialsFolder = "Assets/Roulette/Materials";
    private const string PrefabPath = "Assets/Roulette/RouletteTable.prefab";
    private const string BundleName = "hideoutroulette.bundle";
    private const string OutputFolder = "Build";

    [Serializable]
    private class MaterialList
    {
        public MaterialEntry[] materials;
    }

    [Serializable]
    private class MaterialEntry
    {
        public string name;
        public float r;
        public float g;
        public float b;
        public float metallic;
        public float roughness;
    }

    public static void Build()
    {
        var materials = CreateMaterials();
        ImportModel(materials);
        CreatePrefab();
        RenderPreview();

        Directory.CreateDirectory(OutputFolder);
        var build = new AssetBundleBuild { assetBundleName = BundleName, assetNames = new[] { PrefabPath } };
        var manifest = BuildPipeline.BuildAssetBundles(OutputFolder, new[] { build }, BuildAssetBundleOptions.None, BuildTarget.StandaloneWindows64);
        if (manifest == null)
        {
            throw new Exception("Asset bundle build failed");
        }

        Debug.Log($"ROULETTE bundle built: {Path.Combine(OutputFolder, BundleName)}");
    }

    /// <summary>
    ///     One Standard material per entry written by the Blender script
    /// </summary>
    private static Dictionary<string, Material> CreateMaterials()
    {
        if (!AssetDatabase.IsValidFolder(MaterialsFolder))
        {
            AssetDatabase.CreateFolder("Assets/Roulette", "Materials");
        }

        var list = JsonUtility.FromJson<MaterialList>(File.ReadAllText(MaterialsJsonPath));
        var result = new Dictionary<string, Material>();
        foreach (var entry in list.materials)
        {
            var path = $"{MaterialsFolder}/{entry.name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, path);
            }

            // Blender stores linear colours, Unity colour fields are sRGB
            material.color = new Color(entry.r, entry.g, entry.b).gamma;
            material.SetFloat("_Metallic", entry.metallic);
            material.SetFloat("_Glossiness", 1f - entry.roughness);
            EditorUtility.SetDirty(material);
            result[entry.name] = material;
        }

        AssetDatabase.SaveAssets();

        return result;
    }

    private static void ImportModel(Dictionary<string, Material> materials)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        importer.importCameras = false;
        importer.importLights = false;
        importer.importAnimation = false;
        importer.animationType = ModelImporterAnimationType.None;
        importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;

        foreach (var material in materials)
        {
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), material.Key), material.Value);
        }

        importer.SaveAndReimport();
    }

    private static void CreatePrefab()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = "Model";

        // The FBX import turns the model half a turn round the vertical axis. Undone here so the prefab matches the
        // Blender scene: player on the -Z side, +X to their right.
        var root = new GameObject("RouletteTable");
        instance.transform.SetParent(root.transform, false);
        instance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        // Logged so the build output shows how Blenders axes came through the import
        foreach (var child in root.GetComponentsInChildren<Transform>())
        {
            if (child.name.StartsWith("Pocket_") && child.name != "Pocket_0" && child.name != "Pocket_32")
            {
                continue;
            }

            Debug.Log(
                $"ROULETTE part {child.name}: position {child.position.ToString("F4")} rotation {child.eulerAngles.ToString("F1")} scale {child.lossyScale.ToString("F3")}"
            );
        }

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
    }

    /// <summary>
    ///     Top down picture of the prefab, to check orientation and materials without opening the editor
    /// </summary>
    private static void RenderPreview()
    {
        var instance = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));

        var lightObject = new GameObject("Preview light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.1f;
        lightObject.transform.rotation = Quaternion.Euler(55f, 30f, 0f);

        var cameraObject = new GameObject("Preview camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.1f, 0.1f, 0.1f);
        camera.fieldOfView = 40f;
        camera.nearClipPlane = 0.05f;

        var texture = new RenderTexture(1000, 1000, 24);
        camera.targetTexture = texture;

        Directory.CreateDirectory(OutputFolder);
        Capture(camera, texture, new Vector3(0.05f, 4.3f, 0f), Quaternion.Euler(90f, 0f, 0f), "preview_top.png");
        Capture(camera, texture, new Vector3(0.05f, 2.45f, -1.5f), Quaternion.Euler(45f, 0f, 0f), "preview_player.png");

        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(texture);
        UnityEngine.Object.DestroyImmediate(cameraObject);
        UnityEngine.Object.DestroyImmediate(lightObject);
        UnityEngine.Object.DestroyImmediate(instance);
    }

    private static void Capture(Camera camera, RenderTexture texture, Vector3 position, Quaternion rotation, string fileName)
    {
        camera.transform.SetPositionAndRotation(position, rotation);
        camera.Render();

        RenderTexture.active = texture;
        var image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
        image.Apply();
        RenderTexture.active = null;

        File.WriteAllBytes(Path.Combine(OutputFolder, fileName), image.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(image);
    }
}
