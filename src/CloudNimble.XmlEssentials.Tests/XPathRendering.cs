using System.Globalization;
using System.Linq;
using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.XPath;

namespace CloudNimble.XmlEssentials.Tests
{

    /// <summary>
    /// Writes the result of an XPath evaluation as one string, so that a test can compare the whole of it.
    /// </summary>
    /// <remarks>
    /// A node-set is written as its nodes, separated by spaces: <c>/</c> for the document node, the <c>id</c>
    /// attribute of an element that has one and its qualified name otherwise, <c>@name=value</c> for an attribute,
    /// <c>ns(prefix)</c> for a namespace node, <c>'text'</c> for a text node, <c>!text</c> for a comment and
    /// <c>?target</c> for a processing instruction. An empty node-set is <c>{}</c>. A boolean is <c>true</c> or
    /// <c>false</c>, a number is its round-trip form with <c>-0</c> for negative zero, and a string is quoted.
    /// </remarks>
    internal static class XPathRendering
    {

        #region Public Methods

        /// <summary>
        /// Evaluates an expression over a document, from the node another expression selects, and writes the result.
        /// </summary>
        public static string Evaluate(string xml, string expression, string? context = null, XPathOptions? options = null, XPathVariables? variables = null)
        {
            using var document = XmlDocument.Parse(xml);
            return Evaluate(document, expression, context, options, variables);
        }

        /// <summary>
        /// Evaluates an expression over a document, from the node another expression selects, and writes the result.
        /// </summary>
        public static string Evaluate(XmlDocument document, string expression, string? context = null, XPathOptions? options = null, XPathVariables? variables = null)
        {
            var compiled = XPathExpression.Compile(expression, options);
            var result = context is null
                ? compiled.Evaluate(document, variables)
                : compiled.Evaluate(XPathExpression.Compile(context, options).Select(document).Single(), variables);
            return Render(result);
        }

        /// <summary>
        /// Writes a node.
        /// </summary>
        public static string Label(XPathNode node)
        {
            var name = node.Prefix.Length == 0 ? node.LocalName : node.Prefix + ":" + node.LocalName;
            return node.Kind switch
            {
                XPathNodeKind.Document => "/",
                XPathNodeKind.Element => node.GetElement().TryGetAttribute("id", out var id) ? id.GetString()! : name,
                XPathNodeKind.Attribute => "@" + name + "=" + node.GetString(),
                XPathNodeKind.Namespace => "ns(" + node.LocalName + ")",
                XPathNodeKind.Text => "'" + node.GetString() + "'",
                XPathNodeKind.Comment => "!" + node.GetString(),
                _ => "?" + node.LocalName,
            };
        }

        /// <summary>
        /// Writes a result.
        /// </summary>
        public static string Render(XPathResult result)
        {
            return result.Kind switch
            {
                XPathResultKind.NodeSet => result.GetNodeSet().Count == 0 ? "{}" : string.Join(" ", result.GetNodeSet().Select(Label)),
                XPathResultKind.Boolean => result.GetBoolean() ? "true" : "false",
                XPathResultKind.Number => double.IsNegative(result.GetNumber()) && result.GetNumber() == 0 ? "-0" : result.GetNumber().ToString("R", CultureInfo.InvariantCulture),
                _ => "'" + result.GetString() + "'",
            };
        }

        #endregion

    }

}
