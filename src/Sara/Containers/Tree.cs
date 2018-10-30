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

            var head = _mem.Read<TreeNodeHead>(parent);
            
            if (head.FirstChildPtr >= 0) // There is a sibling chain. Switch function
            {
                return AddSibling(head.FirstChildPtr, element);
            }

            // This is the first child of this parent
            var res = AllocateAndWriteNode(parent, element);
            if (!res.Success) return Result.Fail<long>();
            var newChildPtr = res.Value;

            // Set ourself as the parent's first child
            head.FirstChildPtr = newChildPtr;
            _mem.Write(parent, head);
            return Result.Ok(newChildPtr);
        }

        /// <summary>
        /// Make a new node from an element. Sets parent node inside head, returns result of pointer to new node
        /// </summary>
        private Result<long> AllocateAndWriteNode(long parent, TElement element)
        {
            // Allocate new node and header
            var res = _alloc.Alloc(NodeSize);
            if (!res.Success)
            {
                return Result.Fail<long>();
            }

            // Write a node head and data into memory
            var newChildHead = new TreeNodeHead
            {
                FirstChildPtr = -1,
                NextSiblingPtr = -1,
                ParentPtr = parent
            };
            _mem.WriteC<TreeNodeHead, TElement>(res.Value, newChildHead, element);
            return res;
        }

        public Result<long> AddSibling(long treeNodePtr, TElement element)
        {
            // Not the first child, we need to walk the sibling link chain
            var ptr = treeNodePtr;
            var next = _mem.Read<TreeNodeHead>(ptr);
            while (next.NextSiblingPtr >= 0)
            {
                ptr = next.NextSiblingPtr;
                next = _mem.Read<TreeNodeHead>(ptr);
            }
            
            // Make a new node
            var res = AllocateAndWriteNode(next.ParentPtr, element);
            if (!res.Success) return Result.Fail<long>();
            var newChildPtr = res.Value;

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

        /// <summary>
        /// Try to get next sibling in chain
        /// </summary>
        public Result<long> Sibling(long olderSiblingPtr)
        {
            var head = _mem.Read<TreeNodeHead>(olderSiblingPtr);
            if (head.NextSiblingPtr < 0) return Result.Fail<long>();
            return Result.Ok(head.NextSiblingPtr);
        }

        /// <summary>
        /// Try to get next sibling in chain only if the input was success
        /// </summary>
        public Result<long> SiblingR(Result<long> result)
        {
            if (!result.Success) return result;
            return Sibling(result.Value);
        }

        /// <summary>
        /// Try to insert an element at the given 0-based index.
        /// Will fail if index is greater that the current length (i.e. you can add to the end, but the child chain can not be sparse)
        /// Elements at and after the given index are shifted along.
        /// Returns a pointer to the new node.
        /// </summary>
        /// <param name="parent">Parent element</param>
        /// <param name="index">zero based index</param>
        /// <param name="element">data to store in the node</param>
        public Result<long> InsertChild(long parent, int index, TElement element)
        {
            var head = _mem.Read<TreeNodeHead>(parent);

            // Simplest case: a plain add
            if (head.FirstChildPtr < 0)
            {
                if (index != 0) return Result.Fail<long>();
                return AddChild(parent, element);
            }

            // Special case: insert at start (need to update parent)
            if (index == 0) {
                var newRes = AllocateAndWriteNode(parent, element);
                if (!newRes.Success) return Result.Fail<long>();
                var next = head.FirstChildPtr;
                head.FirstChildPtr = newRes.Value;
                _mem.Write<TreeNodeHead>(parent, head);
            }

            // Main case: walk the chain, keeping track of our index



            return Result.Fail<long>();
        }
    }

    /// <summary>
    /// Tree node headers. These can't contain the body reference for C# reasons
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct TreeNodeHead
    {
        public long ParentPtr;      // Pointer to parent. -1 means root
        public long FirstChildPtr;  // Pointer to child linked list. -1 mean leaf node
        public long NextSiblingPtr; // Pointer to next sibling (linked list of parent's children)
    }
}