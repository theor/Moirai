namespace TestProject1;

public class FilterTests : TestsBase
{
    [Test]
    public void Filter_Start()
    {
        var s = @"
entity Person {}
prop alive: bool
@start
event char_dies {
    pick $p: type=Person, alive = true
    set $p.alive = false
}";

        Run(s, out _);
    }


    [Test]
    public void Filter_Every()
    {
        var s = @"
entity Person {}
prop alive: bool
@1 every 1 year
event char_dies {
    pick $p: type=Person, alive = true
    set $p.alive = false
}";

        Run(s, out _);
    }

    [Test]
    public void Filter_Frequency()
    {
        var s = @"
entity Person {}
prop alive: bool
@1 per 2 years
event char_dies {
    pick $p: type=Person, alive = true
    set $p.alive = false
}";

        Run(s, out _);
    }
}
