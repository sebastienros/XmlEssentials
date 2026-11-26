using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.XPath;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudNimble.XmlEssentials.Tests
{

    /// <summary>
    /// The public XPath types: what <see cref="XPathExpression.Evaluate(XmlDocument, XPathVariables?)"/> and
    /// <see cref="XPathExpression.Select(XmlDocument, XPathVariables?)"/> take and return, and how each refuses what
    /// it cannot use.
    /// </summary>
    [TestClass]
    public class XPathApiTests
    {

        #region Fields

        private const string Catalog = "<catalog><item id=\"1\" price=\"5\">apple</item><item id=\"2\" price=\"12\">pear<![CDATA[ & plum]]></item><!--end--></catalog>";

        #endregion

        #region Public Methods

        [TestMethod]
        public void Evaluate_EachKind_ConvertsAsTheCoreFunctions()
        {
            using var document = XmlDocument.Parse(Catalog);
            var nodes = XPathExpression.Compile("//item/@price").Evaluate(document);
            nodes.Kind.Should().Be(XPathResultKind.NodeSet);
            nodes.GetBoolean().Should().BeTrue();
            nodes.GetNumber().Should().Be(5);
            nodes.GetString().Should().Be("5");
            nodes.GetNodeSet().Count.Should().Be(2);

            var empty = XPathExpression.Compile("//missing").Evaluate(document);
            empty.GetBoolean().Should().BeFalse();
            empty.GetNumber().Should().Be(double.NaN);
            empty.GetString().Should().Be("");

            var boolean = XPathExpression.Compile("count(//item) = 2").Evaluate(document);
            boolean.Kind.Should().Be(XPathResultKind.Boolean);
            boolean.GetBoolean().Should().BeTrue();
            boolean.GetNumber().Should().Be(1);
            boolean.GetString().Should().Be("true");
            XPathExpression.Compile("false()").Evaluate(document).GetString().Should().Be("false");
            XPathExpression.Compile("false()").Evaluate(document).GetNumber().Should().Be(0);

            var number = XPathExpression.Compile("sum(//@price) div 2").Evaluate(document);
            number.Kind.Should().Be(XPathResultKind.Number);
            number.GetBoolean().Should().BeTrue();
            number.GetNumber().Should().Be(8.5);
            number.GetString().Should().Be("8.5");
            XPathExpression.Compile("0 div 0").Evaluate(document).GetBoolean().Should().BeFalse();
            XPathExpression.Compile("0").Evaluate(document).GetBoolean().Should().BeFalse();

            var text = XPathExpression.Compile("string(//item[2])").Evaluate(document);
            text.Kind.Should().Be(XPathResultKind.String);
            text.GetBoolean().Should().BeTrue();
            text.GetNumber().Should().Be(double.NaN);
            text.GetString().Should().Be("pear & plum");
            XPathExpression.Compile("''").Evaluate(document).GetBoolean().Should().BeFalse();
            XPathExpression.Compile("' 42 '").Evaluate(document).GetNumber().Should().Be(42);
        }

        [TestMethod]
        public void GetNodeSet_NotANodeSet_Throws()
        {
            using var document = XmlDocument.Parse(Catalog);
            var act = () => XPathExpression.Compile("1").Evaluate(document).GetNodeSet();
            act.Should().Throw<InvalidOperationException>().Which.Message.Should().Be("The XPath result is a Number, not a node-set.");
        }

        [TestMethod]
        public void DefaultResult_EveryMember_Throws()
        {
            XPathResult result = default;
            var members = new Action[]
            {
                () => _ = result.Kind,
                () => result.GetBoolean(),
                () => result.GetNumber(),
                () => result.GetString(),
                () => result.GetNodeSet(),
            };
            members.Should().AllSatisfy(member => member.Should().Throw<InvalidOperationException>().Which.Message.Should().Be("The XPath result is empty."));
        }

        [TestMethod]
        public void Select_NotANodeSet_Throws()
        {
            using var document = XmlDocument.Parse(Catalog);
            var act = () => XPathExpression.Compile("count(//item)").Select(document);
            var thrown = act.Should().Throw<XPathException>().Which;
            thrown.Message.Should().Be("The expression yields a Number, not a node-set.");
            thrown.Position.Should().BeNull();
        }

        [TestMethod]
        public void Select_FromAnElementAndFromANode_UsesItAsTheContext()
        {
            using var document = XmlDocument.Parse(Catalog);
            var second = Children(document.RootElement)[1];
            XPathExpression.Compile("string(@id)").Evaluate(second).GetString().Should().Be("2");
            XPathExpression.Compile("..").Select(second).Single().Kind.Should().Be(XPathNodeKind.Element);
            var node = XPathExpression.Compile("//item[1]").Select(document)[0];
            XPathExpression.Compile("following-sibling::item/@id").Select(node).Select(XPathRendering.Label).Should().Equal("@id=2");
            XPathExpression.Compile("string(@id)").Evaluate(node).GetString().Should().Be("1");
        }

        [TestMethod]
        public void Evaluate_ElementOnALaterRowOfARun_IsTheTextNodeOfTheRun()
        {
            using var document = XmlDocument.Parse(Catalog);
            var cdata = Children(Children(document.RootElement)[1])[1];
            cdata.ValueKind.Should().Be(XmlValueKind.CData);
            XPathExpression.Compile("string(.)").Evaluate(cdata).GetString().Should().Be("pear & plum");
            XPathExpression.Compile("count(preceding-sibling::node())").Evaluate(cdata).GetNumber().Should().Be(0);
        }

        [TestMethod]
        public void Evaluate_ContextThatIsNotANode_Throws()
        {
            using var document = XmlDocument.Parse("<a xmlns:p=\"urn:p\"><![CDATA[]]></a>");
            var expression = XPathExpression.Compile(".");
            var noElement = () => expression.Evaluate(default(XmlElement));
            noElement.Should().Throw<ArgumentException>().Which.Message.Should().Be("The context is a default XmlElement, which refers to no node. (Parameter 'context')");
            var noNode = () => expression.Evaluate(default(XPathNode));
            noNode.Should().Throw<ArgumentException>().Which.Message.Should().Be("The context is a default XPathNode, which refers to no node. (Parameter 'context')");
            var declaration = () => expression.Evaluate(Attributes(document.RootElement)[0]);
            declaration.Should().Throw<ArgumentException>().Which.Message.Should().Be("The element is a namespace declaration, which is not a node in the XPath data model. (Parameter 'context')");
            var emptyRun = () => expression.Select(Children(document.RootElement)[0]);
            emptyRun.Should().Throw<ArgumentException>().Which.Message.Should().Be("The element is a text run with no characters, which is not a node in the XPath data model. (Parameter 'context')");
            var noDocument = () => expression.Evaluate((XmlDocument)null!);
            noDocument.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("document");
        }

        [TestMethod]
        public void Evaluate_DisposedDocument_Throws()
        {
            var document = XmlDocument.Parse(Catalog);
            var root = document.RootElement;
            var node = XPathExpression.Compile("/catalog").Select(document)[0];
            document.Dispose();
            var expression = XPathExpression.Compile(".");
            var fromDocument = () => expression.Evaluate(document);
            fromDocument.Should().Throw<ObjectDisposedException>();
            var fromElement = () => expression.Select(root);
            fromElement.Should().Throw<ObjectDisposedException>();
            var fromNode = () => expression.Select(node);
            fromNode.Should().Throw<ObjectDisposedException>();
            var useNode = () => node.GetString();
            useNode.Should().Throw<ObjectDisposedException>();
        }

        [TestMethod]
        public void Node_Members_DescribeTheNode()
        {
            using var document = XmlDocument.Parse("<p:a xmlns:p=\"urn:p\" p:b=\"1\">t<!--c--><?pi data?></p:a>");
            var options = new XPathOptions { Namespaces = new Dictionary<string, string> { ["p"] = "urn:p" } };
            var nodes = XPathExpression.Compile("/ | /p:a | /p:a/@p:b | /p:a/namespace::p | /p:a/node()", options).Select(document);
            nodes.Select(node => node.Kind).Should().Equal(
                XPathNodeKind.Document, XPathNodeKind.Element, XPathNodeKind.Namespace, XPathNodeKind.Attribute,
                XPathNodeKind.Text, XPathNodeKind.Comment, XPathNodeKind.ProcessingInstruction);
            nodes.Select(node => node.LocalName).Should().Equal("", "a", "p", "b", "", "", "pi");
            nodes.Select(node => node.NamespaceUri).Should().Equal("", "urn:p", "", "urn:p", "", "", "");
            nodes.Select(node => node.Prefix).Should().Equal("", "p", "", "p", "", "", "");
            nodes.Select(node => node.GetString()).Should().Equal("t", "t", "urn:p", "1", "t", "c", "data");
            nodes.Select(node => node.TryGetElement(out _)).Should().Equal(false, true, false, true, true, true, true);
            nodes[1].GetElement().LocalName.Should().Be("a");
            nodes[3].GetElement().GetString().Should().Be("1");
            nodes[4].GetElement().ValueKind.Should().Be(XmlValueKind.Text);
            nodes[0].TryGetElement(out var none).Should().BeFalse();
            none.Should().Be(default(XmlElement));
            var document0 = () => nodes[0].GetElement();
            document0.Should().Throw<InvalidOperationException>().Which.Message.Should().Be("A node of kind Document has no XmlElement.");
            var namespace0 = () => nodes[2].GetElement();
            namespace0.Should().Throw<InvalidOperationException>().Which.Message.Should().Be("A node of kind Namespace has no XmlElement.");
        }

        [TestMethod]
        public void Node_Equality_IsTheSameNodeOfTheSameDocument()
        {
            using var document = XmlDocument.Parse(Catalog);
            using var other = XmlDocument.Parse(Catalog);
            var first = XPathExpression.Compile("//item[1]").Select(document)[0];
            var again = XPathExpression.Compile("/catalog/item[@id = 1]").Select(document)[0];
            var second = XPathExpression.Compile("//item[2]").Select(document)[0];
            var elsewhere = XPathExpression.Compile("//item[1]").Select(other)[0];
            (first == again).Should().BeTrue();
            (first != again).Should().BeFalse();
            first.Equals(again).Should().BeTrue();
            first.Equals((object)again).Should().BeTrue();
            first.GetHashCode().Should().Be(again.GetHashCode());
            (first == second).Should().BeFalse();
            (first != elsewhere).Should().BeTrue();
            first.Equals("item").Should().BeFalse();
            default(XPathNode).Equals(default(XPathNode)).Should().BeTrue();
        }

        [TestMethod]
        public void DefaultNode_EveryMember_Throws()
        {
            XPathNode node = default;
            var members = new Action[]
            {
                () => _ = node.Kind,
                () => _ = node.LocalName,
                () => _ = node.NamespaceUri,
                () => _ = node.Prefix,
                () => node.GetString(),
                () => node.GetElement(),
                () => node.TryGetElement(out _),
            };
            members.Should().AllSatisfy(member => member.Should().Throw<InvalidOperationException>().Which.Message.Should().Be("The XPath node is empty."));
        }

        [TestMethod]
        public void NodeSet_IsAReadOnlyListInDocumentOrder()
        {
            using var document = XmlDocument.Parse(Catalog);
            var set = XPathExpression.Compile("//@id | //item | /catalog").Select(document);
            set.Count.Should().Be(5);
            set.Select(XPathRendering.Label).Should().Equal("catalog", "1", "@id=1", "2", "@id=2");
            ((IEnumerable<XPathNode>)set).Select(XPathRendering.Label).Should().Equal("catalog", "1", "@id=1", "2", "@id=2");
            var labels = new List<string>();
            foreach (var node in (IEnumerable)set)
            {
                labels.Add(XPathRendering.Label((XPathNode)node));
            }

            labels.Should().Equal("catalog", "1", "@id=1", "2", "@id=2");
            var enumerator = set.GetEnumerator();
            enumerator.MoveNext().Should().BeTrue();
            enumerator.MoveNext().Should().BeTrue();
            XPathRendering.Label(enumerator.Current).Should().Be("1");
            ((IEnumerator)enumerator).Current.Should().Be(enumerator.Current);
            enumerator.Reset();
            enumerator.MoveNext().Should().BeTrue();
            XPathRendering.Label(enumerator.Current).Should().Be("catalog");
            enumerator.Dispose();
            var below = () => set[-1];
            below.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("index");
            var above = () => set[5];
            above.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("index");
        }

        [TestMethod]
        public void Variables_EachKind_IsUsedByTheExpression()
        {
            using var document = XmlDocument.Parse(Catalog);
            var variables = new XPathVariables();
            variables.Set("id", "2");
            variables.Set("min", 10d);
            variables.Set("on", true);
            variables.Set("items", XPathExpression.Compile("//item").Select(document));
            variables.Set("x", "é😀", "urn:v");
            XPathRendering.Evaluate(document, "//item[@id = $id]/@price", variables: variables).Should().Be("@price=12");
            XPathRendering.Evaluate(document, "//item[@price > $min]/@id", variables: variables).Should().Be("@id=2");
            XPathRendering.Evaluate(document, "$on and $items", variables: variables).Should().Be("true");
            XPathRendering.Evaluate(document, "$items[2]/@id", variables: variables).Should().Be("@id=2");
            XPathRendering.Evaluate(document, "$items/@price", variables: variables).Should().Be("@price=5 @price=12");
            XPathRendering.Evaluate(document, "$items//text()", variables: variables).Should().Be("'apple' 'pear & plum'");
            XPathRendering.Evaluate(document, "count($items)", variables: variables).Should().Be("2");
            var options = new XPathOptions { Namespaces = new Dictionary<string, string> { ["v"] = "urn:v" } };
            XPathRendering.Evaluate(document, "string-length($v:x)", options: options, variables: variables).Should().Be("2");
            variables.Set("id", 1d);
            XPathRendering.Evaluate(document, "//item[@id = $id]/@price", variables: variables).Should().Be("@price=5");
        }

        [TestMethod]
        public void Variables_FilteringANodeSetVariable_LeavesItAsItWas()
        {
            using var document = XmlDocument.Parse(Catalog);
            var variables = new XPathVariables();
            var items = XPathExpression.Compile("//item").Select(document);
            variables.Set("items", items);
            XPathRendering.Evaluate(document, "$items[@id = 2]", variables: variables).Should().Be("2");
            XPathRendering.Evaluate(document, "$items", variables: variables).Should().Be("1 2");
            items.Select(XPathRendering.Label).Should().Equal("1", "2");
        }

        [TestMethod]
        public void Variables_NodeSetOfAnotherDocument_Throws()
        {
            using var document = XmlDocument.Parse(Catalog);
            using var other = XmlDocument.Parse(Catalog);
            var variables = new XPathVariables();
            variables.Set("items", XPathExpression.Compile("//item").Select(other));
            var act = () => XPathExpression.Compile("count($items)").Evaluate(document, variables);
            act.Should().Throw<ArgumentException>().Which.Message.Should().Be("The variable '$items' holds nodes of another document. (Parameter 'variables')");
            XPathExpression.Compile("1").Evaluate(document, variables).GetNumber().Should().Be(1);
        }

        [TestMethod]
        public void Variables_Set_RefusesWhatCannotBeAVariable()
        {
            var variables = new XPathVariables();
            var noName = () => variables.Set(" ", "x");
            noName.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("localName");
            var noString = () => variables.Set("a", (string)null!);
            noString.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("value");
            var noNodes = () => variables.Set("a", (XPathNodeSet)null!);
            noNodes.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("value");
            var noNamespace = () => variables.Set("a", 1d, null!);
            noNamespace.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("namespaceUri");
            var noNamespaceBoolean = () => variables.Set("a", true, null!);
            noNamespaceBoolean.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("namespaceUri");
            var noNameNumber = () => variables.Set("", 1d);
            noNameNumber.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("localName");
            var noNameBoolean = () => variables.Set(null!, true);
            noNameBoolean.Should().Throw<ArgumentNullException>().Which.ParamName.Should().Be("localName");
            var noNameNodes = () => variables.Set("", (XPathNodeSet)null!);
            noNameNodes.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("localName");
            var surrogate = () => variables.Set("a", "x" + new string('\uD800', 1));
            surrogate.Should().Throw<ArgumentException>().Which.Message.Should().Be("The value holds a surrogate that is not part of a pair. (Parameter 'value')");
        }

        [TestMethod]
        [DataRow("<a xml:lang=\"\"><b/></a>", "lang('')", "true")]
        [DataRow("<a xml:lang=\"\"><b/></a>", "lang('en')", "false")]
        [DataRow("<a><b/></a>", "lang('')", "false")]
        [DataRow("<a xml:lang=\"en\"><b xml:lang=\"\"/></a>", "lang('en')", "false")]
        public void Lang_EmptyXmlLang_IsALanguage_AndNoneIsNot(string xml, string expression, string expected)
        {
            XPathRendering.Evaluate(xml, expression, "//b").Should().Be(expected);
        }

        [TestMethod]
        public void MaxOperations_IsTheRowsLookedAtPlusTheStringValuesRead()
        {
            using var document = XmlDocument.Parse("<a><b>1</b><b>2</b><b>3</b></a>");
            // child of the document: 1 row; child of a: 3 rows; and the string-value of each b, read by the comparison.
            var expression = "/a/b = 3";
            XPathExpression.Compile(expression, new XPathOptions { MaxOperations = 7 }).Evaluate(document).GetBoolean().Should().BeTrue();
            var act = () => XPathExpression.Compile(expression, new XPathOptions { MaxOperations = 6 }).Evaluate(document);
            var thrown = act.Should().Throw<XPathException>().Which;
            thrown.Message.Should().Be("MaxOperations exceeded (6).");
            thrown.Position.Should().BeNull();
            XPathExpression.Compile(expression).Evaluate(document).GetBoolean().Should().BeTrue();
        }

        [TestMethod]
        public void MaxOperations_OnceTheNameIndexIsBuilt_CountsOnlyTheElementsOfThatName()
        {
            using var document = XmlDocument.Parse("<a><b>1</b><c/><c/><c/><b>2</b></a>");
            // The first scan for a name looks at every row below the document node: a, b, its text, three c, b, its text.
            var expression = XPathExpression.Compile("count(//b)", new XPathOptions { MaxOperations = 7 });
            var first = () => expression.Evaluate(document);
            first.Should().Throw<XPathException>().Which.Message.Should().Be("MaxOperations exceeded (7).");
            // The second builds the index and looks at the two b rows only.
            expression.Evaluate(document).GetNumber().Should().Be(2);
            XPathExpression.Compile("count(//b)", new XPathOptions { MaxOperations = 2 }).Evaluate(document).GetNumber().Should().Be(2);
            var third = () => XPathExpression.Compile("count(//b)", new XPathOptions { MaxOperations = 1 }).Evaluate(document);
            third.Should().Throw<XPathException>().Which.Message.Should().Be("MaxOperations exceeded (1).");
        }

        [TestMethod]
        public void MaxOperations_BoundsAnExpressionThatCostsAPowerOfTheDocument()
        {
            var xml = "<a>" + string.Concat(Enumerable.Repeat("<b/>", 200)) + "</a>";
            using var document = XmlDocument.Parse(xml);
            var act = () => XPathExpression.Compile("count(//b[//b[//b]])", new XPathOptions { MaxOperations = 1_000_000 }).Evaluate(document);
            act.Should().Throw<XPathException>().Which.Message.Should().Be("MaxOperations exceeded (1000000).");
        }

        [TestMethod]
        public void Exception_Constructors_KeepWhatTheyAreGiven()
        {
            var inner = new InvalidOperationException("inner");
            new XPathException("plain").Position.Should().BeNull();
            new XPathException("plain").Message.Should().Be("plain");
            new XPathException("wrapped", inner).InnerException.Should().BeSameAs(inner);
            new XPathException("wrapped", inner).Position.Should().BeNull();
            new XPathException("placed", 3).Position.Should().Be(3);
        }

        #endregion

        #region Private Methods

        private static List<XmlElement> Attributes(XmlElement element)
        {
            var attributes = new List<XmlElement>();
            foreach (var attribute in element.EnumerateAttributes())
            {
                attributes.Add(attribute);
            }

            return attributes;
        }

        private static List<XmlElement> Children(XmlElement element)
        {
            var children = new List<XmlElement>();
            foreach (var child in element.EnumerateChildren())
            {
                children.Add(child);
            }

            return children;
        }

        #endregion

    }

}
