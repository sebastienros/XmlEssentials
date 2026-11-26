using System;
using System.Text;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// The string functions of XPath 1.0 that count characters, on UTF-8.
    /// </summary>
    /// <remarks>
    /// A character is a Unicode code point, as the Recommendation says (section 4.2), which in UTF-8 is a byte that is
    /// not a continuation byte. A result that is a part of its input is a slice of it, so it is not copied.
    /// </remarks>
    internal static class XPathStrings
    {

        #region Internal Methods

        /// <summary>
        /// Returns <c>string-length()</c>: the number of characters in a string.
        /// </summary>
        /// <param name="utf8">Well-formed UTF-8.</param>
        /// <returns>The number of code points.</returns>
        internal static int Length(ReadOnlySpan<byte> utf8)
        {
            var count = 0;
            foreach (var value in utf8)
            {
                if (IsFirstByte(value))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Returns <c>normalize-space()</c>: a string without white space at either end, and with every run of white
        /// space in it made one space.
        /// </summary>
        /// <param name="utf8">The string.</param>
        /// <returns>A slice of the string when it has nothing to collapse inside; a new array otherwise.</returns>
        internal static ReadOnlyMemory<byte> NormalizeSpace(ReadOnlyMemory<byte> utf8)
        {
            var span = utf8.Span;
            var start = 0;
            var end = span.Length;
            while (start < end && XPathConversions.IsSpace(span[start]))
            {
                start++;
            }

            while (end > start && XPathConversions.IsSpace(span[end - 1]))
            {
                end--;
            }

            var length = 0;
            var collapse = false;
            for (var i = start; i < end; i++)
            {
                if (!XPathConversions.IsSpace(span[i]))
                {
                    length++;
                    continue;
                }

                // A run of white space, which ends before the end of the trimmed string, is one space.
                var run = i;
                while (XPathConversions.IsSpace(span[i + 1]))
                {
                    i++;
                }

                collapse |= i > run || span[run] != (byte)' ';
                length++;
            }

            if (!collapse)
            {
                return utf8[start..end];
            }

            var text = new byte[length];
            var written = 0;
            for (var i = start; i < end; i++)
            {
                if (!XPathConversions.IsSpace(span[i]))
                {
                    text[written++] = span[i];
                    continue;
                }

                while (XPathConversions.IsSpace(span[i + 1]))
                {
                    i++;
                }

                text[written++] = (byte)' ';
            }

            return text;
        }

        /// <summary>
        /// Returns <c>substring()</c>: the characters of a string whose position <c>p</c>, from 1, is such that
        /// <c>p &gt;= round(start)</c> and <c>p &lt; round(start) + round(length)</c>.
        /// </summary>
        /// <param name="utf8">The string.</param>
        /// <param name="start">The second argument.</param>
        /// <param name="length">The third argument, or <see langword="null"/> when there is none, which takes the rest of the string.</param>
        /// <returns>A slice of the string, which is empty when the bounds are NaN or select nothing.</returns>
        internal static ReadOnlyMemory<byte> Substring(ReadOnlyMemory<byte> utf8, double start, double? length)
        {
            var first = XPathConversions.Round(start);
            var end = length is { } count ? first + XPathConversions.Round(count) : double.PositiveInfinity;
            var span = utf8.Span;
            var begin = -1;
            var position = 0;
            for (var i = 0; i < span.Length; i++)
            {
                if (!IsFirstByte(span[i]))
                {
                    continue;
                }

                position++;
                var inside = position >= first && position < end;
                if (inside && begin < 0)
                {
                    begin = i;
                }
                else if (!inside && begin >= 0)
                {
                    return utf8[begin..i];
                }
            }

            return begin < 0 ? ReadOnlyMemory<byte>.Empty : utf8[begin..];
        }

        /// <summary>
        /// Returns <c>translate()</c>: a string in which each character that occurs in <paramref name="from"/> is
        /// replaced by the character at the same position in <paramref name="to"/>, or removed when <paramref name="to"/>
        /// is shorter.
        /// </summary>
        /// <param name="utf8">The string.</param>
        /// <param name="from">The characters to replace. Only the first occurrence of a character counts.</param>
        /// <param name="to">The characters to replace them with.</param>
        /// <returns>The string itself when no character of it occurs in <paramref name="from"/>; a new array otherwise.</returns>
        internal static ReadOnlyMemory<byte> Translate(ReadOnlyMemory<byte> utf8, ReadOnlySpan<byte> from, ReadOnlySpan<byte> to)
        {
            var source = CodePoints(from);
            var target = CodePoints(to);
            var span = utf8.Span;
            var changed = false;
            var length = 0;
            for (var i = 0; i < span.Length;)
            {
                Rune.DecodeFromUtf8(span[i..], out var rune, out var width);
                var index = Array.IndexOf(source, rune.Value);
                changed |= index >= 0;
                length += index < 0 ? width : index < target.Length ? new Rune(target[index]).Utf8SequenceLength : 0;
                i += width;
            }

            if (!changed)
            {
                return utf8;
            }

            var text = new byte[length];
            var written = 0;
            for (var i = 0; i < span.Length;)
            {
                Rune.DecodeFromUtf8(span[i..], out var rune, out var width);
                var index = Array.IndexOf(source, rune.Value);
                if (index < 0)
                {
                    span.Slice(i, width).CopyTo(text.AsSpan(written));
                    written += width;
                }
                else if (index < target.Length)
                {
                    written += new Rune(target[index]).EncodeToUtf8(text.AsSpan(written));
                }

                i += width;
            }

            return text;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Returns the code points of a string.
        /// </summary>
        /// <param name="utf8">Well-formed UTF-8.</param>
        /// <returns>The code points, in order.</returns>
        private static int[] CodePoints(ReadOnlySpan<byte> utf8)
        {
            var points = new int[Length(utf8)];
            var count = 0;
            for (var i = 0; i < utf8.Length;)
            {
                Rune.DecodeFromUtf8(utf8[i..], out var rune, out var width);
                points[count++] = rune.Value;
                i += width;
            }

            return points;
        }

        /// <summary>
        /// Returns whether a byte starts a code point: whether it is not a continuation byte.
        /// </summary>
        /// <param name="value">The byte.</param>
        /// <returns><see langword="true"/> for a byte that is not <c>10xxxxxx</c>.</returns>
        private static bool IsFirstByte(byte value)
        {
            return (value & 0xC0) != 0x80;
        }

        #endregion

    }

}
