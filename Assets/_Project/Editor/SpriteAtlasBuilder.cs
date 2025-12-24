#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

public static class SpriteAtlasBuilder
{
    private const string AtlasRoot = "Assets/_Project/Art/_Atlases";

    // Where to look for sprites to atlas
    private static readonly string[] ContentRoots =
    {
        "Assets/_Project/Art",
        "Assets/_Project/UI",
        "Assets/_Project/VFX"
    };

    // Folders we NEVER pack (generated anim clips folder etc.)
    private static readonly string[] IgnorePathTokens =
    {
        "/_Generated/",
        "/_Incoming/",
        "/Masks/"
    };

    // Safe defaults for crisp 2D + anti-bleed
    private const int Padding = 8;
    private const int MaxTextureSize = 2048; // safe default; can raise later (4096) if needed
    private const bool TightPacking = false; // false = safer for anti-bleed
    private const bool AllowRotation = false; // keep sprites upright (debug friendly)

    // ===== MENUS =====

    [MenuItem("Tools/_Project/Atlases/Create Default Atlases")]
    public static void CreateDefaultAtlases()
    {
        EnsureFolder(AtlasRoot);

        // Build a default set based on top-level folders.
        // - Characters (Hero1/Hero2/NPC)
        // - UI (Bubbles/Cursors/Icons/Panels)
        // - Environments
        // - Props
        // - VFX

        var targets = new List<string>();

        // Characters per character folder (Hero1, Hero2, NPC)
        var charsRoot = "Assets/_Project/Art/Characters";
        if (AssetDatabase.IsValidFolder(charsRoot))
        {
            foreach (var f in GetSubfolders(charsRoot))
                targets.Add(f); // e.g. .../Hero1
        }

        // UI per subfolder
        var uiRoot = "Assets/_Project/Art/UI";
        if (AssetDatabase.IsValidFolder(uiRoot))
        {
            foreach (var f in GetSubfolders(uiRoot))
                targets.Add(f);
        }

        // Environments per subfolder (or one big atlas if you prefer later)
        var envRoot = "Assets/_Project/Art/Environments";
        if (AssetDatabase.IsValidFolder(envRoot))
        {
            foreach (var f in GetSubfolders(envRoot))
                targets.Add(f);
        }

        // Props per subfolder
        var propsRoot = "Assets/_Project/Art/Props";
        if (AssetDatabase.IsValidFolder(propsRoot))
        {
            foreach (var f in GetSubfolders(propsRoot))
                targets.Add(f);
        }

        // VFX per subfolder
        var vfxRoot = "Assets/_Project/Art/VFX";
        if (AssetDatabase.IsValidFolder(vfxRoot))
        {
            foreach (var f in GetSubfolders(vfxRoot))
                targets.Add(f);
        }

        if (targets.Count == 0)
        {
            EditorUtility.DisplayDialog("SpriteAtlasBuilder", "Aucun dossier cible trouvé sous _Project/Art. Rien à faire.", "OK");
            return;
        }

        int created = 0;
        int updated = 0;

        foreach (var folder in targets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!AssetDatabase.IsValidFolder(folder))
                continue;

            var atlasPath = GetAtlasPathForFolder(folder);
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(atlasPath);

            if (atlas == null)
            {
                atlas = CreateAtlas(atlasPath);
                created++;
            }
            else
            {
                updated++;
            }

            AssignFolderToAtlas(atlas, folder);
            ApplyAtlasSettings(atlas);
            EditorUtility.SetDirty(atlas);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog(
            "SpriteAtlasBuilder",
            $"OK.\n\nCréés: {created}\nMis à jour: {updated}\n\nAtlases dans:\n{AtlasRoot}",
            "Parfait"
        );
    }

