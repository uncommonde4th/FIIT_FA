using TreeDataStructures.Core;

namespace TreeDataStructures.Implementations.RedBlackTree;

public class RedBlackTree<TKey, TValue> : BinarySearchTreeBase<TKey, TValue, RbNode<TKey, TValue>>
{
    protected override RbNode<TKey, TValue> CreateNode(TKey key, TValue value)
    {
        return new RbNode<TKey, TValue>(key, value);
    }

    private bool IsRed(RbNode<TKey, TValue>? node)
    {
        return node != null && node.Color == RbColor.Red;
    }
    
    protected override void OnNodeAdded(RbNode<TKey, TValue> newNode)
    {
        while (newNode.Parent != null && IsRed(newNode.Parent))
        {
            var dad = newNode.Parent;
            var grand = dad.Parent;

            if (grand == null) { break; }

            if (dad == grand.Left)
            {
                var uncle = grand.Right;

                if (IsRed(uncle))
                {
                    dad.Color = RbColor.Black;
                    uncle!.Color = RbColor.Black;
                    grand.Color = RbColor.Red;

                    newNode = grand;
                }
                else
                {
                    if (newNode == dad.Right)
                    {
                        newNode = dad;
                        RotateLeft(newNode);
                        dad = newNode.Parent;
                    }

                    dad!.Color = RbColor.Black;
                    grand.Color = RbColor.Red;
                    RotateRight(grand);
                }
            }
            else
            {
                var uncle = grand.Left;

                if (IsRed(uncle))
                {
                    dad.Color = RbColor.Black;
                    uncle!.Color = RbColor.Black;
                    grand.Color = RbColor.Red;

                    newNode = grand;
                }
                else
                {
                    if (newNode == dad.Left)
                    {
                        newNode = dad;
                        RotateRight(newNode);
                        dad = newNode.Parent;
                    }

                    dad!.Color = RbColor.Black;
                    grand.Color = RbColor.Red;
                    RotateLeft(grand);
                }
            }
        }

        if (Root != null) { Root.Color = RbColor.Black; }
    }

    protected override void RemoveNode(RbNode<TKey, TValue> node)
    {
        RbNode<TKey, TValue>? x = null;
        RbNode<TKey, TValue>? xParent = null;

        RbNode<TKey, TValue>? y = node;
        RbColor yOriginalColor = y.Color;

        if (node.Left == null)
        {
            x = node.Right;
            xParent = node.Parent;
            Transplant(node, node.Right);
        }
        else if (node.Right == null)
        {
            x = node.Left;
            xParent = node.Parent;
            Transplant(node, node.Left);
        }
        else
        {
            y = Minimum(node.Right);
            yOriginalColor = y.Color;
            x = y.Right;

            if (y.Parent == node) { xParent = y; }
            else
            {
                xParent = y.Parent;
                Transplant(y, y.Right);
                y.Right = node.Right;
                y.Right.Parent = y;
            }

            Transplant(node, y);
            y.Left = node.Left;
            y.Left.Parent = y;
            y.Color = node.Color;
        }

        if (yOriginalColor == RbColor.Black) { OnNodeRemoved(xParent, x); }
    }

    protected override void OnNodeRemoved(RbNode<TKey, TValue>? parent, RbNode<TKey, TValue>? child)
    {
        var x = child;
        var xParent = parent;

        while (x != Root && !IsRed(x))
        {
            if (xParent == null) { break; }

            if (x == xParent.Left)
            {
                var brother = xParent.Right;

                if (IsRed(brother))
                {
                    brother!.Color = RbColor.Black;
                    xParent.Color = RbColor.Red;
                    RotateLeft(xParent);
                    brother = xParent.Right;
                }

                if (!IsRed(brother?.Left) && !IsRed(brother?.Right))
                {
                    if (brother != null) { brother.Color = RbColor.Red; }
                    x = xParent;
                    xParent = x.Parent;
                }
                else
                {
                    if (!IsRed(brother?.Right))
                    {
                        if (brother != null)
                        {
                            if (brother?.Left != null) { brother.Left.Color = RbColor.Black; }
                            brother!.Color = RbColor.Red;
                            RotateRight(brother);
                        }
                        brother = xParent.Right;
                    }

                    if (brother != null)
                    {
                        brother.Color = xParent.Color;
                        xParent.Color = RbColor.Black;
                        if (brother.Right != null) { brother.Right.Color = RbColor.Black; }
                        RotateLeft(xParent);
                    }

                    x = Root;
                }
            }
            else
            {
                var brother = xParent.Left;

                if (IsRed(brother))
                {
                    brother!.Color = RbColor.Black;
                    xParent.Color = RbColor.Red;
                    RotateRight(xParent);
                    brother = xParent.Left;
                }

                if (!IsRed(brother?.Left) && !IsRed(brother?.Right))
                {
                    if (brother != null) brother.Color = RbColor.Red;
                    x = xParent;
                    xParent = x.Parent;
                }
                else
                {
                    if (!IsRed(brother?.Left))
                    {
                        if (brother != null)
                        {
                            if (brother.Right != null) { brother.Right.Color = RbColor.Black; }
                            brother.Color = RbColor.Red;
                            RotateLeft(brother);
                        }
                        brother = xParent.Left;
                    }
                    
                    if (brother != null)
                    {
                        brother.Color = xParent.Color;
                        xParent.Color = RbColor.Black;
                        if (brother.Left != null) { brother.Left.Color = RbColor.Black; }
                        RotateRight(xParent);
                    }

                    x = Root;
                }
            }
        }

        if (x != null) x.Color = RbColor.Black;
    }
}