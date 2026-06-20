using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;
using ToperJarvis.Abstractions;

namespace ToperJarvis.App.Controls;

/// <summary>
/// HUD rysowany przez SkiaSharp: animowane spektrum, wskaźnik stanu asystenta oraz metryki
/// systemu (CPU/RAM). Animacja odświeżana timerem.
/// </summary>
public sealed class HudControl : Control
{
    public static readonly StyledProperty<AssistantState> StateProperty =
        AvaloniaProperty.Register<HudControl, AssistantState>(nameof(State));

    public static readonly StyledProperty<double> CpuProperty =
        AvaloniaProperty.Register<HudControl, double>(nameof(Cpu));

    public static readonly StyledProperty<double> RamProperty =
        AvaloniaProperty.Register<HudControl, double>(nameof(Ram));

    private readonly DispatcherTimer _timer;
    private double _phase;

    public HudControl()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) =>
        {
            _phase += 0.15;
            InvalidateVisual();
        };
    }

    public AssistantState State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public double Cpu { get => GetValue(CpuProperty); set => SetValue(CpuProperty, value); }
    public double Ram { get => GetValue(RamProperty); set => SetValue(RamProperty, value); }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        context.Custom(new HudDrawOperation(
            new Rect(Bounds.Size), _phase, State, Cpu, Ram));
    }

    /// <summary>Operacja rysująca HUD bezpośrednio na płótnie SkiaSharp.</summary>
    private sealed class HudDrawOperation(Rect bounds, double phase, AssistantState state, double cpu, double ram)
        : ICustomDrawOperation
    {
        public Rect Bounds => bounds;
        public bool HitTest(Point p) => false;
        public bool Equals(ICustomDrawOperation? other) => false;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>()?.Lease();
            if (lease is null)
                return;

            using (lease)
            {
                var canvas = lease.SkCanvas;
                var w = (float)bounds.Width;
                var h = (float)bounds.Height;
                canvas.Clear(new SKColor(0x0B, 0x0F, 0x1A));

                var color = StateColor(state);
                var intensity = StateIntensity(state);

                DrawSpectrum(canvas, w, h, color, intensity);
                DrawStateRing(canvas, w, h, color);
                DrawMetrics(canvas, w, h);
            }
        }

        private void DrawSpectrum(SKCanvas canvas, float w, float h, SKColor color, double intensity)
        {
            const int bars = 32;
            var barWidth = w / (bars * 1.5f);
            var midY = h * 0.55f;
            using var paint = new SKPaint { Color = color, IsAntialias = true };

            for (var i = 0; i < bars; i++)
            {
                var t = phase + i * 0.4;
                var amp = (Math.Sin(t) * 0.5 + 0.5) * intensity;
                var barHeight = (float)(amp * h * 0.3) + 2f;
                var x = i * barWidth * 1.5f + barWidth;
                canvas.DrawRoundRect(x, midY - barHeight, barWidth, barHeight * 2, 2, 2, paint);
            }
        }

        private static void DrawStateRing(SKCanvas canvas, float w, float h, SKColor color)
        {
            var cx = w / 2f;
            var cy = h * 0.25f;
            var r = Math.Min(w, h) * 0.08f;
            using var ring = new SKPaint
            {
                Color = color,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 3,
            };
            canvas.DrawCircle(cx, cy, r, ring);
            using var fill = new SKPaint { Color = color.WithAlpha(60), IsAntialias = true };
            canvas.DrawCircle(cx, cy, r * 0.6f, fill);
        }

        private void DrawMetrics(SKCanvas canvas, float w, float h)
        {
            using var font = new SKFont { Size = 13 };
            using var paint = new SKPaint { Color = new SKColor(0xB0, 0xBE, 0xC5), IsAntialias = true };
            canvas.DrawText($"CPU: {cpu:0}%   RAM: {ram:0}%", 12, h - 12, font, paint);
        }

        private static SKColor StateColor(AssistantState state) => state switch
        {
            AssistantState.Idle => new SKColor(0x37, 0x47, 0x4F),
            AssistantState.Listening => new SKColor(0x4F, 0xC3, 0xF7),
            AssistantState.Transcribing => new SKColor(0xFF, 0xB7, 0x4D),
            AssistantState.Thinking => new SKColor(0xBA, 0x68, 0xC8),
            AssistantState.Speaking => new SKColor(0x66, 0xBB, 0x6A),
            _ => new SKColor(0x37, 0x47, 0x4F),
        };

        private static double StateIntensity(AssistantState state) => state switch
        {
            AssistantState.Idle => 0.15,
            AssistantState.Listening => 1.0,
            AssistantState.Transcribing => 0.5,
            AssistantState.Thinking => 0.7,
            AssistantState.Speaking => 0.9,
            _ => 0.15,
        };
    }
}
