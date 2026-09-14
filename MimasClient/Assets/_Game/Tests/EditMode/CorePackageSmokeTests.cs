using Mimas.Core.Geometry;
using NUnit.Framework;

namespace Mimas.Client.Tests
{
    /// <summary>Proves the com.mimas.core local package is compiled and referenced inside Unity.</summary>
    public class CorePackageSmokeTests
    {
        [Test]
        public void Hex_Distance_AcrossRadius3Board_Is6()
        {
            Assert.AreEqual(6, Hex.Distance(new Hex(-3, 0), new Hex(3, 0)));
        }

        [Test]
        public void Hex_Ring3_Has18Hexes()
        {
            Assert.AreEqual(18, Hex.Ring(Hex.Zero, 3).Count);
        }
    }
}
