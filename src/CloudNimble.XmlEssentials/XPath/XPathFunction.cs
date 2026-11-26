namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// The 27 core functions of XPath 1.0, in the order of the Recommendation, section 4.
    /// </summary>
    internal enum XPathFunction : byte
    {

        /// <summary><c>last()</c>.</summary>
        Last,

        /// <summary><c>position()</c>.</summary>
        Position,

        /// <summary><c>count(node-set)</c>.</summary>
        Count,

        /// <summary><c>id(object)</c>.</summary>
        Id,

        /// <summary><c>local-name(node-set?)</c>.</summary>
        LocalName,

        /// <summary><c>namespace-uri(node-set?)</c>.</summary>
        NamespaceUri,

        /// <summary><c>name(node-set?)</c>.</summary>
        Name,

        /// <summary><c>string(object?)</c>.</summary>
        String,

        /// <summary><c>concat(string, string, string*)</c>.</summary>
        Concat,

        /// <summary><c>starts-with(string, string)</c>.</summary>
        StartsWith,

        /// <summary><c>contains(string, string)</c>.</summary>
        Contains,

        /// <summary><c>substring-before(string, string)</c>.</summary>
        SubstringBefore,

        /// <summary><c>substring-after(string, string)</c>.</summary>
        SubstringAfter,

        /// <summary><c>substring(string, number, number?)</c>.</summary>
        Substring,

        /// <summary><c>string-length(string?)</c>.</summary>
        StringLength,

        /// <summary><c>normalize-space(string?)</c>.</summary>
        NormalizeSpace,

        /// <summary><c>translate(string, string, string)</c>.</summary>
        Translate,

        /// <summary><c>boolean(object)</c>.</summary>
        Boolean,

        /// <summary><c>not(boolean)</c>.</summary>
        Not,

        /// <summary><c>true()</c>.</summary>
        True,

        /// <summary><c>false()</c>.</summary>
        False,

        /// <summary><c>lang(string)</c>.</summary>
        Lang,

        /// <summary><c>number(object?)</c>.</summary>
        Number,

        /// <summary><c>sum(node-set)</c>.</summary>
        Sum,

        /// <summary><c>floor(number)</c>.</summary>
        Floor,

        /// <summary><c>ceiling(number)</c>.</summary>
        Ceiling,

        /// <summary><c>round(number)</c>.</summary>
        Round,

    }

}
