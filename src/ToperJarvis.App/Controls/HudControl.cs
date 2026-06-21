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
/// HUD J.A.R.V.I.S. rysowany przez SkiaSharp: szklisty centralny orb (puls idle, jaśnienie i
/// zmiana barwy zależnie od stanu, fale energii przy przetwarzaniu), trzy koncentryczne pierścienie
/// danych wirujące z różną prędkością oraz reaktywny waveform sterowany poziomem mikrofonu.
/// </summary>
public sealed class HudControl : Control
{
    public static readonly StyledProperty<AssistantState> StateProperty =
        AvaloniaProperty.Register<HudControl, AssistantState>(nameof(State));

    public static readonly StyledProperty<double> CpuProperty =
        AvaloniaProperty.Register<HudControl, double>(nameof(Cpu));

    public static readonly StyledProperty<double> RamProperty =
        AvaloniaProperty.Register<HudControl, double>(nameof(Ram));

    public static readonly StyledProperty<double> MicLevelProperty =
        AvaloniaProperty.Register<HudControl, double>(nameof(MicLevel));

    private readonly DispatcherTimer _timer;
    private double _seconds;

    public HudControl()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _timer.Tick += (_, _) =>
        {
            _seconds += 0.033;
            InvalidateVisual();
        };
    }

    public AssistantState State { get => GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public double Cpu { get => GetValue(CpuProperty); set => SetValue(CpuProperty, value); }
    public double Ram { get => GetValue(RamProperty); set => SetValue(RamProperty, value); }

    /// <summary>Poziom sygnału mikrofonu (0..1) — napędza waveform i jasność orba.</summary>
    public double MicLevel { get => GetValue(MicLevelProperty); set => SetValue(MicLevelProperty, value); }

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
            new Rect(Bounds.Size), _seconds, State, Cpu, Ram, MicLevel));
    }

    /// <summary>Operacja rysująca HUD bezpośrednio na płótnie SkiaSharp.</summary>
    private sealed class HudDrawOperation(
        Rect bounds, double seconds, AssistantState state, double cpu, double ram, double micLevel)
        : ICustomDrawOperation
    {
        // Paleta cyan → fiolet (stany aktywne) zgodna ze stylem J.A.R.V.I.S.
        private static readonly SKColor Cyan = new(0x4F, 0xC3, 0xF7);
        private static readonly SKColor BrightCyan = new(0xB3, 0xEC, 0xFF);

        // Futurystyczna czcionka (Bahnschrift — dostarczana z Windows 10/11), z fallbackiem.
        private static readonly SKTypeface Futuristic =
            SKTypeface.FromFamilyName("Bahnschrift") ?? SKTypeface.Default;

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

                var cx = w / 2f;
                var cy = h / 2f;
                var maxR = Math.Min(w, h) * 0.46f;
                var level = (float)Math.Clamp(micLevel, 0.0, 1.0);
                var intensity = (float)StateIntensity(state);

                // Łagodny puls idle co 1.5 s + jaśnienie w rytm głosu.
                var idlePulse = 0.5f + 0.5f * (float)Math.Sin(seconds * (Math.PI * 2 / 1.5));
                var color = Lerp(StateColor(state), BrightCyan, level * 0.6);

                // Fale energii „puff" — emitowane gdy J.A.R.V.I.S przetwarza.
                if (intensity >= 0.5f)
                    DrawRipples(canvas, cx, cy, maxR, color, intensity);

                // Trzy koncentryczne pierścienie danych (różne prędkości i kierunki).
                DrawOuterRing(canvas, cx, cy, maxR, color, seconds * 38);          // szybki
                DrawMiddleRing(canvas, cx, cy, maxR * 0.80f, color, -seconds * 22); // średni
                DrawInnerRing(canvas, cx, cy, maxR * 0.62f, color, seconds * 12);    // wolny

                // Reaktywny waveform — bije w rytm mowy (komponent „listening").
                DrawWaveform(canvas, cx, cy, maxR * 0.54f, color, level, intensity);

                // Szklisty rdzeń + jasny wewnętrzny pierścień.
                DrawCore(canvas, cx, cy, maxR * 0.46f, color, idlePulse, level);
                DrawCenterText(canvas, cx, cy, maxR, color, level, state);

                // Futurystyczne wskaźniki systemu po bokach orba.
                var gr = Math.Min(w, h) * 0.12f;
                DrawGauge(canvas, gr * 1.4f, h - gr * 1.35f, gr, cpu, "CPU");
                DrawGauge(canvas, w - gr * 1.4f, h - gr * 1.35f, gr, ram, "RAM");
            }
        }

        private void DrawRipples(SKCanvas canvas, float cx, float cy, float maxR, SKColor color, float intensity)
        {
            const int count = 3;
            using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2 };
            for (var i = 0; i < count; i++)
            {
                var t = (float)Frac(seconds * 0.5 + i / (double)count);
                var r = maxR * (0.45f + t * 0.6f);
                var alpha = (byte)((1f - t) * 120f * intensity);
                paint.Color = color.WithAlpha(alpha);
                canvas.DrawCircle(cx, cy, r, paint);
            }
        }

        // Pierścień zewnętrzny (szybki): gęste „kreski" + kilka dłuższych segmentów.
        private void DrawOuterRing(SKCanvas canvas, float cx, float cy, float r, SKColor color, double rotDeg)
        {
            DrawFullCircle(canvas, cx, cy, r, color.WithAlpha(40), 1f);
            DrawDashes(canvas, cx, cy, r, color.WithAlpha(150), 60, 2f, 6f, rotDeg);
            DrawArcs(canvas, cx, cy, r * 0.965f, color.WithAlpha(220), 3, 26f, rotDeg * 0.7, 3.5f);
        }

        // Pierścień środkowy: segmentowe łuki + znaczniki.
        private void DrawMiddleRing(SKCanvas canvas, float cx, float cy, float r, SKColor color, double rotDeg)
        {
            DrawArcs(canvas, cx, cy, r, color.WithAlpha(200), 6, 34f, rotDeg, 2.5f);
            DrawTicks(canvas, cx, cy, r * 0.9f, color.WithAlpha(120), 40, 5f, rotDeg);
        }

        // Pierścień wewnętrzny (wolny): punktowy + pojedyncze łuki.
        private void DrawInnerRing(SKCanvas canvas, float cx, float cy, float r, SKColor color, double rotDeg)
        {
            DrawDots(canvas, cx, cy, r, color.WithAlpha(170), 36, 1.6f, rotDeg);
            DrawArcs(canvas, cx, cy, r * 0.88f, color.WithAlpha(120), 2, 60f, -rotDeg * 1.5, 2f);
        }

        // Reaktywny waveform — długość słupków zależna od poziomu mikrofonu.
        private void DrawWaveform(SKCanvas canvas, float cx, float cy, float r, SKColor color, float level, float intensity)
        {
            const int bars = 72;
            using var paint = new SKPaint
            {
                Color = color, IsAntialias = true, StrokeWidth = 2.4f,
                Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round,
            };
            for (var i = 0; i < bars; i++)
            {
                var ang = i / (double)bars * Math.PI * 2 + seconds * 0.3;
                var wobble = Math.Sin(seconds * 6 + i * 0.7) * 0.5 + 0.5;
                var amp = r * 0.18f * (float)(0.12 + (level * 1.7 + intensity * 0.18) * wobble);
                var x0 = cx + (float)Math.Cos(ang) * r;
                var y0 = cy + (float)Math.Sin(ang) * r;
                var x1 = cx + (float)Math.Cos(ang) * (r + amp);
                var y1 = cy + (float)Math.Sin(ang) * (r + amp);
                canvas.DrawLine(x0, y0, x1, y1, paint);
            }
        }

        // Szklisty rdzeń: poświata, gradient radialny, jasny pierścień, świecący punkt.
        private static void DrawCore(SKCanvas canvas, float cx, float cy, float r, SKColor color, float idlePulse, float level)
        {
            var pulse = 1f + idlePulse * 0.06f + level * 0.18f;
            var coreR = r * pulse;

            using (var glow = new SKPaint
            {
                Color = color.WithAlpha((byte)(60 + level * 130)),
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 30),
            })
            {
                canvas.DrawCircle(cx, cy, coreR * 1.05f, glow);
            }

            using (var fill = new SKPaint
            {
                IsAntialias = true,
                Shader = SKShader.CreateRadialGradient(
                    new SKPoint(cx, cy - coreR * 0.15f), coreR,
                    new[] { BrightCyan.WithAlpha(220), color.WithAlpha(110), color.WithAlpha(20) },
                    new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp),
            })
            {
                canvas.DrawCircle(cx, cy, coreR, fill);
            }

            // Jasny wewnętrzny pierścień (charakterystyczny mocny okrąg).
            using (var ring = new SKPaint
            {
                Color = BrightCyan.WithAlpha((byte)(180 + level * 75)),
                IsAntialias = true, Style = SKPaintStyle.Stroke,
                StrokeWidth = 3f + level * 3f,
            })
            {
                canvas.DrawCircle(cx, cy, coreR, ring);
            }

            using (var dot = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)(130 + level * 125)),
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8),
            })
            {
                canvas.DrawCircle(cx, cy, coreR * 0.32f, dot);
            }
        }

        private static void DrawCenterText(SKCanvas canvas, float cx, float cy, float maxR, SKColor color, float level, AssistantState state)
        {
            // Marka u góry rdzenia.
            using var brandFont = new SKFont(Futuristic, maxR * 0.13f);
            using var brandPaint = new SKPaint { Color = SKColors.White.WithAlpha(140), IsAntialias = true };
            canvas.DrawText("J.A.R.V.I.S", cx, cy - maxR * 0.02f, SKTextAlign.Center, brandFont, brandPaint);

            // Bieżący stan poniżej — barwą stanu, futurystyczną czcionką, z poświatą.
            using var stateFont = new SKFont(Futuristic, maxR * 0.10f);
            using var glow = new SKPaint
            {
                Color = color.WithAlpha((byte)(120 + level * 100)),
                IsAntialias = true,
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6),
            };
            using var statePaint = new SKPaint
            {
                Color = Lerp(color, BrightCyan, 0.3).WithAlpha((byte)(200 + level * 55)),
                IsAntialias = true,
            };
            var label = StateLabel(state);
            var y = cy + maxR * 0.17f;
            canvas.DrawText(label, cx, y, SKTextAlign.Center, stateFont, glow);
            canvas.DrawText(label, cx, y, SKTextAlign.Center, stateFont, statePaint);
        }

        private static string StateLabel(AssistantState state) => state switch
        {
            AssistantState.Idle => "GOTOWY",
            AssistantState.Listening => "SŁUCHAM",
            AssistantState.Transcribing => "ROZPOZNAJĘ",
            AssistantState.Thinking => "MYŚLĘ",
            AssistantState.Speaking => "MÓWIĘ",
            _ => state.ToString().ToUpperInvariant(),
        };

        // ---- prymitywy pierścieni ----

        private static void DrawFullCircle(SKCanvas canvas, float cx, float cy, float r, SKColor color, float sw)
        {
            using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = sw };
            canvas.DrawCircle(cx, cy, r, paint);
        }

        private static void DrawArcs(SKCanvas canvas, float cx, float cy, float r, SKColor color, int count, float spanDeg, double rotDeg, float sw)
        {
            using var paint = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = sw, StrokeCap = SKStrokeCap.Round };
            using var path = new SKPath();
            var rect = new SKRect(cx - r, cy - r, cx + r, cy + r);
            var step = 360f / count;
            for (var i = 0; i < count; i++)
                path.AddArc(rect, (float)(rotDeg + i * step), spanDeg);
            canvas.DrawPath(path, paint);
        }

        private static void DrawDashes(SKCanvas canvas, float cx, float cy, float r, SKColor color, int count, float sw, float len, double rotDeg)
        {
            using var paint = new SKPaint { Color = color, IsAntialias = true, StrokeWidth = sw, Style = SKPaintStyle.Stroke };
            var rot = rotDeg * Math.PI / 180;
            for (var i = 0; i < count; i++)
            {
                var ang = i / (double)count * Math.PI * 2 + rot;
                var x0 = cx + (float)Math.Cos(ang) * r;
                var y0 = cy + (float)Math.Sin(ang) * r;
                var x1 = cx + (float)Math.Cos(ang) * (r + len);
                var y1 = cy + (float)Math.Sin(ang) * (r + len);
                canvas.DrawLine(x0, y0, x1, y1, paint);
            }
        }

        private static void DrawTicks(SKCanvas canvas, float cx, float cy, float r, SKColor color, int count, float len, double rotDeg)
            => DrawDashes(canvas, cx, cy, r, color, count, 1.4f, len, rotDeg);

        private static void DrawDots(SKCanvas canvas, float cx, float cy, float r, SKColor color, int count, float radius, double rotDeg)
        {
            using var paint = new SKPaint { Color = color, IsAntialias = true };
            var rot = rotDeg * Math.PI / 180;
            for (var i = 0; i < count; i++)
            {
                var ang = i / (double)count * Math.PI * 2 + rot;
                canvas.DrawCircle(cx + (float)Math.Cos(ang) * r, cy + (float)Math.Sin(ang) * r, radius, paint);
            }
        }

        // Łukowy wskaźnik (gauge) wartości 0–100% z etykietą; barwa zależy od obciążenia.
        private static void DrawGauge(SKCanvas canvas, float cx, float cy, float r, double value, string label)
        {
            const float start = 130f;
            const float sweep = 280f;
            var frac = (float)Math.Clamp(value / 100.0, 0, 1);
            var col = GaugeColor(value);
            var rect = new SKRect(cx - r, cy - r, cx + r, cy + r);
            var stroke = r * 0.16f;

            using (var bg = new SKPaint { Color = col.WithAlpha(45), IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = stroke, StrokeCap = SKStrokeCap.Round })
            using (var bgPath = new SKPath())
            {
                bgPath.AddArc(rect, start, sweep);
                canvas.DrawPath(bgPath, bg);
            }

            using (var fg = new SKPaint { Color = col, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = stroke, StrokeCap = SKStrokeCap.Round })
            using (var fgPath = new SKPath())
            {
                fgPath.AddArc(rect, start, sweep * frac);
                canvas.DrawPath(fgPath, fg);
            }

            using (var vFont = new SKFont(Futuristic, r * 0.52f))
            using (var vPaint = new SKPaint { Color = SKColors.White.WithAlpha(225), IsAntialias = true })
                canvas.DrawText($"{value:0}%", cx, cy + r * 0.18f, SKTextAlign.Center, vFont, vPaint);

            using (var lFont = new SKFont(Futuristic, r * 0.28f))
            using (var lPaint = new SKPaint { Color = col.WithAlpha(210), IsAntialias = true })
                canvas.DrawText(label, cx, cy + r * 0.62f, SKTextAlign.Center, lFont, lPaint);
        }

        private static SKColor GaugeColor(double v) => v switch
        {
            < 60 => new SKColor(0x4F, 0xC3, 0xF7),
            < 85 => new SKColor(0xFF, 0xB7, 0x4D),
            _ => new SKColor(0xEF, 0x53, 0x50),
        };

        private static double Frac(double x) => x - Math.Floor(x);

        private static SKColor Lerp(SKColor a, SKColor b, double t)
        {
            t = Math.Clamp(t, 0.0, 1.0);
            return new SKColor(
                (byte)(a.Red + (b.Red - a.Red) * t),
                (byte)(a.Green + (b.Green - a.Green) * t),
                (byte)(a.Blue + (b.Blue - a.Blue) * t));
        }

        private static SKColor StateColor(AssistantState state) => state switch
        {
            AssistantState.Idle => new SKColor(0x35, 0x8E, 0xB0),
            AssistantState.Listening => new SKColor(0x4F, 0xC3, 0xF7),
            AssistantState.Transcribing => new SKColor(0xFF, 0xB7, 0x4D),
            AssistantState.Thinking => new SKColor(0xBA, 0x68, 0xC8),
            AssistantState.Speaking => new SKColor(0x66, 0xBB, 0x6A),
            _ => new SKColor(0x35, 0x8E, 0xB0),
        };

        private static double StateIntensity(AssistantState state) => state switch
        {
            AssistantState.Idle => 0.15,
            AssistantState.Listening => 1.0,
            AssistantState.Transcribing => 0.6,
            AssistantState.Thinking => 0.7,
            AssistantState.Speaking => 0.9,
            _ => 0.15,
        };
    }
}
