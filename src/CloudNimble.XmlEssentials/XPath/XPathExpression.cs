using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// A compiled XPath 1.0 expression, which can be evaluated over any number of <see cref="XmlDocument"/>
    /// instances.
    /// </summary>
    /// <example>
    /// <code>
    /// var expensive = XPathExpression.Compile("/catalog/item[price &gt; 10]/@id");
    /// using var document = XmlDocument.Parse(catalog);
    /// foreach (var id in expensive.Select(document))
    /// {
    ///     Console.WriteLine(id.GetString());
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// An expression is immutable, and may be evaluated from several threads at once, over one document or many. It
    /// holds its names and literals as UTF-8, and is evaluated over the UTF-8 rows of the document without making a
    /// string for a name or a value, except for a value that has to be put together from several rows.
    /// </remarks>
    public sealed class XPathExpression
    {

        #region Fields

        /// <summary>
        /// The default of <see cref="XPathOptions.MaxDepth"/>.
        /// </summary>
        internal const int DefaultMaxDepth = 64;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the expression as it was compiled.
        /// </summary>
        public string Expression { get; }

        /// <summary>
        /// Gets the compiled form of the expression.
        /// </summary>
        internal XPathAst Ast { get; }

        /// <summary>
        /// Gets the limit on the work of one evaluation, or 0 for none.
        /// </summary>
        internal long MaxOperations { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a compiled expression.
        /// </summary>
        /// <param name="expression">The expression.</param>
        /// <param name="ast">The compiled form.</param>
        /// <param name="maxOperations">The limit on the work of one evaluation, or 0.</param>
        private XPathExpression(string expression, XPathAst ast, long maxOperations)
        {
            Expression = expression;
            Ast = ast;
            MaxOperations = maxOperations;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Compiles an XPath 1.0 expression.
        /// </summary>
        /// <param name="expression">The expression.</param>
        /// <param name="options">The prefixes the expression may use and the limits on it, or <see langword="null"/> for none and the defaults.</param>
        /// <returns>The compiled expression.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="expression"/> is <see langword="null"/>, empty or white space.
        /// -or-
        /// <see cref="XPathOptions.Namespaces"/> binds <c>xml</c> to another namespace, binds <c>xmlns</c>, or binds a
        /// prefix to an empty namespace URI.
        /// </exception>
        /// <exception cref="XPathException">
        /// The expression is not an XPath 1.0 expression; it uses a prefix that <see cref="XPathOptions.Namespaces"/>
        /// does not bind, a function that is not a core function, or a function with the wrong number of arguments; or
        /// it is nested deeper than <see cref="XPathOptions.MaxDepth"/>. <see cref="XPathException.Position"/> says
        /// where.
        /// </exception>
        /// <example>
        /// <code>
        /// var options = new XPathOptions { Namespaces = new Dictionary&lt;string, string&gt; { ["a"] = "http://www.w3.org/2005/Atom" } };
        /// var titles = XPathExpression.Compile("//a:entry/a:title", options);
        /// </code>
        /// </example>
        /// <remarks>
        /// The whole expression is checked for syntax before its names are checked, so a syntax error anywhere is the
        /// error reported. A variable is not checked until the expression is evaluated.
        /// </remarks>
        public static XPathExpression Compile(string expression, XPathOptions? options = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(expression);
            var namespaces = CopyNamespaces(options?.Namespaces);
            var maxDepth = options is { MaxDepth: > 0 } ? options.MaxDepth : DefaultMaxDepth;
            var ast = XPathParser.Parse(expression, namespaces, maxDepth);
            return new XPathExpression(expression, ast, options?.MaxOperations ?? 0);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Copies the prefixes an expression may use, checks them, and adds <c>xml</c>.
        /// </summary>
        /// <param name="namespaces">The caller's prefixes, or <see langword="null"/>.</param>
        /// <returns>The copy.</returns>
        private static Dictionary<string, string> CopyNamespaces(IReadOnlyDictionary<string, string>? namespaces)
        {
            const string Xml = "http://www.w3.org/XML/1998/namespace";
            var copy = new Dictionary<string, string>(StringComparer.Ordinal) { ["xml"] = Xml };
            foreach (var (prefix, uri) in namespaces ?? ReadOnlyDictionary<string, string>.Empty)
            {
                switch (prefix)
                {
                    case "":
                        // An unprefixed name is in no namespace, so the default namespace has no use here.
                        continue;
                    case "xmlns":
                        throw new ArgumentException($"The prefix 'xmlns' cannot be bound in {nameof(XPathOptions)}.{nameof(XPathOptions.Namespaces)}.", "options");
                    case "xml" when uri != Xml:
                        throw new ArgumentException($"The prefix 'xml' can only be bound to {Xml} in {nameof(XPathOptions)}.{nameof(XPathOptions.Namespaces)}.", "options");
                }

                if (uri.Length == 0)
                {
                    throw new ArgumentException($"The prefix '{prefix}' is bound to an empty namespace URI in {nameof(XPathOptions)}.{nameof(XPathOptions.Namespaces)}.", "options");
                }

                copy[prefix] = uri;
            }

            return copy;
        }

        #endregion

    }

}
