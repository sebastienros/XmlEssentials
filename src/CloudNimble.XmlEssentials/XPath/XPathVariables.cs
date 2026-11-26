using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Unicode;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// The values of the variables an XPath expression refers to as <c>$name</c>.
    /// </summary>
    /// <example>
    /// <code>
    /// var byId = XPathExpression.Compile("/feed/entry[@id = $id]/title");
    /// var variables = new XPathVariables();
    /// variables.Set("id", "urn:entry:7");
    /// var title = byId.Evaluate(document, variables).GetString();
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// A variable is named by its local name and its namespace URI. A prefix in the expression, as in
    /// <c>$p:name</c>, is resolved with <see cref="XPathOptions.Namespaces"/> when the expression is compiled.
    /// </para>
    /// <para>
    /// The same instance may be passed to any number of evaluations. It is not safe to set a value while an
    /// evaluation that uses the instance runs on another thread.
    /// </para>
    /// </remarks>
    public sealed class XPathVariables
    {

        #region Fields

        private readonly Dictionary<(string LocalName, string NamespaceUri), (XPathValue Value, XmlDocument? Document)> _values = [];

        #endregion

        #region Public Methods

        /// <summary>
        /// Sets a variable to a string.
        /// </summary>
        /// <param name="localName">The local name of the variable.</param>
        /// <param name="value">The value.</param>
        /// <param name="namespaceUri">The namespace URI of the variable, or empty for no namespace.</param>
        /// <exception cref="ArgumentNullException"><paramref name="localName"/>, <paramref name="value"/> or <paramref name="namespaceUri"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="localName"/> is empty or white space, or <paramref name="value"/> holds a surrogate that is not part of a pair.</exception>
        public void Set(string localName, string value, string namespaceUri = "")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(localName);
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(namespaceUri);
            var utf8 = new byte[Encoding.UTF8.GetMaxByteCount(value.Length)];
            if (Utf8.FromUtf16(value, utf8, out _, out var written, replaceInvalidSequences: false) is not System.Buffers.OperationStatus.Done)
            {
                throw new ArgumentException("The value holds a surrogate that is not part of a pair.", nameof(value));
            }

            _values[(localName, namespaceUri)] = (XPathValue.FromString(utf8.AsMemory(0, written)), null);
        }

        /// <summary>
        /// Sets a variable to a number.
        /// </summary>
        /// <param name="localName">The local name of the variable.</param>
        /// <param name="value">The value, which may be NaN or an infinity.</param>
        /// <param name="namespaceUri">The namespace URI of the variable, or empty for no namespace.</param>
        /// <exception cref="ArgumentNullException"><paramref name="localName"/> or <paramref name="namespaceUri"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="localName"/> is empty or white space.</exception>
        public void Set(string localName, double value, string namespaceUri = "")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(localName);
            ArgumentNullException.ThrowIfNull(namespaceUri);
            _values[(localName, namespaceUri)] = (XPathValue.FromNumber(value), null);
        }

        /// <summary>
        /// Sets a variable to a boolean.
        /// </summary>
        /// <param name="localName">The local name of the variable.</param>
        /// <param name="value">The value.</param>
        /// <param name="namespaceUri">The namespace URI of the variable, or empty for no namespace.</param>
        /// <exception cref="ArgumentNullException"><paramref name="localName"/> or <paramref name="namespaceUri"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="localName"/> is empty or white space.</exception>
        public void Set(string localName, bool value, string namespaceUri = "")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(localName);
            ArgumentNullException.ThrowIfNull(namespaceUri);
            _values[(localName, namespaceUri)] = (XPathValue.FromBoolean(value), null);
        }

        /// <summary>
        /// Sets a variable to a node-set.
        /// </summary>
        /// <param name="localName">The local name of the variable.</param>
        /// <param name="value">The value: a node-set that an evaluation over the same document returned.</param>
        /// <param name="namespaceUri">The namespace URI of the variable, or empty for no namespace.</param>
        /// <exception cref="ArgumentNullException"><paramref name="localName"/>, <paramref name="value"/> or <paramref name="namespaceUri"/> is <see langword="null"/>.</exception>
        /// <exception cref="ArgumentException"><paramref name="localName"/> is empty or white space.</exception>
        /// <remarks>
        /// The node-set can be used only in an evaluation over the document it came from. Using it in an evaluation
        /// over another document throws <see cref="ArgumentException"/> from that evaluation.
        /// </remarks>
        public void Set(string localName, XPathNodeSet value, string namespaceUri = "")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(localName);
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(namespaceUri);
            _values[(localName, namespaceUri)] = (XPathValue.FromNodes(value.Ids, value.Count), value.Document);
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Finds the value of a variable.
        /// </summary>
        /// <param name="localName">The local name.</param>
        /// <param name="namespaceUri">The namespace URI, or empty.</param>
        /// <param name="value">When this method returns <see langword="true"/>, the value.</param>
        /// <param name="document">When this method returns <see langword="true"/>, the document of a node-set, or <see langword="null"/> for any other value.</param>
        /// <returns><see langword="true"/> when the variable has a value.</returns>
        internal bool TryGet(string localName, string namespaceUri, out XPathValue value, out XmlDocument? document)
        {
            var found = _values.TryGetValue((localName, namespaceUri), out var entry);
            value = entry.Value;
            document = entry.Document;
            return found;
        }

        #endregion

    }

}
