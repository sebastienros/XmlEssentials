using System;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A node of a compiled XPath expression.
    /// </summary>
    /// <remarks>
    /// The tree is built by <see cref="XPathParser"/> and never changes, so one compiled expression can be evaluated
    /// from several threads at once. Names and literals are held as UTF-8, and every prefix has been resolved.
    /// </remarks>
    internal abstract class XPathAst
    {

        #region Fields

        /// <summary>
        /// Whether this part, as a predicate, may depend on the position of its context node: 0 when not yet known,
        /// 1 when it does not, 2 when it may.
        /// </summary>
        private byte _positional;

        #endregion

        #region Properties

        /// <summary>
        /// Gets whether this part, as a predicate, may depend on the position of its context node
        /// (<see cref="XPathPathOptimizer.IsPositional"/>).
        /// </summary>
        /// <remarks>
        /// Worked out the first time it is asked for and kept. Two threads that race write the same value.
        /// </remarks>
        internal bool IsPositional
        {
            get
            {
                if (_positional == 0)
                {
                    _positional = XPathPathOptimizer.IsPositional(this) ? (byte)2 : (byte)1;
                }

                return _positional == 2;
            }
        }

        /// <summary>
        /// Gets the zero-based character offset in the expression where this part of it starts.
        /// </summary>
        internal int Position { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a node.
        /// </summary>
        /// <param name="position">The offset in the expression where this part of it starts.</param>
        protected XPathAst(int position)
        {
            Position = position;
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Evaluates this part of the expression.
        /// </summary>
        /// <param name="evaluator">The evaluation it is part of.</param>
        /// <param name="focus">The context node, position and size.</param>
        /// <returns>The value.</returns>
        internal abstract XPathValue Evaluate(XPathEvaluator evaluator, in XPathFocus focus);

        /// <summary>
        /// Evaluates this part of the expression where a boolean is wanted: <c>boolean()</c> of its value.
        /// </summary>
        /// <param name="evaluator">The evaluation it is part of.</param>
        /// <param name="focus">The context node, position and size.</param>
        /// <returns>The boolean.</returns>
        /// <remarks>
        /// A part whose value is a boolean, or can be told from cheaply, answers without making a value.
        /// </remarks>
        internal virtual bool EvaluateBoolean(XPathEvaluator evaluator, in XPathFocus focus)
        {
            return evaluator.ToBooleanAndRelease(Evaluate(evaluator, focus));
        }

        /// <summary>
        /// Evaluates this part of the expression where a string is wanted: <c>string()</c> of its value, as UTF-8.
        /// </summary>
        /// <param name="evaluator">The evaluation it is part of.</param>
        /// <param name="focus">The context node, position and size.</param>
        /// <returns>The string.</returns>
        internal virtual ReadOnlyMemory<byte> EvaluateText(XPathEvaluator evaluator, in XPathFocus focus)
        {
            return evaluator.ToTextAndRelease(Evaluate(evaluator, focus));
        }

        #endregion

    }

}
