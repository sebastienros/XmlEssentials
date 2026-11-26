namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// The tokens of an XPath 1.0 expression.
    /// </summary>
    /// <remarks>
    /// <c>*</c>, <c>and</c>, <c>or</c>, <c>div</c> and <c>mod</c> are not told apart from a name test here. The parser
    /// knows whether it is after an operand, which is what decides it (Recommendation, section 3.7).
    /// </remarks>
    internal enum XPathTokenKind : byte
    {

        /// <summary>The end of the expression.</summary>
        End,

        /// <summary><c>@</c>.</summary>
        At,

        /// <summary><c>]</c>.</summary>
        CloseBracket,

        /// <summary><c>)</c>.</summary>
        CloseParenthesis,

        /// <summary><c>::</c>.</summary>
        ColonColon,

        /// <summary><c>,</c>.</summary>
        Comma,

        /// <summary><c>$</c>.</summary>
        Dollar,

        /// <summary><c>.</c>.</summary>
        Dot,

        /// <summary><c>..</c>.</summary>
        DotDot,

        /// <summary><c>//</c>.</summary>
        DoubleSlash,

        /// <summary><c>=</c>.</summary>
        Equal,

        /// <summary><c>&gt;</c>.</summary>
        Greater,

        /// <summary><c>&gt;=</c>.</summary>
        GreaterOrEqual,

        /// <summary><c>&lt;</c>.</summary>
        Less,

        /// <summary><c>&lt;=</c>.</summary>
        LessOrEqual,

        /// <summary>A string literal, with its quotes.</summary>
        Literal,

        /// <summary><c>-</c>.</summary>
        Minus,

        /// <summary>An NCName or a QName.</summary>
        Name,

        /// <summary><c>NCName:*</c>.</summary>
        NamespaceWildcard,

        /// <summary><c>!=</c>.</summary>
        NotEqual,

        /// <summary>A number.</summary>
        Number,

        /// <summary><c>[</c>.</summary>
        OpenBracket,

        /// <summary><c>(</c>.</summary>
        OpenParenthesis,

        /// <summary><c>|</c>.</summary>
        Pipe,

        /// <summary><c>+</c>.</summary>
        Plus,

        /// <summary><c>/</c>.</summary>
        Slash,

        /// <summary><c>*</c>.</summary>
        Star,

    }

}
