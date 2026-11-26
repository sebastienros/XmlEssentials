namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A run of <c>|</c>: the union of node-sets.
    /// </summary>
    internal sealed class XPathUnion : XPathAst
    {

        #region Properties

        /// <summary>
        /// Gets the operands, two or more, each of which must be a node-set.
        /// </summary>
        internal XPathAst[] Operands { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a union.
        /// </summary>
        /// <param name="operands">The operands.</param>
        internal XPathUnion(XPathAst[] operands)
            : base(operands[0].Position)
        {
            Operands = operands;
        }

        #endregion

        #region Internal Methods

        /// <inheritdoc />
        internal override XPathValue Evaluate(XPathEvaluator evaluator, in XPathFocus focus)
        {
            return evaluator.Union(this, focus);
        }

        #endregion

    }

}
