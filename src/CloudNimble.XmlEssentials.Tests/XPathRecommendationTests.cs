using System.Linq;
using CloudNimble.Text.Xml;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudNimble.XmlEssentials.Tests
{

    /// <summary>
    /// The examples of the XPath 1.0 Recommendation: every location path of section 2 and section 2.5, over a book
    /// whose every element has an <c>id</c>, and the <c>substring</c>, <c>round</c> and comparison cases of sections
    /// 4.2, 4.4 and 3.4, each with its exact result.
    /// </summary>
    [TestClass]
    public class XPathRecommendationTests
    {

        #region Fields

        /// <summary>
        /// A book: six chapters and an appendix, 50 figures, two lists and two employees. The context node of the
        /// relative paths is chapter <c>c2</c>, which has text, a title, seven paragraphs (all but the third a warning),
        /// nested divisions and a list.
        /// </summary>
        private static readonly string Book =
            "<doc id=\"doc\" lang=\"en\">"
            + "<chapter id=\"c1\"><title id=\"t1\">Introduction</title><para id=\"c1p1\"/><div id=\"d1\"><para id=\"c1p2\"/></div></chapter>"
            + "<chapter id=\"c2\" name=\"second\">before<title id=\"t2\">Two</title>"
            + "<para id=\"c2p1\" type=\"warning\"/><para id=\"c2p2\" type=\"warning\"/><para id=\"c2p3\"/><para id=\"c2p4\" type=\"warning\"/>"
            + "<para id=\"c2p5\" type=\"warning\"/><para id=\"c2p6\" type=\"warning\"/><para id=\"c2p7\" type=\"warning\"/>"
            + "<div id=\"d2\"><div id=\"d3\"><para id=\"c2p8\"/></div></div><olist id=\"o1\"><item id=\"i1\"/><item id=\"i2\"/></olist>after</chapter>"
            + "<chapter id=\"c3\"><para id=\"c3p1\"/></chapter>"
            + "<chapter id=\"c4\"><title id=\"t4\">Four</title></chapter>"
            + "<chapter id=\"c5\"><section id=\"s1\"/><section id=\"s2\"/><section id=\"s3\"/></chapter>"
            + "<appendix id=\"a1\"/><chapter id=\"c6\"/><olist id=\"o2\"><item id=\"i3\"/></olist>"
            + "<figures id=\"figs\">" + string.Concat(Enumerable.Range(1, 50).Select(i => "<figure id=\"f" + i + "\"/>")) + "</figures>"
            + "<employee id=\"e1\" secretary=\"s\" assistant=\"a\"/><employee id=\"e2\" secretary=\"s\"/>"
            + "</doc>";

        #endregion

        #region Public Methods

        [TestMethod]
        [DataRow("child::para", "//chapter[@id='c2']", "c2p1 c2p2 c2p3 c2p4 c2p5 c2p6 c2p7")]
        [DataRow("child::*", "//chapter[@id='c2']", "t2 c2p1 c2p2 c2p3 c2p4 c2p5 c2p6 c2p7 d2 o1")]
        [DataRow("child::text()", "//chapter[@id='c2']", "'before' 'after'")]
        [DataRow("child::node()", "//chapter[@id='c2']", "'before' t2 c2p1 c2p2 c2p3 c2p4 c2p5 c2p6 c2p7 d2 o1 'after'")]
        [DataRow("attribute::name", "//chapter[@id='c2']", "@name=second")]
        [DataRow("attribute::*", "//chapter[@id='c2']", "@id=c2 @name=second")]
        [DataRow("descendant::para", "//chapter[@id='c2']", "c2p1 c2p2 c2p3 c2p4 c2p5 c2p6 c2p7 c2p8")]
        [DataRow("ancestor::div", "//para[@id='c2p8']", "d2 d3")]
        [DataRow("ancestor-or-self::div", "//div[@id='d3']", "d2 d3")]
        [DataRow("descendant-or-self::para", "//chapter[@id='c2']", "c2p1 c2p2 c2p3 c2p4 c2p5 c2p6 c2p7 c2p8")]
        [DataRow("descendant-or-self::para", "//para[@id='c2p8']", "c2p8")]
        [DataRow("self::para", "//chapter[@id='c2']", "{}")]
        [DataRow("self::para", "//para[@id='c2p1']", "c2p1")]
        [DataRow("child::chapter/descendant::para", "/doc", "c1p1 c1p2 c2p1 c2p2 c2p3 c2p4 c2p5 c2p6 c2p7 c2p8 c3p1")]
        [DataRow("child::*/child::para", "/doc", "c1p1 c2p1 c2p2 c2p3 c2p4 c2p5 c2p6 c2p7 c3p1")]
        [DataRow("/", "//chapter[@id='c2']", "/")]
        [DataRow("/descendant::para", "//chapter[@id='c2']", "c1p1 c1p2 c2p1 c2p2 c2p3 c2p4 c2p5 c2p6 c2p7 c2p8 c3p1")]
        [DataRow("/descendant::olist/child::item", "//chapter[@id='c2']", "i1 i2 i3")]
        [DataRow("child::para[position()=1]", "//chapter[@id='c2']", "c2p1")]
        [DataRow("child::para[position()=last()]", "//chapter[@id='c2']", "c2p7")]
        [DataRow("child::para[position()=last()-1]", "//chapter[@id='c2']", "c2p6")]
        [DataRow("child::para[position()>1]", "//chapter[@id='c2']", "c2p2 c2p3 c2p4 c2p5 c2p6 c2p7")]
        [DataRow("following-sibling::chapter[position()=1]", "//chapter[@id='c2']", "c3")]
        [DataRow("preceding-sibling::chapter[position()=1]", "//chapter[@id='c2']", "c1")]
        [DataRow("/descendant::figure[position()=42]", "//chapter[@id='c2']", "f42")]
        [DataRow("/child::doc/child::chapter[position()=5]/child::section[position()=2]", "//chapter[@id='c2']", "s2")]
        [DataRow("child::para[attribute::type=\"warning\"]", "//chapter[@id='c2']", "c2p1 c2p2 c2p4 c2p5 c2p6 c2p7")]
        [DataRow("child::para[attribute::type='warning'][position()=5]", "//chapter[@id='c2']", "c2p6")]
        [DataRow("child::para[position()=5][attribute::type=\"warning\"]", "//chapter[@id='c2']", "c2p5")]
        [DataRow("child::chapter[child::title='Introduction']", "/doc", "c1")]
        [DataRow("child::chapter[child::title]", "/doc", "c1 c2 c4")]
        [DataRow("child::*[self::chapter or self::appendix]", "/doc", "c1 c2 c3 c4 c5 a1 c6")]
        [DataRow("child::*[self::chapter or self::appendix][position()=last()]", "/doc", "c6")]
        public void LocationPath_Section2_SelectsWhatTheRecommendationSays(string expression, string context, string expected)
        {
            XPathRendering.Evaluate(Book, expression, context).Should().Be(expected);
        }

        [TestMethod]
        [DataRow("para", "//chapter[@id='c2']", "c2p1 c2p2 c2p3 c2p4 c2p5 c2p6 c2p7")]
        [DataRow("*", "//chapter[@id='c2']", "t2 c2p1 c2p2 c2p3 c2p4 c2p5 c2p6 c2p7 d2 o1")]
        [DataRow("text()", "//chapter[@id='c2']", "'before' 'after'")]
        [DataRow("@name", "//chapter[@id='c2']", "@name=second")]
        [DataRow("@*", "//chapter[@id='c2']", "@id=c2 @name=second")]
        [DataRow("para[1]", "//chapter[@id='c2']", "c2p1")]
        [DataRow("para[last()]", "//chapter[@id='c2']", "c2p7")]
        [DataRow("*/para", "/doc", "c1p1 c2p1 c2p2 c2p3 c2p4 c2p5 c2p6 c2p7 c3p1")]
        [DataRow("/doc/chapter[5]/section[2]", "//chapter[@id='c2']", "s2")]
        [DataRow("chapter//para", "/doc", "c1p1 c1p2 c2p1 c2p2 c2p3 c2p4 c2p5 c2p6 c2p7 c2p8 c3p1")]
        [DataRow("//para", "//chapter[@id='c2']", "c1p1 c1p2 c2p1 c2p2 c2p3 c2p4 c2p5 c2p6 c2p7 c2p8 c3p1")]
        [DataRow("//olist/item", "//chapter[@id='c2']", "i1 i2 i3")]
        [DataRow(".", "//chapter[@id='c2']", "c2")]
        [DataRow(".//para", "//chapter[@id='c2']", "c2p1 c2p2 c2p3 c2p4 c2p5 c2p6 c2p7 c2p8")]
        [DataRow("..", "//chapter[@id='c2']", "doc")]
        [DataRow("../@lang", "//chapter[@id='c2']", "@lang=en")]
        [DataRow("para[@type=\"warning\"]", "//chapter[@id='c2']", "c2p1 c2p2 c2p4 c2p5 c2p6 c2p7")]
        [DataRow("para[@type=\"warning\"][5]", "//chapter[@id='c2']", "c2p6")]
        [DataRow("para[5][@type=\"warning\"]", "//chapter[@id='c2']", "c2p5")]
        [DataRow("chapter[title=\"Introduction\"]", "/doc", "c1")]
        [DataRow("chapter[title]", "/doc", "c1 c2 c4")]
        [DataRow("employee[@secretary and @assistant]", "/doc", "e1")]
        [DataRow("//para[1]", "/doc", "c1p1 c1p2 c2p1 c2p8 c3p1")]
        [DataRow("/descendant::para[1]", "/doc", "c1p1")]
        public void LocationPath_Section25_Abbreviated_SelectsWhatTheRecommendationSays(string expression, string context, string expected)
        {
            XPathRendering.Evaluate(Book, expression, context).Should().Be(expected);
        }

        [TestMethod]
        [DataRow("substring(\"12345\", 2, 3)", "'234'")]
        [DataRow("substring(\"12345\", 2)", "'2345'")]
        [DataRow("substring(\"12345\", 1.5, 2.6)", "'234'")]
        [DataRow("substring(\"12345\", 0, 3)", "'12'")]
        [DataRow("substring(\"12345\", 0 div 0, 3)", "''")]
        [DataRow("substring(\"12345\", 1, 0 div 0)", "''")]
        [DataRow("substring(\"12345\", -42, 1 div 0)", "'12345'")]
        [DataRow("substring(\"12345\", -1 div 0, 1 div 0)", "''")]
        [DataRow("substring(\"12345\", 6)", "''")]
        [DataRow("substring(\"\", 1)", "''")]
        [DataRow("substring(\"😀a😀é\", 2, 2)", "'a😀'")]
        [DataRow("substring(\"😀a😀é\", 4)", "'é'")]
        public void Substring_Section42_CountsCharacters(string expression, string expected)
        {
            XPathRendering.Evaluate(Book, expression).Should().Be(expected);
        }

        [TestMethod]
        [DataRow("round(0.5)", "1")]
        [DataRow("round(1.5)", "2")]
        [DataRow("round(-0.5)", "-0")]
        [DataRow("round(-1.5)", "-1")]
        [DataRow("round(-1.6)", "-2")]
        [DataRow("round(0 div 0)", "NaN")]
        [DataRow("round(1 div 0)", "Infinity")]
        [DataRow("round(-1 div 0)", "-Infinity")]
        [DataRow("round(0)", "0")]
        [DataRow("round(-0)", "-0")]
        [DataRow("round(-0.4)", "-0")]
        [DataRow("floor(-0.4)", "-1")]
        [DataRow("ceiling(-0.4)", "-0")]
        public void Round_Section44_RoundsTowardsPositiveInfinity(string expression, string expected)
        {
            XPathRendering.Evaluate(Book, expression).Should().Be(expected);
        }

        [TestMethod]
        [DataRow("string(1234567890123456.7)", "'1234567890123456.8'")]
        [DataRow("string(12345678901234567)", "'12345678901234568'")]
        [DataRow("string(0.000012345)", "'0.000012345'")]
        [DataRow("string(-123456789012345678)", "'-123456789012345680'")]
        public void Number_ToString_HasNoExponent(string expression, string expected)
        {
            XPathRendering.Evaluate(Book, expression).Should().Be(expected);
        }

        [TestMethod]
        [DataRow("//para/@type = 'warning'", "true")]
        [DataRow("not(//para/@type != 'warning')", "true")]
        [DataRow("//para/@type != 'warning'", "false")]
        [DataRow("//chapter/@id = //para/@id", "false")]
        [DataRow("//chapter/@name = 'second'", "true")]
        [DataRow("//chapter = //title", "true")]
        [DataRow("//title = 'Two'", "true")]
        [DataRow("count(//figure[@id = //figure/@id])", "50")]
        public void Comparison_Section34_IsExistential(string expression, string expected)
        {
            XPathRendering.Evaluate(Book, expression).Should().Be(expected);
        }

        [TestMethod]
        public void Book_HasTheShapeTheExamplesNeed()
        {
            using var document = XmlDocument.Parse(Book);
            XPathRendering.Evaluate(document, "count(//*[not(@id)])").Should().Be("0");
            XPathRendering.Evaluate(document, "count(//figure)").Should().Be("50");
        }

        #endregion

    }

}
