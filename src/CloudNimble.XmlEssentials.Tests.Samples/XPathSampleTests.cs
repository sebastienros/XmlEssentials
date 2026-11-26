using CloudNimble.XmlEssentials.Samples;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudNimble.XmlEssentials.Tests.Samples
{

    /// <summary>
    /// Runs sample 47 and compares what each part prints with the output the XPath guide shows.
    /// </summary>
    [TestClass]
    public class XPathSampleTests
    {

        #region Public Methods

        [TestMethod]
        public void Sample47_QueryWithXPath_NodeSet()
        {
            SampleOutput.Capture(QueryWithXPath.Run).Should().Be(
                "title: XML in Practice\n" +
                "title: Le XML");
        }

        [TestMethod]
        public void Sample47_QueryWithXPath_Boolean()
        {
            SampleOutput.Capture(QueryWithXPath.Boolean).Should().Be("Boolean: True");
        }

        [TestMethod]
        public void Sample47_QueryWithXPath_Number()
        {
            SampleOutput.Capture(QueryWithXPath.Number).Should().Be("Number: 25.483333333333334");
        }

        [TestMethod]
        public void Sample47_QueryWithXPath_String()
        {
            SampleOutput.Capture(QueryWithXPath.String).Should().Be(
                "XML in Practice (en, EUR 39.95)\n" +
                "Le XML (fr, EUR 24.50) soft cover\n" +
                "Paths and Queries (en, EUR 12.00)");
        }

        #endregion

    }

}
