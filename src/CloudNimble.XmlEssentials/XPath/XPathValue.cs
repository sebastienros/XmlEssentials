using System;
using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A value while an expression is evaluated: a node-set, a boolean, a number, or a string held as UTF-8.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A node-set is an ascending run of node ids with no duplicates. It is one node held in the value itself, which
    /// is what <c>.</c> is and costs nothing, or the first <see cref="Count"/> entries of an array.
    /// </para>
    /// <para>
    /// An array the evaluation rented from <see cref="ArrayPool{T}.Shared"/> and that nothing else holds is
    /// <see cref="IsOwned"/>: the one part of the expression that consumes the value gives it back with
    /// <see cref="XPathEvaluator.Release"/>. A node-set from a variable is not owned. A string may be a slice of the document's own
    /// buffer, which is why no value leaves an evaluation.
    /// </para>
    /// </remarks>
    internal readonly struct XPathValue
    {

        #region Fields

        private readonly long[]? _nodes;
        private readonly double _number;
        private readonly long _single;
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
        /// Gets whether the node-set is held in an array that the evaluation rented and nothing else holds.
        /// </summary>
        internal bool IsOwned { get; }

        /// <summary>
        /// Gets the type of the value.
        /// </summary>
        internal XPathResultKind Kind { get; }

        /// <summary>
        /// Gets the nodes of a node-set, in document order.
        /// </summary>
        [UnscopedRef]
        internal ReadOnlySpan<long> Nodes => _nodes is null ? new ReadOnlySpan<long>(in _single) : _nodes.AsSpan(0, Count);

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
        private XPathValue(XPathResultKind kind, double number, ReadOnlyMemory<byte> text, long[]? nodes, long single, int count, bool owned)
        {
            Kind = kind;
            _number = number;
            _string = text;
            _nodes = nodes;
            _single = single;
            Count = count;
            IsOwned = owned;
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
            return new XPathValue(XPathResultKind.Boolean, value ? 1 : 0, default, null, 0, 0, false);
        }

        /// <summary>
        /// Creates a node-set of one node.
        /// </summary>
        /// <param name="id">The node.</param>
        /// <returns>The node-set.</returns>
        internal static XPathValue FromNode(long id)
        {
            return new XPathValue(XPathResultKind.NodeSet, 0, default, null, id, 1, false);
        }

        /// <summary>
        /// Creates a node-set.
        /// </summary>
        /// <param name="nodes">An ascending array of ids with no duplicates, which is not changed afterwards.</param>
        /// <param name="count">The number of ids in it that are used.</param>
        /// <param name="owned">Whether the array was rented by this evaluation and nothing else holds it.</param>
        /// <returns>The node-set.</returns>
        internal static XPathValue FromNodes(long[] nodes, int count, bool owned)
        {
            return new XPathValue(XPathResultKind.NodeSet, 0, default, nodes, 0, count, owned);
        }

        /// <summary>
        /// Creates a number.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <returns>The number.</returns>
        internal static XPathValue FromNumber(double value)
        {
            return new XPathValue(XPathResultKind.Number, value, default, null, 0, 0, false);
        }

        /// <summary>
        /// Creates a string.
        /// </summary>
        /// <param name="utf8">The UTF-8 of the string.</param>
        /// <returns>The string.</returns>
        internal static XPathValue FromString(ReadOnlyMemory<byte> utf8)
        {
            return new XPathValue(XPathResultKind.String, 0, utf8, null, 0, 0, false);
        }

        /// <summary>
        /// Returns the nodes of a node-set as an array that may be kept, for a result.
        /// </summary>
        /// <returns>The array, which holds the nodes in its first <see cref="Count"/> entries.</returns>
        internal long[] ToArray()
        {
            return _nodes ?? [_single];
        }

        #endregion

    }

}
