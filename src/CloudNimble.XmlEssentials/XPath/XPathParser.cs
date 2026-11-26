using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// Parses an XPath 1.0 expression into an <see cref="XPathAst"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A recursive-descent parser with one method for each level of precedence (Recommendation, section 3). Each
    /// level returns its operand alone when it has no operator, and a run of operators at one level is one node, so a
    /// long run costs no depth. <c>*</c>, <c>and</c>, <c>or</c>, <c>div</c> and <c>mod</c> are operators only where an
    /// operator can follow an operand; anywhere else they are name tests. A name followed by <c>::</c> is an axis,
    /// and one followed by <c>(</c> is a function unless it names a node type.
    /// </para>
    /// <para>
    /// The whole expression is checked for syntax before a name is reported: a prefix that is not bound, an unknown
    /// function or a call with the wrong number of arguments is kept until the end, so that a syntax error anywhere is
    /// the one reported.
    /// </para>
    /// <para>
    /// Depth is counted by nesting: the whole expression is at depth 1, and an expression in parentheses, an argument
    /// and a predicate are one deeper than the expression they are in.
    /// </para>
    /// </remarks>
    internal sealed class XPathParser
    {

        #region Fields

        /// <summary>
        /// The 13 axes by name, looked up with the name as a span of the expression.
        /// </summary>
        private static readonly FrozenDictionary<string, XPathAxis>.AlternateLookup<ReadOnlySpan<char>> Axes = new Dictionary<string, XPathAxis>
        {
            ["ancestor"] = XPathAxis.Ancestor,
            ["ancestor-or-self"] = XPathAxis.AncestorOrSelf,
            ["attribute"] = XPathAxis.Attribute,
            ["child"] = XPathAxis.Child,
            ["descendant"] = XPathAxis.Descendant,
            ["descendant-or-self"] = XPathAxis.DescendantOrSelf,
            ["following"] = XPathAxis.Following,
            ["following-sibling"] = XPathAxis.FollowingSibling,
            ["namespace"] = XPathAxis.Namespace,
            ["parent"] = XPathAxis.Parent,
            ["preceding"] = XPathAxis.Preceding,
            ["preceding-sibling"] = XPathAxis.PrecedingSibling,
            ["self"] = XPathAxis.Self,
        }.ToFrozenDictionary(StringComparer.Ordinal).GetAlternateLookup<ReadOnlySpan<char>>();

        private static readonly XPathStep DescendantOrSelf = new(XPathAxis.DescendantOrSelf, XPathNodeTest.AnyNode, []);

        private XPathToken _current;
        private string? _deferredMessage;
        private int _deferredPosition;
        private XPathLexer _lexer;
        private readonly int _maxDepth;
        private readonly IReadOnlyDictionary<string, string> _namespaces;
        private XPathToken _next;
        private readonly string _source;

        #endregion

        #region Constructors

        /// <summary>
        /// Creates a parser for an expression.
        /// </summary>
        /// <param name="source">The expression.</param>
        /// <param name="namespaces">The prefixes the expression may use, with their namespace URIs.</param>
        /// <param name="maxDepth">The deepest nesting allowed.</param>
        private XPathParser(string source, IReadOnlyDictionary<string, string> namespaces, int maxDepth)
        {
            _source = source;
            _namespaces = namespaces;
            _maxDepth = maxDepth;
            _lexer = new XPathLexer(source);
            _current = _lexer.Next();
            _next = _lexer.Next();
        }

        #endregion

        #region Internal Methods

        /// <summary>
        /// Parses an expression.
        /// </summary>
        /// <param name="source">The expression.</param>
        /// <param name="namespaces">The prefixes the expression may use, with their namespace URIs.</param>
        /// <param name="maxDepth">The deepest nesting allowed, at least 1.</param>
        /// <returns>The compiled expression.</returns>
        /// <exception cref="XPathException">The expression is not an XPath 1.0 expression that these names allow.</exception>
        internal static XPathAst Parse(string source, IReadOnlyDictionary<string, string> namespaces, int maxDepth)
        {
            var parser = new XPathParser(source, namespaces, maxDepth);
            var ast = parser.ParseExpression(1, 0);
            if (parser._current.Kind is not XPathTokenKind.End)
            {
                throw XPathLexer.Error($"Unexpected '{parser.Text(parser._current)}'.", parser._current.Start);
            }

            if (parser._deferredMessage is not null)
            {
                throw XPathLexer.Error(parser._deferredMessage, parser._deferredPosition);
            }

            return ast;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Moves to the next token.
        /// </summary>
        private void Advance()
        {
            _current = _next;
            _next = _lexer.Next();
        }

        /// <summary>
        /// Returns the axis an axis name names.
        /// </summary>
        /// <param name="name">The name before <c>::</c>.</param>
        /// <returns>The axis, or <see langword="null"/> for a name that is not one of the 13.</returns>
        private static XPathAxis? AxisNamed(ReadOnlySpan<char> name)
        {
            return Axes.TryGetValue(name, out var axis) ? axis : null;
        }

        /// <summary>
        /// Keeps the first error about a name, to report once the syntax of the whole expression is known to be right.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="position">The offset.</param>
        private void Defer(string message, int position)
        {
            if (_deferredMessage is null)
            {
                _deferredMessage = message;
                _deferredPosition = position;
            }
        }

        /// <summary>
        /// Moves past a token of a kind, or fails.
        /// </summary>
        /// <param name="kind">The kind that must come next.</param>
        /// <param name="message">The message when it does not.</param>
        private void Expect(XPathTokenKind kind, string message)
        {
            if (_current.Kind != kind)
            {
                throw XPathLexer.Error(message, _current.Start);
            }

            Advance();
        }

        /// <summary>
        /// Returns whether the current token is an operator name: <c>and</c>, <c>or</c>, <c>div</c> or <c>mod</c>.
        /// </summary>
        /// <param name="name">The operator name.</param>
        /// <returns><see langword="true"/> when the current token is that name, unprefixed.</returns>
        private bool IsOperator(string name)
        {
            return _current.Kind is XPathTokenKind.Name && Text(_current).SequenceEqual(name);
        }

        /// <summary>
        /// Returns whether the current token starts a location step.
        /// </summary>
        /// <returns><see langword="true"/> for <c>.</c>, <c>..</c>, <c>@</c>, <c>*</c>, <c>p:*</c>, an axis name, a node type, and a name that is not a function name.</returns>
        private bool IsStepStart()
        {
            return _current.Kind switch
            {
                XPathTokenKind.Dot or XPathTokenKind.DotDot or XPathTokenKind.At or XPathTokenKind.Star or XPathTokenKind.NamespaceWildcard => true,
                XPathTokenKind.Name => _next.Kind is not XPathTokenKind.OpenParenthesis || NodeType(_current) is not null,
                _ => false,
            };
        }

        /// <summary>
        /// Returns the text of a string literal without its quotes, as UTF-8.
        /// </summary>
        private byte[] LiteralValue(XPathToken literal)
        {
            return Encoding.UTF8.GetBytes(_source, literal.Start + 1, literal.Length - 2);
        }

        /// <summary>
        /// Returns the local part of a name.
        /// </summary>
        private ReadOnlySpan<char> LocalPart(XPathToken name)
        {
            return name.Colon < 0 ? Text(name) : _source.AsSpan(name.Colon + 1, name.Start + name.Length - name.Colon - 1);
        }

        /// <summary>
        /// Returns the multiplicative operator the current token is, after an operand.
        /// </summary>
        private XPathArithmeticOperator? MultiplicativeOperator()
        {
            if (_current.Kind is XPathTokenKind.Star)
            {
                return XPathArithmeticOperator.Multiply;
            }

            if (IsOperator("div"))
            {
                return XPathArithmeticOperator.Divide;
            }

            return IsOperator("mod") ? XPathArithmeticOperator.Modulo : null;
        }

        /// <summary>
        /// Returns the node type a name names, when it is followed by <c>(</c>.
        /// </summary>
        /// <param name="token">A name.</param>
        /// <returns>The kind of test, or <see langword="null"/> for a name that is not a node type.</returns>
        private XPathNodeTestKind? NodeType(XPathToken token)
        {
            return Text(token) switch
            {
                "node" => XPathNodeTestKind.Node,
                "text" => XPathNodeTestKind.Text,
                "comment" => XPathNodeTestKind.Comment,
                "processing-instruction" => XPathNodeTestKind.ProcessingInstruction,
                _ => null,
            };
        }

        /// <summary>
        /// AdditiveExpr ::= MultiplicativeExpr (('+' | '-') MultiplicativeExpr)*
        /// </summary>
        private XPathAst ParseAdditive(int depth)
        {
            var first = ParseMultiplicative(depth);
            if (_current.Kind is not (XPathTokenKind.Plus or XPathTokenKind.Minus))
            {
                return first;
            }

            List<XPathAst> operands = [first];
            List<XPathArithmeticOperator> operators = [];
            while (_current.Kind is XPathTokenKind.Plus or XPathTokenKind.Minus)
            {
                operators.Add(_current.Kind is XPathTokenKind.Plus ? XPathArithmeticOperator.Add : XPathArithmeticOperator.Subtract);
                Advance();
                operands.Add(ParseMultiplicative(depth));
            }

            return new XPathArithmetic([.. operands], [.. operators]);
        }

        /// <summary>
        /// AndExpr ::= EqualityExpr ('and' EqualityExpr)*
        /// </summary>
        private XPathAst ParseAnd(int depth)
        {
            var first = ParseEquality(depth);
            if (!IsOperator("and"))
            {
                return first;
            }

            List<XPathAst> operands = [first];
            while (IsOperator("and"))
            {
                Advance();
                operands.Add(ParseEquality(depth));
            }

            return new XPathLogical(false, [.. operands]);
        }

        /// <summary>
        /// EqualityExpr ::= RelationalExpr (('=' | '!=') RelationalExpr)*
        /// </summary>
        private XPathAst ParseEquality(int depth)
        {
            var first = ParseRelational(depth);
            if (_current.Kind is not (XPathTokenKind.Equal or XPathTokenKind.NotEqual))
            {
                return first;
            }

            List<XPathAst> operands = [first];
            List<XPathComparisonOperator> operators = [];
            while (_current.Kind is XPathTokenKind.Equal or XPathTokenKind.NotEqual)
            {
                operators.Add(_current.Kind is XPathTokenKind.Equal ? XPathComparisonOperator.Equal : XPathComparisonOperator.NotEqual);
                Advance();
                operands.Add(ParseRelational(depth));
            }

            return new XPathComparison([.. operands], [.. operators]);
        }

        /// <summary>
        /// Expr ::= OrExpr, at a depth of nesting.
        /// </summary>
        /// <param name="depth">The depth of this expression.</param>
        /// <param name="position">The offset of what opens it, for the error when it is too deep.</param>
        private XPathAst ParseExpression(int depth, int position)
        {
            if (depth > _maxDepth)
            {
                throw XPathLexer.Error(string.Create(CultureInfo.InvariantCulture, $"{nameof(XPathOptions.MaxDepth)} exceeded ({_maxDepth})."), position);
            }

            RuntimeHelpers.EnsureSufficientExecutionStack();
            return ParseOr(depth);
        }

        /// <summary>
        /// FilterExpr ::= PrimaryExpr Predicate*
        /// </summary>
        private XPathAst ParseFilter(int depth)
        {
            var primary = ParsePrimary(depth);
            var predicates = ParsePredicates(depth);
            return predicates.Length == 0 ? primary : new XPathFilter(primary, predicates);
        }

        /// <summary>
        /// FunctionCall ::= FunctionName '(' ( Argument ( ',' Argument )* )? ')'
        /// </summary>
        private XPathAst ParseFunctionCall(int depth)
        {
            var name = _current;
            var text = Text(name);
            var function = XPathFunction.Last;
            var known = name.Colon < 0 && XPathFunctions.TryFind(text, out function);
            if (name.Colon >= 0)
            {
                ResolvePrefix(name);
            }

            if (!known)
            {
                Defer($"The function '{text}' is not an XPath 1.0 core function.", name.Start);
            }

            // The name and the parenthesis after it.
            Advance();
            Advance();
            List<XPathAst> arguments = [];
            if (_current.Kind is not XPathTokenKind.CloseParenthesis)
            {
                arguments.Add(ParseExpression(depth + 1, _current.Start));
                while (_current.Kind is XPathTokenKind.Comma)
                {
                    Advance();
                    arguments.Add(ParseExpression(depth + 1, _current.Start));
                }
            }

            Expect(XPathTokenKind.CloseParenthesis, "Expected ')'.");
            if (known && !XPathFunctions.Accepts(function, arguments.Count))
            {
                Defer($"The function '{text}' takes {XPathFunctions.Arity(function)}.", name.Start);
            }

            return new XPathFunctionCall(function, [.. arguments], name.Start);
        }

        /// <summary>
        /// MultiplicativeExpr ::= UnaryExpr (('*' | 'div' | 'mod') UnaryExpr)*
        /// </summary>
        private XPathAst ParseMultiplicative(int depth)
        {
            var first = ParseUnary(depth);
            var op = MultiplicativeOperator();
            if (op is null)
            {
                return first;
            }

            List<XPathAst> operands = [first];
            List<XPathArithmeticOperator> operators = [];
            while (op is { } found)
            {
                operators.Add(found);
                Advance();
                operands.Add(ParseUnary(depth));
                op = MultiplicativeOperator();
            }

            return new XPathArithmetic([.. operands], [.. operators]);
        }

        /// <summary>
        /// NodeTest ::= NameTest | NodeType '(' ')' | 'processing-instruction' '(' Literal ')'
        /// </summary>
        private XPathNodeTest ParseNodeTest()
        {
            switch (_current.Kind)
            {
                case XPathTokenKind.Star:
                    Advance();
                    return new XPathNodeTest(XPathNodeTestKind.Any, [], [], null);
                case XPathTokenKind.NamespaceWildcard:
                    var uri = ResolvePrefix(_current);
                    Advance();
                    return new XPathNodeTest(XPathNodeTestKind.NamespaceWildcard, [], Encoding.UTF8.GetBytes(uri), null);
                case XPathTokenKind.Name:
                    if (_next.Kind is XPathTokenKind.OpenParenthesis && NodeType(_current) is { } kind)
                    {
                        Advance();
                        Advance();
                        byte[]? target = null;
                        if (kind is XPathNodeTestKind.ProcessingInstruction && _current.Kind is XPathTokenKind.Literal)
                        {
                            target = LiteralValue(_current);
                            Advance();
                        }

                        Expect(XPathTokenKind.CloseParenthesis, "Expected ')'.");
                        return new XPathNodeTest(kind, [], [], target);
                    }

                    var name = _current;
                    var namespaceUri = name.Colon < 0 ? string.Empty : ResolvePrefix(name);
                    Advance();
                    return new XPathNodeTest(XPathNodeTestKind.Name, Encoding.UTF8.GetBytes(LocalPart(name).ToString()), Encoding.UTF8.GetBytes(namespaceUri), null);
                default:
                    throw XPathLexer.Error("Expected a node test.", _current.Start);
            }
        }

        /// <summary>
        /// OrExpr ::= AndExpr ('or' AndExpr)*
        /// </summary>
        private XPathAst ParseOr(int depth)
        {
            var first = ParseAnd(depth);
            if (!IsOperator("or"))
            {
                return first;
            }

            List<XPathAst> operands = [first];
            while (IsOperator("or"))
            {
                Advance();
                operands.Add(ParseAnd(depth));
            }

            return new XPathLogical(true, [.. operands]);
        }

        /// <summary>
        /// PathExpr ::= LocationPath | FilterExpr | FilterExpr ('/' | '//') RelativeLocationPath
        /// </summary>
        private XPathAst ParsePath(int depth)
        {
            var start = _current.Start;
            List<XPathStep> steps = [];
            switch (_current.Kind)
            {
                case XPathTokenKind.Slash:
                    Advance();
                    if (IsStepStart())
                    {
                        ParseRelative(depth, steps);
                    }

                    return new XPathPath(true, null, [.. steps], start);
                case XPathTokenKind.DoubleSlash:
                    Advance();
                    steps.Add(DescendantOrSelf);
                    ParseRelative(depth, steps);
                    return new XPathPath(true, null, [.. steps], start);
            }

            if (IsStepStart())
            {
                ParseRelative(depth, steps);
                return new XPathPath(false, null, [.. steps], start);
            }

            var filter = ParseFilter(depth);
            if (_current.Kind is not (XPathTokenKind.Slash or XPathTokenKind.DoubleSlash))
            {
                return filter;
            }

            if (_current.Kind is XPathTokenKind.DoubleSlash)
            {
                steps.Add(DescendantOrSelf);
            }

            Advance();
            ParseRelative(depth, steps);
            return new XPathPath(false, filter, [.. steps], start);
        }

        /// <summary>
        /// Predicate*, where Predicate ::= '[' Expr ']'
        /// </summary>
        private XPathAst[] ParsePredicates(int depth)
        {
            if (_current.Kind is not XPathTokenKind.OpenBracket)
            {
                return [];
            }

            List<XPathAst> predicates = [];
            while (_current.Kind is XPathTokenKind.OpenBracket)
            {
                var position = _current.Start;
                Advance();
                predicates.Add(ParseExpression(depth + 1, position));
                Expect(XPathTokenKind.CloseBracket, "Expected ']'.");
            }

            return [.. predicates];
        }

        /// <summary>
        /// PrimaryExpr ::= VariableReference | '(' Expr ')' | Literal | Number | FunctionCall
        /// </summary>
        private XPathAst ParsePrimary(int depth)
        {
            var start = _current.Start;
            switch (_current.Kind)
            {
                case XPathTokenKind.Dollar:
                    Advance();
                    if (_current.Kind is not XPathTokenKind.Name || _current.Start != start + 1)
                    {
                        throw XPathLexer.Error("Expected a variable name after '$'.", start + 1);
                    }

                    var name = _current;
                    var namespaceUri = name.Colon < 0 ? string.Empty : ResolvePrefix(name);
                    Advance();
                    return new XPathVariableReference(LocalPart(name).ToString(), namespaceUri, Text(name).ToString(), start);
                case XPathTokenKind.OpenParenthesis:
                    Advance();
                    var inner = ParseExpression(depth + 1, start);
                    Expect(XPathTokenKind.CloseParenthesis, "Expected ')'.");
                    return inner;
                case XPathTokenKind.Literal:
                    var literal = LiteralValue(_current);
                    Advance();
                    return new XPathLiteral(literal, start);
                case XPathTokenKind.Number:
                    var number = double.Parse(Text(_current), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
                    Advance();
                    return new XPathNumber(number, start);
                case XPathTokenKind.Name:
                    // A name that does not start a step is followed by a parenthesis and is not a node type.
                    return ParseFunctionCall(depth);
                default:
                    throw XPathLexer.Error("Expected an expression.", start);
            }
        }

        /// <summary>
        /// RelationalExpr ::= AdditiveExpr (('&lt;' | '&gt;' | '&lt;=' | '&gt;=') AdditiveExpr)*
        /// </summary>
        private XPathAst ParseRelational(int depth)
        {
            var first = ParseAdditive(depth);
            if (RelationalOperator() is null)
            {
                return first;
            }

            List<XPathAst> operands = [first];
            List<XPathComparisonOperator> operators = [];
            while (RelationalOperator() is { } op)
            {
                operators.Add(op);
                Advance();
                operands.Add(ParseAdditive(depth));
            }

            return new XPathComparison([.. operands], [.. operators]);
        }

        /// <summary>
        /// RelativeLocationPath ::= Step (('/' | '//') Step)*, appended to <paramref name="steps"/>.
        /// </summary>
        private void ParseRelative(int depth, List<XPathStep> steps)
        {
            steps.Add(ParseStep(depth));
            while (_current.Kind is XPathTokenKind.Slash or XPathTokenKind.DoubleSlash)
            {
                if (_current.Kind is XPathTokenKind.DoubleSlash)
                {
                    steps.Add(DescendantOrSelf);
                }

                Advance();
                steps.Add(ParseStep(depth));
            }
        }

        /// <summary>
        /// Step ::= AxisSpecifier NodeTest Predicate* | '.' | '..'
        /// </summary>
        private XPathStep ParseStep(int depth)
        {
            if (!IsStepStart())
            {
                throw XPathLexer.Error("Expected a location step.", _current.Start);
            }

            switch (_current.Kind)
            {
                case XPathTokenKind.Dot:
                    Advance();
                    return new XPathStep(XPathAxis.Self, XPathNodeTest.AnyNode, []);
                case XPathTokenKind.DotDot:
                    Advance();
                    return new XPathStep(XPathAxis.Parent, XPathNodeTest.AnyNode, []);
            }

            var axis = XPathAxis.Child;
            if (_current.Kind is XPathTokenKind.At)
            {
                axis = XPathAxis.Attribute;
                Advance();
            }
            else if (_current.Kind is XPathTokenKind.Name && _next.Kind is XPathTokenKind.ColonColon)
            {
                axis = AxisNamed(Text(_current)) ?? throw XPathLexer.Error($"Unknown axis '{Text(_current)}'.", _current.Start);
                Advance();
                Advance();
            }

            var test = ParseNodeTest();
            return new XPathStep(axis, test, ParsePredicates(depth));
        }

        /// <summary>
        /// UnaryExpr ::= UnionExpr | '-' UnaryExpr
        /// </summary>
        private XPathAst ParseUnary(int depth)
        {
            var start = _current.Start;
            var signs = 0;
            while (_current.Kind is XPathTokenKind.Minus)
            {
                signs++;
                Advance();
            }

            var operand = ParseUnion(depth);
            return signs == 0 ? operand : new XPathNegation(operand, (signs & 1) == 1, start);
        }

        /// <summary>
        /// UnionExpr ::= PathExpr ('|' PathExpr)*
        /// </summary>
        private XPathAst ParseUnion(int depth)
        {
            var first = ParsePath(depth);
            if (_current.Kind is not XPathTokenKind.Pipe)
            {
                return first;
            }

            List<XPathAst> operands = [first];
            while (_current.Kind is XPathTokenKind.Pipe)
            {
                Advance();
                operands.Add(ParsePath(depth));
            }

            return new XPathUnion([.. operands]);
        }

        /// <summary>
        /// Returns the relational operator the current token is.
        /// </summary>
        private XPathComparisonOperator? RelationalOperator()
        {
            return _current.Kind switch
            {
                XPathTokenKind.Less => XPathComparisonOperator.Less,
                XPathTokenKind.LessOrEqual => XPathComparisonOperator.LessOrEqual,
                XPathTokenKind.Greater => XPathComparisonOperator.Greater,
                XPathTokenKind.GreaterOrEqual => XPathComparisonOperator.GreaterOrEqual,
                _ => null,
            };
        }

        /// <summary>
        /// Returns the namespace URI the prefix of a name is bound to.
        /// </summary>
        /// <param name="name">A name with a prefix, or a namespace wildcard.</param>
        /// <returns>The namespace URI, or empty when the prefix is not bound, which is reported at the end.</returns>
        private string ResolvePrefix(XPathToken name)
        {
            var prefix = _source.Substring(name.Start, name.Colon - name.Start);
            if (_namespaces.TryGetValue(prefix, out var uri))
            {
                return uri;
            }

            Defer($"The prefix '{prefix}' is not bound in {nameof(XPathOptions)}.{nameof(XPathOptions.Namespaces)}.", name.Start);
            return string.Empty;
        }

        /// <summary>
        /// Returns the text of a token.
        /// </summary>
        private ReadOnlySpan<char> Text(XPathToken token)
        {
            return _source.AsSpan(token.Start, token.Length);
        }

        #endregion

    }

}
