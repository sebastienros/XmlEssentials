namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// Specifies which of the four XPath 1.0 types a result is.
    /// </summary>
    public enum XPathResultKind : byte
    {

        /// <summary>
        /// A set of nodes, in document order and without duplicates.
        /// </summary>
        NodeSet = 0,

        /// <summary>
        /// <see langword="true"/> or <see langword="false"/>.
        /// </summary>
        Boolean = 1,

        /// <summary>
        /// An IEEE 754 double-precision number.
        /// </summary>
        Number = 2,

        /// <summary>
        /// A string.
        /// </summary>
        String = 3,

    }

}
