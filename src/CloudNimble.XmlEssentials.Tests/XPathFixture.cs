using System.Reflection;
using CloudNimble.Text.Xml;

namespace CloudNimble.XmlEssentials.Tests
{

    /// <summary>
    /// The document the XPath navigator tests are written against, with every kind of node in it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rows of <see cref="Xml"/>, in order (white space outside the document element is not kept):
    /// </para>
    /// <code>
    ///  0 comment c0          9 text t2            18 attribute xmlns=""
    ///  1 PI p0 d0           10 e                  19 attribute xmlns:a=""
    ///  2 r                  11 attribute xmlns:b  20 h
    ///  3 attribute xmlns    12 b:f                21 attribute xml:lang
    ///  4 attribute xmlns:a  13 attribute a:x      22 comment c1
    ///  5 attribute id       14 text u             23 PI p1 d1
    ///  6 attribute a:k      15 text w             24 CDATA, empty
    ///  7 text t1            16 CDATA, empty       25 comment c2
    ///  8 CDATA c1           17 g                  26 PI p2
    /// </code>
    /// <para>
    /// Rows 7 to 9 are one text node, and so are rows 15 and 16. Rows 14 and 15 are adjacent text rows with
    /// different parents. Row 24 is a text run with no characters, which is not a node. The document is XML 1.1,
    /// so that <c>xmlns:a=""</c> takes the binding of <c>a</c> away.
    /// </para>
    /// </remarks>
    internal static class XPathFixture
    {

        #region Fields

        /// <summary>
        /// The parent of each row of <see cref="Xml"/>.
        /// </summary>
        public static readonly int[] Parents = [-1, -1, -1, 2, 2, 2, 2, 2, 2, 2, 2, 10, 10, 12, 10, 2, 2, 2, 17, 17, 17, 20, 2, 2, 2, -1, -1];

        /// <summary>
        /// The fixture document.
        /// </summary>
        public const string Xml =
            "<?xml version=\"1.1\"?>\n<!--c0--><?p0 d0?>\n"
            + "<r xmlns=\"urn:d\" xmlns:a=\"urn:a\" id=\"1\" a:k=\"v\">t1<![CDATA[c1]]>t2"
            + "<e xmlns:b=\"urn:b\"><b:f a:x=\"2\"/>u</e>w<![CDATA[]]>"
            + "<g xmlns=\"\" xmlns:a=\"\"><h xml:lang=\"en\"/></g><!--c1--><?p1 d1?><![CDATA[]]></r>\n"
            + "<!--c2--><?p2?>";

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns whether <paramref name="document"/> has built its parent table.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <returns><see langword="true"/> once the table has been built.</returns>
        /// <remarks>
        /// The table is private state. It is read here, by reflection, so that the library carries no member that
        /// only a test calls.
        /// </remarks>
        public static bool HasParentTable(XmlDocument document)
        {
            return typeof(XmlDocument).GetField("_parents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(document) is not null;
        }

        #endregion

    }

}
