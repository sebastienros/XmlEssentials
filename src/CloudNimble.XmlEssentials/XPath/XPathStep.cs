namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A step of a location path: an axis, a node test and predicates.
    /// </summary>
    /// <remarks>
    /// The abbreviations are expanded: <c>.</c> is <c>self::node()</c>, <c>..</c> is <c>parent::node()</c>,
    /// <c>@</c> is <c>attribute::</c>, and <c>//</c> is <c>/descendant-or-self::node()/</c>.
    /// </remarks>
    internal sealed class XPathStep
    {

        #region Properties

        /// <summary>
        /// Gets the axis.
        /// </summary>
        internal XPathAxis Axis { get; }

        /// <summary>
        /// Gets whether the proximity position on the axis counts from the end of document order:
        /// <c>ancestor</c>, <c>ancestor-or-self</c>, <c>preceding</c> and <c>preceding-sibling</c>.
        /// </summary>
        internal bool IsReverse => Axis is XPathAxis.Ancestor or XPathAxis.AncestorOrSelf or XPathAxis.Preceding or XPathAxis.PrecedingSibling;

        /// <summary>
        /// Gets the predicates, which may be none.
        /// </summary>
        internal XPathAst[] Predicates { get; }

        /// <summary>
        /// Gets the node test.
        /// </summary>
        internal XPathNodeTest Test { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a step.
        /// </summary>
        /// <param name="axis">The axis.</param>
        /// <param name="test">The node test.</param>
        /// <param name="predicates">The predicates.</param>
        internal XPathStep(XPathAxis axis, XPathNodeTest test, XPathAst[] predicates)
        {
            Axis = axis;
            Test = test;
            Predicates = predicates;
        }

        #endregion

    }

}
