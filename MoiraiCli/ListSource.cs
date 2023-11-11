using System.Collections;
using Terminal.Gui;

namespace Moirai;

public class ListSource<T>(List<T> data, ListSource<T>.RenderDelegate render, Func<int, bool>? isMarked = null, Action<int, bool>? setMark = null)
    : IListDataSource
{
    public delegate void RenderDelegate(ListView container, ConsoleDriver driver, bool selected, int item, int col, int line, int width,
        int start);
    public void Render(ListView container, ConsoleDriver driver, bool selected, int item, int col, int line, int width,
        int start = 0)
    {
        render(container, driver, selected, item, col, line, width, start);
    }

    public bool IsMarked(int item) => isMarked == null ? false : isMarked(item);

    public void SetMark(int item, bool value)
    {
        if (setMark != null)
            setMark(item, value);
    }

    public IList ToList() => data;

    public int Count => data.Count;
    public int Length => 100;
}
