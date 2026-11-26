using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;
using CloudNimble.Text.Xml.Nodes;

namespace CloudNimble.Text.Xml
{

    /// <summary>
    /// Provides a mechanism for examining the structural content of an XML document without automatically
    /// instantiating data values. This class cannot be inherited.
    /// </summary>
    /// <example>
    /// <code>
    /// using var document = XmlDocument.Parse("&lt;order id=\"7\"&gt;&lt;qty&gt;3&lt;/qty&gt;&lt;/order&gt;"u8);
    /// var id = document.RootElement.GetAttribute("id").GetInt32();
    /// var qty = document.RootElement.GetChild("qty").GetInt32();
    /// </code>
    /// </example>
    /// <remarks>
    /// This class is the XML counterpart of <c>JsonDocument</c> in System.Text.Json. The document is read-only.
    /// It uses buffers from the shared array pool, and <see cref="Dispose"/> returns them. A document that
    /// is not disposed does not return its buffers to the pool. Using the document or an
    /// <see cref="XmlElement"/> that was obtained from it after it is disposed throws
    /// <see cref="ObjectDisposedException"/>.
    /// Comments and processing instructions before and after the document element are kept for
    /// <see cref="WriteTo"/>. They are not children of <see cref="RootElement"/>. Comments are kept by default, so that
    /// a document that is parsed and written back does not lose them. To leave them out, set
    /// <see cref="XmlDocumentOptions.CommentHandling"/> to <see cref="XmlCommentHandling.Skip"/>.
    /// The document owns a pooled copy of the UTF-8 input. Names and values that contain no entity or
    /// character references are stored as offsets into that copy. Only values that had references
    /// expanded and namespace URIs are copied a second time.
    /// </remarks>
    public sealed class XmlDocument : IDisposable
    {

        #region Fields

        private const string NoElementMessage = "The reader is not on an element and has none left to read.";

        private const string UnmatchedEndTagMessage = "End tag without a matching start tag.";

        private XmlDeclarationInfo? _declaration;
        private bool _disposed;
        private byte[] _extra;
        private int _extraLength;
        private bool _isPooled = true;
        private int _nsCacheLength;
        private int _nsCacheStart;
        private int[]? _parents;
        private int _rootIndex;
        private int _rowCount;
        private XmlDbRow[] _rows;
        private byte[] _source;
        private int _sourceLength;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the XML declaration of this document.
        /// </summary>
        /// <value>
        /// The XML declaration that was read when the document was parsed, or <see langword="null"/> if the
        /// document has no XML declaration. The version is reported as <c>1.0</c> or <c>1.1</c>, and the
        /// encoding is always reported as <c>utf-8</c>.
        /// </value>
        /// <exception cref="ObjectDisposedException">
        /// The document has been disposed.
        /// </exception>
        public XmlDeclarationInfo? Declaration
        {
            get
            {
                ThrowIfDisposed();
                return _declaration;
            }
        }

        /// <summary>
        /// Gets a value that indicates whether the document holds arrays rented from a pool, which
        /// <see cref="Dispose"/> returns.
        /// </summary>
        internal bool IsPooled => _isPooled;

        /// <summary>
        /// Gets the number of rows the document holds.
        /// </summary>
        /// <remarks>
        /// Does not check for disposal: a disposed document has none.
        /// </remarks>
        internal int RowCount => _rowCount;

        /// <summary>
        /// Gets the root element of this XML document.
        /// </summary>
        /// <value>
        /// An <see cref="XmlElement"/> that represents the document element.
        /// </value>
        /// <exception cref="ObjectDisposedException">
        /// The document has been disposed.
        /// </exception>
        /// <exception cref="XmlException">
        /// The document has no root element. No document that a public method returns is in that state.
        /// </exception>
        public XmlElement RootElement
        {
            get
            {
                ThrowIfDisposed();
                if (_rootIndex < 0)
                {
                    throw XmlException.Create("The document has no root element.");
                }

                return new XmlElement(this, _rootIndex);
            }
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates an empty document database that copies every captured slice.
        /// </summary>
        internal XmlDocument() : this([], 0)
        {
        }

        /// <summary>
        /// Reads UTF-8 encoded XML and returns the namespaces it declares, each with the prefix it is declared
        /// under.
        /// </summary>
        /// <param name="xml">The UTF-8 encoded XML to read.</param>
        /// <param name="options">Options that control how the XML is read.</param>
        /// <returns>
        /// The namespace URIs, keyed by prefix, in the order the document first declares each prefix. The default
        /// namespace of the document element has an empty string as its prefix.
        /// </returns>
        /// <exception cref="XmlException">
        /// <paramref name="xml"/> is not a well-formed XML document.
        /// -or-
        /// A limit set in <paramref name="options"/> is exceeded.
        /// </exception>
        /// <example>
        /// <code>
        /// var options = new XmlSerializerOptions { Namespaces = XmlDocument.DiscoverNamespaces(existingFeed) };
        /// string xml = XmlSerializer.Serialize(feed, options);
        /// </code>
        /// </example>
        /// <remarks>
        /// <para>
        /// Every start tag in the document is examined for prefixes, not only that of the document element. A
        /// prefix that the document binds more than once is listed with the namespace of its first declaration.
        /// The <c>xml</c> prefix is listed only if the document declares it.
        /// </para>
        /// <para>
        /// A default namespace (<c>xmlns="..."</c>) is listed only when the document element declares it. One
        /// that an element further in declares names the elements under that element alone, so it could not be
        /// declared for the whole document without renaming the rest.
        /// </para>
        /// <para>
        /// The result can be assigned to <see cref="XmlSerializerOptions.Namespaces"/>, so that what is serialized
        /// uses the prefixes of an existing document. No <see cref="XmlDocument"/> is built.
        /// </para>
        /// </remarks>
        public static OrderedDictionary<string, string> DiscoverNamespaces(ReadOnlySpan<byte> xml, XmlDocumentOptions options = default)
        {
            ThrowIfDocumentTooLarge(xml.Length, options.MaxDocumentByteCount);
            return DiscoverNamespaces(xml, options, ignoreDeclaredEncoding: false);
        }

        /// <summary>
        /// Reads XML from a stream of UTF-8 bytes and returns the namespaces it declares, each with the prefix it
        /// is declared under.
        /// </summary>
        /// <param name="xml">The stream to read. It is read to its end.</param>
        /// <param name="options">Options that control how the XML is read.</param>
        /// <returns>
        /// The namespace URIs, keyed by prefix, in the order the document first declares each prefix. The default
        /// namespace of the document element has an empty string as its prefix.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="xml"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="XmlException">
        /// The stream does not hold a well-formed XML document.
        /// -or-
        /// A limit set in <paramref name="options"/> is exceeded.
        /// </exception>
        /// <remarks>
        /// See <see cref="DiscoverNamespaces(ReadOnlySpan{byte}, XmlDocumentOptions)"/> for what is listed.
        /// </remarks>
        public static OrderedDictionary<string, string> DiscoverNamespaces(Stream xml, XmlDocumentOptions options = default)
        {
            ArgumentNullException.ThrowIfNull(xml);
            var rented = XmlStreamBuffer.ReadToEnd(xml, options.MaxDocumentByteCount, out var length);
            try
            {
                return DiscoverNamespaces(rented.AsSpan(0, length), options, ignoreDeclaredEncoding: false);
            }
            finally
            {
                ReturnBuffer(rented, length);
            }
        }

        /// <summary>
        /// Reads XML text and returns the namespaces it declares, each with the prefix it is declared under.
        /// </summary>
        /// <param name="xml">The XML text to read.</param>
        /// <param name="options">Options that control how the XML is read.</param>
        /// <returns>
        /// The namespace URIs, keyed by prefix, in the order the document first declares each prefix. The default
        /// namespace of the document element has an empty string as its prefix.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="xml"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="XmlException">
        /// <paramref name="xml"/> is not a well-formed XML document.
        /// -or-
        /// A limit set in <paramref name="options"/> is exceeded.
        /// </exception>
        /// <remarks>
        /// See <see cref="DiscoverNamespaces(ReadOnlySpan{byte}, XmlDocumentOptions)"/> for what is listed. The
        /// encoding that an XML declaration names is not checked, because the text is already decoded.
        /// </remarks>
        public static OrderedDictionary<string, string> DiscoverNamespaces(string xml, XmlDocumentOptions options = default)
        {
            ArgumentNullException.ThrowIfNull(xml);
            var byteCount = Encoding.UTF8.GetByteCount(xml);
            ThrowIfDocumentTooLarge(byteCount, options.MaxDocumentByteCount);
            var rented = ArrayPool<byte>.Shared.Rent(byteCount);
            var written = Encoding.UTF8.GetBytes(xml, rented);
            try
            {
                return DiscoverNamespaces(rented.AsSpan(0, written), options, ignoreDeclaredEncoding: true);
            }
            finally
            {
                ReturnBuffer(rented, written);
            }
        }

        /// <summary>
        /// Creates an empty document database over an owned UTF-8 payload.
        /// </summary>
        /// <param name="source">A buffer rented from <see cref="ArrayPool{T}.Shared"/>, or an empty array.</param>
        /// <param name="sourceLength">The number of payload bytes in <paramref name="source"/>.</param>
        /// <remarks>
        /// The document takes ownership of <paramref name="source"/> and returns it on <see cref="Dispose"/>.
        /// Rows are pre-sized on the assumption of one node per 12 payload bytes (the STJ <c>MetadataDb</c> estimate).
        /// </remarks>
        internal XmlDocument(byte[] source, int sourceLength)
        {
            _source = source;
            _sourceLength = sourceLength;
            _extra = [];
            _rows = ArrayPool<XmlDbRow>.Shared.Rent(Math.Max(16, sourceLength / 12));
            _rootIndex = -1;
        }

        #endregion

        #region Public Methods

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_isPooled)
            {
                ReturnBuffer(_source, _sourceLength);
                ReturnBuffer(_extra, _extraLength);
                ArrayPool<XmlDbRow>.Shared.Return(_rows);
            }

