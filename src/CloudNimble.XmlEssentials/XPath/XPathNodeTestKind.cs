namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// The forms of an XPath 1.0 node test.
    /// </summary>
    internal enum XPathNodeTestKind : byte
    {

        /// <summary>
        /// <c>*</c>: any node of the principal node type of the axis.
        /// </summary>
        Any,

        /// <summary>
        /// <c>comment()</c>.
        /// </summary>
        Comment,

        /// <summary>
        /// <c>name</c> or <c>p:name</c>: a node of the principal node type with that expanded name.
        /// </summary>
        Name,

        /// <summary>
        /// <c>p:*</c>: a node of the principal node type in that namespace.
        /// </summary>
        NamespaceWildcard,

        /// <summary>
        /// <c>node()</c>: any node.
        /// </summary>
        Node,

        /// <summary>
        /// <c>processing-instruction()</c>, with or without a target.
        /// </summary>
        ProcessingInstruction,

        /// <summary>
        /// <c>text()</c>.
        /// </summary>
        Text,

    }

}
