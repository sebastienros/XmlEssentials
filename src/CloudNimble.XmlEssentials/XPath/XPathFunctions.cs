using System;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// The names and the arities of the core functions.
    /// </summary>
    internal static class XPathFunctions
    {

        #region Fields

        /// <summary>
        /// How many arguments each function takes, as the message for a wrong count says it.
        /// </summary>
        private static readonly string[] Arities =
        [
            "no arguments", "no arguments", "1 argument", "1 argument", "at most 1 argument", "at most 1 argument",
            "at most 1 argument", "at most 1 argument", "at least 2 arguments", "2 arguments", "2 arguments",
            "2 arguments", "2 arguments", "2 or 3 arguments", "at most 1 argument", "at most 1 argument",
            "3 arguments", "1 argument", "1 argument", "no arguments", "no arguments", "1 argument",
            "at most 1 argument", "1 argument", "1 argument", "1 argument", "1 argument",
        ];

        /// <summary>
        /// The name of each function.
        /// </summary>
        private static readonly string[] Names =
        [
            "last", "position", "count", "id", "local-name", "namespace-uri", "name", "string", "concat", "starts-with",
            "contains", "substring-before", "substring-after", "substring", "string-length", "normalize-space",
            "translate", "boolean", "not", "true", "false", "lang", "number", "sum", "floor", "ceiling", "round",
        ];

        /// <summary>
        /// The most arguments each function takes.
        /// </summary>
        private static readonly int[] Maximums = [0, 0, 1, 1, 1, 1, 1, 1, int.MaxValue, 2, 2, 2, 2, 3, 1, 1, 3, 1, 1, 0, 0, 1, 1, 1, 1, 1, 1];

        /// <summary>
        /// The fewest arguments each function takes.
        /// </summary>
        private static readonly int[] Minimums = [0, 0, 1, 1, 0, 0, 0, 0, 2, 2, 2, 2, 2, 2, 0, 0, 3, 1, 1, 0, 0, 1, 0, 1, 1, 1, 1];

        #endregion

        #region Internal Methods

        /// <summary>
        /// Returns whether a function takes a number of arguments.
        /// </summary>
        /// <param name="function">The function.</param>
        /// <param name="count">The number of arguments in the call.</param>
        /// <returns><see langword="true"/> when the count is within the arity of the function.</returns>
        internal static bool Accepts(XPathFunction function, int count)
        {
            return count >= Minimums[(int)function] && count <= Maximums[(int)function];
        }

        /// <summary>
        /// Returns how many arguments a function takes, in words.
        /// </summary>
        /// <param name="function">The function.</param>
        /// <returns>The arity, such as <c>2 or 3 arguments</c>.</returns>
        internal static string Arity(XPathFunction function)
        {
            return Arities[(int)function];
        }

        /// <summary>
        /// Returns the name of a function.
        /// </summary>
        /// <param name="function">The function.</param>
        /// <returns>The name, such as <c>local-name</c>.</returns>
        internal static string Name(XPathFunction function)
        {
            return Names[(int)function];
        }

        /// <summary>
        /// Finds a core function by its name.
        /// </summary>
        /// <param name="name">The name, which has no prefix.</param>
        /// <param name="function">When this method returns <see langword="true"/>, the function.</param>
        /// <returns><see langword="true"/> when the name is that of a core function.</returns>
        internal static bool TryFind(ReadOnlySpan<char> name, out XPathFunction function)
        {
            XPathFunction? found = name switch
            {
                "last" => XPathFunction.Last,
                "position" => XPathFunction.Position,
                "count" => XPathFunction.Count,
                "id" => XPathFunction.Id,
                "local-name" => XPathFunction.LocalName,
                "namespace-uri" => XPathFunction.NamespaceUri,
                "name" => XPathFunction.Name,
                "string" => XPathFunction.String,
                "concat" => XPathFunction.Concat,
                "starts-with" => XPathFunction.StartsWith,
                "contains" => XPathFunction.Contains,
                "substring-before" => XPathFunction.SubstringBefore,
                "substring-after" => XPathFunction.SubstringAfter,
                "substring" => XPathFunction.Substring,
                "string-length" => XPathFunction.StringLength,
                "normalize-space" => XPathFunction.NormalizeSpace,
                "translate" => XPathFunction.Translate,
                "boolean" => XPathFunction.Boolean,
                "not" => XPathFunction.Not,
                "true" => XPathFunction.True,
                "false" => XPathFunction.False,
                "lang" => XPathFunction.Lang,
                "number" => XPathFunction.Number,
                "sum" => XPathFunction.Sum,
                "floor" => XPathFunction.Floor,
                "ceiling" => XPathFunction.Ceiling,
                "round" => XPathFunction.Round,
                _ => null,
            };
            function = found.GetValueOrDefault();
            return found.HasValue;
        }

        #endregion

    }

}
