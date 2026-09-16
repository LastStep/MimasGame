#nullable disable

using Mimas.Core.Data;

namespace Mimas.Core.Tests
{
    /// <summary>
    /// The heights every bare test unit is built with: the shipped numbers (1 level = 3 units, hero body 6,
    /// aim 4). A test that wants different proportions builds its own <see cref="HeightsDef"/>.
    /// </summary>
    internal static class TestHeights
    {
        internal static readonly HeightsDef Default = new HeightsDef(3, 6, 4);
    }
}
