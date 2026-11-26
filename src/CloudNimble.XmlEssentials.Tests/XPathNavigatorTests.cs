using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.XPath;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudNimble.XmlEssentials.Tests
{

    /// <summary>
    /// The XPath data model over the row store: every axis from every kind of context node in
    /// <see cref="XPathFixture"/>, the node tests, names, string-values and the cursors a caller passes in.
    /// </summary>
    /// <remarks>
    /// A node is written as a label: <c>/</c> for the document node, the qualified name for an element,
    /// <c>@name</c> for an attribute, <c>ns(element,prefix)</c> for a namespace node, <c>'text'</c> for a text node,
    /// <c>!text</c> for a comment and <c>?target</c> for a processing instruction.
    /// </remarks>
    [TestClass]
    public class XPathNavigatorTests
    {

        #region Public Methods

        [TestMethod]
        [DataRow("/", XPathAxis.Ancestor, "")]
        [DataRow("/", XPathAxis.AncestorOrSelf, "/")]
        [DataRow("/", XPathAxis.Attribute, "")]
        [DataRow("/", XPathAxis.Child, "!c0 ?p0 r !c2 ?p2")]
        [DataRow("/", XPathAxis.Descendant, "!c0 ?p0 r 't1c1t2' e b:f 'u' 'w' g h !c1 ?p1 !c2 ?p2")]
        [DataRow("/", XPathAxis.DescendantOrSelf, "/ !c0 ?p0 r 't1c1t2' e b:f 'u' 'w' g h !c1 ?p1 !c2 ?p2")]
        [DataRow("/", XPathAxis.Following, "")]
        [DataRow("/", XPathAxis.FollowingSibling, "")]
        [DataRow("/", XPathAxis.Namespace, "")]
        [DataRow("/", XPathAxis.Parent, "")]
        [DataRow("/", XPathAxis.Preceding, "")]
        [DataRow("/", XPathAxis.PrecedingSibling, "")]
        [DataRow("/", XPathAxis.Self, "/")]
        [DataRow("e", XPathAxis.Ancestor, "/ r")]
        [DataRow("e", XPathAxis.AncestorOrSelf, "/ r e")]
        [DataRow("e", XPathAxis.Attribute, "")]
        [DataRow("e", XPathAxis.Child, "b:f 'u'")]
        [DataRow("e", XPathAxis.Descendant, "b:f 'u'")]
        [DataRow("e", XPathAxis.DescendantOrSelf, "e b:f 'u'")]
        [DataRow("e", XPathAxis.Following, "'w' g h !c1 ?p1 !c2 ?p2")]
        [DataRow("e", XPathAxis.FollowingSibling, "'w' g !c1 ?p1")]
        [DataRow("e", XPathAxis.Namespace, "ns(e,xml) ns(e,) ns(e,a) ns(e,b)")]
        [DataRow("e", XPathAxis.Parent, "r")]
        [DataRow("e", XPathAxis.Preceding, "!c0 ?p0 't1c1t2'")]
        [DataRow("e", XPathAxis.PrecedingSibling, "'t1c1t2'")]
        [DataRow("e", XPathAxis.Self, "e")]
        [DataRow("@a:x", XPathAxis.Ancestor, "/ r e b:f")]
        [DataRow("@a:x", XPathAxis.AncestorOrSelf, "/ r e b:f @a:x")]
        [DataRow("@a:x", XPathAxis.Attribute, "")]
        [DataRow("@a:x", XPathAxis.Child, "")]
        [DataRow("@a:x", XPathAxis.Descendant, "")]
        [DataRow("@a:x", XPathAxis.DescendantOrSelf, "@a:x")]
        [DataRow("@a:x", XPathAxis.Following, "'u' 'w' g h !c1 ?p1 !c2 ?p2")]
        [DataRow("@a:x", XPathAxis.FollowingSibling, "")]
        [DataRow("@a:x", XPathAxis.Namespace, "")]
        [DataRow("@a:x", XPathAxis.Parent, "b:f")]
        [DataRow("@a:x", XPathAxis.Preceding, "!c0 ?p0 't1c1t2'")]
        [DataRow("@a:x", XPathAxis.PrecedingSibling, "")]
        [DataRow("@a:x", XPathAxis.Self, "@a:x")]
        [DataRow("ns(e,b)", XPathAxis.Ancestor, "/ r e")]
        [DataRow("ns(e,b)", XPathAxis.AncestorOrSelf, "/ r e ns(e,b)")]
        [DataRow("ns(e,b)", XPathAxis.Attribute, "")]
        [DataRow("ns(e,b)", XPathAxis.Child, "")]
        [DataRow("ns(e,b)", XPathAxis.Descendant, "")]
        [DataRow("ns(e,b)", XPathAxis.DescendantOrSelf, "ns(e,b)")]
        [DataRow("ns(e,b)", XPathAxis.Following, "b:f 'u' 'w' g h !c1 ?p1 !c2 ?p2")]
        [DataRow("ns(e,b)", XPathAxis.FollowingSibling, "")]
        [DataRow("ns(e,b)", XPathAxis.Namespace, "")]
        [DataRow("ns(e,b)", XPathAxis.Parent, "e")]
        [DataRow("ns(e,b)", XPathAxis.Preceding, "!c0 ?p0 't1c1t2'")]
        [DataRow("ns(e,b)", XPathAxis.PrecedingSibling, "")]
        [DataRow("ns(e,b)", XPathAxis.Self, "ns(e,b)")]
        [DataRow("'u'", XPathAxis.Ancestor, "/ r e")]
        [DataRow("'u'", XPathAxis.AncestorOrSelf, "/ r e 'u'")]
        [DataRow("'u'", XPathAxis.Attribute, "")]
        [DataRow("'u'", XPathAxis.Child, "")]
        [DataRow("'u'", XPathAxis.Descendant, "")]
        [DataRow("'u'", XPathAxis.DescendantOrSelf, "'u'")]
        [DataRow("'u'", XPathAxis.Following, "'w' g h !c1 ?p1 !c2 ?p2")]
        [DataRow("'u'", XPathAxis.FollowingSibling, "")]
        [DataRow("'u'", XPathAxis.Namespace, "")]
        [DataRow("'u'", XPathAxis.Parent, "e")]
        [DataRow("'u'", XPathAxis.Preceding, "!c0 ?p0 't1c1t2' b:f")]
        [DataRow("'u'", XPathAxis.PrecedingSibling, "b:f")]
        [DataRow("'u'", XPathAxis.Self, "'u'")]
        [DataRow("!c1", XPathAxis.Ancestor, "/ r")]
        [DataRow("!c1", XPathAxis.AncestorOrSelf, "/ r !c1")]
        [DataRow("!c1", XPathAxis.Attribute, "")]
        [DataRow("!c1", XPathAxis.Child, "")]
        [DataRow("!c1", XPathAxis.Descendant, "")]
        [DataRow("!c1", XPathAxis.DescendantOrSelf, "!c1")]
        [DataRow("!c1", XPathAxis.Following, "?p1 !c2 ?p2")]
        [DataRow("!c1", XPathAxis.FollowingSibling, "?p1")]
        [DataRow("!c1", XPathAxis.Namespace, "")]
        [DataRow("!c1", XPathAxis.Parent, "r")]
        [DataRow("!c1", XPathAxis.Preceding, "!c0 ?p0 't1c1t2' e b:f 'u' 'w' g h")]
        [DataRow("!c1", XPathAxis.PrecedingSibling, "'t1c1t2' e 'w' g")]
        [DataRow("!c1", XPathAxis.Self, "!c1")]
        [DataRow("?p2", XPathAxis.Ancestor, "/")]
        [DataRow("?p2", XPathAxis.AncestorOrSelf, "/ ?p2")]
        [DataRow("?p2", XPathAxis.Attribute, "")]
        [DataRow("?p2", XPathAxis.Child, "")]
        [DataRow("?p2", XPathAxis.Descendant, "")]
        [DataRow("?p2", XPathAxis.DescendantOrSelf, "?p2")]
        [DataRow("?p2", XPathAxis.Following, "")]
        [DataRow("?p2", XPathAxis.FollowingSibling, "")]
        [DataRow("?p2", XPathAxis.Namespace, "")]
        [DataRow("?p2", XPathAxis.Parent, "/")]
        [DataRow("?p2", XPathAxis.Preceding, "!c0 ?p0 r 't1c1t2' e b:f 'u' 'w' g h !c1 ?p1 !c2")]
        [DataRow("?p2", XPathAxis.PrecedingSibling, "!c0 ?p0 r !c2")]
        [DataRow("?p2", XPathAxis.Self, "?p2")]
        [DataRow("r", XPathAxis.Attribute, "@id @a:k")]
        [DataRow("r", XPathAxis.Child, "'t1c1t2' e 'w' g !c1 ?p1")]
        [DataRow("r", XPathAxis.Following, "!c2 ?p2")]
        [DataRow("r", XPathAxis.FollowingSibling, "!c2 ?p2")]
        [DataRow("r", XPathAxis.Namespace, "ns(r,xml) ns(r,) ns(r,a)")]
        [DataRow("r", XPathAxis.Parent, "/")]
        [DataRow("r", XPathAxis.Preceding, "!c0 ?p0")]
        [DataRow("r", XPathAxis.PrecedingSibling, "!c0 ?p0")]
        [DataRow("b:f", XPathAxis.Attribute, "@a:x")]
        [DataRow("b:f", XPathAxis.Child, "")]
        [DataRow("b:f", XPathAxis.FollowingSibling, "'u'")]
        [DataRow("b:f", XPathAxis.Namespace, "ns(b:f,xml) ns(b:f,) ns(b:f,a) ns(b:f,b)")]
        [DataRow("b:f", XPathAxis.PrecedingSibling, "")]
        [DataRow("g", XPathAxis.Child, "h")]
        [DataRow("g", XPathAxis.Namespace, "ns(g,xml)")]
        [DataRow("h", XPathAxis.Ancestor, "/ r g")]
        [DataRow("h", XPathAxis.Attribute, "@xml:lang")]
        [DataRow("h", XPathAxis.Namespace, "ns(h,xml)")]
        [DataRow("'t1c1t2'", XPathAxis.Following, "e b:f 'u' 'w' g h !c1 ?p1 !c2 ?p2")]
        [DataRow("'t1c1t2'", XPathAxis.FollowingSibling, "e 'w' g !c1 ?p1")]
        [DataRow("'t1c1t2'", XPathAxis.Preceding, "!c0 ?p0")]
        [DataRow("'t1c1t2'", XPathAxis.PrecedingSibling, "")]
        [DataRow("'w'", XPathAxis.Following, "g h !c1 ?p1 !c2 ?p2")]
        [DataRow("'w'", XPathAxis.FollowingSibling, "g !c1 ?p1")]
        [DataRow("'w'", XPathAxis.Preceding, "!c0 ?p0 't1c1t2' e b:f 'u'")]
        [DataRow("'w'", XPathAxis.PrecedingSibling, "'t1c1t2' e")]
        [DataRow("!c0", XPathAxis.Following, "?p0 r 't1c1t2' e b:f 'u' 'w' g h !c1 ?p1 !c2 ?p2")]
        [DataRow("!c0", XPathAxis.FollowingSibling, "?p0 r !c2 ?p2")]
        [DataRow("!c0", XPathAxis.Parent, "/")]
        [DataRow("!c0", XPathAxis.Preceding, "")]
        [DataRow("!c0", XPathAxis.PrecedingSibling, "")]
        [DataRow("@id", XPathAxis.Following, "'t1c1t2' e b:f 'u' 'w' g h !c1 ?p1 !c2 ?p2")]
        [DataRow("@id", XPathAxis.Parent, "r")]
        [DataRow("@id", XPathAxis.Preceding, "!c0 ?p0")]
        public void Select_AnyNode_SelectsEveryNodeOnTheAxis_InDocumentOrder(string context, object axis, string expected)
        {
            using var document = XmlDocument.Parse(XPathFixture.Xml);
            Select(document, context, (XPathAxis)axis, "node()").Should().Be(expected);
        }

        [TestMethod]
        [DataRow("r", XPathAxis.Child, "*", "e g")]
        [DataRow("r", XPathAxis.Child, "text()", "'t1c1t2' 'w'")]
        [DataRow("r", XPathAxis.Child, "comment()", "!c1")]
        [DataRow("r", XPathAxis.Attribute, "text()", "")]
        [DataRow("r", XPathAxis.Attribute, "comment()", "")]
        [DataRow("r", XPathAxis.Attribute, "processing-instruction()", "")]
        [DataRow("r", XPathAxis.Parent, "*", "")]
        [DataRow("/", XPathAxis.Self, "*", "")]
        [DataRow("/", XPathAxis.Child, "*", "r")]
        [DataRow("/", XPathAxis.Descendant, "*", "r e b:f g h")]
        [DataRow("/", XPathAxis.Descendant, "text()", "'t1c1t2' 'u' 'w'")]
        [DataRow("/", XPathAxis.Descendant, "comment()", "!c0 !c1 !c2")]
        [DataRow("/", XPathAxis.Descendant, "processing-instruction()", "?p0 ?p1 ?p2")]
        [DataRow("/", XPathAxis.Descendant, "processing-instruction(p1)", "?p1")]
        [DataRow("/", XPathAxis.Descendant, "{urn:d}e", "e")]
        [DataRow("/", XPathAxis.Descendant, "{}e", "")]
        [DataRow("/", XPathAxis.Descendant, "{}g", "g")]
        [DataRow("/", XPathAxis.Descendant, "{urn:b}f", "b:f")]
        [DataRow("/", XPathAxis.Descendant, "{urn:b}*", "b:f")]
        [DataRow("/", XPathAxis.DescendantOrSelf, "*", "r e b:f g h")]
        [DataRow("b:f", XPathAxis.Attribute, "*", "@a:x")]
        [DataRow("b:f", XPathAxis.Attribute, "{urn:a}x", "@a:x")]
        [DataRow("b:f", XPathAxis.Attribute, "{}x", "")]
        [DataRow("b:f", XPathAxis.Attribute, "{urn:a}*", "@a:x")]
        [DataRow("b:f", XPathAxis.Attribute, "{urn:b}*", "")]
        [DataRow("e", XPathAxis.Namespace, "*", "ns(e,xml) ns(e,) ns(e,a) ns(e,b)")]
        [DataRow("e", XPathAxis.Namespace, "{}a", "ns(e,a)")]
        [DataRow("e", XPathAxis.Namespace, "{}xml", "ns(e,xml)")]
        [DataRow("e", XPathAxis.Namespace, "{urn:a}a", "")]
        [DataRow("e", XPathAxis.Namespace, "{urn:a}*", "")]
        [DataRow("e", XPathAxis.Namespace, "comment()", "")]
        [DataRow("e", XPathAxis.Preceding, "*", "")]
        [DataRow("e", XPathAxis.FollowingSibling, "*", "g")]
        [DataRow("@a:x", XPathAxis.Self, "*", "")]
        [DataRow("@a:x", XPathAxis.Parent, "*", "b:f")]
        [DataRow("@a:x", XPathAxis.AncestorOrSelf, "*", "r e b:f")]
        [DataRow("ns(e,b)", XPathAxis.AncestorOrSelf, "*", "r e")]
        [DataRow("h", XPathAxis.Ancestor, "*", "r g")]
        [DataRow("h", XPathAxis.AncestorOrSelf, "{}g", "g")]
        [DataRow("'u'", XPathAxis.Self, "text()", "'u'")]
        [DataRow("'u'", XPathAxis.Self, "*", "")]
        [DataRow("'w'", XPathAxis.Preceding, "*", "e b:f")]
        [DataRow("'w'", XPathAxis.PrecedingSibling, "*", "e")]
        [DataRow("!c1", XPathAxis.Self, "comment()", "!c1")]
        [DataRow("?p2", XPathAxis.Self, "processing-instruction(p2)", "?p2")]
        [DataRow("?p2", XPathAxis.Self, "processing-instruction(p1)", "")]
        public void Select_NodeTest_SelectsTheNodesThatPass(string context, object axis, string test, string expected)
        {
            using var document = XmlDocument.Parse(XPathFixture.Xml);
            Select(document, context, (XPathAxis)axis, test).Should().Be(expected);
        }

        [TestMethod]
        public void Select_NamespaceAxis_DeclarationOfXml_GivesOnlyTheXmlNode()
        {
            using var document = XmlDocument.Parse("<a xmlns:xml=\"http://www.w3.org/XML/1998/namespace\" xmlns:p=\"urn:p\"/>");
            Select(document, "a", XPathAxis.Namespace, "node()").Should().Be("ns(a,xml) ns(a,p)");
        }

        [TestMethod]
        public void Select_NamespaceAxis_NearerDeclaration_HidesTheOuterOne()
        {
            using var document = XmlDocument.Parse("<a xmlns:p=\"urn:outer\" xmlns:q=\"urn:q\"><b xmlns:p=\"urn:inner\"/></a>");
            var navigator = new XPathNavigator(document);
            var buffer = new XPathIdBuffer(4);
            navigator.Select(XPathNodeId.FromRow(3), XPathAxis.Namespace, XPathNodeTest.AnyNode, ref buffer);
            buffer.AsSpan().ToArray().Select(id => Encoding.UTF8.GetString(navigator.LocalName(id)) + "=" + Encoding.UTF8.GetString(navigator.StringValue(id).Span))
                .Should().Equal("xml=http://www.w3.org/XML/1998/namespace", "q=urn:q", "p=urn:inner");
        }

        [TestMethod]
        public void Select_ChildSelfAttributeAndElementScans_DoNotBuildTheParentTable()
        {
            using var document = XmlDocument.Parse(XPathFixture.Xml);
            var navigator = new XPathNavigator(document);
            var element = new XPathNodeTest(XPathNodeTestKind.Any, [], [], null);
            Ids(navigator, XPathNodeId.FromRow(2), XPathAxis.Child, XPathNodeTest.AnyNode).Should().Equal(7, 10, 15, 17, 22, 23);
            Ids(navigator, XPathNodeId.FromRow(2), XPathAxis.Self, XPathNodeTest.AnyNode).Should().Equal(2);
            Ids(navigator, XPathNodeId.FromRow(2), XPathAxis.Attribute, XPathNodeTest.AnyNode).Should().Equal(5, 6);
            Ids(navigator, XPathNodeId.Document, XPathAxis.Descendant, element).Should().Equal(2, 10, 12, 17, 20);
            Ids(navigator, XPathNodeId.FromRow(10), XPathAxis.Following, element).Should().Equal(17, 20);
            Ids(navigator, XPathNodeId.FromRow(17), XPathAxis.Preceding, element).Should().Equal(10, 12);
            Ids(navigator, XPathNodeId.FromRow(13), XPathAxis.Parent, XPathNodeTest.AnyNode).Should().Equal(12);
            XPathFixture.HasParentTable(document).Should().BeFalse();
        }

        [TestMethod]
        public void Select_TextOnDescendant_AdjacentTextRowsWithDifferentParents_BuildsTheParentTable()
        {
            using var document = XmlDocument.Parse(XPathFixture.Xml);
            Select(document, "/", XPathAxis.Descendant, "text()").Should().Be("'t1c1t2' 'u' 'w'");
            XPathFixture.HasParentTable(document).Should().BeTrue();
        }

        [TestMethod]
        public void Select_TextOnDescendant_NoAdjacentTextRows_DoesNotBuildTheParentTable()
        {
            using var document = XmlDocument.Parse("<a><b>x</b><c>y</c></a>");
            var navigator = new XPathNavigator(document);
            var buffer = new XPathIdBuffer(4);
            navigator.Select(XPathNodeId.Document, XPathAxis.Descendant, new XPathNodeTest(XPathNodeTestKind.Text, [], [], null), ref buffer);
            buffer.AsSpan().ToArray().Should().Equal(XPathNodeId.FromRow(2), XPathNodeId.FromRow(4));
            XPathFixture.HasParentTable(document).Should().BeFalse();
        }

        [TestMethod]
        [DataRow("/", XPathNodeKind.Document)]
        [DataRow("b:f", XPathNodeKind.Element)]
        [DataRow("@a:x", XPathNodeKind.Attribute)]
        [DataRow("ns(e,b)", XPathNodeKind.Namespace)]
        [DataRow("'t1c1t2'", XPathNodeKind.Text)]
        [DataRow("!c1", XPathNodeKind.Comment)]
        [DataRow("?p1", XPathNodeKind.ProcessingInstruction)]
        public void KindOf_EachKindOfNode_IsItsKind(string node, XPathNodeKind expected)
        {
            using var document = XmlDocument.Parse(XPathFixture.Xml);
            new XPathNavigator(document).KindOf(Find(document, node)).Should().Be(expected);
        }

        [TestMethod]
        [DataRow("/", "", "", "")]
        [DataRow("r", "r", "urn:d", "")]
        [DataRow("b:f", "f", "urn:b", "b")]
        [DataRow("g", "g", "", "")]
        [DataRow("@a:x", "x", "urn:a", "a")]
        [DataRow("@id", "id", "", "")]
        [DataRow("@xml:lang", "lang", "http://www.w3.org/XML/1998/namespace", "xml")]
        [DataRow("ns(e,b)", "b", "", "")]
        [DataRow("ns(e,)", "", "", "")]
        [DataRow("ns(e,xml)", "xml", "", "")]
        [DataRow("'u'", "", "", "")]
        [DataRow("!c1", "", "", "")]
        [DataRow("?p1", "p1", "", "")]
        public void Names_EachKindOfNode_AreThoseOfTheDataModel(string node, string localName, string namespaceUri, string prefix)
        {
            using var document = XmlDocument.Parse(XPathFixture.Xml);
            var navigator = new XPathNavigator(document);
            var id = Find(document, node);
            Encoding.UTF8.GetString(navigator.LocalName(id)).Should().Be(localName);
            Encoding.UTF8.GetString(navigator.NamespaceUri(id)).Should().Be(namespaceUri);
            Encoding.UTF8.GetString(navigator.Prefix(id)).Should().Be(prefix);
        }

        [TestMethod]
        [DataRow("/", "t1c1t2uw")]
        [DataRow("r", "t1c1t2uw")]
        [DataRow("e", "u")]
        [DataRow("b:f", "")]
        [DataRow("g", "")]
        [DataRow("@id", "1")]
        [DataRow("@a:x", "2")]
        [DataRow("ns(e,b)", "urn:b")]
        [DataRow("ns(r,)", "urn:d")]
        [DataRow("ns(r,xml)", "http://www.w3.org/XML/1998/namespace")]
        [DataRow("'t1c1t2'", "t1c1t2")]
        [DataRow("'w'", "w")]
        [DataRow("!c0", "c0")]
        [DataRow("?p0", "d0")]
        [DataRow("?p2", "")]
        public void StringValue_EachKindOfNode_IsItsText(string node, string expected)
        {
            using var document = XmlDocument.Parse(XPathFixture.Xml);
            Encoding.UTF8.GetString(new XPathNavigator(document).StringValue(Find(document, node)).Span).Should().Be(expected);
        }

        [TestMethod]
        public void StringValue_OneRow_IsASliceOfTheDocument()
        {
            using var document = XmlDocument.Parse("<a><b>text</b></a>");
            ref var text = ref document.Row(2);
            var expected = document.SliceMemory(text.ValueStart, text.ValueLength);
            var value = new XPathNavigator(document).StringValue(XPathNodeId.FromRow(1));
            MemoryMarshal.TryGetArray(value, out var actual).Should().BeTrue();
            MemoryMarshal.TryGetArray(expected, out var slice).Should().BeTrue();
            actual.Array.Should().BeSameAs(slice.Array);
            actual.Offset.Should().Be(slice.Offset);
            actual.Count.Should().Be(4);
        }

        [TestMethod]
        [DataRow("<a xml:lang=\"EN-us\"><b>t</b><!--c--></a>", -1, null)]
        [DataRow("<a xml:lang=\"EN-us\"><b>t</b><!--c--></a>", 0, "EN-us")]
        [DataRow("<a xml:lang=\"EN-us\"><b>t</b><!--c--></a>", 1, "EN-us")]
        [DataRow("<a xml:lang=\"EN-us\"><b>t</b><!--c--></a>", 2, "EN-us")]
        [DataRow("<a xml:lang=\"EN-us\"><b>t</b><!--c--></a>", 3, "EN-us")]
        [DataRow("<a xml:lang=\"EN-us\"><b>t</b><!--c--></a>", 4, "EN-us")]
        [DataRow("<a xml:lang=\"en\"><b xml:lang=\"\" c=\"1\"/></a>", 2, "")]
        [DataRow("<a xml:lang=\"en\"><b xml:lang=\"\" c=\"1\"/></a>", 4, "")]
        [DataRow("<!--c--><a lang=\"en\" b:lang=\"en\" xmlns:b=\"urn:b\"/>", 0, null)]
        [DataRow("<!--c--><a lang=\"en\" b:lang=\"en\" xmlns:b=\"urn:b\"/>", 1, null)]
        [DataRow("<!--c--><a lang=\"en\" b:lang=\"en\" xmlns:b=\"urn:b\"/>", 2, null)]
        public void Language_FindsTheNearestXmlLang(string xml, int row, string? expected)
        {
            using var document = XmlDocument.Parse(xml);
            var id = row < 0 ? XPathNodeId.Document : XPathNodeId.FromRow(row);
            var found = new XPathNavigator(document).Language(id, out var value);
            (found ? Encoding.UTF8.GetString(value) : null).Should().Be(expected);
        }

        [TestMethod]
        public void Language_NamespaceNode_StartsAtItsElement()
        {
            using var document = XmlDocument.Parse("<a xml:lang=\"de\" xmlns:p=\"urn:p\"/>");
            new XPathNavigator(document).Language(XPathNodeId.Namespace(0, 2), out var value).Should().BeTrue();
            Encoding.UTF8.GetString(value).Should().Be("de");
        }

        [TestMethod]
        [DataRow(2, 2)]
        [DataRow(5, 5)]
        [DataRow(7, 7)]
        [DataRow(8, 7)]
        [DataRow(9, 7)]
        [DataRow(15, 15)]
        [DataRow(16, 15)]
        [DataRow(14, 14)]
        [DataRow(22, 22)]
        public void FromElement_Cursor_IsTheNodeOfItsRow_OrTheFirstRowOfItsRun(int row, int expected)
        {
            using var document = XmlDocument.Parse(XPathFixture.Xml);
            new XPathNavigator(document).FromElement(new XmlElement(document, row), "context").Should().Be(XPathNodeId.FromRow(expected));
        }

        [TestMethod]
        [DataRow(3, "The element is a namespace declaration, which is not a node in the XPath data model. (Parameter 'context')")]
        [DataRow(19, "The element is a namespace declaration, which is not a node in the XPath data model. (Parameter 'context')")]
        [DataRow(24, "The element is a text run with no characters, which is not a node in the XPath data model. (Parameter 'context')")]
        public void FromElement_NotANode_Throws(int row, string message)
        {
            using var document = XmlDocument.Parse(XPathFixture.Xml);
            var navigator = new XPathNavigator(document);
            var act = () => navigator.FromElement(new XmlElement(document, row), "context");
            act.Should().Throw<ArgumentException>().Which.Message.Should().Be(message);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Selects from a node and returns the rows of what was selected.
        /// </summary>
        private static int[] Ids(XPathNavigator navigator, long context, XPathAxis axis, XPathNodeTest test)
        {
            var buffer = new XPathIdBuffer(1);
            navigator.Select(context, axis, test, ref buffer);
            return buffer.AsSpan().ToArray().Select(XPathNodeId.Row).ToArray();
        }

        /// <summary>
        /// Returns the id of the node with a label.
        /// </summary>
        private static long Find(XmlDocument document, string label)
        {
            return Nodes(document).Single(id => Label(document, id) == label);
        }

        /// <summary>
        /// Returns a label for a node.
        /// </summary>
        private static string Label(XmlDocument document, long id)
        {
            var navigator = new XPathNavigator(document);
            var local = Encoding.UTF8.GetString(navigator.LocalName(id));
            var prefix = Encoding.UTF8.GetString(navigator.Prefix(id));
            var name = prefix.Length == 0 ? local : prefix + ":" + local;
            var value = Encoding.UTF8.GetString(navigator.StringValue(id).Span);
            return navigator.KindOf(id) switch
            {
                XPathNodeKind.Document => "/",
                XPathNodeKind.Element => name,
                XPathNodeKind.Attribute => "@" + name,
                XPathNodeKind.Namespace => "ns(" + Label(document, XPathNodeId.FromRow(XPathNodeId.Row(id))) + "," + local + ")",
                XPathNodeKind.Text => "'" + value + "'",
                XPathNodeKind.Comment => "!" + value,
                _ => "?" + local,
            };
        }

        /// <summary>
        /// Returns every node of a document: the document node and its descendants, and the attributes and the
        /// namespace nodes of every element.
        /// </summary>
        private static List<long> Nodes(XmlDocument document)
        {
            var navigator = new XPathNavigator(document);
            var all = new XPathIdBuffer(8);
            navigator.Select(XPathNodeId.Document, XPathAxis.DescendantOrSelf, XPathNodeTest.AnyNode, ref all);
            var nodes = new List<long>();
            foreach (var id in all.AsSpan().ToArray())
            {
                nodes.Add(id);
                var owned = new XPathIdBuffer(4);
                navigator.Select(id, XPathAxis.Namespace, XPathNodeTest.AnyNode, ref owned);
                navigator.Select(id, XPathAxis.Attribute, XPathNodeTest.AnyNode, ref owned);
                nodes.AddRange(owned.AsSpan().ToArray());
            }

            return nodes;
        }

        /// <summary>
        /// Parses a node test written as <c>node()</c>, <c>*</c>, <c>text()</c>, <c>comment()</c>,
        /// <c>processing-instruction()</c>, <c>processing-instruction(target)</c>, <c>{uri}local</c> or <c>{uri}*</c>.
        /// </summary>
        private static XPathNodeTest Parse(string test)
        {
            if (test.StartsWith('{'))
            {
                var close = test.IndexOf('}');
                var uri = Encoding.UTF8.GetBytes(test[1..close]);
                var local = test[(close + 1)..];
                return local == "*"
                    ? new XPathNodeTest(XPathNodeTestKind.NamespaceWildcard, [], uri, null)
                    : new XPathNodeTest(XPathNodeTestKind.Name, Encoding.UTF8.GetBytes(local), uri, null);
            }

            return test switch
            {
                "node()" => XPathNodeTest.AnyNode,
                "*" => new XPathNodeTest(XPathNodeTestKind.Any, [], [], null),
                "text()" => new XPathNodeTest(XPathNodeTestKind.Text, [], [], null),
                "comment()" => new XPathNodeTest(XPathNodeTestKind.Comment, [], [], null),
                "processing-instruction()" => new XPathNodeTest(XPathNodeTestKind.ProcessingInstruction, [], [], null),
                _ => new XPathNodeTest(XPathNodeTestKind.ProcessingInstruction, [], [], Encoding.UTF8.GetBytes(test["processing-instruction(".Length..^1])),
            };
        }

        /// <summary>
        /// Selects from the node with a label, and returns the labels of what was selected.
        /// </summary>
        private static string Select(XmlDocument document, string context, XPathAxis axis, string test)
        {
            var nodes = Nodes(document).ToDictionary(id => id, id => Label(document, id));
            var navigator = new XPathNavigator(document);
            var buffer = new XPathIdBuffer(1);
            navigator.Select(nodes.Single(pair => pair.Value == context).Key, axis, Parse(test), ref buffer);
            return string.Join(" ", buffer.AsSpan().ToArray().Select(id => nodes[id]));
        }

        #endregion

    }

}
