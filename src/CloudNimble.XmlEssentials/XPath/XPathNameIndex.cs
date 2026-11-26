using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// The rows of the elements of a document, by expanded name, in document order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A scan for a name over a range of rows, which is what <c>//name</c> is, looks at every row of the range. With
    /// the index it looks only at the elements of that name: the rows of the range are a contiguous part of the
    /// sorted list, found by two binary searches.
    /// </para>
    /// <para>
    /// Building it costs about as much as a scan. So it is built the second time a document is scanned for a name, not
    /// the first, which keeps a document that is queried once from paying for it (the sibling-position index of Jint's
    /// selector matcher is built on the second question in the same way). It is published as the parent table is.
    /// </para>
    /// </remarks>
    internal sealed class XPathNameIndex
    {

        #region Fields

        private readonly Dictionary<XPathNameKey, List<int>>.AlternateLookup<XPathNameSpan> _rows;

        #endregion

        #region Constructors

        /// <summary>
        /// Builds the index of a document.
        /// </summary>
        /// <param name="document">The document, which is not disposed.</param>
        internal XPathNameIndex(XmlDocument document)
        {
            _rows = new Dictionary<XPathNameKey, List<int>>(XPathNameKeyComparer.Instance).GetAlternateLookup<XPathNameSpan>();
            var count = document.RowCount;
            for (var r = 0; r < count; r++)
            {
                ref var row = ref document.Row(r);
                if (!XmlDocument.IsElementKind(row.Kind))
                {
                    continue;
                }

                var name = new XPathNameSpan(document.LocalName(in row), document.NamespaceUri(in row));
                if (!_rows.TryGetValue(name, out var rows))
                {
                    rows = [];
                    _rows[name] = rows;
                }

                rows.Add(r);
            }
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Returns the rows of the elements of a name in a range of rows.
        /// </summary>
        /// <param name="localName">The local name.</param>
        /// <param name="namespaceUri">The namespace URI, or empty.</param>
        /// <param name="start">The first row of the range.</param>
        /// <param name="end">The row after the range.</param>
        /// <returns>The rows, ascending.</returns>
        internal ReadOnlySpan<int> Rows(ReadOnlySpan<byte> localName, ReadOnlySpan<byte> namespaceUri, int start, int end)
        {
            if (!_rows.TryGetValue(new XPathNameSpan(localName, namespaceUri), out var list))
            {
                return [];
            }

            var rows = CollectionsMarshal.AsSpan(list);
            var first = LowerBound(rows, start);
            return rows[first..LowerBound(rows, end)];
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Returns the index of the first row that is not below a row.
        /// </summary>
        private static int LowerBound(ReadOnlySpan<int> rows, int row)
        {
            var found = rows.BinarySearch(row);
            return found < 0 ? ~found : found;
        }

        #endregion

    }

}
