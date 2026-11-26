namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// The 13 axes of XPath 1.0.
    /// </summary>
    internal enum XPathAxis : byte
    {

        /// <summary>
        /// <c>ancestor</c>.
        /// </summary>
        Ancestor,

        /// <summary>
        /// <c>ancestor-or-self</c>.
        /// </summary>
        AncestorOrSelf,

        /// <summary>
        /// <c>attribute</c>, and <c>@</c>.
        /// </summary>
        Attribute,

        /// <summary>
        /// <c>child</c>, and a step with no axis.
        /// </summary>
        Child,

        /// <summary>
        /// <c>descendant</c>.
        /// </summary>
        Descendant,

        /// <summary>
        /// <c>descendant-or-self</c>, and <c>//</c>.
        /// </summary>
        DescendantOrSelf,

        /// <summary>
        /// <c>following</c>.
        /// </summary>
        Following,

        /// <summary>
        /// <c>following-sibling</c>.
        /// </summary>
        FollowingSibling,

        /// <summary>
        /// <c>namespace</c>.
        /// </summary>
        Namespace,

        /// <summary>
        /// <c>parent</c>, and <c>..</c>.
        /// </summary>
        Parent,

        /// <summary>
        /// <c>preceding</c>.
        /// </summary>
        Preceding,

        /// <summary>
        /// <c>preceding-sibling</c>.
        /// </summary>
        PrecedingSibling,

        /// <summary>
        /// <c>self</c>, and <c>.</c>.
        /// </summary>
        Self,

    }

}
