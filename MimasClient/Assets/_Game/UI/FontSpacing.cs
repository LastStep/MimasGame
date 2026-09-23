using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.TextCore.Text;

namespace Mimas.Client.UI
{
    /// <summary>
    /// Lets letter-spacing apply between every pair of letters. The TextCore font assets made from our Latin
    /// subsets carry the "ignore spacing adjustments" flag on most of their kerning pairs (1422 of 1439 in Josefin
    /// Sans Light, measured 23 Sep 2026), and TextCore drops the letter-spacing on any pair so flagged: the band's
    /// "VICTORY" read "V I C TORY" and "YOU MOVE FIRST" lost its spacing in "YOU". The interface language tracks its
    /// capitals wide (docs/ui/language.md §2), so the flag is cleared, in memory, once per font asset — the kerning
    /// itself is kept. Reflection on TextCore's serialized fields, because the table is internal; if a Unity update
    /// renames them this logs once and the text simply keeps today's uneven spacing.
    /// </summary>
    internal static class FontSpacing
    {
        /// <summary>FontFeatureLookupFlags.IgnoreSpacingAdjustments.</summary>
        private const int IgnoreSpacingAdjustments = 0x100;

        private const BindingFlags Fields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly HashSet<FontAsset> Repaired = new HashSet<FontAsset>();
        private static bool _warned;

        /// <summary>Repairs every font asset loaded so far that has not been repaired yet. Cheap after the first call.</summary>
        public static void RepairLoaded()
        {
            FontAsset[] fonts = Resources.FindObjectsOfTypeAll<FontAsset>();
            for (int i = 0; i < fonts.Length; i++) Repair(fonts[i]);
        }

        private static void Repair(FontAsset font)
        {
            if (font == null || !Repaired.Add(font)) return;
            try
            {
                object table = font.fontFeatureTable;
                if (table == null) return;
                Type type = table.GetType();
                var records = type.GetField("m_GlyphPairAdjustmentRecords", Fields)?.GetValue(table) as IList;
                if (records != null)
                    for (int i = 0; i < records.Count; i++)
                    {
                        object record = records[i];
                        if (Clear(record)) records[i] = record;
                    }

                // TextCore reads kerning through a lookup built from the list; it holds copies, so they are cleared too.
                var lookup = type.GetField("m_GlyphPairAdjustmentRecordLookup", Fields)?.GetValue(table) as IDictionary;
                if (lookup != null)
                {
                    var keys = new ArrayList(lookup.Keys);
                    for (int i = 0; i < keys.Count; i++)
                    {
                        object record = lookup[keys[i]];
                        if (Clear(record)) lookup[keys[i]] = record;
                    }
                }
            }
            catch (Exception e)
            {
                if (_warned) return;
                _warned = true;
                Debug.LogWarning("[FontSpacing] could not clear the kerning pairs' spacing flag (" + e.GetType().Name + "): letter-spacing stays uneven.");
            }
        }

        /// <summary>Clears the flag on a boxed record; true when it changed.</summary>
        private static bool Clear(object record)
        {
            if (record == null) return false;
            FieldInfo field = record.GetType().GetField("m_FeatureLookupFlags", Fields);
            if (field == null) return false;
            int flags = Convert.ToInt32(field.GetValue(record));
            if ((flags & IgnoreSpacingAdjustments) == 0) return false;
            field.SetValue(record, Enum.ToObject(field.FieldType, flags & ~IgnoreSpacingAdjustments));
            return true;
        }
    }
}
