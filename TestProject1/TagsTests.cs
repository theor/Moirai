namespace TestProject1;

public class TagsTests : TestsBase
{
    [Test]
    public void ParseCategory()
    {
        var db = Run(@"
@tag('x')
event r1 x {
    record('')
}
@tag('x', 'y')
event r2 {
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
