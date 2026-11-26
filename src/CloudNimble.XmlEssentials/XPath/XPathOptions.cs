using System;
using System.Collections.Generic;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// Options that control how an XPath expression is compiled and how much work its evaluation may do.
    /// </summary>
    /// <example>
    /// <code>
    /// var options = new XPathOptions { Namespaces = XmlDocument.DiscoverNamespaces(feed), MaxOperations = 1_000_000 };
    /// var titles = XPathExpression.Compile("/atom:feed/atom:entry/atom:title", options);
    /// </code>
    /// </example>
    /// <remarks>
    /// <see cref="XPathExpression.Compile"/> copies what it needs from the options. Changing them afterwards changes
    /// nothing in an expression already compiled.
    /// </remarks>
    public sealed class XPathOptions
    {

        #region Properties

        /// <summary>
        /// Gets or sets the deepest nesting an expression may have.
        /// </summary>
        /// <value>
        /// The limit, or 0 for the default of 64, the default depth limit of the reader. The whole expression is at
        /// depth 1, and an expression in parentheses, a function argument and a predicate are one deeper than the
        /// expression they are in. A run of operators, a path and a run of minus signs add no depth.
        /// </value>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
        /// <remarks>
        /// The limit bounds the recursion of the compiler and of the evaluation, so that an expression cannot exhaust
        /// the stack.
        /// </remarks>
        public int MaxDepth
        {
            get;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                field = value;
            }
        }

        /// <summary>
        /// Gets or sets the most work one evaluation of an expression may do.
        /// </summary>
        /// <value>
        /// The limit, or 0, the default, for no limit. Work is counted as the rows that the steps of the expression
        /// look at, plus the nodes that functions and comparisons read the string-value of.
        /// </value>
        /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
        /// <remarks>
        /// An expression is code. The size of a document is already bounded by
        /// <see cref="XmlDocumentOptions.MaxDocumentByteCount"/>, and a fixed expression costs a fixed power of it.
        /// A caller who evaluates an expression it did not write sets this limit.
        /// Once a document has been searched for elements by name twice, it keeps an index of them, and a search by name
        /// looks only at the elements of that name. The same evaluation over the same document may therefore count less
        /// work the next time, never more.
        /// </remarks>
        public long MaxOperations
        {
            get;
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                field = value;
            }
        }

        /// <summary>
        /// Gets or sets the prefixes an expression may use, with the namespace URIs they are bound to.
        /// </summary>
        /// <value>
        /// The namespace URIs, keyed by prefix, or <see langword="null"/> for none. The result of
        /// <see cref="XmlDocument.DiscoverNamespaces(ReadOnlySpan{byte}, XmlDocumentOptions)"/> can be assigned here.
        /// </value>
        /// <remarks>
        /// <para>
        /// A name with no prefix in an expression is in no namespace, as the Recommendation says (section 2.3); the
        /// default namespace of a document does not apply to it. An entry with an empty prefix is therefore not used.
        /// </para>
        /// <para>
        /// The prefix <c>xml</c> is bound to <c>http://www.w3.org/XML/1998/namespace</c> without an entry. An entry that
        /// binds <c>xml</c> to anything else, binds <c>xmlns</c>, or binds a prefix to an empty URI is refused by
        /// <see cref="XPathExpression.Compile"/>.
        /// </para>
        /// </remarks>
        public IReadOnlyDictionary<string, string>? Namespaces { get; set; }

        #endregion

    }

}
