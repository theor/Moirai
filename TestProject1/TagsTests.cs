namespace TestProject1;

public class TagsTests : TestsBase
{
    [Test]
    public void ParseTags()
    {
        var db = Run(@"
tag #x
tag #y

rule r1 #x {
    record ''
}
rule r2 #x #y {
    record ''
}

", out _);
        Assert.IsTrue(db.GetTagId("#x", out var x));
        Assert.IsTrue(db.GetTagId("#y", out var y));
        Assert.AreEqual(x, db.Actions[0].Tags[0]);
        Assert.AreEqual(x, db.Actions[1].Tags[0]);
        Assert.AreEqual(y, db.Actions[1].Tags[1]);
    }
}
