using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Jarvis.Core;

namespace Jarvis.App;

internal sealed partial class JarvisMainForm : Form
{
    private readonly ActionCueOverlayForm _actionCueOverlay = new();
    private IReadOnlyList<WakeWordAssetDescriptor> _availableWakeWordAssets = Array.Empty<WakeWordAssetDescriptor>();
    private IReadOnlyList<WakeWordEngineDescriptor> _availableWakeWordEngines = Array.Empty<WakeWordEngineDescriptor>();
    private IReadOnlyList<VendorDspProfileDescriptor> _availableVendorDspProfiles = Array.Empty<VendorDspProfileDescriptor>();
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly JarvisDesktopContext _context;
    private readonly Font _historyMessageFont;
    private readonly Font _historySourceFont;
    private readonly MicrophoneCalibrationService _microphoneCalibrationService = new();
    private readonly Dictionary<SurfaceMode, Button> _modeButtons = new();
    private readonly SemaphoreSlim _optionsPersistenceGate = new(1, 1);
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly Dictionary<SurfaceState, Label> _stageLabels = new();
    private readonly NotifyIcon _trayIcon;
    private readonly System.Windows.Forms.Timer _waveformTimer;

    private string _activeSessionId = string.Empty;
    private BufferedFlowLayoutPanel _activityFlow = null!;
    private bool _allowExit;
    private SurfaceState? _assistantStateOverride;
    private string _assistantStateOverrideDetail = string.Empty;
    private Panel _approvalPanel = null!;
    private Label _approvalPromptLabel = null!;
    private Label _approvalSummaryLabel = null!;
    private Button _approveButton = null!;
    private Label _assistantStatusLabel = null!;
    private Label _backgroundLabel = null!;
    private ComboBox _beamformingProfileComboBox = null!;
    private Button _calibrateMicButton = null!;
    private Label _calibrationStatusLabel = null!;
    private TransparentPanel _composerPanel = null!;
    private TransparentTableLayoutPanel _contentLayout = null!;
    private JarvisOptions _currentOptions;
    private IVoiceRuntime _currentVoiceRuntime;
    private Button _denyButton = null!;
    private ComboBox _deviceComboBox = null!;
    private Button _deviceRefreshButton = null!;
    private Button _helpButton = null!;
    private RichTextBox _historyBox = null!;
    private Panel _historyCard = null!;
    private ComboBox _hardwareDspProfileComboBox = null!;
    private TextBox _inputBox = null!;
    private bool _isInTrayMode;
    private bool _isRefreshingUiState;
    private bool _isSubmitting;
    private string _lastAssistantDetail = "Stand by.";
    private AssistantActivityKind _lastAssistantKind = AssistantActivityKind.Status;
    private string _lastAssistantStatusText = string.Empty;
    private string _lastClarificationSnapshotKey = string.Empty;
    private DateTimeOffset _lastBargeInUtc = DateTimeOffset.MinValue;
    private string _lastSessionSummary = "No active session.";
    private string _lastVoiceStatusText = string.Empty;
    private VoiceSignalSnapshot _latestSignal;
    private Label _listenIndicatorLabel = null!;
    private RichTextBox _liveResponseBox = null!;
    private CancellationTokenSource? _liveResponseCts;
    private bool _isAssistantHistoryStreamActive;
    private bool _isAssistantResponseStreaming;
    private bool _isAwaitingFollowUpInput;
    private DateTimeOffset _followUpAnswerDeadlineUtc = DateTimeOffset.MinValue;
    private TransparentTableLayoutPanel _mainColumnLayout = null!;
    private Button _micToggleButton = null!;
    private Label _modeStatusLabel = null!;
    private Button _memoryButton = null!;
    private Label _missionBriefLabel = null!;
    private string _missionBriefText = "LOCAL --:--  |  ACTIVE standby  |  OBJECTIVE Stand by for the next directive.";
    private string _currentMissionObjective = "Stand by for the next directive.";
    private Button _notesButton = null!;
    private Rectangle _operatorBounds = new(140, 80, 1500, 920);
    private PendingVoiceClarification? _pendingVoiceClarification;
    private Label _peakLabel = null!;
    private PendingApprovalAction? _pendingApproval;
    private Button _presenceButton = null!;
    private Label _promptEchoLabel = null!;
    private Button _pushToTalkButton = null!;
    private QueuedVoiceDirective? _queuedVoiceDirective;
    private TransparentTableLayoutPanel _rightColumnLayout = null!;
    private Label _rmsLabel = null!;
    private Button _sendButton = null!;
    private RichTextBox _sessionBox = null!;
    private GlassPanel _sessionSummaryCard = null!;
    private Button _statusButton = null!;
    private string _awaitingFollowUpDetail = string.Empty;
    private Label _surfaceDetailLabel = null!;
    private SurfaceMode _surfaceMode = SurfaceMode.OperatorConsole;
    private SurfaceState _surfaceState = SurfaceState.Idle;
    private Label _surfaceStateLabel = null!;
    private Panel _telemetryColumn = null!;
    private RichTextBox _timelineBox = null!;
    private Button _validateMicButton = null!;
    private Label _validationStatusLabel = null!;
    private Label _voiceStatusLabel = null!;
    private ComboBox _vendorDspProfileComboBox = null!;
    private ComboBox _wakeEngineComboBox = null!;
    private Label _wakeIndicatorLabel = null!;
    private ComboBox _wakeWordAssetComboBox = null!;
    private SignalWaveformControl _waveformControl = null!;

