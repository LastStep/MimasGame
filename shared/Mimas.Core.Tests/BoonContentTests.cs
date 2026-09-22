#nullable disable

using System.Collections.Generic;
using System.Linq;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Xunit;

namespace Mimas.Core.Tests
{
    /// <summary>
    /// The catalogue's link rules for everything the boons system added (spec D part 1 §5): elements on
    /// attacks and modifiers must be declared in <c>rules.elements</c>; boons and lineages load, link and
    /// fail closed; the shipped lineages cover every item.
    /// </summary>
    public class BoonContentTests
    {
        private static List<ContentFile> Replace(List<ContentFile> files, string path, string from, string to)
            => files.Select(f => f.Path == path ? new ContentFile(f.Path, f.Text.Replace(from, to)) : f).ToList();

        [Fact]
        public void Attack_UnknownElement_IsAnError()
        {
            var files = CombatFixtures.Files();
            files = Replace(files, "abilities/bow.json", @"""lineOfSight"": true }", @"""lineOfSight"": true, ""element"": ""acid"" }");
            Assert.Contains("abilities/bow.json: attack 'bow' carries undeclared element 'acid' (rules.json elements: fire, frost, lightning)", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Attack_DeclaredElement_Links()
        {
            var files = CombatFixtures.Files();
            files = Replace(files, "abilities/bow.json", @"""lineOfSight"": true }", @"""lineOfSight"": true, ""element"": ""frost"" }");
            var catalog = ContentCatalog.Load(files);
            Assert.Equal(new[] { "frost" }, catalog.GetAttack("bow").Elements);
        }

        [Fact]
        public void Modifier_UnknownElement_IsAnError()
        {
            var files = CombatFixtures.Files();
            files.Add(new ContentFile("modifiers/acid-skin.json", @"{ ""version"": 1, ""id"": ""acid-skin"", ""trigger"": ""takeDamage"",
                ""when"": { ""elements"": [ ""acid"" ] }, ""effect"": { ""nullify"": true } }"));
            Assert.Contains("modifiers/acid-skin.json: modifier 'acid-skin' conditions on undeclared element 'acid'", ContentFixtures.ErrorsOf(files));
        }
    }
}
