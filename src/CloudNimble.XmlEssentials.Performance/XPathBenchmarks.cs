using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.XPath;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BclXPathExpression = System.Xml.XPath.XPathExpression;
using EssentialsXmlDocument = CloudNimble.Text.Xml.XmlDocument;
using EssentialsXPathExpression = CloudNimble.Text.Xml.XPath.XPathExpression;

namespace CloudNimble.XmlEssentials.Performance
{

    /// <summary>
    /// Compares XPath 1.0 over <see cref="EssentialsXmlDocument"/> to <c>System.Xml.XPath</c> over
    /// <see cref="XPathDocument"/>, its read-only store, with each expression compiled once on both sides.
    /// </summary>
    /// <remarks>
    /// The document is a catalog of 1000 items, parsed once on each side, so a benchmark measures the evaluation
    /// alone. <see cref="GlobalSetup"/> evaluates every expression on both sides and throws when the results differ,
    /// so a benchmark never compares two engines that disagree.
    /// </remarks>
    [MemoryDiagnoser]
    [CategoriesColumn]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
    public class XPathBenchmarks
    {

        #region Fields

        private const string AncestorPath = "//qty[. = '7']/ancestor::item/@id";
        private const string CountPath = "count(//qty)";
        private const string FilterPath = "/catalog/item[@category = 'b']/name";
        private const string LastPath = "string(/catalog/item[last()]/name)";
        private const string PredicatesPath = "count(//item[qty > 50][@category = 'a'])";
        private const string StringsPath = "count(//name[contains(., '5') and starts-with(., 'item')])";
        private const string SumPath = "sum(//item/@price)";

        private BclXPathExpression _bclAncestor = null!;
        private BclXPathExpression _bclCount = null!;
        private BclXPathExpression _bclFilter = null!;
        private BclXPathExpression _bclLast = null!;
        private XPathNavigator _bclNavigator = null!;
        private BclXPathExpression _bclPredicates = null!;
        private BclXPathExpression _bclStrings = null!;
        private BclXPathExpression _bclSum = null!;
        private EssentialsXmlDocument _document = null!;
        private EssentialsXPathExpression _ancestor = null!;
        private EssentialsXPathExpression _count = null!;
        private EssentialsXPathExpression _filter = null!;
        private EssentialsXPathExpression _last = null!;
        private EssentialsXPathExpression _predicates = null!;
        private EssentialsXPathExpression _strings = null!;
        private EssentialsXPathExpression _sum = null!;

        #endregion

        #region Public Methods

        /// <summary>
        /// BCL: the <c>id</c> of every item whose quantity is 7, found by walking up from the quantity.
        /// </summary>
        /// <returns>The number of attributes selected.</returns>
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("XPathAncestor")]
        public int Bcl_Ancestor()
        {
            return Count(_bclNavigator.Select(_bclAncestor));
        }

