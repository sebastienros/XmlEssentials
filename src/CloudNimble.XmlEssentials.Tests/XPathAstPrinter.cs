using System;
using System.Globalization;
using System.Linq;
using System.Text;
using CloudNimble.Text.Xml.XPath;

namespace CloudNimble.XmlEssentials.Tests
{

    /// <summary>
    /// Writes a compiled XPath expression in a canonical form, with every abbreviation expanded, every name test as
    /// <c>{namespace-uri}local-name</c>, and every run of operators in parentheses.
    /// </summary>
    internal static class XPathAstPrinter
    {

        #region Fields

        private static readonly string[] Axes =
        [
            "ancestor", "ancestor-or-self", "attribute", "child", "descendant", "descendant-or-self", "following",
            "following-sibling", "namespace", "parent", "preceding", "preceding-sibling", "self",
        ];

        private static readonly string[] Functions =
        [
            "last", "position", "count", "id", "local-name", "namespace-uri", "name", "string", "concat", "starts-with",
            "contains", "substring-before", "substring-after", "substring", "string-length", "normalize-space",
            "translate", "boolean", "not", "true", "false", "lang", "number", "sum", "floor", "ceiling", "round",
        ];

        #endregion

        #region Public Methods

        /// <summary>
        /// Writes a compiled expression.
        /// </summary>
        public static string Print(XPathAst ast)
        {
            return ast switch
            {
                XPathLiteral literal => "'" + Encoding.UTF8.GetString(literal.Value) + "'",
                XPathNumber number => number.Value.ToString("R", CultureInfo.InvariantCulture),
                XPathVariableReference variable => "${" + variable.NamespaceUri + "}" + variable.LocalName,
                XPathNegation negation => (negation.Negate ? "-(" : "--(") + Print(negation.Operand) + ")",
                XPathLogical logical => "(" + string.Join(logical.IsOr ? " or " : " and ", logical.Operands.Select(Print)) + ")",
                XPathComparison comparison => Run(comparison.Operands, comparison.Operators.Select(op => op switch
                {
                    XPathComparisonOperator.Equal => "=",
                    XPathComparisonOperator.NotEqual => "!=",
                    XPathComparisonOperator.Less => "<",
                    XPathComparisonOperator.LessOrEqual => "<=",
                    XPathComparisonOperator.Greater => ">",
                    _ => ">=",
                }).ToArray()),
                XPathArithmetic arithmetic => Run(arithmetic.Operands, arithmetic.Operators.Select(op => op switch
                {
                    XPathArithmeticOperator.Add => "+",
                    XPathArithmeticOperator.Subtract => "-",
                    XPathArithmeticOperator.Multiply => "*",
                    XPathArithmeticOperator.Divide => "div",
                    _ => "mod",
                }).ToArray()),
                XPathUnion union => "(" + string.Join(" | ", union.Operands.Select(Print)) + ")",
                XPathFunctionCall call => Functions[(int)call.Function] + "(" + string.Join(", ", call.Arguments.Select(Print)) + ")",
                XPathFilter filter => "filter(" + Print(filter.Primary) + ")" + Predicates(filter.Predicates),
                XPathPath path => PrintPath(path),
                _ => throw new InvalidOperationException(),
            };
        }

        #endregion

        #region Private Methods

        private static string Predicates(XPathAst[] predicates)
        {
            return string.Concat(predicates.Select(p => "[" + Print(p) + "]"));
        }

        private static string PrintPath(XPathPath path)
        {
            var steps = string.Join("/", path.Steps.Select(PrintStep));
            if (path.Input is not null)
            {
                return Print(path.Input) + "/" + steps;
            }

            return path.IsAbsolute ? "/" + steps : steps;
        }

        private static string PrintStep(XPathStep step)
        {
            var test = step.Test.Kind switch
            {
                XPathNodeTestKind.Any => "*",
                XPathNodeTestKind.Comment => "comment()",
                XPathNodeTestKind.Name => "{" + Encoding.UTF8.GetString(step.Test.NamespaceUri) + "}" + Encoding.UTF8.GetString(step.Test.LocalName),
                XPathNodeTestKind.NamespaceWildcard => "{" + Encoding.UTF8.GetString(step.Test.NamespaceUri) + "}*",
                XPathNodeTestKind.Node => "node()",
                XPathNodeTestKind.Text => "text()",
                _ => step.Test.Target is null ? "processing-instruction()" : "processing-instruction('" + Encoding.UTF8.GetString(step.Test.Target) + "')",
            };
            return Axes[(int)step.Axis] + "::" + test + Predicates(step.Predicates);
        }

        private static string Run(XPathAst[] operands, string[] operators)
        {
            var text = new StringBuilder("(").Append(Print(operands[0]));
            for (var i = 0; i < operators.Length; i++)
            {
                text.Append(' ').Append(operators[i]).Append(' ').Append(Print(operands[i + 1]));
            }

            return text.Append(')').ToString();
        }

        #endregion

    }

}
