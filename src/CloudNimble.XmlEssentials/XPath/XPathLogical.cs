namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A run of <c>or</c>, or a run of <c>and</c>, which is evaluated from left to right and stops as soon as the
    /// result is known.
    /// </summary>
    internal sealed class XPathLogical : XPathAst
    {

        #region Properties

        /// <summary>
        /// Gets whether the operator is <c>or</c>; otherwise it is <c>and</c>.
        /// </summary>
        internal bool IsOr { get; }

        /// <summary>
        /// Gets the operands, two or more.
        /// </summary>
        internal XPathAst[] Operands { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a run of <c>or</c> or of <c>and</c>.
        /// </summary>
        /// <param name="isOr">Whether the operator is <c>or</c>.</param>
        /// <param name="operands">The operands.</param>
        internal XPathLogical(bool isOr, XPathAst[] operands)
            : base(operands[0].Position)
        {
            IsOr = isOr;
            Operands = operands;
        }

        #endregion

        #region Internal Methods

        /// <inheritdoc />
        internal override XPathValue Evaluate(XPathEvaluator evaluator, in XPathFocus focus)
        {
            return evaluator.Logical(this, focus);
        }

        #endregion

    }

}
