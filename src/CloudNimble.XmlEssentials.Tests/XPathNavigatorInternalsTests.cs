using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.XPath;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudNimble.XmlEssentials.Tests
{

    /// <summary>
    /// Internals of the XPath navigator over <see cref="XmlDocument"/>: the parent table and the slices it reads.
    /// </summary>
    [TestClass]
    public class XPathNavigatorInternalsTests
    {

        #region Public Methods

        [TestMethod]
        public void GetParentTable_Fixture_HoldsTheParentOfEveryRow()
        {
            using var document = XmlDocument.Parse(XPathFixture.Xml);
            document.RowCount.Should().Be(27);
            document.GetParentTable().Should().Equal(XPathFixture.Parents);
        }

        [TestMethod]
        public void GetParentTable_IsBuiltOnFirstUse_AndKept()
        {
            using var document = XmlDocument.Parse(XPathFixture.Xml);
            XPathFixture.HasParentTable(document).Should().BeFalse();
            var first = document.GetParentTable();
            XPathFixture.HasParentTable(document).Should().BeTrue();
            document.GetParentTable().Should().BeSameAs(first);
        }

        [TestMethod]
        public void GetParentTable_OneRow_IsMinusOne()
        {
            using var document = XmlDocument.Parse("<e/>");
            document.GetParentTable().Should().Equal([-1]);
        }

        [TestMethod]
        public void GetParentTable_DeepDocument_StepsBackOverEndedElements()
        {
            using var document = XmlDocument.Parse("<a><b><c/><d><e/></d></b><f/>x</a>");
            // a b c d e f x
            document.GetParentTable().Should().Equal([-1, 0, 1, 1, 3, 0, 0]);
        }

        [TestMethod]
        public void GetParentTable_ThreadsThatRace_AllGetTheSameTable()
        {
            using var document = XmlDocument.Parse(XPathFixture.Xml);
            var tables = new int[8][];
            Parallel.For(0, tables.Length, i => tables[i] = document.GetParentTable());
            tables.Should().AllSatisfy(table => table.Should().BeSameAs(tables[0]));
            tables[0].Should().Equal(XPathFixture.Parents);
        }

        [TestMethod]
        public void Dispose_LetsTheParentTableGo()
        {
            var document = XmlDocument.Parse(XPathFixture.Xml);
            document.GetParentTable();
            document.Dispose();
            XPathFixture.HasParentTable(document).Should().BeFalse();
            document.RowCount.Should().Be(0);
        }

        [TestMethod]
        public void Select_NodeTestOfAKindTheParserNeverMakes_PassesNothing()
        {
            using var document = XmlDocument.Parse(XPathFixture.Xml);
            var navigator = new XPathNavigator(document);
            var unknown = new XPathNodeTest((XPathNodeTestKind)99, [], [], null);
            var output = new XPathIdBuffer(4);
            navigator.Select(XPathNodeId.Document, XPathAxis.Descendant, unknown, ref output);
            navigator.Select(XPathNodeId.FromRow(2), XPathAxis.Attribute, unknown, ref output);
            navigator.Select(XPathNodeId.FromRow(10), XPathAxis.Namespace, unknown, ref output);
            output.Count.Should().Be(0);
        }

        [TestMethod]
        public void NameKeyComparer_KeysAndSpans_HashAndCompareAlike()
        {
            var comparer = XPathNameKeyComparer.Instance;
            var key = comparer.Create(new XPathNameSpan("item"u8, "urn:a"u8));
            var same = new XPathNameKey("item"u8.ToArray(), "urn:a"u8.ToArray());
            var other = new XPathNameKey("item"u8.ToArray(), []);
            comparer.Equals(key, same).Should().BeTrue();
            comparer.Equals(key, other).Should().BeFalse();
            comparer.Equals(new XPathNameKey("it"u8.ToArray(), "emurn:a"u8.ToArray()), key).Should().BeFalse();
            comparer.GetHashCode(key).Should().Be(comparer.GetHashCode(same));
            comparer.GetHashCode(key).Should().Be(comparer.GetHashCode(new XPathNameSpan("item"u8, "urn:a"u8)));
            comparer.Equals(new XPathNameSpan("item"u8, "urn:a"u8), key).Should().BeTrue();
            comparer.Equals(new XPathNameSpan("item"u8, ""u8), key).Should().BeFalse();
            comparer.Equals(new XPathNameSpan("name"u8, "urn:a"u8), key).Should().BeFalse();
        }

        [TestMethod]
        public void SliceMemory_PayloadAndExtra_ReturnTheBytesOfTheRow()
        {
            using var document = XmlDocument.Parse("<e a=\"1\">&amp;x</e>");
            ref var attribute = ref document.Row(1);
            ref var text = ref document.Row(2);
            Encoding.UTF8.GetString(document.SliceMemory(attribute.ValueStart, attribute.ValueLength).Span).Should().Be("1");
            Encoding.UTF8.GetString(document.SliceMemory(text.ValueStart, text.ValueLength).Span).Should().Be("&x");
            document.SliceMemory(text.ValueStart, text.ValueLength).Span.SequenceEqual(document.Slice(text.ValueStart, text.ValueLength)).Should().BeTrue();
        }

        #endregion

    }

}
