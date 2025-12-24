using System;
using UnityEngine;

namespace CyberPunk.Core
{
    /// <summary>
    /// A static Event Bus for decoupled communication.
    /// Allows systems to talk without knowing about each other.
    /// </summary>
    public static class GameEvents
    {
        // Event triggered when a hotspot is clicked
        public static event Action<string> OnHotspotClicked;

        public static void TriggerHotspotClicked(string hotspotId)
        {
            Debug.Log($"[EVENT] Hotspot Clicked: {hotspotId}");
            OnHotspotClicked?.Invoke(hotspotId);
        }
    }
}
