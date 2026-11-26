namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A run of equality operators, or a run of relational operators, which associates to the left:
    /// <c>a = b != c</c> is <c>(a = b) != c</c>.
    /// </summary>
    internal sealed class XPathComparison : XPathAst
    {

        #region Properties

        /// <summary>
        /// Gets the operands, one more than the operators.
        /// </summary>
        internal XPathAst[] Operands { get; }

        /// <summary>
        /// Gets the operators, the first between the first two operands.
        /// </summary>
        internal XPathComparisonOperator[] Operators { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a run of comparisons.
        /// </summary>
        /// <param name="operands">The operands.</param>
        /// <param name="operators">The operators.</param>
        internal XPathComparison(XPathAst[] operands, XPathComparisonOperator[] operators)
            : base(operands[0].Position)
        {
            Operands = operands;
            Operators = operators;
        }

        #endregion

        #region Internal Methods

        /// <inheritdoc />
        internal override XPathValue Evaluate(XPathEvaluator evaluator, in XPathFocus focus)
        {
            return XPathValue.FromBoolean(evaluator.ComparisonBoolean(this, focus));
        }


        /// <inheritdoc />
        internal override bool EvaluateBoolean(XPathEvaluator evaluator, in XPathFocus focus)
        {
            return evaluator.ComparisonBoolean(this, focus);
        }

        #endregion

    }

}
