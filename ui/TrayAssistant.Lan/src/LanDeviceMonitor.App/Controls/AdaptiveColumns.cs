using System.Windows;
using Size = System.Windows.Size;
using System.Windows.Controls;

namespace LanDeviceMonitor.Controls;

// Reflows complete information blocks without shrinking their text.
public sealed class AdaptiveColumns : System.Windows.Controls.Panel
{
    public int Columns { get; set; } = 2;
    public double MinimumColumnWidth { get; set; } = 220;
    public double Gap { get; set; } = 12;
    public bool Justify { get; set; }
    public bool EqualCellsWhenWrapped { get; set; }
    private readonly List<double> _widths = [];
    private int _columns;
    private bool _intrinsicCells;
    private readonly List<double> _heights = [];
    protected override Size MeasureOverride(Size available)
    {
        var width = double.IsInfinity(available.Width) ? MinimumColumnWidth * Columns : available.Width;
        _columns = Math.Clamp((int)((width + Gap) / (MinimumColumnWidth + Gap)), 1, Columns);
        if(Justify)
        {
            _widths.Clear();
            foreach(UIElement child in InternalChildren)
            {
                child.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
                // Fractional DPI can round an exact text width down. Reserve
                // one physical pixel at each edge before choosing the columns.
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleX;
                _widths.Add(Math.Min(width, (Math.Ceiling(child.DesiredSize.Width * dpi) + 2) / dpi));
            }
            _columns=Columns;
            while(_columns>1 && Enumerable.Range(0,(_widths.Count+_columns-1)/_columns)
                .Any(row=>_widths.Skip(row*_columns).Take(_columns).Sum()+Gap*(_columns-1)>width)) _columns/=2;
            // Wrapped metadata rows share column centers even when their labels
            // have different widths. Every complete value must fit its equal cell.
            if(EqualCellsWhenWrapped && _columns<Columns)
                while(_columns>1 && _widths.Any(value=>value>(width-Gap*(_columns-1))/_columns)) _columns/=2;
        }
        // Four related metrics should become two balanced pairs, never a 3+1 row.
        if (Columns == 4 && _columns == 3) _columns = 2;
        _intrinsicCells=Justify && _columns>1 && (!EqualCellsWhenWrapped || _columns==Columns);
        var cellWidth = Math.Max(0, (width - Gap * (_columns - 1)) / _columns);
        _heights.Clear();
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            // The intrinsic pass already measured a complete, fitting block.
            // Measuring it again at a different constraint invalidates all of
            // its text/layout caches, even though no wrapping is needed.
            var childWidth = _intrinsicCells ? _widths[i] : cellWidth;
            if (!Justify || InternalChildren[i].DesiredSize.Width > childWidth)
                InternalChildren[i].Measure(new Size(childWidth, double.PositiveInfinity));
            if (i % _columns == 0) _heights.Add(0);
            _heights[^1] = Math.Max(_heights[^1], InternalChildren[i].DesiredSize.Height);
        }
        return new Size(width, _heights.Sum() + Math.Max(0, _heights.Count - 1) * Gap);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        var cellWidth = Math.Max(0, (finalSize.Width - Gap * (_columns - 1)) / _columns);
        var y = 0d;
        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var row = i / _columns;
            if (i > 0 && i % _columns == 0) y += _heights[row - 1] + Gap;
            var rowWidths=_intrinsicCells ? _widths.Skip(row*_columns).Take(_columns).ToArray() : [];
            var gap=_intrinsicCells && rowWidths.Length>1 ? (finalSize.Width-rowWidths.Sum())/(rowWidths.Length-1) : Gap;
            var x=_intrinsicCells ? rowWidths.Take(i%_columns).Sum()+(i%_columns)*gap : (i%_columns)*(cellWidth+Gap);
            InternalChildren[i].Arrange(new Rect(x,y,_intrinsicCells ? _widths[i] : cellWidth,_heights[row]));
        }
        return finalSize;
    }
}
