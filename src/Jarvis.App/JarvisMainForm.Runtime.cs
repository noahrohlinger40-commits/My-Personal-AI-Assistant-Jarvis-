using System.Drawing;
using System.Media;
using System.Text;
using System.Windows.Forms;
using Jarvis.App;
using Jarvis.Core;
using NAudio.Wave;


namespace Jarvis.App;

internal sealed partial class JarvisMainForm
{
    private async void OnRefreshTimerTick(object? sender, EventArgs eventArgs)
    {
        if (_isRefreshingUiState || _cancellationTokenSource.IsCancellationRequested)
        {
            return;
        }

        _isRefreshingUiState = true;

        try
        {
            await RefreshRuntimeStateAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            _isRefreshingUiState = false;
        }
    }

    private async Task RefreshRuntimeStateAsync()
    {
        await RefreshPendingApprovalUiAsync();
        await RefreshClarificationUiAsync();
        await RefreshSessionSnapshotAsync();
        await RefreshBackgroundSummaryAsync();
        await RefreshMissionBriefAsync();
    }

    private async Task RefreshPendingApprovalUiAsync()
    {
        var pendingApproval = await _context.Assistant.GetPendingApprovalAsync(_cancellationTokenSource.Token);
        UpdateApprovalUi(pendingApproval);
    }