    private sealed record PendingVoiceClarification(string Text, string RawText, string Prompt, double? Confidence);

    private sealed record QueuedVoiceDirective(
        string Text,
        string RawText,
        double? Confidence,
        bool RequiresClarification,
        string ClarificationPrompt)
    {
        public static QueuedVoiceDirective From(VoiceCommandRecognizedEventArgs args) =>
            new(args.Text, args.RawText, args.Confidence, args.RequiresClarification, args.ClarificationPrompt);

        public VoiceCommandRecognizedEventArgs ToEventArgs() =>
            new(Text, Confidence, RequiresClarification, ClarificationPrompt, RawText);
    }

    public JarvisMainForm(JarvisDesktopContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _currentOptions = context.Options;
        _currentVoiceRuntime = context.VoiceRuntime;
        _latestSignal = _currentVoiceRuntime.CurrentSignal;
        _historySourceFont = new Font("Bahnschrift SemiBold", 9.75f, FontStyle.Bold, GraphicsUnit.Point);
        _historyMessageFont = new Font("Consolas", 10.15f, FontStyle.Regular, GraphicsUnit.Point);

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = BuildTrayText("Initializing"),
            Visible = _currentOptions.KeepRunningInTrayOnClose
        };

        _refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = 1400
        };
        _refreshTimer.Tick += OnRefreshTimerTick;

        _waveformTimer = new System.Windows.Forms.Timer
        {
            Interval = 33
        };
        _waveformTimer.Tick += (_, _) => _waveformControl.AdvanceAnimation();

        InitializeWindow();
        InitializeSurface();
        InitializeTrayIcon();
        HookUiEvents();
        HookRuntimeEvents();

        RefreshAudioDevices();
        RefreshWakeWordAssets();
        RefreshWakeEngines();
        RefreshBeamformingProfiles();
        RefreshHardwareDspProfiles();
        RefreshVendorDspProfiles();
        UpdateCalibrationUi();
        UpdateVoiceUi(_currentVoiceRuntime.CurrentStatus, appendToFeed: false);
        UpdateSignalUi(_currentVoiceRuntime.CurrentSignal);
        UpdateSpeakerUi(_context.Speaker.CurrentStatus);
        UpdateSurfaceState(SurfaceState.Idle, "Stand by for voice, operator, or background automation.", playCue: false);
        UpdateModeStatusLabel();
        ApplySurfaceMode(_surfaceMode);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _refreshTimer.Start();
        _waveformTimer.Start();
        _ = InitializeRuntimeAsync();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (_currentOptions.KeepRunningInTrayOnClose
            && WindowState == FormWindowState.Minimized
            && !_isInTrayMode)
        {
            SendToTray("window minimized");
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowExit
            && _currentOptions.KeepRunningInTrayOnClose
            && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            SendToTray("window close");
            return;
        }

        _allowExit = true;
        base.OnFormClosing(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var backgroundBrush = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(7, 14, 24),
            Color.FromArgb(4, 9, 16),
            90f);
        e.Graphics.FillRectangle(backgroundBrush, ClientRectangle);

        using var gridPen = new Pen(Color.FromArgb(18, 38, 58), 1f);

        for (var x = 0; x < ClientRectangle.Width; x += 28)
        {
            e.Graphics.DrawLine(gridPen, x, 0, x, ClientRectangle.Height);
        }

        for (var y = 0; y < ClientRectangle.Height; y += 28)
        {
            e.Graphics.DrawLine(gridPen, 0, y, ClientRectangle.Width, y);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _allowExit = true;
            _refreshTimer.Stop();
            _waveformTimer.Stop();
            _refreshTimer.Dispose();
            _waveformTimer.Dispose();

            _context.Assistant.Activity -= OnAssistantActivity;
            _context.Speaker.StatusChanged -= OnSpeakerStatusChanged;

            _currentVoiceRuntime.StatusChanged -= OnVoiceStatusChanged;
            _currentVoiceRuntime.SignalLevelChanged -= OnVoiceSignalChanged;
            _currentVoiceRuntime.CommandRecognized -= OnVoiceCommandRecognized;
            _currentVoiceRuntime.ActivationRequested -= OnVoiceActivationRequested;
            _currentVoiceRuntime.BargeInRequested -= OnVoiceBargeInRequested;

            try
            {
                _currentVoiceRuntime.Dispose();
            }
            catch
            {
            }

            _liveResponseCts?.Cancel();
            _liveResponseCts?.Dispose();
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _optionsPersistenceGate.Dispose();

            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _actionCueOverlay.Dispose();
            _historySourceFont.Dispose();
            _historyMessageFont.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeWindow()
    {
        SuspendLayout();
        Text = $"{_currentOptions.AssistantName} Operations";
        StartPosition = FormStartPosition.Manual;
        Bounds = _operatorBounds;
        MinimumSize = new Size(1080, 760);
        BackColor = Color.FromArgb(7, 14, 24);
        ForeColor = Color.FromArgb(237, 244, 252);
        DoubleBuffered = true;
        KeyPreview = true;
        ShowIcon = true;
        ResumeLayout(false);
    }

    private void InitializeSurface()
    {
        SuspendLayout();

        _contentLayout = new TransparentTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(22, 10, 22, 0),
            Margin = Padding.Empty
        };
        _contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63f));
        _contentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37f));

        _mainColumnLayout = new TransparentTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 12, 0)
        };
        _mainColumnLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 278f));
        _mainColumnLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var liveConsoleCard = BuildLiveConsoleCard();
        _historyCard = BuildHistoryCard();
        _mainColumnLayout.Controls.Add(liveConsoleCard, 0, 0);
        _mainColumnLayout.Controls.Add(_historyCard, 0, 1);

        _telemetryColumn = new TransparentPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12, 0, 0, 0),
            Margin = Padding.Empty
        };

        _rightColumnLayout = new TransparentTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty
        };
        _rightColumnLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 452f));
        _rightColumnLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
        _rightColumnLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 54f));
        _rightColumnLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 46f));

        var signalCard = BuildSignalCard();
        _approvalPanel = BuildApprovalCard();
        var activitiesCard = BuildActivitiesCard();
        var timelineCard = BuildTimelineCard();

        _rightColumnLayout.Controls.Add(signalCard, 0, 0);
        _rightColumnLayout.Controls.Add(_approvalPanel, 0, 1);
        _rightColumnLayout.Controls.Add(activitiesCard, 0, 2);
        _rightColumnLayout.Controls.Add(timelineCard, 0, 3);
        _telemetryColumn.Controls.Add(_rightColumnLayout);

        _contentLayout.Controls.Add(_mainColumnLayout, 0, 0);
        _contentLayout.Controls.Add(_telemetryColumn, 1, 0);

        _composerPanel = BuildComposerPanel();

        Controls.Add(_contentLayout);
        Controls.Add(_composerPanel);
        Controls.Add(BuildHeaderPanel());

        ResumeLayout(true);
    }

    private void InitializeTrayIcon()
    {
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Open", null, (_, _) => BringAssistantToFront("tray menu"));
        trayMenu.Items.Add("Hide", null, (_, _) => SendToTray("tray menu"));
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());

        _trayIcon.ContextMenuStrip = trayMenu;
        _trayIcon.DoubleClick += (_, _) => BringAssistantToFront("tray double click");
    }

    private void HookUiEvents()
    {
        _sendButton.Click += async (_, _) => await SubmitPromptAsync();
        _helpButton.Click += async (_, _) => await ProcessPromptAsync("help", "SURFACE", clearInput: false, displayedInput: "Requested the help briefing.");
        _statusButton.Click += async (_, _) => await ProcessPromptAsync("status", "SURFACE", clearInput: false, displayedInput: "Requested the current runtime status.");
        _notesButton.Click += async (_, _) => await ProcessPromptAsync("notes", "SURFACE", clearInput: false, displayedInput: "Requested notes and memory context.");
        _memoryButton.Click += async (_, _) => await OpenMemoryWorkbenchAsync();
        _presenceButton.Click += async (_, _) => await OpenPresenceWorkbenchAsync();
        _approveButton.Click += async (_, _) => await HandleApprovalDecisionAsync(true);
        _denyButton.Click += async (_, _) => await HandleApprovalDecisionAsync(false);
        _micToggleButton.Click += async (_, _) => await ToggleMicrophoneAsync();
        _pushToTalkButton.Click += async (_, _) => await TogglePushToTalkAsync();
        _deviceRefreshButton.Click += (_, _) => RefreshAudioDevices();
        _calibrateMicButton.Click += async (_, _) => await CalibrateMicrophoneAsync();
        _validateMicButton.Click += async (_, _) => await ValidateMicrophoneAsync();
        _inputBox.KeyDown += InputBoxOnKeyDown;
    }

    private void HookRuntimeEvents()
    {
        _context.Assistant.Activity += OnAssistantActivity;
        _context.Speaker.StatusChanged += OnSpeakerStatusChanged;
        _currentVoiceRuntime.StatusChanged += OnVoiceStatusChanged;
        _currentVoiceRuntime.SignalLevelChanged += OnVoiceSignalChanged;
        _currentVoiceRuntime.CommandRecognized += OnVoiceCommandRecognized;
        _currentVoiceRuntime.ActivationRequested += OnVoiceActivationRequested;
        _currentVoiceRuntime.BargeInRequested += OnVoiceBargeInRequested;
        _currentVoiceRuntime.SetPlaybackState(_context.Speaker.CurrentStatus.IsSpeaking);
    }

    private async Task InitializeRuntimeAsync()
    {
        try
        {
            await RefreshRuntimeStateAsync();
        }
        catch
        {
        }

        try
        {
            await _currentVoiceRuntime.StartAsync(_cancellationTokenSource.Token);
        }
        catch (Exception exception)
        {
            AppendSystemMessage($"Voice runtime failed to start: {exception.Message}");
        }
    }

    private void ReplaceSpeaker(JarvisOptions options)
    {
        _context.Options = options;
        _context.Speaker.StatusChanged -= OnSpeakerStatusChanged;
        _context.Speaker = SpeakerFactory.Create(options);
        _context.Speaker.StatusChanged += OnSpeakerStatusChanged;
        UpdateSpeakerUi(_context.Speaker.CurrentStatus);
    }

    private bool CanUseUi()
    {
        return !IsDisposed && !Disposing && IsHandleCreated;
    }

    private string BuildTrayText(string status)
    {
        var text = $"{_currentOptions.AssistantName}: {TrimForSingleLine(status, 52)}";
        return text.Length <= 63 ? text : text[..63];
    }

    private void BringAssistantToFront(string reason)
    {
        if (_isInTrayMode)
        {
            Show();
            ApplySurfaceMode(_surfaceMode);
            _isInTrayMode = false;
        }

        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }

        ShowInTaskbar = true;
        Activate();
        BringToFront();

        if (CanUseUi())
        {
            AppendTimelineEntry(
                "SURFACE",
                "Operator surface brought to the foreground.",
                $"Trigger: {TrimForSingleLine(reason, 96)}.",
                Color.FromArgb(132, 196, 255));
        }
    }

    private void SendToTray(string reason)
    {
        if (_isInTrayMode)
        {
            return;
        }

        _isInTrayMode = true;
        ShowInTaskbar = false;
        _trayIcon.Visible = true;
        Hide();

        if (CanUseUi())
        {
            AppendTimelineEntry(
                "SURFACE",
                "Jarvis moved to tray mode.",
                $"Trigger: {TrimForSingleLine(reason, 96)}. Voice runtime remains available.",
                Color.FromArgb(255, 191, 105));
        }
    }

    private void ExitApplication()
    {
        if (_allowExit)
        {
            return;
        }

        _allowExit = true;
        _trayIcon.Visible = false;
        Close();
    }
}
