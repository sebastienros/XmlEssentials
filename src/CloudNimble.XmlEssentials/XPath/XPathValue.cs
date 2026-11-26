using System;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A value while an expression is evaluated: a node-set, a boolean, a number, or a string held as UTF-8.
    /// </summary>
    /// <remarks>
    /// A node-set is an ascending array of node ids with no duplicates, of which the first <see cref="Count"/>
    /// entries are used. The array is never changed once it is in a value, so values share it freely. A string may be
    /// a slice of the document's own buffer, which is why no value leaves an evaluation.
    /// </remarks>
    internal readonly struct XPathValue
    {

        #region Fields

        private readonly long[]? _nodes;
        private readonly double _number;
        private readonly ReadOnlyMemory<byte> _string;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the value of a boolean.
        /// </summary>
        internal bool Boolean => _number != 0;

        /// <summary>
        /// Gets the number of nodes in a node-set.
        /// </summary>
        internal int Count { get; }

        /// <summary>
        /// Gets the type of the value.
        /// </summary>
        internal XPathResultKind Kind { get; }

        /// <summary>
        /// Gets the array that holds the nodes of a node-set.
        /// </summary>
        internal long[] NodeArray => _nodes!;

        /// <summary>
        /// Gets the nodes of a node-set, in document order.
        /// </summary>
        internal ReadOnlySpan<long> Nodes => _nodes.AsSpan(0, Count);

        /// <summary>
        /// Gets the value of a number.
        /// </summary>
        internal double Number => _number;

        /// <summary>
        /// Gets the UTF-8 of a string.
        /// </summary>
        internal ReadOnlyMemory<byte> String => _string;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a value.
        /// </summary>
        private XPathValue(XPathResultKind kind, double number, ReadOnlyMemory<byte> text, long[]? nodes, int count)
        {
            Kind = kind;
            _number = number;
            _string = text;
            _nodes = nodes;
            Count = count;
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Creates a boolean.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The boolean.</returns>
        internal static XPathValue FromBoolean(bool value)
        {
            return new XPathValue(XPathResultKind.Boolean, value ? 1 : 0, default, null, 0);
        }

        /// <summary>
        /// Creates a node-set.
        /// </summary>
        /// <param name="nodes">An ascending array of ids with no duplicates, which is not changed afterwards.</param>
        /// <param name="count">The number of ids in it that are used.</param>
        /// <returns>The node-set.</returns>
        internal static XPathValue FromNodes(long[] nodes, int count)
        {
            return new XPathValue(XPathResultKind.NodeSet, 0, default, nodes, count);
        }

        /// <summary>
        /// Creates a number.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The number.</returns>
        internal static XPathValue FromNumber(double value)
        {
            return new XPathValue(XPathResultKind.Number, value, default, null, 0);
        }

        /// <summary>
        /// Creates a string.
        /// </summary>
        /// <param name="utf8">The UTF-8 of the string.</param>
        /// <returns>The string.</returns>
        internal static XPathValue FromString(ReadOnlyMemory<byte> utf8)
        {
            return new XPathValue(XPathResultKind.String, 0, utf8, null, 0);
        }

        #endregion

    }

}
