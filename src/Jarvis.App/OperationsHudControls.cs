using System.Drawing.Drawing2D;

namespace Jarvis.App;

internal sealed class GlassPanel : Panel
{
    private int _cornerRadius = 22;

    public GlassPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.Transparent;
        Padding = new Padding(18);
    }

    public Color SurfaceColor { get; set; } = Color.FromArgb(16, 26, 42);

    public Color SurfaceColor2 { get; set; } = Color.FromArgb(12, 20, 34);

    public Color BorderColor { get; set; } = Color.FromArgb(56, 86, 118);

    public Color AccentColor { get; set; } = Color.FromArgb(106, 220, 197);

    public int CornerRadius
    {
        get => _cornerRadius;
        set
        {
            _cornerRadius = Math.Max(8, value);
            UpdateRegion();
            Invalidate();
        }
    }

    protected override void OnCreateControl()
    {
        base.OnCreateControl();
        UpdateRegion();
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        UpdateRegion();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = ClientRectangle;

        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            return;
        }

        rectangle.Width -= 1;
        rectangle.Height -= 1;

        using var path = CreateRoundedRectangle(rectangle, CornerRadius);
        using var brush = new LinearGradientBrush(rectangle, SurfaceColor, SurfaceColor2, 115f);
        e.Graphics.FillPath(brush, path);

        using var accentPen = new Pen(Color.FromArgb(130, AccentColor), 2f);
        e.Graphics.DrawLine(accentPen, rectangle.Left + CornerRadius, rectangle.Top + 1, rectangle.Right - CornerRadius, rectangle.Top + 1);

        using var borderPen = new Pen(BorderColor, 1f);
        e.Graphics.DrawPath(borderPen, path);
    }

    private void UpdateRegion()
    {
        if (!IsHandleCreated || Width <= 0 || Height <= 0)
        {
            return;
        }

        using var path = CreateRoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius);
        Region = new Region(path);
    }

    internal static GraphicsPath CreateRoundedRectangle(Rectangle rectangle, int radius)
    {
        var diameter = Math.Min(radius * 2, Math.Min(rectangle.Width, rectangle.Height));
        var path = new GraphicsPath();

        if (diameter <= 0)
        {
            path.AddRectangle(rectangle);
            path.CloseFigure();
            return path;
        }

        var arc = new Rectangle(rectangle.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = rectangle.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = rectangle.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = rectangle.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class BufferedFlowLayoutPanel : FlowLayoutPanel
{
    public BufferedFlowLayoutPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        ResizeRedraw = true;
        AutoScroll = true;
        WrapContents = false;
        FlowDirection = FlowDirection.TopDown;
        BackColor = Color.Transparent;
    }
}

internal sealed class TransparentPanel : Panel
{
    public TransparentPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.Transparent;
    }
}

internal sealed class TransparentFlowLayoutPanel : FlowLayoutPanel
{
    public TransparentFlowLayoutPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.Transparent;
    }
}

internal sealed class TransparentTableLayoutPanel : TableLayoutPanel
{
    public TransparentTableLayoutPanel()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }
}

internal sealed class SignalWaveformControl : Control
{
    private readonly Queue<float> _samples = new();
    private readonly object _sync = new();
    private DateTimeOffset _lastSignalUtc = DateTimeOffset.UtcNow;
    private float _sweepPosition;

