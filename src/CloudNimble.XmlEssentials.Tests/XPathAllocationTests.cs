using System;
using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.XPath;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudNimble.XmlEssentials.Tests
{

    /// <summary>
    /// Heap-allocation checks for the XPath result types.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    public class XPathAllocationTests
    {

        #region Public Methods

        [TestMethod]
        public void NodeSet_Foreach_DoesNotAllocate()
        {
            using var document = XmlDocument.Parse("<a><b/><c/><d x=\"1\"/>text</a>");
            var nodes = XPathExpression.Compile("//node() | //@*").Select(document);
            Walk(nodes).Should().Be(Walk(nodes));
            var before = GC.GetAllocatedBytesForCurrentThread();
            var kinds = Walk(nodes);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            allocated.Should().Be(0);
            kinds.Should().Be(0 + 1 + 1 + 1 + 1 + 2 + 4);
        }

        #endregion

        #region Private Methods

        private static int Walk(XPathNodeSet nodes)
        {
            var sum = 0;
            foreach (var node in nodes)
            {
                sum += (int)node.Kind;
            }

            return sum;
        }

        #endregion

    }

}
