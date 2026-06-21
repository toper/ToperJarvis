using System;
using System.Collections.Generic;
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

/// <summary>Pojedynczy odczyt telemetrii w HUD (etykieta + wartość + strona orba).</summary>
public readonly record struct HudReadout(string Label, string Value, bool Right);

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

    public static readonly StyledProperty<double> GpuUtilProperty =
        AvaloniaProperty.Register<HudControl, double>(nameof(GpuUtil));

    public static readonly StyledProperty<double> PowerWProperty =
        AvaloniaProperty.Register<HudControl, double>(nameof(PowerW));

    public static readonly StyledProperty<double> MicLevelProperty =
        AvaloniaProperty.Register<HudControl, double>(nameof(MicLevel));

    public static readonly StyledProperty<double> LastTurnMsProperty =
        AvaloniaProperty.Register<HudControl, double>(nameof(LastTurnMs));

    public static readonly StyledProperty<IReadOnlyList<HudReadout>?> TelemetryProperty =
        AvaloniaProperty.Register<HudControl, IReadOnlyList<HudReadout>?>(nameof(Telemetry));

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

    /// <summary>Obciążenie GPU serwera DGX (%) — duży gauge.</summary>
    public double GpuUtil { get => GetValue(GpuUtilProperty); set => SetValue(GpuUtilProperty, value); }

    /// <summary>Pobór mocy GPU serwera DGX (W) — duży gauge.</summary>
    public double PowerW { get => GetValue(PowerWProperty); set => SetValue(PowerWProperty, value); }

    /// <summary>Poziom sygnału mikrofonu (0..1) — napędza waveform i jasność orba.</summary>
    public double MicLevel { get => GetValue(MicLevelProperty); set => SetValue(MicLevelProperty, value); }

    /// <summary>Czas przetwarzania ostatniej komendy (ms) — pokazywany w telemetrii HUD.</summary>
    public double LastTurnMs { get => GetValue(LastTurnMsProperty); set => SetValue(LastTurnMsProperty, value); }

    /// <summary>Odczyty telemetrii (HA, DGX) prezentowane wokół orba.</summary>
    public IReadOnlyList<HudReadout>? Telemetry
    {
        get => GetValue(TelemetryProperty);
        set => SetValue(TelemetryProperty, value);
    }

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
            new Rect(Bounds.Size), _seconds, State, Cpu, Ram, GpuUtil, PowerW, MicLevel, LastTurnMs, DateTime.Now,
            Telemetry ?? Array.Empty<HudReadout>()));
    }

    /// <summary>Operacja rysująca HUD bezpośrednio na płótnie SkiaSharp.</summary>
    private sealed class HudDrawOperation(
        Rect bounds, double seconds, AssistantState state, double cpu, double ram, double gpuUtil, double powerW,
        double micLevel, double lastTurnMs, DateTime now,
        IReadOnlyList<HudReadout> telemetry)
        : ICustomDrawOperation
    {
        // Paleta cyan → fiolet (stany aktywne) zgodna ze stylem J.A.R.V.I.S.
        private static readonly SKColor Cyan = new(0x4F, 0xC3, 0xF7);
        private static readonly SKColor BrightCyan = new(0xB3, 0xEC, 0xFF);

        // Futurystyczna czcionka (Bahnschrift — dostarczana z Windows 10/11), z fallbackiem.
        private static readonly SKTypeface Futuristic =
            SKTypeface.FromFamilyName("Bahnschrift") ?? SKTypeface.Default;

        // Stałe (niezależne od klatki) — alokowane raz, nie ~30×/s. Fonty o stałym rozmiarze i
        // kosztowne filtry rozmycia (CreateBlur) buforowane statycznie.
        private static readonly SKFont ReadoutLabelFont = new(Futuristic, 11);
        private static readonly SKFont ReadoutValueFont = new(Futuristic, 17);
        private static readonly SKFont ClockTimeFont = new(Futuristic, 30);
        private static readonly SKFont ClockDateFont = new(Futuristic, 14);
        private static readonly SKMaskFilter BlurCoreGlow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 30);
        private static readonly SKMaskFilter BlurCoreDot = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 8);
        private static readonly SKMaskFilter BlurStateGlow = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 6);

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

                // Duże wskaźniki: GPU AI i MOC AI z serwera DGX (ważniejsze) + linie danych do orba.
                const double maxPowerW = 140.0; // ~TDP GB10 — skala łuku mocy
                var gr = Math.Min(w, h) * 0.12f;
                var leftPos = new SKPoint(gr * 1.4f, h - gr * 1.35f);
                var rightPos = new SKPoint(w - gr * 1.4f, h - gr * 1.35f);
                var edge = maxR * 1.02f;
                var gpuFrac = gpuUtil / 100.0;
                var powFrac = powerW / maxPowerW;
                DrawFlowLine(canvas, leftPos, new SKPoint(cx, cy), edge, GaugeColor(gpuFrac * 100));
                DrawFlowLine(canvas, rightPos, new SKPoint(cx, cy), edge, GaugeColor(powFrac * 100));
                DrawGauge(canvas, leftPos.X, leftPos.Y, gr, gpuFrac, $"{gpuUtil:0}%", "GPU AI");
                DrawGauge(canvas, rightPos.X, rightPos.Y, gr, powFrac, $"{powerW:0} W", "MOC AI");

                // Zegar (lewy górny) + telemetria wokół orba.
                DrawClock(canvas, 8, 8);
                DrawTelemetry(canvas, w, h, color);
            }
        }

        // Linia danych z węzła (gauge) do krawędzi orba z płynącymi „pakietami".
        private void DrawFlowLine(SKCanvas canvas, SKPoint from, SKPoint center, float orbEdge, SKColor color)
        {
            var dx = center.X - from.X;
            var dy = center.Y - from.Y;
            var dist = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dist < 1f)
                return;

            // Kończ na krawędzi orba, nie w środku.
            var ux = dx / dist;
            var uy = dy / dist;
            var to = new SKPoint(center.X - ux * orbEdge, center.Y - uy * orbEdge);

            using (var lane = new SKPaint { Color = color.WithAlpha(45), IsAntialias = true, StrokeWidth = 1.2f, Style = SKPaintStyle.Stroke })
                canvas.DrawLine(from, to, lane);

            // Pakiety danych przesuwające się ku orbowi.
            using var dot = new SKPaint { Color = color.WithAlpha(220), IsAntialias = true };
            for (var i = 0; i < 3; i++)
            {
                var t = (float)Frac(seconds * 0.6 + i / 3.0);
                var x = from.X + (to.X - from.X) * t;
                var y = from.Y + (to.Y - from.Y) * t;
                dot.Color = color.WithAlpha((byte)(220 * (1 - t) + 35));
                canvas.DrawCircle(x, y, 2.4f, dot);
            }
        }

        private void DrawClock(SKCanvas canvas, float x, float y)
        {
            using var timePaint = new SKPaint { Color = BrightCyan.WithAlpha(230), IsAntialias = true };
            canvas.DrawText(now.ToString("HH:mm:ss"), x, y + 26, SKTextAlign.Left, ClockTimeFont, timePaint);

            using var datePaint = new SKPaint { Color = Cyan.WithAlpha(170), IsAntialias = true };
            canvas.DrawText(now.ToString("dddd, dd.MM.yyyy"), x + 2, y + 46, SKTextAlign.Left, ClockDateFont, datePaint);
        }

        // Panele telemetrii wokół orba: PRZETWARZANIE (realne) + odczyty z listy (HA, DGX).
        private void DrawTelemetry(SKCanvas canvas, float w, float h, SKColor color)
        {
            const float spacing = 44f;
            var topY = h * 0.30f;

            // Lewa kolumna: czas przetwarzania + CPU; prawa: RAM. Reszta z listy (HA).
            var leftY = topY;
            DrawReadout(canvas, 8, leftY, "PRZETWARZANIE", lastTurnMs > 0 ? $"{lastTurnMs:0} ms" : "—", color);
            leftY += spacing;
            DrawReadout(canvas, 8, leftY, "CPU", $"{cpu:0}%", color);
            leftY += spacing;

            var rightY = topY;
            DrawReadout(canvas, w - 8, rightY, "RAM", $"{ram:0}%", color, right: true);
            rightY += spacing;

            foreach (var r in telemetry)
            {
                if (r.Right)
                {
                    DrawReadout(canvas, w - 8, rightY, r.Label, r.Value, color, right: true);
                    rightY += spacing;
                }
                else
                {
                    DrawReadout(canvas, 8, leftY, r.Label, r.Value, color);
                    leftY += spacing;
                }
            }
        }

        // Jeden odczyt telemetrii; right = wyrównanie do prawej (pasek i tekst po drugiej stronie).
        private void DrawReadout(SKCanvas canvas, float x, float y, string label, string value, SKColor color, bool right = false)
        {
            var a = (byte)(80 + 60 * (0.5 + 0.5 * Math.Sin(seconds * 2 + x + y))); // subtelne pulsowanie
            var align = right ? SKTextAlign.Right : SKTextAlign.Left;
            var textX = right ? x - 10 : x + 10;

            using (var bar = new SKPaint { Color = color.WithAlpha(a), IsAntialias = true })
                canvas.DrawRect(right ? x - 3 : x, y, 3, 30, bar);
            using (var lPaint = new SKPaint { Color = color.WithAlpha(150), IsAntialias = true })
                canvas.DrawText(label, textX, y + 12, align, ReadoutLabelFont, lPaint);
            using (var vPaint = new SKPaint { Color = SKColors.White.WithAlpha(220), IsAntialias = true })
                canvas.DrawText(value, textX, y + 30, align, ReadoutValueFont, vPaint);
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
                MaskFilter = BlurCoreGlow,
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
                MaskFilter = BlurCoreDot,
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
                MaskFilter = BlurStateGlow,
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

        // Łukowy wskaźnik (gauge): wypełnienie frac (0..1), własny tekst i etykieta; barwa wg obciążenia.
        private static void DrawGauge(SKCanvas canvas, float cx, float cy, float r, double fraction, string text, string label)
        {
            const float start = 130f;
            const float sweep = 280f;
            var frac = (float)Math.Clamp(fraction, 0, 1);
            var col = GaugeColor(fraction * 100);
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

            using (var vFont = new SKFont(Futuristic, r * 0.42f))
            using (var vPaint = new SKPaint { Color = SKColors.White.WithAlpha(225), IsAntialias = true })
                canvas.DrawText(text, cx, cy + r * 0.16f, SKTextAlign.Center, vFont, vPaint);

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
