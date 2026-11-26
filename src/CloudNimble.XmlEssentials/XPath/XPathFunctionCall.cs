namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A call of a core function.
    /// </summary>
    internal sealed class XPathFunctionCall : XPathAst
    {

        #region Properties

        /// <summary>
        /// Gets the arguments, as many as the function takes.
        /// </summary>
        internal XPathAst[] Arguments { get; }

        /// <summary>
        /// Gets the function.
        /// </summary>
        internal XPathFunction Function { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a function call.
        /// </summary>
        /// <param name="function">The function.</param>
        /// <param name="arguments">The arguments.</param>
        /// <param name="position">The offset of the function name.</param>
        internal XPathFunctionCall(XPathFunction function, XPathAst[] arguments, int position)
            : base(position)
        {
            Function = function;
            Arguments = arguments;
        }

        #endregion

        #region Internal Methods

        /// <inheritdoc />
        internal override XPathValue Evaluate(XPathEvaluator evaluator, in XPathFocus focus)
        {
            return XPathCoreFunctions.Call(evaluator, this, focus);
        }


        /// <inheritdoc />
        internal override bool EvaluateBoolean(XPathEvaluator evaluator, in XPathFocus focus)
        {
            return XPathCoreFunctions.CallBoolean(evaluator, this, focus);
        }

        #endregion

    }

}
