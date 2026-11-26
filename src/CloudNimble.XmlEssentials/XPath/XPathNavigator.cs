using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// Presents the row store of an <see cref="XmlDocument"/> as the XPath 1.0 data model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A node is a 64-bit id (<see cref="XPathNodeId"/>). The navigator answers three questions about an id: which
    /// ids an axis reaches from it, what its name is, and what its string-value is. Every answer is read from the
    /// rows as UTF-8. No value is made except one that has to be put together from several rows.
    /// </para>
    /// <para>
    /// The parent table of the document (<see cref="XmlDocument.GetParentTable"/>) is asked for only by what needs
    /// it: <c>parent</c> and the ancestor axes from an element or a leaf, both sibling axes, the <c>namespace</c>
    /// axis, <see cref="Language"/>, and telling whether two adjacent text rows are siblings. The members do not
    /// check for disposal; the public types check it once, on entry.
    /// </para>
    /// </remarks>
    internal readonly struct XPathNavigator
    {

        #region Fields

        private readonly XmlDocument _document;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a navigator over <paramref name="document"/>.
        /// </summary>
        /// <param name="document">The document, which is not disposed.</param>
        internal XPathNavigator(XmlDocument document)
        {
            _document = document;
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Returns the id of the node a cursor into the document is on.
        /// </summary>
        /// <param name="element">A cursor into this navigator's document.</param>
        /// <param name="parameterName">The name of the parameter the cursor was passed as, for the exception.</param>
        /// <returns>The id. A cursor on a later row of a text run gives the first row of the run.</returns>
        /// <exception cref="ArgumentException">
        /// The cursor is on a namespace declaration, or on a text run with no characters. Neither is a node.
        /// </exception>
        internal long FromElement(XmlElement element, string parameterName)
        {
            var index = element.Index;
            ref var row = ref _document.Row(index);
            if (row.Kind is XmlValueKind.Attribute && _document.IsNamespaceDeclaration(in row, out _))
            {
                throw new ArgumentException("The element is a namespace declaration, which is not a node in the XPath data model.", parameterName);
            }

            if (!IsText(row.Kind))
            {
                return XPathNodeId.FromRow(index);
            }

            // A text row always has an element above it, so the row before a run is never missing.
            var parents = _document.GetParentTable();
            var start = index;
            while (IsText(_document.Row(start - 1).Kind) && parents[start - 1] == parents[index])
            {
                start--;
            }

            int[]? table = parents;
            if (!HasCharacters(start, RunEnd(start, _document.RowCount, ref table)))
            {
                throw new ArgumentException("The element is a text run with no characters, which is not a node in the XPath data model.", parameterName);
            }

            return XPathNodeId.FromRow(start);
        }

        /// <summary>
        /// Returns the kind of a node.
        /// </summary>
        /// <param name="id">The node.</param>
        /// <returns>The kind.</returns>
        internal XPathNodeKind KindOf(long id)
        {
            if (id == XPathNodeId.Document)
            {
                return XPathNodeKind.Document;
            }

            if (XPathNodeId.IsNamespace(id))
            {
                return XPathNodeKind.Namespace;
            }

            return _document.Row(XPathNodeId.Row(id)).Kind switch
            {
                XmlValueKind.Attribute => XPathNodeKind.Attribute,
                XmlValueKind.Text or XmlValueKind.CData => XPathNodeKind.Text,
                XmlValueKind.Comment => XPathNodeKind.Comment,
                XmlValueKind.ProcessingInstruction => XPathNodeKind.ProcessingInstruction,
                _ => XPathNodeKind.Element,
            };
        }

        /// <summary>
        /// Finds the <c>xml:lang</c> in scope on a node, for <c>lang()</c>.
        /// </summary>
        /// <param name="id">The node.</param>
        /// <param name="value">When this method returns <see langword="true"/>, the value of the nearest <c>xml:lang</c>.</param>
        /// <returns>
        /// <see langword="true"/> when the node or an element above it has an <c>xml:lang</c> attribute, even an empty
        /// one; <see langword="false"/> when none has.
        /// </returns>
        /// <remarks>
        /// The search starts at the node for an element, at its element for an attribute or a namespace node, and at
        /// its parent for any other node.
        /// </remarks>
        internal bool Language(long id, out ReadOnlySpan<byte> value)
        {
            value = default;
            if (id == XPathNodeId.Document)
            {
                return false;
            }

            var parents = _document.GetParentTable();
            var element = XPathNodeId.Row(id);
            if (!XPathNodeId.IsNamespace(id) && !XmlDocument.IsElementKind(_document.Row(element).Kind))
            {
                // The table holds the element of an attribute row, and the parent of a leaf.
                element = parents[element];
            }

            for (; element >= 0; element = parents[element])
            {
                ref var row = ref _document.Row(element);
                var attributeEnd = element + 1 + row.AttributeCount;
                for (var a = element + 1; a < attributeEnd; a++)
                {
                    ref var attribute = ref _document.Row(a);
                    if (_document.LocalName(in attribute).SequenceEqual("lang"u8) && _document.NamespaceUri(in attribute).SequenceEqual(XmlUtf8.Xml))
                    {
                        value = _document.Value(in attribute);
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the local name of a node.
        /// </summary>
        /// <param name="id">The node.</param>
        /// <returns>
        /// The local name of an element or an attribute, the target of a processing instruction, the prefix of a
        /// namespace node (empty for the default namespace), and empty for any other node.
        /// </returns>
        internal ReadOnlySpan<byte> LocalName(long id)
        {
            if (id == XPathNodeId.Document)
            {
                return [];
            }

            if (XPathNodeId.IsNamespace(id))
            {
                return NamespacePrefix(id);
            }

            return _document.LocalName(in _document.Row(XPathNodeId.Row(id)));
        }

        /// <summary>
        /// Returns the namespace URI of a node.
        /// </summary>
        /// <param name="id">The node.</param>
        /// <returns>The namespace URI of an element or an attribute, and empty for any other node.</returns>
        internal ReadOnlySpan<byte> NamespaceUri(long id)
        {
            if (id == XPathNodeId.Document || XPathNodeId.IsNamespace(id))
            {
                return [];
            }

            return _document.NamespaceUri(in _document.Row(XPathNodeId.Row(id)));
        }

        /// <summary>
        /// Returns the prefix of a node.
        /// </summary>
        /// <param name="id">The node.</param>
        /// <returns>The prefix of an element or an attribute as it was written, and empty for any other node.</returns>
        internal ReadOnlySpan<byte> Prefix(long id)
        {
            if (id == XPathNodeId.Document || XPathNodeId.IsNamespace(id))
            {
                return [];
            }

            return _document.Prefix(in _document.Row(XPathNodeId.Row(id)));
        }

        /// <summary>
        /// Appends to <paramref name="output"/>, in ascending order, every node on <paramref name="axis"/> from
        /// <paramref name="context"/> that passes <paramref name="test"/>.
        /// </summary>
        /// <param name="context">The context node.</param>
        /// <param name="axis">The axis.</param>
        /// <param name="test">The node test.</param>
        /// <param name="output">The buffer the nodes are appended to.</param>
        /// <returns>The number of rows looked at, which the evaluator charges to the operation limit.</returns>
        /// <remarks>
        /// The nodes are appended in document order for the reverse axes as well. The evaluator takes the proximity
        /// position of a node on a reverse axis from the other end.
        /// </remarks>
        internal long Select(long context, XPathAxis axis, XPathNodeTest test, ref XPathIdBuffer output)
        {
            if (context == XPathNodeId.Document)
            {
                return SelectFromDocument(axis, test, ref output);
            }

            var row = XPathNodeId.Row(context);
            if (XPathNodeId.IsNamespace(context))
            {
                return SelectFromOwned(context, row, axis, test, ref output);
            }

            if (_document.Row(row).Kind is XmlValueKind.Attribute)
            {
                return SelectFromOwned(context, OwnerOf(row), axis, test, ref output);
            }

            return SelectFromRow(row, axis, test, ref output);
        }

        /// <summary>
        /// Returns the string-value of a node.
        /// </summary>
        /// <param name="id">The node.</param>
        /// <returns>
        /// Memory over the document's own buffer when the value is one row, which it is for an attribute, a comment,
        /// a processing instruction, a namespace node, a text run of one row and an element with one text child; a
        /// new array of exactly the length needed otherwise.
        /// </returns>
        /// <remarks>
        /// The string-value of the document node and of an element is the text of every text and CDATA row under it,
        /// in document order. That of a text node is the text of the rows of its run.
        /// </remarks>
        internal ReadOnlyMemory<byte> StringValue(long id)
        {
            if (id == XPathNodeId.Document)
            {
                return Text(0, _document.RowCount);
            }

            if (XPathNodeId.IsNamespace(id))
            {
                if (XPathNodeId.IsXmlNamespace(id))
                {
                    return XmlUtf8.Xml;
                }

                return ValueMemory(XPathNodeId.Declaration(id));
            }

            var r = XPathNodeId.Row(id);
            ref var row = ref _document.Row(r);
            if (XmlDocument.IsElementKind(row.Kind))
            {
                var first = r + 1 + row.AttributeCount;
                if (row.EndIndex == first + 1 && IsText(_document.Row(first).Kind))
                {
                    // An element whose content is one text node, the most common shape, is that node's value.
                    return ValueMemory(first);
                }

                return Text(first, row.EndIndex);
            }

            if (IsText(row.Kind))
            {
                int[]? parents = null;
                return Text(r, RunEnd(r, _document.RowCount, ref parents));
            }

            return ValueMemory(r);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Appends the ancestors of a row, ascending, that pass a test, and the row itself when it is asked for.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="includeSelf">Whether the row itself is a candidate.</param>
        /// <param name="test">The node test.</param>
        /// <param name="output">The buffer.</param>
        /// <returns>The number of nodes looked at.</returns>
        private long AppendAncestors(int row, bool includeSelf, XPathNodeTest test, ref XPathIdBuffer output)
        {
            var parents = _document.GetParentTable();
            var start = output.Count;
            long looked = 1;
            for (var p = includeSelf ? row : parents[row]; p >= 0; p = parents[p])
            {
                looked++;
                if (MatchesRow(in _document.Row(p), test))
                {
                    output.Add(XPathNodeId.FromRow(p));
                }
            }

            if (MatchesOther(test))
            {
                output.Add(XPathNodeId.Document);
            }

            output.Reverse(start);
            return looked;
        }

        /// <summary>
        /// Appends the attributes of an element that pass a test.
        /// </summary>
        /// <param name="element">The element row.</param>
        /// <param name="test">The node test.</param>
        /// <param name="output">The buffer.</param>
        /// <returns>The number of rows looked at.</returns>
        private long AppendAttributes(int element, XPathNodeTest test, ref XPathIdBuffer output)
        {
            var end = element + 1 + _document.Row(element).AttributeCount;
            for (var a = element + 1; a < end; a++)
            {
                ref var row = ref _document.Row(a);
                if (!_document.IsNamespaceDeclaration(in row, out _) && MatchesAttribute(in row, test))
                {
                    output.Add(XPathNodeId.FromRow(a));
                }
            }

            return end - element;
        }

        /// <summary>
        /// Appends the namespace nodes of an element that pass a test.
        /// </summary>
        /// <param name="element">The element row.</param>
        /// <param name="test">The node test.</param>
        /// <param name="output">The buffer.</param>
        /// <returns>The number of rows looked at.</returns>
        /// <remarks>
        /// The <c>xml</c> node comes first. Then each declaration on the element or an element above it gives a node,
        /// unless a nearer element declares the same prefix, the declaration has an empty value (which takes the
        /// binding away), or it declares <c>xml</c>. The nodes are in ascending order of their declaration rows.
        /// </remarks>
        private long AppendNamespaces(int element, XPathNodeTest test, ref XPathIdBuffer output)
        {
            if (MatchesNamespace(XmlUtf8.XmlPrefix, test))
            {
                output.Add(XPathNodeId.XmlNamespace(element));
            }

            var parents = _document.GetParentTable();
            var declarations = 0;
            for (var e = element; e >= 0; e = parents[e])
            {
                declarations += _document.Row(e).AttributeCount;
            }

            var start = output.Count;
            var seen = ArrayPool<int>.Shared.Rent(declarations + 1);
            var seenCount = 0;
            long looked = 1;
            try
            {
                for (var e = element; e >= 0; e = parents[e])
                {
                    var end = e + 1 + _document.Row(e).AttributeCount;
                    for (var d = e + 1; d < end; d++)
                    {
                        looked++;
                        ref var row = ref _document.Row(d);
                        if (!_document.IsNamespaceDeclaration(in row, out var prefix) || IsSeen(seen.AsSpan(0, seenCount), prefix))
                        {
                            continue;
                        }

                        seen[seenCount++] = d;
                        if (row.ValueLength > 0 && !prefix.SequenceEqual(XmlUtf8.XmlPrefix) && MatchesNamespace(prefix, test))
                        {
                            output.Add(XPathNodeId.Namespace(element, d));
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(seen);
            }

            output.Sort(start);
            return looked;
        }

        /// <summary>
        /// Returns whether any row of a text run holds a character.
        /// </summary>
        /// <param name="start">The first row of the run.</param>
        /// <param name="end">The row after the run.</param>
        /// <returns><see langword="true"/> when the run is a text node.</returns>
        private bool HasCharacters(int start, int end)
        {
            for (var i = start; i < end; i++)
            {
                if (_document.Row(i).ValueLength > 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns whether a declaration of <paramref name="prefix"/> is among those already met.
        /// </summary>
        /// <param name="seen">The rows of the declarations met on nearer elements.</param>
        /// <param name="prefix">The prefix.</param>
        /// <returns><see langword="true"/> when the prefix has been declared nearer to the element.</returns>
        private bool IsSeen(ReadOnlySpan<int> seen, ReadOnlySpan<byte> prefix)
        {
            foreach (var d in seen)
            {
                _document.IsNamespaceDeclaration(in _document.Row(d), out var declared);
                if (declared.SequenceEqual(prefix))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns whether a row kind is text or CDATA.
        /// </summary>
        /// <param name="kind">The row kind.</param>
        /// <returns><see langword="true"/> for the kinds a text node is made of.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsText(XmlValueKind kind)
        {
            return kind is XmlValueKind.Text or XmlValueKind.CData;
        }

        /// <summary>
        /// Returns whether an attribute row passes a test on the <c>attribute</c> axis.
        /// </summary>
        /// <param name="row">An attribute row that is not a namespace declaration.</param>
        /// <param name="test">The node test.</param>
        /// <returns><see langword="true"/> when the attribute passes.</returns>
        private bool MatchesAttribute(in XmlDbRow row, XPathNodeTest test)
        {
            return test.Kind switch
            {
                XPathNodeTestKind.Any or XPathNodeTestKind.Node => true,
                XPathNodeTestKind.Name => NameEquals(in row, test),
                XPathNodeTestKind.NamespaceWildcard => _document.NamespaceUri(in row).SequenceEqual(test.NamespaceUri),
                // text(), comment() and processing-instruction() never pass an attribute, nor does a kind the parser never makes.
                _ => false,
            };
        }

        /// <summary>
        /// Returns whether a namespace node passes a test on the <c>namespace</c> axis.
        /// </summary>
        /// <param name="prefix">The prefix of the node.</param>
        /// <param name="test">The node test.</param>
        /// <returns><see langword="true"/> when the node passes.</returns>
        /// <remarks>
        /// The expanded-name of a namespace node is its prefix, in no namespace, so a name test with a prefix and a
        /// namespace wildcard never pass one.
        /// </remarks>
        private static bool MatchesNamespace(ReadOnlySpan<byte> prefix, XPathNodeTest test)
        {
            return test.Kind switch
            {
                XPathNodeTestKind.Any or XPathNodeTestKind.Node => true,
                XPathNodeTestKind.Name => test.NamespaceUri.Length == 0 && prefix.SequenceEqual(test.LocalName),
                _ => false,
            };
        }

        /// <summary>
        /// Returns whether a node that is not of the principal node type of the axis, and has no row of its own on
        /// it, passes a test: the document node, or an attribute or a namespace node on an axis other than its own.
        /// </summary>
        /// <param name="test">The node test.</param>
        /// <returns><see langword="true"/> for <c>node()</c> only.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool MatchesOther(XPathNodeTest test)
        {
            return test.Kind is XPathNodeTestKind.Node;
        }

        /// <summary>
        /// Returns whether a row passes a test on an axis whose principal node type is element.
        /// </summary>
        /// <param name="row">An element or a leaf row. A text row is the first row of a text node.</param>
        /// <param name="test">The node test.</param>
        /// <returns><see langword="true"/> when the row passes.</returns>
        private bool MatchesRow(in XmlDbRow row, XPathNodeTest test)
        {
            return test.Kind switch
            {
                XPathNodeTestKind.Any => XmlDocument.IsElementKind(row.Kind),
                XPathNodeTestKind.Name => XmlDocument.IsElementKind(row.Kind) && NameEquals(in row, test),
                XPathNodeTestKind.NamespaceWildcard => XmlDocument.IsElementKind(row.Kind) && _document.NamespaceUri(in row).SequenceEqual(test.NamespaceUri),
                XPathNodeTestKind.Node => true,
                XPathNodeTestKind.Text => IsText(row.Kind),
                XPathNodeTestKind.Comment => row.Kind is XmlValueKind.Comment,
                XPathNodeTestKind.ProcessingInstruction => row.Kind is XmlValueKind.ProcessingInstruction && (test.Target is null || _document.LocalName(in row).SequenceEqual(test.Target)),
                // A kind the parser never makes passes nothing.
                _ => false,
            };
        }

        /// <summary>
        /// Returns whether the expanded-name of a row equals that of a name test.
        /// </summary>
        /// <param name="row">An element or an attribute row.</param>
        /// <param name="test">A <see cref="XPathNodeTestKind.Name"/> test.</param>
        /// <returns><see langword="true"/> when the local names and the namespace URIs are equal, byte for byte.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool NameEquals(in XmlDbRow row, XPathNodeTest test)
        {
            // The lengths are on the row, so most rows that do not match are told apart without reading a name.
            var localLength = row.PrefixLength > 0 ? row.NameLength - row.PrefixLength - 1 : row.NameLength;
            return localLength == test.LocalName.Length
                && row.NsLength == test.NamespaceUri.Length
                && _document.LocalName(in row).SequenceEqual(test.LocalName)
                && _document.NamespaceUri(in row).SequenceEqual(test.NamespaceUri);
        }

        /// <summary>
        /// Returns the prefix of a namespace node.
        /// </summary>
        /// <param name="id">A namespace node.</param>
        /// <returns>The prefix, which is empty for the default namespace.</returns>
        private ReadOnlySpan<byte> NamespacePrefix(long id)
        {
            if (XPathNodeId.IsXmlNamespace(id))
            {
                return XmlUtf8.XmlPrefix;
            }

            _document.IsNamespaceDeclaration(in _document.Row(XPathNodeId.Declaration(id)), out var prefix);
            return prefix;
        }

        /// <summary>
        /// Returns the element row an attribute row belongs to.
        /// </summary>
        /// <param name="attribute">The attribute row.</param>
        /// <returns>The element row, found by stepping back over the attribute rows before it.</returns>
        private int OwnerOf(int attribute)
        {
            var element = attribute - 1;
            while (_document.Row(element).Kind is XmlValueKind.Attribute)
            {
                element--;
            }

            return element;
        }

        /// <summary>
        /// Returns the row after the text run that starts at <paramref name="start"/>.
        /// </summary>
        /// <param name="start">The first row of the run.</param>
        /// <param name="limit">The row the run cannot reach.</param>
        /// <param name="parents">The parent table, fetched the first time two text rows are adjacent.</param>
        /// <returns>The row after the last row of the run.</returns>
        /// <remarks>
        /// Two adjacent text rows are in one run only when they are siblings: in <c>&lt;a&gt;&lt;b&gt;x&lt;/b&gt;y&lt;/a&gt;</c>
        /// the rows of <c>x</c> and <c>y</c> are adjacent, but <c>x</c> is a child of <c>b</c>.
        /// </remarks>
        private int RunEnd(int start, int limit, ref int[]? parents)
        {
            var end = start + 1;
            while (end < limit && IsText(_document.Row(end).Kind))
            {
                parents ??= _document.GetParentTable();
                if (parents[end] != parents[start])
                {
                    break;
                }

                end++;
            }

            return end;
        }

        /// <summary>
        /// Appends the nodes in a range of rows that pass a test: the <c>descendant</c>, <c>following</c> and
        /// <c>preceding</c> axes.
        /// </summary>
        /// <param name="start">The first row, which is not an attribute row and not inside a text run.</param>
        /// <param name="end">The row after the range.</param>
        /// <param name="ancestorsOf">
        /// For <c>preceding</c>, the row whose ancestors are left out: an element that ends after it. Otherwise the
        /// row count, which no element ends after.
        /// </param>
        /// <param name="test">The node test.</param>
        /// <param name="output">The buffer.</param>
        /// <returns>The number of rows looked at.</returns>
        /// <remarks>
        /// Attribute rows are stepped over from their element. A text row counts once for its run, and only when the
        /// run holds a character; with a test that cannot pass a text node, the rows of a run are stepped over one at
        /// a time and the parent table is not needed.
        /// </remarks>
        private long Scan(int start, int end, int ancestorsOf, XPathNodeTest test, ref XPathIdBuffer output)
        {
            if (test.Kind is XPathNodeTestKind.Name && _document.GetNameIndex() is { } index)
            {
                return ScanIndex(index, start, end, ancestorsOf, test, ref output);
            }

            int[]? parents = null;
            long looked = 0;
            var i = start;
            while (i < end)
            {
                looked++;
                ref var row = ref _document.Row(i);
                if (XmlDocument.IsElementKind(row.Kind))
                {
                    if (row.EndIndex <= ancestorsOf && MatchesRow(in row, test))
                    {
                        output.Add(XPathNodeId.FromRow(i));
                    }

                    i += 1 + row.AttributeCount;
                    continue;
                }

                if (IsText(row.Kind))
                {
                    if (!test.MatchesText)
                    {
                        i++;
                        continue;
                    }

                    var runEnd = RunEnd(i, end, ref parents);
                    if (HasCharacters(i, runEnd))
                    {
                        output.Add(XPathNodeId.FromRow(i));
                    }

                    i = runEnd;
                    continue;
                }

                if (MatchesRow(in row, test))
                {
                    output.Add(XPathNodeId.FromRow(i));
                }

                i++;
            }

            return looked;
        }

        /// <summary>
        /// Appends the elements of a name in a range of rows, from the index of the document.
        /// </summary>
        /// <param name="index">The index.</param>
        /// <param name="start">The first row.</param>
        /// <param name="end">The row after the range.</param>
        /// <param name="ancestorsOf">As for <see cref="Scan"/>.</param>
        /// <param name="test">A name test.</param>
        /// <param name="output">The buffer.</param>
        /// <returns>The number of rows looked at: the elements of that name in the range.</returns>
        private long ScanIndex(XPathNameIndex index, int start, int end, int ancestorsOf, XPathNodeTest test, ref XPathIdBuffer output)
        {
            var rows = index.Rows(test.LocalName, test.NamespaceUri, start, end);
            foreach (var r in rows)
            {
                if (_document.Row(r).EndIndex <= ancestorsOf)
                {
                    output.Add(XPathNodeId.FromRow(r));
                }
            }

            return rows.Length;
        }

        /// <summary>
        /// Selects from the document node.
        /// </summary>
        /// <param name="axis">The axis.</param>
        /// <param name="test">The node test.</param>
        /// <param name="output">The buffer.</param>
        /// <returns>The number of rows looked at.</returns>
        private long SelectFromDocument(XPathAxis axis, XPathNodeTest test, ref XPathIdBuffer output)
        {
            switch (axis)
            {
                case XPathAxis.Self:
                case XPathAxis.AncestorOrSelf:
                    if (MatchesOther(test))
                    {
                        output.Add(XPathNodeId.Document);
                    }

                    return 1;
                case XPathAxis.Child:
                    return WalkSiblings(0, _document.RowCount, test, ref output);
                case XPathAxis.Descendant:
                    return Scan(0, _document.RowCount, _document.RowCount, test, ref output);
                case XPathAxis.DescendantOrSelf:
                    if (MatchesOther(test))
                    {
                        output.Add(XPathNodeId.Document);
                    }

                    return 1 + Scan(0, _document.RowCount, _document.RowCount, test, ref output);
                default:
                    // The document node has no parent, no siblings, no attributes and no namespace nodes, and
                    // nothing precedes or follows it.
                    return 0;
            }
        }

        /// <summary>
        /// Selects from an attribute or a namespace node.
        /// </summary>
        /// <param name="context">The context node.</param>
        /// <param name="element">The row of its element.</param>
        /// <param name="axis">The axis.</param>
        /// <param name="test">The node test.</param>
        /// <param name="output">The buffer.</param>
        /// <returns>The number of rows looked at.</returns>
        private long SelectFromOwned(long context, int element, XPathAxis axis, XPathNodeTest test, ref XPathIdBuffer output)
        {
            switch (axis)
            {
                case XPathAxis.Self:
                case XPathAxis.DescendantOrSelf:
                    if (MatchesOther(test))
                    {
                        output.Add(context);
                    }

                    return 1;
                case XPathAxis.Parent:
                    if (MatchesRow(in _document.Row(element), test))
                    {
                        output.Add(XPathNodeId.FromRow(element));
                    }

                    return 1;
                case XPathAxis.Ancestor:
                    return AppendAncestors(element, includeSelf: true, test, ref output);
                case XPathAxis.AncestorOrSelf:
                    var looked = AppendAncestors(element, includeSelf: true, test, ref output);
                    if (MatchesOther(test))
                    {
                        output.Add(context);
                    }

                    return looked;
                case XPathAxis.Following:
                    return Scan(element + 1 + _document.Row(element).AttributeCount, _document.RowCount, _document.RowCount, test, ref output);
                case XPathAxis.Preceding:
                    return Scan(0, element, element, test, ref output);
                default:
                    // An attribute or a namespace node has no children, no siblings, no attributes and no namespace
                    // nodes.
                    return 0;
            }
        }

        /// <summary>
        /// Selects from an element or a leaf.
        /// </summary>
        /// <param name="r">The row of the context node.</param>
        /// <param name="axis">The axis.</param>
        /// <param name="test">The node test.</param>
        /// <param name="output">The buffer.</param>
        /// <returns>The number of rows looked at.</returns>
        private long SelectFromRow(int r, XPathAxis axis, XPathNodeTest test, ref XPathIdBuffer output)
        {
            ref var row = ref _document.Row(r);
            var isElement = XmlDocument.IsElementKind(row.Kind);
            var count = _document.RowCount;
            switch (axis)
            {
                case XPathAxis.Child:
                    return isElement ? WalkSiblings(r + 1 + row.AttributeCount, row.EndIndex, test, ref output) : 0;
                case XPathAxis.Descendant:
                    return isElement ? Scan(r + 1 + row.AttributeCount, row.EndIndex, count, test, ref output) : 0;
                case XPathAxis.DescendantOrSelf:
                    if (MatchesRow(in row, test))
                    {
                        output.Add(XPathNodeId.FromRow(r));
                    }

                    return 1 + (isElement ? Scan(r + 1 + row.AttributeCount, row.EndIndex, count, test, ref output) : 0);
                case XPathAxis.Parent:
                    var parent = _document.GetParentTable()[r];
                    if (parent < 0)
                    {
                        if (MatchesOther(test))
                        {
                            output.Add(XPathNodeId.Document);
                        }
                    }
                    else if (MatchesRow(in _document.Row(parent), test))
                    {
                        output.Add(XPathNodeId.FromRow(parent));
                    }

                    return 1;
                case XPathAxis.Ancestor:
                    return AppendAncestors(r, includeSelf: false, test, ref output);
                case XPathAxis.AncestorOrSelf:
                    return AppendAncestors(r, includeSelf: true, test, ref output);
                case XPathAxis.FollowingSibling:
                {
                    var p = _document.GetParentTable()[r];
                    var limit = p < 0 ? count : _document.Row(p).EndIndex;
                    var start = IsText(row.Kind) ? SiblingRunEnd(r, limit) : _document.NextSiblingIndex(r);
                    return WalkSiblings(start, limit, test, ref output);
                }

                case XPathAxis.PrecedingSibling:
                {
                    var p = _document.GetParentTable()[r];
                    var start = p < 0 ? 0 : p + 1 + _document.Row(p).AttributeCount;
                    return WalkSiblings(start, r, test, ref output);
                }

                case XPathAxis.Following:
                    int[]? parents = null;
                    var following = isElement ? row.EndIndex : IsText(row.Kind) ? RunEnd(r, count, ref parents) : r + 1;
                    return Scan(following, count, count, test, ref output);
                case XPathAxis.Preceding:
                    return Scan(0, r, r, test, ref output);
                case XPathAxis.Attribute:
                    return isElement ? AppendAttributes(r, test, ref output) : 0;
                case XPathAxis.Namespace:
                    return isElement ? AppendNamespaces(r, test, ref output) : 0;
                default:
                    // XPathAxis.Self.
                    if (MatchesRow(in row, test))
                    {
                        output.Add(XPathNodeId.FromRow(r));
                    }

                    return 1;
            }
        }

        /// <summary>
        /// Returns the row after the text run that starts at a sibling row.
        /// </summary>
        /// <param name="start">The first row of the run.</param>
        /// <param name="limit">The end of the parent: every row before it after <paramref name="start"/> is a sibling.</param>
        /// <returns>The row after the run.</returns>
        /// <remarks>
        /// The row after a leaf, when it is before the end of the leaf's parent, is the leaf's next sibling, so a walk
        /// over siblings needs no parent table to find where a run ends.
        /// </remarks>
        private int SiblingRunEnd(int start, int limit)
        {
            var end = start + 1;
            while (end < limit && IsText(_document.Row(end).Kind))
            {
                end++;
            }

            return end;
        }

        /// <summary>
        /// Returns the string made of the text and CDATA rows in a range.
        /// </summary>
        /// <param name="start">The first row.</param>
        /// <param name="end">The row after the range.</param>
        /// <returns>A slice of the document when one row holds all the text; a new array otherwise.</returns>
        private ReadOnlyMemory<byte> Text(int start, int end)
        {
            var length = 0;
            var last = -1;
            var rows = 0;
            for (var i = start; i < end; i++)
            {
                ref var row = ref _document.Row(i);
                if (IsText(row.Kind) && row.ValueLength > 0)
                {
                    length += row.ValueLength;
                    last = i;
                    rows++;
                }
            }

            if (rows == 0)
            {
                return ReadOnlyMemory<byte>.Empty;
            }

            if (rows == 1)
            {
                return ValueMemory(last);
            }

            var text = new byte[length];
            var written = 0;
            for (var i = start; i <= last; i++)
            {
                ref var row = ref _document.Row(i);
                if (IsText(row.Kind))
                {
                    _document.Value(in row).CopyTo(text.AsSpan(written));
                    written += row.ValueLength;
                }
            }

            return text;
        }

        /// <summary>
        /// Returns the value of a row as memory over the document's buffer.
        /// </summary>
        /// <param name="r">An attribute or leaf row.</param>
        /// <returns>The value.</returns>
        private ReadOnlyMemory<byte> ValueMemory(int r)
        {
            ref var row = ref _document.Row(r);
            return _document.SliceMemory(row.ValueStart, row.ValueLength);
        }

        /// <summary>
        /// Appends the nodes among a run of siblings that pass a test: the <c>child</c> and both sibling axes.
        /// </summary>
        /// <param name="start">The first sibling, which is not inside a text run.</param>
        /// <param name="limit">The row after the last sibling.</param>
        /// <param name="test">The node test.</param>
        /// <param name="output">The buffer.</param>
        /// <returns>The number of siblings looked at.</returns>
        private long WalkSiblings(int start, int limit, XPathNodeTest test, ref XPathIdBuffer output)
        {
            long looked = 0;
            var s = start;
            while (s < limit)
            {
                looked++;
                ref var row = ref _document.Row(s);
                if (XmlDocument.IsElementKind(row.Kind))
                {
                    if (MatchesRow(in row, test))
                    {
                        output.Add(XPathNodeId.FromRow(s));
                    }

                    s = row.EndIndex;
                    continue;
                }

                if (IsText(row.Kind))
                {
                    var end = SiblingRunEnd(s, limit);
                    if (test.MatchesText && HasCharacters(s, end))
                    {
                        output.Add(XPathNodeId.FromRow(s));
                    }

                    s = end;
                    continue;
                }

                if (MatchesRow(in row, test))
                {
                    output.Add(XPathNodeId.FromRow(s));
                }

                s++;
            }

            return looked;
        }

        #endregion

    }

}
