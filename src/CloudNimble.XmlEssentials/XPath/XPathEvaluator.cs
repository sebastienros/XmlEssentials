using System;
using System.Buffers;
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

        /// <summary>
        /// The depth of recursion from which every evaluation checks that the stack has room.
        /// </summary>
        private const int StackCheckDepth = 128;

        /// <summary>
        /// The context node of the last <c>.</c> that <see cref="TrySelectSimple"/> selected, which its span points at.
        /// </summary>
        private long _context;

        private int _depth;
        private readonly XmlDocument _document;
        private long[][] _free = new long[4][];
        private int _freeCount;
        private readonly long _maxOperations;
        private readonly XPathNavigator _navigator;
        private long _operations;
        private XPathIdBuffer _scratch;
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
            var value = Number(node.Operands[0], focus);
            for (var i = 0; i < node.Operators.Length; i++)
            {
                var right = Number(node.Operands[i + 1], focus);
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
        /// Evaluates an operand where a boolean is wanted.
        /// </summary>
        internal bool Boolean(XPathAst operand, in XPathFocus focus)
        {
            Enter();
            var result = operand.EvaluateBoolean(this, focus);
            _depth--;
            return result;
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
        internal bool ComparisonBoolean(XPathComparison node, in XPathFocus focus)
        {
            if (node.Operators.Length == 1 && TryCompareConstant(node.Operators[0], node.Operands[0], node.Operands[1], focus, out var direct))
            {
                return direct;
            }

            var value = Evaluate(node.Operands[0], focus);
            var result = false;
            for (var i = 0; i < node.Operators.Length; i++)
            {
                var right = Evaluate(node.Operands[i + 1], focus);
                result = Compare(node.Operators[i], value, right);
                Release(value);
                Release(right);
                value = XPathValue.FromBoolean(result);
            }

            return result;
        }

        /// <summary>
        /// Evaluates a part of the expression.
        /// </summary>
        /// <param name="ast">The part.</param>
        /// <param name="focus">The context.</param>
        /// <returns>The value.</returns>
        internal XPathValue Evaluate(XPathAst ast, in XPathFocus focus)
        {
            Enter();
            var value = ast.Evaluate(this, focus);
            _depth--;
            return value;
        }

        /// <summary>
        /// Evaluates a filter: a node-set and predicates over it, in document order.
        /// </summary>
        internal XPathValue Filter(XPathFilter node, in XPathFocus focus)
        {
            var input = NodeSet(node.Primary, focus, "A predicate can only filter a node-set.");
            XPathIdBuffer nodes;
            if (input.IsOwned)
            {
                // Nothing else holds the array, so the predicates filter it in place.
                nodes = new XPathIdBuffer(input.ToArray(), input.Count);
            }
            else
            {
                nodes = NewBuffer(input.Count);
                foreach (var id in input.Nodes)
                {
                    nodes.Add(id);
                }
            }

            ApplyPredicates(ref nodes, node.Predicates, reverse: false);
            return XPathValue.FromNodes(nodes.Items, nodes.Count, owned: true);
        }

        /// <summary>
        /// Evaluates a run of <c>or</c> or of <c>and</c>, which stops at the first operand that decides it.
        /// </summary>
        internal bool LogicalBoolean(XPathLogical node, in XPathFocus focus)
        {
            foreach (var operand in node.Operands)
            {
                if (Boolean(operand, focus) == node.IsOr)
                {
                    return node.IsOr;
                }
            }

            return !node.IsOr;
        }

        /// <summary>
        /// Evaluates unary minus.
        /// </summary>
        internal XPathValue Negation(XPathNegation node, in XPathFocus focus)
        {
            var value = Number(node.Operand, focus);
            return XPathValue.FromNumber(node.Negate ? -value : value);
        }

        /// <summary>
        /// Returns a buffer for node ids, from the arrays this evaluation has finished with when there is one.
        /// </summary>
        /// <param name="capacity">The number of ids it is expected to hold.</param>
        /// <returns>The buffer, which is given back with <see cref="Recycle"/> or kept by a value.</returns>
        internal XPathIdBuffer NewBuffer(int capacity)
        {
            return _freeCount > 0 ? new XPathIdBuffer(_free[--_freeCount], 0) : new XPathIdBuffer(capacity);
        }

        /// <summary>
        /// Evaluates an operand and returns <c>number()</c> of it, giving its node-set back to the pool.
        /// </summary>
        internal double Number(XPathAst operand, in XPathFocus focus)
        {
            var value = Evaluate(operand, focus);
            var result = ToNumber(value);
            Release(value);
            return result;
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
            var input = node.Input is { } source
                ? NodeSet(source, focus, "A path can only continue from a node-set.")
                : XPathValue.FromNode(node.IsAbsolute ? XPathNodeId.Document : focus.Node);
            if (node.Steps.Length == 0)
            {
                return input;
            }

            var output = Step(node.Steps[0], input.Nodes);
            Release(input);
            for (var i = 1; i < node.Steps.Length; i++)
            {
                var next = Step(node.Steps[i], output.AsSpan());
                Recycle(output.Items);
                output = next;
            }

            return XPathValue.FromNodes(output.Items, output.Count, owned: true);
        }

        /// <summary>
        /// Keeps an array this evaluation has finished with, for the next buffer it needs.
        /// </summary>
        /// <param name="array">An array rented from <see cref="ArrayPool{T}.Shared"/> that nothing else holds.</param>
        /// <remarks>
        /// Renting and returning through the shared pool costs more than a node test, and a predicate may need a buffer
        /// for each node it filters. The arrays are kept here instead and go back to the pool once, in
        /// <see cref="ReturnBuffers"/>.
        /// </remarks>
        internal void Recycle(long[] array)
        {
            if (_freeCount == _free.Length)
            {
                Array.Resize(ref _free, _free.Length * 2);
            }

            _free[_freeCount++] = array;
        }

        /// <summary>
        /// Gives the array of an owned node-set back, once the value has been consumed.
        /// </summary>
        /// <param name="value">The value. One that is not an owned node-set is left alone.</param>
        internal void Release(in XPathValue value)
        {
            if (value.IsOwned)
            {
                Recycle(value.ToArray());
            }
        }

        /// <summary>
        /// Gives every array this evaluation kept back to <see cref="ArrayPool{T}.Shared"/>, at its end.
        /// </summary>
        internal void ReturnBuffers()
        {
            for (var i = 0; i < _freeCount; i++)
            {
                ArrayPool<long>.Shared.Return(_free[i]);
            }

            _freeCount = 0;
            if (_scratch.Items is { } scratch)
            {
                ArrayPool<long>.Shared.Return(scratch);
                _scratch = default;
            }
        }

        /// <summary>
        /// Evaluates a path where a boolean is wanted: whether it selects a node. A one-step path is answered from the
        /// scratch buffer.
        /// </summary>
        internal bool PathBoolean(XPathPath node, in XPathFocus focus)
        {
            if (TrySelectSimple(node, focus, out var nodes))
            {
                return nodes.Length > 0;
            }

            return ToBooleanAndRelease(Path(node, focus));
        }

        /// <summary>
        /// Evaluates a path where a string is wanted: the string-value of its first node. <c>.</c> is the context node,
        /// and a one-step path is answered from the scratch buffer.
        /// </summary>
        internal ReadOnlyMemory<byte> PathText(XPathPath node, in XPathFocus focus)
        {
            if (TrySelectSimple(node, focus, out var nodes))
            {
                return nodes.Length == 0 ? ReadOnlyMemory<byte>.Empty : StringValue(nodes[0]);
            }

            return ToTextAndRelease(Path(node, focus));
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
        /// Evaluates an operand where a string is wanted.
        /// </summary>
        internal ReadOnlyMemory<byte> Text(XPathAst operand, in XPathFocus focus)
        {
            Enter();
            var result = operand.EvaluateText(this, focus);
            _depth--;
            return result;
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
        /// Returns <c>boolean()</c> of a value, and gives its node-set back.
        /// </summary>
        internal bool ToBooleanAndRelease(in XPathValue value)
        {
            var result = ToBoolean(value);
            Release(value);
            return result;
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
        /// Returns <c>string()</c> of a value, and gives its node-set back.
        /// </summary>
        /// <remarks>
        /// The string-value of a node is a slice of the document or a new array, never the array of the node-set.
        /// </remarks>
        internal ReadOnlyMemory<byte> ToTextAndRelease(in XPathValue value)
        {
            var result = ToText(value);
            Release(value);
            return result;
        }

        /// <summary>
        /// Evaluates a run of <c>|</c>.
        /// </summary>
        internal XPathValue Union(XPathUnion node, in XPathFocus focus)
        {
            var value = NodeSet(node.Operands[0], focus, "The operands of '|' must be node-sets.");
            for (var i = 1; i < node.Operands.Length; i++)
            {
                var next = NodeSet(node.Operands[i], focus, "The operands of '|' must be node-sets.");
                var merged = Merge(value, next);
                Release(value);
                Release(next);
                value = merged;
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
                    var candidate = new XPathFocus(items[i], position, size);
                    bool pass;
                    if (!predicate.IsPositional)
                    {
                        // Its value is never a number, so it passes when its boolean() is true.
                        pass = Boolean(predicate, candidate);
                    }
                    else
                    {
                        var value = Evaluate(predicate, candidate);
                        pass = value.Kind is XPathResultKind.Number ? value.Number == position : ToBoolean(value);
                        Release(value);
                    }

                    if (pass)
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
                return right.Kind is XPathResultKind.NodeSet ? CompareSets(op, left, right) : CompareSet(op, left.Nodes, right);
            }

            if (right.Kind is XPathResultKind.NodeSet)
            {
                return CompareSet(Flip(op), right.Nodes, left);
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
        private bool CompareSet(XPathComparisonOperator op, ReadOnlySpan<long> set, in XPathValue other)
        {
            var equality = op is XPathComparisonOperator.Equal or XPathComparisonOperator.NotEqual;
            switch (other.Kind)
            {
                case XPathResultKind.Boolean:
                    // The node-set is converted to a boolean as a whole.
                    var any = set.Length > 0;
                    return equality ? Same(op, any == other.Boolean) : CompareNumbers(op, any ? 1 : 0, other.Boolean ? 1 : 0);
                case XPathResultKind.Number:
                    foreach (var id in set)
                    {
                        if (CompareNumbers(op, XPathConversions.ParseNumber(StringValue(id).Span), other.Number))
                        {
                            return true;
                        }
                    }

                    return false;
                default:
                    var number = equality ? 0 : XPathConversions.ParseNumber(other.String.Span);
                    foreach (var id in set)
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
        /// Counts one more level of recursion, and checks that the stack has room once it is deep.
        /// </summary>
        /// <remarks>
        /// The parser bounds the nesting, so the stack is checked only when the recursion has gone deep: asking the
        /// runtime on every node costs more than most nodes do.
        /// </remarks>
        private void Enter()
        {
            if (++_depth > StackCheckDepth)
            {
                RuntimeHelpers.EnsureSufficientExecutionStack();
            }
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
            var merged = ArrayPool<long>.Shared.Rent(Math.Max(a.Length + b.Length, 1));
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
            return XPathValue.FromNodes(merged, count, owned: true);
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
            var output = NewBuffer(contexts.Length);
            var filtered = step.Predicates.Length > 0;
            var scratch = filtered ? NewBuffer(8) : default;
            var ascending = true;
            var last = -1L;
            foreach (var context in contexts)
            {
                var start = output.Count;
                if (!filtered)
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

            if (filtered)
            {
                Recycle(scratch.Items);
            }

            if (!ascending)
            {
                output.SortAndDeduplicate();
            }

            return output;
        }

        /// <summary>
        /// Compares a one-step path with a literal or a number without making a node-set: the nodes are selected into
        /// the scratch buffer and compared there, and the comparison stops at the first that passes.
        /// </summary>
        /// <param name="op">The operator.</param>
        /// <param name="left">The left operand.</param>
        /// <param name="right">The right operand.</param>
        /// <param name="focus">The context.</param>
        /// <param name="result">When this method returns <see langword="true"/>, the result of the comparison.</param>
        /// <returns><see langword="true"/> when the comparison had this shape, either way round.</returns>
        /// <remarks>
        /// This is the shape of most predicates, such as <c>[@id = 'x']</c> and <c>[. &gt; 5]</c>. The constant is
        /// evaluated first, and nothing else is evaluated while the scratch buffer holds the nodes.
        /// </remarks>
        private bool TryCompareConstant(XPathComparisonOperator op, XPathAst left, XPathAst right, in XPathFocus focus, out bool result)
        {
            if (right is XPathLiteral or XPathNumber && TrySelectSimple(left, focus, out var nodes))
            {
                result = CompareSet(op, nodes, right.Evaluate(this, focus));
                return true;
            }

            if (left is XPathLiteral or XPathNumber && TrySelectSimple(right, focus, out nodes))
            {
                result = CompareSet(Flip(op), nodes, left.Evaluate(this, focus));
                return true;
            }

            result = false;
            return false;
        }

        /// <summary>
        /// Selects the nodes of a relative path of at most one step with no predicate, such as <c>.</c>, <c>@id</c> or
        /// <c>title</c>: the context node for <c>.</c>, the scratch buffer for a step.
        /// </summary>
        /// <param name="ast">The expression.</param>
        /// <param name="focus">The context.</param>
        /// <param name="nodes">When this method returns <see langword="true"/>, the nodes, ascending and without duplicates.</param>
        /// <returns><see langword="true"/> when the expression had this shape.</returns>
        /// <remarks>
        /// One step from one context node selects in document order with no duplicates, so the nodes need no sorting.
        /// The span is valid until this method is called again, so the caller reads it before it evaluates anything.
        /// </remarks>
        private bool TrySelectSimple(XPathAst ast, in XPathFocus focus, out ReadOnlySpan<long> nodes)
        {
            if (ast is not XPathPath { Input: null, IsAbsolute: false, Steps.Length: <= 1 } path || (path.Steps.Length == 1 && path.Steps[0].Predicates.Length > 0))
            {
                nodes = default;
                return false;
            }

            if (path.Steps.Length == 0)
            {
                _context = focus.Node;
                nodes = new ReadOnlySpan<long>(in _context);
                return true;
            }

            if (_scratch.Items is null)
            {
                _scratch = new XPathIdBuffer(8);
            }

            _scratch.Clear();
            var step = path.Steps[0];
            Charge(_navigator.Select(focus.Node, step.Axis, step.Test, ref _scratch));
            nodes = _scratch.AsSpan();
            return true;
        }

        #endregion

    }

}
