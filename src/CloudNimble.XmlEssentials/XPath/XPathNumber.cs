namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A number literal: <c>Digits ('.' Digits?)? | '.' Digits</c>.
    /// </summary>
    internal sealed class XPathNumber : XPathAst
    {

        #region Properties

        /// <summary>
        /// Gets the value: the IEEE 754 double nearest to the decimal number, or positive infinity when it is too large.
        /// </summary>
        internal double Value { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a number literal.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="position">The offset of the first character.</param>
        internal XPathNumber(double value, int position)
            : base(position)
        {
            Value = value;
        }

        #endregion

    }

}
