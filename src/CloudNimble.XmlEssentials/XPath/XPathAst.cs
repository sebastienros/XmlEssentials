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

        #region Properties

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

    }

}