    public SignalWaveformControl()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor, true);
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.Transparent;
        Size = new Size(440, 150);
    }

    public Color AccentColor { get; set; } = Color.FromArgb(106, 220, 197);

    public double RmsLevel { get; private set; }

    public double PeakLevel { get; private set; }

    public bool IsSpeechDetected { get; private set; }

    public bool IsWakeWindowOpen { get; private set; }

    public bool IsSpeaking { get; private set; }

    public void PushSignal(VoiceSignalSnapshot signal)
    {
        lock (_sync)
        {
            _lastSignalUtc = signal.TimestampUtc;
            RmsLevel = signal.RmsLevel;
            PeakLevel = signal.PeakLevel;
            IsSpeechDetected = signal.IsSpeechDetected;
            IsWakeWindowOpen = signal.IsWakeWindowOpen;
            EnqueueSample((float)Math.Max(signal.RmsLevel, signal.PeakLevel * 0.68));
        }

        Invalidate();
    }

    public void SetSpeaking(bool isSpeaking)
    {
        IsSpeaking = isSpeaking;
        Invalidate();
    }

    public void AdvanceAnimation()
    {
        lock (_sync)
        {
            _sweepPosition += 0.028f;

            if (_sweepPosition > 1f)
            {
                _sweepPosition -= 1f;
            }

            if ((DateTimeOffset.UtcNow - _lastSignalUtc).TotalMilliseconds > 100)
            {
                RmsLevel *= 0.86;
                PeakLevel *= 0.82;
                EnqueueSample((float)Math.Max(RmsLevel, PeakLevel * 0.68));

                if (RmsLevel < 0.003)
                {
                    RmsLevel = 0;
                }

                if (PeakLevel < 0.003)
                {
                    PeakLevel = 0;
                }

                if (RmsLevel == 0 && PeakLevel == 0)
                {
                    IsSpeechDetected = false;
                }
            }
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = ClientRectangle;

        if (rectangle.Width <= 0 || rectangle.Height <= 0)
        {
            return;
        }

        var waveformArea = new Rectangle(10, 14, Math.Max(120, rectangle.Width - 90), Math.Max(40, rectangle.Height - 28));
        var meterArea = new Rectangle(waveformArea.Right + 16, waveformArea.Top, 28, waveformArea.Height);
        var baselineY = waveformArea.Top + (waveformArea.Height / 2f);

        using var baselinePen = new Pen(Color.FromArgb(42, 88, 118), 1f);
        e.Graphics.DrawLine(baselinePen, waveformArea.Left, (int)baselineY, waveformArea.Right, (int)baselineY);

        float[] samples;

        lock (_sync)
        {
            samples = _samples.ToArray();
        }

        if (samples.Length == 0)
        {
            samples = Enumerable.Repeat(0.02f, 32).ToArray();
        }

        var barWidth = Math.Max(3f, waveformArea.Width / (float)Math.Max(28, samples.Length));
        var spacing = Math.Max(1f, barWidth * 0.34f);
        var totalBarWidth = barWidth - spacing;

        for (var index = 0; index < samples.Length; index++)
        {
            var sample = Math.Clamp(samples[index], 0f, 1f);
            var height = Math.Max(4f, sample * waveformArea.Height * (IsSpeechDetected ? 0.96f : 0.78f));
            var x = waveformArea.Left + (index * barWidth);
            var y = baselineY - (height / 2f);
            var alpha = 28 + (int)(205 * ((index + 1f) / samples.Length));
            var color = Color.FromArgb(alpha, AccentColor);

            using var brush = new SolidBrush(color);
            using var barPath = GlassPanel.CreateRoundedRectangle(
                Rectangle.Round(new RectangleF(x, y, Math.Max(2f, totalBarWidth), height)),
                4);
            e.Graphics.FillPath(brush, barPath);
        }

        var sweepX = waveformArea.Left + (waveformArea.Width * _sweepPosition);
        using var sweepBrush = new LinearGradientBrush(
            new Rectangle((int)sweepX - 26, waveformArea.Top, 52, waveformArea.Height),
            Color.FromArgb(0, AccentColor),
            Color.FromArgb(IsSpeaking ? 130 : 80, AccentColor),
            0f);
        e.Graphics.FillRectangle(sweepBrush, sweepX - 26, waveformArea.Top, 52, waveformArea.Height);

        DrawMeter(e.Graphics, meterArea);
    }

    private void DrawMeter(Graphics graphics, Rectangle meterArea)
    {
        using var trackBrush = new SolidBrush(Color.FromArgb(18, 40, 58));
        using var trackPath = GlassPanel.CreateRoundedRectangle(meterArea, 14);
        graphics.FillPath(trackBrush, trackPath);

        var meterHeight = (int)Math.Round(meterArea.Height * Math.Clamp(PeakLevel, 0, 1));
        var fillArea = new Rectangle(meterArea.Left + 4, meterArea.Bottom - meterHeight - 4, meterArea.Width - 8, Math.Max(8, meterHeight));
        var meterColor = IsSpeaking
            ? Color.FromArgb(255, 191, 105)
            : IsWakeWindowOpen
                ? Color.FromArgb(255, 138, 101)
                : AccentColor;

        using var fillBrush = new LinearGradientBrush(fillArea, Color.FromArgb(120, meterColor), meterColor, 90f);
        using var fillPath = GlassPanel.CreateRoundedRectangle(fillArea, 10);
        graphics.FillPath(fillBrush, fillPath);

        using var borderPen = new Pen(Color.FromArgb(78, 110, 142), 1f);
        graphics.DrawPath(borderPen, trackPath);
    }

    private void EnqueueSample(float sample)
    {
        const int maxSamples = 84;
        _samples.Enqueue(Math.Clamp(sample, 0f, 1f));

        while (_samples.Count > maxSamples)
        {
            _samples.Dequeue();
        }
    }
}

