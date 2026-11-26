using System;
using System.Collections.Generic;
using System.Linq;
using CloudNimble.Text.Xml;
using CloudNimble.Text.Xml.XPath;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudNimble.XmlEssentials.Tests
{

    /// <summary>
    /// The XPath 1.0 grammar (Recommendation, section 3.7 and the productions of sections 2 to 4): what
    /// <see cref="XPathExpression.Compile"/> accepts, what it expands it to, and the exact error and position of what
    /// it refuses.
    /// </summary>
    [TestClass]
    public class XPathCompilerTests
    {

        #region Fields

        private static readonly XPathOptions Options = new()
        {
            Namespaces = new Dictionary<string, string> { ["a"] = "urn:a", ["p"] = "urn:p" },
        };

        #endregion

        #region Public Methods

        [TestMethod]
        [DataRow("/", "/")]
        [DataRow("/*", "/child::*")]
        [DataRow("//x", "/descendant-or-self::node()/child::{}x")]
        [DataRow("a/b", "child::{}a/child::{}b")]
        [DataRow("a//b", "child::{}a/descendant-or-self::node()/child::{}b")]
        [DataRow(".", "self::node()")]
        [DataRow("..", "parent::node()")]
        [DataRow("../a", "parent::node()/child::{}a")]
        [DataRow("@id", "attribute::{}id")]
        [DataRow("@*", "attribute::*")]
        [DataRow("@a:x", "attribute::{urn:a}x")]
        [DataRow("a:b", "child::{urn:a}b")]
        [DataRow("a:*", "child::{urn:a}*")]
        [DataRow("xml:lang", "child::{http://www.w3.org/XML/1998/namespace}lang")]
        [DataRow("node()", "child::node()")]
        [DataRow("text()", "child::text()")]
        [DataRow("comment()", "child::comment()")]
        [DataRow("processing-instruction()", "child::processing-instruction()")]
        [DataRow("processing-instruction('go')", "child::processing-instruction('go')")]
        [DataRow("processing-instruction ( \"go\" )", "child::processing-instruction('go')")]
        [DataRow("node ()", "child::node()")]
        [DataRow("text", "child::{}text")]
        [DataRow("comment/node", "child::{}comment/child::{}node")]
        [DataRow("ancestor::a", "ancestor::{}a")]
        [DataRow("ancestor-or-self::a", "ancestor-or-self::{}a")]
        [DataRow("attribute::a", "attribute::{}a")]
        [DataRow("child::a", "child::{}a")]
        [DataRow("descendant::a", "descendant::{}a")]
        [DataRow("descendant-or-self::a", "descendant-or-self::{}a")]
        [DataRow("following::a", "following::{}a")]
        [DataRow("following-sibling::a", "following-sibling::{}a")]
        [DataRow("namespace::a", "namespace::{}a")]
        [DataRow("parent::a", "parent::{}a")]
        [DataRow("preceding::a", "preceding::{}a")]
        [DataRow("preceding-sibling::a", "preceding-sibling::{}a")]
        [DataRow("self::a", "self::{}a")]
        [DataRow("child :: a", "child::{}a")]
        [DataRow("child::div", "child::{}div")]
        [DataRow("div", "child::{}div")]
        [DataRow("div div div", "(child::{}div div child::{}div)")]
        [DataRow("mod mod mod", "(child::{}mod mod child::{}mod)")]
        [DataRow("and and and", "(child::{}and and child::{}and)")]
        [DataRow("or or or", "(child::{}or or child::{}or)")]
        [DataRow("* * *", "(child::* * child::*)")]
        [DataRow("a:div div a:mod", "(child::{urn:a}div div child::{urn:a}mod)")]
        [DataRow("a-b", "child::{}a-b")]
        [DataRow("a - b", "(child::{}a - child::{}b)")]
        [DataRow("é/ü", "child::{}é/child::{}ü")]
        [DataRow("1 + 2 * 3", "(1 + (2 * 3))")]
        [DataRow("1 - 2 - 3", "(1 - 2 - 3)")]
        [DataRow("6 div 2 mod 4 * 5", "(6 div 2 mod 4 * 5)")]
        [DataRow("-1", "-(1)")]
        [DataRow("--1", "--(1)")]
        [DataRow("- - -1", "-(1)")]
        [DataRow("-a|b", "-((child::{}a | child::{}b))")]
        [DataRow("1 - -1", "(1 - -(1))")]
        [DataRow("a = b != c", "(child::{}a = child::{}b != child::{}c)")]
        [DataRow("a < b = c", "((child::{}a < child::{}b) = child::{}c)")]
        [DataRow("1 <= 2 >= 3 > 4 < 5", "(1 <= 2 >= 3 > 4 < 5)")]
        [DataRow("a or b and c", "(child::{}a or (child::{}b and child::{}c))")]
        [DataRow("a and b or c and d", "((child::{}a and child::{}b) or (child::{}c and child::{}d))")]
        [DataRow("(a or b) and c", "((child::{}a or child::{}b) and child::{}c)")]
        [DataRow("1 + 2 = 3 and 4 < 5 | 6", "(((1 + 2) = 3) and (4 < (5 | 6)))")]
        [DataRow("$v", "${}v")]
        [DataRow("$a:v", "${urn:a}v")]
        [DataRow("'x'", "'x'")]
        [DataRow("\"it's\"", "'it's'")]
        [DataRow("'😀'", "'😀'")]
        [DataRow("''", "''")]
        [DataRow("12", "12")]
        [DataRow("1.", "1")]
        [DataRow(".5", "0.5")]
        [DataRow("0.25", "0.25")]
        [DataRow("count(a)", "count(child::{}a)")]
        [DataRow("concat('a', 'b', 'c')", "concat('a', 'b', 'c')")]
        [DataRow("true()", "true()")]
        [DataRow("substring('abc', 1, 2)", "substring('abc', 1, 2)")]
        [DataRow("(a)[1]", "filter(child::{}a)[1]")]
        [DataRow("(a)[1]/b", "filter(child::{}a)[1]/child::{}b")]
        [DataRow("(a)//b", "child::{}a/descendant-or-self::node()/child::{}b")]
        [DataRow("$v//b", "${}v/descendant-or-self::node()/child::{}b")]
        [DataRow("$v[2]", "filter(${}v)[2]")]
        [DataRow("id('x')/a", "id('x')/child::{}a")]
        [DataRow("a[1][@b]", "child::{}a[1][attribute::{}b]")]
        [DataRow("a[b[c]]", "child::{}a[child::{}b[child::{}c]]")]
        [DataRow("/ | a", "(/ | child::{}a)")]
        [DataRow(" a / b ", "child::{}a/child::{}b")]
        [DataRow("\ta\r\n|\nb", "(child::{}a | child::{}b)")]
        public void Compile_ValidExpression_ExpandsToItsCanonicalForm(string expression, string expected)
        {
            var compiled = XPathExpression.Compile(expression, Options);
            compiled.Expression.Should().Be(expression);
            XPathAstPrinter.Print(compiled.Ast).Should().Be(expected);
        }

        [TestMethod]
        [DataRow("(", "Expected an expression.", 1)]
        [DataRow("()", "Expected an expression.", 1)]
        [DataRow("a[", "Expected an expression.", 2)]
        [DataRow("a[]", "Expected an expression.", 2)]
        [DataRow("1 +", "Expected an expression.", 3)]
        [DataRow("count(1,)", "Expected an expression.", 8)]
        [DataRow(")", "Expected an expression.", 0)]
        [DataRow("a | -1", "Expected an expression.", 4)]
        [DataRow("a[1", "Expected ']'.", 3)]
        [DataRow("(1", "Expected ')'.", 2)]
        [DataRow("count(1", "Expected ')'.", 7)]
        [DataRow("count(1 2)", "Expected ')'.", 8)]
        [DataRow("processing-instruction(1)", "Expected ')'.", 23)]
        [DataRow("text(1)", "Expected ')'.", 5)]
        [DataRow("text('x')", "Expected ')'.", 5)]
        [DataRow("/r/", "Expected a location step.", 3)]
        [DataRow("//", "Expected a location step.", 2)]
        [DataRow("a//", "Expected a location step.", 3)]
        [DataRow("(a)/", "Expected a location step.", 4)]
        [DataRow("a/1", "Expected a location step.", 2)]
        [DataRow("@", "Expected a node test.", 1)]
        [DataRow("child::", "Expected a node test.", 7)]
        [DataRow("child::1", "Expected a node test.", 7)]
        [DataRow("child::.", "Expected a node test.", 7)]
        [DataRow("foo::a", "Unknown axis 'foo'.", 0)]
        [DataRow("a:child::b", "Unknown axis 'a:child'.", 0)]
        [DataRow("1 2", "Unexpected '2'.", 2)]
        [DataRow("a b", "Unexpected 'b'.", 2)]
        [DataRow(".[1]", "Unexpected '['.", 1)]
        [DataRow("..[1]", "Unexpected '['.", 2)]
        [DataRow("child::foo()", "Unexpected '('.", 10)]
        [DataRow("1e2", "Unexpected 'e2'.", 1)]
        [DataRow("1 'x'", "Unexpected ''x''.", 2)]
        [DataRow("if (true()) then 1 else 2", "Unexpected 'then'.", 12)]
        [DataRow("unknown() 1", "Unexpected '1'.", 10)]
        [DataRow("'abc", "The string literal is not closed.", 0)]
        [DataRow("\"abc'", "The string literal is not closed.", 0)]
        [DataRow("#", "Unexpected character '#'.", 0)]
        [DataRow("a!b", "Unexpected character '!'.", 1)]
        [DataRow(":a", "Unexpected character ':'.", 0)]
        [DataRow("a:", "Unexpected character ':'.", 1)]
        [DataRow("1 ; 2", "Unexpected character ';'.", 2)]
        [DataRow("\u00A7", "Unexpected character '\u00A7'.", 0)]
        [DataRow("a:1", "A prefix must be followed by a local name or '*'.", 2)]
        [DataRow("a:-b", "A prefix must be followed by a local name or '*'.", 2)]
        [DataRow("'\u0001'", "The expression contains U+0001, which is not an XML character.", 1)]
        [DataRow("'x\uFFFE'", "The expression contains U+FFFE, which is not an XML character.", 2)]
        [DataRow("\u0007", "The expression contains U+0007, which is not an XML character.", 0)]
        [DataRow("$", "Expected a variable name after '$'.", 1)]
        [DataRow("$ v", "Expected a variable name after '$'.", 1)]
        [DataRow("$1", "Expected a variable name after '$'.", 1)]
        [DataRow("$a:*", "Expected a variable name after '$'.", 1)]
        [DataRow("q:x", "The prefix 'q' is not bound in XPathOptions.Namespaces.", 0)]
        [DataRow("q:*", "The prefix 'q' is not bound in XPathOptions.Namespaces.", 0)]
        [DataRow("@q:x", "The prefix 'q' is not bound in XPathOptions.Namespaces.", 1)]
        [DataRow("$q:v", "The prefix 'q' is not bound in XPathOptions.Namespaces.", 1)]
        [DataRow("q:f()", "The prefix 'q' is not bound in XPathOptions.Namespaces.", 0)]
        [DataRow("a | q:x | r:y", "The prefix 'q' is not bound in XPathOptions.Namespaces.", 4)]
        [DataRow("a:f()", "The function 'a:f' is not an XPath 1.0 core function.", 0)]
        [DataRow("unknown()", "The function 'unknown' is not an XPath 1.0 core function.", 0)]
        [DataRow("1 + unknown(count())", "The function 'unknown' is not an XPath 1.0 core function.", 4)]
        [DataRow("count()", "The function 'count' takes 1 argument.", 0)]
        [DataRow("true(1)", "The function 'true' takes no arguments.", 0)]
        [DataRow("concat('a')", "The function 'concat' takes at least 2 arguments.", 0)]
        [DataRow("substring('a')", "The function 'substring' takes 2 or 3 arguments.", 0)]
        [DataRow("name(a, b)", "The function 'name' takes at most 1 argument.", 0)]
        [DataRow("translate(1, 2)", "The function 'translate' takes 3 arguments.", 0)]
        [DataRow("starts-with(1)", "The function 'starts-with' takes 2 arguments.", 0)]
        public void Compile_InvalidExpression_ThrowsWithItsPosition(string expression, string message, int position)
        {
            var act = () => XPathExpression.Compile(expression, Options);
            var thrown = act.Should().Throw<XPathException>().Which;
            thrown.Message.Should().Be(message);
            thrown.Position.Should().Be(position);
        }

        [TestMethod]
        public void Compile_UnpairedSurrogate_IsNotAnXmlCharacter()
        {
            var name = () => XPathExpression.Compile(new string('\uD800', 1));
            var thrown = name.Should().Throw<XPathException>().Which;
            thrown.Message.Should().Be("The expression contains U+D800, which is not an XML character.");
            thrown.Position.Should().Be(0);
            var literal = () => XPathExpression.Compile("'a" + new string('\uDC00', 1) + "'");
            thrown = literal.Should().Throw<XPathException>().Which;
            thrown.Message.Should().Be("The expression contains U+DC00, which is not an XML character.");
            thrown.Position.Should().Be(2);
        }

        [TestMethod]
        public void Compile_EveryFunction_AcceptsItsArity()
        {
            string[] calls =
            [
                "last()", "position()", "count(a)", "id(1)", "local-name()", "local-name(a)", "namespace-uri()",
                "namespace-uri(a)", "name()", "name(a)", "string()", "string(1)", "concat(1, 2)", "concat(1, 2, 3, 4)",
                "starts-with(1, 2)", "contains(1, 2)", "substring-before(1, 2)", "substring-after(1, 2)", "substring(1, 2)",
                "substring(1, 2, 3)", "string-length()", "string-length(1)", "normalize-space()", "normalize-space(1)",
                "translate(1, 2, 3)", "boolean(1)", "not(1)", "true()", "false()", "lang(1)", "number()", "number(1)",
                "sum(a)", "floor(1)", "ceiling(1)", "round(1)",
            ];
            calls.Select(call => XPathAstPrinter.Print(XPathExpression.Compile(call).Ast)).Should().Equal(
                "last()", "position()", "count(child::{}a)", "id(1)", "local-name()", "local-name(child::{}a)", "namespace-uri()",
                "namespace-uri(child::{}a)", "name()", "name(child::{}a)", "string()", "string(1)", "concat(1, 2)", "concat(1, 2, 3, 4)",
                "starts-with(1, 2)", "contains(1, 2)", "substring-before(1, 2)", "substring-after(1, 2)", "substring(1, 2)",
                "substring(1, 2, 3)", "string-length()", "string-length(1)", "normalize-space()", "normalize-space(1)",
                "translate(1, 2, 3)", "boolean(1)", "not(1)", "true()", "false()", "lang(1)", "number()", "number(1)",
                "sum(child::{}a)", "floor(1)", "ceiling(1)", "round(1)");
        }

        [TestMethod]
        public void Compile_NumberTooLargeForADouble_IsInfinity()
        {
            var compiled = XPathExpression.Compile("1" + new string('0', 400));
            ((XPathNumber)compiled.Ast).Value.Should().Be(double.PositiveInfinity);
        }

        [TestMethod]
        public void Compile_ManyDigits_RoundsToTheNearestDouble()
        {
            ((XPathNumber)XPathExpression.Compile("0.1000000000000000055511151231257827021181583404541015625").Ast).Value.Should().Be(0.1);
            ((XPathNumber)XPathExpression.Compile("9007199254740993").Ast).Value.Should().Be(9007199254740992d);
        }

        [TestMethod]
        public void Compile_LongRunsOfOperators_AddNoDepth()
        {
            var sum = string.Join(" + ", Enumerable.Repeat("1", 10_000));
            ((XPathArithmetic)XPathExpression.Compile(sum).Ast).Operands.Should().HaveCount(10_000);
            var or = string.Join(" or ", Enumerable.Repeat("a", 10_000));
            ((XPathLogical)XPathExpression.Compile(or).Ast).Operands.Should().HaveCount(10_000);
            XPathAstPrinter.Print(XPathExpression.Compile(new string('-', 10_001) + "1").Ast).Should().Be("-(1)");
            var path = string.Join("/", Enumerable.Repeat("a", 10_000));
            ((XPathPath)XPathExpression.Compile(path).Ast).Steps.Should().HaveCount(10_000);
        }

        [TestMethod]
        public void Compile_DefaultMaxDepth_Is64()
        {
            XPathAstPrinter.Print(XPathExpression.Compile(new string('(', 63) + "1" + new string(')', 63)).Ast).Should().Be("1");
            var act = () => XPathExpression.Compile(new string('(', 64) + "1" + new string(')', 64));
            var thrown = act.Should().Throw<XPathException>().Which;
            thrown.Message.Should().Be("MaxDepth exceeded (64).");
            thrown.Position.Should().Be(63);
        }

        [TestMethod]
        [DataRow("((1))", 1)]
        [DataRow("a[b[c]]", 3)]
        [DataRow("count(count(a))", 12)]
        [DataRow("(a)[(1)]", 4)]
        public void Compile_DeeperThanMaxDepth_Throws(string expression, int position)
        {
            var act = () => XPathExpression.Compile(expression, new XPathOptions { MaxDepth = 2 });
            var thrown = act.Should().Throw<XPathException>().Which;
            thrown.Message.Should().Be("MaxDepth exceeded (2).");
            thrown.Position.Should().Be(position);
        }

        [TestMethod]
        public void Compile_MaxDepthOne_AllowsOnlyAFlatExpression()
        {
            XPathAstPrinter.Print(XPathExpression.Compile("-a/b | c + 2", new XPathOptions { MaxDepth = 1 }).Ast).Should().Be("(-((child::{}a/child::{}b | child::{}c)) + 2)");
            var act = () => XPathExpression.Compile("a[1]", new XPathOptions { MaxDepth = 1 });
            act.Should().Throw<XPathException>().Which.Message.Should().Be("MaxDepth exceeded (1).");
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" \t")]
        public void Compile_NoExpression_ThrowsArgumentException(string? expression)
        {
            var act = () => XPathExpression.Compile(expression!);
            act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be("expression");
        }

        [TestMethod]
        [DataRow("xmlns", "http://www.w3.org/2000/xmlns/", "The prefix 'xmlns' cannot be bound in XPathOptions.Namespaces. (Parameter 'options')")]
        [DataRow("xml", "urn:other", "The prefix 'xml' can only be bound to http://www.w3.org/XML/1998/namespace in XPathOptions.Namespaces. (Parameter 'options')")]
        [DataRow("p", "", "The prefix 'p' is bound to an empty namespace URI in XPathOptions.Namespaces. (Parameter 'options')")]
        public void Compile_InvalidNamespaces_ThrowsArgumentException(string prefix, string uri, string message)
        {
            var options = new XPathOptions { Namespaces = new Dictionary<string, string> { [prefix] = uri } };
            var act = () => XPathExpression.Compile("a", options);
            act.Should().Throw<ArgumentException>().Which.Message.Should().Be(message);
        }

        [TestMethod]
        public void Compile_XmlBoundToItsOwnNamespace_AndAnEmptyPrefix_AreAccepted()
        {
            var options = new XPathOptions
            {
                Namespaces = new Dictionary<string, string> { ["xml"] = "http://www.w3.org/XML/1998/namespace", [""] = "urn:default" },
            };
            XPathAstPrinter.Print(XPathExpression.Compile("a/@xml:lang", options).Ast).Should().Be("child::{}a/attribute::{http://www.w3.org/XML/1998/namespace}lang");
        }

        [TestMethod]
        public void Compile_DiscoveredNamespaces_BindThePrefixesOfTheDocument()
        {
            var options = new XPathOptions { Namespaces = XmlDocument.DiscoverNamespaces("<f xmlns=\"urn:atom\" xmlns:m=\"urn:media\"><m:t/></f>") };
            XPathAstPrinter.Print(XPathExpression.Compile("f/m:t", options).Ast).Should().Be("child::{}f/child::{urn:media}t");
        }

        [TestMethod]
        public void Compile_CopiesTheOptions()
        {
            var namespaces = new Dictionary<string, string> { ["a"] = "urn:a" };
            var options = new XPathOptions { Namespaces = namespaces };
            var compiled = XPathExpression.Compile("a:x", options);
            namespaces["a"] = "urn:changed";
            options.Namespaces = null;
            XPathAstPrinter.Print(compiled.Ast).Should().Be("child::{urn:a}x");
        }

        [TestMethod]
        public void Options_NegativeLimits_AreRefused()
        {
            var options = new XPathOptions();
            var depth = () => options.MaxDepth = -1;
            depth.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("value");
            var operations = () => options.MaxOperations = -1;
            operations.Should().Throw<ArgumentOutOfRangeException>().Which.ParamName.Should().Be("value");
            options.MaxDepth.Should().Be(0);
            options.MaxOperations.Should().Be(0);
        }

        #endregion

    }

}
