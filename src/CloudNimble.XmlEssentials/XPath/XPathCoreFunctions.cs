using System;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// The 27 core functions of XPath 1.0 (Recommendation, section 4), on UTF-8.
    /// </summary>
    /// <remarks>
    /// Ported from Xemele's <c>XPathCoreFunctions</c>, rewritten to work on UTF-8 and to count code points. A function
    /// with an optional argument applies to the context node when it is left out. Each function is a method of its own,
    /// so that a call does only the work of its function: one method with a case for each would set up the locals of
    /// every case on every call.
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
            return call.Function switch
            {
                XPathFunction.Last => XPathValue.FromNumber(focus.Size),
                XPathFunction.Position => XPathValue.FromNumber(focus.Position),
                XPathFunction.Count => Count(evaluator, call, focus),
                XPathFunction.Id => Id(evaluator, call, focus),
                XPathFunction.LocalName => LocalName(evaluator, call, focus),
                XPathFunction.NamespaceUri => NamespaceUri(evaluator, call, focus),
                XPathFunction.Name => Name(evaluator, call, focus),
                XPathFunction.String => XPathValue.FromString(Text(evaluator, call, focus)),
                XPathFunction.Concat => XPathValue.FromString(Concat(evaluator, call.Arguments, focus)),
                XPathFunction.StartsWith or XPathFunction.Contains => XPathValue.FromBoolean(CallBoolean(evaluator, call, focus)),
                XPathFunction.SubstringBefore => SubstringBefore(evaluator, call, focus),
                XPathFunction.SubstringAfter => SubstringAfter(evaluator, call, focus),
                XPathFunction.Substring => Substring(evaluator, call, focus),
                XPathFunction.StringLength => XPathValue.FromNumber(XPathStrings.Length(Text(evaluator, call, focus).Span)),
                XPathFunction.NormalizeSpace => XPathValue.FromString(XPathStrings.NormalizeSpace(Text(evaluator, call, focus))),
                XPathFunction.Translate => Translate(evaluator, call, focus),
                XPathFunction.Boolean or XPathFunction.Not or XPathFunction.True or XPathFunction.False or XPathFunction.Lang => XPathValue.FromBoolean(CallBoolean(evaluator, call, focus)),
                XPathFunction.Number => XPathValue.FromNumber(call.Arguments.Length == 0
                    ? XPathConversions.ParseNumber(evaluator.StringValue(focus.Node).Span)
                    : evaluator.Number(call.Arguments[0], focus)),
                XPathFunction.Sum => Sum(evaluator, call, focus),
                XPathFunction.Floor => XPathValue.FromNumber(Math.Floor(evaluator.Number(call.Arguments[0], focus))),
                XPathFunction.Ceiling => XPathValue.FromNumber(Math.Ceiling(evaluator.Number(call.Arguments[0], focus))),
                // XPathFunction.Round.
                _ => XPathValue.FromNumber(XPathConversions.Round(evaluator.Number(call.Arguments[0], focus))),
            };
        }

        /// <summary>
        /// Calls a core function where a boolean is wanted: <c>boolean()</c> of its value.
        /// </summary>
        /// <param name="evaluator">The evaluator.</param>
        /// <param name="call">The call.</param>
        /// <param name="focus">The context.</param>
        /// <returns>The boolean. The functions that return one answer without making a value.</returns>
        internal static bool CallBoolean(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            return call.Function switch
            {
                XPathFunction.StartsWith => StartsWith(evaluator, call, focus),
                XPathFunction.Contains => Contains(evaluator, call, focus),
                XPathFunction.Boolean => evaluator.Boolean(call.Arguments[0], focus),
                XPathFunction.Not => !evaluator.Boolean(call.Arguments[0], focus),
                XPathFunction.True => true,
                XPathFunction.False => false,
                XPathFunction.Lang => Lang(evaluator, Argument(evaluator, call, 0, focus).Span, focus.Node),
                // A function whose value is not a boolean.
                _ => evaluator.ToBooleanAndRelease(Call(evaluator, call, focus)),
            };
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Returns <c>string()</c> of an argument.
        /// </summary>
        private static ReadOnlyMemory<byte> Argument(XPathEvaluator evaluator, XPathFunctionCall call, int index, in XPathFocus focus)
        {
            return evaluator.Text(call.Arguments[index], focus);
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
                parts[i] = evaluator.Text(arguments[i], focus);
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
        /// Returns <c>contains()</c>.
        /// </summary>
        private static bool Contains(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            var text = Argument(evaluator, call, 0, focus);
            return text.Span.IndexOf(Argument(evaluator, call, 1, focus).Span) >= 0;
        }

        /// <summary>
        /// Returns <c>count()</c>.
        /// </summary>
        private static XPathValue Count(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            var nodes = NodeSet(evaluator, call, focus);
            evaluator.Release(nodes);
            return XPathValue.FromNumber(nodes.Count);
        }

        /// <summary>
        /// Returns a byte with an ASCII capital letter made small.
        /// </summary>
        private static byte Fold(byte value)
        {
            return value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value;
        }

        /// <summary>
        /// Returns <c>id()</c>: no attribute has type ID without a DTD, so no element is ever found. The argument is
        /// still evaluated.
        /// </summary>
        private static XPathValue Id(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            evaluator.Release(evaluator.Evaluate(call.Arguments[0], focus));
            return XPathValue.FromNodes([], 0, owned: false);
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
        /// Returns <c>local-name()</c>.
        /// </summary>
        private static XPathValue LocalName(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            return XPathValue.FromString(NodeOrContext(evaluator, call, focus, out var id) ? evaluator.Navigator.LocalName(id).ToArray() : ReadOnlyMemory<byte>.Empty);
        }

        /// <summary>
        /// Returns <c>name()</c>: the QName of the node, <c>prefix:local-name</c>, or the local name when it has no prefix.
        /// </summary>
        private static XPathValue Name(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            if (!NodeOrContext(evaluator, call, focus, out var id))
            {
                return XPathValue.FromString(ReadOnlyMemory<byte>.Empty);
            }

            var prefix = evaluator.Navigator.Prefix(id);
            var local = evaluator.Navigator.LocalName(id);
            if (prefix.IsEmpty)
            {
                return XPathValue.FromString(local.ToArray());
            }

            var name = new byte[prefix.Length + 1 + local.Length];
            prefix.CopyTo(name);
            name[prefix.Length] = (byte)':';
            local.CopyTo(name.AsSpan(prefix.Length + 1));
            return XPathValue.FromString(name);
        }

        /// <summary>
        /// Returns <c>namespace-uri()</c>.
        /// </summary>
        private static XPathValue NamespaceUri(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            return XPathValue.FromString(NodeOrContext(evaluator, call, focus, out var id) ? evaluator.Navigator.NamespaceUri(id).ToArray() : ReadOnlyMemory<byte>.Empty);
        }

        /// <summary>
        /// Finds the node a name function is about: the first node of the argument, or the context node when there is none.
        /// </summary>
        /// <returns><see langword="false"/> when the argument is an empty node-set.</returns>
        private static bool NodeOrContext(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus, out long id)
        {
            if (call.Arguments.Length == 0)
            {
                id = focus.Node;
                return true;
            }

            var nodes = NodeSet(evaluator, call, focus);
            id = nodes.Count == 0 ? 0 : nodes.Nodes[0];
            evaluator.Release(nodes);
            return nodes.Count > 0;
        }

        /// <summary>
        /// Evaluates the first argument, which must be a node-set.
        /// </summary>
        private static XPathValue NodeSet(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            return evaluator.NodeSet(call.Arguments[0], focus, $"The argument of {XPathFunctions.Name(call.Function)}() must be a node-set.");
        }

        /// <summary>
        /// Returns <c>starts-with()</c>.
        /// </summary>
        private static bool StartsWith(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            var text = Argument(evaluator, call, 0, focus);
            return text.Span.StartsWith(Argument(evaluator, call, 1, focus).Span);
        }

        /// <summary>
        /// Returns <c>substring()</c>.
        /// </summary>
        private static XPathValue Substring(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            var text = Argument(evaluator, call, 0, focus);
            var start = evaluator.Number(call.Arguments[1], focus);
            double? length = call.Arguments.Length == 3 ? evaluator.Number(call.Arguments[2], focus) : null;
            return XPathValue.FromString(XPathStrings.Substring(text, start, length));
        }

        /// <summary>
        /// Returns <c>substring-after()</c>.
        /// </summary>
        private static XPathValue SubstringAfter(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            var text = Argument(evaluator, call, 0, focus);
            var search = Argument(evaluator, call, 1, focus);
            var at = text.Span.IndexOf(search.Span);
            return XPathValue.FromString(at < 0 ? ReadOnlyMemory<byte>.Empty : text[(at + search.Length)..]);
        }

        /// <summary>
        /// Returns <c>substring-before()</c>.
        /// </summary>
        private static XPathValue SubstringBefore(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            var text = Argument(evaluator, call, 0, focus);
            var at = text.Span.IndexOf(Argument(evaluator, call, 1, focus).Span);
            return XPathValue.FromString(at < 0 ? ReadOnlyMemory<byte>.Empty : text[..at]);
        }

        /// <summary>
        /// Returns <c>sum()</c>.
        /// </summary>
        private static XPathValue Sum(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            var sum = 0d;
            var nodes = NodeSet(evaluator, call, focus);
            foreach (var id in nodes.Nodes)
            {
                sum += XPathConversions.ParseNumber(evaluator.StringValue(id).Span);
            }

            evaluator.Release(nodes);
            return XPathValue.FromNumber(sum);
        }

        /// <summary>
        /// Returns <c>string()</c> of the first argument, or the string-value of the context node when there is none.
        /// </summary>
        private static ReadOnlyMemory<byte> Text(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            return call.Arguments.Length == 0 ? evaluator.StringValue(focus.Node) : Argument(evaluator, call, 0, focus);
        }

        /// <summary>
        /// Returns <c>translate()</c>.
        /// </summary>
        private static XPathValue Translate(XPathEvaluator evaluator, XPathFunctionCall call, in XPathFocus focus)
        {
            var text = Argument(evaluator, call, 0, focus);
            var from = Argument(evaluator, call, 1, focus);
            var to = Argument(evaluator, call, 2, focus);
            return XPathValue.FromString(XPathStrings.Translate(text, from.Span, to.Span));
        }

        #endregion

    }

}