    private async Task RefreshClarificationUiAsync()
    {
        var session = await _context.Assistant.GetPendingClarificationSessionAsync(_cancellationTokenSource.Token);

        if (session is null)
        {
            if (!_isSubmitting && _pendingVoiceClarification is null)
            {
                _isAwaitingFollowUpInput = false;
                _awaitingFollowUpDetail = string.Empty;
            }

            _lastClarificationSnapshotKey = string.Empty;
            return;
        }

        _activeSessionId = session.SessionId;
        _isAwaitingFollowUpInput = true;
        _awaitingFollowUpDetail = session.FollowUpQuestion;

        if (!_isSubmitting
            && !_isAssistantResponseStreaming
            && _pendingVoiceClarification is null)
        {
            var promptEcho = $"FOLLOW-UP REQUIRED  |  {TrimForSingleLine(session.FollowUpQuestion, 124)}";
            var livePacket = BuildClarificationConsoleText(session);

            if (!string.Equals(_promptEchoLabel.Text, promptEcho, StringComparison.Ordinal))
            {
                _promptEchoLabel.Text = promptEcho;
            }

            if (!string.Equals(_liveResponseBox.Text, livePacket, StringComparison.Ordinal))
            {
                _liveResponseBox.Text = livePacket;
            }
        }

        var snapshotKey = BuildClarificationSnapshotKey(session);

        if (string.Equals(snapshotKey, _lastClarificationSnapshotKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastClarificationSnapshotKey = snapshotKey;

        AppendRuntimeMessage("Follow-up required before Jarvis can continue the current workflow.");
        AppendTimelineEntry(
            "CLARIFICATION",
            TrimForSingleLine(session.FollowUpQuestion, 180),
            BuildClarificationWhy(session),
            Color.FromArgb(255, 210, 132));
        AddActivityCard(
            "Clarification",
            "Awaiting operator input",
            TrimForSingleLine(session.FollowUpQuestion, 108),
            Color.FromArgb(255, 210, 132));
    }

    private async Task RefreshSessionSnapshotAsync()
    {
        if (string.IsNullOrWhiteSpace(_activeSessionId))
        {
            _sessionBox.Text = _lastSessionSummary;
            return;
        }

        var session = await _context.Assistant.GetSessionAsync(_activeSessionId, _cancellationTokenSource.Token);

        if (session is null)
        {
            _activeSessionId = string.Empty;
            _sessionBox.Text = _lastSessionSummary;
            return;
        }

        _sessionBox.Text = BuildSessionSummary(session);
    }

    private async Task RefreshBackgroundSummaryAsync()
    {
        var backgroundSessions = await _context.Assistant.GetBackgroundSessionsAsync(_cancellationTokenSource.Token);

        if (backgroundSessions.Count == 0)
        {
            _backgroundLabel.Text = "Background queue clear.";
            return;
        }

        _backgroundLabel.Text = string.Join(
            "  |  ",
            backgroundSessions
                .Take(3)
                .Select(session => $"{TrimForSingleLine(session.OriginalRequest, 34)} [{session.Status}]"));
    }

    private async Task RefreshMissionBriefAsync()
    {
        string nextText;

        try
        {
            var snapshot = await _context.DesktopAutomation.GetContextSnapshotAsync(_cancellationTokenSource.Token);
            nextText = BuildMissionBrief(snapshot);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            nextText = BuildMissionBrief(null);
        }

        _missionBriefText = nextText;

        if (!string.Equals(_missionBriefLabel.Text, nextText, StringComparison.Ordinal))
        {
            _missionBriefLabel.Text = nextText;
        }
    }

    private async Task SubmitPromptAsync(string? promptOverride = null)
    {
        var input = promptOverride ?? _inputBox.Text.Trim();
        await ProcessPromptAsync(input, "YOU", promptOverride is null);
    }

    private async Task ProcessPromptAsync(string input, string source, bool clearInput, string? displayedInput = null)
    {
        if (_cancellationTokenSource.IsCancellationRequested || _isSubmitting)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            return;
        }

        if (await TryResolvePendingVoiceClarificationAsync(input, source, clearInput))
        {
            return;
        }

        if (await TryHandleVoiceCorrectionDirectiveAsync(input, source, clearInput))
        {
            return;
        }

        _isSubmitting = true;
        _isAwaitingFollowUpInput = false;
        _awaitingFollowUpDetail = string.Empty;
        _followUpAnswerDeadlineUtc = DateTimeOffset.MinValue;
        _lastAssistantKind = AssistantActivityKind.Planning;
        _lastAssistantDetail = "Planning the next response.";
        _currentMissionObjective = DescribeMissionObjective(input);
        ClearAssistantStateOverride();
        CompleteAssistantMessageStream();
        SetBusyState(true);
        SetAssistantStateOverride(SurfaceState.Thinking, "Analyzing the directive and preparing the next response.");

        try
        {
            if (clearInput)
            {
                _inputBox.Clear();
            }

            var renderedInput = displayedInput ?? input;
            await RefreshMissionBriefAsync();
            _promptEchoLabel.Text = $"LATEST DIRECTIVE  |  {TrimForSingleLine(renderedInput, 132)}";
            _assistantStatusLabel.Text = $"Mission acknowledged. {BuildImmediateAcknowledgement(input)}";
            _liveResponseBox.Text = BuildProcessingConsoleText(source, renderedInput, input);
            AppendOperatorMessage(source, renderedInput);
            AppendTimelineEntry("REQUEST", renderedInput, $"{source} routed into the active assistant runtime.", Color.FromArgb(255, 191, 105));

            if (ShouldSpeakVoiceAcknowledgement(source))
            {
                await TrySpeakAsync(BuildVoiceAcknowledgement(input), _cancellationTokenSource.Token);
            }

            var turn = await _context.Assistant.HandleAsync(input, _cancellationTokenSource.Token);
            _activeSessionId = turn.SessionId;
            _currentMissionObjective = BuildPostTurnObjective(turn, _pendingApproval);
            await RefreshRuntimeStateAsync();

            var renderedResponse = BuildDisplayResponse(turn, _pendingApproval);
            await StreamLiveResponseAsync(renderedResponse, _cancellationTokenSource.Token);
            ApplyTurnSummary(turn, _pendingApproval, renderedResponse);
            _isAwaitingFollowUpInput = turn.IsFollowUpQuestion;
            _awaitingFollowUpDetail = turn.IsFollowUpQuestion
                ? renderedResponse
                : string.Empty;

            if (turn.IsFollowUpQuestion)
            {
                await RefreshClarificationUiAsync();
            }

            if (_pendingApproval is not null)
            {
                await TrySpeakAsync("I need your approval before I continue. It's on screen.", _cancellationTokenSource.Token);
            }
            else if (SpokenReply.Build(renderedResponse, turn.SpeakInFull) is { Length: > 0 } spoken)
            {
                await TrySpeakAsync(spoken, _cancellationTokenSource.Token);
            }

            if (turn.ShouldExit)
            {
                ExitApplication();
            }
        }
        catch (OperationCanceledException)
        {
            CompleteAssistantMessageStream();
        }
        catch (Exception exception)
        {
            CompleteAssistantMessageStream();
            ClearAssistantStateOverride();
            var message = $"Runtime fault: {exception.Message}";
            _liveResponseBox.Text = message;
            AppendSystemMessage(message);
            AddActivityCard("Runtime Fault", "Execution error", exception.Message, Color.FromArgb(255, 138, 101));
            AppendTimelineEntry("ERROR", message, "Unhandled exception during prompt processing.", Color.FromArgb(255, 138, 101));
            _lastAssistantKind = AssistantActivityKind.Error;
            _lastAssistantDetail = exception.Message;
            _currentMissionObjective = "Recovering from a runtime fault.";
            UpdateSurfaceState(SurfaceState.Error, TrimForSingleLine(exception.Message, 120));
        }
        finally
        {
            _isSubmitting = false;
            _isAssistantResponseStreaming = false;
            ClearAssistantStateOverride();
            SetBusyState(false);
            if (_pendingApproval is null && !_isAwaitingFollowUpInput)
            {
                _currentMissionObjective = "Stand by for the next directive.";
            }
            if (!_cancellationTokenSource.IsCancellationRequested)
            {
                await RefreshMissionBriefAsync();
            }
            RecomputeSurfaceState();
            _inputBox.Focus();

            // After speaking a question or approval request, accept the answer without another wake word.
            if (source == "VOICE"
                && (_isAwaitingFollowUpInput || _pendingApproval is not null)
                && !_cancellationTokenSource.IsCancellationRequested)
            {
                _currentVoiceRuntime.ListenForFollowUp();
                _followUpAnswerDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(30);
            }

            var queuedVoiceDirective = _queuedVoiceDirective;
            _queuedVoiceDirective = null;

            if (queuedVoiceDirective is not null
                && !_cancellationTokenSource.IsCancellationRequested
                && CanUseUi())
            {
                BeginInvoke(new Action(() =>
                {
                    if (CanUseUi())
                    {
                        _ = RouteQueuedVoiceDirectiveAsync(queuedVoiceDirective);
                    }
                }));
            }
        }
    }

    private async Task HandleApprovalDecisionAsync(bool approved)
    {
        if (_pendingApproval is null || _isSubmitting)
        {
            return;
        }

        await ProcessPromptAsync(
            approved ? "approve" : "deny",
            approved ? "APPROVAL" : "DENIAL",
            clearInput: false,
            displayedInput: approved
                ? "Approved the pending major action from the live control surface."
                : "Denied the pending major action from the live control surface.");
    }

    private async Task OpenMemoryWorkbenchAsync()
    {
        using var form = new MemoryWorkbenchForm(_context.Assistant);
        form.ShowDialog(this);
        await RefreshRuntimeStateAsync();
    }

    private async Task OpenPresenceWorkbenchAsync()
    {
        using var form = new PresenceWorkbenchForm(_currentOptions);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        await ApplyPresenceSettingsAsync(form.SelectedOptions);
    }

    private async Task StreamLiveResponseAsync(string message, CancellationToken cancellationToken)
    {
        _liveResponseCts?.Cancel();
        _liveResponseCts?.Dispose();
        _liveResponseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _liveResponseCts.Token;

        _isAssistantResponseStreaming = true;
        _lastAssistantKind = AssistantActivityKind.Summary;
        _lastAssistantDetail = "Streaming the latest response.";
        _assistantStatusLabel.Text = "Streaming the latest response.";
        SetAssistantStateOverride(SurfaceState.Speaking, "Streaming the latest response to the console.");
        BeginAssistantMessageStream();
        _liveResponseBox.Clear();

        try
        {
            // Speech waits for this display to finish, and word-by-word animation of a whole article would
            // take about a minute, so long replies appear at once.
            var chunks = message.Length <= 600 ? EnumerateResponseChunks(message) : [message];

            foreach (var chunk in chunks)
            {
                token.ThrowIfCancellationRequested();
                _liveResponseBox.AppendText(chunk);
                _liveResponseBox.SelectionStart = _liveResponseBox.TextLength;
                _liveResponseBox.ScrollToCaret();
                AppendAssistantMessageChunk(chunk);

                var delay = GetResponseChunkDelay(chunk);

                if (delay > 0)
                {
                    await Task.Delay(delay, token);
                }
            }
        }
        finally
        {
            _isAssistantResponseStreaming = false;
            CompleteAssistantMessageStream();

            if (_assistantStateOverride == SurfaceState.Speaking)
            {
                ClearAssistantStateOverride();
            }
        }
    }

    private void ApplyTurnSummary(AssistantTurn turn, PendingApprovalAction? pendingApproval, string renderedResponse)
    {
        _lastSessionSummary = BuildTurnSummary(turn, pendingApproval, renderedResponse);
        _sessionBox.Text = _lastSessionSummary;
        AppendTimelineEntry("RESPONSE", TrimForSingleLine(renderedResponse, 180), $"Intent `{turn.Intent}` completed.", Color.FromArgb(106, 220, 197));

        if (turn.ToolResults is not null)
        {
            foreach (var tool in turn.ToolResults)
            {
                AppendTimelineEntry(
                    $"TOOL {tool.ToolName.ToUpperInvariant()}",
                    tool.Summary,
                    tool.VerificationText,
                    tool.Succeeded ? Color.FromArgb(132, 196, 255) : Color.FromArgb(255, 138, 101));

                AddActivityCard(
                    tool.ToolName,
                    tool.Succeeded ? "Result confirmed" : "Result flagged",
                    tool.Summary,
                    tool.Succeeded ? Color.FromArgb(132, 196, 255) : Color.FromArgb(255, 138, 101));
            }
        }

        if (turn.Citations is { Length: > 0 })
        {
            AppendTimelineEntry(
                "CITATIONS",
                $"Attached {turn.Citations.Length} citation{(turn.Citations.Length == 1 ? string.Empty : "s")}.",
                string.Join(" | ", turn.Citations.Take(3).Select(citation => TrimForSingleLine(citation.Title, 40))),
                Color.FromArgb(173, 216, 230));
        }
    }

    private void RefreshAudioDevices()
    {
        _deviceComboBox.Items.Clear();

        for (var index = 0; index < WaveInEvent.DeviceCount; index++)
        {
            var capabilities = WaveInEvent.GetCapabilities(index);
            _deviceComboBox.Items.Add(capabilities.ProductName);
        }

        if (_deviceComboBox.Items.Count == 0)
        {
            UpdateCalibrationUi();
            return;
        }

        var preferredIndex = AudioDeviceResolver.GetPreferredInputDeviceIndex(_currentOptions.SpeechRecognitionDeviceName);

        if (preferredIndex < 0 || preferredIndex >= _deviceComboBox.Items.Count)
        {
            preferredIndex = 0;
        }

        _deviceComboBox.SelectedIndexChanged -= DeviceComboBoxOnSelectedIndexChanged;
        _deviceComboBox.SelectedIndex = preferredIndex;
        _deviceComboBox.SelectedIndexChanged += DeviceComboBoxOnSelectedIndexChanged;
        UpdateCalibrationUi();
    }

    private void RefreshBeamformingProfiles()
    {
        _beamformingProfileComboBox.SelectedIndexChanged -= BeamformingProfileComboBoxOnSelectedIndexChanged;
        _beamformingProfileComboBox.Items.Clear();

        foreach (var profile in BeamformingProfileCatalog.All)
        {
            _beamformingProfileComboBox.Items.Add(profile.DisplayName);
        }

        var selectedProfileName = string.IsNullOrWhiteSpace(_currentOptions.SpeechRecognitionBeamformingProfile)
            ? "auto"
            : _currentOptions.SpeechRecognitionBeamformingProfile.Trim();
        var selectedIndex = BeamformingProfileCatalog.All
            .Select((profile, index) => new { profile.Name, index })
            .FirstOrDefault(item => string.Equals(item.Name, selectedProfileName, StringComparison.OrdinalIgnoreCase))
            ?.index ?? 0;

        _beamformingProfileComboBox.SelectedIndex = selectedIndex;
        _beamformingProfileComboBox.SelectedIndexChanged += BeamformingProfileComboBoxOnSelectedIndexChanged;
        UpdateCalibrationUi();
    }

    private void RefreshWakeWordAssets()
    {
        _availableWakeWordAssets = WakeWordAssetCatalog.Discover(_context.WorkspaceRoot, _currentOptions);
        _wakeWordAssetComboBox.SelectedIndexChanged -= WakeWordAssetComboBoxOnSelectedIndexChanged;
        _wakeWordAssetComboBox.Items.Clear();

        foreach (var asset in _availableWakeWordAssets)
        {
            _wakeWordAssetComboBox.Items.Add(asset.BuildShortLabel());
        }

        var configuredPath = WakeWordAssetCatalog.NormalizeStoredPath(_currentOptions.SpeechRecognitionWakeWordAssetPath);
        var selectedIndex = _availableWakeWordAssets
            .Select((asset, index) => new { asset.Path, index })
            .FirstOrDefault(item => string.Equals(
                WakeWordAssetCatalog.NormalizeStoredPath(item.Path),
                configuredPath,
                StringComparison.OrdinalIgnoreCase))
            ?.index ?? 0;

        _wakeWordAssetComboBox.SelectedIndex = selectedIndex;
        _wakeWordAssetComboBox.SelectedIndexChanged += WakeWordAssetComboBoxOnSelectedIndexChanged;
        UpdateCalibrationUi();
    }

    private void RefreshWakeEngines()
    {
        _availableWakeWordEngines = WakeWordEngineCatalog.Discover(_context.WorkspaceRoot, _currentOptions);
        _wakeEngineComboBox.SelectedIndexChanged -= WakeEngineComboBoxOnSelectedIndexChanged;
        _wakeEngineComboBox.Items.Clear();

        foreach (var engine in _availableWakeWordEngines)
        {
            _wakeEngineComboBox.Items.Add(engine.BuildShortLabel());
        }

        var configuredPath = WakeWordEngineCatalog.NormalizeStoredPath(_currentOptions.SpeechRecognitionWakeEnginePath);
        var selectedIndex = _availableWakeWordEngines
            .Select((engine, index) => new { engine.Path, index })
            .FirstOrDefault(item => string.Equals(
                WakeWordEngineCatalog.NormalizeStoredPath(item.Path),
                configuredPath,
                StringComparison.OrdinalIgnoreCase))
            ?.index ?? 0;

        _wakeEngineComboBox.SelectedIndex = selectedIndex;
        _wakeEngineComboBox.SelectedIndexChanged += WakeEngineComboBoxOnSelectedIndexChanged;
        UpdateCalibrationUi();
    }

    private void RefreshHardwareDspProfiles()
    {
        _hardwareDspProfileComboBox.SelectedIndexChanged -= HardwareDspProfileComboBoxOnSelectedIndexChanged;
        _hardwareDspProfileComboBox.Items.Clear();

        foreach (var profile in HardwareDspProfileCatalog.All)
        {
            _hardwareDspProfileComboBox.Items.Add(profile.DisplayName);
        }

        var selectedProfileName = string.IsNullOrWhiteSpace(_currentOptions.SpeechRecognitionHardwareDspProfile)
            ? "auto"
            : _currentOptions.SpeechRecognitionHardwareDspProfile.Trim();
        var selectedIndex = HardwareDspProfileCatalog.All
            .Select((profile, index) => new { profile.Name, index })
            .FirstOrDefault(item => string.Equals(item.Name, selectedProfileName, StringComparison.OrdinalIgnoreCase))
            ?.index ?? 0;

        _hardwareDspProfileComboBox.SelectedIndex = selectedIndex;
        _hardwareDspProfileComboBox.SelectedIndexChanged += HardwareDspProfileComboBoxOnSelectedIndexChanged;
        UpdateCalibrationUi();
    }

    private void RefreshVendorDspProfiles()
    {
        _availableVendorDspProfiles = VendorDspProfileCatalog.Discover(_context.WorkspaceRoot, _currentOptions);
        _vendorDspProfileComboBox.SelectedIndexChanged -= VendorDspProfileComboBoxOnSelectedIndexChanged;
        _vendorDspProfileComboBox.Items.Clear();

        foreach (var profile in _availableVendorDspProfiles)
        {
            _vendorDspProfileComboBox.Items.Add(profile.BuildShortLabel());
        }

        var configuredPath = VendorDspProfileCatalog.NormalizeStoredPath(_currentOptions.SpeechRecognitionVendorDspProfilePath);
        var selectedIndex = _availableVendorDspProfiles
            .Select((profile, index) => new { profile.Path, index })
            .FirstOrDefault(item => string.Equals(
                VendorDspProfileCatalog.NormalizeStoredPath(item.Path),
                configuredPath,
                StringComparison.OrdinalIgnoreCase))
            ?.index ?? 0;

        _vendorDspProfileComboBox.SelectedIndex = selectedIndex;
        _vendorDspProfileComboBox.SelectedIndexChanged += VendorDspProfileComboBoxOnSelectedIndexChanged;
        UpdateCalibrationUi();
    }

    private void UpdateCalibrationUi()
    {
        var resolved = MicrophoneProcessingProfileResolver.Resolve(_currentOptions);
        _calibrationStatusLabel.Text = TrimForSingleLine(resolved.BuildUiSummary(), 96);
        _validationStatusLabel.Text = resolved.Validation is null
            ? "No validation saved."
            : TrimForSingleLine(
                $"{resolved.Validation.RecommendedConversationMode} | wake {resolved.Validation.WakeWordReadinessScore:P0} | duplex {resolved.Validation.FullDuplexReadinessScore:P0}",
                96);

        var hasAvailableDevice = !string.IsNullOrWhiteSpace(_currentOptions.SpeechRecognitionDeviceName)
            || WaveInEvent.DeviceCount > 0;
        _calibrateMicButton.Enabled = hasAvailableDevice;
        _validateMicButton.Enabled = hasAvailableDevice;
    }

    private async Task OnDeviceSelectionChangedAsync()
    {
        var deviceName = ResolveSelectedInputDeviceName();

        if (string.IsNullOrWhiteSpace(deviceName))
        {
            return;
        }

        if (string.Equals(deviceName, _currentOptions.SpeechRecognitionDeviceName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentOptions = _currentOptions with { SpeechRecognitionDeviceName = deviceName };
        UpdateCalibrationUi();
        await PersistCurrentOptionsAsync();
        await ReloadVoiceRuntimeAsync(_currentOptions);
    }

    private async void DeviceComboBoxOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        try
        {
            await OnDeviceSelectionChangedAsync();
        }
        catch (Exception exception)
        {
            AppendSystemMessage($"Microphone selection failed: {exception.Message}");
        }
    }

    private string ResolveSelectedInputDeviceName()
    {
        return _deviceComboBox.SelectedItem as string
            ?? _currentOptions.SpeechRecognitionDeviceName;
    }

    private void SyncSelectedInputDeviceIntoCurrentOptions()
    {
        var deviceName = ResolveSelectedInputDeviceName();

        if (string.IsNullOrWhiteSpace(deviceName)
            || string.Equals(deviceName, _currentOptions.SpeechRecognitionDeviceName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentOptions = _currentOptions with
        {
            SpeechRecognitionDeviceName = deviceName
        };
    }

    private async Task OnBeamformingProfileSelectionChangedAsync()
    {
        if (_beamformingProfileComboBox.SelectedIndex < 0
            || _beamformingProfileComboBox.SelectedIndex >= BeamformingProfileCatalog.All.Count)
        {
            return;
        }

        var selectedProfile = BeamformingProfileCatalog.All[_beamformingProfileComboBox.SelectedIndex];

        if (string.Equals(selectedProfile.Name, _currentOptions.SpeechRecognitionBeamformingProfile, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentOptions = _currentOptions with
        {
            SpeechRecognitionBeamformingProfile = selectedProfile.Name,
            SpeechRecognitionBeamformingEnabled = !string.Equals(selectedProfile.Name, "off", StringComparison.OrdinalIgnoreCase)
        };

        UpdateCalibrationUi();
        await PersistCurrentOptionsAsync();
        await ReloadVoiceRuntimeAsync(_currentOptions);
    }

    private async void BeamformingProfileComboBoxOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        try
        {
            await OnBeamformingProfileSelectionChangedAsync();
        }
        catch (Exception exception)
        {
            AppendSystemMessage($"Beamforming profile update failed: {exception.Message}");
        }
    }

    private async Task OnWakeWordAssetSelectionChangedAsync()
    {
        if (_wakeWordAssetComboBox.SelectedIndex < 0
            || _wakeWordAssetComboBox.SelectedIndex >= _availableWakeWordAssets.Count)
        {
            return;
        }

        var selectedAsset = _availableWakeWordAssets[_wakeWordAssetComboBox.SelectedIndex];
        var selectedPath = WakeWordAssetCatalog.NormalizeStoredPath(selectedAsset.Path);
        var currentPath = WakeWordAssetCatalog.NormalizeStoredPath(_currentOptions.SpeechRecognitionWakeWordAssetPath);

        if (string.Equals(selectedPath, currentPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentOptions = _currentOptions with
        {
            SpeechRecognitionWakeWordAssetPath = selectedAsset.Path
        };

        UpdateCalibrationUi();
        await PersistCurrentOptionsAsync();
        await ReloadVoiceRuntimeAsync(_currentOptions);
    }

    private async void WakeWordAssetComboBoxOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        try
        {
            await OnWakeWordAssetSelectionChangedAsync();
        }
        catch (Exception exception)
        {
            AppendSystemMessage($"Wake asset update failed: {exception.Message}");
        }
    }

    private async Task OnWakeEngineSelectionChangedAsync()
    {
        if (_wakeEngineComboBox.SelectedIndex < 0
            || _wakeEngineComboBox.SelectedIndex >= _availableWakeWordEngines.Count)
        {
            return;
        }

        var selectedEngine = _availableWakeWordEngines[_wakeEngineComboBox.SelectedIndex];
        var selectedPath = WakeWordEngineCatalog.NormalizeStoredPath(selectedEngine.Path);
        var currentPath = WakeWordEngineCatalog.NormalizeStoredPath(_currentOptions.SpeechRecognitionWakeEnginePath);

        if (string.Equals(selectedPath, currentPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentOptions = _currentOptions with
        {
            SpeechRecognitionWakeEnginePath = selectedEngine.Path
        };

        UpdateCalibrationUi();
        await PersistCurrentOptionsAsync();
        await ReloadVoiceRuntimeAsync(_currentOptions);
    }

    private async void WakeEngineComboBoxOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        try
        {
            await OnWakeEngineSelectionChangedAsync();
        }
        catch (Exception exception)
        {
            AppendSystemMessage($"Wake engine update failed: {exception.Message}");
        }
    }

    private async Task OnHardwareDspProfileSelectionChangedAsync()
    {
        if (_hardwareDspProfileComboBox.SelectedIndex < 0
            || _hardwareDspProfileComboBox.SelectedIndex >= HardwareDspProfileCatalog.All.Count)
        {
            return;
        }

        var selectedProfile = HardwareDspProfileCatalog.All[_hardwareDspProfileComboBox.SelectedIndex];

        if (string.Equals(selectedProfile.Name, _currentOptions.SpeechRecognitionHardwareDspProfile, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentOptions = _currentOptions with
        {
            SpeechRecognitionHardwareDspProfile = selectedProfile.Name
        };

        UpdateCalibrationUi();
        await PersistCurrentOptionsAsync();
        await ReloadVoiceRuntimeAsync(_currentOptions);
    }

    private async void HardwareDspProfileComboBoxOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        try
        {
            await OnHardwareDspProfileSelectionChangedAsync();
        }
        catch (Exception exception)
        {
            AppendSystemMessage($"Hardware DSP update failed: {exception.Message}");
        }
    }

    private async Task OnVendorDspProfileSelectionChangedAsync()
    {
        if (_vendorDspProfileComboBox.SelectedIndex < 0
            || _vendorDspProfileComboBox.SelectedIndex >= _availableVendorDspProfiles.Count)
        {
            return;
        }

        var selectedProfile = _availableVendorDspProfiles[_vendorDspProfileComboBox.SelectedIndex];
        var selectedPath = VendorDspProfileCatalog.NormalizeStoredPath(selectedProfile.Path);
        var currentPath = VendorDspProfileCatalog.NormalizeStoredPath(_currentOptions.SpeechRecognitionVendorDspProfilePath);

        if (string.Equals(selectedPath, currentPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentOptions = _currentOptions with
        {
            SpeechRecognitionVendorDspProfilePath = selectedProfile.Path
        };

        UpdateCalibrationUi();
        await PersistCurrentOptionsAsync();
        await ReloadVoiceRuntimeAsync(_currentOptions);
    }

    private async void VendorDspProfileComboBoxOnSelectedIndexChanged(object? sender, EventArgs e)
    {
        try
        {
            await OnVendorDspProfileSelectionChangedAsync();
        }
        catch (Exception exception)
        {
            AppendSystemMessage($"Vendor DSP update failed: {exception.Message}");
        }
    }

    private async Task CalibrateMicrophoneAsync()
    {
        if (_isSubmitting)
        {
            AppendSystemMessage("Calibration is blocked while Jarvis is processing another request.");
            return;
        }

        if (WaveInEvent.DeviceCount <= 0)
        {
            AppendSystemMessage("No microphone is available for calibration.");
            return;
        }

        SyncSelectedInputDeviceIntoCurrentOptions();

        var wasListening = _currentVoiceRuntime.CurrentStatus.IsListening;

        try
        {
            _calibrateMicButton.Enabled = false;

            if (wasListening)
            {
                await _currentVoiceRuntime.StopAsync(_cancellationTokenSource.Token);
            }

            MessageBox.Show(
                this,
                "Microphone calibration starts now.\n\n1. Keep the room quiet for the first capture.\n2. After that prompt, speak naturally toward the selected microphone for a few seconds.",
                "Microphone Calibration",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            AppendRuntimeMessage("Calibration: capturing room noise.");
            var ambientSample = await _microphoneCalibrationService.CaptureSampleAsync(_currentOptions, 2200, _cancellationTokenSource.Token);

            MessageBox.Show(
                this,
                "Room-noise capture complete.\n\nNow speak a short command in your normal voice until the next prompt closes.",
                "Microphone Calibration",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            AppendRuntimeMessage("Calibration: capturing speech sample.");
            var speechSample = await _microphoneCalibrationService.CaptureSampleAsync(_currentOptions, 3200, _cancellationTokenSource.Token);
            var resolvedProcessing = ResolveActiveProcessingProfile();
            var calibration = _microphoneCalibrationService.CreateCalibrationProfile(
                _currentOptions,
                ambientSample,
                speechSample,
                resolvedProcessing.EffectiveBeamformingProfileName,
                resolvedProcessing.EffectiveHardwareDspProfileName,
                ResolveEffectiveVendorDspLabel(),
                resolvedProcessing.EffectiveVendorDspProfilePath);
            var profiles = _currentOptions.SpeechRecognitionCalibrationProfiles
                .Where(profile =>
                    !IsSameCalibrationConfiguration(profile, calibration))
                .Concat([calibration])
                .OrderBy(profile => profile.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(profile => NormalizeProfileNameForStorage(profile.PreferredBeamformingProfile), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _currentOptions = _currentOptions with
            {
                SpeechRecognitionCalibrationProfiles = profiles
            };

            UpdateCalibrationUi();
            await PersistCurrentOptionsAsync();
            await ReloadVoiceRuntimeAsync(_currentOptions);
            AppendRuntimeMessage($"Calibration saved for {_currentOptions.SpeechRecognitionDeviceName}: {calibration.Notes}.");
            AppendTimelineEntry(
                "AUDIO",
                "Microphone calibration saved.",
                $"Gate {calibration.RecommendedVoiceActivityThreshold:P1}, gain cap {calibration.RecommendedInputGainCap:F1}x, beam {calibration.PreferredBeamformingProfile}, DSP {calibration.HardwareDspProfileName}, vendor {ResolveEffectiveVendorDspLabel()}.",
                Color.FromArgb(106, 220, 197));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppendSystemMessage($"Calibration failed: {exception.Message}");
            AppendTimelineEntry("AUDIO", "Microphone calibration failed.", exception.Message, Color.FromArgb(255, 138, 101));
        }
        finally
        {
            _calibrateMicButton.Enabled = true;

            if (wasListening
                && !_currentOptions.SpeechRecognitionPushToTalkEnabled
                && !_cancellationTokenSource.IsCancellationRequested
                && !_currentVoiceRuntime.CurrentStatus.IsListening)
            {
                try
                {
                    await _currentVoiceRuntime.StartAsync(_cancellationTokenSource.Token);
                }
                catch
                {
                }
            }
        }
    }

    private async Task ValidateMicrophoneAsync()
    {
        if (_isSubmitting)
        {
            AppendSystemMessage("Validation is blocked while Jarvis is processing another request.");
            return;
        }

        if (WaveInEvent.DeviceCount <= 0)
        {
            AppendSystemMessage("No microphone is available for validation.");
            return;
        }

        SyncSelectedInputDeviceIntoCurrentOptions();

        var wasListening = _currentVoiceRuntime.CurrentStatus.IsListening;

        try
        {
            _validateMicButton.Enabled = false;

            if (wasListening)
            {
                await _currentVoiceRuntime.StopAsync(_cancellationTokenSource.Token);
            }

            MessageBox.Show(
                this,
                "Microphone validation starts now.\n\n1. Keep the room quiet for the first capture.\n2. Say only the wake phrase after the next prompt.\n3. Then say a short command.\n4. Jarvis will play a short tone to estimate full-duplex speaker bleed.",
                "Microphone Validation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            AppendRuntimeMessage("Validation: capturing room noise.");
            var ambientSample = await _microphoneCalibrationService.CaptureSampleAsync(_currentOptions, 1800, _cancellationTokenSource.Token);

            MessageBox.Show(
                this,
                $"Say only \"{_currentOptions.WakePhrase}\" toward the selected microphone until this prompt closes.",
                "Wake Phrase Validation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            AppendRuntimeMessage("Validation: capturing wake phrase sample.");
            var wakeSample = await _microphoneCalibrationService.CaptureSampleAsync(_currentOptions, 2200, _cancellationTokenSource.Token);

            MessageBox.Show(
                this,
                "Now say a short command in your normal voice until this prompt closes.",
                "Command Validation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            AppendRuntimeMessage("Validation: capturing command sample.");
            var speechSample = await _microphoneCalibrationService.CaptureSampleAsync(_currentOptions, 2600, _cancellationTokenSource.Token);

            MessageBox.Show(
                this,
                "Stay quiet for the next capture while Jarvis plays a short validation tone through the speaker.",
                "Full-Duplex Validation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            AppendRuntimeMessage("Validation: measuring playback leakage.");
            var playbackLeakageSample = await _microphoneCalibrationService.CapturePlaybackLeakageAsync(_currentOptions, 1700, _cancellationTokenSource.Token);
            var resolvedProcessing = ResolveActiveProcessingProfile();
            var validation = _microphoneCalibrationService.CreateValidationProfile(
                _currentOptions,
                ambientSample,
                wakeSample,
                speechSample,
                playbackLeakageSample,
                ResolveEffectiveWakeWordAssetLabel(),
                resolvedProcessing.EffectiveWakeWordAssetPath,
                ResolveEffectiveWakeEngineLabel(),
                resolvedProcessing.EffectiveWakeEnginePath,
                resolvedProcessing.EffectiveBeamformingProfileName,
                resolvedProcessing.EffectiveHardwareDspProfileName,
                ResolveEffectiveVendorDspLabel(),
                resolvedProcessing.EffectiveVendorDspProfilePath);
            var profiles = _currentOptions.SpeechRecognitionValidationProfiles
                .Where(profile =>
                    !IsSameValidationConfiguration(profile, validation))
                .Concat([validation])
                .OrderBy(profile => profile.DeviceName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(profile => WakeWordAssetCatalog.NormalizeStoredPath(profile.WakeWordAssetPath), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            _currentOptions = _currentOptions with
            {
                SpeechRecognitionValidationProfiles = profiles
            };

            UpdateCalibrationUi();
            await PersistCurrentOptionsAsync();
            await ReloadVoiceRuntimeAsync(_currentOptions);
            AppendRuntimeMessage(
                $"Validation saved for {_currentOptions.SpeechRecognitionDeviceName}: {validation.Notes}. Recommended {validation.RecommendedConversationMode}.");
            AppendTimelineEntry(
                "AUDIO",
                "Voice stack validation saved.",
                $"Wake {validation.WakeWordReadinessScore:P0}, duplex {validation.FullDuplexReadinessScore:P0}, engine {validation.WakeEngineName}, beam {validation.BeamformingProfileName}, DSP {validation.HardwareDspProfileName}, vendor {ResolveEffectiveVendorDspLabel()}, recommended mode {validation.RecommendedConversationMode}.",
                validation.FullDuplexReadinessScore >= 0.58
                    ? Color.FromArgb(106, 220, 197)
                    : Color.FromArgb(255, 191, 105));
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AppendSystemMessage($"Validation failed: {exception.Message}");
            AppendTimelineEntry("AUDIO", "Voice stack validation failed.", exception.Message, Color.FromArgb(255, 138, 101));
        }
        finally
        {
            _validateMicButton.Enabled = true;

            if (wasListening
                && !_currentOptions.SpeechRecognitionPushToTalkEnabled
                && !_cancellationTokenSource.IsCancellationRequested
                && !_currentVoiceRuntime.CurrentStatus.IsListening)
            {
                try
                {
                    await _currentVoiceRuntime.StartAsync(_cancellationTokenSource.Token);
                }
                catch
                {
                }
            }
        }
    }

    private string ResolveSelectedBeamformingProfileName()
    {
        if (_beamformingProfileComboBox.SelectedIndex < 0
            || _beamformingProfileComboBox.SelectedIndex >= BeamformingProfileCatalog.All.Count)
        {
            return string.IsNullOrWhiteSpace(_currentOptions.SpeechRecognitionBeamformingProfile)
                ? "auto"
                : _currentOptions.SpeechRecognitionBeamformingProfile;
        }

        return BeamformingProfileCatalog.All[_beamformingProfileComboBox.SelectedIndex].Name;
    }

    private string ResolveSelectedHardwareDspProfileName()
    {
        if (_hardwareDspProfileComboBox.SelectedIndex < 0
            || _hardwareDspProfileComboBox.SelectedIndex >= HardwareDspProfileCatalog.All.Count)
        {
            return string.IsNullOrWhiteSpace(_currentOptions.SpeechRecognitionHardwareDspProfile)
                ? "auto"
                : _currentOptions.SpeechRecognitionHardwareDspProfile;
        }

        return HardwareDspProfileCatalog.All[_hardwareDspProfileComboBox.SelectedIndex].Name;
    }

    private string ResolveSelectedWakeWordAssetLabel()
    {
        if (_wakeWordAssetComboBox.SelectedIndex < 0
            || _wakeWordAssetComboBox.SelectedIndex >= _availableWakeWordAssets.Count)
        {
            return WakeWordAssetCatalog.ResolveSelectionDescriptor(_currentOptions).BuildShortLabel();
        }

        return _availableWakeWordAssets[_wakeWordAssetComboBox.SelectedIndex].BuildShortLabel();
    }

    private string ResolveEffectiveWakeWordAssetLabel()
    {
        return WakeWordAssetCatalog.ResolveConfiguredAsset(_currentOptions)?.BuildShortLabel()
            ?? "Recognizer Default";
    }

    private string ResolveEffectiveWakeEngineLabel()
    {
        var resolved = ResolveActiveProcessingProfile();
        var descriptor = WakeWordEngineCatalog.ResolveDescriptorByPath(
            _context.WorkspaceRoot,
            _currentOptions,
            resolved.EffectiveWakeEnginePath);
        return descriptor.BuildShortLabel();
    }

    private string ResolveEffectiveVendorDspLabel()
    {
        var resolved = ResolveActiveProcessingProfile();
        var descriptor = VendorDspProfileCatalog.ResolveDescriptorByPath(
            _context.WorkspaceRoot,
            _currentOptions,
            resolved.EffectiveVendorDspProfilePath);
        return descriptor.IsNone ? string.Empty : descriptor.BuildShortLabel();
    }

    private ResolvedMicrophoneProcessingProfile ResolveActiveProcessingProfile()
    {
        return MicrophoneProcessingProfileResolver.Resolve(_currentOptions);
    }

    private static bool IsSameCalibrationConfiguration(
        SpeechRecognitionCalibrationProfile existing,
        SpeechRecognitionCalibrationProfile candidate)
    {
        return string.Equals(existing.DeviceName, candidate.DeviceName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                NormalizeProfileNameForStorage(existing.PreferredBeamformingProfile),
                NormalizeProfileNameForStorage(candidate.PreferredBeamformingProfile),
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                NormalizeProfileNameForStorage(existing.HardwareDspProfileName),
                NormalizeProfileNameForStorage(candidate.HardwareDspProfileName),
                StringComparison.OrdinalIgnoreCase)
            && VendorDspProfilePathsMatch(existing.VendorDspProfilePath, candidate.VendorDspProfilePath)
            && string.Equals(
                NormalizeVendorLabelForStorage(existing.VendorDspProfileName),
                NormalizeVendorLabelForStorage(candidate.VendorDspProfileName),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSameValidationConfiguration(
        SpeechRecognitionValidationProfile existing,
        SpeechRecognitionValidationProfile candidate)
    {
        var existingWakeWordAssetPath = WakeWordAssetCatalog.NormalizeStoredPath(existing.WakeWordAssetPath);
        var candidateWakeWordAssetPath = WakeWordAssetCatalog.NormalizeStoredPath(candidate.WakeWordAssetPath);
        var wakeAssetMatches = string.Equals(existingWakeWordAssetPath, candidateWakeWordAssetPath, StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(existingWakeWordAssetPath)
                && string.IsNullOrWhiteSpace(candidateWakeWordAssetPath)
                && string.Equals(existing.WakeWordAssetName, candidate.WakeWordAssetName, StringComparison.OrdinalIgnoreCase));
        var existingWakeEnginePath = WakeWordEngineCatalog.NormalizeStoredPath(existing.WakeEnginePath);
        var candidateWakeEnginePath = WakeWordEngineCatalog.NormalizeStoredPath(candidate.WakeEnginePath);
        var wakeEngineMatches = WakeWordEngineCatalog.StoredPathsMatch(existingWakeEnginePath, candidateWakeEnginePath)
            || (string.IsNullOrWhiteSpace(existingWakeEnginePath)
                && string.IsNullOrWhiteSpace(candidateWakeEnginePath)
                && string.Equals(existing.WakeEngineName, candidate.WakeEngineName, StringComparison.OrdinalIgnoreCase));

        return string.Equals(existing.DeviceName, candidate.DeviceName, StringComparison.OrdinalIgnoreCase)
            && wakeAssetMatches
            && wakeEngineMatches
            && string.Equals(
                NormalizeProfileNameForStorage(existing.BeamformingProfileName),
                NormalizeProfileNameForStorage(candidate.BeamformingProfileName),
                StringComparison.OrdinalIgnoreCase)
            && string.Equals(
                NormalizeProfileNameForStorage(existing.HardwareDspProfileName),
                NormalizeProfileNameForStorage(candidate.HardwareDspProfileName),
                StringComparison.OrdinalIgnoreCase)
            && VendorDspProfilePathsMatch(existing.VendorDspProfilePath, candidate.VendorDspProfilePath)
            && string.Equals(
                NormalizeVendorLabelForStorage(existing.VendorDspProfileName),
                NormalizeVendorLabelForStorage(candidate.VendorDspProfileName),
                StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeProfileNameForStorage(string? profileName)
    {
        return string.IsNullOrWhiteSpace(profileName)
            ? "auto"
            : profileName.Trim().ToLowerInvariant();
    }

    private static string NormalizeVendorLabelForStorage(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant();
    }

    private static bool VendorDspProfilePathsMatch(string? left, string? right)
    {
        return VendorDspProfileCatalog.StoredPathsMatch(left, right)
            || (string.IsNullOrWhiteSpace(VendorDspProfileCatalog.NormalizeStoredPath(left))
                && string.IsNullOrWhiteSpace(VendorDspProfileCatalog.NormalizeStoredPath(right)));
    }

    private async Task PersistCurrentOptionsAsync()
    {
        await _optionsPersistenceGate.WaitAsync(_cancellationTokenSource.Token);

        try
        {
            _context.Options = _currentOptions;
            await JarvisSettingsLoader.SaveAsync(_context.WorkspaceRoot, _currentOptions, _cancellationTokenSource.Token);
        }
        finally
        {
            _optionsPersistenceGate.Release();
        }
    }

    private async Task ReplaceAssistantAsync(JarvisOptions options)
    {
        var assistant = await AppBootstrapper.CreateAssistantAsync(
            _context.WorkspaceRoot,
            options,
            _context.DesktopAutomation,
            _cancellationTokenSource.Token);

        _context.Assistant.Activity -= OnAssistantActivity;
        _context.Assistant = assistant;
        _context.Options = options;
        _context.Assistant.Activity += OnAssistantActivity;
    }

    private async Task ReloadVoiceRuntimeAsync(JarvisOptions options)
    {
        _currentOptions = options;
        await ReplaceAssistantAsync(options);

        try
        {
            await _currentVoiceRuntime.StopAsync(_cancellationTokenSource.Token);
        }
        catch
        {
        }

        _currentVoiceRuntime.StatusChanged -= OnVoiceStatusChanged;
        _currentVoiceRuntime.SignalLevelChanged -= OnVoiceSignalChanged;
        _currentVoiceRuntime.CommandRecognized -= OnVoiceCommandRecognized;
        _currentVoiceRuntime.ActivationRequested -= OnVoiceActivationRequested;
        _currentVoiceRuntime.BargeInRequested -= OnVoiceBargeInRequested;
        _currentVoiceRuntime.Dispose();

        _currentVoiceRuntime = VoiceRuntimeFactory.Create(options);
        _context.VoiceRuntime = _currentVoiceRuntime;
        _context.Options = options;
        _currentVoiceRuntime.StatusChanged += OnVoiceStatusChanged;
        _currentVoiceRuntime.SignalLevelChanged += OnVoiceSignalChanged;
        _currentVoiceRuntime.CommandRecognized += OnVoiceCommandRecognized;
        _currentVoiceRuntime.ActivationRequested += OnVoiceActivationRequested;
        _currentVoiceRuntime.BargeInRequested += OnVoiceBargeInRequested;
        _currentVoiceRuntime.SetPlaybackState(_context.Speaker.CurrentStatus.IsSpeaking);
        RefreshWakeWordAssets();
        RefreshWakeEngines();
        RefreshBeamformingProfiles();
        RefreshHardwareDspProfiles();
        RefreshVendorDspProfiles();
        UpdateCalibrationUi();
        UpdateVoiceUi(_currentVoiceRuntime.CurrentStatus, appendToFeed: false);
        UpdateSignalUi(_currentVoiceRuntime.CurrentSignal);

        try
        {
            await _currentVoiceRuntime.StartAsync(_cancellationTokenSource.Token);
        }
        catch
        {
        }
    }

    private async Task ApplyPresenceSettingsAsync(JarvisOptions options)
    {
        var normalizedOptions = options with
        {
            AssistantPresencePreset = AssistantPersonaConfiguration.ResolvePresencePreset(options),
            AssistantResponseStyle = AssistantPersonaConfiguration.ResolveResponseStyle(options),
            AssistantUserNickname = options.AssistantUserNickname?.Trim() ?? string.Empty,
            AssistantPronunciationHints = AssistantPersonaConfiguration.ResolvePronunciationHints(options).ToArray(),
            AssistantPersona = options.AssistantPersona?.Trim() ?? string.Empty
        };

        if (string.Equals(_currentOptions.AssistantPresencePreset, normalizedOptions.AssistantPresencePreset, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_currentOptions.AssistantResponseStyle, normalizedOptions.AssistantResponseStyle, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_currentOptions.AssistantUserNickname, normalizedOptions.AssistantUserNickname, StringComparison.Ordinal)
            && _currentOptions.AssistantSmallTalkEnabled == normalizedOptions.AssistantSmallTalkEnabled
            && _currentOptions.AssistantPronunciationHints.SequenceEqual(normalizedOptions.AssistantPronunciationHints, StringComparer.OrdinalIgnoreCase)
            && string.Equals(_currentOptions.AssistantPersona, normalizedOptions.AssistantPersona, StringComparison.Ordinal))
        {
            return;
        }

        _currentOptions = normalizedOptions;
        await PersistCurrentOptionsAsync();
        await ReplaceAssistantAsync(_currentOptions);
        ReplaceSpeaker(_currentOptions);
        await RefreshRuntimeStateAsync();

        var summary = BuildPresenceUpdateSummary(_currentOptions);
        AppendRuntimeMessage(summary);
        AppendTimelineEntry("PRESENCE", "Presence profile updated.", summary, Color.FromArgb(106, 220, 197));
        AddActivityCard("Presence", "Profile updated", summary, Color.FromArgb(106, 220, 197));
    }

    private async Task ToggleMicrophoneAsync()
    {
        if (_currentVoiceRuntime.CurrentStatus.IsListening)
        {
            await _currentVoiceRuntime.StopAsync(_cancellationTokenSource.Token);
            return;
        }

        await _currentVoiceRuntime.StartAsync(_cancellationTokenSource.Token);
    }

    private async Task TogglePushToTalkAsync()
    {
        if (!_currentOptions.SpeechRecognitionPushToTalkEnabled)
        {
            await ToggleMicrophoneAsync();
            return;
        }

        if (_currentVoiceRuntime.CurrentStatus.IsListening)
        {
            await _currentVoiceRuntime.StopAsync(_cancellationTokenSource.Token);
            AppendSystemMessage("Push-to-talk microphone paused.");
            AppendTimelineEntry("VOICE", "Push-to-talk paused.", "Operator halted live capture.", Color.FromArgb(255, 138, 101));
            return;
        }

        await _currentVoiceRuntime.StartAsync(_cancellationTokenSource.Token);
        AppendRuntimeMessage("Push-to-talk microphone active. Press the hotkey again to stop.");
        AppendTimelineEntry("VOICE", "Push-to-talk active.", "Operator armed the microphone.", Color.FromArgb(106, 220, 197));
    }

    private async Task UpdateSpeakerVolumeAsync(double volumeMultiplier)
    {
        var normalized = Math.Clamp(volumeMultiplier, 0.10, 2.0);

        if (Math.Abs(_currentOptions.SpeakerVolumeMultiplier - normalized) < 0.001)
        {
            return;
        }

        _currentOptions = _currentOptions with
        {
            SpeakerVolumeMultiplier = normalized
        };

        ReplaceSpeaker(_currentOptions);
        await PersistCurrentOptionsAsync();
    }

    private async Task UpdateWhisperModeAsync(bool whisperEnabled)
    {
        if (_currentOptions.WhisperModeEnabled == whisperEnabled)
        {
            return;
        }

        _currentOptions = _currentOptions with
        {
            WhisperModeEnabled = whisperEnabled
        };

        ReplaceSpeaker(_currentOptions);
        await PersistCurrentOptionsAsync();
    }

    private void OnVoiceStatusChanged(object? sender, VoiceStatusChangedEventArgs eventArgs)
    {
        if (!CanUseUi())
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() =>
            {
                if (CanUseUi())
                {
                    UpdateVoiceUi(eventArgs.Status, appendToFeed: true);
                }
            }));
            return;
        }

        UpdateVoiceUi(eventArgs.Status, appendToFeed: true);
    }

    private void OnVoiceSignalChanged(object? sender, VoiceSignalChangedEventArgs eventArgs)
    {
        if (!CanUseUi())
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() =>
            {
                if (CanUseUi())
                {
                    UpdateSignalUi(eventArgs.Signal);
                }
            }));
            return;
        }

        UpdateSignalUi(eventArgs.Signal);
    }

    private void OnSpeakerStatusChanged(object? sender, SpeakerStatusChangedEventArgs eventArgs)
    {
        _currentVoiceRuntime.SetPlaybackState(eventArgs.Status.IsSpeaking);

        if (!CanUseUi())
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() =>
            {
                if (CanUseUi())
                {
                    UpdateSpeakerUi(eventArgs.Status);
                }
            }));
            return;
        }

        UpdateSpeakerUi(eventArgs.Status);
    }

    private void OnAssistantActivity(object? sender, AssistantActivityEventArgs eventArgs)
    {
        if (!CanUseUi())
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() =>
            {
                if (CanUseUi())
                {
                    UpdateAssistantRuntimeUi(eventArgs.Activity);
                }
            }));
            return;
        }

        UpdateAssistantRuntimeUi(eventArgs.Activity);
    }

    private void OnVoiceCommandRecognized(object? sender, VoiceCommandRecognizedEventArgs eventArgs)
    {
        if (!CanUseUi())
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() =>
            {
                if (CanUseUi())
                {
                    _ = HandleVoiceCommandAsync(eventArgs);
                }
            }));
            return;
        }

        _ = HandleVoiceCommandAsync(eventArgs);
    }

    private async Task HandleVoiceCommandAsync(VoiceCommandRecognizedEventArgs eventArgs)
    {
        if (_isSubmitting)
        {
            if (DateTimeOffset.UtcNow - _lastBargeInUtc < TimeSpan.FromSeconds(4))
            {
                _queuedVoiceDirective = QueuedVoiceDirective.From(eventArgs);
                AppendRuntimeMessage(
                    eventArgs.RequiresClarification
                        ? "Low-confidence voice directive queued behind the interrupted response."
                        : "Voice command queued behind the interrupted response.");
                AppendTimelineEntry(
                    "VOICE",
                    eventArgs.RequiresClarification ? "Barge-in confirmation queued." : "Barge-in command queued.",
                    eventArgs.RequiresClarification
                        ? "The assistant is finishing the current turn cleanup before asking for a low-confidence voice confirmation."
                        : "The assistant is finishing the current turn cleanup before routing the new voice command.",
                    Color.FromArgb(255, 191, 105));
                return;
            }

            AppendSystemMessage("Voice command ignored because the assistant is already processing another request.");
            AppendTimelineEntry("VOICE", "Command ignored while busy.", "The assistant was already working on another task.", Color.FromArgb(255, 138, 101));

            if (ShouldSpeakVoiceAcknowledgement("VOICE"))
            {
                await TrySpeakAsync("Still working on the last request.", _cancellationTokenSource.Token);
            }

            return;
        }

        if (eventArgs.RequiresClarification)
        {
            await PromptForVoiceClarificationAsync(eventArgs);
            return;
        }

        // An answer to Jarvis's question is dictation, not a command: "Spotify" must not become "open app Spotify".
        var isAnswer = DateTimeOffset.UtcNow < _followUpAnswerDeadlineUtc;
        await ProcessPromptAsync(isAnswer ? eventArgs.RawText : eventArgs.Text, "VOICE", clearInput: false);
    }

    private void OnVoiceActivationRequested(object? sender, VoiceActivationRequestedEventArgs eventArgs)
    {
        if (!CanUseUi())
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() =>
            {
                if (CanUseUi())
                {
                    BringAssistantToFront(eventArgs.Reason);
                }
            }));
            return;
        }

        BringAssistantToFront(eventArgs.Reason);
    }

    private void OnVoiceBargeInRequested(object? sender, VoiceBargeInEventArgs eventArgs)
    {
        _context.Speaker.Interrupt();
        _lastBargeInUtc = DateTimeOffset.UtcNow;

        if (!CanUseUi())
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(new Action(() =>
            {
                if (CanUseUi())
                {
                    AddBargeInTimelineEntry(eventArgs.Reason);
                }
            }));
            return;
        }

        AddBargeInTimelineEntry(eventArgs.Reason);
    }

    private void AddBargeInTimelineEntry(string reason)
    {
        AppendRuntimeMessage("Barge-in detected. Voice output interrupted.");
        AppendTimelineEntry(
            "VOICE",
            "Barge-in detected.",
            $"The microphone detected user speech during playback ({reason}).",
            Color.FromArgb(255, 191, 105));
    }

    private void UpdateVoiceUi(VoiceStatusSnapshot status, bool appendToFeed)
    {
        _trayIcon.Text = BuildTrayText(status.StatusText);
        _voiceStatusLabel.Text = status.StatusText;
        _listenIndicatorLabel.Text = status.IsListening ? "LISTEN HOT" : status.IsAvailable ? "LISTEN READY" : "MIC OFFLINE";
        _wakeIndicatorLabel.Text = status.IsArmed
            ? "WAKE DETECTED"
            : _currentOptions.WakeWordEnabled
                ? "WAKE GATE"
                : "OPEN MIC";

        _listenIndicatorLabel.ForeColor = status.IsAvailable
            ? status.IsListening ? Color.FromArgb(106, 220, 197) : Color.FromArgb(255, 191, 105)
            : Color.FromArgb(255, 138, 101);
        _wakeIndicatorLabel.ForeColor = status.IsArmed
            ? Color.FromArgb(255, 138, 101)
            : Color.FromArgb(132, 196, 255);

        if (_currentOptions.SpeechRecognitionPushToTalkEnabled)
        {
            _pushToTalkButton.Visible = true;
            _pushToTalkButton.Text = status.IsListening ? "Stop PTT" : "Start PTT";
        }
        else
        {
            _pushToTalkButton.Visible = false;
        }

        _micToggleButton.Text = status.IsListening ? "Mute Mic" : "Start Mic";
        _micToggleButton.Enabled = status.IsAvailable || status.IsListening;

        if (!appendToFeed
            || _isInTrayMode
            || string.Equals(_lastVoiceStatusText, status.StatusText, StringComparison.Ordinal))
        {
            _lastVoiceStatusText = status.StatusText;
            RecomputeSurfaceState();
            return;
        }

        _lastVoiceStatusText = status.StatusText;
        AppendRuntimeMessage(status.StatusText);
        AppendTimelineEntry("VOICE", status.StatusText, "Voice runtime status changed.", Color.FromArgb(173, 216, 230));
        RecomputeSurfaceState();
    }

    private void UpdateSignalUi(VoiceSignalSnapshot signal)
    {
        _latestSignal = signal;
        _rmsLabel.Text = $"RMS {signal.RmsLevel:P0}";
        _peakLabel.Text = $"PEAK {signal.PeakLevel:P0}";
        _waveformControl.PushSignal(signal);



        RecomputeSurfaceState();
    }

    private void UpdateSpeakerUi(SpeakerStatusSnapshot status)
    {
        _waveformControl.SetSpeaking(status.IsSpeaking);
        RecomputeSurfaceState();
    }

    private void UpdateAssistantRuntimeUi(AssistantActivityEvent activity)
    {
        _assistantStatusLabel.Text = activity.Message;
        _lastAssistantKind = activity.Kind;
        _lastAssistantDetail = string.IsNullOrWhiteSpace(activity.Detail) ? activity.Message : activity.Detail;

        if (!string.IsNullOrWhiteSpace(activity.SessionId))
        {
            _activeSessionId = activity.SessionId;
            _ = RefreshSessionSnapshotAsync();
        }

        SyncAssistantSurfaceState(activity);

        if (!_isInTrayMode
            && !string.Equals(_lastAssistantStatusText, activity.Message, StringComparison.Ordinal))
        {
            _lastAssistantStatusText = activity.Message;

            if (!activity.IsTransient)
            {
                switch (activity.Kind)
                {
                    case AssistantActivityKind.Error:
                        AppendSystemMessage(activity.Message);
                        break;

                    default:
                        AppendRuntimeMessage(activity.Message);
                        break;
                }
            }
        }

        if (ShouldCreateActivityCard(activity))
        {
            AddActivityCard(
                activity.Kind.ToString(),
                activity.Message,
                string.IsNullOrWhiteSpace(activity.Detail) ? "No extra detail supplied." : activity.Detail,
                GetActivityAccent(activity.Kind));
        }

        AppendTimelineEntry(
            activity.Kind.ToString().ToUpperInvariant(),
            activity.Message,
            string.IsNullOrWhiteSpace(activity.Detail) ? "Runtime emitted an activity signal." : TrimForSingleLine(activity.Detail, 180),
            GetActivityAccent(activity.Kind));

        if (activity.Kind == AssistantActivityKind.ToolStart)
        {
            MaybeShowActionCue(activity);
        }

        RecomputeSurfaceState();
    }

    private void MaybeShowActionCue(AssistantActivityEvent activity)
    {
        var toolName = activity.Message.Replace("Running ", string.Empty, StringComparison.OrdinalIgnoreCase).Trim().TrimEnd('.');

        if (!new[]
            {
                "open app",
                "open path",
                "browse",
                "search web",
                "run",
                "spotify",
                "code",
                "chrome",
                "explorer",
                "office",
                "discord",
                "slack",
                "screen"
            }.Contains(toolName, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var headline = $"JARVIS {toolName.ToUpperInvariant()}";
        var detail = string.IsNullOrWhiteSpace(activity.Detail) ? activity.Message : TrimForSingleLine(activity.Detail, 120);
        _actionCueOverlay.ShowCue(Screen.FromControl(this), headline, detail, GetActivityAccent(activity.Kind));
    }

    private void SyncAssistantSurfaceState(AssistantActivityEvent activity)
    {
        if (_isAssistantResponseStreaming || !_isSubmitting)
        {
            return;
        }

        switch (activity.Kind)
        {
            case AssistantActivityKind.ToolStart:
                SetAssistantStateOverride(SurfaceState.Acting, BuildSurfaceDetail(activity));
                break;

            case AssistantActivityKind.ToolComplete:
            case AssistantActivityKind.Status:
            case AssistantActivityKind.Classification:
            case AssistantActivityKind.Planning:
            case AssistantActivityKind.Reflection:
            case AssistantActivityKind.Background:
            case AssistantActivityKind.Summary:
            case AssistantActivityKind.Warning:
                SetAssistantStateOverride(SurfaceState.Thinking, BuildSurfaceDetail(activity));
                break;

            case AssistantActivityKind.Error:
                ClearAssistantStateOverride();
                break;
        }
    }

    private bool ShouldCreateActivityCard(AssistantActivityEvent activity)
    {
        if (activity.Kind is AssistantActivityKind.ToolStart
            or AssistantActivityKind.ToolComplete
            or AssistantActivityKind.Reflection
            or AssistantActivityKind.Summary
            or AssistantActivityKind.Warning
            or AssistantActivityKind.Error
            or AssistantActivityKind.Background
            or AssistantActivityKind.Clarification)
        {
            return true;
        }

        return !activity.IsTransient;
    }

    private void SetAssistantStateOverride(SurfaceState state, string detail)
    {
        _assistantStateOverride = state;
        _assistantStateOverrideDetail = detail;
        RecomputeSurfaceState();
    }

    private void ClearAssistantStateOverride()
    {
        _assistantStateOverride = null;
        _assistantStateOverrideDetail = string.Empty;
    }

    private static string BuildSurfaceDetail(AssistantActivityEvent activity)
    {
        if (!string.IsNullOrWhiteSpace(activity.Detail))
        {
            return activity.Detail.Trim();
        }

        return string.IsNullOrWhiteSpace(activity.Message)
            ? "Processing the current directive."
            : activity.Message.Trim();
    }

    private static IEnumerable<string> EnumerateResponseChunks(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            yield break;
        }

        var builder = new StringBuilder();

        foreach (var character in message)
        {
            builder.Append(character);

            if (char.IsWhiteSpace(character) || char.IsPunctuation(character))
            {
                yield return builder.ToString();
                builder.Clear();
            }
        }

        if (builder.Length > 0)
        {
            yield return builder.ToString();
        }
    }

    private static int GetResponseChunkDelay(string chunk)
    {
        if (string.IsNullOrEmpty(chunk))
        {
            return 0;
        }

        if (string.IsNullOrWhiteSpace(chunk))
        {
            return chunk.Contains('\n') ? 24 : 0;
        }

        var trimmed = chunk.TrimEnd();
        var lastCharacter = trimmed.Length == 0 ? '\0' : trimmed[^1];

        return lastCharacter switch
        {
            '.' or '!' or '?' => 56,
            ',' or ';' or ':' => 34,
            _ => Math.Clamp(14 + (trimmed.Length * 2), 18, 38)
        };
    }

    private void RecomputeSurfaceState()
    {
        var speakerStatus = _context.Speaker.CurrentStatus;

        if (_pendingApproval is not null)
        {
            UpdateSurfaceState(SurfaceState.AwaitingApproval, TrimForSingleLine(_pendingApproval.Summary, 110));
            return;
        }

        if (_pendingVoiceClarification is not null)
        {
            UpdateSurfaceState(SurfaceState.Listening, TrimForSingleLine(_pendingVoiceClarification.Prompt, 110));
            return;
        }

        if (_lastAssistantKind == AssistantActivityKind.Error)
        {
            UpdateSurfaceState(SurfaceState.Error, TrimForSingleLine(_lastAssistantDetail, 110));
            return;
        }

        if (speakerStatus.IsSpeaking)
        {
            UpdateSurfaceState(SurfaceState.Speaking, string.IsNullOrWhiteSpace(speakerStatus.Message) ? "Speaking the latest response." : TrimForSingleLine(speakerStatus.Message, 110));
            return;
        }

        if (_assistantStateOverride is SurfaceState assistantStateOverride)
        {
            var detail = string.IsNullOrWhiteSpace(_assistantStateOverrideDetail)
                ? "Processing the current directive."
                : TrimForSingleLine(_assistantStateOverrideDetail, 110);
            UpdateSurfaceState(assistantStateOverride, detail);
            return;
        }

        if (_isAwaitingFollowUpInput)
        {
            UpdateSurfaceState(
                SurfaceState.Listening,
                string.IsNullOrWhiteSpace(_awaitingFollowUpDetail)
                    ? "Awaiting the next instruction."
                    : TrimForSingleLine(_awaitingFollowUpDetail, 110));
            return;
        }

        if (_currentVoiceRuntime.CurrentStatus.IsListening)
        {
            var detail = _latestSignal.IsSpeechDetected
                ? "Voice activity detected on the active microphone."
                : TrimForSingleLine(_currentVoiceRuntime.CurrentStatus.StatusText, 110);
            UpdateSurfaceState(SurfaceState.Listening, detail);
            return;
        }

        UpdateSurfaceState(SurfaceState.Idle, "Stand by for voice, operator, or background automation.");
    }

    private void UpdateSurfaceState(SurfaceState state, string detail, bool playCue = true)
    {
        if (_surfaceState == state && string.Equals(_surfaceDetailLabel.Text, detail, StringComparison.Ordinal))
        {
            return;
        }

        var previousState = _surfaceState;
        _surfaceState = state;
        _surfaceStateLabel.Text = GetSurfaceStateLabel(state);
        _surfaceStateLabel.ForeColor = GetStateAccent(state);
        _surfaceDetailLabel.Text = detail;
        _waveformControl.AccentColor = GetStateAccent(state);
        UpdateStageLabels();
        UpdateSurfaceModeButtons();

        if (playCue && previousState != state)
        {
            AppendTimelineEntry(
                "STATE",
                $"{GetSurfaceStateLabel(previousState)} -> {GetSurfaceStateLabel(state)}",
                TrimForSingleLine(detail, 180),
                GetStateAccent(state));
            PlayStateCue(state);
        }
    }

    private void UpdateStageLabels()
    {
        foreach (var pair in _stageLabels)
        {
            var isActive = pair.Key == _surfaceState;
            pair.Value.ForeColor = isActive ? GetStateAccent(_surfaceState) : Color.FromArgb(109, 130, 152);
            pair.Value.BackColor = isActive ? Color.FromArgb(24, 42, 62) : Color.FromArgb(12, 20, 34);
            pair.Value.BorderStyle = BorderStyle.FixedSingle;
        }
    }

    private void PlayStateCue(SurfaceState state)
    {
        try
        {
            switch (state)
            {
                case SurfaceState.AwaitingApproval:
                    SystemSounds.Question.Play();
                    break;

                case SurfaceState.Acting:
                    SystemSounds.Exclamation.Play();
                    break;

                case SurfaceState.Speaking:
                    SystemSounds.Asterisk.Play();
                    break;

                case SurfaceState.Error:
                    SystemSounds.Hand.Play();
                    break;
            }
        }
        catch
        {
        }
    }

    private void UpdateApprovalUi(PendingApprovalAction? pendingApproval)
    {
        _pendingApproval = pendingApproval;
        _approvalPanel.Visible = pendingApproval is not null;
        _rightColumnLayout.RowStyles[1].Height = pendingApproval is null ? 0f : 132f;

        if (pendingApproval is null)
        {
            RecomputeSurfaceState();
            return;
        }

        _approvalSummaryLabel.Text = pendingApproval.Summary;
        _approvalPromptLabel.Text = TrimForSingleLine(pendingApproval.ApprovalPrompt.ReplaceLineEndings(" "), 220);
        RecomputeSurfaceState();
    }

    private void AddActivityCard(string title, string subtitle, string detail, Color accent)
    {
        var card = new GlassPanel
        {
            Width = 10,
            Height = 104,
            Margin = new Padding(0, 0, 0, 10),
            Padding = new Padding(14, 12, 14, 12),
            SurfaceColor = Color.FromArgb(16, 28, 44),
            SurfaceColor2 = Color.FromArgb(10, 20, 34),
            AccentColor = accent,
            BorderColor = Color.FromArgb(40, 72, 102)
        };

        var titleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 20,
            ForeColor = accent,
            Font = new Font("Bahnschrift SemiBold", 10.5f, FontStyle.Bold, GraphicsUnit.Point),
            Text = TrimForSingleLine(title, 42)
        };

        var subtitleLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = Color.FromArgb(231, 239, 248),
            Font = new Font("Consolas", 9.85f, FontStyle.Regular, GraphicsUnit.Point),
            Text = TrimForSingleLine(subtitle, 72)
        };

        var detailLabel = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(165, 188, 210),
            Font = new Font("Consolas", 9.25f, FontStyle.Regular, GraphicsUnit.Point),
            Text = TrimForSingleLine(detail, 120)
        };

        card.Controls.Add(detailLabel);
        card.Controls.Add(subtitleLabel);
        card.Controls.Add(titleLabel);
        titleLabel.BringToFront();

        _activityFlow.Controls.Add(card);
        _activityFlow.Controls.SetChildIndex(_sessionSummaryCard, 0);
        UpdateActivityCardWidths();

        while (_activityFlow.Controls.Count > 11)
        {
            var lastIndex = _activityFlow.Controls.Count - 1;
            var control = _activityFlow.Controls[lastIndex];

            if (ReferenceEquals(control, _sessionSummaryCard))
            {
                break;
            }

            _activityFlow.Controls.RemoveAt(lastIndex);
            control.Dispose();
        }
    }

    private void UpdateActivityCardWidths()
    {
        var width = Math.Max(250, _activityFlow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 14);
        _sessionSummaryCard.Width = width;

        foreach (var control in _activityFlow.Controls.Cast<Control>())
        {
            control.Width = width;
        }
    }

    private void AppendOperatorMessage(string source, string message)
    {
        var accent = source.Equals("VOICE", StringComparison.OrdinalIgnoreCase)
            ? Color.FromArgb(173, 216, 230)
            : source.Contains("APPROVAL", StringComparison.OrdinalIgnoreCase) || source.Contains("DENIAL", StringComparison.OrdinalIgnoreCase)
                ? Color.FromArgb(255, 138, 101)
                : Color.FromArgb(255, 191, 105);

        AppendMessage(source, message, accent);
    }

    private void BeginAssistantMessageStream()
    {
        if (!CanUseUi() || _isAssistantHistoryStreamActive)
        {
            return;
        }

        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        _historyBox.SelectionStart = _historyBox.TextLength;
        _historyBox.SelectionLength = 0;
        _historyBox.SelectionColor = Color.FromArgb(106, 220, 197);
        _historyBox.SelectionFont = _historySourceFont;
        _historyBox.AppendText($"[{timestamp}] {_currentOptions.AssistantName.ToUpperInvariant()}{Environment.NewLine}");
        _historyBox.SelectionColor = Color.FromArgb(236, 243, 252);
        _historyBox.SelectionFont = _historyMessageFont;
        _historyBox.ScrollToCaret();
        _isAssistantHistoryStreamActive = true;
    }

    private void AppendAssistantMessageChunk(string chunk)
    {
        if (!CanUseUi() || string.IsNullOrEmpty(chunk))
        {
            return;
        }

        if (!_isAssistantHistoryStreamActive)
        {
            BeginAssistantMessageStream();
        }

        _historyBox.SelectionStart = _historyBox.TextLength;
        _historyBox.SelectionLength = 0;
        _historyBox.SelectionColor = Color.FromArgb(236, 243, 252);
        _historyBox.SelectionFont = _historyMessageFont;
        _historyBox.AppendText(chunk);
        _historyBox.ScrollToCaret();
    }

    private void CompleteAssistantMessageStream()
    {
        if (!CanUseUi() || !_isAssistantHistoryStreamActive)
        {
            return;
        }

        _historyBox.SelectionStart = _historyBox.TextLength;
        _historyBox.SelectionLength = 0;
        _historyBox.SelectionColor = _historyBox.ForeColor;
        _historyBox.SelectionFont = _historyMessageFont;
        _historyBox.AppendText($"{Environment.NewLine}{Environment.NewLine}");
        _historyBox.ScrollToCaret();
        _isAssistantHistoryStreamActive = false;
    }

    private void AppendSystemMessage(string message)
    {
        AppendMessage("SYSTEM", message, Color.FromArgb(255, 138, 101));
    }

    private void AppendRuntimeMessage(string message)
    {
        AppendMessage("RUNTIME", message, Color.FromArgb(132, 196, 255));
    }

    private void AppendMessage(string source, string message, Color accent)
    {
        if (!CanUseUi())
        {
            return;
        }

        if (_isAssistantHistoryStreamActive)
        {
            CompleteAssistantMessageStream();
        }

        var timestamp = DateTime.Now.ToString("HH:mm:ss");

        _historyBox.SelectionStart = _historyBox.TextLength;
        _historyBox.SelectionLength = 0;
        _historyBox.SelectionColor = accent;
        _historyBox.SelectionFont = _historySourceFont;
        _historyBox.AppendText($"[{timestamp}] {source}{Environment.NewLine}");
        _historyBox.SelectionColor = Color.FromArgb(236, 243, 252);
        _historyBox.SelectionFont = _historyMessageFont;
        _historyBox.AppendText($"{message}{Environment.NewLine}{Environment.NewLine}");
        _historyBox.SelectionColor = _historyBox.ForeColor;
        _historyBox.ScrollToCaret();
    }

    private void AppendTimelineEntry(string category, string detail, string justification, Color accent)
    {
        if (!CanUseUi())
        {
            return;
        }

        var timestamp = DateTime.Now.ToString("HH:mm:ss");

        _timelineBox.SelectionStart = _timelineBox.TextLength;
        _timelineBox.SelectionLength = 0;
        _timelineBox.SelectionColor = accent;
        _timelineBox.SelectionFont = _historySourceFont;
        _timelineBox.AppendText($"[{timestamp}] {category}{Environment.NewLine}");
        _timelineBox.SelectionColor = Color.FromArgb(236, 243, 252);
        _timelineBox.SelectionFont = _historyMessageFont;
        _timelineBox.AppendText($"{detail}{Environment.NewLine}");

        if (!string.IsNullOrWhiteSpace(justification))
        {
            _timelineBox.SelectionColor = Color.FromArgb(166, 188, 210);
            _timelineBox.SelectionFont = _historyMessageFont;
            _timelineBox.AppendText($"why: {justification}{Environment.NewLine}");
        }

        _timelineBox.AppendText(Environment.NewLine);
        _timelineBox.SelectionColor = _timelineBox.ForeColor;
        _timelineBox.ScrollToCaret();
    }

    private async void InputBoxOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || e.Shift)
        {
            return;
        }

        e.SuppressKeyPress = true;
        await SubmitPromptAsync();
    }

    private void SetBusyState(bool isBusy)
    {
        _sendButton.Enabled = !isBusy;
        _helpButton.Enabled = !isBusy;
        _statusButton.Enabled = !isBusy;
        _notesButton.Enabled = !isBusy;
        _memoryButton.Enabled = !isBusy;
        _approveButton.Enabled = !isBusy;
        _denyButton.Enabled = !isBusy;
        _inputBox.Enabled = !isBusy;
        Cursor = isBusy ? Cursors.WaitCursor : Cursors.Default;
    }

    private async Task TrySpeakAsync(string message, CancellationToken cancellationToken)
    {
        var shouldPauseMicrophone = string.Equals(
                _currentOptions.SpeechRecognitionMode,
                "half-duplex",
                StringComparison.OrdinalIgnoreCase)
            && _currentVoiceRuntime.CurrentStatus.IsListening;

        try
        {
            if (shouldPauseMicrophone)
            {
                await _currentVoiceRuntime.StopAsync(cancellationToken);
            }

            await _context.Speaker.SpeakAsync(message, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
        }
        finally
        {
            if (shouldPauseMicrophone
                && !_currentOptions.SpeechRecognitionPushToTalkEnabled
                && !_cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    await _currentVoiceRuntime.StartAsync(_cancellationTokenSource.Token);
                }
                catch
                {
                }
            }
        }
    }

    private bool ShouldSpeakVoiceAcknowledgement(string source)
    {
        return _currentOptions.VoiceEnabled
            && source.Equals("VOICE", StringComparison.OrdinalIgnoreCase)
            && IsAssistantInBackground();
    }

    private bool IsAssistantInBackground()
    {
        return _isInTrayMode
            || !Visible
            || WindowState == FormWindowState.Minimized;
    }

    private string BuildVoiceAcknowledgement(string input)
    {
        return BuildImmediateAcknowledgement(input);
    }

    private string BuildProcessingConsoleText(string source, string renderedInput, string input)
    {
        return string.Join(
            Environment.NewLine,
            $"[{source}] {renderedInput}",
            string.Empty,
            $"[ACKNOWLEDGED] {BuildImmediateAcknowledgement(input)}",
            $"[SITUATION] {_missionBriefText}",
            "[STATUS] THINKING | Analyzing the directive and preparing the response stream.");
    }

    private string BuildImmediateAcknowledgement(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "I'm listening.";
        }

        if (AssistantPresenceStyle.TryBuildAcknowledgement(input, _currentOptions, out var acknowledgement))
        {
            return acknowledgement;
        }

        var normalized = input.Trim();

        if (StartsWithDirective(normalized, "research", "search web"))
        {
            return "Pulling intelligence.";
        }

        if (StartsWithDirective(normalized, "screen", "analyze image", "analyze document", "ambient"))
        {
            return "Scanning the visual feed.";
        }

        if (StartsWithDirective(normalized, "weather"))
        {
            return "Checking the forecast.";
        }

        if (StartsWithDirective(normalized, "remember", "note", "remind me", "reminders"))
        {
            return "Logging it.";
        }

        if (StartsWithDirective(normalized, "computer", "system", "network", "status"))
        {
            return "Checking the system.";
        }

        if (StartsWithDirective(normalized, "browse", "chrome"))
        {
            return "Routing navigation.";
        }

        if (StartsWithDirective(normalized, "spotify", "media"))
        {
            return "Routing audio control.";
        }

        if (StartsWithDirective(normalized, "code", "office", "explorer", "word", "excel", "powerpoint", "power point", "ppt", "outlook"))
        {
            return "Opening the workspace.";
        }

        if (StartsWithDirective(normalized, "discord", "slack", "open app", "launch", "start"))
        {
            return "Opening it.";
        }

        if (StartsWithDirective(normalized, "focus app", "window state", "move window", "resize window", "snap window", "send hotkey", "click element", "type into", "get element text", "scroll"))
        {
            return "Working the desktop.";
        }

        if (StartsWithDirective(normalized, "run", "shell"))
        {
            return "Running it.";
        }

        return "On it.";
    }

