using System.Collections.Generic;
using System.Globalization;
using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.XPath;

namespace CloudNimble.XmlEssentials.Tests.Aot
{

    /// <summary>
    /// Checks, in a Native AOT program, that an XPath expression compiles and evaluates to each of the four kinds of
    /// result.
    /// </summary>
    public static class XPathFeatures
    {

        #region Internal Methods

        /// <summary>
        /// Runs every check, and throws on the first result that differs from the expected one.
        /// </summary>
        internal static void Check()
        {
            using var document = XmlDocument.Parse("<o:order xmlns:o=\"urn:order\"><line sku=\"a\" qty=\"2\"/><line sku=\"b\" qty=\"3\"/></o:order>");
            var options = new XPathOptions { Namespaces = new Dictionary<string, string> { ["o"] = "urn:order" } };
            var variables = new XPathVariables();
            variables.Set("sku", "b");

            var lines = XPathExpression.Compile("/o:order/line[@qty > 1]/@sku", options).Select(document);
            Program.CheckEqual(lines.Count + ":" + lines[0].GetString() + "," + lines[1].GetString(), "2:a,b", "xpath node-set");
            Program.CheckEqual(XPathExpression.Compile("count(//line) = 2").Evaluate(document).GetBoolean().ToString(), "True", "xpath boolean");
            Program.CheckEqual(XPathExpression.Compile("sum(//@qty) div 2").Evaluate(document).GetNumber().ToString(CultureInfo.InvariantCulture), "2.5", "xpath number");
            Program.CheckEqual(XPathExpression.Compile("concat(name(/*), ':', //line[@sku = $sku]/@qty)").Evaluate(document, variables).GetString(), "o:order:3", "xpath string");
        }

        #endregion

    }

}
