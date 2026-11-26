using System;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// The 27 core functions of XPath 1.0 (Recommendation, section 4), on UTF-8.
    /// </summary>
    /// <remarks>
    /// Ported from Xemele's <c>XPathCoreFunctions</c>, rewritten to work on UTF-8 and to count code points. A function
    /// with an optional argument applies to the context node when it is left out.
    /// </remarks>
    internal static class XPathCoreFunctions
    {

        #region Internal Methods

        /// <summary>
        /// Calls a core function.
        /// </summary>
        /// <param name="evaluator">The evaluator.</param>
        /// <param name="call">The call, whose arity the parser checked.</param>
        /// <param name="focus">The context.</param>
        /// <returns>The value of the call.</returns>
        /// <exception cref="XPathException">An argument that must be a node-set is not one.</exception>
        internal static XPathValue Call(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            var arguments = call.Arguments;
            switch (call.Function)
            {
                case XPathFunction.Last:
                    return XPathValue.FromNumber(focus.Size);
                case XPathFunction.Position:
                    return XPathValue.FromNumber(focus.Position);
                case XPathFunction.Count:
                    return XPathValue.FromNumber(NodeSet(evaluator, call, focus).Count);
                case XPathFunction.Id:
                    // No attribute has type ID without a DTD, so no element is ever found. The argument is still evaluated.
                    evaluator.Evaluate(arguments[0], focus);
                    return XPathValue.FromNodes([], 0);
                case XPathFunction.LocalName:
                    return Name(evaluator, call, focus, static (navigator, id) => navigator.LocalName(id).ToArray());
                case XPathFunction.NamespaceUri:
                    return Name(evaluator, call, focus, static (navigator, id) => navigator.NamespaceUri(id).ToArray());
                case XPathFunction.Name:
                    return Name(evaluator, call, focus, QualifiedName);
                case XPathFunction.String:
                    return XPathValue.FromString(Text(evaluator, call, focus));
                case XPathFunction.Concat:
                    return XPathValue.FromString(Concat(evaluator, arguments, focus));
                case XPathFunction.StartsWith:
                    return XPathValue.FromBoolean(Argument(evaluator, call, 0, focus).Span.StartsWith(Argument(evaluator, call, 1, focus).Span));
                case XPathFunction.Contains:
                    return XPathValue.FromBoolean(Argument(evaluator, call, 0, focus).Span.IndexOf(Argument(evaluator, call, 1, focus).Span) >= 0);
                case XPathFunction.SubstringBefore:
                {
                    var text = Argument(evaluator, call, 0, focus);
                    var at = text.Span.IndexOf(Argument(evaluator, call, 1, focus).Span);
                    return XPathValue.FromString(at < 0 ? ReadOnlyMemory<byte>.Empty : text[..at]);
                }

                case XPathFunction.SubstringAfter:
                {
                    var text = Argument(evaluator, call, 0, focus);
                    var search = Argument(evaluator, call, 1, focus);
                    var at = text.Span.IndexOf(search.Span);
                    return XPathValue.FromString(at < 0 ? ReadOnlyMemory<byte>.Empty : text[(at + search.Length)..]);
                }

                case XPathFunction.Substring:
                {
                    var text = Argument(evaluator, call, 0, focus);
                    var start = evaluator.ToNumber(evaluator.Evaluate(arguments[1], focus));
                    double? length = arguments.Length == 3 ? evaluator.ToNumber(evaluator.Evaluate(arguments[2], focus)) : null;
                    return XPathValue.FromString(XPathStrings.Substring(text, start, length));
                }

                case XPathFunction.StringLength:
                    return XPathValue.FromNumber(XPathStrings.Length(Text(evaluator, call, focus).Span));
                case XPathFunction.NormalizeSpace:
                    return XPathValue.FromString(XPathStrings.NormalizeSpace(Text(evaluator, call, focus)));
                case XPathFunction.Translate:
                    return XPathValue.FromString(XPathStrings.Translate(
                        Argument(evaluator, call, 0, focus),
                        Argument(evaluator, call, 1, focus).Span,
                        Argument(evaluator, call, 2, focus).Span));
                case XPathFunction.Boolean:
                    return XPathValue.FromBoolean(XPathEvaluator.ToBoolean(evaluator.Evaluate(arguments[0], focus)));
                case XPathFunction.Not:
                    return XPathValue.FromBoolean(!XPathEvaluator.ToBoolean(evaluator.Evaluate(arguments[0], focus)));
                case XPathFunction.True:
                    return XPathValue.FromBoolean(true);
                case XPathFunction.False:
                    return XPathValue.FromBoolean(false);
                case XPathFunction.Lang:
                    return XPathValue.FromBoolean(Lang(evaluator, Argument(evaluator, call, 0, focus).Span, focus.Node));
                case XPathFunction.Number:
                    return XPathValue.FromNumber(arguments.Length == 0
                        ? XPathConversions.ParseNumber(evaluator.StringValue(focus.Node).Span)
                        : Number(evaluator, call, focus));
                case XPathFunction.Sum:
                    var sum = 0d;
                    foreach (var id in NodeSet(evaluator, call, focus).Nodes)
                    {
                        sum += XPathConversions.ParseNumber(evaluator.StringValue(id).Span);
                    }

                    return XPathValue.FromNumber(sum);
                case XPathFunction.Floor:
                    return XPathValue.FromNumber(Math.Floor(Number(evaluator, call, focus)));
                case XPathFunction.Ceiling:
                    return XPathValue.FromNumber(Math.Ceiling(Number(evaluator, call, focus)));
                default:
                    // XPathFunction.Round.
                    return XPathValue.FromNumber(XPathConversions.Round(Number(evaluator, call, focus)));
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Returns <c>string()</c> of an argument.
        /// </summary>
        private static ReadOnlyMemory<byte> Argument(XPathEvaluator evaluator, XPathFunctionCall call, int index, in XPathFocus focus)
        {
            return evaluator.ToText(evaluator.Evaluate(call.Arguments[index], focus));
        }

        /// <summary>
        /// Returns <c>concat()</c> of the arguments.
        /// </summary>
        private static byte[] Concat(XPathEvaluator evaluator, XPathAst[] arguments, in XPathFocus focus)
        {
            var parts = new ReadOnlyMemory<byte>[arguments.Length];
            var length = 0;
            for (var i = 0; i < parts.Length; i++)
            {
                parts[i] = evaluator.ToText(evaluator.Evaluate(arguments[i], focus));
                length += parts[i].Length;
            }

            var text = new byte[length];
            var written = 0;
            foreach (var part in parts)
            {
                part.Span.CopyTo(text.AsSpan(written));
                written += part.Length;
            }

            return text;
        }

        /// <summary>
        /// Returns a byte with an ASCII capital letter made small.
        /// </summary>
        private static byte Fold(byte value)
        {
            return value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value;
        }

        /// <summary>
        /// Returns <c>lang()</c>: whether the <c>xml:lang</c> in scope on the context node is the language asked for, or
        /// a sublanguage of it, without regard to ASCII case.
        /// </summary>
        private static bool Lang(XPathEvaluator evaluator, ReadOnlySpan<byte> language, long context)
        {
            if (!evaluator.Navigator.Language(context, out var value) || value.Length < language.Length)
            {
                return false;
            }

            for (var i = 0; i < language.Length; i++)
            {
                if (Fold(value[i]) != Fold(language[i]))
                {
                    return false;
                }
            }

            return value.Length == language.Length || value[language.Length] == (byte)'-';
        }

        /// <summary>
        /// Returns <c>local-name()</c>, <c>namespace-uri()</c> or <c>name()</c> of the first node of the argument, or of
        /// the context node when there is none; the empty string for an empty node-set.
        /// </summary>
        private static XPathValue Name(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus, Func<XPathNavigator, long, byte[]> name)
        {
            if (call.Arguments.Length == 0)
            {
                return XPathValue.FromString(name(evaluator.Navigator, focus.Node));
            }

            var nodes = NodeSet(evaluator, call, focus);
            return XPathValue.FromString(nodes.Count == 0 ? ReadOnlyMemory<byte>.Empty : name(evaluator.Navigator, nodes.Nodes[0]));
        }

        /// <summary>
        /// Evaluates the first argument, which must be a node-set.
        /// </summary>
        private static XPathValue NodeSet(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            return evaluator.NodeSet(call.Arguments[0], focus, $"The argument of {XPathFunctions.Name(call.Function)}() must be a node-set.");
        }

        /// <summary>
        /// Returns <c>number()</c> of the first argument.
        /// </summary>
        private static double Number(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            return evaluator.ToNumber(evaluator.Evaluate(call.Arguments[0], focus));
        }

        /// <summary>
        /// Returns the QName of a node: <c>prefix:local-name</c>, or the local name when it has no prefix.
        /// </summary>
        private static byte[] QualifiedName(XPathNavigator navigator, long id)
        {
            var prefix = navigator.Prefix(id);
            var local = navigator.LocalName(id);
            if (prefix.IsEmpty)
            {
                return local.ToArray();
            }

            var name = new byte[prefix.Length + 1 + local.Length];
            prefix.CopyTo(name);
            name[prefix.Length] = (byte)':';
            local.CopyTo(name.AsSpan(prefix.Length + 1));
            return name;
        }

        /// <summary>
        /// Returns <c>string()</c> of the first argument, or the string-value of the context node when there is none.
        /// </summary>
        private static ReadOnlyMemory<byte> Text(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            return call.Arguments.Length == 0 ? evaluator.StringValue(focus.Node) : Argument(evaluator, call, 0, focus);
        }

        #endregion

    }

}
