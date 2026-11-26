using System;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// The exception that is thrown when an XPath expression cannot be compiled, or fails while it is evaluated.
    /// </summary>
    /// <example>
    /// <code>
    /// try
    /// {
    ///     XPathExpression.Compile("/catalog/item[");
    /// }
    /// catch (XPathException ex)
    /// {
    ///     // ex.Message is "Expected an expression." and ex.Position is 14.
    /// }
    /// </code>
    /// </example>
    /// <remarks>
    /// A syntax error, a prefix that <see cref="XPathOptions.Namespaces"/> does not bind, an unknown function and a
    /// call with the wrong number of arguments are thrown by <see cref="XPathExpression.Compile"/>. A variable with
    /// no value, a value of the wrong type where a node-set is required, and a limit are thrown while the expression
    /// is evaluated. The messages are part of the contract.
    /// </remarks>
    public sealed class XPathException : Exception
    {

        #region Properties

        /// <summary>
        /// Gets the zero-based character offset in the expression where the error was found.
        /// </summary>
        /// <value>
        /// The offset of the token or of the part of the expression at fault, which is the length of the expression
        /// when the expression ends too soon; or <see langword="null"/> when the error has no place in it, as for a
        /// limit on the work of an evaluation.
        /// </value>
        public int? Position { get; }

        #endregion

        #region Constructors

        /// <summary>
        /// Creates an exception with a message.
        /// </summary>
        /// <param name="message">The message.</param>
        public XPathException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Creates an exception with a message and the exception that caused it.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
        public XPathException(string message, Exception? innerException)
            : base(message, innerException)
        {
        }

        /// <summary>
        /// Creates an exception with a message and the place in the expression it is about.
        /// </summary>
        /// <param name="message">The message.</param>
        /// <param name="position">The zero-based character offset in the expression, or <see langword="null"/>.</param>
        public XPathException(string message, int? position)
            : base(message)
        {
            Position = position;
        }

        #endregion

    }

}