    private string BuildMissionBrief(DesktopContextSnapshot? snapshot)
    {
        var timestamp = snapshot?.LocalTime.ToLocalTime() ?? DateTimeOffset.Now;
        var activeApp = snapshot is null
            ? "Unavailable"
            : !string.IsNullOrWhiteSpace(snapshot.ActiveApplication)
                ? snapshot.ActiveApplication
                : !string.IsNullOrWhiteSpace(snapshot.ActiveWindow)
                    ? snapshot.ActiveWindow
                    : "Desktop";
        var objective = ResolveMissionObjective();

        return string.Join(
            "  |  ",
            $"LOCAL {timestamp:hh:mm tt}",
            $"ACTIVE {TrimForSingleLine(activeApp, 28)}",
            $"OBJECTIVE {TrimForSingleLine(objective, 68)}");
    }

    private string ResolveMissionObjective()
    {
        if (_pendingApproval is not null)
        {
            return _pendingApproval.Summary;
        }

        if (_pendingVoiceClarification is not null)
        {
            return _pendingVoiceClarification.Prompt;
        }

        if (_isAwaitingFollowUpInput)
        {
            return string.IsNullOrWhiteSpace(_awaitingFollowUpDetail)
                ? "Awaiting follow-up input."
                : _awaitingFollowUpDetail;
        }

        if (_lastAssistantKind == AssistantActivityKind.Error)
        {
            return string.IsNullOrWhiteSpace(_lastAssistantDetail)
                ? "Recovering from a runtime fault."
                : _lastAssistantDetail;
        }

        if (_context.Speaker.CurrentStatus.IsSpeaking)
        {
            return "Delivering the latest response.";
        }

        if (_isSubmitting || _isAssistantResponseStreaming)
        {
            return string.IsNullOrWhiteSpace(_currentMissionObjective)
                ? "Processing the current directive."
                : _currentMissionObjective;
        }

        return "Stand by for the next directive.";
    }

