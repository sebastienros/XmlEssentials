namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A run of additive operators, or a run of multiplicative operators, which associates to the left:
    /// <c>a - b + c</c> is <c>(a - b) + c</c>.
    /// </summary>
    internal sealed class XPathArithmetic : XPathAst
    {

        #region Properties

        /// <summary>
        /// Gets the operands, one more than the operators.
        /// </summary>
        internal XPathAst[] Operands { get; }

        /// <summary>
        /// Gets the operators, the first between the first two operands.
        /// </summary>
        internal XPathArithmeticOperator[] Operators { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a run of arithmetic.
        /// </summary>
        /// <param name="operands">The operands.</param>
        /// <param name="operators">The operators.</param>
        internal XPathArithmetic(XPathAst[] operands, XPathArithmeticOperator[] operators)
            : base(operands[0].Position)
        {
            Operands = operands;
            Operators = operators;
        }

        #endregion

    }

}
