/// Doc comments: `///` lines directly above a definition (attributes may sit between them and it) are
/// its documentation, which hover shows. The engine never sees them -- to the tokenizer a `///` line is
/// an ordinary `//` comment -- so this reads the source text rather than the tree.
public static class MoiraiDocComments
{
    /// The doc comment above the definition that starts on 0-based <paramref name="line"/>, its lines
    /// joined with the `///` stripped; null when there is none.
    public static string? Above(string content, int line)
    {
        var lines = content.Split('\n');
        if (line <= 0 || line > lines.Length)
            return null;

        var doc = new List<string>();
        for (int i = line - 1; i >= 0; i--)
        {
            var text = lines[i].Trim();
            if (text.StartsWith("///"))
            {
                var body = text[3..];
                doc.Add(body.StartsWith(' ') ? body[1..] : body);
            }
            else if (text.StartsWith('@') && doc.Count == 0)
                continue; // `/// doc` then `@tag(...)` then the definition
            else
                break;
        }

        if (doc.Count == 0)
            return null;
        doc.Reverse();
        return string.Join("\n", doc);
    }
}
