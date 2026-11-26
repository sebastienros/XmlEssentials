namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A token of an XPath expression: its kind, and where it is in the expression.
    /// </summary>
    /// <param name="Kind">The kind.</param>
    /// <param name="Start">The offset of the first character.</param>
    /// <param name="Length">The number of characters.</param>
    /// <param name="Colon">For a <see cref="XPathTokenKind.Name"/> or a <see cref="XPathTokenKind.NamespaceWildcard"/>, the offset of the colon after the prefix, or <c>-1</c> when there is no prefix.</param>
    internal readonly record struct XPathToken(XPathTokenKind Kind, int Start, int Length, int Colon);

}
