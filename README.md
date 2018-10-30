# SArA
**S**imple **Ar**ena **A**llocator.
Experimenting with a basic region-based allocator/GC

This is to support MECS. The the address space is split into relatively large arenas (64K in this implementation).
Each is consumed forward-only with its own 'head' position.
Once there are no references to an arena, it is considered cleared, and the head position is reset.
This design means simple pointers work fine.

Beyond the MemorySimulator, there should be no heap-memory managed by .Net

## Parts:
* [x] Basic allocator
* [x] Raw memory simulator
* [x] Variable array/vector
* [x] Generic variable vector (to help back hash table etc)
* [x] Hash table
* [x] Proper result handling
* [ ] Tree structure (for syntax parsing etc. Probably as list-pairs (leafs and children)
* [ ] Serialisation
* [ ] GC scanning & reference listing

## Maybe:
* [ ] Tag arenas with owner scope? Multiple arenas per scope, but only one scope per arena? (how would that work with recursion?)
      -> (could also tag things up per-function, which might make leaking easier to trace?)

Expecting quite high fragmentation. Maybe certain subsets of arenas for smaller objects? Could add defrag later, but that would need smart pointers.