    [MenuItem("Tools/_Project/Atlases/Rebuild Atlases (Refresh & Pack)")]
    public static void RebuildAtlases()
    {
        EnsureFolder(AtlasRoot);

        // Load all atlases under AtlasRoot
        var guids = AssetDatabase.FindAssets("t:SpriteAtlas", new[] { AtlasRoot });
        var atlases = guids.Select(g => AssetDatabase.GUIDToAssetPath(g))
                           .Select(p => AssetDatabase.LoadAssetAtPath<SpriteAtlas>(p))
                           .Where(a => a != null)
                           .ToArray();

        if (atlases.Length == 0)
        {
            EditorUtility.DisplayDialog("SpriteAtlasBuilder", "Aucun atlas trouvé. Fais d'abord: Create Default Atlases", "OK");
            return;
        }

        foreach (var a in atlases)
        {
            ApplyAtlasSettings(a);
            EditorUtility.SetDirty(a);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Pack for active build target (Android/Standalone)
        SpriteAtlasUtility.PackAtlases(atlases, EditorUserBuildSettings.activeBuildTarget);

        Debug.Log($"[SpriteAtlasBuilder] Packed {atlases.Length} atlases for {EditorUserBuildSettings.activeBuildTarget}.");
        EditorUtility.DisplayDialog("SpriteAtlasBuilder", $"Packed {atlases.Length} atlases.\nTarget: {EditorUserBuildSettings.activeBuildTarget}", "OK");
    }

    [MenuItem("Tools/_Project/Atlases/Validate Atlases")]
    public static void ValidateAtlases()
    {
        EnsureFolder(AtlasRoot);

        var guids = AssetDatabase.FindAssets("t:SpriteAtlas", new[] { AtlasRoot });
        var atlasPaths = guids.Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p).ToList();

        if (atlasPaths.Count == 0)
        {
            EditorUtility.DisplayDialog("SpriteAtlasBuilder", "Aucun atlas trouvé sous _Project/Art/_Atlases.", "OK");
            return;
        }

        int issues = 0;

        foreach (var p in atlasPaths)
        {
            var atlas = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(p);
            if (atlas == null) continue;

            var so = new SerializedObject(atlas);
            var packSettings = so.FindProperty("m_PackingSettings");
            var texSettings = so.FindProperty("m_TextureSettings");

            int padding = packSettings.FindPropertyRelative("padding").intValue;
            bool tight = packSettings.FindPropertyRelative("enableTightPacking").boolValue;

            bool readable = texSettings.FindPropertyRelative("readable").boolValue;
            bool mipmaps = texSettings.FindPropertyRelative("generateMipMaps").boolValue;

            if (padding < 4) { issues++; Debug.LogWarning($"[AtlasValidate] Low padding ({padding}): {p}"); }
            if (tight) { issues++; Debug.LogWarning($"[AtlasValidate] Tight packing ON (risk bleed): {p}"); }
            if (readable) { issues++; Debug.LogWarning($"[AtlasValidate] Readable ON (waste): {p}"); }
            if (mipmaps) { issues++; Debug.LogWarning($"[AtlasValidate] MipMaps ON (waste/blur): {p}"); }
        }

        EditorUtility.DisplayDialog("SpriteAtlasBuilder", issues == 0 ? "OK. Aucun problème détecté." : $"Problèmes détectés: {issues}\nRegarde la Console.", "OK");
    }

    // ===== CORE =====

    private static SpriteAtlas CreateAtlas(string atlasPath)
    {
        EnsureFolder(Path.GetDirectoryName(atlasPath)?.Replace("\\", "/") ?? AtlasRoot);

        var atlas = new SpriteAtlas();
        ApplyAtlasSettings(atlas);

        AssetDatabase.CreateAsset(atlas, atlasPath);
        AssetDatabase.SaveAssets();
        return atlas;
    }

