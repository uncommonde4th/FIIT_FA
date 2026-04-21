using TreeDataStructures.Core;

namespace TreeDataStructures.Implementations.AVL;

public class AvlTree<TKey, TValue> : BinarySearchTreeBase<TKey, TValue, AvlNode<TKey, TValue>>
    where TKey : IComparable<TKey>
{
    protected override AvlNode<TKey, TValue> CreateNode(TKey key, TValue value)
        => new(key, value);
    
    protected override void OnNodeAdded(AvlNode<TKey, TValue> newNode)
    {
        Rebalance(newNode);
    }

    protected override void OnNodeRemoved(AvlNode<TKey, TValue>? parent, AvlNode<TKey, TValue>? child)
    {
        Rebalance(parent ?? child);
    }

    private static int GetHeight(AvlNode<TKey, TValue>? node) => node?.Height ?? 0;

    private static int GetBalance(AvlNode<TKey, TValue>? node) => node == null ? 0 : GetHeight(node.Left) - GetHeight(node.Right);

    private static void UpdateHeight(AvlNode<TKey, TValue>? node)
    {
        if (node != null) { node.Height = 1 + Math.Max(GetHeight(node.Left), GetHeight(node.Right)); }
    }

    private void Rebalance(AvlNode<TKey, TValue>? node)
    {
        while (node != null)
        {
            UpdateHeight(node);
            int balance = GetBalance(node);

            if (balance > 1)
            {
                if (GetBalance(node.Left) < 0) { RotateBigRight(node) ;}
                else { RotateRight(node); }

                UpdateHeight(node);
                UpdateHeight(node.Parent);
            }
            else if (balance < -1)
            {
                if (GetBalance(node.Right) > 0) { RotateBigLeft(node); }
                else { RotateLeft(node); }

                UpdateHeight(node);
                UpdateHeight(node.Parent);
            }

            node = node.Parent;
        }
    }  
}

// dotnet test TreeDataStructures.Tests/ --filter TestCategory=AVL