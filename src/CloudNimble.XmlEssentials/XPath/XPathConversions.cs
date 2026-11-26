using System;
using System.Globalization;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// The conversions between strings and numbers of XPath 1.0 (Recommendation, section 4.2 and 4.4), on UTF-8.
    /// </summary>
    /// <remarks>
    /// Ported from Xemele's <c>XPathConversions</c>. Neither conversion depends on the culture.
    /// </remarks>
    internal static class XPathConversions
    {

        #region Fields

        private static readonly byte[] FalseText = "false"u8.ToArray();
        private static readonly byte[] InfinityText = "Infinity"u8.ToArray();
        private static readonly byte[] NaNText = "NaN"u8.ToArray();
        private static readonly byte[] NegativeInfinityText = "-Infinity"u8.ToArray();

        /// <summary>
        /// The powers of ten up to 10^15, each exact in a double.
        /// </summary>
        private static readonly double[] PowersOfTen = [1e0, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7, 1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15];
        private static readonly byte[] TrueText = "true"u8.ToArray();
        private static readonly byte[] ZeroText = "0"u8.ToArray();

        #endregion

        #region Internal Methods

        /// <summary>
        /// Returns <c>string()</c> of a boolean.
        /// </summary>
        /// <param name="value">The boolean.</param>
        /// <returns><c>true</c> or <c>false</c>.</returns>
        internal static byte[] FormatBoolean(bool value)
        {
            return value ? TrueText : FalseText;
        }

        /// <summary>
        /// Returns <c>string()</c> of a number.
        /// </summary>
        /// <param name="value">The number.</param>
        /// <returns>
        /// <c>NaN</c>, <c>Infinity</c>, <c>-Infinity</c>, <c>0</c> for both zeros, an integer with no decimal point,
        /// or a decimal with as many digits after the point as it takes to tell the number from every other double.
        /// No exponent is written.
        /// </returns>
        internal static byte[] FormatNumber(double value)
        {
            if (double.IsNaN(value))
            {
                return NaNText;
            }

            if (double.IsInfinity(value))
            {
                return value > 0 ? InfinityText : NegativeInfinityText;
            }

            if (value == 0)
            {
                return ZeroText;
            }

            // "R" gives the shortest digits that read back as the same double. Only its exponent is expanded.
            Span<byte> shortest = stackalloc byte[32];
            value.TryFormat(shortest, out var written, "R", CultureInfo.InvariantCulture);
            shortest = shortest[..written];
            var exponentAt = shortest.IndexOf((byte)'E');
            if (exponentAt < 0)
            {
                return shortest.ToArray();
            }

            var negative = shortest[0] == (byte)'-';
            var mantissa = shortest[(negative ? 1 : 0)..exponentAt];
            Span<byte> digits = stackalloc byte[17];
            var count = 0;
            var point = mantissa.Length;
            for (var i = 0; i < mantissa.Length; i++)
            {
                if (mantissa[i] == (byte)'.')
                {
                    point = i;
                    continue;
                }

                digits[count++] = mantissa[i];
            }

            var decimalAt = point + int.Parse(shortest[(exponentAt + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            Span<byte> text = stackalloc byte[350];
            var length = 0;
            if (negative)
            {
                text[length++] = (byte)'-';
            }

            // "R" writes an exponent only below 1e-5 and at 1e17 and above, so the point of the expanded number is
            // either before the digits or after them.
            if (decimalAt <= 0)
            {
                text[length++] = (byte)'0';
                text[length++] = (byte)'.';
                text.Slice(length, -decimalAt).Fill((byte)'0');
                length -= decimalAt;
                digits[..count].CopyTo(text[length..]);
                length += count;
            }
            else
            {
                digits[..count].CopyTo(text[length..]);
                length += count;
                text.Slice(length, decimalAt - count).Fill((byte)'0');
                length += decimalAt - count;
            }

            return text[..length].ToArray();
        }

        /// <summary>
        /// Returns whether a byte is XML white space: space, tab, carriage return or line feed.
        /// </summary>
        /// <param name="value">The byte.</param>
        /// <returns><see langword="true"/> for the four white space characters.</returns>
        internal static bool IsSpace(byte value)
        {
            return value is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n';
        }

        /// <summary>
        /// Returns <c>number()</c> of a string.
        /// </summary>
        /// <param name="utf8">The UTF-8 of the string.</param>
        /// <returns>
        /// The number, or NaN unless the string is optional white space, an optional <c>-</c>, a Number
        /// (<c>Digits ('.' Digits?)? | '.' Digits</c>) and optional white space. There is no <c>+</c>, no exponent and
        /// no <c>Infinity</c>.
        /// </returns>
        internal static double ParseNumber(ReadOnlySpan<byte> utf8)
        {
            var start = 0;
            var end = utf8.Length;
            while (start < end && IsSpace(utf8[start]))
            {
                start++;
            }

            while (end > start && IsSpace(utf8[end - 1]))
            {
                end--;
            }

            var text = utf8[start..end];
            var negative = text.Length > 0 && text[0] == (byte)'-';
            var i = negative ? 1 : 0;
            var digits = 0;
            var fraction = 0;
            var mantissa = 0UL;
            while (i < text.Length && char.IsAsciiDigit((char)text[i]))
            {
                mantissa = (mantissa * 10) + (uint)(text[i] - '0');
                i++;
                digits++;
            }

            if (i < text.Length && text[i] == (byte)'.')
            {
                i++;
                while (i < text.Length && char.IsAsciiDigit((char)text[i]))
                {
                    mantissa = (mantissa * 10) + (uint)(text[i] - '0');
                    i++;
                    digits++;
                    fraction++;
                }
            }

            if (digits == 0 || i != text.Length)
            {
                return double.NaN;
            }

            if (digits <= 15)
            {
                // Fifteen digits are exact in a double, and so is every power of ten up to 10^22, so one division,
                // which IEEE 754 rounds correctly, gives the double nearest the decimal (Clinger's fast path).
                var value = mantissa / PowersOfTen[fraction];
                return negative ? -value : value;
            }

            return double.Parse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Returns <c>round()</c> of a number: the integer closest to it, and the one towards positive infinity when two
        /// are as close.
        /// </summary>
        /// <param name="value">The number.</param>
        /// <returns>The rounded number. NaN, the infinities and the zeros are returned as they are, and a number from -0.5 up to 0 gives negative zero.</returns>
        /// <remarks>
        /// Adding 0.5 and taking the floor loses a bit for the double just below 0.5 and for large integers, so the
        /// distance to the floor is compared instead (ported from Xemele).
        /// </remarks>
        internal static double Round(double value)
        {
            if (value == 0 || !double.IsFinite(value))
            {
                return value;
            }

            if (value is >= -0.5 and < 0)
            {
                return -0.0;
            }

            var floor = Math.Floor(value);
            return value - floor >= 0.5 ? floor + 1 : floor;
        }

        #endregion

    }

}
