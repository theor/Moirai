namespace TestProject1;

public class TestsBase
{
    public static Database Run(string s, out List<StoryParser.Error> errors, Action<string> assertReprintedCode)
    {
        Console.WriteLine(s);
        var db = StoryParser.Parse(s, out errors);

        var printed = db.Printer.Print();
        Console.WriteLine("### REPRINT");
        Console.WriteLine(printed);
        Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
        assertReprintedCode(printed);
        db.Init();
        return db;
    }
    public static Database Run(string s, out List<StoryParser.Error> errors, int errorCount = 0)
    {
        Console.WriteLine(s);
        var db = StoryParser.Parse(s, out errors);

        var printed = db.Printer.Print();
        Console.WriteLine("### REPRINT");
        Console.WriteLine(printed);
        Assert.AreEqual(errorCount, errors.Count, string.Join("\n", errors));
        if (errorCount == 0)
        {
            var reparsed = StoryParser.Parse(printed, out var errors2);
            Assert.AreEqual(errorCount, errors2.Count, "During reparse: " + string.Join(", ", errors2));
            // Console.WriteLine("### REPRINT 2");
            var print2 = reparsed.Printer.Print();
            // Console.WriteLine(print2);
            Assert.AreEqual(printed, print2);
        }

        db.Init();
        return db;
    }

    protected void AssertInstruction(System.Action a)
    {
        try
        {
            a();
        }
        catch (InvalidOperationException e)
        {
            Console.WriteLine(e.Message);
            return;
        }

        Assert.Fail("Should have thrown");
    }

    protected void NoAssertInstruction(System.Action a)
    {
        a();
    }
}
