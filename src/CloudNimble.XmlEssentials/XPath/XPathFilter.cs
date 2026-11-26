namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A primary expression with predicates: <c>(expression)[predicate]</c> or <c>$name[predicate]</c>.
    /// </summary>
    /// <remarks>
    /// The predicates filter a node-set in document order, whatever axis selected it.
    /// </remarks>
    internal sealed class XPathFilter : XPathAst
    {

        #region Properties

        /// <summary>
        /// Gets the predicates, one or more.
        /// </summary>
        internal XPathAst[] Predicates { get; }

        /// <summary>
        /// Gets the primary expression, which must be a node-set.
        /// </summary>
        internal XPathAst Primary { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a filter.
        /// </summary>
        /// <param name="primary">The primary expression.</param>
        /// <param name="predicates">The predicates.</param>
        internal XPathFilter(XPathAst primary, XPathAst[] predicates)
            : base(primary.Position)
        {
            Primary = primary;
            Predicates = predicates;
        }

        #endregion

    }

}
