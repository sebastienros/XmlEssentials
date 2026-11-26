namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A variable reference: <c>$name</c> or <c>$p:name</c>.
    /// </summary>
    internal sealed class XPathVariableReference : XPathAst
    {

        #region Properties

        /// <summary>
        /// Gets the local name of the variable.
        /// </summary>
        internal string LocalName { get; }

        /// <summary>
        /// Gets the name as it is written in the expression, for messages.
        /// </summary>
        internal string Name { get; }

        /// <summary>
        /// Gets the namespace URI the prefix of the name is bound to, or empty for no namespace.
        /// </summary>
        internal string NamespaceUri { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a variable reference.
        /// </summary>
        /// <param name="localName">The local name.</param>
        /// <param name="namespaceUri">The namespace URI, or empty.</param>
        /// <param name="name">The name as written.</param>
        /// <param name="position">The offset of the <c>$</c>.</param>
        internal XPathVariableReference(string localName, string namespaceUri, string name, int position)
            : base(position)
        {
            LocalName = localName;
            NamespaceUri = namespaceUri;
            Name = name;
        }

        #endregion

    }

}
