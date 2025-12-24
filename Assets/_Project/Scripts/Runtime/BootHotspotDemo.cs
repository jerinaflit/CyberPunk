using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using CyberPunk.Core;
using CyberPunk.Hero;

public class BootHotspotDemo : MonoBehaviour
{
    [Header("Configuration")]
    // Walk mask: where the hero is allowed to move (alpha > 0)
    public Texture2D walkMaskImage; // <-- glisse rue_mask.png ici
    // Hotspot mask: colored zones for interactions (alpha > 0 + color mapping)
    public Texture2D hotspotMaskImage; // <-- glisse rue_hot.png ici

    // Backward-compat: if you already assigned only one texture, we treat it as the hotspot mask.
    [HideInInspector] public Texture2D maskImage;
    public HotspotDatabase hotspotDatabase;
    public SimpleHeroController hero; // Reference to the hero

    [Header("Runtime Debug")]
    [SerializeField] private string _uiText = "Assigne Mask Image & Database puis Play.";

    private const int PPU = 100;
    private bool _inited;
    private SpriteRenderer _bgRenderer;
    private Texture2D _walkMask;
    private Texture2D _hotspotMask;

    // Actions (still hardcoded for demo purposes, but could be event-based)
    private Dictionary<string, Action> _onClick;

    private void Awake() => InitOnce();
    private void Start() => InitOnce();

    private void InitOnce()
    {
        if (_inited) return;
        _inited = true;

        Application.targetFrameRate = 60;
        EnsureMainCamera();

        // Find Hero if not assigned
        if (hero == null) hero = FindFirstObjectByType<SimpleHeroController>();

        // Initialize Actions
        _onClick = new Dictionary<string, Action>
        {
            { "BAR_DOOR", () => Say("La porte du bar est verrouillée...") },
            { "ATM",      () => Say("L’ATM clignote. Ça sent l’ozone.") },
            { "ROBOT",    () => Say("Le robot t’observe en silence.") },
        };

        // Backward-compat bridge
        if (hotspotMaskImage == null && maskImage != null)
            hotspotMaskImage = maskImage;

        if (walkMaskImage == null)
            walkMaskImage = hotspotMaskImage; // fallback (not ideal, but prevents "no movement")

        if (walkMaskImage == null)
        {
            Say("⚠️ Assigne 'Walk Mask Image' (rue_mask.png) sur BootHotspotDemo.");
            return;
        }

        _walkMask = TryGetReadableTexture(walkMaskImage, "WalkMask");
        if (_walkMask == null) return;

        _hotspotMask = hotspotMaskImage != null ? TryGetReadableTexture(hotspotMaskImage, "HotspotMask") : null;

        // Create Background
        var bg = NewTex(_walkMask.width, _walkMask.height, new Color32(30, 30, 34, 255));
        _bgRenderer = CreateSpriteGO("Background", bg, sortingOrder: 0);

        // Create Debug Overlay
        var maskVis = TextureToSprite(_walkMask, PPU);
        var go = new GameObject("MaskDebug");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = maskVis;
        sr.sortingOrder = 1;
        sr.color = new Color(1, 1, 1, 0.20f);

        Say($"WalkMask chargé: {_walkMask.width}x{_walkMask.height}. Clique pour bouger.");
    }

    private void Update()
    {
        if (_walkMask == null || _bgRenderer == null) return;

        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame) return;

        Vector2 screenPos = Mouse.current.position.ReadValue();

        if (!TryScreenToPixel(screenPos, out int px, out int py))
        {
            Say("(clic hors scène)");
            return;
        }

        Color32 walk = (Color32)_walkMask.GetPixel(px, py);

        // Move Hero if click is on valid WALK mask area (alpha > 0)
        if (walk.a > 0 && hero != null)
        {
            Vector2 worldPos = PixelToWorld(px, py);
            hero.MoveTo(worldPos);
        }

        // Hotspot interaction (optional)
        if (_hotspotMask == null) return;

        Color32 c = (Color32)_hotspotMask.GetPixel(px, py);

        if (c.a == 0)
        {
            return;
        }

        string id = hotspotDatabase != null ? hotspotDatabase.GetIdFromColor(c) : null;

