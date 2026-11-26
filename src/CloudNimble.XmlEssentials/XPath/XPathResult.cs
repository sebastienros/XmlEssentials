using System;
using System.Text;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// The value of an XPath expression: a node-set, a boolean, a number or a string.
    /// </summary>
    /// <example>
    /// <code>
    /// var total = XPathExpression.Compile("sum(/order/line/@amount)").Evaluate(document).GetNumber();
    /// var open = XPathExpression.Compile("/order/@status = 'open'").Evaluate(document).GetBoolean();
    /// </code>
    /// </example>
    /// <remarks>
    /// <para>
    /// <see cref="GetBoolean"/>, <see cref="GetNumber"/> and <see cref="GetString"/> convert a result of any kind as the
    /// XPath functions <c>boolean()</c>, <c>number()</c> and <c>string()</c> do. A string result is made a
    /// <see cref="string"/> before the evaluation returns. A node-set result holds its document, which must not be
    /// disposed before the nodes are used.
    /// </para>
    /// <para>
    /// A default <see cref="XPathResult"/> is the result of no evaluation, and every member of it throws
    /// <see cref="InvalidOperationException"/>.
    /// </para>
    /// </remarks>
    public readonly struct XPathResult
    {

        #region Fields

        private readonly byte _kind;
        private readonly XPathNodeSet? _nodes;
        private readonly double _number;
        private readonly string? _string;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the type of the result.
        /// </summary>
        /// <exception cref="InvalidOperationException">This is a default <see cref="XPathResult"/>.</exception>
        public XPathResultKind Kind
        {
            get
            {
                if (_kind == 0)
                {
                    throw new InvalidOperationException("The XPath result is empty.");
                }

                return (XPathResultKind)(_kind - 1);
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a result.
        /// </summary>
        private XPathResult(XPathResultKind kind, double number, string? text, XPathNodeSet? nodes)
        {
            _kind = (byte)(kind + 1);
            _number = number;
            _string = text;
            _nodes = nodes;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns <c>boolean()</c> of the result.
        /// </summary>
        /// <returns>
        /// Whether a node-set is not empty, a boolean itself, whether a number is neither zero nor NaN, and whether a
        /// string is not empty.
        /// </returns>
        /// <exception cref="InvalidOperationException">This is a default <see cref="XPathResult"/>.</exception>
        public bool GetBoolean()
        {
            return Kind switch
            {
                XPathResultKind.NodeSet => _nodes!.Count > 0,
                XPathResultKind.Boolean => _number != 0,
                XPathResultKind.Number => !(_number == 0 || double.IsNaN(_number)),
                _ => _string!.Length > 0,
            };
        }

        /// <summary>
        /// Returns the node-set the result is.
        /// </summary>
        /// <returns>The nodes, in document order.</returns>
        /// <exception cref="InvalidOperationException">The result is not a node-set, or this is a default <see cref="XPathResult"/>.</exception>
        public XPathNodeSet GetNodeSet()
        {
            var kind = Kind;
            if (kind is not XPathResultKind.NodeSet)
            {
                throw new InvalidOperationException($"The XPath result is a {kind}, not a node-set.");
            }

            return _nodes!;
        }

        /// <summary>
        /// Returns <c>number()</c> of the result.
        /// </summary>
        /// <returns>
        /// The number of the string-value of the first node of a node-set, or NaN for an empty one; 1 or 0 for a
        /// boolean; a number itself; and the number a string is, or NaN.
        /// </returns>
        /// <exception cref="InvalidOperationException">This is a default <see cref="XPathResult"/>.</exception>
        /// <exception cref="ObjectDisposedException">The result is a node-set whose document has been disposed.</exception>
        public double GetNumber()
        {
            return Kind switch
            {
                XPathResultKind.NodeSet => _nodes!.Count == 0 ? double.NaN : XPathConversions.ParseNumber(Encoding.UTF8.GetBytes(_nodes[0].GetString())),
                XPathResultKind.Boolean or XPathResultKind.Number => _number,
                _ => XPathConversions.ParseNumber(Encoding.UTF8.GetBytes(_string!)),
            };
        }

        /// <summary>
        /// Returns <c>string()</c> of the result.
        /// </summary>
        /// <returns>
        /// The string-value of the first node of a node-set, or an empty string for an empty one; <c>true</c> or
        /// <c>false</c>; a number as XPath writes it, with no exponent; and a string itself.
        /// </returns>
        /// <exception cref="InvalidOperationException">This is a default <see cref="XPathResult"/>.</exception>
        /// <exception cref="ObjectDisposedException">The result is a node-set whose document has been disposed.</exception>
        public string GetString()
        {
            return Kind switch
            {
                XPathResultKind.NodeSet => _nodes!.Count == 0 ? string.Empty : _nodes[0].GetString(),
                XPathResultKind.Boolean => _number != 0 ? "true" : "false",
                XPathResultKind.Number => Encoding.UTF8.GetString(XPathConversions.FormatNumber(_number)),
                _ => _string!,
            };
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Makes the result of an evaluation from its value, so that nothing in it points into the document's buffers.
        /// </summary>
        /// <param name="document">The document the expression was evaluated over.</param>
        /// <param name="value">The value.</param>
        /// <returns>The result.</returns>
        internal static XPathResult From(XmlDocument document, in XPathValue value)
        {
            return value.Kind switch
            {
                XPathResultKind.NodeSet => new XPathResult(XPathResultKind.NodeSet, 0, null, new XPathNodeSet(document, value.ToArray(), value.Count)),
                XPathResultKind.Boolean => new XPathResult(XPathResultKind.Boolean, value.Boolean ? 1 : 0, null, null),
                XPathResultKind.Number => new XPathResult(XPathResultKind.Number, value.Number, null, null),
                _ => new XPathResult(XPathResultKind.String, 0, Encoding.UTF8.GetString(value.String.Span), null),
            };
        }

        #endregion

    }

}