            _source = [];
            _extra = [];
            _rows = [];
            _parents = null;
            _sourceLength = 0;
            _extraLength = 0;
            _rowCount = 0;
        }

        /// <summary>
        /// Parses a string that holds a single XML document into an <see cref="XmlDocument"/>.
        /// </summary>
        /// <param name="xml">The XML text to parse.</param>
        /// <param name="options">
        /// Options that control the parser behavior. By default, comments are kept, so that writing the
        /// document back does not lose them. To leave comments out, set <see cref="XmlDocumentOptions.CommentHandling"/>
        /// to <see cref="XmlCommentHandling.Skip"/>.
        /// </param>
        /// <returns>
        /// An <see cref="XmlDocument"/> representation of the XML document.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="xml"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="XmlException">
        /// <paramref name="xml"/> is not a well-formed XML document.
        /// -or-
        /// <paramref name="xml"/> exceeds a limit that is set in <paramref name="options"/>.
        /// </exception>
        /// <remarks>
        /// The text is converted to UTF-8 in a pooled buffer that the document owns.
        /// <see cref="XmlDocumentOptions.MaxDocumentByteCount"/> applies to the UTF-8 size.
        /// </remarks>
        public static XmlDocument Parse(string xml, XmlDocumentOptions options = default)
        {
            ArgumentNullException.ThrowIfNull(xml);
            return Parse(xml.AsSpan(), options);
        }

        /// <summary>
        /// Parses a span of characters that holds a single XML document into an <see cref="XmlDocument"/>.
        /// </summary>
        /// <param name="xml">The XML text to parse.</param>
        /// <param name="options">
        /// Options that control the parser behavior. By default, comments are kept, so that writing the
        /// document back does not lose them. To leave comments out, set <see cref="XmlDocumentOptions.CommentHandling"/>
        /// to <see cref="XmlCommentHandling.Skip"/>.
        /// </param>
        /// <returns>
        /// An <see cref="XmlDocument"/> representation of the XML document.
        /// </returns>
        /// <exception cref="XmlException">
        /// <paramref name="xml"/> is not a well-formed XML document.
        /// -or-
        /// <paramref name="xml"/> exceeds a limit that is set in <paramref name="options"/>.
        /// </exception>
        /// <remarks>
        /// The text is converted to UTF-8 in a pooled buffer that the document owns.
        /// <see cref="XmlDocumentOptions.MaxDocumentByteCount"/> applies to the UTF-8 size.
        /// </remarks>
        public static XmlDocument Parse(ReadOnlySpan<char> xml, XmlDocumentOptions options = default)
        {
            var byteCount = Encoding.UTF8.GetByteCount(xml);
            ThrowIfDocumentTooLarge(byteCount, options.MaxDocumentByteCount);
            var rented = ArrayPool<byte>.Shared.Rent(byteCount);
            var written = Encoding.UTF8.GetBytes(xml, rented);
            return ParseOwned(rented, written, options, ignoreDeclaredEncoding: true);
        }

        /// <summary>
        /// Parses a span of UTF-8 bytes that holds a single XML document into an <see cref="XmlDocument"/>.
        /// </summary>
        /// <param name="xml">The UTF-8 encoded XML text to parse.</param>
        /// <param name="options">
        /// Options that control the parser behavior. By default, comments are kept, so that writing the
        /// document back does not lose them. To leave comments out, set <see cref="XmlDocumentOptions.CommentHandling"/>
        /// to <see cref="XmlCommentHandling.Skip"/>.
        /// </param>
        /// <returns>
        /// An <see cref="XmlDocument"/> representation of the XML document.
        /// </returns>
        /// <exception cref="XmlException">
        /// <paramref name="xml"/> is not a well-formed XML document.
        /// -or-
        /// <paramref name="xml"/> exceeds a limit that is set in <paramref name="options"/>.
        /// </exception>
        /// <remarks>
        /// The bytes are copied into a pooled buffer that the document owns, so the document does not
        /// depend on <paramref name="xml"/> after this method returns.
        /// </remarks>
        public static XmlDocument Parse(ReadOnlySpan<byte> xml, XmlDocumentOptions options = default)
        {
            ThrowIfDocumentTooLarge(xml.Length, options.MaxDocumentByteCount);
            var rented = ArrayPool<byte>.Shared.Rent(xml.Length);
            xml.CopyTo(rented);
            return ParseOwned(rented, xml.Length, options, ignoreDeclaredEncoding: false);
        }

        /// <summary>
        /// Parses a sequence of UTF-8 bytes that holds a single XML document into an <see cref="XmlDocument"/>.
        /// </summary>
        /// <param name="xml">The UTF-8 encoded XML text to parse.</param>
        /// <param name="options">
        /// Options that control the parser behavior. By default, comments are kept, so that writing the
        /// document back does not lose them. To leave comments out, set <see cref="XmlDocumentOptions.CommentHandling"/>
        /// to <see cref="XmlCommentHandling.Skip"/>.
        /// </param>
        /// <returns>
        /// An <see cref="XmlDocument"/> representation of the XML document.
        /// </returns>
        /// <exception cref="OverflowException">
        /// <paramref name="xml"/> is longer than <see cref="int.MaxValue"/> bytes.
        /// </exception>
        /// <exception cref="XmlException">
        /// <paramref name="xml"/> is not a well-formed XML document.
        /// -or-
        /// <paramref name="xml"/> exceeds a limit that is set in <paramref name="options"/>.
        /// </exception>
        /// <remarks>
        /// The bytes are copied into one pooled buffer that the document owns, so the document does not
        /// depend on <paramref name="xml"/> after this method returns.
        /// </remarks>
        public static XmlDocument Parse(ReadOnlySequence<byte> xml, XmlDocumentOptions options = default)
        {
            var length = checked((int)xml.Length);
            ThrowIfDocumentTooLarge(length, options.MaxDocumentByteCount);
            var rented = ArrayPool<byte>.Shared.Rent(length);
            xml.CopyTo(rented);
            return ParseOwned(rented, length, options, ignoreDeclaredEncoding: false);
        }

