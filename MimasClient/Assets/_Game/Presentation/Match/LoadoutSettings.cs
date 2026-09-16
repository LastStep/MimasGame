using System;
using UnityEngine;
using Mimas.Core.Match;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// One side's gear as four <c>items/*.json</c> ids, edited in the Inspector. Turned into the Core
    /// <see cref="Loadout"/> the match actually starts from; the catalogue checks that each id exists and
    /// fills the slot it was put in, so a typo fails at setup with a named error.
    /// </summary>
    [Serializable]
    public sealed class LoadoutSettings
    {
        [Tooltip("items/*.json id with slot weapon.")] public string Weapon = "longbow";
        [Tooltip("items/*.json id with slot crown.")] public string Crown = "ember-circlet";
        [Tooltip("items/*.json id with slot boots.")] public string Boots = "leaping-boots";
        [Tooltip("items/*.json id with slot armour.")] public string Armour = "leather-jerkin";

        public Loadout ToLoadout() => new Loadout(Weapon, Crown, Boots, Armour);

        public override string ToString() => Weapon + " / " + Crown + " / " + Boots + " / " + Armour;
    }
}