        // Fallback if no database or not found (for backward compatibility or testing)
        if (string.IsNullOrEmpty(id))
        {
             // Try hardcoded fallback if database is missing
             if (hotspotDatabase == null)
             {
                 // Simple fallback for demo
                 if (c.r == 255 && c.g == 0) id = "BAR_DOOR";
                 else if (c.g == 200) id = "ATM";
                 else if (c.b == 255) id = "ROBOT";
             }
        }

        if (string.IsNullOrEmpty(id))
        {
            // Just walking, no hotspot
            return;
        }

        Debug.Log($"[HOTSPOT] {id} @ ({px},{py})");
        
        // Use the new Event Bus architecture
        GameEvents.TriggerHotspotClicked(id);
        
        // Keep local handling for demo text feedback
        HandleHotspotClick(id);
    }

    private Texture2D TryGetReadableTexture(Texture2D source, string label)
    {
        if (source == null) return null;
        try
        {
            if (source.isReadable)
            {
                source.filterMode = FilterMode.Point;
                return source;
            }

            Debug.LogWarning($"{label} is not readable. Cloning texture (slower startup). Enable 'Read/Write' in Import Settings to optimize.");
            var clone = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            clone.SetPixels(source.GetPixels());
            clone.Apply();
            clone.filterMode = FilterMode.Point;
            return clone;
        }
        catch (Exception e)
        {
            Debug.LogError($"[BootHotspotDemo] Failed to read {label} pixels: {e.Message}. Make sure 'Read/Write Enabled' is checked for '{source.name}'.");
            return null;
        }
    }

    private void HandleHotspotClick(string id)
    {
        if (_onClick != null && _onClick.TryGetValue(id, out var action))
            action.Invoke();
        else
            Say($"Hotspot détecté: {id} (pas encore d’action)");
    }

    private void Say(string text)
    {
        _uiText = text;
        Debug.Log("[SAY] " + text);
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(12, 12, 1400, 40), _uiText);
        GUI.Label(new Rect(12, 32, 1400, 40), "Astuce: l’overlay 'MaskDebug' montre les zones cliquables.");
    }

    // ---------------- Helpers ----------------

    private void EnsureMainCamera()
    {
        if (Camera.main != null) return;
        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.transform.position = new Vector3(0, 0, -10);
    }

    private Texture2D NewTex(int w, int h, Color32 fill)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var pixels = new Color32[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
        t.SetPixels32(pixels);
        t.Apply();
        t.filterMode = FilterMode.Point;
        return t;
    }

    private SpriteRenderer CreateSpriteGO(string name, Texture2D tex, int sortingOrder)
    {
        var go = new GameObject(name);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = TextureToSprite(tex, PPU);
        sr.sortingOrder = sortingOrder;

        var cam = Camera.main;
        if (cam != null)
        {
            cam.orthographic = true;
            cam.orthographicSize = (tex.height / (float)PPU) * 0.5f;
            cam.transform.position = new Vector3(0, 0, -10);
        }
        return sr;
    }

    private Sprite TextureToSprite(Texture2D tex, int ppu)
    {
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
    }

    private bool TryScreenToPixel(Vector2 screen, out int px, out int py)
    {
        px = py = 0;
        if (_bgRenderer == null || _walkMask == null) return false;

        var cam = Camera.main;
        if (cam == null) return false;

        Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
        Vector3 local = _bgRenderer.transform.InverseTransformPoint(world);

        // Assuming sprite is centered
        float localX = local.x * PPU + (_walkMask.width / 2f);
        float localY = local.y * PPU + (_walkMask.height / 2f);

        px = Mathf.FloorToInt(localX);
        py = Mathf.FloorToInt(localY);

        return (px >= 0 && px < _walkMask.width && py >= 0 && py < _walkMask.height);
    }

    private Vector2 PixelToWorld(int px, int py)
    {
        // Convert mask pixel -> local units -> world
        float localX = (px - (_walkMask.width / 2f)) / PPU;
        float localY = (py - (_walkMask.height / 2f)) / PPU;
        Vector3 local = new Vector3(localX, localY, 0f);
        return _bgRenderer.transform.TransformPoint(local);
    }
}
