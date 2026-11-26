namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A string literal: <c>'text'</c> or <c>"text"</c>.
    /// </summary>
    internal sealed class XPathLiteral : XPathAst
    {

        #region Properties

        /// <summary>
        /// Gets the text of the literal, without its quotes, as UTF-8.
        /// </summary>
        internal byte[] Value { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a literal.
        /// </summary>
        /// <param name="value">The text, as UTF-8.</param>
        /// <param name="position">The offset of the opening quote.</param>
        internal XPathLiteral(byte[] value, int position)
            : base(position)
        {
            Value = value;
        }

        #endregion

    }

}
