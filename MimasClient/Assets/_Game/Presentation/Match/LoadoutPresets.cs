using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// The named kits the room screen offers. Client data, not a Core concept: the server is told four item
    /// ids and does not care where they came from, and the real thing — character select and the draft — is
    /// M3. Until then this is how a player says what they want to be.
    /// </summary>
    [CreateAssetMenu(fileName = "LoadoutPresets", menuName = "Mimas/Loadout Presets")]
    public sealed class LoadoutPresets : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            [Tooltip("What the dropdown shows.")]
            public string Name = "Preset";

            public LoadoutSettings Loadout = new LoadoutSettings();
        }

        [Tooltip("In dropdown order. The first is the default.")]
        public List<Entry> Presets = new List<Entry>();

        public int Count => Presets != null ? Presets.Count : 0;

        public Entry Get(int index)
        {
            if (Presets == null || index < 0 || index >= Presets.Count) return null;
            return Presets[index];
        }

        public List<string> Names()
        {
            var names = new List<string>(Count);
            for (int i = 0; i < Count; i++) names.Add(Presets[i].Name);
            return names;
        }
    }
}
