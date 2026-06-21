using Glass.Network.Protocol.Fields;
using System.Text;

namespace Inference.Core;

///////////////////////////////////////////////////////////////////////////////////////////////
// FieldTreeFormatter
//
// Renders a FieldDisplayNode tree as indented multi-line text, one node per line, with each
// node indented by IndentSpaces spaces per level of depth.
///////////////////////////////////////////////////////////////////////////////////////////////
public static class FieldTreeFormatter
{
    private const int IndentSpaces = 2;

    ///////////////////////////////////////////////////////////////////////////////////////////
    // ToIndentedText
    //
    // Walks the tree from root in depth-first child order and returns the indented text.  Each
    // node's line is its Text preceded by IndentSpaces spaces per depth level; the root is at
    // depth zero.  Returns an empty string when root is null.
    //
    // root:  The tree's root node.
    //
    // Returns:  The indented multi-line text.
    ///////////////////////////////////////////////////////////////////////////////////////////
    public static string ToIndentedText(FieldDisplayNode? root)
    {
        if (root == null)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder();
        AppendNode(builder, root, 0);
        return builder.ToString();
    }

    ///////////////////////////////////////////////////////////////////////////////////////////
    // AppendNode
    //
    // Appends a node's indented line to the builder and recurses into its children at the next
    // depth level.
    //
    // builder:  Receives the text.
    // node:     The node to append.
    // depth:    The node's depth from the root.
    ///////////////////////////////////////////////////////////////////////////////////////////
    private static void AppendNode(StringBuilder builder, FieldDisplayNode node, int depth)
    {
        int spaceCount = depth * IndentSpaces;
        for (int i = 0; i < spaceCount; i++)
        {
            builder.Append(' ');
        }

        builder.Append(node.Text);
        builder.Append('\n');

        for (int childIndex = 0; childIndex < node.Children.Count; childIndex++)
        {
            AppendNode(builder, node.Children[childIndex], depth + 1);
        }
    }
}