internal sealed class ActionCueOverlayForm : Form
{
    private readonly System.Windows.Forms.Timer _timer;
    private readonly Font _headlineFont = new("Bahnschrift SemiBold", 18f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Font _detailFont = new("Consolas", 11f, FontStyle.Regular, GraphicsUnit.Point);
    private string _headline = string.Empty;
    private string _detail = string.Empty;
    private Color _accent = Color.FromArgb(106, 220, 197);
    private DateTimeOffset _startedUtc;
    private TimeSpan _duration = TimeSpan.FromMilliseconds(1500);

    public ActionCueOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Opacity = 0;
        Width = 760;
        Height = 110;
        BackColor = Color.Black;

        _timer = new System.Windows.Forms.Timer
        {
            Interval = 16
        };
        _timer.Tick += OnTick;
    }

    protected override bool ShowWithoutActivation => true;

    public void ShowCue(Screen screen, string headline, string detail, Color accent)
    {
        _headline = headline.Trim();
        _detail = detail.Trim();
        _accent = accent;
        _startedUtc = DateTimeOffset.UtcNow;

        var workingArea = screen.WorkingArea;
        var width = Math.Min(workingArea.Width - 48, Width);
        Bounds = new Rectangle(
            workingArea.Left + ((workingArea.Width - width) / 2),
            workingArea.Top + 24,
            width,
            Height);

        if (!Visible)
        {
            Show();
        }

        _timer.Start();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var rectangle = ClientRectangle;
        rectangle.Width -= 1;
        rectangle.Height -= 1;

        using var path = GlassPanel.CreateRoundedRectangle(rectangle, 28);
        using var brush = new LinearGradientBrush(rectangle, Color.FromArgb(32, 12, 24, 38), Color.FromArgb(28, 7, 14, 24), 90f);
        e.Graphics.FillPath(brush, path);

        using var glowPen = new Pen(Color.FromArgb(220, _accent), 2f);
        e.Graphics.DrawPath(glowPen, path);
        using var accentBrush = new SolidBrush(Color.FromArgb(245, _accent));
        e.Graphics.FillRectangle(accentBrush, 20, 18, 8, rectangle.Height - 36);

        var headlineArea = new Rectangle(46, 18, rectangle.Width - 64, 28);
        var detailArea = new Rectangle(46, 50, rectangle.Width - 64, 38);

        TextRenderer.DrawText(
            e.Graphics,
            _headline,
            _headlineFont,
            headlineArea,
            Color.FromArgb(240, 247, 255),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        TextRenderer.DrawText(
            e.Graphics,
            _detail,
            _detailFont,
            detailArea,
            Color.FromArgb(170, 191, 214),
            TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _headlineFont.Dispose();
            _detailFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnTick(object? sender, EventArgs eventArgs)
    {
        var elapsed = DateTimeOffset.UtcNow - _startedUtc;

        if (elapsed >= _duration)
        {
            _timer.Stop();
            Hide();
            Opacity = 0;
            return;
        }

        var progress = elapsed.TotalMilliseconds / _duration.TotalMilliseconds;
        var fadeIn = Math.Min(1d, progress / 0.18d);
        var fadeOut = Math.Min(1d, (1d - progress) / 0.25d);
        Opacity = Math.Min(fadeIn, fadeOut) * 0.92;
    }
}
