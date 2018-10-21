using System.Runtime.InteropServices;

namespace Sara
{
    /// <summary>
    /// A simple k-wide tree structure.
    /// This is an accessor class, it doesn't actually hold the data
    /// </summary>
    /// <remarks>I'd like to make a less pointer-heavy way of doing this,
    /// but for the parser tree this should do
    ///
    /// Each node has (in order)
    ///  - a pointer to its parent
    ///  - a pointer to the first child
    ///  - a pointer to the next sibling.
    ///  - the data value of the node
    /// Any of the pointers may be invalid
    /// 
    /// </remarks>
    public class Tree<TElement> : IGcContainer where TElement:unmanaged
    {
        private readonly Allocator _alloc;
        private readonly IMemoryAccess _mem;

        public const uint POINTER_SIZE = sizeof(long);
        public const uint NODE_HEAD_SIZE = POINTER_SIZE * 3; //sizeof(TreeNodeHead);

        public readonly long NodeSize;

        public bool Valid;

        public readonly long Root;

        public unsafe Tree(Allocator alloc, IMemoryAccess mem)
        {
            _alloc = alloc;
            _mem = mem;

            NodeSize = NODE_HEAD_SIZE + sizeof(TElement);

            // Make the root node
            var res = _alloc.Alloc(NodeSize);
            if (!res.Success) {
                Valid = false;
                return;
            }

            // Set initial values
            Root = res.Value;
            var rootHead = new TreeNodeHead
            {
                FirstChildPtr = -1,
                NextSiblingPtr = -1,
                ParentPtr = -1
            };
            _mem.WriteC<TreeNodeHead, TElement>(Root, rootHead, default);

            Valid = true;
        }

        public void SetRootValue(TElement element)
        {
            _mem.Write(Root + NODE_HEAD_SIZE, element);
        }

        public Result<long> AddChild(long parent, TElement element)
        {
            if (parent < 0) return Result.Fail<long>();
            var res = _alloc.Alloc(NodeSize);
            if (!res.Success) return Result.Fail<long>();
            var newChildPtr = res.Value;

            // Write the new node into memory
            var newChildHead = new TreeNodeHead
            {
                FirstChildPtr = -1,
                NextSiblingPtr = -1,
                ParentPtr = parent
            };
            _mem.WriteC<TreeNodeHead, TElement>(newChildPtr, newChildHead, element);

            // Inject the node into the tree
            var head = _mem.Read<TreeNodeHead>(parent);
            
            if (head.FirstChildPtr < 0) // first child. We can just tag ourself in to the head.
            {
                head.FirstChildPtr = newChildPtr;
                _mem.Write(parent, head);
                return Result.Ok(newChildPtr);
            }

            // Not the first child, we need to walk the sibling link chain
            var ptr = head.FirstChildPtr;
            var next = _mem.Read<TreeNodeHead>(ptr);
            while (next.NextSiblingPtr >= 0)
            {
                ptr = next.NextSiblingPtr;
                next = _mem.Read<TreeNodeHead>(ptr);
            }
            // Write ourself into the chain
            next.NextSiblingPtr = newChildPtr;
            _mem.Write(ptr, next);
            return Result.Ok(newChildPtr);
        }

        public Vector<ulong> References()
        {
            return null;
        }

        public void Deallocate()
        {
        }

        public TElement ReadBody(long treeNodePtr)
        {
            return _mem.Read<TElement>(treeNodePtr + NODE_HEAD_SIZE);
        }

        public Result<long> Child(long parentPtr)
        {
            var head = _mem.Read<TreeNodeHead>(parentPtr);
            if (head.FirstChildPtr < 0) return Result.Fail<long>();
            return Result.Ok(head.FirstChildPtr);
        }

        public Result<long> Sibling(long olderSiblingPtr)
        {
            var head = _mem.Read<TreeNodeHead>(olderSiblingPtr);
            if (head.NextSiblingPtr < 0) return Result.Fail<long>();
            return Result.Ok(head.NextSiblingPtr);
        }
    }

    /// <summary>
    /// The actual tree nodes. Must be nested for C# reasons.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TreeNodeHead
    {
        public long ParentPtr;      // Pointer to parent. -1 means root
        public long FirstChildPtr;  // Pointer to child linked list. -1 mean leaf node
        public long NextSiblingPtr; // Pointer to next sibling (linked list of parent's children)
    }
}