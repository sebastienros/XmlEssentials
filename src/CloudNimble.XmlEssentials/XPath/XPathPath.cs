namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A location path, or a filter expression followed by steps.
    /// </summary>
    internal sealed class XPathPath : XPathAst
    {

        #region Properties

        /// <summary>
        /// Gets the expression the steps start from, or <see langword="null"/> when they start from the context node
        /// or, for an absolute path, from the document node.
        /// </summary>
        internal XPathAst? Input { get; }

        /// <summary>
        /// Gets whether the path starts from the document node.
        /// </summary>
        internal bool IsAbsolute { get; }

        /// <summary>
        /// Gets the steps. Only <c>/</c> has none.
        /// </summary>
        internal XPathStep[] Steps { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a path.
        /// </summary>
        /// <param name="isAbsolute">Whether the path starts from the document node.</param>
        /// <param name="input">The expression the steps start from, or <see langword="null"/>.</param>
        /// <param name="steps">The steps.</param>
        /// <param name="position">The offset of the first character of the path.</param>
        internal XPathPath(bool isAbsolute, XPathAst? input, XPathStep[] steps, int position)
            : base(position)
        {
            IsAbsolute = isAbsolute;
            Input = input;
            Steps = steps;
        }

        #endregion

        #region Internal Methods

        /// <inheritdoc />
        internal override XPathValue Evaluate(XPathEvaluator evaluator, in XPathFocus focus)
        {
            return evaluator.Path(this, focus);
        }

        #endregion

    }

}
