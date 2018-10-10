# SArA
**S**imple **Ar**ena **A**llocator.
Experimenting with a basic region-based allocator/GC

This is to support ECS. The vague idea is to split the address space into large arenas. Each is consumed forward-only with its own 'head' position.
Once there are no references to an arena, it is considered cleared, and the head position is reset.
This design means simple pointers work fine.

Expecting quite high fragmentation. Maybe certain subsets of arenas for smaller objects? Could add defrag later, but that would need smart pointers.

## Parts:
* [x] Basic allocator
* [x] Raw memory simulator
* [ ] Variable array/vector
* [ ] GC scanning
* [ ] Hash table

## Maybe:
* [ ] Tag arenas with owner scope? Multiple arenas per scope, but only one scope per arena? (how would that work with recursion?)
      -> (could also tag things up per-function, which might make leaking easier to trace?)