namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// One or more unary minus signs in front of an operand.
    /// </summary>
    /// <remarks>
    /// <c>--x</c> is <c>number(x)</c>, not <c>x</c>, so an even run of signs still converts its operand. The run is
    /// held as one node, so that a long one costs no depth.
    /// </remarks>
    internal sealed class XPathNegation : XPathAst
    {

        #region Properties

        /// <summary>
        /// Gets whether the number of signs is odd, so that the value is negated.
        /// </summary>
        internal bool Negate { get; }

        /// <summary>
        /// Gets the operand.
        /// </summary>
        internal XPathAst Operand { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a negation.
        /// </summary>
        /// <param name="operand">The operand.</param>
        /// <param name="negate">Whether the number of signs is odd.</param>
        /// <param name="position">The offset of the first sign.</param>
        internal XPathNegation(XPathAst operand, bool negate, int position)
            : base(position)
        {
            Operand = operand;
            Negate = negate;
        }

        #endregion

        #region Internal Methods

        /// <inheritdoc />
        internal override XPathValue Evaluate(XPathEvaluator evaluator, in XPathFocus focus)
        {
            return evaluator.Negation(this, focus);
        }

        #endregion

    }

}
