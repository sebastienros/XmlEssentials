using System.Collections.Generic;
using System.IO;
using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.XPath;

namespace CloudNimble.XmlEssentials.Samples
{

    /// <summary>
    /// Sample 47: queries an <see cref="XmlDocument"/> with XPath 1.0 expressions, one for each kind of result.
    /// </summary>
    public static class QueryWithXPath
    {

        #region Fields

        private const string Catalog = """
            <catalog xmlns:m="urn:media">
              <book id="b1" lang="en"><title>XML in Practice</title><price>39.95</price></book>
              <book id="b2" lang="fr"><title>Le XML</title><price>24.50</price><m:cover>soft</m:cover></book>
              <book id="b3" lang="en"><title>Paths and Queries</title><price>12.00</price></book>
            </catalog>
            """;

        #endregion

        #region Public Methods

        /// <summary>
        /// Selects a node-set: the titles of the books that cost more than 20.
        /// </summary>
        /// <param name="output">The writer that receives what the sample prints.</param>
        public static void Run(TextWriter output)
        {
            using XmlDocument document = XmlDocument.Parse(Catalog);
            XPathExpression expensive = XPathExpression.Compile("/catalog/book[price > 20]/title");

            foreach (XPathNode title in expensive.Select(document))
            {
                output.WriteLine($"{title.LocalName}: {title.GetString()}");
            }
        }

        /// <summary>
        /// Evaluates a boolean: whether every book has a price.
        /// </summary>
        /// <param name="output">The writer that receives what the sample prints.</param>
        public static void Boolean(TextWriter output)
        {
            using XmlDocument document = XmlDocument.Parse(Catalog);
            XPathResult result = XPathExpression.Compile("not(/catalog/book[not(price)])").Evaluate(document);
            output.WriteLine($"{result.Kind}: {result.GetBoolean()}");
        }

        /// <summary>
        /// Evaluates a number: the average price.
        /// </summary>
        /// <param name="output">The writer that receives what the sample prints.</param>
        public static void Number(TextWriter output)
        {
            using XmlDocument document = XmlDocument.Parse(Catalog);
            XPathResult result = XPathExpression.Compile("sum(//price) div count(//book)").Evaluate(document);
            output.WriteLine($"{result.Kind}: {result.GetString()}");
        }

        /// <summary>
        /// Evaluates a string, from an element as the context node, with a variable and a prefix.
        /// </summary>
        /// <param name="output">The writer that receives what the sample prints.</param>
        public static void String(TextWriter output)
        {
            using XmlDocument document = XmlDocument.Parse(Catalog);
            var options = new XPathOptions { Namespaces = new Dictionary<string, string> { ["m"] = "urn:media" } };
            XPathExpression describe = XPathExpression.Compile("concat(title, ' (', @lang, ', ', $currency, price, ')', substring(' soft cover', 1, 11 * boolean(m:cover)))", options);

            var variables = new XPathVariables();
            variables.Set("currency", "EUR ");
            foreach (XmlElement book in document.RootElement.EnumerateChildren())
            {
                if (book.ValueKind is XmlValueKind.Element)
                {
                    output.WriteLine(describe.Evaluate(book, variables).GetString());
                }
            }
        }

        #endregion

    }

}
