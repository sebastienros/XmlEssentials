using System.Collections.Generic;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// Rewrites the steps of a location path, when it is compiled, into steps that select the same nodes with less work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three rewrites, each exact for every document:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <c>self::node()</c> with no predicate selects its context nodes, so it is dropped. <c>.//x</c> is then
    /// <c>//x</c>, and <c>.</c> on its own is a path with no step.
    /// </description></item>
    /// <item><description>
    /// <c>descendant-or-self::node()/child::x[p]</c>, which is what <c>//x[p]</c> means, is <c>descendant::x[p]</c>
    /// when no predicate of the step is positional (<see cref="IsPositional"/>): the children of a node and of its
    /// descendants are its descendants, and a predicate that reads no position filters each node alone. This is the
    /// rewrite <c>System.Xml.XPath</c> makes (<c>QueryBuilder.ProcessAxis</c>), under the same condition.
    /// <c>//x[1]</c> keeps its two steps: it is the first <c>x</c> child of each node.
    /// </description></item>
    /// <item><description>
    /// <c>descendant-or-self::node()/attribute::a</c> is <c>descendant-or-self::*/attribute::a</c>, because only an
    /// element has attributes or namespace nodes; the same holds for the <c>namespace</c> axis.
    /// </description></item>
    /// </list>
    /// </remarks>
    internal static class XPathPathOptimizer
    {

        #region Fields

        private static readonly XPathNodeTest AnyElement = new(XPathNodeTestKind.Any, [], [], null);

        #endregion

        #region Internal Methods

        /// <summary>
        /// Returns whether a predicate may depend on the position of its context node: its value may be a number,
        /// which is compared with the position, or it calls <c>position()</c> or <c>last()</c> for its own context.
        /// </summary>
        /// <param name="predicate">The predicate.</param>
        /// <returns><see langword="true"/> unless the predicate is known to filter each node by itself.</returns>
        /// <remarks>
        /// A variable may hold a number, so it is positional. A predicate of a nested step or filter has a context of
        /// its own, so a <c>position()</c> in it does not count.
        /// </remarks>
        internal static bool IsPositional(XPathAst predicate)
        {
            return predicate switch
            {
                XPathNumber or XPathArithmetic or XPathNegation or XPathVariableReference => true,
                XPathFunctionCall call => call.Function is XPathFunction.Last or XPathFunction.Position or XPathFunction.Count
                    or XPathFunction.StringLength or XPathFunction.Number or XPathFunction.Sum or XPathFunction.Floor
                    or XPathFunction.Ceiling or XPathFunction.Round || ReadsPosition(predicate),
                _ => ReadsPosition(predicate),
            };
        }

        /// <summary>
        /// Rewrites the steps of a path.
        /// </summary>
        /// <param name="steps">The steps as they were parsed, which are changed in place.</param>
        internal static void Optimize(List<XPathStep> steps)
        {
            steps.RemoveAll(static step => step.Axis is XPathAxis.Self && IsAnyNode(step));
            for (var i = 0; i < steps.Count - 1; i++)
            {
                if (steps[i].Axis is not XPathAxis.DescendantOrSelf || !IsAnyNode(steps[i]))
                {
                    continue;
                }

                var next = steps[i + 1];
                if (next.Axis is XPathAxis.Child && !System.Array.Exists(next.Predicates, IsPositional))
                {
                    steps[i] = new XPathStep(XPathAxis.Descendant, next.Test, next.Predicates);
                    steps.RemoveAt(i + 1);
                }
                else if (next.Axis is XPathAxis.Attribute or XPathAxis.Namespace)
                {
                    steps[i] = new XPathStep(XPathAxis.DescendantOrSelf, AnyElement, []);
                }
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Returns whether a step is <c>axis::node()</c> with no predicate.
        /// </summary>
        private static bool IsAnyNode(XPathStep step)
        {
            return step.Test.Kind is XPathNodeTestKind.Node && step.Predicates.Length == 0;
        }

        /// <summary>
        /// Returns whether any of the operands calls <c>position()</c> or <c>last()</c> for the context it is evaluated in.
        /// </summary>
        private static bool ReadsPosition(XPathAst[] operands)
        {
            foreach (var operand in operands)
            {
                if (ReadsPosition(operand))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns whether an expression calls <c>position()</c> or <c>last()</c> for the context it is evaluated in.
        /// </summary>
        private static bool ReadsPosition(XPathAst ast)
        {
            return ast switch
            {
                XPathFunctionCall call => call.Function is XPathFunction.Last or XPathFunction.Position || ReadsPosition(call.Arguments),
                XPathLogical logical => ReadsPosition(logical.Operands),
                XPathComparison comparison => ReadsPosition(comparison.Operands),
                XPathArithmetic arithmetic => ReadsPosition(arithmetic.Operands),
                XPathUnion union => ReadsPosition(union.Operands),
                XPathNegation negation => ReadsPosition(negation.Operand),
                XPathFilter filter => ReadsPosition(filter.Primary),
                XPathPath path => path.Input is not null && ReadsPosition(path.Input),
                // A literal, a number and a variable read no position.
                _ => false,
            };
        }

        #endregion

    }

}
