namespace TestProject1;

public class TagsTests : TestsBase
{
    [Test]
    public void ParseCategory()
    {
        var db = Run(@"
@tag('x')
@start
event r1 {
    record('')
}
@start
@tag('x', 'y')
event r2 {
    record('')
}
@tag('x')
@tag('y')
@frequency(1, EveryXYear, 1)
@tag('z')
event r3 {
    record('')
}

", out _);
        // var x = db.GetCategoryId("x");
        // var y = db.GetCategoryId("y");
        // Assert.AreEqual(x, db.Actions[0].Categories[0]);
        // Assert.AreEqual(x, db.Actions[1].Categories[0]);
        // Assert.AreEqual(y, db.Actions[1].Categories[1]);
    }
}
