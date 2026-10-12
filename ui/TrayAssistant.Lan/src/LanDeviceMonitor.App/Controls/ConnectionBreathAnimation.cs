using System.Windows;
using System.Windows.Media.Animation;

namespace LanDeviceMonitor.Controls;

/// <summary>A gentle inhale/exhale with continuous position and velocity.</summary>
internal sealed class ConnectionBreathAnimation : DoubleAnimationBase
{
    private const double PeakExpansion = .12;
    private const double InhaleFraction = .42;
    private const double HandoffFraction = .18;
    public static readonly DependencyProperty StartScaleProperty = DependencyProperty.Register(
        nameof(StartScale), typeof(double), typeof(ConnectionBreathAnimation), new PropertyMetadata(1d));
    public static readonly DependencyProperty StartVelocityProperty = DependencyProperty.Register(
        nameof(StartVelocity), typeof(double), typeof(ConnectionBreathAnimation), new PropertyMetadata(0d));
    public double StartScale { get => (double)GetValue(StartScaleProperty); set => SetValue(StartScaleProperty, value); }
    public double StartVelocity { get => (double)GetValue(StartVelocityProperty); set => SetValue(StartVelocityProperty, value); }

    private double Seconds => Duration.HasTimeSpan ? Math.Max(.001, Duration.TimeSpan.TotalSeconds) : 1;
    private static double SmootherStep(double t) => t * t * t * (10 + t * (-15 + 6 * t));
    internal static double Envelope(double progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        var phase = p <= InhaleFraction ? p / InhaleFraction : (1 - p) / (1 - InhaleFraction);
        return SmootherStep(phase);
    }
    internal double Sample(double progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        var q = Math.Min(1, p / HandoffFraction);
        // Hermite terms carry an interrupted breath's current position and
        // velocity, then settle both to their resting values at the endpoint.
        return 1 + PeakExpansion * Envelope(p) +
            (StartScale - 1) * (1 - SmootherStep(q)) +
            StartVelocity * Seconds * HandoffFraction * (q - 6 * q * q * q + 8 * q * q * q * q - 3 * q * q * q * q * q);
    }
    internal double Velocity(double progress)
    {
        var p = Math.Clamp(progress, 0, 1);
        var q = Math.Min(1, p / HandoffFraction);
        var inhale = p <= InhaleFraction;
        var phase = inhale ? p / InhaleFraction : (1 - p) / (1 - InhaleFraction);
        var envelopeDerivative = 30 * phase * phase * (phase - 1) * (phase - 1) *
            (inhale ? 1 / InhaleFraction : -1 / (1 - InhaleFraction));
        return (PeakExpansion * envelopeDerivative +
            (StartScale - 1) * -30 * q * q * (q - 1) * (q - 1) / HandoffFraction +
            StartVelocity * Seconds * (1 - 18 * q * q + 32 * q * q * q - 15 * q * q * q * q)) / Seconds;
    }
    protected override double GetCurrentValueCore(double defaultOriginValue, double defaultDestinationValue, AnimationClock animationClock) =>
        Sample(animationClock.CurrentProgress ?? 0);
    protected override Freezable CreateInstanceCore() => new ConnectionBreathAnimation();
}
