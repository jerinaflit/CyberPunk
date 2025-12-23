using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class BootHotspotDemo : MonoBehaviour
{
    public Texture2D maskImage; // <-- glisse rue_hot.png ici dans l'Inspector

    const int PPU = 100;

    bool _inited;

    SpriteRenderer _bgRenderer;
    Texture2D _mask;

    string _uiText = "Assigne Mask Image (rue_hot.png) puis Play.";

    Dictionary<Color32, string> _colorToId;
    Dictionary<string, Action> _onClick;

    void Awake() => InitOnce();
    void Start() => InitOnce();

    void InitOnce()
    {
        if (_inited) return;
        _inited = true;

        Application.targetFrameRate = 60;

        EnsureMainCamera();

        // Mapping couleurs -> HotspotId (mets tes vraies couleurs ici si besoin)
        _colorToId = new Dictionary<Color32, string>
        {
            { new Color32(255, 0,   0,   255), "BAR_DOOR" },
            { new Color32(0,   200, 200, 255), "ATM" },
            { new Color32(0,   0,   255, 255), "ROBOT" },
        };

        // Actions test
        _onClick = new Dictionary<string, Action>
        {
            { "BAR_DOOR", () => Say("La porte du bar est verrouillée...") },
            { "ATM",      () => Say("L’ATM clignote. Ça sent l’ozone.") },
            { "ROBOT",    () => Say("Le robot t’observe en silence.") },
        };

        // Si l'image n'est pas assignée, on stoppe proprement (pas d'erreurs infinies)
        if (maskImage == null)
        {
            Say("⚠️ Assigne 'Mask Image' (rue_hot.png) sur BootHotspotDemo.");
            return;
        }

        // Clone lisible du mask (important: lire les pixels via _mask)
        _mask = new Texture2D(maskImage.width, maskImage.height, TextureFormat.RGBA32, false);
        _mask.SetPixels(maskImage.GetPixels());
        _mask.Apply();
        _mask.filterMode = FilterMode.Point;

        // Fond neutre à la taille du mask (juste pour avoir une surface)
        var bg = NewTex(_mask.width, _mask.height, new Color32(30, 30, 34, 255));
        _bgRenderer = CreateSpriteGO("Background", bg, sortingOrder: 0);

        // Overlay du mask (semi-transparent) pour debug visuel
        var maskVis = TextureToSprite(_mask, PPU);
        var go = new GameObject("MaskDebug");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = maskVis;
        sr.sortingOrder = 1;
        sr.color = new Color(1, 1, 1, 0.20f);

        Say($"Mask chargé: {_mask.width}x{_mask.height}. Clique sur une zone colorée.");
    }

    void Update()
    {
        // Si pas de mask assigné, on ne fait rien
        if (maskImage == null || _mask == null || _bgRenderer == null || _colorToId == null)
            return;

        if (Mouse.current == null) return;
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;

        Vector2 screenPos = Mouse.current.position.ReadValue();

        if (!TryScreenToPixel(screenPos, out int px, out int py))
        {
            Say("(clic hors scène)");
            return;
        }

        Color32 c = (Color32)_mask.GetPixel(px, py);

        if (c.a == 0)
        {
            Say("Rien ici.");
            return;
        }

        if (!_colorToId.TryGetValue(c, out var id))
        {
            Say($"Couleur inconnue: {c.r},{c.g},{c.b}");
            return;
        }

        Debug.Log($"[HOTSPOT] {id} @ ({px},{py})");
        HandleHotspotClick(id);
    }

    void HandleHotspotClick(string id)
    {
        if (_onClick != null && _onClick.TryGetValue(id, out var action))
            action.Invoke();
        else
            Say($"Hotspot détecté: {id} (pas encore d’action)");
    }

    void Say(string text)
    {
        _uiText = text;
        Debug.Log("[SAY] " + text);
    }

    void OnGUI()
    {
        GUI.Label(new Rect(12, 12, 1400, 40), _uiText);
        GUI.Label(new Rect(12, 32, 1400, 40), "Astuce: l’overlay 'MaskDebug' montre les zones cliquables.");
    }

    // ---------------- Helpers ----------------

    void EnsureMainCamera()
    {
        if (Camera.main != null) return;

        var camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        var cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.transform.position = new Vector3(0, 0, -10);
    }

    Texture2D NewTex(int w, int h, Color32 fill)
    {
        var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var pixels = new Color32[w * h];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = fill;
        t.SetPixels32(pixels);
        t.Apply();
        t.filterMode = FilterMode.Point;
        return t;
    }

    SpriteRenderer CreateSpriteGO(string name, Texture2D tex, int sortingOrder)
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

    Sprite TextureToSprite(Texture2D tex, int ppu)
    {
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), ppu);
    }

    bool TryScreenToPixel(Vector2 screen, out int px, out int py)
    {
        px = py = 0;

        if (_bgRenderer == null || _mask == null) return false;

        var cam = Camera.main;
        if (cam == null) return false;

        Vector3 world = cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 0f));
        Vector3 local = _bgRenderer.transform.InverseTransformPoint(world);

        var sprite = _bgRenderer.sprite;

        float halfW = sprite.rect.width * 0.5f;
        float halfH = sprite.rect.height * 0.5f;

        px = Mathf.FloorToInt(local.x * PPU + halfW);
        py = Mathf.FloorToInt(local.y * PPU + halfH);

        // ✅ CORRECTION ICI : on compare à la taille du mask
        return (px >= 0 && px < _mask.width && py >= 0 && py < _mask.height);
    }
}
