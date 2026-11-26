using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// Evaluates a compiled expression over one document, once.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A plain recursive walk of the tree: each node of it calls back into the evaluator for its operation. The depth
    /// of the recursion is bounded by <see cref="XPathOptions.MaxDepth"/>, which the parser enforced, and every step
    /// checks that the stack has room.
    /// </para>
    /// <para>
    /// One counter is the budget: the rows the navigator looks at, plus one for each string-value read. It is checked
    /// against <see cref="XPathOptions.MaxOperations"/> each time it grows. The evaluator is used by one thread.
    /// </para>
    /// </remarks>
    internal sealed class XPathEvaluator
    {

        #region Fields

        private readonly XmlDocument _document;
        private readonly long _maxOperations;
        private readonly XPathNavigator _navigator;
        private long _operations;
        private readonly XPathVariables? _variables;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the navigator over the document.
        /// </summary>
        internal XPathNavigator Navigator => _navigator;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates an evaluator.
        /// </summary>
        /// <param name="document">The document, which is not disposed.</param>
        /// <param name="variables">The values of the variables, or <see langword="null"/>.</param>
        /// <param name="maxOperations">The limit on the work, or 0 for none.</param>
        internal XPathEvaluator(XmlDocument document, XPathVariables? variables, long maxOperations)
        {
            _document = document;
            _navigator = new XPathNavigator(document);
            _variables = variables;
            _maxOperations = maxOperations;
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Evaluates a run of arithmetic, from left to right.
        /// </summary>
        internal XPathValue Arithmetic(XPathArithmetic node, in XPathFocus focus)
        {
            var value = ToNumber(Evaluate(node.Operands[0], focus));
            for (var i = 0; i < node.Operators.Length; i++)
            {
                var right = ToNumber(Evaluate(node.Operands[i + 1], focus));
                value = node.Operators[i] switch
                {
                    XPathArithmeticOperator.Add => value + right,
                    XPathArithmeticOperator.Subtract => value - right,
                    XPathArithmeticOperator.Multiply => value * right,
                    XPathArithmeticOperator.Divide => value / right,
                    // mod: the remainder of a truncating division, with the sign of the dividend.
                    _ => value % right,
                };
            }

            return XPathValue.FromNumber(value);
        }

        /// <summary>
        /// Adds work to the budget.
        /// </summary>
        /// <param name="operations">The work done.</param>
        /// <exception cref="XPathException">The work of the evaluation is now more than <see cref="XPathOptions.MaxOperations"/>.</exception>
        internal void Charge(long operations)
        {
            _operations += operations;
            if (_maxOperations > 0 && _operations > _maxOperations)
            {
                throw new XPathException(string.Create(CultureInfo.InvariantCulture, $"{nameof(XPathOptions.MaxOperations)} exceeded ({_maxOperations})."), position: null);
            }
        }

        /// <summary>
        /// Evaluates a run of comparisons, from left to right.
        /// </summary>
        internal XPathValue Comparison(XPathComparison node, in XPathFocus focus)
        {
            var value = Evaluate(node.Operands[0], focus);
            for (var i = 0; i < node.Operators.Length; i++)
            {
                value = XPathValue.FromBoolean(Compare(node.Operators[i], value, Evaluate(node.Operands[i + 1], focus)));
            }

            return value;
        }

        /// <summary>
        /// Evaluates a part of the expression.
        /// </summary>
        /// <param name="ast">The part.</param>
        /// <param name="focus">The context.</param>
        /// <returns>The value.</returns>
        internal XPathValue Evaluate(XPathAst ast, in XPathFocus focus)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
            return ast.Evaluate(this, focus);
        }

        /// <summary>
        /// Evaluates a filter: a node-set and predicates over it, in document order.
        /// </summary>
        internal XPathValue Filter(XPathFilter node, in XPathFocus focus)
        {
            var input = NodeSet(node.Primary, focus, "A predicate can only filter a node-set.");
            var nodes = new XPathIdBuffer(input.Count);
            foreach (var id in input.Nodes)
            {
                nodes.Add(id);
            }

            ApplyPredicates(ref nodes, node.Predicates, reverse: false);
            return XPathValue.FromNodes(nodes.Items, nodes.Count);
        }

        /// <summary>
        /// Evaluates a run of <c>or</c> or of <c>and</c>, which stops at the first operand that decides it.
        /// </summary>
        internal XPathValue Logical(XPathLogical node, in XPathFocus focus)
        {
            foreach (var operand in node.Operands)
            {
                if (ToBoolean(Evaluate(operand, focus)) == node.IsOr)
                {
                    return XPathValue.FromBoolean(node.IsOr);
                }
            }

            return XPathValue.FromBoolean(!node.IsOr);
        }

        /// <summary>
        /// Evaluates unary minus.
        /// </summary>
        internal XPathValue Negation(XPathNegation node, in XPathFocus focus)
        {
            var value = ToNumber(Evaluate(node.Operand, focus));
            return XPathValue.FromNumber(node.Negate ? -value : value);
        }

        /// <summary>
        /// Evaluates an operand that must be a node-set.
        /// </summary>
        /// <param name="operand">The operand.</param>
        /// <param name="focus">The context.</param>
        /// <param name="message">The message when it is not one.</param>
        /// <returns>The node-set.</returns>
        /// <exception cref="XPathException">The operand is not a node-set.</exception>
        internal XPathValue NodeSet(XPathAst operand, in XPathFocus focus, string message)
        {
            var value = Evaluate(operand, focus);
            if (value.Kind is not XPathResultKind.NodeSet)
            {
                throw new XPathException(message, operand.Position);
            }

            return value;
        }

        /// <summary>
        /// Evaluates a location path, or a filter followed by steps.
        /// </summary>
        internal XPathValue Path(XPathPath node, in XPathFocus focus)
        {
            long[] nodes;
            int count;
            if (node.Input is { } input)
            {
                var start = NodeSet(input, focus, "A path can only continue from a node-set.");
                nodes = start.NodeArray;
                count = start.Count;
            }
            else
            {
                nodes = [node.IsAbsolute ? XPathNodeId.Document : focus.Node];
                count = 1;
            }

            foreach (var step in node.Steps)
            {
                var output = Step(step, nodes.AsSpan(0, count));
                nodes = output.Items;
                count = output.Count;
            }

            return XPathValue.FromNodes(nodes, count);
        }

        /// <summary>
        /// Returns the string-value of a node, and charges one operation for reading it.
        /// </summary>
        /// <param name="id">The node.</param>
        /// <returns>The string-value, as UTF-8.</returns>
        internal ReadOnlyMemory<byte> StringValue(long id)
        {
            Charge(1);
            return _navigator.StringValue(id);
        }

        /// <summary>
        /// Returns <c>boolean()</c> of a value.
        /// </summary>
        internal static bool ToBoolean(in XPathValue value)
        {
            return value.Kind switch
            {
                XPathResultKind.NodeSet => value.Count > 0,
                XPathResultKind.Boolean => value.Boolean,
                XPathResultKind.Number => !(value.Number == 0 || double.IsNaN(value.Number)),
                _ => value.String.Length > 0,
            };
        }

        /// <summary>
        /// Returns <c>number()</c> of a value.
        /// </summary>
        internal double ToNumber(in XPathValue value)
        {
            return value.Kind switch
            {
                XPathResultKind.NodeSet => value.Count == 0 ? double.NaN : XPathConversions.ParseNumber(StringValue(value.Nodes[0]).Span),
                XPathResultKind.Boolean => value.Boolean ? 1 : 0,
                XPathResultKind.Number => value.Number,
                _ => XPathConversions.ParseNumber(value.String.Span),
            };
        }

        /// <summary>
        /// Returns <c>string()</c> of a value, as UTF-8.
        /// </summary>
        internal ReadOnlyMemory<byte> ToText(in XPathValue value)
        {
            return value.Kind switch
            {
                XPathResultKind.NodeSet => value.Count == 0 ? ReadOnlyMemory<byte>.Empty : StringValue(value.Nodes[0]),
                XPathResultKind.Boolean => XPathConversions.FormatBoolean(value.Boolean),
                XPathResultKind.Number => XPathConversions.FormatNumber(value.Number),
                _ => value.String,
            };
        }

        /// <summary>
        /// Evaluates a run of <c>|</c>.
        /// </summary>
        internal XPathValue Union(XPathUnion node, in XPathFocus focus)
        {
            var value = NodeSet(node.Operands[0], focus, "The operands of '|' must be node-sets.");
            for (var i = 1; i < node.Operands.Length; i++)
            {
                value = Merge(value, NodeSet(node.Operands[i], focus, "The operands of '|' must be node-sets."));
            }

            return value;
        }

        /// <summary>
        /// Returns the value of a variable.
        /// </summary>
        /// <exception cref="XPathException">The variable has no value.</exception>
        /// <exception cref="ArgumentException">The value is a node-set from another document.</exception>
        internal XPathValue Variable(XPathVariableReference node)
        {
            if (_variables is null || !_variables.TryGet(node.LocalName, node.NamespaceUri, out var value, out var document))
            {
                throw new XPathException($"The variable '${node.Name}' has no value.", node.Position);
            }

            if (document is not null && !ReferenceEquals(document, _document))
            {
                throw new ArgumentException($"The variable '${node.Name}' holds nodes of another document.", "variables");
            }

            return value;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Filters nodes, in place, by each predicate in turn, each with the size the one before it left.
        /// </summary>
        /// <param name="nodes">The nodes, in document order.</param>
        /// <param name="predicates">The predicates.</param>
        /// <param name="reverse">Whether the proximity position counts from the last node: on a reverse axis.</param>
        /// <remarks>
        /// A predicate that is a number passes the node whose proximity position it equals; any other value passes a
        /// node when its <c>boolean()</c> is true. A number literal, such as <c>[1]</c>, picks its node directly.
        /// </remarks>
        private void ApplyPredicates(ref XPathIdBuffer nodes, XPathAst[] predicates, bool reverse)
        {
            foreach (var predicate in predicates)
            {
                var size = nodes.Count;
                var items = nodes.Items;
                if (predicate is XPathNumber number)
                {
                    var position = number.Value;
                    var found = position >= 1 && position <= size && position == Math.Floor(position);
                    if (found)
                    {
                        items[0] = items[reverse ? size - (int)position : (int)position - 1];
                    }

                    nodes.Truncate(found ? 1 : 0);
                    continue;
                }

                var kept = 0;
                for (var i = 0; i < size; i++)
                {
                    var position = reverse ? size - i : i + 1;
                    var value = Evaluate(predicate, new XPathFocus(items[i], position, size));
                    if (value.Kind is XPathResultKind.Number ? value.Number == position : ToBoolean(value))
                    {
                        items[kept++] = items[i];
                    }
                }

                nodes.Truncate(kept);
            }
        }

        /// <summary>
        /// Compares two values with an equality or relational operator (Recommendation, section 3.4).
        /// </summary>
        private bool Compare(XPathComparisonOperator op, in XPathValue left, in XPathValue right)
        {
            if (left.Kind is XPathResultKind.NodeSet)
            {
                return right.Kind is XPathResultKind.NodeSet ? CompareSets(op, left, right) : CompareSet(op, left, right);
            }

            if (right.Kind is XPathResultKind.NodeSet)
            {
                return CompareSet(Flip(op), right, left);
            }

            if (op is not (XPathComparisonOperator.Equal or XPathComparisonOperator.NotEqual))
            {
                return CompareNumbers(op, ToNumber(left), ToNumber(right));
            }

            if (left.Kind is XPathResultKind.Boolean || right.Kind is XPathResultKind.Boolean)
            {
                return Same(op, ToBoolean(left) == ToBoolean(right));
            }

            if (left.Kind is XPathResultKind.Number || right.Kind is XPathResultKind.Number)
            {
                return CompareNumbers(op, ToNumber(left), ToNumber(right));
            }

            return Same(op, left.String.Span.SequenceEqual(right.String.Span));
        }

        /// <summary>
        /// Compares two numbers.
        /// </summary>
        private static bool CompareNumbers(XPathComparisonOperator op, double left, double right)
        {
            return op switch
            {
                XPathComparisonOperator.Equal => left == right,
                XPathComparisonOperator.NotEqual => left != right,
                XPathComparisonOperator.Less => left < right,
                XPathComparisonOperator.LessOrEqual => left <= right,
                XPathComparisonOperator.Greater => left > right,
                _ => left >= right,
            };
        }

        /// <summary>
        /// Compares a node-set, on the left, with a value that is not one: true when some node compares true.
        /// </summary>
        private bool CompareSet(XPathComparisonOperator op, in XPathValue set, in XPathValue other)
        {
            var equality = op is XPathComparisonOperator.Equal or XPathComparisonOperator.NotEqual;
            switch (other.Kind)
            {
                case XPathResultKind.Boolean:
                    // The node-set is converted to a boolean as a whole.
                    var any = set.Count > 0;
                    return equality ? Same(op, any == other.Boolean) : CompareNumbers(op, any ? 1 : 0, other.Boolean ? 1 : 0);
                case XPathResultKind.Number:
                    foreach (var id in set.Nodes)
                    {
                        if (CompareNumbers(op, XPathConversions.ParseNumber(StringValue(id).Span), other.Number))
                        {
                            return true;
                        }
                    }

                    return false;
                default:
                    var number = equality ? 0 : XPathConversions.ParseNumber(other.String.Span);
                    foreach (var id in set.Nodes)
                    {
                        var text = StringValue(id).Span;
                        if (equality ? Same(op, text.SequenceEqual(other.String.Span)) : CompareNumbers(op, XPathConversions.ParseNumber(text), number))
                        {
                            return true;
                        }
                    }

                    return false;
            }
        }

        /// <summary>
        /// Compares two node-sets: true when some node of the one and some node of the other compare true.
        /// </summary>
        /// <remarks>
        /// Each string-value is read once. <c>=</c> puts the smaller set in a hash set; <c>!=</c> is true when not every
        /// string-value is the same; a relational operator compares the extremes of the numbers of the two sets.
        /// </remarks>
        private bool CompareSets(XPathComparisonOperator op, in XPathValue left, in XPathValue right)
        {
            if (left.Count == 0 || right.Count == 0)
            {
                return false;
            }

            switch (op)
            {
                case XPathComparisonOperator.Equal:
                    var (small, large) = left.Count <= right.Count ? (left, right) : (right, left);
                    var values = new HashSet<ReadOnlyMemory<byte>>(small.Count, XPathBytesComparer.Instance);
                    foreach (var id in small.Nodes)
                    {
                        values.Add(StringValue(id));
                    }

                    foreach (var id in large.Nodes)
                    {
                        if (values.Contains(StringValue(id)))
                        {
                            return true;
                        }
                    }

                    return false;
                case XPathComparisonOperator.NotEqual:
                    var first = StringValue(left.Nodes[0]);
                    return Differs(left.Nodes[1..], first) || Differs(right.Nodes, first);
                default:
                    var (leftMin, leftMax) = Range(left);
                    var (rightMin, rightMax) = Range(right);
                    return op switch
                    {
                        XPathComparisonOperator.Less => leftMin < rightMax,
                        XPathComparisonOperator.LessOrEqual => leftMin <= rightMax,
                        XPathComparisonOperator.Greater => leftMax > rightMin,
                        _ => leftMax >= rightMin,
                    };
            }
        }

        /// <summary>
        /// Returns whether the string-value of some node differs from a string.
        /// </summary>
        private bool Differs(ReadOnlySpan<long> nodes, ReadOnlyMemory<byte> text)
        {
            foreach (var id in nodes)
            {
                if (!StringValue(id).Span.SequenceEqual(text.Span))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the operator that gives the same result with its operands swapped.
        /// </summary>
        private static XPathComparisonOperator Flip(XPathComparisonOperator op)
        {
            return op switch
            {
                XPathComparisonOperator.Less => XPathComparisonOperator.Greater,
                XPathComparisonOperator.LessOrEqual => XPathComparisonOperator.GreaterOrEqual,
                XPathComparisonOperator.Greater => XPathComparisonOperator.Less,
                XPathComparisonOperator.GreaterOrEqual => XPathComparisonOperator.LessOrEqual,
                _ => op,
            };
        }

        /// <summary>
        /// Returns the union of two node-sets, merged in document order.
        /// </summary>
        private static XPathValue Merge(in XPathValue left, in XPathValue right)
        {
            var a = left.Nodes;
            var b = right.Nodes;
            var merged = new long[a.Length + b.Length];
            int i = 0, j = 0, count = 0;
            while (i < a.Length && j < b.Length)
            {
                var x = a[i];
                var y = b[j];
                merged[count++] = x <= y ? x : y;
                i += x <= y ? 1 : 0;
                j += y <= x ? 1 : 0;
            }

            a[i..].CopyTo(merged.AsSpan(count));
            count += a.Length - i;
            b[j..].CopyTo(merged.AsSpan(count));
            count += b.Length - j;
            return XPathValue.FromNodes(merged, count);
        }

        /// <summary>
        /// Returns the least and the greatest of the numbers of the string-values of a node-set, leaving out NaN.
        /// </summary>
        /// <returns>NaN for both when every number is NaN, so that every comparison with them is false.</returns>
        private (double Min, double Max) Range(in XPathValue set)
        {
            var min = double.NaN;
            var max = double.NaN;
            foreach (var id in set.Nodes)
            {
                var value = XPathConversions.ParseNumber(StringValue(id).Span);
                if (double.IsNaN(value))
                {
                    continue;
                }

                min = double.IsNaN(min) ? value : Math.Min(min, value);
                max = double.IsNaN(max) ? value : Math.Max(max, value);
            }

            return (min, max);
        }

        /// <summary>
        /// Returns the result of <c>=</c> or <c>!=</c> from whether two values are equal.
        /// </summary>
        private static bool Same(XPathComparisonOperator op, bool equal)
        {
            return op is XPathComparisonOperator.Equal ? equal : !equal;
        }

        /// <summary>
        /// Evaluates one step from every node of a node-set.
        /// </summary>
        /// <param name="step">The step.</param>
        /// <param name="contexts">The context nodes, in document order.</param>
        /// <returns>The nodes selected, in document order and without duplicates.</returns>
        /// <remarks>
        /// The nodes selected from each context node are ascending, so the whole list is ascending unless the nodes of
        /// two context nodes overlap or interleave; only then is it sorted and its duplicates dropped.
        /// </remarks>
        private XPathIdBuffer Step(XPathStep step, ReadOnlySpan<long> contexts)
        {
            var output = new XPathIdBuffer(contexts.Length);
            var scratch = new XPathIdBuffer(step.Predicates.Length == 0 ? 0 : 8);
            var ascending = true;
            var last = -1L;
            foreach (var context in contexts)
            {
                var start = output.Count;
                if (step.Predicates.Length == 0)
                {
                    Charge(_navigator.Select(context, step.Axis, step.Test, ref output));
                }
                else
                {
                    scratch.Clear();
                    Charge(_navigator.Select(context, step.Axis, step.Test, ref scratch));
                    ApplyPredicates(ref scratch, step.Predicates, step.IsReverse);
                    foreach (var id in scratch.AsSpan())
                    {
                        output.Add(id);
                    }
                }

                if (output.Count > start)
                {
                    ascending &= output.Items[start] > last;
                    last = output.Items[output.Count - 1];
                }
            }

            if (!ascending)
            {
                output.SortAndDeduplicate();
            }

            return output;
        }

        #endregion

    }

}
