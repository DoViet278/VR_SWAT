using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Sinh prefab lỗ đạn (quad + material decal) bằng code để khỏi phải viết tay YAML prefab.
/// Chạy qua menu Tools > VR SWAT > Create Bullet Decal Prefabs.
/// </summary>
public static class ImpactDecalPrefabBuilder
{
    private const string OutputFolder = "Assets/Prefabs/Decals";
    private const string MetalMaterialPath = "Assets/Knife.PlayerController/Materials/MetalHitDecal.mat";
    private const string WoodMaterialPath = "Assets/Knife.PlayerController/Materials/WoodDecal.mat";

    /// <summary>Kích thước quad, lấy theo prefab mẫu HitEffect của Knife.</summary>
    private const float DecalSize = 0.0776f;

    [MenuItem("Tools/VR SWAT/Create Bullet Decal Prefabs")]
    private static void CreateAll()
    {
        if (!Directory.Exists(OutputFolder))
            Directory.CreateDirectory(OutputFolder);

        bool metal = Build("MetalBulletHole", MetalMaterialPath);
        bool wood = Build("WoodBulletHole", WoodMaterialPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        if (metal && wood)
            Debug.Log($"[ImpactDecalPrefabBuilder] Đã tạo prefab lỗ đạn trong {OutputFolder}.");
    }

    private static bool Build(string prefabName, string materialPath)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

        if (material == null)
        {
            Debug.LogError($"[ImpactDecalPrefabBuilder] Không tìm thấy material: {materialPath}");
            return false;
        }

        GameObject root = new GameObject(prefabName);
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);

        // BẮT BUỘC: CreatePrimitive tự thêm MeshCollider. Súng raycast với mask Everything
        // (gồm cả layer Ignore Raycast) nên decal còn collider sẽ bị phát sau bắn vào.
        Collider spawnedCollider = quad.GetComponent<Collider>();
        if (spawnedCollider != null)
            Object.DestroyImmediate(spawnedCollider);

        quad.name = "Decal";
        quad.transform.SetParent(root.transform, false);
        // Lật 180 độ quanh X: quad có pháp tuyến -Z, lật lại để mặt quad hướng ra ngoài tường
        // khi root forward được canh theo hit.normal.
        quad.transform.localRotation = Quaternion.Euler(180f, 0f, 0f);
        // Offset chống z-fighting do BulletDecalManager.surfaceOffset lo, prefab để 0.
        quad.transform.localPosition = Vector3.zero;
        quad.transform.localScale = Vector3.one * DecalSize;

        MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        renderer.allowOcclusionWhenDynamic = false;

        string prefabPath = $"{OutputFolder}/{prefabName}.prefab";
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        return true;
    }
}
