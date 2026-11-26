using System;
using System.Collections;
using System.Collections.Generic;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// The nodes an XPath expression selected, in document order and without duplicates.
    /// </summary>
    /// <example>
    /// <code>
    /// var items = XPathExpression.Compile("/catalog/item").Select(document);
    /// for (var i = 0; i &lt; items.Count; i++)
    /// {
    ///     var id = items[i].GetElement().GetAttribute("id").GetInt32();
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// A node-set holds its document and the ids of its nodes; each <see cref="XPathNode"/> is a cursor into the
    /// document, used before it is disposed. <c>foreach</c> uses a struct enumerator, so it does not allocate. The
    /// order of the namespace nodes of one element is the order of their declarations, with <c>xml</c> first, and
    /// the order of the attributes of one element is the order of the start tag.
    /// </remarks>
    public sealed class XPathNodeSet : IReadOnlyList<XPathNode>
    {

        #region Properties

        /// <summary>
        /// Gets the number of nodes.
        /// </summary>
        public int Count { get; }

        /// <summary>
        /// Gets the node at an index, in document order.
        /// </summary>
        /// <param name="index">The index, from 0.</param>
        /// <returns>The node.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative, or not less than <see cref="Count"/>.</exception>
        public XPathNode this[int index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual((uint)index, (uint)Count, nameof(index));
                return new XPathNode(Document, Ids[index]);
            }
        }

        /// <summary>
        /// Gets the document the nodes are in.
        /// </summary>
        internal XmlDocument Document { get; }

        /// <summary>
        /// Gets the array that holds the ids of the nodes, ascending. Only the first <see cref="Count"/> are used.
        /// </summary>
        internal long[] Ids { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a node-set.
        /// </summary>
        /// <param name="document">The document.</param>
        /// <param name="ids">An ascending array of ids with no duplicates, which is not changed afterwards.</param>
        /// <param name="count">The number of ids used.</param>
        internal XPathNodeSet(XmlDocument document, long[] ids, int count)
        {
            Document = document;
            Ids = ids;
            Count = count;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Returns an enumerator over the nodes, in document order.
        /// </summary>
        /// <returns>The enumerator, which is a struct.</returns>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(this);
        }

        /// <inheritdoc />
        IEnumerator<XPathNode> IEnumerable<XPathNode>.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        /// <summary>
        /// Enumerates the nodes of an <see cref="XPathNodeSet"/>, in document order.
        /// </summary>
        public struct Enumerator : IEnumerator<XPathNode>
        {

            #region Fields

            private int _index;
            private readonly XPathNodeSet _set;

            #endregion

            #region Properties

            /// <summary>
            /// Gets the node at the position of the enumerator.
            /// </summary>
            public readonly XPathNode Current => new(_set.Document, _set.Ids[_index]);

            /// <inheritdoc />
            readonly object IEnumerator.Current => Current;

            #endregion

            #region Constructors

            /// <summary>
            /// Creates an enumerator before the first node.
            /// </summary>
            /// <param name="set">The node-set.</param>
            internal Enumerator(XPathNodeSet set)
            {
                _set = set;
                _index = -1;
            }

            #endregion

            #region Public Methods

            /// <inheritdoc />
            public readonly void Dispose()
            {
            }

            /// <summary>
            /// Moves to the next node.
            /// </summary>
            /// <returns><see langword="true"/> when there is one.</returns>
            public bool MoveNext()
            {
                return ++_index < _set.Count;
            }

            /// <summary>
            /// Moves the enumerator back before the first node.
            /// </summary>
            public void Reset()
            {
                _index = -1;
            }

            #endregion

        }

    }

}
