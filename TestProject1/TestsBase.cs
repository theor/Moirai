using System.Linq;
using Moirai.Parser;

namespace TestProject1;

public class TestsBase
{
    protected static void RunAssert(string s, string? actionName = null)
    {
        var db = Run(s, out var errors);
        if (actionName != null)
            db.RunAction(actionName);
        else
            db.RunAction(db.Actions[0]);
        db.Printer.PrintDb();
    }
    public static Database Run(string s, out List<StoryParser.Error> errors, Action<string> assertReprintedCode)
    {
        Console.WriteLine(s);
        var db = StoryParser.Parse(s, out errors);

        var printed = db.Printer.Print();
        Console.WriteLine("### REPRINT");
        Console.WriteLine(printed);
        Assert.That(errors.Count, Is.EqualTo(0), string.Join("\n", errors));
        assertReprintedCode(printed);
        db.Init();
        return db;
    }

    public static Database Run(string s, out List<StoryParser.Error> errors, int errorCount = 0)
    {
        Console.WriteLine(string.Join("\n", s.Split('\n').Select((s1, i) =>
            $"{i+1,3} {s1}"
        )));
        var db = StoryParser.Parse(s, out errors);

        string printed = "";
        // try
        // {
            printed = db.Printer.Print();
            Console.WriteLine("### REPRINT");
            Console.WriteLine(string.Join("\n", printed.Split('\n').Select((s1, i) =>
                $"{i+1,3} {s1}"
            )));
        // }
        // catch (Exception e)
        // {
            // Console.WriteLine(e);
        // }
        Assert.That(errors.Count(e => e.Severity == StoryParser.Severity.Error), Is.EqualTo(errorCount),
            string.Join("\n", errors));
        if (errorCount == 0)
        {
            var reparsed = StoryParser.Parse(printed, out var errors2);
            Assert.That(errors2.Count(e => e.Severity == StoryParser.Severity.Error), Is.EqualTo(errorCount),
                "During reparse: " + string.Join(", ", errors2));
            var print2 = reparsed.Printer.Print();
            Console.WriteLine("### REPRINT 2");
            Console.WriteLine(print2);
            Assert.That(print2, Is.EqualTo(printed));
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
