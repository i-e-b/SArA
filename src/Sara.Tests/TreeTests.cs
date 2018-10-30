using NUnit.Framework;

namespace Sara.Tests
{
    [TestFixture]
    public class TreeTests {
        
        public SampleElement[] Sample = {
            new SampleElement { a = 0, b = 0.1 },
            new SampleElement { a = 1, b = 1.1 },
            new SampleElement { a = 2, b = 2.1 },
            new SampleElement { a = 3, b = 3.1 },
            new SampleElement { a = 4, b = 4.1 },
            new SampleElement { a = 5, b = 5.1 },
            new SampleElement { a = 6, b = 6.1 },
        };

        [Test]
        public void building_and_walking_a_tree ()
        {
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Tree<SampleElement>(new Allocator(0, Mega.Bytes(1), mem), mem);

            //##### BUILD #####
            subject.SetRootValue(Sample[0]);

            // First child of root
            var ptrRes = subject.AddChild(subject.Root, Sample[1]);
            if (!ptrRes.Success) Assert.Fail("Failed to add child");

            // Second child of root
            ptrRes = subject.AddChild(subject.Root, Sample[2]);
            if (!ptrRes.Success) Assert.Fail("Failed to add child");
            var rc2 = ptrRes.Value;

            // Child of second child
            ptrRes = subject.AddChild(rc2, Sample[3]);
            if (!ptrRes.Success) Assert.Fail("Failed to add child");

            //##### WALK #####
            var body1 = subject.ReadBody(subject.Root);
            Assert.That(body1, Is.EqualTo(Sample[0]));

            var child1Res = subject.Child(subject.Root);
            Assert.IsTrue(child1Res.Success);
            
            var child1childRes = subject.Child(child1Res.Value);
            Assert.IsFalse(child1childRes.Success);

            var child2Res = subject.Sibling(child1Res.Value);
            Assert.IsTrue(child2Res.Success);
            
            var child3Res = subject.Sibling(child2Res.Value);
            Assert.IsFalse(child3Res.Success);

            var child2childRes = subject.Child(child2Res.Value);
            Assert.IsTrue(child2childRes.Success);
            var body_c2c1 = subject.ReadBody(child2childRes.Value);
            Assert.That(body_c2c1, Is.EqualTo(Sample[3]));
        }

        [Test]
        public void can_continue_a_sibling_chain_without_going_to_parent ()
        {
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Tree<SampleElement>(new Allocator(0, Mega.Bytes(1), mem), mem);
            
            //##### BUILD #####
            subject.SetRootValue(Sample[0]);

            // First child of root
            var ptrRes = subject.AddChild(subject.Root, Sample[1]); if (!ptrRes.Success) Assert.Fail("Failed to add child");

            // Second child of root
            ptrRes = subject.AddChild(subject.Root, Sample[2]); if (!ptrRes.Success) Assert.Fail("Failed to add child");
            var rc2 = ptrRes.Value;

            // Child of second child
            ptrRes = subject.AddChild(rc2, Sample[3]); if (!ptrRes.Success) Assert.Fail("Failed to add child");

            //##### ACTION #####
            // add a third child to root by second child
            ptrRes = subject.AddSibling(rc2, Sample[4]);
            if (!ptrRes.Success) Assert.Fail("Failed to add sibling");
            
            //##### WALK #####
            var wres = subject.Child(subject.Root);
            wres = subject.SiblingR(wres);
            wres = subject.SiblingR(wres);

            Assert.That(wres.Success, Is.True, "Failed to get full chain");
            var final = subject.ReadBody(wres.Value);
            Assert.That(final, Is.EqualTo(Sample[4]), "Incorrect data");
        }

        [Test]
        public void can_insert_a_child_at_an_index ()
        {
            var mem = new MemorySimulator(Mega.Bytes(1));
            var subject = new Tree<SampleElement>(new Allocator(0, Mega.Bytes(1), mem), mem);
            
            //##### BUILD #####
            subject.SetRootValue(Sample[0]);

            // First child of root
            var ptrRes = subject.AddChild(subject.Root, Sample[1]); if (!ptrRes.Success) Assert.Fail("Failed to add child 1");

            // Second child of root
            ptrRes = subject.AddChild(subject.Root, Sample[2]); if (!ptrRes.Success) Assert.Fail("Failed to add child 2");
            
            //##### ACTION #####
            // add a new second child to root, pushing the other out to 3rd
            ptrRes = subject.InsertChild(parent: subject.Root, index: 1, element: Sample[4]);
            if (!ptrRes.Success) Assert.Fail("Failed to add new child");
            
            //##### WALK #####
            var wres1 = subject.Child(subject.Root);
            var wres2 = subject.SiblingR(wres1);
            var wres3 = subject.SiblingR(wres2);

            Assert.That(wres3.Success, Is.True, "Failed to get full chain");

            var newChild2 = subject.ReadBody(wres2.Value);
            Assert.That(newChild2, Is.EqualTo(Sample[4]), "Incorrect data (1)");
            
            var newChild3 = subject.ReadBody(wres3.Value);
            Assert.That(newChild3, Is.EqualTo(Sample[2]), "Incorrect data (2)");
        }

        [Test]
        public void can_remove_a_sibling_from_an_index (){ }
    }
}