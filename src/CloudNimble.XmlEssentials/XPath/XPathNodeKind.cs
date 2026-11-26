namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// Specifies the kind of a node in the XPath 1.0 data model.
    /// </summary>
    /// <remarks>
    /// The seven kinds are those of the XPath 1.0 Recommendation, section 5. A text node is a run of adjacent text
    /// and CDATA sections that holds at least one character. A namespace node stands for one prefix in scope on an
    /// element; the document holds no row for it.
    /// </remarks>
    public enum XPathNodeKind : byte
    {

        /// <summary>
        /// The document node, which is the parent of the document element.
        /// </summary>
        Document = 0,

        /// <summary>
        /// An element.
        /// </summary>
        Element = 1,

        /// <summary>
        /// An attribute that is not a namespace declaration.
        /// </summary>
        Attribute = 2,

        /// <summary>
        /// A prefix in scope on an element, and the namespace URI it is bound to.
        /// </summary>
        Namespace = 3,

        /// <summary>
        /// A run of adjacent text and CDATA sections that holds at least one character.
        /// </summary>
        Text = 4,

        /// <summary>
        /// A comment.
        /// </summary>
        Comment = 5,

        /// <summary>
        /// A processing instruction.
        /// </summary>
        ProcessingInstruction = 6,

    }

}
