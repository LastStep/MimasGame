using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace Mimas.Client.Tests
{
    /// <summary>
    /// Every ability number the presenter shows must come from the mirror's resolver, never from the
    /// catalogue (ADR-034, spec D part 2 §7.9). A boon's effect lives on the unit's overlay: a Sigil's grant
    /// and an Enchant's changed cost, range or damage exist nowhere in <c>abilities/*.json</c>, so a
    /// catalogue lookup would quietly draw the wrong button and preview the wrong shot.
    /// <para>
    /// A source scan rather than a behaviour test because that is the rule: not "the numbers happen to be
    /// right today" but "there is no path by which they could be wrong". Core keeps the same kind of test
    /// over <c>ResolveAbility</c>.
    /// </para>
    /// </summary>
    public class MirrorResolverTests
    {
        private const string Relative = "Assets/_Game/Presentation/Match/MatchSession.cs";

        [Test]
        public void MatchSession_ReadsAbilitiesOnlyThroughTheMirror()
        {
            string path = Path.Combine(Application.dataPath, "..", Relative);
            Assert.IsTrue(File.Exists(path), Relative + " was not found at " + path);

            string[] lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimStart();
                if (trimmed.StartsWith("//")) continue;              // a comment may name it; code may not
                Assert.IsFalse(lines[i].Contains("_catalog.Abilities"),
                    Relative + ":" + (i + 1) + " reads _catalog.Abilities. Ability numbers come from "
                    + "Rules.ResolveAbility / ResolveAbilityKnownTo, so boons are in them (ADR-034):\n" + lines[i].Trim());
            }
        }
    }
}
