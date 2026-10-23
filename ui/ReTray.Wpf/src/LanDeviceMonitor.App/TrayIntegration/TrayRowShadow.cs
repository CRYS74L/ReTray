using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace ReTray_App;

// Only the empty capsule silhouette goes through the effect pipeline. Its host
// sits below page content, outside the scrolling viewport and its opacity mask.
internal sealed class TrayRowShadow : IDisposable
{
    private readonly Canvas host;
    private readonly FrameworkElement row;
    private readonly Border silhouette = new()
    {
        IsHitTestVisible = false,
        Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = .18, RenderingBias = RenderingBias.Quality }
    };

    internal TrayRowShadow(Canvas host, FrameworkElement row)
    {
        this.host = host; this.row = row;
        silhouette.SetResourceReference(Border.BackgroundProperty, "StudioSurfaceBrush");
        host.Children.Add(silhouette);
        CompositionTarget.Rendering += FollowRow;
        Update();
    }

    private void FollowRow(object? sender, EventArgs args) => Update();

    internal void Update()
    {
        var point = row.TranslatePoint(new Point(), host);
        Canvas.SetLeft(silhouette, point.X); Canvas.SetTop(silhouette, point.Y);
        silhouette.Width = row.ActualWidth; silhouette.Height = row.ActualHeight;
        silhouette.CornerRadius = new CornerRadius(row.ActualHeight / 2);
    }

    public void Dispose()
    {
        CompositionTarget.Rendering -= FollowRow;
        host.Children.Remove(silhouette);
    }
}