    private static void ApplyAtlasSettings(SpriteAtlas atlas)
    {
        // Packing settings
        var pack = atlas.GetPackingSettings();
        pack.padding = Padding;
        pack.enableTightPacking = TightPacking;
        pack.enableRotation = AllowRotation;
        atlas.SetPackingSettings(pack);

        // Texture settings
        var tex = atlas.GetTextureSettings();
        tex.generateMipMaps = false;
        tex.readable = false;
        tex.sRGB = true;
        tex.filterMode = FilterMode.Point; // consistent with our import rules
        atlas.SetTextureSettings(tex);

        // Platform settings
        // Standalone (PC)
        var pc = atlas.GetPlatformSettings("Standalone");
        pc.overridden = true;
        pc.maxTextureSize = Math.Max(MaxTextureSize, 2048);
        pc.format = TextureImporterFormat.Automatic;
        pc.textureCompression = TextureImporterCompression.Compressed;
        pc.compressionQuality = 50;
        atlas.SetPlatformSettings(pc);

        // Android (ASTC; Unity will fallback if needed)
        var and = atlas.GetPlatformSettings("Android");
        and.overridden = true;
        and.maxTextureSize = MaxTextureSize;
        and.format = TextureImporterFormat.ASTC_6x6;
        and.textureCompression = TextureImporterCompression.Compressed;
        and.compressionQuality = 50;
        atlas.SetPlatformSettings(and);

        // Include in build by default
        atlas.SetIncludeInBuild(true);
    }

    private static void AssignFolderToAtlas(SpriteAtlas atlas, string folderPath)
    {
        folderPath = Normalize(folderPath);

        // Remove existing packables to avoid duplicates/accumulation
        var existing = atlas.GetPackables();
        if (existing != null && existing.Length > 0)
        {
            atlas.Remove(existing);
        }

        // Add folder itself as packable (Unity will pack sprites inside)
        var folderObj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(folderPath);
        if (folderObj != null)
            atlas.Add(new[] { folderObj });

        // Exclusions: we can't exclude inside atlas directly, so we prevent bad folders from being targets
        // by choosing sensible root targets only (done in CreateDefaultAtlases).
    }

    private static string GetAtlasPathForFolder(string folderPath)
    {
        folderPath = Normalize(folderPath);

        // Turn folder path into a safe file name
        // Example: Assets/_Project/Art/Characters/Hero1 -> Atlas_Characters_Hero1.spriteatlas
        string name = folderPath
            .Replace("Assets/_Project/Art/", "")
            .Replace("Assets/_Project/UI/", "UI/")
            .Replace("Assets/_Project/VFX/", "VFX/")
            .Replace("/", "_")
            .Replace("\\", "_");

        name = name.Replace("__", "_").Trim('_');

        string file = $"Atlas_{name}.spriteatlas";
        return $"{AtlasRoot}/{file}";
    }

    // ===== HELPERS =====

    private static string Normalize(string p) => (p ?? "").Replace("\\", "/");

    private static IEnumerable<string> GetSubfolders(string parent)
    {
        parent = Normalize(parent);
        string abs = ToAbsolutePath(parent);
        if (!Directory.Exists(abs)) yield break;

        foreach (var d in Directory.GetDirectories(abs))
        {
            string ap = ToAssetPath(d);
            if (string.IsNullOrEmpty(ap)) continue;

            // Skip ignored folders
            bool ignore = IgnorePathTokens.Any(t => ap.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
            if (ignore) continue;

            yield return ap.Replace("\\", "/");
        }
    }

    private static void EnsureFolder(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return;
        folderPath = Normalize(folderPath);
        if (AssetDatabase.IsValidFolder(folderPath)) return;

        string[] parts = folderPath.Split('/');
        string current = parts[0]; // Assets
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static string ToAbsolutePath(string assetPath)
    {
        string projectDir = Directory.GetParent(Application.dataPath)!.FullName.Replace("\\", "/");
        return Path.Combine(projectDir, assetPath).Replace("\\", "/");
    }

    private static string ToAssetPath(string absolutePath)
    {
        string proj = Directory.GetParent(Application.dataPath)!.FullName.Replace("\\", "/");
        absolutePath = absolutePath.Replace("\\", "/");
        if (!absolutePath.StartsWith(proj, StringComparison.OrdinalIgnoreCase)) return "";
        string rel = absolutePath.Substring(proj.Length).TrimStart('/');
        return rel;
    }
}
#endif