        /// <summary>
        /// Parses a stream of UTF-8 bytes that holds a single XML document into an <see cref="XmlDocument"/>.
        /// The stream is read to completion.
        /// </summary>
        /// <param name="xml">The stream that holds the UTF-8 encoded XML text to parse.</param>
        /// <param name="options">
        /// Options that control the parser behavior. By default, comments are kept, so that writing the
        /// document back does not lose them. To leave comments out, set <see cref="XmlDocumentOptions.CommentHandling"/>
        /// to <see cref="XmlCommentHandling.Skip"/>.
        /// </param>
        /// <returns>
        /// An <see cref="XmlDocument"/> representation of the XML document.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="xml"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="XmlException">
        /// <paramref name="xml"/> does not hold a well-formed XML document.
        /// -or-
        /// <paramref name="xml"/> exceeds a limit that is set in <paramref name="options"/>.
        /// </exception>
        /// <remarks>
        /// When <see cref="XmlDocumentOptions.MaxDocumentByteCount"/> is set, reading stops as soon as the
        /// stream is found to be longer than that limit. The stream is not closed.
        /// </remarks>
        public static XmlDocument Parse(Stream xml, XmlDocumentOptions options = default)
        {
            ArgumentNullException.ThrowIfNull(xml);
            var rented = XmlStreamBuffer.ReadToEnd(xml, options.MaxDocumentByteCount, out var length);
            return ParseOwned(rented, length, options, ignoreDeclaredEncoding: false);
        }

        /// <summary>
        /// Parses a stream of UTF-8 bytes that holds a single XML document into an <see cref="XmlDocument"/>,
        /// without blocking the calling thread. The stream is read to completion.
        /// </summary>
        /// <param name="xml">The stream that holds the UTF-8 encoded XML text to parse.</param>
        /// <param name="options">
        /// Options that control the parser behavior. By default, comments are kept, so that writing the
        /// document back does not lose them. To leave comments out, set <see cref="XmlDocumentOptions.CommentHandling"/>
        /// to <see cref="XmlCommentHandling.Skip"/>.
        /// </param>
        /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
        /// <returns>
        /// A task that produces an <see cref="XmlDocument"/> representation of the XML document.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="xml"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// <paramref name="cancellationToken"/> was canceled.
        /// </exception>
        /// <exception cref="XmlException">
        /// <paramref name="xml"/> does not hold a well-formed XML document.
        /// -or-
        /// <paramref name="xml"/> exceeds a limit that is set in <paramref name="options"/>.
        /// </exception>
        /// <example>
        /// <code>
        /// await using var stream = File.OpenRead("catalog.xml");
        /// using var document = await XmlDocument.ParseAsync(stream);
        /// var count = document.RootElement.GetChild("item").GetChild("qty").GetInt32();
        /// </code>
        /// </example>
        /// <remarks>
        /// A <see langword="null"/> stream is refused before the task is created. When
        /// <see cref="XmlDocumentOptions.MaxDocumentByteCount"/> is set, reading stops as soon as the stream
        /// is found to be longer than that limit. The stream is not closed.
        /// </remarks>
        public static Task<XmlDocument> ParseAsync(Stream xml, XmlDocumentOptions options = default, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(xml);
            return ParseStreamAsync(xml, options, cancellationToken);
        }

