using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.XPath;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudNimble.XmlEssentials.Tests
{

    /// <summary>
    /// XPath over documents parsed with other options, from several threads at once, and with the parent table
    /// built only for what needs it.
    /// </summary>
    [TestClass]
    public class XPathBehaviorTests
    {

        #region Fields

        private const string Indented = "<a>\n  <b>x<!--c-->y</b>\n  <c/>\n</a>";

        private const string Indexed = "<r xmlns:p=\"urn:p\"><e n=\"1\"><f/></e><g/><e n=\"2\"><e n=\"3\"/></e><p:e/></r>";

        private const string Feed = "<feed><entry id=\"1\"><title>one</title></entry><entry id=\"2\"><title>two</title></entry></feed>";

        #endregion

        #region Public Methods

        [TestMethod]
        [DataRow(XmlCommentHandling.Allow, XmlWhitespaceHandling.Significant, "9 5 1 2 'xy'")]
        [DataRow(XmlCommentHandling.Skip, XmlWhitespaceHandling.Significant, "7 4 0 1 'xy'")]
        [DataRow(XmlCommentHandling.Allow, XmlWhitespaceHandling.Insignificant, "6 2 1 2 'xy'")]
        [DataRow(XmlCommentHandling.Skip, XmlWhitespaceHandling.Insignificant, "4 1 0 1 'xy'")]
        public void DocumentOptions_TheDataModelIsThatOfTheDocumentAsParsed(XmlCommentHandling comments, XmlWhitespaceHandling whitespace, string expected)
        {
            using var document = XmlDocument.Parse(Indented, new XmlDocumentOptions { CommentHandling = comments, WhitespaceHandling = whitespace });
            string[] expressions = ["count(//node())", "count(//text())", "count(//comment())", "count(//b/text())", "string(//b)"];
            string.Join(" ", expressions.Select(expression => XPathRendering.Evaluate(document, expression))).Should().Be(expected);
        }

        [TestMethod]
        public void DocumentOptions_SkippedComment_JoinsTheTextOnEitherSide()
        {
            using var document = XmlDocument.Parse(Indented, new XmlDocumentOptions { CommentHandling = XmlCommentHandling.Skip });
            XPathRendering.Evaluate(document, "//b/text()").Should().Be("'xy'");
            XPathRendering.Evaluate(document, "//b/node()").Should().Be("'xy'");
        }

        [TestMethod]
        public void Evaluate_PathsThatNeedNoParent_DoNotBuildTheParentTable()
        {
            using var document = XmlDocument.Parse(Feed);
            var variables = new XPathVariables();
            variables.Set("id", "2");
            XPathRendering.Evaluate(document, "/feed/entry[@id = $id]/title", variables: variables).Should().Be("title");
            XPathRendering.Evaluate(document, "string(/feed/entry[@id = $id]/title)", variables: variables).Should().Be("'two'");
            XPathRendering.Evaluate(document, "count(//*)").Should().Be("5");
            XPathRendering.Evaluate(document, "//entry/@id").Should().Be("@id=1 @id=2");
            XPathRendering.Evaluate(document, "count(//title[. = 'one'])").Should().Be("1");
            XPathRendering.Evaluate(document, "count(/feed/*/title/text())").Should().Be("2");
            XPathRendering.Evaluate(document, "count(//entry[1]/following::title)").Should().Be("1");
            XPathRendering.Evaluate(document, "count(//entry[2]/preceding::*)").Should().Be("2");
            XPathRendering.Evaluate(document, "count(//@id/..)").Should().Be("2");
            XPathRendering.Evaluate(document, "count(//text())").Should().Be("2");
            XPathFixture.HasParentTable(document).Should().BeFalse();
        }

        [TestMethod]
        [DataRow("//title/..")]
        [DataRow("//entry/ancestor::*")]
        [DataRow("//entry[1]/following-sibling::*")]
        [DataRow("//entry[2]/preceding-sibling::*")]
        [DataRow("/feed/namespace::*")]
        [DataRow("//title[lang('en')]")]
        public void Evaluate_AxisThatWalksUp_BuildsTheParentTable(string expression)
        {
            using var document = XmlDocument.Parse(Feed);
            XPathExpression.Compile(expression).Evaluate(document);
            XPathFixture.HasParentTable(document).Should().BeTrue();
        }

        [TestMethod]
        public void Evaluate_OneExpressionFromManyThreads_OverADocumentWithNoTableYet()
        {
            var xml = "<r>" + string.Concat(Enumerable.Range(0, 500).Select(i => "<e n=\"" + i + "\">t" + i + "<f/>\n</e>")) + "</r>";
            using var document = XmlDocument.Parse(xml);
            var expression = XPathExpression.Compile("sum(//f/../@n) + count(//text()/following-sibling::*)");
            XPathFixture.HasParentTable(document).Should().BeFalse();
            const int threads = 8;
            var results = new double[threads];
            using var start = new Barrier(threads);
            Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, i =>
            {
                start.SignalAndWait();
                results[i] = expression.Evaluate(document).GetNumber();
            });
            results.Should().AllBeEquivalentTo(124750 + 500);
            XPathFixture.HasParentTable(document).Should().BeTrue();
        }

        [TestMethod]
        [DataRow("count(//e)", "3")]
        [DataRow("count(/r/descendant::e)", "3")]
        [DataRow("//e[1]/following::e/@n", "@n=2 @n=3")]
        [DataRow("//f/preceding::e/@n", "{}")]
        [DataRow("//g/preceding::e/@n", "@n=1")]
        [DataRow("//e[@n = 3]/preceding::e/@n", "@n=1")]
        [DataRow("count(//g/preceding::f)", "1")]
        [DataRow("//e[@n = 2]/descendant-or-self::e/@n", "@n=2 @n=3")]
        [DataRow("//e[@n = 2]/descendant::e/@n", "@n=3")]
        [DataRow("count(//p:e)", "1")]
        [DataRow("count(//missing)", "0")]
        [DataRow("count(//f/following::e)", "2")]
        public void NameIndex_GivesWhatTheScanGives_FromTheSecondScanOn(string expression, string expected)
        {
            using var document = XmlDocument.Parse(Indexed);
            var options = new XPathOptions { Namespaces = new System.Collections.Generic.Dictionary<string, string> { ["p"] = "urn:p" } };
            var first = XPathRendering.Evaluate(document, expression, options: options);
            XPathRendering.Evaluate(document, expression, options: options).Should().Be(first);
            HasNameIndex(document).Should().BeTrue();
            XPathRendering.Evaluate(document, expression, options: options).Should().Be(expected);
            first.Should().Be(expected);
        }

        [TestMethod]
        public void NameIndex_OneScan_IsNotBuilt_AndDisposeLetsItGo()
        {
            var document = XmlDocument.Parse(Indexed);
            XPathRendering.Evaluate(document, "count(//e)").Should().Be("3");
            HasNameIndex(document).Should().BeFalse();
            XPathRendering.Evaluate(document, "count(//*)").Should().Be("7");
            XPathRendering.Evaluate(document, "count(r)").Should().Be("1");
            HasNameIndex(document).Should().BeFalse();
            XPathRendering.Evaluate(document, "count(//f)").Should().Be("1");
            HasNameIndex(document).Should().BeTrue();
            document.Dispose();
            HasNameIndex(document).Should().BeFalse();
        }

        [TestMethod]
        public void NameIndex_ThreadsThatRace_AllGetTheSameNodes()
        {
            using var document = XmlDocument.Parse(Indexed);
            var expression = XPathExpression.Compile("sum(//e/@n)");
            var results = new double[8];
            using var start = new Barrier(results.Length);
            Parallel.For(0, results.Length, new ParallelOptions { MaxDegreeOfParallelism = results.Length }, i =>
            {
                start.SignalAndWait();
                results[i] = expression.Evaluate(document).GetNumber();
                results[i] += expression.Evaluate(document).GetNumber();
            });
            results.Should().AllBeEquivalentTo(12d);
            HasNameIndex(document).Should().BeTrue();
        }

        [TestMethod]
        public void Evaluate_NestingDeeperThanTheStackCheck_IsEvaluated()
        {
            var expression = string.Concat(Enumerable.Repeat("not(", 300)) + "true()" + new string(')', 300);
            using var document = XmlDocument.Parse(Feed);
            XPathExpression.Compile(expression, new XPathOptions { MaxDepth = 400 }).Evaluate(document).GetBoolean().Should().BeTrue();
            var sum = string.Concat(Enumerable.Repeat("1 + (", 200)) + "0" + new string(')', 200);
            XPathExpression.Compile(sum, new XPathOptions { MaxDepth = 400 }).Evaluate(document).GetNumber().Should().Be(200);
        }

        [TestMethod]
        public void Evaluate_OneExpressionOverManyDocuments_GivesEachItsOwnResult()
        {
            var expression = XPathExpression.Compile("string(/a/@v)");
            var results = Enumerable.Range(0, 20).AsParallel().Select(i =>
            {
                using var document = XmlDocument.Parse("<a v=\"" + i + "\"/>");
                return expression.Evaluate(document).GetString();
            }).ToArray();
            results.Should().Equal(Enumerable.Range(0, 20).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }

        #endregion

        #region Private Methods

        private static bool HasNameIndex(XmlDocument document)
        {
            return typeof(XmlDocument).GetField("_nameIndex", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(document) is not null;
        }

        #endregion

    }

}