        /// <summary>
        /// BCL: <c>count()</c> of a descendant path.
        /// </summary>
        /// <returns>The count.</returns>
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("XPathCount")]
        public double Bcl_Count()
        {
            return (double)_bclNavigator.Evaluate(_bclCount);
        }

        /// <summary>
        /// BCL: the names of the items of one category.
        /// </summary>
        /// <returns>The number of nodes selected.</returns>
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("XPathFilter")]
        public int Bcl_Filter()
        {
            return Count(_bclNavigator.Select(_bclFilter));
        }

        /// <summary>
        /// BCL: the string-value of the name of the last item.
        /// </summary>
        /// <returns>The string.</returns>
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("XPathLast")]
        public string Bcl_Last()
        {
            return (string)_bclNavigator.Evaluate(_bclLast);
        }

        /// <summary>
        /// BCL: two predicates, one numeric comparison and one string comparison.
        /// </summary>
        /// <returns>The count.</returns>
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("XPathPredicates")]
        public double Bcl_Predicates()
        {
            return (double)_bclNavigator.Evaluate(_bclPredicates);
        }

        /// <summary>
        /// BCL: string functions in a predicate.
        /// </summary>
        /// <returns>The count.</returns>
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("XPathStrings")]
        public double Bcl_Strings()
        {
            return (double)_bclNavigator.Evaluate(_bclStrings);
        }

        /// <summary>
        /// BCL: <c>sum()</c> of an attribute of every item.
        /// </summary>
        /// <returns>The sum.</returns>
        [Benchmark(Baseline = true)]
        [BenchmarkCategory("XPathSum")]
        public double Bcl_Sum()
        {
            return (double)_bclNavigator.Evaluate(_bclSum);
        }

        /// <summary>
        /// XmlEssentials: the <c>id</c> of every item whose quantity is 7, found by walking up from the quantity.
        /// </summary>
        /// <returns>The number of attributes selected.</returns>
        [Benchmark]
        [BenchmarkCategory("XPathAncestor")]
        public int XmlEssentials_Ancestor()
        {
            return _ancestor.Select(_document).Count;
        }

        /// <summary>
        /// XmlEssentials: <c>count()</c> of a descendant path.
        /// </summary>
        /// <returns>The count.</returns>
        [Benchmark]
        [BenchmarkCategory("XPathCount")]
        public double XmlEssentials_Count()
        {
            return _count.Evaluate(_document).GetNumber();
        }

        /// <summary>
        /// XmlEssentials: the names of the items of one category.
        /// </summary>
        /// <returns>The number of nodes selected.</returns>
        [Benchmark]
        [BenchmarkCategory("XPathFilter")]
        public int XmlEssentials_Filter()
        {
            return _filter.Select(_document).Count;
        }

        /// <summary>
        /// XmlEssentials: the string-value of the name of the last item.
        /// </summary>
        /// <returns>The string.</returns>
        [Benchmark]
        [BenchmarkCategory("XPathLast")]
        public string XmlEssentials_Last()
        {
            return _last.Evaluate(_document).GetString();
        }

        /// <summary>
        /// XmlEssentials: two predicates, one numeric comparison and one string comparison.
        /// </summary>
        /// <returns>The count.</returns>
        [Benchmark]
        [BenchmarkCategory("XPathPredicates")]
        public double XmlEssentials_Predicates()
        {
            return _predicates.Evaluate(_document).GetNumber();
        }

        /// <summary>
        /// XmlEssentials: string functions in a predicate.
        /// </summary>
        /// <returns>The count.</returns>
        [Benchmark]
        [BenchmarkCategory("XPathStrings")]
        public double XmlEssentials_Strings()
        {
            return _strings.Evaluate(_document).GetNumber();
        }

        /// <summary>
        /// XmlEssentials: <c>sum()</c> of an attribute of every item.
        /// </summary>
        /// <returns>The sum.</returns>
        [Benchmark]
        [BenchmarkCategory("XPathSum")]
        public double XmlEssentials_Sum()
        {
            return _sum.Evaluate(_document).GetNumber();
        }

        /// <summary>
        /// Disposes the document.
        /// </summary>
        [GlobalCleanup]
        public void GlobalCleanup()
        {
            _document.Dispose();
        }

        /// <summary>
        /// Parses the catalog on both sides, compiles the expressions, and checks that both engines agree on each.
        /// </summary>
        /// <exception cref="InvalidOperationException">The two engines give different results for an expression.</exception>
        [GlobalSetup]
        public void GlobalSetup()
        {
            var xml = BuildCatalog();
            _document = EssentialsXmlDocument.Parse(xml);
            _bclNavigator = new XPathDocument(new StringReader(xml)).CreateNavigator();
            _bclAncestor = BclXPathExpression.Compile(AncestorPath);
            _bclCount = BclXPathExpression.Compile(CountPath);
            _bclFilter = BclXPathExpression.Compile(FilterPath);
            _bclLast = BclXPathExpression.Compile(LastPath);
            _bclPredicates = BclXPathExpression.Compile(PredicatesPath);
            _bclStrings = BclXPathExpression.Compile(StringsPath);
            _bclSum = BclXPathExpression.Compile(SumPath);
            _ancestor = EssentialsXPathExpression.Compile(AncestorPath);
            _count = EssentialsXPathExpression.Compile(CountPath);
            _filter = EssentialsXPathExpression.Compile(FilterPath);
            _last = EssentialsXPathExpression.Compile(LastPath);
            _predicates = EssentialsXPathExpression.Compile(PredicatesPath);
            _strings = EssentialsXPathExpression.Compile(StringsPath);
            _sum = EssentialsXPathExpression.Compile(SumPath);

            Agree(AncestorPath, Values(_bclNavigator.Select(_bclAncestor)), Values(_ancestor));
            Agree(FilterPath, Values(_bclNavigator.Select(_bclFilter)), Values(_filter));
            Agree(CountPath, Bcl_Count().ToString(CultureInfo.InvariantCulture), XmlEssentials_Count().ToString(CultureInfo.InvariantCulture));
            Agree(LastPath, Bcl_Last(), XmlEssentials_Last());
            Agree(PredicatesPath, Bcl_Predicates().ToString(CultureInfo.InvariantCulture), XmlEssentials_Predicates().ToString(CultureInfo.InvariantCulture));
            Agree(StringsPath, Bcl_Strings().ToString(CultureInfo.InvariantCulture), XmlEssentials_Strings().ToString(CultureInfo.InvariantCulture));
            Agree(SumPath, Bcl_Sum().ToString(CultureInfo.InvariantCulture), XmlEssentials_Sum().ToString(CultureInfo.InvariantCulture));
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Throws when the two engines give different results.
        /// </summary>
        private static void Agree(string expression, string bcl, string essentials)
        {
            if (bcl != essentials)
            {
                throw new InvalidOperationException($"{expression}: System.Xml.XPath gives '{bcl}', XmlEssentials gives '{essentials}'.");
            }
        }

        /// <summary>
        /// Builds a catalog of 1000 items in four categories.
        /// </summary>
        private static string BuildCatalog()
        {
            var xml = new StringBuilder("<catalog>");
            for (var i = 0; i < 1000; i++)
            {
                xml.Append(CultureInfo.InvariantCulture, $"<item id=\"{i}\" category=\"{(char)('a' + (i % 4))}\" price=\"{(i % 97) + 0.25}\"><name>item {i}</name><qty>{i % 101}</qty></item>");
            }

            return xml.Append("</catalog>").ToString();
        }

        /// <summary>
        /// Counts the nodes of a BCL selection.
        /// </summary>
        private static int Count(XPathNodeIterator nodes)
        {
            var count = 0;
            while (nodes.MoveNext())
            {
                count++;
            }

            return count;
        }

        /// <summary>
        /// Writes the string-values of a BCL selection.
        /// </summary>
        private static string Values(XPathNodeIterator nodes)
        {
            var values = new StringBuilder();
            while (nodes.MoveNext())
            {
                values.Append(nodes.Current!.Value).Append('|');
            }

            return values.ToString();
        }

        /// <summary>
        /// Writes the string-values of an XmlEssentials selection.
        /// </summary>
        private string Values(EssentialsXPathExpression expression)
        {
            var values = new StringBuilder();
            foreach (var node in expression.Select(_document))
            {
                values.Append(node.GetString()).Append('|');
            }

            return values.ToString();
        }

        #endregion

    }

}
