namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// The equality and relational operators of XPath 1.0.
    /// </summary>
    internal enum XPathComparisonOperator : byte
    {

        /// <summary><c>=</c>.</summary>
        Equal,

        /// <summary><c>!=</c>.</summary>
        NotEqual,

        /// <summary><c>&lt;</c>.</summary>
        Less,

        /// <summary><c>&lt;=</c>.</summary>
        LessOrEqual,

        /// <summary><c>&gt;</c>.</summary>
        Greater,

        /// <summary><c>&gt;=</c>.</summary>
        GreaterOrEqual,

    }

}
