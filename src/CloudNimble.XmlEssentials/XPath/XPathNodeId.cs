using System.Runtime.CompilerServices;

namespace CloudNimble.Text.Xml.XPath
{

    /// <summary>
    /// Makes and reads the 64-bit ids that stand for the nodes of an <see cref="XmlDocument"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The high 32 bits hold the row index plus one, or 0 for the document node. The low 32 bits are 0 for the
    /// node the row is, 1 for the <c>xml</c> namespace node of the element on the row, and <c>d + 2</c> for the
    /// namespace node of that element bound by the declaration at row <c>d</c>.
    /// </para>
    /// <para>
    /// Ids compare as document order: an element is before its namespace nodes, which are before its attributes
    /// (the rows after it), which are before its children. A node-set is therefore an ascending array of ids.
    /// </para>
    /// </remarks>
    internal static class XPathNodeId
    {

        #region Fields

        /// <summary>
        /// The id of the document node.
        /// </summary>
        internal const long Document = 0;

        #endregion

        #region Internal Methods

        /// <summary>
        /// Returns the row of the declaration that binds a namespace node.
        /// </summary>
        /// <param name="id">A namespace node that is not the <c>xml</c> one.</param>
        /// <returns>The row index of the declaration.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Declaration(long id)
        {
            return (int)(uint)id - 2;
        }

        /// <summary>
        /// Returns the id of the node a row is.
        /// </summary>
        /// <param name="row">The row index.</param>
        /// <returns>The id.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long FromRow(int row)
        {
            return (long)(row + 1) << 32;
        }

        /// <summary>
        /// Returns whether an id is a namespace node.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns><see langword="true"/> for a namespace node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsNamespace(long id)
        {
            return (uint)id != 0;
        }

        /// <summary>
        /// Returns whether a namespace node is the <c>xml</c> one, which no declaration binds.
        /// </summary>
        /// <param name="id">A namespace node.</param>
        /// <returns><see langword="true"/> for the <c>xml</c> namespace node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsXmlNamespace(long id)
        {
            return (uint)id == 1;
        }

        /// <summary>
        /// Returns the id of a namespace node.
        /// </summary>
        /// <param name="element">The row of the element the node belongs to.</param>
        /// <param name="declaration">The row of the declaration that binds the prefix.</param>
        /// <returns>The id.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long Namespace(int element, int declaration)
        {
            return FromRow(element) | (uint)(declaration + 2);
        }

        /// <summary>
        /// Returns the row of a node: the row it is, or for a namespace node the row of its element.
        /// </summary>
        /// <param name="id">The id.</param>
        /// <returns>The row index, or <c>-1</c> for the document node.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int Row(long id)
        {
            return (int)(id >> 32) - 1;
        }

        /// <summary>
        /// Returns the id of the <c>xml</c> namespace node of an element.
        /// </summary>
        /// <param name="element">The row of the element.</param>
        /// <returns>The id.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static long XmlNamespace(int element)
        {
            return FromRow(element) | 1;
        }

        #endregion

    }

}
