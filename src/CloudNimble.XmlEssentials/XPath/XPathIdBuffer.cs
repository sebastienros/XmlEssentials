using System;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A growable list of node ids, which the navigator appends to and the evaluator turns into node-sets.
    /// </summary>
    /// <remarks>
    /// A mutable struct: it is passed by reference, never copied.
    /// </remarks>
    internal struct XPathIdBuffer
    {

        #region Fields

        private int _count;
        private long[] _items;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the number of ids in the buffer.
        /// </summary>
        internal readonly int Count => _count;

        /// <summary>
        /// Gets the array that holds the ids. Only the first <see cref="Count"/> entries are ids.
        /// </summary>
        internal readonly long[] Items => _items;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a buffer with room for <paramref name="capacity"/> ids before it grows.
        /// </summary>
        /// <param name="capacity">The initial capacity, which may be 0.</param>
        internal XPathIdBuffer(int capacity)
        {
            _items = new long[capacity];
            _count = 0;
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Appends an id.
        /// </summary>
        /// <param name="id">The id.</param>
        internal void Add(long id)
        {
            if (_count == _items.Length)
            {
                Array.Resize(ref _items, Math.Max(_items.Length * 2, 4));
            }

            _items[_count++] = id;
        }

        /// <summary>
        /// Returns the ids in the buffer.
        /// </summary>
        /// <returns>The first <see cref="Count"/> entries.</returns>
        internal readonly ReadOnlySpan<long> AsSpan()
        {
            return _items.AsSpan(0, _count);
        }

        /// <summary>
        /// Empties the buffer and keeps its array.
        /// </summary>
        internal void Clear()
        {
            _count = 0;
        }

        /// <summary>
        /// Reverses the ids from <paramref name="start"/> to the end.
        /// </summary>
        /// <param name="start">The index of the first id to reverse.</param>
        /// <remarks>
        /// An axis that is walked from the context node outwards, such as <c>ancestor</c>, finds its nodes in
        /// reverse document order, and appends them ascending by reversing what it found.
        /// </remarks>
        internal readonly void Reverse(int start)
        {
            _items.AsSpan(start, _count - start).Reverse();
        }

        /// <summary>
        /// Sorts the ids from <paramref name="start"/> to the end.
        /// </summary>
        /// <param name="start">The index of the first id to sort.</param>
        internal readonly void Sort(int start)
        {
            _items.AsSpan(start, _count - start).Sort();
        }

        /// <summary>
        /// Sorts the buffer and removes the ids that occur more than once.
        /// </summary>
        internal void SortAndDeduplicate()
        {
            var span = _items.AsSpan(0, _count);
            span.Sort();
            var kept = 0;
            for (var i = 0; i < span.Length; i++)
            {
                if (kept == 0 || span[i] != span[kept - 1])
                {
                    span[kept++] = span[i];
                }
            }

            _count = kept;
        }

        /// <summary>
        /// Shortens the buffer to <paramref name="count"/> ids.
        /// </summary>
        /// <param name="count">The number of ids to keep, at most <see cref="Count"/>.</param>
        internal void Truncate(int count)
        {
            _count = count;
        }

        #endregion

    }

}