    private static string DescribeMissionObjective(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "Awaiting the next directive.";
        }

        var trimmed = TrimForSingleLine(input, 84);
        return $"Execute: {trimmed}";
    }

    private static string BuildPostTurnObjective(AssistantTurn turn, PendingApprovalAction? pendingApproval)
    {
        if (pendingApproval is not null)
        {
            return pendingApproval.Summary;
        }

        if (turn.IsFollowUpQuestion)
        {
            return turn.ResponseText;
        }

        if (turn.ShouldExit)
        {
            return "Shutdown sequence complete.";
        }

        return "Stand by for the next directive.";
    }

    private static string BuildPresenceUpdateSummary(JarvisOptions options)
    {
        var nickname = string.IsNullOrWhiteSpace(options.AssistantUserNickname)
            ? "none"
            : options.AssistantUserNickname.Trim();
        var pronunciationCount = AssistantPersonaConfiguration.ResolvePronunciationHints(options).Count;

        return string.Join(
            " | ",
            $"Preset {AssistantPersonaConfiguration.ResolvePresencePreset(options)}",
            $"Style {AssistantPersonaConfiguration.ResolveResponseStyle(options)}",
            $"Small talk {(options.AssistantSmallTalkEnabled ? "enabled" : "minimal")}",
            $"Nickname {nickname}",
            $"Pronunciation hints {pronunciationCount}");
    }

    private static bool StartsWithDirective(string input, params string[] directives)
    {
        foreach (var directive in directives)
        {
            if (input.Equals(directive, StringComparison.OrdinalIgnoreCase)
                || input.StartsWith(directive + " ", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private string BuildDisplayResponse(AssistantTurn turn, PendingApprovalAction? pendingApproval)
    {
        if (pendingApproval is null)
        {
            return turn.ResponseText;
        }

        return string.Join(
            Environment.NewLine,
            "Approval required before Jarvis continues.",
            pendingApproval.Summary,
            "Use the approval card to approve or deny the action directly in the UI.");
    }

    private string BuildTurnSummary(AssistantTurn turn, PendingApprovalAction? pendingApproval, string renderedResponse)
    {
        var lines = new List<string>
        {
            $"Intent: {turn.Intent}",
            string.IsNullOrWhiteSpace(turn.SessionId) ? "Session: direct route" : $"Session: {ShortId(turn.SessionId)}",
            $"Follow-up required: {(turn.IsFollowUpQuestion ? "yes" : "no")}"
        };

        if (pendingApproval is not null)
        {
            lines.Add($"Approval gate: {pendingApproval.Summary}");
        }

        if (turn.ToolResults is { Length: > 0 })
        {
            lines.Add(string.Empty);
            lines.Add("Tool results:");
            lines.AddRange(turn.ToolResults.Take(4).Select(tool =>
                $"- {tool.ToolName}: {FormatToolOutcome(tool)} | {TrimForSingleLine(BuildToolSummary(tool), 72)}"));
            lines.Add($"Verification: {BuildVerificationOverview(turn.ToolResults)}");
        }

        if (turn.IsFollowUpQuestion)
        {
            lines.Add(string.Empty);
            lines.Add("Clarification:");
            lines.Add($"Question: {TrimForSingleLine(renderedResponse, 110)}");
            lines.Add("Reply guidance: answer the missing detail directly and Jarvis will continue the same workflow.");
        }

        if (turn.Citations is { Length: > 0 })
        {
            lines.Add(string.Empty);
            lines.Add($"Citations: {turn.Citations.Length}");
        }

        lines.Add(string.Empty);
        lines.Add($"Latest response: {TrimForSingleLine(renderedResponse, 120)}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildSessionSummary(PersistedAgentSession session)
    {
        var lines = new List<string>
        {
            $"Session: {ShortId(session.SessionId)}  |  {session.Status}  |  {session.Intent}",
            $"Updated: {session.UpdatedAtUtc.ToLocalTime():MM-dd h:mm tt}",
            $"Request: {TrimForSingleLine(session.OriginalRequest, 110)}"
        };

        if (!string.IsNullOrWhiteSpace(session.LatestInput)
            && !string.Equals(session.LatestInput, session.OriginalRequest, StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"Latest input: {TrimForSingleLine(session.LatestInput, 110)}");
        }

        if (!string.IsNullOrWhiteSpace(session.FollowUpQuestion))
        {
            lines.Add(string.Empty);
            lines.Add("Clarification:");
            lines.Add($"Question: {TrimForSingleLine(session.FollowUpQuestion, 104)}");
            lines.Add("Reply guidance: answer the missing detail directly to continue the same session.");
        }

        if (session.Steps.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Plan:");
            lines.AddRange(session.Steps.Take(4).Select(step =>
                $"- [{step.Status}] {step.Title}: {TrimForSingleLine(step.Detail, 72)}"));
        }

        if (session.ToolResults.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add("Latest tools:");
            lines.AddRange(session.ToolResults.Take(4).Select(tool =>
                $"- {tool.ToolName}: {FormatToolOutcome(tool)} | {TrimForSingleLine(BuildToolSummary(tool), 68)}"));
            lines.Add($"Verification: {BuildVerificationOverview(session.ToolResults)}");
        }

        if (!string.IsNullOrWhiteSpace(session.LatestResponse))
        {
            lines.Add(string.Empty);
            lines.Add($"Latest response: {TrimForSingleLine(session.LatestResponse, 104)}");
        }

        if (!string.IsNullOrWhiteSpace(session.FollowUpQuestion))
        {
            lines.Add(string.Empty);
            lines.Add($"Follow-up: {TrimForSingleLine(session.FollowUpQuestion, 96)}");
        }

        if (session.Citations.Length > 0)
        {
            lines.Add(string.Empty);
            lines.Add($"Citations: {session.Citations.Length}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildClarificationSnapshotKey(PersistedAgentSession session) =>
        string.Join(
            "|",
            session.SessionId,
            session.UpdatedAtUtc.UtcTicks.ToString(),
            session.FollowUpQuestion);

    private static string BuildClarificationConsoleText(PersistedAgentSession session)
    {
        var lines = new List<string>
        {
            "[FOLLOW-UP REQUIRED]",
            $"Question: {session.FollowUpQuestion}",
            $"Original request: {TrimForSingleLine(session.OriginalRequest, 160)}",
            "Reply guidance: answer the missing detail directly and Jarvis will continue the same workflow."
        };

        if (!string.IsNullOrWhiteSpace(session.LatestInput)
            && !string.Equals(session.LatestInput, session.OriginalRequest, StringComparison.OrdinalIgnoreCase))
        {
            lines.Add($"Latest input: {TrimForSingleLine(session.LatestInput, 160)}");
        }

        if (session.ToolResults.Length > 0)
        {
            lines.Add($"Current evidence: {BuildVerificationOverview(session.ToolResults)}");
            lines.Add("Recent tools:");
            lines.AddRange(session.ToolResults.Take(3).Select(tool =>
                $"- {tool.ToolName}: {FormatToolOutcome(tool)} | {TrimForSingleLine(BuildToolSummary(tool), 88)}"));
        }

        if (session.Steps.Length > 0)
        {
            var latestStep = session.Steps[^1];
            lines.Add($"Latest plan step: {latestStep.Title} | {TrimForSingleLine(latestStep.Detail, 120)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildClarificationWhy(PersistedAgentSession session)
    {
        var parts = new List<string>
        {
            $"Original request: {TrimForSingleLine(session.OriginalRequest, 84)}."
        };

        if (session.ToolResults.Length > 0)
        {
            parts.Add($"Current evidence: {BuildVerificationOverview(session.ToolResults)}.");
        }

        if (session.Steps.Length > 0)
        {
            var latestStep = session.Steps[^1];
            parts.Add($"Latest step: {TrimForSingleLine($"{latestStep.Title} - {latestStep.Detail}", 96)}.");
        }

        parts.Add("Reply with the missing detail directly to continue the same session.");
        return string.Join(" ", parts);
    }

    private async Task<bool> TryResolvePendingVoiceClarificationAsync(string input, string source, bool clearInput)
    {
        var pending = _pendingVoiceClarification;

        if (pending is null)
        {
            return false;
        }

        if (clearInput)
        {
            _inputBox.Clear();
        }

        if (VoiceRecognitionText.LooksLikeConfirmationApproval(input))
        {
            _pendingVoiceClarification = null;
            AppendTimelineEntry(
                "VOICE",
                "Low-confidence directive confirmed.",
                BuildVoiceClarificationWhy(pending.Text, pending.RawText, pending.Confidence),
                Color.FromArgb(255, 210, 132));

            var confirmationSource = source.Equals("VOICE", StringComparison.OrdinalIgnoreCase) ? "VOICE" : "YOU";
            await ProcessPromptAsync(
                pending.Text,
                confirmationSource,
                clearInput: false,
                displayedInput: $"Confirmed voice directive: {pending.Text}");
            return true;
        }

        if (VoiceRecognitionText.LooksLikeConfirmationDenial(input))
        {
            _pendingVoiceClarification = null;
            await RecordRejectedVoiceTranscriptAsync(pending.RawText, pending.Confidence);
            var message = "Voice directive canceled. Say it again when ready.";
            _liveResponseBox.Text = message;
            AppendRuntimeMessage(message);
            AppendTimelineEntry(
                "VOICE",
                "Low-confidence directive canceled.",
                BuildVoiceClarificationWhy(pending.Text, pending.RawText, pending.Confidence),
                Color.FromArgb(255, 138, 101));
            AddActivityCard("Voice", "Recognition canceled", pending.RawText, Color.FromArgb(255, 138, 101));
            RecomputeSurfaceState();

            if (_currentOptions.VoiceEnabled && source.Equals("VOICE", StringComparison.OrdinalIgnoreCase))
            {
                await TrySpeakAsync("Okay. Please say it again.", _cancellationTokenSource.Token);
            }

            return true;
        }

        if (VoiceCorrectionLearning.TryParseCorrectionDirective(input, out var explicitCorrection))
        {
            _pendingVoiceClarification = null;
            await LearnVoiceCorrectionAsync(pending.RawText, explicitCorrection.CorrectedText, pending.Confidence, source);
            AppendTimelineEntry(
                "VOICE",
                "Voice correction learned.",
                $"Stored \"{TrimForSingleLine(pending.RawText, 64)}\" -> \"{TrimForSingleLine(explicitCorrection.CorrectedText, 64)}\" for future recognition.",
                Color.FromArgb(106, 220, 197));
            await ProcessPromptAsync(
                explicitCorrection.CorrectedText,
                source.Equals("VOICE", StringComparison.OrdinalIgnoreCase) ? "VOICE" : "YOU",
                clearInput: false,
                displayedInput: $"Corrected voice directive: {explicitCorrection.CorrectedText}");
            return true;
        }

        if (source.Equals("VOICE", StringComparison.OrdinalIgnoreCase)
            && VoiceCorrectionLearning.CanLearnCorrection(pending.RawText, input))
        {
            _pendingVoiceClarification = null;
            await LearnVoiceCorrectionAsync(pending.RawText, input, pending.Confidence, source);
            AppendTimelineEntry(
                "VOICE",
                "Voice correction learned from the retry.",
                $"Stored \"{TrimForSingleLine(pending.RawText, 64)}\" -> \"{TrimForSingleLine(input, 64)}\" for future recognition.",
                Color.FromArgb(106, 220, 197));
            await ProcessPromptAsync(
                input,
                "VOICE",
                clearInput: false,
                displayedInput: $"Corrected voice directive: {input}");
            return true;
        }

        _pendingVoiceClarification = null;
        AppendTimelineEntry(
            "VOICE",
            "Voice clarification dismissed.",
            "Received a new directive instead of a yes or no confirmation, so Jarvis will route the latest input.",
            Color.FromArgb(255, 191, 105));
        return false;
    }

    private async Task PromptForVoiceClarificationAsync(VoiceCommandRecognizedEventArgs eventArgs)
    {
        var prompt = string.IsNullOrWhiteSpace(eventArgs.ClarificationPrompt)
            ? VoiceRecognitionConfidence.BuildClarificationPrompt(eventArgs.RawText, eventArgs.Confidence)
            : eventArgs.ClarificationPrompt;

        _pendingVoiceClarification = new PendingVoiceClarification(eventArgs.Text, eventArgs.RawText, prompt, eventArgs.Confidence);
        _lastAssistantKind = AssistantActivityKind.Clarification;
        _lastAssistantDetail = prompt;
        _promptEchoLabel.Text = $"VOICE CHECK  |  {TrimForSingleLine(eventArgs.RawText, 132)}";
        _liveResponseBox.Text = prompt;
        AppendRuntimeMessage(prompt);
        AppendTimelineEntry(
            "VOICE",
            "Recognition needs confirmation.",
            BuildVoiceClarificationWhy(eventArgs.Text, eventArgs.RawText, eventArgs.Confidence),
            Color.FromArgb(255, 210, 132));
        AddActivityCard(
            "Voice",
            "Low-confidence recognition",
            TrimForSingleLine(eventArgs.RawText, 108),
            Color.FromArgb(255, 210, 132));
        RecomputeSurfaceState();

        if (_currentOptions.VoiceEnabled)
        {
            await TrySpeakAsync(prompt, _cancellationTokenSource.Token);
        }
    }

    private Task RouteQueuedVoiceDirectiveAsync(QueuedVoiceDirective directive)
    {
        var eventArgs = directive.ToEventArgs();
        return eventArgs.RequiresClarification
            ? PromptForVoiceClarificationAsync(eventArgs)
            : ProcessPromptAsync(eventArgs.Text, "VOICE", clearInput: false);
    }

    private async Task<bool> TryHandleVoiceCorrectionDirectiveAsync(string input, string source, bool clearInput)
    {
        if (VoiceCorrectionLearning.IsReviewCommand(input))
        {
            if (clearInput)
            {
                _inputBox.Clear();
            }

            var review = VoiceCorrectionLearning.BuildReview(_currentOptions);
            _promptEchoLabel.Text = "VOICE CORRECTIONS";
            _liveResponseBox.Text = review;
            AppendRuntimeMessage(review);
            AppendTimelineEntry(
                "VOICE",
                "Displayed learned voice corrections.",
                "Reviewed the stored correction pairs and rejected transcripts.",
                Color.FromArgb(132, 196, 255));
            return true;
        }

        if (!VoiceCorrectionLearning.TryParseCorrectionDirective(input, out var correction))
        {
            return false;
        }

        if (clearInput)
        {
            _inputBox.Clear();
        }

        await LearnVoiceCorrectionAsync(correction.HeardText, correction.CorrectedText, null, source);

        var message = $"Learned voice correction: \"{correction.HeardText}\" -> \"{correction.CorrectedText}\".";
        _promptEchoLabel.Text = "VOICE CORRECTION SAVED";
        _liveResponseBox.Text = message;
        AppendRuntimeMessage(message);
        AppendTimelineEntry(
            "VOICE",
            "Voice correction stored.",
            $"Future recognition will bias \"{TrimForSingleLine(correction.HeardText, 56)}\" toward \"{TrimForSingleLine(correction.CorrectedText, 56)}\".",
            Color.FromArgb(106, 220, 197));
        AddActivityCard("Voice", "Correction learned", correction.CorrectedText, Color.FromArgb(106, 220, 197));
        RecomputeSurfaceState();
        return true;
    }

    private async Task LearnVoiceCorrectionAsync(string heardText, string correctedText, double? confidence, string source)
    {
        var updatedOptions = VoiceCorrectionLearning.RecordCorrection(_currentOptions, heardText, correctedText, confidence);

        if (ReferenceEquals(updatedOptions, _currentOptions)
            || (updatedOptions.SpeechRecognitionCorrections.SequenceEqual(_currentOptions.SpeechRecognitionCorrections)
                && updatedOptions.SpeechRecognitionRejectedPhrases.SequenceEqual(_currentOptions.SpeechRecognitionRejectedPhrases)))
        {
            return;
        }

        _currentOptions = updatedOptions;
        await PersistCurrentOptionsAsync();
        await ReloadVoiceRuntimeAsync(_currentOptions);

        if (_currentOptions.VoiceEnabled && source.Equals("VOICE", StringComparison.OrdinalIgnoreCase))
        {
            await TrySpeakAsync("Learned it. I will bias future transcripts toward that wording.", _cancellationTokenSource.Token);
        }
    }

    private async Task RecordRejectedVoiceTranscriptAsync(string heardText, double? confidence)
    {
        var updatedOptions = VoiceCorrectionLearning.RecordRejectedPhrase(_currentOptions, heardText, confidence);

        if (ReferenceEquals(updatedOptions, _currentOptions)
            || updatedOptions.SpeechRecognitionRejectedPhrases.SequenceEqual(_currentOptions.SpeechRecognitionRejectedPhrases))
        {
            return;
        }

        _currentOptions = updatedOptions;
        await PersistCurrentOptionsAsync();
    }

    private static string BuildVoiceClarificationWhy(string text, string rawText, double? confidence)
    {
        var confidenceText = confidence is double value ? $"{value:P0}" : "unknown";
        var heardText = string.IsNullOrWhiteSpace(rawText) ? text : rawText;

        if (string.Equals(heardText, text, StringComparison.OrdinalIgnoreCase))
        {
            return $"Jarvis heard \"{TrimForSingleLine(heardText, 96)}\" with {confidenceText} confidence and is waiting for confirmation or a correction before acting.";
        }

        return $"Jarvis heard \"{TrimForSingleLine(heardText, 72)}\" with {confidenceText} confidence. The routed directive would be \"{TrimForSingleLine(text, 72)}\" unless you confirm or correct it.";
    }

    private static Color GetActivityAccent(AssistantActivityKind kind) =>
        kind switch
        {
            AssistantActivityKind.Error => Color.FromArgb(255, 138, 101),
            AssistantActivityKind.Warning => Color.FromArgb(255, 191, 105),
            AssistantActivityKind.ToolStart => Color.FromArgb(132, 196, 255),
            AssistantActivityKind.ToolComplete => Color.FromArgb(106, 220, 197),
            AssistantActivityKind.Summary => Color.FromArgb(106, 220, 197),
            AssistantActivityKind.Reflection => Color.FromArgb(145, 186, 255),
            AssistantActivityKind.Background => Color.FromArgb(186, 150, 255),
            AssistantActivityKind.Clarification => Color.FromArgb(255, 210, 132),
            _ => Color.FromArgb(173, 216, 230)
        };

    private static string FormatToolOutcome(AssistantToolExecution tool)
    {
        var status = string.IsNullOrWhiteSpace(tool.VerificationStatus)
            ? tool.Succeeded ? "observed" : "failed"
            : tool.VerificationStatus.Replace('_', ' ').Trim().ToLowerInvariant();
        return status;
    }

    private static string BuildToolSummary(AssistantToolExecution tool)
    {
        if (!string.IsNullOrWhiteSpace(tool.VerificationSummary))
        {
            return tool.VerificationSummary;
        }

        if (!string.IsNullOrWhiteSpace(tool.Summary))
        {
            return tool.Summary;
        }

        return tool.VerificationText;
    }

    private static string BuildVerificationOverview(IReadOnlyList<AssistantToolExecution> toolResults)
    {
        if (toolResults.Count == 0)
        {
            return "no tool evidence";
        }

        var orderedStatuses = new[]
        {
            "verified",
            "observed",
            "awaiting approval",
            "unconfirmed",
            "failed"
        };
        var counts = toolResults
            .GroupBy(tool => FormatToolOutcome(tool), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var parts = new List<string>();

        foreach (var status in orderedStatuses)
        {
            if (counts.TryGetValue(status, out var count) && count > 0)
            {
                parts.Add($"{count} {status}");
            }
        }

        foreach (var extra in counts.Keys
                     .Where(key => !orderedStatuses.Contains(key, StringComparer.OrdinalIgnoreCase))
                     .OrderBy(key => key, StringComparer.OrdinalIgnoreCase))
        {
            parts.Add($"{counts[extra]} {extra}");
        }

        return parts.Count == 0 ? "no tool evidence" : string.Join(" | ", parts);
    }

    private static string GetSurfaceStateLabel(SurfaceState state) =>
        state switch
        {
            SurfaceState.Listening => "LISTENING",
            SurfaceState.Thinking => "THINKING",
            SurfaceState.Acting => "ACTING",
            SurfaceState.Speaking => "SPEAKING",
            SurfaceState.AwaitingApproval => "AWAITING APPROVAL",
            SurfaceState.Error => "RUNTIME ALERT",
            _ => "IDLE"
        };

    private static Color GetStateAccent(SurfaceState state) =>
        state switch
        {
            SurfaceState.Listening => Color.FromArgb(106, 220, 197),
            SurfaceState.Thinking => Color.FromArgb(132, 196, 255),
            SurfaceState.Acting => Color.FromArgb(255, 191, 105),
            SurfaceState.Speaking => Color.FromArgb(255, 138, 101),
            SurfaceState.AwaitingApproval => Color.FromArgb(255, 112, 102),
            SurfaceState.Error => Color.FromArgb(255, 123, 114),
            _ => Color.FromArgb(170, 191, 214)
        };

    private static string TrimForSingleLine(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var flattened = value.ReplaceLineEndings(" ").Trim();
        return flattened.Length <= maxLength ? flattened : flattened[..maxLength] + "...";
    }

    private static string ShortId(string sessionId)
    {
        return string.IsNullOrWhiteSpace(sessionId)
            ? "n/a"
            : sessionId.Length <= 8
                ? sessionId
                : sessionId[..8];
    }
}