        /// <summary>
        /// Parses one XML element (including its attributes and descendants) from the provided reader.
        /// </summary>
        /// <param name="reader">The reader to read. It is positioned on or before the start tag of the element.</param>
        /// <returns>
        /// An <see cref="XmlDocument"/> representation of the element (and nested nodes) read from the reader.
        /// </returns>
        /// <exception cref="XmlException">
        /// <paramref name="reader"/> reaches an end tag before it reaches a start tag.
        /// -or-
        /// <paramref name="reader"/> reaches the end of its input without reading an element.
        /// -or-
        /// The XML that <paramref name="reader"/> reads is not well formed.
        /// </exception>
        /// <remarks>
        /// If the reader is positioned on an attribute, it is moved back to the element that owns the attribute.
        /// When this method returns, the reader is positioned on the end tag of the element, or on the
        /// element itself if it is an empty-element tag.
        /// The document copies the names and values it keeps, so it does not depend on the reader's input
        /// after this method returns.
        /// The reader is not restored to its original position when this method throws.
        /// </remarks>
        public static XmlDocument ParseValue(ref Utf8XmlReader reader)
        {
            var document = new XmlDocument();
            try
            {
                document.FillFromReader(ref reader, requireDocumentRoot: false);
                if (document._rootIndex < 0)
                {
                    // The reader ran out of input without reaching a start tag. A document with no element
                    // has nothing to give back.
                    throw XmlException.Create(NoElementMessage);
                }

                return document;
            }
            catch
            {
                document.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Attempts to parse a span of UTF-8 bytes that holds a single XML document into an <see cref="XmlDocument"/>.
        /// </summary>
        /// <param name="xml">The UTF-8 encoded XML text to parse.</param>
        /// <param name="options">
        /// Options that control the parser behavior. By default, comments are kept, so that writing the
        /// document back does not lose them. To leave comments out, set <see cref="XmlDocumentOptions.CommentHandling"/>
        /// to <see cref="XmlCommentHandling.Skip"/>.
        /// </param>
        /// <param name="document">
        /// When this method returns, contains the parsed document if parsing succeeded, or
        /// <see langword="null"/> if it failed. This parameter is treated as uninitialized.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if <paramref name="xml"/> is a well-formed XML document that is within
        /// the limits set in <paramref name="options"/>; otherwise, <see langword="false"/>.
        /// </returns>
        /// <remarks>
        /// This method returns <see langword="false"/> in every case where
        /// <see cref="Parse(ReadOnlySpan{byte}, XmlDocumentOptions)"/> throws an <see cref="XmlException"/>.
        /// </remarks>
        public static bool TryParse(ReadOnlySpan<byte> xml, XmlDocumentOptions options, out XmlDocument? document)
        {
            try
            {
                document = Parse(xml, options);
                return true;
            }
            catch (XmlException)
            {
                document = null;
                return false;
            }
        }

        /// <summary>
        /// Attempts to parse one XML element (including its attributes and descendants) from the provided reader.
        /// </summary>
        /// <param name="reader">The reader to read. It is positioned on or before the start tag of the element.</param>
        /// <param name="document">
        /// When this method returns, contains the parsed document if the element was read, or
        /// <see langword="null"/> if it was not. This parameter is treated as uninitialized.
        /// </param>
        /// <returns>
        /// <see langword="true"/> if the element was read; <see langword="false"/> if the reader's input ends
        /// before the element does and <see cref="Utf8XmlReader.IsFinalBlock"/> is <see langword="false"/>,
        /// or if the reader is on an end tag.
        /// </returns>
        /// <exception cref="XmlException">
        /// <paramref name="reader"/> reaches an end tag before it reaches a start tag.
        /// -or-
        /// <paramref name="reader"/> reaches the end of its input without reading an element, and
        /// <see cref="Utf8XmlReader.IsFinalBlock"/> is <see langword="true"/>.
        /// -or-
        /// The XML that <paramref name="reader"/> reads is not well formed.
        /// </exception>
        /// <example>
        /// <code>
        /// var reader = new Utf8XmlReader(block, isFinalBlock: false, state);
        /// if (!XmlDocument.TryParseValue(ref reader, out XmlDocument? item))
        /// {
        ///     // The element is not all in this block. Read more input and call again.
        /// }
        /// </code>
        /// </example>
        /// <remarks>
        /// This is the counterpart of <c>JsonDocument.TryParseValue</c>. When this method returns
        /// <see langword="true"/>, the reader is where <see cref="ParseValue"/> leaves it: on the end tag of
        /// the element, or on the element itself if it is an empty-element tag. When it returns
        /// <see langword="false"/> or throws, the reader is where it was before the call, so the caller can
        /// supply more input and call again. The element is read twice: once to find its end, and once to keep it.
        /// </remarks>
        public static bool TryParseValue(ref Utf8XmlReader reader, [NotNullWhen(true)] out XmlDocument? document)
        {
            document = null;
            if (reader.TokenType is XmlTokenType.EndElement)
            {
                return false;
            }

            // The element is found and read to its end by a second reader first. That reader shares nothing
            // with this one, so when the element turns out not to be complete, or not to be well formed, the
            // caller's reader has not moved.
            var started = reader.TokenType is XmlTokenType.StartElement or XmlTokenType.Attribute;
            if (!started || !reader.IsEmptyElement)
            {
                var open = started ? 1 : 0;
                var ahead = reader.CreateLookAhead();
                while (!started || open > 0)
                {
                    if (!ahead.Read())
                    {
                        if (reader.IsFinalBlock)
                        {
                            throw XmlException.Create(NoElementMessage);
                        }

                        return false;
                    }

                    if (ahead.TokenType is XmlTokenType.StartElement)
                    {
                        started = true;
                        if (!ahead.IsEmptyElement)
                        {
                            open++;
                        }
                    }
                    else if (ahead.TokenType is XmlTokenType.EndElement)
                    {
                        if (!started)
                        {
                            throw XmlException.Create(UnmatchedEndTagMessage);
                        }

                        open--;
                    }
                }
            }

            document = ParseValue(ref reader);
            return true;
        }

        /// <summary>
        /// Writes the document to the provided writer as XML.
        /// </summary>
        /// <param name="writer">The writer to which to write the document.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="writer"/> is <see langword="null"/>.
        /// </exception>
        /// <exception cref="ObjectDisposedException">
        /// The document has been disposed.
        /// </exception>
        /// <remarks>
        /// The XML declaration (when the document has one) is written first, then the comments and processing
        /// instructions before the root element, the root element, and the comments and processing
        /// instructions after it. The declaration belongs to the document, so it is written whatever the writer's
        /// <see cref="XmlWriterOptions.OmitXmlDeclaration"/> says: that option only decides whether the writer adds a declaration
        /// to a document that has none. Write with it set to <see langword="true"/> to get back what was parsed. The
        /// declaration is written with the version of the parsed document and
        /// <c>encoding="utf-8"</c>.
        /// </remarks>
        public void WriteTo(Utf8XmlWriter writer)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ThrowIfDisposed();
            if (_declaration is { } declaration)
            {
                writer.WriteRawValue(FormatDeclaration(declaration), skipInputValidation: true);
                writer.MarkDeclarationWritten();
            }

            for (var i = 0; i < _rowCount; i = NextSiblingIndex(i))
            {
                WriteRow(i, writer);
            }
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Adds <paramref name="row"/> and returns its index.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <returns>The row index.</returns>
        internal int AddRow(XmlDbRow row)
        {
            EnsureRows(1);
            var index = _rowCount;
            _rows[index] = row;
            _rowCount = index + 1;
            return index;
        }

        /// <summary>
        /// Appends <paramref name="utf8"/> onto the extra buffer and returns its offset in that buffer.
        /// </summary>
        /// <param name="utf8">The bytes to copy.</param>
        /// <returns>The starting offset within the extra buffer.</returns>
        internal int AppendExtra(ReadOnlySpan<byte> utf8)
        {
            if (_extraLength + utf8.Length > _extra.Length)
            {
                var rented = ArrayPool<byte>.Shared.Rent(Math.Max(Math.Max(_extra.Length * 2, 256), _extraLength + utf8.Length));
                _extra.AsSpan(0, _extraLength).CopyTo(rented);
                ReturnBuffer(_extra, _extraLength);
                _extra = rented;
            }

            utf8.CopyTo(_extra.AsSpan(_extraLength));
            var start = _extraLength;
            _extraLength += utf8.Length;
            return start;
        }

        /// <summary>
        /// Captures the current start tag, including attributes.
        /// </summary>
        /// <param name="reader">The tokenizer on a start tag.</param>
        /// <returns>The element row index.</returns>
        /// <remarks>
        /// Attribute rows directly follow the element row. <c>xsi:nil</c> is detected in the same pass
        /// that copies the attributes, so the tag is walked once.
        /// </remarks>
        internal int CaptureElement(ref Utf8XmlReader reader)
        {
            var attributeCount = reader.AttributeCount;
            var index = _rowCount;
            EnsureRows(attributeCount + 1);
            _rowCount = index + attributeCount + 1;
            var name = reader.CurrentQualifiedName(out var prefixLength);
            var ns = reader.NamespaceUriSpan;
            var isEmpty = reader.IsEmptyElement;
            var isNil = false;
            for (var i = 0; i < attributeCount; i++)
            {
                ref readonly var attribute = ref reader.AttributeAt(i);
                var attributeName = reader.AttributeQualifiedName(in attribute);
                var attributeValue = reader.AttributeValue(attribute);
                ReadOnlySpan<byte> attributeNs = [];
                if (attribute.PrefixLength > 0)
                {
                    attributeNs = reader.ResolveAttributeNamespace(attribute);
                    isNil = isNil || IsNilAttribute(attributeName[(attribute.PrefixLength + 1)..], attributeNs, attributeValue);
                }
                else if (attributeName.SequenceEqual(XmlUtf8.XmlnsPrefix))
                {
                    attributeNs = XmlUtf8.Xmlns;
                }

                _rows[index + 1 + i] = new XmlDbRow
                {
                    Kind = XmlValueKind.Attribute,
                    NameStart = Locate(attributeName),
                    NameLength = attributeName.Length,
                    PrefixLength = attribute.PrefixLength,
                    NsStart = LocateNamespace(attributeNs),
                    NsLength = attributeNs.Length,
                    ValueStart = Locate(attributeValue),
                    ValueLength = attributeValue.Length,
                };
            }

            _rows[index] = new XmlDbRow
            {
                Kind = isNil ? XmlValueKind.Nil : isEmpty ? XmlValueKind.EmptyElement : XmlValueKind.Element,
                IsEmpty = isEmpty,
                NameStart = Locate(name),
                NameLength = name.Length,
                PrefixLength = prefixLength,
                NsStart = LocateNamespace(ns),
                NsLength = ns.Length,
                AttributeCount = attributeCount,
                EndIndex = _rowCount,
            };
            return index;
        }

        /// <summary>
        /// Captures the current comment, PI, text, or CDATA token.
        /// </summary>
        /// <param name="reader">The tokenizer.</param>
        /// <returns>The row index.</returns>
        internal int CaptureLeaf(ref Utf8XmlReader reader)
        {
            var kind = reader.TokenType switch
            {
                XmlTokenType.CData => XmlValueKind.CData,
                XmlTokenType.Comment => XmlValueKind.Comment,
                XmlTokenType.ProcessingInstruction => XmlValueKind.ProcessingInstruction,
                _ => XmlValueKind.Text,
            };
            ReadOnlySpan<byte> name = kind is XmlValueKind.ProcessingInstruction ? reader.LocalNameSpan : [];
            var value = reader.ValueSpan;
            return AddRow(new XmlDbRow
            {
                Kind = kind,
                NameStart = Locate(name),
                NameLength = name.Length,
                ValueStart = Locate(value),
                ValueLength = value.Length,
            });
        }

        /// <summary>
        /// Deep-clones row <paramref name="index"/> into a mutable node.
        /// </summary>
        /// <param name="index">The row index.</param>
        /// <param name="names">A table of 64 places for the name strings made so far, shared by the whole tree (<see cref="NameString"/>).</param>
        /// <param name="source">The source of a tree that keeps this document, or null for a copy that does not depend on it.</param>
        /// <returns>The mutable node.</returns>
        /// <remarks>
        /// With a source, an element is made with its name alone and keeps the document: its attributes and
        /// children are made when they are first asked for. With none, everything under it is made now.
        /// </remarks>
        internal XmlNode CloneRow(int index, string?[] names, XmlNodeSource? source)
        {
            ref var row = ref _rows[index];
            switch (row.Kind)
            {
                case XmlValueKind.Text:
                    return new XmlTextNode(Encoding.UTF8.GetString(Value(in row)));
                case XmlValueKind.CData:
                    return new XmlCDataNode(Encoding.UTF8.GetString(Value(in row)));
                case XmlValueKind.Comment:
                    return new XmlCommentNode(Encoding.UTF8.GetString(Value(in row)));
                case XmlValueKind.ProcessingInstruction:
                    return new XmlProcessingInstructionNode(
                        NameString(LocalName(in row), names),
                        Encoding.UTF8.GetString(Value(in row)));
                case XmlValueKind.Attribute:
                    return new XmlAttributeNode(
                        NameString(LocalName(in row), names),
                        Encoding.UTF8.GetString(Value(in row)),
                        NameString(NamespaceUri(in row), names),
                        NameString(Prefix(in row), names));
                default:
                    if (source is not null)
                    {
                        // The element keeps the document, and makes its attributes and children when asked.
                        return new XmlElementNode(
                            NameString(LocalName(in row), names),
                            NameString(NamespaceUri(in row), names),
                            NameString(Prefix(in row), names),
                            row.IsEmpty,
                            source,
                            index);
                    }

                    var element = new XmlElementNode(
                        NameString(LocalName(in row), names),
                        NameString(NamespaceUri(in row), names),
                        NameString(Prefix(in row), names))
                    {
                        IsEmptyElement = row.IsEmpty,
                    };
                    FillElement(element, index, names, null);
                    return element;
            }
        }

        /// <summary>
        /// Gives an element node the attributes and the children of row <paramref name="index"/>.
        /// </summary>
        /// <param name="element">The element node, which has neither yet.</param>
        /// <param name="index">The row index of the element.</param>
        /// <param name="names">The table of name strings made so far.</param>
        /// <param name="source">The source the child elements keep, or null to make their own content now.</param>
        /// <remarks>
        /// The lists are filled before the element is given them, so that a second thread reading the element
        /// never finds one half made (<see cref="XmlElementNode.SetContent"/>). An element with no attributes or
        /// no children is given no list for them, and makes an empty one if it is asked.
        /// </remarks>
        internal void FillElement(XmlElementNode element, int index, string?[] names, XmlNodeSource? source)
        {
            ref var row = ref _rows[index];
            var end = row.EndIndex;
            var attributeEnd = index + 1 + row.AttributeCount;
            XmlAttributeNodeList? attributes = null;
            if (row.AttributeCount > 0)
            {
                attributes = new XmlAttributeNodeList(element);
                for (var a = index + 1; a < attributeEnd; a++)
                {
                    attributes.Add((XmlAttributeNode)CloneRow(a, names, null));
                }
            }

            XmlNodeList? children = null;
            if (attributeEnd < end)
            {
                children = new XmlNodeList(element);
                for (var c = attributeEnd; c < end; c = NextSiblingIndex(c))
                {
                    children.Add(CloneRow(c, names, source));
                }
            }

            element.SetContent(attributes, children);
        }

        /// <summary>
        /// Makes room for <paramref name="count"/> more rows.
        /// </summary>
        /// <param name="count">The number of rows about to be written.</param>
        /// <remarks>
        /// Rows hold offsets and lengths only, never content, so the returned array is not cleared.
        /// </remarks>
        internal void EnsureRows(int count)
        {
            if (_rowCount + count <= _rows.Length)
            {
                return;
            }

            var rented = ArrayPool<XmlDbRow>.Shared.Rent(Math.Max(_rows.Length * 2, _rowCount + count));
            _rows.AsSpan(0, _rowCount).CopyTo(rented);
            ArrayPool<XmlDbRow>.Shared.Return(_rows);
            _rows = rented;
        }

        /// <summary>
        /// Fills this database from <paramref name="reader"/>.
        /// </summary>
        /// <param name="reader">The tokenizer.</param>
        /// <param name="requireDocumentRoot">When true, a single document element is required.</param>
        /// <remarks>
        /// Rows are appended in document order. The open-element stack only remembers which rows still
        /// need their <see cref="XmlDbRow.EndIndex"/> patched when the matching end tag arrives.
        /// </remarks>
        internal void FillFromReader(ref Utf8XmlReader reader, bool requireDocumentRoot)
        {
            Span<int> stack = stackalloc int[XmlLimits.DefaultMaxDepth];
            int[]? rentedStack = null;
            var depth = 0;
            if (reader.TokenType is XmlTokenType.Attribute)
            {
                reader.MoveToElement();
            }

            var started = reader.TokenType is not XmlTokenType.None;
            try
            {
                while (started || reader.Read())
                {
                    started = false;
                    switch (reader.TokenType)
                    {
                        case XmlTokenType.XmlDeclaration:
                            _declaration = ReadDeclaration(ref reader);
                            break;
                        case XmlTokenType.StartElement:
                            if (depth == 0)
                            {
                                if (_rootIndex >= 0)
                                {
                                    throw XmlException.Create("Multiple root elements are not allowed.");
                                }

                                _rootIndex = _rowCount;
                            }

                            var element = CaptureElement(ref reader);
                            if (!reader.IsEmptyElement)
                            {
                                if (depth == stack.Length)
                                {
                                    var grown = ArrayPool<int>.Shared.Rent(stack.Length * 2);
                                    stack.CopyTo(grown);
                                    ReturnScratch(rentedStack);
                                    rentedStack = grown;
                                    stack = grown;
                                }

                                stack[depth] = element;
                                depth++;
                            }
                            else if (!requireDocumentRoot && depth == 0)
                            {
                                return;
                            }

                            break;
                        case XmlTokenType.EndElement:
                            if (depth == 0)
                            {
                                // ParseValue takes a reader wherever its caller left it, which may be on or
                                // just before the end tag of an element this document never saw start.
                                throw XmlException.Create(UnmatchedEndTagMessage);
                            }

                            depth--;
                            _rows[stack[depth]].EndIndex = _rowCount;
                            if (!requireDocumentRoot && depth == 0)
                            {
                                return;
                            }

                            break;
                        default:
                            CaptureLeaf(ref reader);
                            break;
                    }
                }

                if (requireDocumentRoot && _rootIndex < 0)
                {
                    throw XmlException.Create("The document has no root element.");
                }

            }
            finally
            {
                while (depth > 0)
                {
                    depth--;
                    _rows[stack[depth]].EndIndex = _rowCount;
                }

                ReturnScratch(rentedStack);
            }
        }

        /// <summary>
        /// Formats <paramref name="declaration"/> as an XML declaration.
        /// </summary>
        /// <param name="declaration">The captured declaration.</param>
        /// <returns>The UTF-8 declaration markup.</returns>
        internal static ReadOnlySpan<byte> FormatDeclaration(XmlDeclarationInfo declaration)
        {
            var version11 = declaration.Version is "1.1";
            return declaration.Standalone switch
            {
                true => version11
                    ? "<?xml version=\"1.1\" encoding=\"utf-8\" standalone=\"yes\"?>"u8
                    : "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>"u8,
                false => version11
                    ? "<?xml version=\"1.1\" encoding=\"utf-8\" standalone=\"no\"?>"u8
                    : "<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"no\"?>"u8,
                null => version11
                    ? "<?xml version=\"1.1\" encoding=\"utf-8\"?>"u8
                    : "<?xml version=\"1.0\" encoding=\"utf-8\"?>"u8,
            };
        }

        /// <summary>
        /// Returns the parent of every row: the row index of the element it is in, or <c>-1</c> for a row that
        /// no element is above.
        /// </summary>
        /// <returns>One entry for each row. An attribute row holds the row of its element.</returns>
        /// <remarks>
        /// <para>
        /// The table is built the first time it is asked for, in one pass with no stack: the row before
        /// <c>r</c> is either the parent of <c>r</c> or a node below a preceding sibling of <c>r</c>, so the
        /// pass steps back over elements that have ended, and never enters one twice. It is linear in the
        /// number of rows.
        /// </para>
        /// <para>
        /// It is published with <see cref="Interlocked.CompareExchange{T}(ref T, T, T)"/>, so two threads that
        /// build it at once both get a correct table and every caller afterwards gets the one that was kept. It is
        /// a plain array, not a pooled one, and <see cref="Dispose"/> lets it go. Does not check for disposal, and is
        /// not to be called while the document is being disposed.
        /// </para>
        /// </remarks>
        internal int[] GetParentTable()
        {
            if (_parents is { } built)
            {
                return built;
            }

            var count = _rowCount;
            var parents = new int[count];
            for (var r = 0; r < count; r++)
            {
                var p = r - 1;
                while (p >= 0 && !(IsElementKind(_rows[p].Kind) && _rows[p].EndIndex > r))
                {
                    p = parents[p];
                }

                parents[r] = p;
            }

            // The first table to be published is the one every caller gets. A thread that loses the race drops
            // its own, which is equal to it.
            Interlocked.CompareExchange(ref _parents, parents, null);
            return _parents!;
        }

        /// <summary>
        /// Returns whether <paramref name="kind"/> is one of the element kinds.
        /// </summary>
        /// <param name="kind">The row kind.</param>
        /// <returns><see langword="true"/> for element, empty element, and nil rows.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsElementKind(XmlValueKind kind)
        {
            return kind is XmlValueKind.Element or XmlValueKind.EmptyElement or XmlValueKind.Nil;
        }

        /// <summary>
        /// Returns whether an attribute is <c>xsi:nil</c> with a true value.
        /// </summary>
        /// <param name="localName">The attribute local name.</param>
        /// <param name="namespaceUri">The attribute namespace URI.</param>
        /// <param name="value">The decoded attribute value.</param>
        /// <returns><see langword="true"/> for <c>xsi:nil="true"</c> or <c>xsi:nil="1"</c>.</returns>
        internal static bool IsNilAttribute(ReadOnlySpan<byte> localName, ReadOnlySpan<byte> namespaceUri, ReadOnlySpan<byte> value)
        {
            if (!localName.SequenceEqual("nil"u8) || !namespaceUri.SequenceEqual(XmlUtf8.Xsi))
            {
                return false;
            }

            var trimmed = XmlLexical.Collapse(value);
            return trimmed.SequenceEqual("true"u8) || trimmed.SequenceEqual("1"u8);
        }

        /// <summary>
        /// Returns the local name of <paramref name="row"/>.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <returns>The UTF-8 local name.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<byte> LocalName(in XmlDbRow row)
        {
            var skip = row.PrefixLength > 0 ? row.PrefixLength + 1 : 0;
            return Slice(row.NameStart + skip, row.NameLength - skip);
        }

        /// <summary>
        /// Returns the document offset of <paramref name="utf8"/>, copying it only when it is not part of the owned payload.
        /// </summary>
        /// <param name="utf8">A slice reported by the tokenizer.</param>
        /// <returns>An offset for <see cref="Slice"/>.</returns>
        /// <remarks>
        /// Tokenizer spans over the owned payload are recorded by address, which is the common case.
        /// Entity-decoded values, interned URIs, and everything from <see cref="ParseValue"/> are appended
        /// to the extra buffer, whose offsets start at the payload length.
        /// </remarks>
        internal int Locate(ReadOnlySpan<byte> utf8)
        {
            if (utf8.IsEmpty)
            {
                return 0;
            }

            var offset = Unsafe.ByteOffset(
                ref MemoryMarshal.GetArrayDataReference(_source),
                ref MemoryMarshal.GetReference(utf8));
            if ((nuint)offset < (nuint)_sourceLength)
            {
                return (int)offset;
            }

            return _sourceLength + AppendExtra(utf8);
        }

        /// <summary>
        /// Returns the document offset of a namespace URI, reusing the previous copy when it repeats.
        /// </summary>
        /// <param name="utf8">The namespace URI reported by the tokenizer.</param>
        /// <returns>An offset for <see cref="Slice"/>.</returns>
        /// <remarks>
        /// Sibling elements almost always share a namespace, so one cached copy removes a per-element append.
        /// </remarks>
        internal int LocateNamespace(ReadOnlySpan<byte> utf8)
        {
            if (utf8.IsEmpty)
            {
                return 0;
            }

            if (utf8.Length == _nsCacheLength && utf8.SequenceEqual(Slice(_nsCacheStart, _nsCacheLength)))
            {
                return _nsCacheStart;
            }

            _nsCacheStart = Locate(utf8);
            _nsCacheLength = utf8.Length;
            return _nsCacheStart;
        }

        /// <summary>
        /// Returns a name, a prefix or a namespace URI as a string, giving the same string for the same bytes.
        /// </summary>
        /// <param name="utf8">The UTF-8 bytes.</param>
        /// <param name="names">The table of 64 places that holds the strings made so far.</param>
        /// <returns>The string.</returns>
        /// <remarks>
        /// A document repeats a small set of names, and a tree made from it would otherwise hold one string for
        /// every element. The strings are kept by a hash of their bytes, where a new name takes the place of the
        /// one that was there. A name outside ASCII is not kept. The table belongs to the tree that is being
        /// made, not to the document, so a document that no tree is made from carries nothing for it.
        /// </remarks>
        internal static string NameString(ReadOnlySpan<byte> utf8, string?[] names)
        {
            if (utf8.IsEmpty)
            {
                return string.Empty;
            }

            var hash = 2166136261u;
            foreach (var value in utf8)
            {
                hash = (hash ^ value) * 16777619u;
            }

            ref var place = ref names[hash & 63];
            if (place is { } kept && Ascii.Equals(utf8, kept))
            {
                return kept;
            }

            var name = Encoding.UTF8.GetString(utf8);
            place = name;
            return name;
        }

        /// <summary>
        /// Returns the namespace URI of <paramref name="row"/>.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <returns>The UTF-8 namespace URI, or empty.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<byte> NamespaceUri(in XmlDbRow row)
        {
            return Slice(row.NsStart, row.NsLength);
        }

        /// <summary>
        /// Returns the row that follows <paramref name="index"/> at the same depth.
        /// </summary>
        /// <param name="index">The row index.</param>
        /// <returns>The index after the row, or after its whole subtree for an element.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int NextSiblingIndex(int index)
        {
            ref var row = ref _rows[index];
            return IsElementKind(row.Kind) ? row.EndIndex : index + 1;
        }

        /// <summary>
        /// Reads a document and collects its namespace declarations.
        /// </summary>
        /// <param name="utf8Xml">The document.</param>
        /// <param name="options">The options.</param>
        /// <param name="ignoreDeclaredEncoding">Whether the bytes were transcoded from characters, so the encoding the declaration names is not checked.</param>
        /// <returns>The namespace URIs by prefix, in the order each prefix is first declared.</returns>
        internal static OrderedDictionary<string, string> DiscoverNamespaces(ReadOnlySpan<byte> utf8Xml, XmlDocumentOptions options, bool ignoreDeclaredEncoding)
        {
            var readerOptions = options.ToReaderOptions();
            readerOptions.IgnoreDeclaredEncoding = ignoreDeclaredEncoding;
            var found = new OrderedDictionary<string, string>(StringComparer.Ordinal);
            var reader = new Utf8XmlReader(utf8Xml, isFinalBlock: true, new XmlReaderState(readerOptions));
            var isDocumentElement = true;
            while (reader.Read())
            {
                if (reader.TokenType is not XmlTokenType.StartElement)
                {
                    continue;
                }

                while (reader.MoveToNextAttribute())
                {
                    // A declaration with no value takes a binding away. It names no namespace.
                    if (!reader.NamespaceUriEquals(XmlUtf8.Xmlns) || reader.ValueSpan.IsEmpty)
                    {
                        continue;
                    }

                    if (!reader.PrefixSpan.IsEmpty)
                    {
                        found.TryAdd(Encoding.UTF8.GetString(reader.LocalNameSpan), reader.GetString()!);
                    }
                    else if (isDocumentElement)
                    {
                        // A prefix means the same wherever it is declared. A default namespace does not: it
                        // names the elements under the one that declares it, so only the document's own is listed.
                        found.Add(string.Empty, reader.GetString()!);
                    }
                }

                reader.MoveToElement();
                isDocumentElement = false;
            }

            return found;
        }

        /// <summary>
        /// Parses a rented UTF-8 payload, transferring its ownership to the new document.
        /// </summary>
        /// <param name="rented">A buffer rented from <see cref="ArrayPool{T}.Shared"/>.</param>
        /// <param name="length">The payload length.</param>
        /// <param name="options">Parse options.</param>
        /// <param name="ignoreDeclaredEncoding">Whether the bytes were transcoded from characters, so the encoding the declaration names is not checked.</param>
        /// <returns>The filled document.</returns>
        internal static XmlDocument ParseOwned(byte[] rented, int length, XmlDocumentOptions options, bool ignoreDeclaredEncoding)
        {
            var document = new XmlDocument(rented, length);
            try
            {
                // A rented buffer is usually longer than what was asked for. The reader works in the part
                // after the payload, and what it wrote there is cleared, because the buffer goes back to the
                // shared pool with only the payload cleared.
                var readerOptions = options.ToReaderOptions();
                readerOptions.IgnoreDeclaredEncoding = ignoreDeclaredEncoding;
                var reader = new Utf8XmlReader(rented.AsSpan(0, length), rented.AsSpan(length), sequenceCopy: null, sequence: default, hasSequence: false, isFinalBlock: true, new XmlReaderState(readerOptions));
                document.FillFromReader(ref reader, requireDocumentRoot: true);
                if (reader.ScratchBytesUsed != 0)
                {
                    rented.AsSpan(length, reader.ScratchBytesUsed).Clear();
                }

                return document;
            }
            catch
            {
                // The reader is gone by now, so everything after the payload is cleared.
                rented.AsSpan(length).Clear();
                document.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Reads <paramref name="xml"/> to its end and parses what was read.
        /// </summary>
        /// <param name="xml">The stream.</param>
        /// <param name="options">The parser options.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The document, which owns the buffer the stream was read into.</returns>
        internal static async Task<XmlDocument> ParseStreamAsync(Stream xml, XmlDocumentOptions options, CancellationToken cancellationToken)
        {
            var utf8 = await XmlStreamBuffer.ReadToEndAsync(xml, options.MaxDocumentByteCount, cancellationToken).ConfigureAwait(false);
            return ParseOwned(utf8.Array!, utf8.Count, options, ignoreDeclaredEncoding: false);
        }

        /// <summary>
        /// Returns the prefix of <paramref name="row"/>.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <returns>The UTF-8 prefix, or empty.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<byte> Prefix(in XmlDbRow row)
        {
            return Slice(row.NameStart, row.PrefixLength);
        }

        /// <summary>
        /// Reads declaration fields from the current token.
        /// </summary>
        /// <param name="reader">The tokenizer on an XML declaration it has already validated.</param>
        /// <returns>The captured declaration.</returns>
        internal static XmlDeclarationInfo ReadDeclaration(ref Utf8XmlReader reader)
        {
            XmlDeclarationSyntax.TryParse(reader.ValueSpan, out var version, out _, out var standalone);
            return new XmlDeclarationInfo(version is XmlVersion.Version11 ? "1.1" : "1.0", "utf-8", standalone);
        }

        /// <summary>
        /// Moves what the document holds out of the array pools into arrays of its own, and returns the
        /// rented ones.
        /// </summary>
        /// <remarks>
        /// For a document that nobody will dispose: the one behind an <see cref="XmlElement"/> that the
        /// serializer hands out on its own. Its arrays are then collected like any other object, and
        /// nothing is kept from the pools.
        /// </remarks>
        internal void MoveOutOfPool()
        {
            ThrowIfDisposed();
            var source = _source.AsSpan(0, _sourceLength).ToArray();
            var extra = _extra.AsSpan(0, _extraLength).ToArray();
            var rows = _rows.AsSpan(0, _rowCount).ToArray();
            ReturnBuffer(_source, _sourceLength);
            ReturnBuffer(_extra, _extraLength);
            ArrayPool<XmlDbRow>.Shared.Return(_rows);
            _source = source;
            _extra = extra;
            _rows = rows;
            _isPooled = false;
        }

        /// <summary>
        /// Clears the used part of a pooled content buffer and returns it.
        /// </summary>
        /// <param name="buffer">The rented buffer, or an empty array.</param>
        /// <param name="used">The number of bytes holding document content.</param>
        /// <remarks>
        /// Content buffers hold caller data, so they are cleared before they go back to the shared pool
        /// (STJ <c>JsonDocument.Dispose</c>).
        /// </remarks>
        internal static void ReturnBuffer(byte[] buffer, int used)
        {
            if (buffer.Length == 0)
            {
                return;
            }

            buffer.AsSpan(0, used).Clear();
            ArrayPool<byte>.Shared.Return(buffer);
        }

        /// <summary>
        /// Returns a scratch array to the shared pool when one was rented.
        /// </summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="rented">The rented array, or <see langword="null"/>.</param>
        internal static void ReturnScratch<T>(T[]? rented)
        {
            if (rented is not null)
            {
                ArrayPool<T>.Shared.Return(rented, clearArray: true);
            }
        }

        /// <summary>
        /// Returns the row at <paramref name="index"/>.
        /// </summary>
        /// <param name="index">The row index.</param>
        /// <returns>The row.</returns>
        /// <remarks>
        /// Does not check for disposal. Public entry points call <see cref="ThrowIfDisposed"/> once.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref XmlDbRow Row(int index)
        {
            return ref _rows[index];
        }

        /// <summary>
        /// Returns <paramref name="length"/> bytes at document offset <paramref name="start"/>.
        /// </summary>
        /// <param name="start">An offset below the payload length for the payload, otherwise into the extra buffer.</param>
        /// <param name="length">The length.</param>
        /// <returns>The UTF-8 bytes.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<byte> Slice(int start, int length)
        {
            return start < _sourceLength
                ? _source.AsSpan(start, length)
                : _extra.AsSpan(start - _sourceLength, length);
        }

        /// <summary>
        /// Returns <paramref name="length"/> bytes at document offset <paramref name="start"/> as memory over the
        /// document's own buffer.
        /// </summary>
        /// <param name="start">An offset below the payload length for the payload, otherwise into the extra buffer.</param>
        /// <param name="length">The length.</param>
        /// <returns>The UTF-8 bytes, which are valid until the document is disposed.</returns>
        internal ReadOnlyMemory<byte> SliceMemory(int start, int length)
        {
            return start < _sourceLength
                ? _source.AsMemory(start, length)
                : _extra.AsMemory(start - _sourceLength, length);
        }

        /// <summary>
        /// Throws when this instance has been disposed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
        }

        /// <summary>
        /// Throws when <paramref name="length"/> exceeds a non-zero document cap.
        /// </summary>
        /// <param name="length">The payload length.</param>
        /// <param name="maxDocumentByteCount">The configured cap.</param>
        internal static void ThrowIfDocumentTooLarge(int length, int maxDocumentByteCount)
        {
            if (maxDocumentByteCount > 0 && length > maxDocumentByteCount)
            {
                throw XmlException.Create($"{nameof(XmlDocumentOptions.MaxDocumentByteCount)} exceeded ({maxDocumentByteCount}).");
            }
        }

        /// <summary>
        /// Compares UTF-8 <paramref name="utf8"/> to UTF-16 <paramref name="text"/> without allocating.
        /// </summary>
        /// <param name="utf8">Well-formed UTF-8 from the document.</param>
        /// <param name="text">The caller's text.</param>
        /// <returns><see langword="true"/> if both encode the same string.</returns>
        /// <remarks>
        /// A non-ASCII character always takes more UTF-8 bytes than UTF-16 code units. Equal lengths can
        /// therefore only match as pure ASCII, and a shorter UTF-8 form can never match, so the name
        /// lookups on <see cref="XmlElement"/> rarely transcode.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool Utf8Equals(ReadOnlySpan<byte> utf8, ReadOnlySpan<char> text)
        {
            if (utf8.Length == text.Length)
            {
                return Ascii.Equals(utf8, text);
            }

            return utf8.Length > text.Length && Utf8EqualsTranscoded(utf8, text);
        }

        /// <summary>
        /// Compares UTF-8 <paramref name="utf8"/> to UTF-16 <paramref name="text"/> by transcoding the text.
        /// </summary>
        /// <param name="utf8">Well-formed UTF-8 from the document.</param>
        /// <param name="text">The caller's text.</param>
        /// <returns><see langword="true"/> if both encode the same string.</returns>
        /// <remarks>
        /// Ill-formed UTF-16 never matches; it is not replaced with U+FFFD.
        /// </remarks>
        internal static bool Utf8EqualsTranscoded(ReadOnlySpan<byte> utf8, ReadOnlySpan<char> text)
        {
            byte[]? rented = null;
            Span<byte> scratch = utf8.Length <= 256
                ? stackalloc byte[256]
                : (rented = ArrayPool<byte>.Shared.Rent(utf8.Length));
            var status = Utf8.FromUtf16(text, scratch[..utf8.Length], out _, out var written, replaceInvalidSequences: false);
            var equal = status is OperationStatus.Done && scratch[..written].SequenceEqual(utf8);
            ReturnScratch(rented);
            return equal;
        }

        /// <summary>
        /// Returns the value of an attribute or leaf <paramref name="row"/>.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <returns>The UTF-8 value.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlySpan<byte> Value(in XmlDbRow row)
        {
            return Slice(row.ValueStart, row.ValueLength);
        }

        /// <summary>
        /// Writes element row <paramref name="index"/>, its attributes, and its children.
        /// </summary>
        /// <param name="index">The element row index.</param>
        /// <param name="writer">The destination.</param>
        /// <remarks>
        /// Namespace declarations are skipped; the writer re-declares each namespace from the names it is given.
        /// </remarks>
        internal void WriteElement(int index, Utf8XmlWriter writer)
        {
            ref var row = ref _rows[index];
            var end = row.EndIndex;
            var attributeEnd = index + 1 + row.AttributeCount;
            if (row.AttributeCount == 0 && end - attributeEnd == 1 && _rows[attributeEnd].Kind is XmlValueKind.Text)
            {
                writer.WriteCompleteElementUtf8(LocalName(in row), Prefix(in row), NamespaceUri(in row), Value(in _rows[attributeEnd]));
                return;
            }

            var elementPrefix = Prefix(in row);
            var elementNs = NamespaceUri(in row);
            writer.WriteStartElementDeferred(LocalName(in row), elementPrefix);
            var declared = false;
            for (var a = index + 1; a < attributeEnd; a++)
            {
                ref var attribute = ref _rows[a];
                if (!IsNamespaceDeclaration(in attribute, out var declaredPrefix))
                {
                    continue;
                }

                if (declaredPrefix.SequenceEqual(elementPrefix))
                {
                    declared = true;
                }

                writer.RegisterNamespace(declaredPrefix, Value(in attribute));
            }

            if (!declared)
            {
                writer.EnsureElementNamespace(elementPrefix, elementNs);
            }

            WriteAttributesAndChildren(index, writer, true);
            if (row.IsEmpty)
            {
                writer.WriteEndElement();
            }
            else
            {
                writer.WriteFullEndElement();
            }
        }

        /// <summary>
        /// Writes the attributes and the children of element row <paramref name="index"/> into the element
        /// that <paramref name="writer"/> has open, without the element's own tags.
        /// </summary>
        /// <param name="index">The row index.</param>
        /// <param name="writer">The destination, with a start tag open.</param>
        /// <remarks>
        /// <para>
        /// This is how an element is written where something else names it: the serializer writes the element
        /// of a member, a list item or a dictionary value declared as <see cref="object"/>, and the value fills it.
        /// </para>
        /// <para>
        /// A declaration of a prefix is written, because the content may use the prefix. A declaration of the
        /// default namespace is not: it would move the open element, whose name is written already, into that
        /// namespace. A child in that namespace declares it for itself.
        /// </para>
        /// <para>
        /// A row that is not an element is written as <see cref="WriteRow"/> writes it.
        /// </para>
        /// </remarks>
        internal void WriteContent(int index, Utf8XmlWriter writer)
        {
            ref var row = ref _rows[index];
            if (row.Kind is not (XmlValueKind.Element or XmlValueKind.EmptyElement or XmlValueKind.Nil))
            {
                WriteRow(index, writer);
                return;
            }

            var attributeEnd = index + 1 + row.AttributeCount;
            for (var a = index + 1; a < attributeEnd; a++)
            {
                ref var attribute = ref _rows[a];
                if (IsNamespaceDeclaration(in attribute, out var declaredPrefix) && !declaredPrefix.IsEmpty)
                {
                    writer.RegisterNamespace(declaredPrefix, Value(in attribute));
                }
            }

            WriteAttributesAndChildren(index, writer, false);
            if (!row.IsEmpty && row.EndIndex == attributeEnd)
            {
                writer.WriteEmptyText();
            }
        }

        /// <summary>
        /// Writes the attributes and the children of element row <paramref name="index"/>.
        /// </summary>
        /// <param name="index">The element row index.</param>
        /// <param name="writer">The destination, with a start tag open and the namespace declarations registered.</param>
        /// <param name="defaultNamespace">Whether a declaration of the default namespace is written.</param>
        internal void WriteAttributesAndChildren(int index, Utf8XmlWriter writer, bool defaultNamespace)
        {
            ref var row = ref _rows[index];
            var end = row.EndIndex;
            var attributeEnd = index + 1 + row.AttributeCount;
            for (var a = index + 1; a < attributeEnd; a++)
            {
                ref var attribute = ref _rows[a];
                if (IsNamespaceDeclaration(in attribute, out var declaredPrefix))
                {
                    if (defaultNamespace || !declaredPrefix.IsEmpty)
                    {
                        writer.WriteRegisteredNamespace(declaredPrefix, Value(in attribute));
                    }

                    continue;
                }

                writer.WriteStartAttributeUtf8(LocalName(in attribute), Prefix(in attribute), NamespaceUri(in attribute));
                writer.WriteStringValue(Value(in attribute));
                writer.WriteEndAttribute();
            }

            for (var c = attributeEnd; c < end; c = NextSiblingIndex(c))
            {
                WriteRow(c, writer);
            }
        }

        /// <summary>
        /// Returns whether an attribute row is an <c>xmlns</c> or <c>xmlns:prefix</c> declaration.
        /// </summary>
        /// <param name="attribute">The attribute row.</param>
        /// <param name="declaredPrefix">When this method returns true, the declared prefix, or empty for the default namespace.</param>
        /// <returns><see langword="true"/> for a namespace declaration. An attribute such as <c>p:xmlns</c> is not one.</returns>
        internal bool IsNamespaceDeclaration(in XmlDbRow attribute, out ReadOnlySpan<byte> declaredPrefix)
        {
            if (attribute.PrefixLength == 0)
            {
                declaredPrefix = [];
                return attribute.NameLength == 5 && Slice(attribute.NameStart, 5).SequenceEqual(XmlUtf8.XmlnsPrefix);
            }

            declaredPrefix = LocalName(in attribute);
            return attribute.PrefixLength == 5 && Slice(attribute.NameStart, 5).SequenceEqual(XmlUtf8.XmlnsPrefix);
        }

        /// <summary>
        /// Writes row <paramref name="index"/> to <paramref name="writer"/>.
        /// </summary>
        /// <param name="index">The row index.</param>
        /// <param name="writer">The destination.</param>
        internal void WriteRow(int index, Utf8XmlWriter writer)
        {
            ref var row = ref _rows[index];
            switch (row.Kind)
            {
                case XmlValueKind.Element:
                case XmlValueKind.EmptyElement:
                case XmlValueKind.Nil:
                    WriteElement(index, writer);
                    break;
                case XmlValueKind.Text:
                    writer.WriteStringValue(Value(in row));
                    break;
                case XmlValueKind.CData:
                    writer.WriteCData(Value(in row));
                    break;
                case XmlValueKind.Comment:
                    writer.WriteComment(Value(in row));
                    break;
                case XmlValueKind.ProcessingInstruction:
                    writer.WriteProcessingInstructionUtf8(LocalName(in row), Value(in row));
                    break;
            }
        }

        #endregion

    }

}
