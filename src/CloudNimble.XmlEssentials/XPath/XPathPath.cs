using System;

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
        /// Gets whether the path is <c>.</c>: relative, with no input and no step, so its one node is the context node.
        /// </summary>
        /// <remarks>
        /// Known when the path is compiled, so evaluating <c>.</c> as a boolean or a string reads the context node and
        /// nothing else, as <c>ContextQuery</c> does in <c>System.Xml.XPath</c>.
        /// </remarks>
        internal bool IsContextNode { get; }

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
            IsContextNode = !isAbsolute && input is null && steps.Length == 0;
        }

        #endregion

        #region Internal Methods

        /// <inheritdoc />
        internal override XPathValue Evaluate(XPathEvaluator evaluator, in XPathFocus focus)
        {
            return evaluator.Path(this, focus);
        }

        /// <inheritdoc />
        internal override bool EvaluateBoolean(XPathEvaluator evaluator, in XPathFocus focus)
        {
            return IsContextNode || evaluator.PathBoolean(this, focus);
        }

        /// <inheritdoc />
        internal override ReadOnlyMemory<byte> EvaluateText(XPathEvaluator evaluator, in XPathFocus focus)
        {
            return IsContextNode ? evaluator.StringValue(focus.Node) : evaluator.PathText(this, focus);
        }

        #endregion

    }

}
