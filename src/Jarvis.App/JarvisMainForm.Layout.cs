using Jarvis.Core;
using Jarvis.App;
using System.Windows.Forms;
using System.Drawing;

namespace Jarvis.App;

internal sealed partial class JarvisMainForm
{
    private GlassPanel BuildHeaderPanel()
    {
        var headerPanel = new GlassPanel
        {
            Dock = DockStyle.Top,
            Height = 188,
            Margin = Padding.Empty,
            Padding = new Padding(24, 18, 24, 18),
            SurfaceColor = Color.FromArgb(14, 24, 39),
            SurfaceColor2 = Color.FromArgb(7, 16, 28),
            BorderColor = Color.FromArgb(42, 78, 106),
            AccentColor = Color.FromArgb(106, 220, 197)
        };

        var layout = new TransparentTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38f));

        var leftLayout = new TransparentTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Margin = Padding.Empty
        };
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var titleLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(240, 248, 255),
            Font = new Font("Bahnschrift SemiBold", 29f, FontStyle.Bold, GraphicsUnit.Point),
            Text = _currentOptions.AssistantName.ToUpperInvariant()
        };

        var subtitleLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(163, 188, 210),
            Font = new Font("Consolas", 10.75f, FontStyle.Regular, GraphicsUnit.Point),
            MaximumSize = new Size(760, 0),
            Text = $"LIVE OPERATIONS SURFACE  |  WORKSPACE {TrimForSingleLine(_context.WorkspaceRoot, 72)}"
        };

        _surfaceStateLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(255, 191, 105),
            Font = new Font("Bahnschrift SemiBold", 18f, FontStyle.Bold, GraphicsUnit.Point),
            Text = "IDLE"
        };

        _surfaceDetailLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(185, 205, 225),
            Font = new Font("Consolas", 10.25f, FontStyle.Regular, GraphicsUnit.Point),
            MaximumSize = new Size(760, 0),
            Text = "Stand by."
        };

        _missionBriefLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(163, 201, 233),
            Font = new Font("Consolas", 9.5f, FontStyle.Bold, GraphicsUnit.Point),
            MaximumSize = new Size(760, 0),
            Margin = new Padding(0, 4, 0, 0),
            Text = _missionBriefText
        };

        var capabilityFlow = new TransparentFlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        capabilityFlow.Controls.Add(CreateInfoChip(_currentOptions.VoiceEnabled ? "VOICE ON" : "VOICE OFF", Color.FromArgb(255, 191, 105)));
        capabilityFlow.Controls.Add(CreateInfoChip(_currentOptions.SpeechRecognitionEnabled ? "LIVE MIC" : "MIC OFF", Color.FromArgb(106, 220, 197)));
        capabilityFlow.Controls.Add(CreateInfoChip(_currentOptions.WakeWordEnabled ? "WAKE GATE" : "OPEN MIC", Color.FromArgb(132, 196, 255)));
        capabilityFlow.Controls.Add(CreateInfoChip(_currentOptions.ShellExecutionEnabled ? "TOOLS LIVE" : "SHELL LOCKED", Color.FromArgb(255, 138, 101)));

        var stageFlow = new TransparentFlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 10, 0, 0)
        };
        AddStageLabel(stageFlow, SurfaceState.Listening, "LISTEN");
        AddStageLabel(stageFlow, SurfaceState.Thinking, "THINK");
        AddStageLabel(stageFlow, SurfaceState.Acting, "ACT");
        AddStageLabel(stageFlow, SurfaceState.Speaking, "SPEAK");

        leftLayout.Controls.Add(titleLabel, 0, 0);
        leftLayout.Controls.Add(subtitleLabel, 0, 1);
        leftLayout.Controls.Add(_surfaceStateLabel, 0, 2);
        leftLayout.Controls.Add(_surfaceDetailLabel, 0, 3);
        leftLayout.Controls.Add(_missionBriefLabel, 0, 4);
        leftLayout.Controls.Add(capabilityFlow, 0, 5);
        leftLayout.Controls.Add(stageFlow, 0, 6);

        var rightLayout = new TransparentTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var modeHeader = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(200, 216, 232),
            Font = new Font("Bahnschrift SemiBold", 11f, FontStyle.Bold, GraphicsUnit.Point),
            Text = "Surface Modes"
        };

        _modeStatusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(162, 186, 208),
            Font = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Point),
            MaximumSize = new Size(420, 0),
            Text = "Operator console on the primary display."
        };

        var modeFlow = new TransparentFlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 82,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 8, 0, 0)
        };
        AddModeButton(modeFlow, SurfaceMode.OperatorConsole, "Operator");
        AddModeButton(modeFlow, SurfaceMode.VoiceOnly, "Voice");
        AddModeButton(modeFlow, SurfaceMode.OverlayHud, "Overlay");
        AddModeButton(modeFlow, SurfaceMode.EdgeDock, "Edge");
        AddModeButton(modeFlow, SurfaceMode.Dashboard, "Dashboard");

        rightLayout.Controls.Add(modeHeader, 0, 0);
        rightLayout.Controls.Add(_modeStatusLabel, 0, 1);
        rightLayout.Controls.Add(modeFlow, 0, 2);

        layout.Controls.Add(leftLayout, 0, 0);
        layout.Controls.Add(rightLayout, 1, 0);
        headerPanel.Controls.Add(layout);
        return headerPanel;
    }

    private GlassPanel BuildLiveConsoleCard()
    {
        var card = CreateSurfaceCard();

        var titleLabel = CreateSectionTitle("Live Response Stream");
        titleLabel.Dock = DockStyle.Top;

        _promptEchoLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 30,
            ForeColor = Color.FromArgb(255, 191, 105),
            Font = new Font("Consolas", 10f, FontStyle.Bold, GraphicsUnit.Point),
            Text = "LATEST DIRECTIVE  |  Awaiting input."
        };

        var statusStack = new TransparentPanel
        {
            Dock = DockStyle.Bottom,
            Height = 58
        };

        _assistantStatusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            ForeColor = Color.FromArgb(163, 201, 233),
            Font = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Point),
            Text = "Assistant runtime idle."
        };

        _voiceStatusLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            ForeColor = Color.FromArgb(170, 191, 214),
            Font = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Point),
            Text = "Speech recognition initializing."
        };

        statusStack.Controls.Add(_assistantStatusLabel);
        statusStack.Controls.Add(_voiceStatusLabel);

        _liveResponseBox = CreateTelemetryBox();
        _liveResponseBox.Dock = DockStyle.Fill;
        _liveResponseBox.Font = new Font("Consolas", 11.5f, FontStyle.Regular, GraphicsUnit.Point);
        _liveResponseBox.Text = "Response renderer ready.";

        card.Controls.Add(_liveResponseBox);
        card.Controls.Add(statusStack);
        card.Controls.Add(_promptEchoLabel);
        card.Controls.Add(titleLabel);
        titleLabel.BringToFront();
        _promptEchoLabel.BringToFront();
        return card;
    }

    private Panel BuildHistoryCard()
    {
        var card = CreateSurfaceCard();
        var titleLabel = CreateSectionTitle("Conversation Feed");
        titleLabel.Dock = DockStyle.Top;

        _historyBox = CreateTelemetryBox();
        _historyBox.Dock = DockStyle.Fill;
        _historyBox.Font = _historyMessageFont;

        card.Controls.Add(_historyBox);
        card.Controls.Add(titleLabel);
        titleLabel.BringToFront();
        return card;
    }

    private GlassPanel BuildSignalCard()
    {
        var card = CreateSurfaceCard();

        var titleLabel = CreateSectionTitle("Signal + Control");
        titleLabel.Dock = DockStyle.Top;

        var topStatus = new Label
        {
            Dock = DockStyle.Top,
            Height = 24,
            ForeColor = Color.FromArgb(170, 191, 214),
            Font = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Point),
            Text = "Waveform, mic levels, wake assets, DSP tuning, and launcher controls."
        };

        _waveformControl = new SignalWaveformControl
        {
            Dock = DockStyle.Top,
            Height = 116,
            Margin = new Padding(0, 8, 0, 8)
        };

        var metricsFlow = new TransparentFlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 30,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _rmsLabel = CreateMetricLabel("RMS 0%");
        _peakLabel = CreateMetricLabel("PEAK 0%");
        _listenIndicatorLabel = CreateMetricLabel("LISTEN STANDBY");
        _wakeIndicatorLabel = CreateMetricLabel(_currentOptions.WakeWordEnabled ? "WAKE GATE" : "OPEN MIC");
        metricsFlow.Controls.Add(_rmsLabel);
        metricsFlow.Controls.Add(_peakLabel);
        metricsFlow.Controls.Add(_listenIndicatorLabel);
        metricsFlow.Controls.Add(_wakeIndicatorLabel);

        var deviceFlow = new TransparentFlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        var microphoneLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(183, 204, 223),
            Font = new Font("Bahnschrift SemiBold", 10f, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 10, 10, 0),
            Text = "Input:"
        };
        _deviceComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 216,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(10, 18, 30),
            ForeColor = Color.FromArgb(237, 244, 252),
            Font = new Font("Bahnschrift", 10f, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 4, 8, 0)
        };
        _deviceRefreshButton = CreateActionButton("Refresh");
        _deviceRefreshButton.Height = 30;
        _deviceRefreshButton.Padding = new Padding(14, 0, 14, 0);
        _calibrateMicButton = CreateActionButton("Calibrate");
        _calibrateMicButton.Height = 30;
        _calibrateMicButton.Padding = new Padding(14, 0, 14, 0);
        _validateMicButton = CreateActionButton("Validate");
        _validateMicButton.Height = 30;
        _validateMicButton.Padding = new Padding(14, 0, 14, 0);
        deviceFlow.Controls.Add(microphoneLabel);
        deviceFlow.Controls.Add(_deviceComboBox);
        deviceFlow.Controls.Add(_deviceRefreshButton);
        deviceFlow.Controls.Add(_calibrateMicButton);
        deviceFlow.Controls.Add(_validateMicButton);

        var tuningFlow = new TransparentFlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 142,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        var beamformingLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(183, 204, 223),
            Font = new Font("Bahnschrift SemiBold", 10f, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 10, 10, 0),
            Text = "Beam:"
        };
        _beamformingProfileComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 162,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(10, 18, 30),
            ForeColor = Color.FromArgb(237, 244, 252),
            Font = new Font("Bahnschrift", 10f, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 4, 8, 0)
        };
        var hardwareDspLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(183, 204, 223),
            Font = new Font("Bahnschrift SemiBold", 10f, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 10, 10, 0),
            Text = "DSP:"
        };
        _hardwareDspProfileComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 154,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(10, 18, 30),
            ForeColor = Color.FromArgb(237, 244, 252),
            Font = new Font("Bahnschrift", 10f, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 4, 8, 0)
        };
        var wakeAssetLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(183, 204, 223),
            Font = new Font("Bahnschrift SemiBold", 10f, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 10, 10, 0),
            Text = "Wake:"
        };
        _wakeWordAssetComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 180,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(10, 18, 30),
            ForeColor = Color.FromArgb(237, 244, 252),
            Font = new Font("Bahnschrift", 10f, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 4, 8, 0)
        };
        var wakeEngineLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(183, 204, 223),
            Font = new Font("Bahnschrift SemiBold", 10f, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 10, 10, 0),
            Text = "Engine:"
        };
        _wakeEngineComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 170,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(10, 18, 30),
            ForeColor = Color.FromArgb(237, 244, 252),
            Font = new Font("Bahnschrift", 10f, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 4, 8, 0)
        };
        var vendorDspLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(183, 204, 223),
            Font = new Font("Bahnschrift SemiBold", 10f, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 10, 10, 0),
            Text = "Vendor:"
        };
        _vendorDspProfileComboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 170,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(10, 18, 30),
            ForeColor = Color.FromArgb(237, 244, 252),
            Font = new Font("Bahnschrift", 10f, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 4, 8, 0)
        };
        _calibrationStatusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(163, 188, 210),
            Font = new Font("Consolas", 9.4f, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 6, 12, 0),
            Text = "No calibration saved."
        };
        _validationStatusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(163, 188, 210),
            Font = new Font("Consolas", 9.4f, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 6, 0, 0),
            Text = "No validation saved."
        };
        tuningFlow.Controls.Add(beamformingLabel);
        tuningFlow.Controls.Add(_beamformingProfileComboBox);
        tuningFlow.Controls.Add(hardwareDspLabel);
        tuningFlow.Controls.Add(_hardwareDspProfileComboBox);
        tuningFlow.Controls.Add(wakeAssetLabel);
        tuningFlow.Controls.Add(_wakeWordAssetComboBox);
        tuningFlow.Controls.Add(wakeEngineLabel);
        tuningFlow.Controls.Add(_wakeEngineComboBox);
        tuningFlow.Controls.Add(vendorDspLabel);
        tuningFlow.Controls.Add(_vendorDspProfileComboBox);
        tuningFlow.Controls.Add(_calibrationStatusLabel);
        tuningFlow.Controls.Add(_validationStatusLabel);

        var speakerFlow = new TransparentFlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 38,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        var volumeLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(183, 204, 223),
            Font = new Font("Bahnschrift SemiBold", 10f, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 10, 10, 0),
            Text = "Vol:"
        };
        var volumeTrackBar = new TrackBar
        {
            Width = 140,
            Height = 28,
            Minimum = 10,
            Maximum = 200,
            Value = Math.Clamp((int)Math.Round(_currentOptions.SpeakerVolumeMultiplier * 100), 10, 200),
            TickStyle = TickStyle.None,
            Margin = new Padding(0, 8, 8, 0)
        };
        volumeTrackBar.ValueChanged += async (_, _) =>
        {
            try
            {
                await UpdateSpeakerVolumeAsync(volumeTrackBar.Value / 100.0);
            }
            catch (Exception exception)
            {
                AppendSystemMessage($"Speaker volume update failed: {exception.Message}");
            }
        };
        var whisperCheckBox = new CheckBox
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(237, 244, 252),
            Text = "Whisper",
            Checked = _currentOptions.WhisperModeEnabled,
            Margin = new Padding(0, 8, 0, 0)
        };
        whisperCheckBox.CheckedChanged += async (_, _) =>
        {
            try
            {
                await UpdateWhisperModeAsync(whisperCheckBox.Checked);
            }
            catch (Exception exception)
            {
                AppendSystemMessage($"Whisper mode update failed: {exception.Message}");
            }
        };
        speakerFlow.Controls.Add(volumeLabel);
        speakerFlow.Controls.Add(volumeTrackBar);
        speakerFlow.Controls.Add(whisperCheckBox);

        var actionFlow = new TransparentFlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 40,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _helpButton = CreateActionButton("Help");
        _statusButton = CreateActionButton("Status");
        _notesButton = CreateActionButton("Notes");
        _memoryButton = CreateActionButton("Memory");
        _micToggleButton = CreateActionButton("Mute Mic");
        _pushToTalkButton = CreateActionButton("Push-to-Talk");
        _pushToTalkButton.Visible = _currentOptions.SpeechRecognitionPushToTalkEnabled;
        actionFlow.Controls.Add(_helpButton);
        actionFlow.Controls.Add(_statusButton);
        actionFlow.Controls.Add(_notesButton);
        actionFlow.Controls.Add(_memoryButton);
        actionFlow.Controls.Add(_micToggleButton);
        actionFlow.Controls.Add(_pushToTalkButton);

        card.Controls.Add(actionFlow);
        card.Controls.Add(speakerFlow);
        card.Controls.Add(tuningFlow);
        card.Controls.Add(deviceFlow);
        card.Controls.Add(metricsFlow);
        card.Controls.Add(_waveformControl);
        card.Controls.Add(topStatus);
        card.Controls.Add(titleLabel);
        titleLabel.BringToFront();
        return card;
    }

    private Panel BuildApprovalCard()
    {
        var card = CreateSurfaceCard();
        card.Visible = false;

        var titleLabel = CreateSectionTitle("Approval Required");
        titleLabel.Dock = DockStyle.Top;
        titleLabel.ForeColor = Color.FromArgb(255, 138, 101);

        _approvalSummaryLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 34,
            ForeColor = Color.FromArgb(255, 215, 200),
            Font = new Font("Bahnschrift SemiBold", 11f, FontStyle.Bold, GraphicsUnit.Point),
            Text = "A major action is waiting for approval."
        };

        _approvalPromptLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(188, 206, 224),
            Font = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Point),
            Text = "Review the planned change.",
            AutoEllipsis = true
        };

        var buttonFlow = new TransparentFlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 42
        };
        _approveButton = CreateAccentButton("Approve", Color.FromArgb(106, 220, 197));
        _denyButton = CreateAccentButton("Deny", Color.FromArgb(255, 138, 101));
        buttonFlow.Controls.Add(_approveButton);
        buttonFlow.Controls.Add(_denyButton);

        card.Controls.Add(_approvalPromptLabel);
        card.Controls.Add(buttonFlow);
        card.Controls.Add(_approvalSummaryLabel);
        card.Controls.Add(titleLabel);
        titleLabel.BringToFront();
        return card;
    }

    private GlassPanel BuildActivitiesCard()
    {
        var card = CreateSurfaceCard();
        var titleLabel = CreateSectionTitle("Activities + Sessions");
        titleLabel.Dock = DockStyle.Top;

        _activityFlow = new BufferedFlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 4, 4, 0)
        };
        _activityFlow.Resize += (_, _) => UpdateActivityCardWidths();

        _sessionSummaryCard = new GlassPanel
        {
            Width = 10,
            Height = 174,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(14),
            SurfaceColor = Color.FromArgb(18, 31, 50),
            SurfaceColor2 = Color.FromArgb(11, 22, 38),
            AccentColor = Color.FromArgb(132, 196, 255),
            BorderColor = Color.FromArgb(43, 78, 111)
        };

        var sessionTitle = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = Color.FromArgb(208, 226, 244),
            Font = new Font("Bahnschrift SemiBold", 11f, FontStyle.Bold, GraphicsUnit.Point),
            Text = "Plan / Result Snapshot"
        };

        _backgroundLabel = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            ForeColor = Color.FromArgb(164, 189, 212),
            Font = new Font("Consolas", 9.5f, FontStyle.Regular, GraphicsUnit.Point),
            Text = "Background queue clear.",
            AutoEllipsis = true
        };

        _sessionBox = CreateTelemetryBox();
        _sessionBox.Dock = DockStyle.Fill;
        _sessionBox.Font = new Font("Consolas", 9.9f, FontStyle.Regular, GraphicsUnit.Point);
        _sessionBox.Text = _lastSessionSummary;

        _sessionSummaryCard.Controls.Add(_sessionBox);
        _sessionSummaryCard.Controls.Add(_backgroundLabel);
        _sessionSummaryCard.Controls.Add(sessionTitle);
        sessionTitle.BringToFront();

        _activityFlow.Controls.Add(_sessionSummaryCard);

        card.Controls.Add(_activityFlow);
        card.Controls.Add(titleLabel);
        titleLabel.BringToFront();
        return card;
    }

    private GlassPanel BuildTimelineCard()
    {
        var card = CreateSurfaceCard();
        var titleLabel = CreateSectionTitle("Timeline + Justification");
        titleLabel.Dock = DockStyle.Top;

        _timelineBox = CreateTelemetryBox();
        _timelineBox.Dock = DockStyle.Fill;
        _timelineBox.Font = new Font("Consolas", 9.85f, FontStyle.Regular, GraphicsUnit.Point);

        card.Controls.Add(_timelineBox);
        card.Controls.Add(titleLabel);
        titleLabel.BringToFront();
        return card;
    }

    private TransparentPanel BuildComposerPanel()
    {
        var wrapper = new TransparentPanel
        {
            Dock = DockStyle.Bottom,
            Height = 136,
            Padding = new Padding(22, 10, 22, 22)
        };

        var card = CreateSurfaceCard();
        card.Dock = DockStyle.Fill;
        card.Padding = new Padding(18, 14, 18, 16);

        var promptLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            ForeColor = Color.FromArgb(164, 189, 212),
            Font = new Font("Bahnschrift SemiBold", 10.5f, FontStyle.Bold, GraphicsUnit.Point),
            Text = "Directive input  |  Type, approve, deny, or let voice drive the loop."
        };

        _inputBox = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Multiline = true,
            AcceptsReturn = true,
            BackColor = Color.FromArgb(10, 18, 30),
            ForeColor = Color.FromArgb(240, 248, 255),
            Font = new Font("Bahnschrift", 12f, FontStyle.Regular, GraphicsUnit.Point)
        };

        _sendButton = CreateAccentButton("Transmit", Color.FromArgb(106, 220, 197));
        _sendButton.Width = 156;
        _sendButton.Margin = new Padding(0, 0, 0, 8);

        _presenceButton = CreateActionButton("Presence");
        _presenceButton.Width = 156;
        _presenceButton.Margin = Padding.Empty;

        var buttonStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Width = 166,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        buttonStack.Controls.Add(_sendButton);
        buttonStack.Controls.Add(_presenceButton);

        var editorLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        editorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        editorLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170f));
        editorLayout.Controls.Add(_inputBox, 0, 0);
        editorLayout.Controls.Add(buttonStack, 1, 0);

        var editorHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 18, 30),
            Padding = new Padding(12, 12, 12, 10)
        };

        editorHost.Controls.Add(editorLayout);
        card.Controls.Add(editorHost);
        card.Controls.Add(promptLabel);
        promptLabel.BringToFront();
        wrapper.Controls.Add(card);
        return wrapper;
    }

    private void AddModeButton(FlowLayoutPanel flow, SurfaceMode mode, string label)
    {
        var button = new Button
        {
            Width = 106,
            Height = 34,
            Margin = new Padding(0, 0, 10, 8),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(14, 28, 42),
            ForeColor = Color.FromArgb(230, 238, 248),
            Font = new Font("Bahnschrift SemiBold", 10.25f, FontStyle.Bold, GraphicsUnit.Point),
            Text = label,
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(44, 78, 106);
        button.FlatAppearance.BorderSize = 1;
        button.Click += (_, _) => ApplySurfaceMode(mode);
        _modeButtons[mode] = button;
        flow.Controls.Add(button);
    }

    private void ApplySurfaceMode(SurfaceMode mode)
    {
        if (_surfaceMode == mode && !_isInTrayMode)
        {
            return;
        }

        if (_surfaceMode == SurfaceMode.OperatorConsole)
        {
            _operatorBounds = Bounds;
        }

        if (mode == SurfaceMode.Dashboard && Screen.AllScreens.Length < 2)
        {
            AppendSystemMessage("Second-screen dashboard requested, but only one display is available.");
            AppendTimelineEntry("DISPLAY", "Second-screen dashboard unavailable.", "A second monitor was not detected.", Color.FromArgb(255, 138, 101));
            mode = SurfaceMode.OperatorConsole;
        }

        _surfaceMode = mode;
        _contentLayout.SuspendLayout();
        _mainColumnLayout.SuspendLayout();

        var targetScreen = mode == SurfaceMode.Dashboard && Screen.AllScreens.Length > 1
            ? Screen.AllScreens[1]
            : Screen.FromControl(this);
        var workingArea = targetScreen.WorkingArea;

        switch (mode)
        {
            case SurfaceMode.OperatorConsole:
                FormBorderStyle = FormBorderStyle.Sizable;
                ShowInTaskbar = true;
                TopMost = false;
                Opacity = 1.0;
                _contentLayout.ColumnStyles[0].Width = 63f;
                _contentLayout.ColumnStyles[1].Width = 37f;
                _mainColumnLayout.RowStyles[0].SizeType = SizeType.Absolute;
                _mainColumnLayout.RowStyles[0].Height = 278f;
                _mainColumnLayout.RowStyles[1].SizeType = SizeType.Percent;
                _mainColumnLayout.RowStyles[1].Height = 100f;
                _historyCard.Visible = true;
                _telemetryColumn.Visible = true;
                _composerPanel.Visible = true;
                Bounds = _operatorBounds;
                break;

            case SurfaceMode.VoiceOnly:
                FormBorderStyle = FormBorderStyle.FixedSingle;
                ShowInTaskbar = true;
                TopMost = false;
                Opacity = 1.0;
                _contentLayout.ColumnStyles[0].Width = 100f;
                _contentLayout.ColumnStyles[1].Width = 0f;
                _historyCard.Visible = false;
                _telemetryColumn.Visible = false;
                _composerPanel.Visible = false;
                _mainColumnLayout.RowStyles[0].SizeType = SizeType.Percent;
                _mainColumnLayout.RowStyles[0].Height = 100f;
                _mainColumnLayout.RowStyles[1].SizeType = SizeType.Absolute;
                _mainColumnLayout.RowStyles[1].Height = 0f;
                Bounds = CenteredBounds(workingArea, 760, 420);
                break;

            case SurfaceMode.OverlayHud:
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = true;
                TopMost = true;
                Opacity = 0.94;
                _contentLayout.ColumnStyles[0].Width = 100f;
                _contentLayout.ColumnStyles[1].Width = 0f;
                _historyCard.Visible = false;
                _telemetryColumn.Visible = false;
                _composerPanel.Visible = false;
                _mainColumnLayout.RowStyles[0].SizeType = SizeType.Percent;
                _mainColumnLayout.RowStyles[0].Height = 100f;
                _mainColumnLayout.RowStyles[1].SizeType = SizeType.Absolute;
                _mainColumnLayout.RowStyles[1].Height = 0f;
                Bounds = new Rectangle(workingArea.Right - 600, workingArea.Top + 32, 560, 430);
                break;

            case SurfaceMode.EdgeDock:
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                Opacity = 0.92;
                _contentLayout.ColumnStyles[0].Width = 100f;
                _contentLayout.ColumnStyles[1].Width = 0f;
                _historyCard.Visible = false;
                _telemetryColumn.Visible = false;
                _composerPanel.Visible = false;
                _mainColumnLayout.RowStyles[0].SizeType = SizeType.Percent;
                _mainColumnLayout.RowStyles[0].Height = 100f;
                _mainColumnLayout.RowStyles[1].SizeType = SizeType.Absolute;
                _mainColumnLayout.RowStyles[1].Height = 0f;
                Bounds = new Rectangle(workingArea.Right - 360, workingArea.Top + 18, 340, Math.Max(360, workingArea.Height - 36));
                break;

            case SurfaceMode.Dashboard:
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = true;
                TopMost = false;
                Opacity = 0.98;
                _contentLayout.ColumnStyles[0].Width = 58f;
                _contentLayout.ColumnStyles[1].Width = 42f;
                _historyCard.Visible = true;
                _telemetryColumn.Visible = true;
                _composerPanel.Visible = false;
                _mainColumnLayout.RowStyles[0].SizeType = SizeType.Absolute;
                _mainColumnLayout.RowStyles[0].Height = 250f;
                _mainColumnLayout.RowStyles[1].SizeType = SizeType.Percent;
                _mainColumnLayout.RowStyles[1].Height = 100f;
                Bounds = workingArea;
                break;
        }

        _mainColumnLayout.ResumeLayout(true);
        _contentLayout.ResumeLayout(true);
        UpdateSurfaceModeButtons();
        UpdateModeStatusLabel();
    }

    private void UpdateSurfaceModeButtons()
    {
        foreach (var pair in _modeButtons)
        {
            var active = pair.Key == _surfaceMode;
            pair.Value.BackColor = active ? Color.FromArgb(24, 44, 64) : Color.FromArgb(14, 28, 42);
            pair.Value.ForeColor = active ? GetStateAccent(_surfaceState) : Color.FromArgb(230, 238, 248);
            pair.Value.FlatAppearance.BorderColor = active ? GetStateAccent(_surfaceState) : Color.FromArgb(44, 78, 106);
        }
    }

    private void UpdateModeStatusLabel()
    {
        _modeStatusLabel.Text = _surfaceMode switch
        {
            SurfaceMode.VoiceOnly => "Compact voice-only mode. Overlay, timeline, and composer are hidden.",
            SurfaceMode.OverlayHud => "Floating HUD mode. Top-most window with compact streaming telemetry.",
            SurfaceMode.EdgeDock => "Transparent edge rail. Docked at the display perimeter for passive monitoring.",
            SurfaceMode.Dashboard => "Second-screen dashboard. Expanded ops surface for dedicated monitoring.",
            _ => "Operator console on the primary display with full tools, timeline, and approval controls."
        };
    }

    private void AddStageLabel(FlowLayoutPanel flow, SurfaceState state, string label)
    {
        var stageLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 10, 0),
            Padding = new Padding(12, 6, 12, 6),
            BackColor = Color.FromArgb(12, 20, 34),
            ForeColor = Color.FromArgb(109, 130, 152),
            Font = new Font("Consolas", 10f, FontStyle.Bold, GraphicsUnit.Point),
            Text = label
        };

        _stageLabels[state] = stageLabel;
        flow.Controls.Add(stageLabel);
    }

    private static RichTextBox CreateTelemetryBox()
    {
        return new RichTextBox
        {
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(10, 18, 30),
            ForeColor = Color.FromArgb(235, 243, 252),
            ScrollBars = RichTextBoxScrollBars.Vertical,
            DetectUrls = false
        };
    }

    private static GlassPanel CreateSurfaceCard()
    {
        return new GlassPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(18),
            SurfaceColor = Color.FromArgb(14, 24, 40),
            SurfaceColor2 = Color.FromArgb(9, 17, 29),
            BorderColor = Color.FromArgb(40, 72, 102),
            AccentColor = Color.FromArgb(106, 220, 197)
        };
    }

    private static Label CreateSectionTitle(string text)
    {
        return new Label
        {
            Height = 24,
            ForeColor = Color.FromArgb(214, 229, 242),
            Font = new Font("Bahnschrift SemiBold", 11f, FontStyle.Bold, GraphicsUnit.Point),
            Text = text
        };
    }

    private static Label CreateMetricLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(10, 4, 10, 4),
            BackColor = Color.FromArgb(12, 22, 36),
            ForeColor = Color.FromArgb(173, 216, 230),
            Font = new Font("Consolas", 9.7f, FontStyle.Bold, GraphicsUnit.Point),
            Text = text
        };
    }

    private static Label CreateInfoChip(string text, Color accent)
    {
        return new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 0, 12, 0),
            Padding = new Padding(10, 5, 10, 5),
            BackColor = Color.FromArgb(13, 24, 36),
            ForeColor = accent,
            Font = new Font("Consolas", 10f, FontStyle.Bold, GraphicsUnit.Point),
            Text = text
        };
    }

    private static Button CreateActionButton(string text)
    {
        var button = new Button
        {
            AutoSize = true,
            Height = 32,
            Margin = new Padding(0, 4, 8, 0),
            Padding = new Padding(14, 0, 14, 0),
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(16, 34, 52),
            ForeColor = Color.FromArgb(226, 236, 248),
            Font = new Font("Bahnschrift SemiBold", 10f, FontStyle.Bold, GraphicsUnit.Point),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(34, 88, 118);
        button.FlatAppearance.BorderSize = 1;
        return button;
    }

    private static Button CreateAccentButton(string text, Color accent)
    {
        var button = new Button
        {
            AutoSize = true,
            Height = 34,
            Margin = new Padding(0, 4, 10, 0),
            Padding = new Padding(16, 0, 16, 0),
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = accent,
            ForeColor = Color.FromArgb(8, 15, 24),
            Font = new Font("Bahnschrift SemiBold", 10.75f, FontStyle.Bold, GraphicsUnit.Point),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private static Rectangle CenteredBounds(Rectangle workingArea, int width, int height)
    {
        var finalWidth = Math.Min(width, workingArea.Width - 40);
        var finalHeight = Math.Min(height, workingArea.Height - 40);
        return new Rectangle(
            workingArea.Left + ((workingArea.Width - finalWidth) / 2),
            workingArea.Top + ((workingArea.Height - finalHeight) / 2),
            finalWidth,
            finalHeight);
    }
}
