using System;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// Splits an XPath 1.0 expression into tokens.
    /// </summary>
    /// <remarks>
    /// White space (<c>#x20</c>, <c>#x9</c>, <c>#xD</c>, <c>#xA</c>) separates tokens, and may not split a name, a
    /// number or <c>::</c>. Names follow the Namespaces in XML name production. Every character of the expression
    /// must be an XML 1.0 character.
    /// </remarks>
    internal struct XPathLexer
    {

        #region Fields

        private int _position;
        private readonly string _source;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a lexer at the start of an expression.
        /// </summary>
        /// <param name="source">The expression.</param>
        internal XPathLexer(string source)
        {
            _source = source;
            _position = 0;
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Returns the exception for an error at a place in the expression.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="position">The offset.</param>
        /// <returns>The exception.</returns>
        internal static XPathException Error(string message, int position)
        {
            return new XPathException(message, position);
        }

        /// <summary>
        /// Reads the next token.
        /// </summary>
        /// <returns>The token, or <see cref="XPathTokenKind.End"/> at the end of the expression.</returns>
        /// <exception cref="XPathException">The expression holds a character that starts no token.</exception>
        internal XPathToken Next()
        {
            while (_position < _source.Length && _source[_position] is ' ' or '\t' or '\r' or '\n')
            {
                _position++;
            }

            var start = _position;
            if (start == _source.Length)
            {
                return new XPathToken(XPathTokenKind.End, start, 0, -1);
            }

            var c = _source[start];
            if (c is '"' or '\'')
            {
                return Literal(start, c);
            }

            if (IsDigit(c) || (c is '.' && start + 1 < _source.Length && IsDigit(_source[start + 1])))
            {
                return Number(start);
            }

            if (c is not ':' && XmlChars.IsNameStartChar(CodePointAt(start, out _), XmlVersion.Version10))
            {
                return Name(start);
            }

            _position++;
            var kind = c switch
            {
                '/' => Pair('/', XPathTokenKind.DoubleSlash, XPathTokenKind.Slash),
                '.' => Pair('.', XPathTokenKind.DotDot, XPathTokenKind.Dot),
                '<' => Pair('=', XPathTokenKind.LessOrEqual, XPathTokenKind.Less),
                '>' => Pair('=', XPathTokenKind.GreaterOrEqual, XPathTokenKind.Greater),
                ':' => Pair(':', XPathTokenKind.ColonColon, XPathTokenKind.End),
                '!' => Pair('=', XPathTokenKind.NotEqual, XPathTokenKind.End),
                '@' => XPathTokenKind.At,
                '$' => XPathTokenKind.Dollar,
                '(' => XPathTokenKind.OpenParenthesis,
                ')' => XPathTokenKind.CloseParenthesis,
                '[' => XPathTokenKind.OpenBracket,
                ']' => XPathTokenKind.CloseBracket,
                ',' => XPathTokenKind.Comma,
                '*' => XPathTokenKind.Star,
                '+' => XPathTokenKind.Plus,
                '-' => XPathTokenKind.Minus,
                '=' => XPathTokenKind.Equal,
                '|' => XPathTokenKind.Pipe,
                _ => XPathTokenKind.End,
            };
            if (kind is XPathTokenKind.End)
            {
                throw Unexpected(start);
            }

            return new XPathToken(kind, start, _position - start, -1);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Returns the code point at an offset.
        /// </summary>
        /// <param name="position">The offset.</param>
        /// <param name="width">When this method returns, the number of UTF-16 code units the code point takes.</param>
        /// <returns>The code point, or <c>-1</c> for a surrogate that is not part of a pair.</returns>
        private readonly int CodePointAt(int position, out int width)
        {
            return Rune.DecodeFromUtf16(_source.AsSpan(position), out var rune, out width) is OperationStatus.Done ? rune.Value : -1;
        }

        /// <summary>
        /// Returns whether a character is an ASCII digit.
        /// </summary>
        /// <param name="c">The character.</param>
        /// <returns><see langword="true"/> for <c>0</c> to <c>9</c>.</returns>
        private static bool IsDigit(char c)
        {
            return c is >= '0' and <= '9';
        }

        /// <summary>
        /// Reads a string literal.
        /// </summary>
        /// <param name="start">The offset of the opening quote.</param>
        /// <param name="quote">The quote character.</param>
        /// <returns>The token, quotes included.</returns>
        private XPathToken Literal(int start, char quote)
        {
            var close = _source.IndexOf(quote, start + 1);
            if (close < 0)
            {
                throw Error("The string literal is not closed.", start);
            }

            for (var i = start + 1; i < close;)
            {
                var cp = CodePointAt(i, out var width);
                if (!XmlChars.IsXml10Char(cp))
                {
                    throw NotACharacter(i, cp);
                }

                i += width;
            }

            _position = close + 1;
            return new XPathToken(XPathTokenKind.Literal, start, _position - start, -1);
        }

        /// <summary>
        /// Reads an NCName, a QName, or <c>NCName:*</c>.
        /// </summary>
        /// <param name="start">The offset of the first character, which is a name start character.</param>
        /// <returns>The token.</returns>
        private XPathToken Name(int start)
        {
            ReadNcName();
            if (_position + 1 >= _source.Length || _source[_position] is not ':' || _source[_position + 1] is ':')
            {
                return new XPathToken(XPathTokenKind.Name, start, _position - start, -1);
            }

            var colon = _position;
            _position++;
            if (_source[_position] is '*')
            {
                _position++;
                return new XPathToken(XPathTokenKind.NamespaceWildcard, start, _position - start, colon);
            }

            // The character after the colon is not a colon: a double colon ends the name before it.
            if (!XmlChars.IsNameStartChar(CodePointAt(_position, out _), XmlVersion.Version10))
            {
                throw Error("A prefix must be followed by a local name or '*'.", _position);
            }

            ReadNcName();
            return new XPathToken(XPathTokenKind.Name, start, _position - start, colon);
        }

        /// <summary>
        /// Returns the exception for a character that is not an XML character.
        /// </summary>
        /// <param name="position">The offset of the character.</param>
        /// <param name="cp">The code point, or <c>-1</c> for an unpaired surrogate.</param>
        /// <returns>The exception.</returns>
        private readonly XPathException NotACharacter(int position, int cp)
        {
            var value = cp < 0 ? _source[position] : cp;
            return Error(string.Create(CultureInfo.InvariantCulture, $"The expression contains U+{value:X4}, which is not an XML character."), position);
        }

        /// <summary>
        /// Reads a number: <c>Digits ('.' Digits?)? | '.' Digits</c>.
        /// </summary>
        /// <param name="start">The offset of the first character.</param>
        /// <returns>The token.</returns>
        private XPathToken Number(int start)
        {
            while (_position < _source.Length && IsDigit(_source[_position]))
            {
                _position++;
            }

            if (_position < _source.Length && _source[_position] is '.')
            {
                _position++;
                while (_position < _source.Length && IsDigit(_source[_position]))
                {
                    _position++;
                }
            }

            return new XPathToken(XPathTokenKind.Number, start, _position - start, -1);
        }

        /// <summary>
        /// Reads the second character of a two-character token.
        /// </summary>
        /// <param name="second">The second character.</param>
        /// <param name="pair">The kind when the second character follows.</param>
        /// <param name="single">The kind when it does not, or <see cref="XPathTokenKind.End"/> when the first character alone is no token.</param>
        /// <returns>The kind.</returns>
        private XPathTokenKind Pair(char second, XPathTokenKind pair, XPathTokenKind single)
        {
            if (_position < _source.Length && _source[_position] == second)
            {
                _position++;
                return pair;
            }

            return single;
        }

        /// <summary>
        /// Reads the characters of an NCName after its first.
        /// </summary>
        private void ReadNcName()
        {
            CodePointAt(_position, out var width);
            _position += width;
            while (_position < _source.Length && _source[_position] is not ':' && XmlChars.IsNameChar(CodePointAt(_position, out width), XmlVersion.Version10))
            {
                _position += width;
            }
        }

        /// <summary>
        /// Returns the exception for a character that starts no token.
        /// </summary>
        /// <param name="position">The offset of the character.</param>
        /// <returns>The exception.</returns>
        private readonly XPathException Unexpected(int position)
        {
            var cp = CodePointAt(position, out var width);
            if (!XmlChars.IsXml10Char(cp))
            {
                return NotACharacter(position, cp);
            }

            return Error($"Unexpected character '{_source.AsSpan(position, width)}'.", position);
        }

        #endregion

    }

}
