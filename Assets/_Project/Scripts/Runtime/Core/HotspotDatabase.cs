using UnityEngine;
using System.Collections.Generic;

namespace CyberPunk.Core
{
    [CreateAssetMenu(fileName = "HotspotDatabase", menuName = "CyberPunk/Hotspot Database")]
    public class HotspotDatabase : ScriptableObject
    {
        [System.Serializable]
        public struct HotspotEntry
        {
            public string id;
            public Color32 color;
        }

        public List<HotspotEntry> entries = new List<HotspotEntry>();

        public string GetIdFromColor(Color32 color)
        {
            foreach (var entry in entries)
            {
                if (ColorsMatch(entry.color, color))
                    return entry.id;
            }
            return null;
        }

        private bool ColorsMatch(Color32 a, Color32 b)
        {
            return a.r == b.r && a.g == b.g && a.b == b.b; // Ignore alpha for ID check if needed, or include it.
        }
    }
}
