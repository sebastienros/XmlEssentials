namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// The context an expression is evaluated in: the context node, and its position in the context size.
    /// </summary>
    /// <param name="Node">The context node.</param>
    /// <param name="Position">The context position, from 1.</param>
    /// <param name="Size">The context size.</param>
    internal readonly record struct XPathFocus(long Node, int Position, int Size);

}
