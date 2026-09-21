# Jarvis

A Windows-first starter for a real JARVIS-style assistant.

This repository does not pretend to be the full Iron Man system. It gives you a working local foundation:

- An interactive assistant shell
- A Windows operations surface with live response streaming, waveform telemetry, tray mode, approvals, and voice controls
- Persistent memory, learned preferences, contextual summaries, structured entity memory, reminder recall, retention controls, and transcript logging
- Command routing for local directives
- Realtime streaming speech recognition with rolling OpenAI-compatible fallback and Windows fallback
- Manifest-backed, recognizer-backed wake-word detection on streaming voice paths, with text-prefix and Windows grammar fallbacks
- Per-device microphone calibration, validation, beamforming, wake-asset, and hardware-DSP tuning for half-duplex and full-duplex voice
- Streaming OpenAI-compatible text-to-speech with Windows fallback
- A safe-by-default shell execution toggle
- A true tool-using agent runtime with model routing, reflection, clarification, and persistence

## What Works Now

- `help` shows built-in directives
- `status` reports system mode and enabled features
- `time` reports the current local time
- `remember <note>` stores a memory, and `remember private <note>` keeps it off disk
- `recall <term>` searches memory with semantic ranking plus context from time of day, location, active app, and recent activity
- `notes` lists recent memories plus upcoming reminder memory
- `reminders` lists pending reminders, and `remind me to <task> at|on|in ...` stores time-based reminder memory
- `forget <id-or-phrase>` removes a stored memory from active recall
- `preferences` or `profile` shows learned phrasing, tone, app, location, habit, routine, structured relationship, device, project, place, and reminder preferences
- `privacy` explains off-the-record turns and persistence boundaries
- The live operations surface now has a memory workbench for reviewing, editing, and forgetting stored memory items without raw commands
- Older transcript history is automatically compacted into summary memories so recall can stay useful without replaying every turn
- `export memories <path>`, `import memories <path>`, `export transcripts <path>`, and `import transcripts <path>` move local history in and out of the runtime
- `weather <location>` checks the current weather
- `system` shows computer state, drives, and process count
- `computer` shows the active window, visible desktop apps, workspace entries, and live machine context
- `network`, `network scan`, `network health`, and `network <ip-or-host>` inspect local adapters, discover nearby devices, and run lightweight health checks
- `apps` or `apps <name>` lists discovered running and installed applications
- `open app <name>` now focuses a running app when possible and can discover apps from Start Menu shortcuts, Desktop shortcuts and executables, Windows App Paths, installed-program registry entries, per-user LocalAppData installs, Steam library manifests, Epic launcher manifests, Riot install metadata, Xbox game configs, and best-effort Battle.net installs
- `spotify`, `code`, `chrome`, `explorer`, `office`, `discord`, and `slack` expose app-specific shortcuts for media/search/navigation flows, selector-aware Chrome DOM automation, and direct Office file opens
- `open path <target>`, `list files <path>`, `read file <path>`, and `find files <term>` handle file navigation
- `browse <url>` and `search web <query>` drive browser actions
- `email` and `tasks` expose user-state context for planning from the local snapshot
- `research <query>` runs open-domain web research with citations
- `screen`, `screen monitors`, `screen capture <target>`, `screen ocr <target>`, and `screen ui <target>` add monitor- and window-aware visual grounding, OCR-style extraction, structured UI detection, and targeted screenshot capture
- `analyze document <path>` inspects text files, extracts embedded text from PDFs, and routes image-heavy scans through OCR-style document analysis
- `ambient`, `ambient webcam`, `ambient presence`, `ambient face`, and `ambient room` expose privacy-gated webcam and room-sensor hooks when you wire a frame-drop or sensor snapshot into settings
- `run <PowerShell command>` executes a local command when enabled
- `exit` shuts the assistant down
- You can say `Jarvis` and then speak the next command
- You can also say `Jarvis status` in one utterance
- When the planner is enabled, free-form typed or spoken requests can classify intent, route to cheap or strong models, ask clarifying questions, chain tool calls, reflect on tool results, and persist interrupted workflows
- Planner activity now surfaces in the desktop UI with classification, planning, tool, reflection, clarification, and verification signals instead of only a final reply
- Recent follow-ups such as `open it`, `the second one`, `click that button`, `read text from the earlier label`, and `type hello into that field` can resolve from recent app, file, site, window, and `screen ui` context when the target is clear
- A configurable `assistantPersona` can pin Jarvis's baseline tone and behavior so it stays stable across sessions instead of drifting with recent turns
- The live operations surface now streams assistant replies into both the response console and conversation feed instead of only appending finished assistant blocks
- The surface header now transitions explicitly through listening, thinking, acting, and speaking states so the current phase stays visible during a turn
- The right-side voice panel now resolves microphone-specific wake assets and processing profiles, and can save separate calibration/validation baselines for different wake-asset, beamforming, and DSP combinations on the same device
- In `auto` mode, Jarvis prefers realtime transcription on OpenAI-hosted endpoints, falls back to rolling `/audio/transcriptions` on compatible endpoints, and only then falls back to Windows recognition
- Major shell-driven machine changes now stop for approval first, and protected system locations are blocked outright

## Run It

```powershell
& '.\Build Jarvis.cmd'
& '.\Start Jarvis.cmd'
```

The first run bootstraps a repo-local `.NET SDK 8.0.400` into `tools\dotnet` so the project does not depend on the machine-wide `dotnet` install being correct. If you want to invoke the CLI directly after bootstrap, use `tools\dotnet\dotnet.exe`.

The app reads configuration from `jarvis.settings.json`.

`Start Jarvis.cmd` launches the `Release` build so the running app does not lock the default `Debug` output that the IDE normally uses for editing and debugging.

## Agent Runtime

Jarvis can now run in two modes:

- Bootstrap mode: exact commands plus command-oriented voice shortcuts
- Agent mode: spoken or typed natural language is routed through a structured tool-using runtime that can classify intent, route across fast and reasoning models, call tools directly, reflect on tool results, ask follow-up questions, stream runtime events to the UI, and then reply
- The runtime is grounded with short-term session state, recent saved memory, learned user preferences, pending reminders, recent turns, recent tool results, local user state, and a live computer-context snapshot so it can reason about what is open, what app you are in, and what is happening in the workspace before it decides which tools to call

Recommended LM Studio planner settings:

```json
{
  "plannerEnabled": true,
  "plannerProvider": "local",
  "plannerBaseUrl": "http://127.0.0.1:1234",
  "plannerModel": "qwen3.5-27b-claude-4.6-opus-reasoning-distilled",
  "plannerFastModel": "qwen3.5-27b-claude-4.6-opus-reasoning-distilled",
  "plannerReasoningModel": "qwen3.5-27b-claude-4.6-opus-reasoning-distilled",
  "plannerVisionModel": "qwen3.5-27b-claude-4.6-opus-reasoning-distilled",
  "plannerLocalContextLength": 16384,
  "plannerReflectionEnabled": false,
  "plannerMaxIterations": 3
}
```

Notes:

- for LM Studio, Jarvis accepts the root server URL like `http://127.0.0.1:1234` and normalizes it to the OpenAI-compatible `/v1` routes internally
- when LM Studio's `lms.exe` CLI is available, Jarvis starts the local server and loads the configured model automatically during startup
- on startup, Jarvis now verifies the loaded LM Studio context size and reloads the planner model with `plannerLocalContextLength` if the current load is too small
- when the planner points at a loopback local model, Jarvis now waits for the model to appear and runs a short warm-up request before the main window opens
- local loopback planners use smaller route-based `max_tokens` caps to keep thinking-model replies from stalling on long internal reasoning
- if the local planner cannot warm up, Jarvis stops startup and shows an error instead of opening half-ready
- local OpenAI-compatible endpoints such as LM Studio and Ollama are supported
- `plannerApiKey` can still be set if your local gateway expects one, but it is optional for most loopback setups
- OpenClaw can also sit behind the planner when you point Jarvis at its local Gateway and use `plannerProvider: "openclaw"`
- `plannerProvider: "openclaw-local"` bypasses the Gateway and runs OpenClaw's embedded local runtime through the bundled helper script
- fallback providers and fallback models can be declared in settings for retries and degraded behavior
- when the planner is off or misconfigured, Jarvis falls back to direct commands and heuristic routing only
- set `defaultWeatherLocation` if you want `Jarvis, what's the weather?` to work without specifying a city
- the runtime now uses structured tool calling, tool-result verification, and proactive summaries instead of only suggesting plain-text internal commands
- background tasks are persisted in `agentStateFilePath`, and clarification workflows can resume across turns
- when shell execution is enabled, Jarvis can use `run`, but state-changing commands require explicit approval and protected system paths stay blocked

OpenClaw Gateway example:

```json
{
  "plannerEnabled": true,
  "plannerProvider": "openclaw",
  "plannerBaseUrl": "http://127.0.0.1:18789/v1",
  "plannerModel": "openclaw/default",
  "plannerFastModel": "openai/gpt-4.1-mini",
  "plannerReasoningModel": "openai/gpt-5.4",
  "plannerVisionModel": "openai/gpt-4.1",
  "plannerApiKeyEnvironmentVariable": "OPENCLAW_GATEWAY_TOKEN"
}
```

OpenClaw notes:

- `plannerModel` is the OpenClaw agent target, typically `openclaw/default`
- `plannerFastModel`, `plannerReasoningModel`, and `plannerVisionModel` become backend model overrides through `x-openclaw-model`
- OpenClaw's `/v1/chat/completions` surface is disabled by default; enable `gateway.http.endpoints.chatCompletions.enabled: true`
- if the Gateway is loopback-only with `gateway.auth.mode: "none"`, Jarvis does not need a planner API key
- Jarvis now sends a stable OpenAI `user` string for planner sessions so OpenClaw can reuse the same agent session across turns

OpenClaw local runtime example:

```json
{
  "plannerEnabled": true,
  "plannerProvider": "openclaw-local",
  "plannerBaseUrl": "",
  "plannerModel": "",
  "plannerFastModel": "",
  "plannerReasoningModel": "",
  "plannerVisionModel": "",
  "plannerFallbackProviders": [
    {
      "name": "openai-fallback",
      "provider": "openai-compatible",
      "baseUrl": "https://api.openai.com/v1",
      "model": "gpt-4o-mini",
      "fastModel": "gpt-4o-mini",
      "reasoningModel": "gpt-4o-mini",
      "visionModel": "gpt-4o-mini",
      "apiKeyEnvironmentVariable": "OPENAI_API_KEY"
    }
  ]
}
```

OpenClaw local notes:

- Jarvis resolves the backend provider/model from your local OpenClaw config when the planner model fields are blank
- the local bridge does not support image prompts, so keep a fallback provider if you want document or screenshot analysis to keep working
- Jarvis launches the helper with your local Node installation; set `OPENCLAW_NODE_PATH` if `node.exe` is installed somewhere unusual

## Local Speech Recognition (No API Key)

Jarvis can transcribe your voice entirely on this PC with a private Whisper server, so speech recognition needs no account, no key, and no internet after setup. It is far more accurate than the built-in Windows recognizer.

One-time setup (needs Python 3.10+ and about 1 GB of disk; the model is downloaded once):

```powershell
.\scripts\Setup-LocalSpeech.ps1
```

Everything is installed to `%LOCALAPPDATA%\Jarvis\whisper`, deliberately outside the project. If the project sits in a OneDrive folder, sync corrupts large model files and Python environments: it renamed the model file during testing and left the server unable to start. Set `JARVIS_SPEECH_HOME` to use a different folder, and pass the same folder to the setup script.

Then point speech recognition at the local server in `jarvis.settings.json`:

```json
{
  "speechRecognitionProvider": "auto",
  "speechRecognitionBaseUrl": "http://127.0.0.1:8178/v1",
  "speechRecognitionModel": "small.en"
}
```

How it behaves:

- Jarvis starts the server in the background at launch when the base URL is on localhost, and the server exits when Jarvis exits. Nothing else needs to be started.
- The server runs on the CPU. On an 8-core laptop a spoken command takes about 1.5 to 2 seconds to transcribe.
- `small.en` is English-only. For other languages use a multilingual model such as `small`, and for speed use `base.en`. The model name is passed straight to the server.
- The server ignores the long instruction-style prompt Jarvis sends, because Whisper treats prompts as earlier speech and can repeat them back. Set `JARVIS_WHISPER_CLIENT_PROMPT=1` to change that.
- Server output goes to `data\whisper-server.log`. Set `JARVIS_WHISPER_SAVE_DIR` to a folder to keep every audio clip it receives, which helps when diagnosing a recognition problem.
- To use a cloud service instead, set `speechRecognitionBaseUrl` back to `https://api.openai.com/v1` with a key as described below.
- If the server has not been set up or is not running, the voice status says "The local speech server is not running" and Jarvis cannot turn your voice into text. Check `data\whisper-server.log`, run the setup script, or set `speechRecognitionProvider` to `windows` to use the built-in recognizer instead.
- The built-in Windows recognizer now listens to the microphone named in `speechRecognitionDeviceName` with automatic gain, and its status line says why the cloud path was unavailable.

## Streaming Voice Setup

Jarvis now supports four speech-recognition modes through `speechRecognitionProvider`:

- `auto`: prefer realtime transcription on OpenAI-hosted endpoints, otherwise use the rolling OpenAI-compatible fallback, then Windows as a last resort
- `realtime`: require the WebSocket realtime transcription session
- `openai-compatible`: require the OpenAI-compatible cloud path, using realtime when available and the rolling fallback otherwise
- `windows`: force the legacy Windows recognizer

Recommended streaming configuration:

```json
{
  "speechRecognitionEnabled": true,
  "speechRecognitionProvider": "realtime",
  "speechRecognitionBaseUrl": "https://api.openai.com/v1",
  "speechRecognitionModel": "gpt-4o-mini-transcribe",
  "speechRecognitionApiKeyCredentialTarget": "Jarvis/SpeechRecognition/OpenAI",
  "speechRecognitionApiKeyEnvironmentVariable": "OPENAI_API_KEY",
  "speechRecognitionSampleRateHz": 24000,
  "speechRecognitionLanguage": "en",
  "speechRecognitionVoiceActivityThreshold": 0.015,
  "speechRecognitionSilenceDurationMilliseconds": 700,
  "speechRecognitionPreRollMilliseconds": 250,
  "speechRecognitionMinimumUtteranceMilliseconds": 350,
  "speechRecognitionWakeWordAssetPath": "auto",
  "speechRecognitionNoiseSuppressionEnabled": true,
  "speechRecognitionEchoCancellationEnabled": true,
  "speechRecognitionBargeInEnabled": true,
  "speechRecognitionBeamformingEnabled": false,
  "speechRecognitionBeamformingProfile": "auto",
  "speechRecognitionHardwareDspProfile": "auto",
  "speechRecognitionCalibrationProfiles": [],
  "speechRecognitionValidationProfiles": [],
  "speechRecognitionMode": "full-duplex",
  "voiceEnabled": true,
  "voiceProvider": "auto",
  "voiceModel": "gpt-4o-mini-tts",
  "voiceApiKeyCredentialTarget": "Jarvis/Voice/OpenAI",
  "voiceApiKeyEnvironmentVariable": "OPENAI_API_KEY",
  "voiceName": "coral",
  "voiceResponseFormat": "pcm"
}
```

How it works:

- The app continuously captures microphone audio instead of relying only on `System.Speech`
- On OpenAI-hosted endpoints, Jarvis streams 24 kHz PCM to a realtime transcription session over WebSocket
- The server handles VAD and noise-reduction-aware turn detection, then emits ordered transcript completion events back to the app
- On non-realtime OpenAI-compatible endpoints, Jarvis falls back to the rolling WAV-to-`/audio/transcriptions` path instead of dropping all the way to Windows speech
- Streaming recognition paths now run a recognizer-backed wake-word detector against captured PCM before transcript routing, and the detector can auto-select dedicated wake-asset manifests for different microphone patterns
- Voice output streams PCM audio from `/audio/speech` and begins playback before the full response has finished downloading
- In `half-duplex` mode, Jarvis pauses the microphone while speaking to reduce self-transcription and wake-word feedback loops
- In `full-duplex` mode, Jarvis keeps listening during speech output, suppresses likely playback echo before STT, and supports barge-in so user speech can interrupt active TTS
- Jarvis now resolves a per-device microphone profile, applies any saved calibration and validation, and uses that tuning to adjust VAD gates, input gain, capture buffering, echo suppression, and server noise-reduction posture before audio reaches STT
- When the planner is available, transcripts are interpreted against live desktop context instead of only static command phrases

Important notes:

- `speechRecognitionApiKey` can be set directly, but an environment variable is safer
- on Windows, `speechRecognitionApiKeyCredentialTarget` can point to a Generic Credential in Credential Manager so the key does not need to live in `jarvis.settings.json`
- `realtime` expects a provider that supports the Realtime transcription WebSocket session at `/v1/realtime`
- `openai-compatible` still works with endpoints that only implement `/audio/transcriptions`
- `voiceProvider: "auto"` prefers streaming `/audio/speech` and falls back to Windows speech if the remote voice path is not configured
- The recognizer-backed wake-word detector still runs on the realtime and rolling-transcription paths by feeding captured utterances into the installed Windows speech model with wake grammars, but Jarvis can now swap that detector for manifest-backed external wake wrappers when a local Porcupine/OpenWakeWord/ONNX or other helper process is available
- The built-in wake-asset manifests currently cover desktop USB mics, monitor webcams, wired headsets, wireless/Bluetooth headsets, conference speakerphones, and far-field arrays; `auto` picks the closest match from the active device name plus the resolved beamforming/DSP posture
- Use the microphone controls in the right-side voice panel to pick a device, choose a wake asset, choose a wake engine, tune beamforming/hardware/vendor DSP posture, and run calibration or validation for the current room
- `speechRecognitionCalibrationProfiles` stores per-device calibration captures keyed by the effective beamforming, DSP posture, and vendor DSP profile path, so multiple tuned baselines can coexist for one microphone
- `speechRecognitionValidationProfiles` stores per-device wake-word and full-duplex checks keyed by wake asset, wake engine, beamforming, hardware DSP, and vendor DSP posture, including recommended conversation mode, wake readiness, and playback-leakage headroom
- `speechRecognitionWakeWordAssetPath` accepts `auto`, an empty recognizer-default selection, or a JSON wake-asset manifest in `data\wake-word-assets`; manifests can now include per-device keyword matching in addition to alias and sensitivity tuning
- `speechRecognitionWakeEnginePath` accepts `auto`, an empty recognizer-backed detector, or a JSON wake-engine manifest in `data\wake-engines`; those manifests can launch a local helper process and pass the captured WAV path, wake aliases, selected wake asset, and resolved mic-tuning context through placeholders and environment variables
- `speechRecognitionBeamformingProfile` selects the software tuning profile (`auto`, `off`, `desktop`, `headset`, `conference`, or `far-field-array`)
- `speechRecognitionHardwareDspProfile` selects the expected device-stack DSP posture (`auto`, `off`, `system-aec`, `voice-processing`, `conference-array`, or `vendor-beamforming`) so Jarvis can tune capture buffering and server noise-reduction hints more realistically
- `speechRecognitionVendorDspProfilePath` accepts `auto`, an empty no-op vendor selection, or a JSON vendor DSP manifest in `data\vendor-dsp-profiles`; those manifests can optionally apply/release external SDK commands and also contribute extra beamforming/AEC/noise-reduction tuning scales to the live pipeline
- `auto` is still the safest default because it preserves the compatibility and Windows fallbacks when the preferred remote path is unavailable

For a clickable start on Windows:

- Recommended: double-click `Start Jarvis.cmd` from the repo root
- If you launch manually after a Debug build, use `src\Jarvis.App\bin\Debug\net8.0-windows\Jarvis.App.exe`
- If you launch manually after a Release build, use `src\Jarvis.App\bin\Release\net8.0-windows\Jarvis.App.exe`

## Voice Control

By default the desktop app now tries to start the configured speech-recognition provider on launch.

- Say the wake phrase from `jarvis.settings.json` to arm the next command
- Or speak the wake phrase and command in one sentence, for example `Jarvis status`
- Use the `Mute Mic` button to pause recognition
- When `clapShortcutEnabled` is on, a quick double clap brings the running Jarvis window to the front
- When `keepRunningInTrayOnClose` is on, closing the window hides Jarvis to the system tray and it keeps listening in the background until you exit from the tray menu
- When voice output is enabled, Jarvis now gives a short streamed spoken acknowledgment for recognized voice commands while it is minimized or hidden to the tray
- Spoken variants such as `tell me the time`, `show my notes`, `take note`, and `do you remember` now normalize to built-in commands
- Spoken automation phrases such as `launch notepad`, `open my spotify`, `open downloads`, `read file README.md`, `what app am I in`, `show me what's open`, and `search web arc reactor design` also normalize
- User-defined app nicknames in `applicationAliases` also feed launch matching and transcription hints, so aliases like `spot` can open Spotify more reliably
- Bare spoken app or game titles such as `Discord`, `Portal 2`, or `Fortnite` now normalize to `open app <title>` when they look like short launch requests
- Wake-word detection is more forgiving now and can handle common variants such as `hey Jarvis`, `okay Jarvis`, `please Jarvis`, hesitant starts like `uh Jarvis`, and frequent transcription slips like `Jervis`
- When Jarvis asks for a low-confidence voice confirmation, you can correct it with phrases like `I said open Discord, not open this cord`, and Jarvis will store that correction to bias future transcripts
- `voice corrections` shows the learned correction pairs plus recent rejected transcripts
- On the realtime and rolling-transcription providers, wake activation now also has a recognizer-backed local detection path, so `Jarvis status` can still route even when the transcript drops or mangles the wake prefix
- In full-duplex mode, speaking over Jarvis interrupts active voice output and queues the new voice command once the interrupted turn finishes cleanup
- After changing microphones or moving to a noisier room, run `Calibrate Mic` to capture room noise and speech so Jarvis can retune its gate, gain cap, and beamforming assumptions for that device
- Run `Validate Mic` after calibration to capture wake-word strength, command headroom, and playback leakage so Jarvis can flag weak full-duplex setups before you rely on them
- Spoken weather requests such as `what's the weather in Boston` can route to the weather tool, and `what's the weather` can use `defaultWeatherLocation`
- With the planner enabled, natural speech such as `Jarvis, check what files mention transcript storage and open the README` can be interpreted even when it does not match a hardcoded phrase exactly
- In streaming mode, you can still say `Jarvis`, then the next utterance, or say the wake phrase and command together

## Desktop Automation

The desktop layer now supports richer app control beyond launch/read:

**App Discovery & Launch**
- `apps`, `apps spotify`, `open app notepad`, `open app spot`, `launch calculator`

**File Operations**
- `open path downloads`, `list files src`, `read file README.md`, `find files jarvis`

**Browser**
- `browse openai.com`, `search web iron man interface design`
- `chrome dom open openai.com`, `chrome dom tabs`, `chrome dom page`
- `chrome dom click button[data-testid='send-button']`, `chrome dom type textarea :: hello`, `chrome dom verify h1 :: OpenAI`

**App Adapters**
- `spotify play`, `spotify playlist coding`, `code goto src/Jarvis.Core/UserStateProvider.cs:1`
- `chrome incognito openai.com`, `explorer reveal README.md`
- `office word docs\\notes.docx`, `office outlook`, `discord jump jarvis`, `slack jump deploys`

**Window Discovery & Management**
- `windows` or `windows code` - Lists visible top-level windows, optionally filtered
- `focus app notepad`, `close app notepad`, `window state notepad maximize`
- `move window notepad 120 80`, `resize window notepad 1280 900`
- `snap window code left`, `snap window browser top right`

**UI Automation (Win32)**
- `click element notepad` - Clicks center of named window
- `click element notepad :: save` - Clicks a named control inside the target window when it can be resolved
- `type into notepad hello` or `type into notepad :: search :: hello` - Types text into the target window or named field
- `get element text notepad` or `get element text notepad :: status bar` - Gets text from the target window or named control
- `send hotkey notepad ctrl+s` - Sends a keyboard shortcut to the target window
- `scroll notepad down` - Scrolls target window
- `wait 2` - Pauses execution

**Clipboard & Media**
- `clipboard`, `set clipboard hello world`, `clear clipboard`
- `paste clipboard into notepad`
- `media next`, `media pause`, `volume up 3`, `mute`

With the planner enabled, Jarvis can also resolve short UI follow-ups from recent context, including references grounded by `screen ui` such as `click that button` or `type hello into that field`.

**Context & Network**
- `computer`, `network scan`, `system`

**Multi-step Example**
`open app notepad; snap window notepad left; set clipboard Hello Jarvis!; paste clipboard into notepad`

Safety: Sensitive `type into` and `set clipboard` content requires approval. `close app`, `send hotkey`, and `paste clipboard` actions also pause for approval when major-action safety is enabled.

## App Index And Spoken Shortcuts

Jarvis scans every app on the PC and keeps an index of them, so "open Spotify" or "open Clock" works without a hand-written alias.

- **What is scanned:** Start Menu and desktop shortcuts, the registry (installed apps and App Paths), Steam, Epic, Xbox, Riot and Battle.net libraries, and everything Windows itself lists as an app. That last source includes Microsoft Store apps such as Claude, ChatGPT, Clock, Photos and Snipping Tool, which have no shortcut file to find.
- **When:** in the background at every launch, cached for ten minutes in memory, with Windows' own app list refreshed every six hours. If a spoken name matches nothing, Jarvis rescans once, at most every two minutes, in case the app was just installed.
- **The saved copy:** `data\app-index.json` lists every indexed app with its launch target and the spoken names it answers to. Open it to see exactly what Jarvis knows. It is rebuilt automatically and is safe to delete.
- **Spoken shortcuts:** each app answers to its full name and to shorter forms, so "Microsoft Edge" also answers to "edge" and "Microsoft Photos" and "Photos" both work. Filler such as "the", "my" and "app" is ignored.
- **Mishearings:** names that are a letter or two off ("Discourd") or that sound alike ("Clawed" for Claude) still find the right app. Text that matches no app opens nothing.
- **Punctuation:** speech transcripts end sentences with a period, and a name like "Spotify." used to be mistaken for a web address. Trailing punctuation is now ignored.
- **Your own aliases:** `applicationAliases` in `jarvis.settings.json` still works and takes priority over scanned names. It is only needed for nicknames.

Measured on this PC with spoken commands for 197 real apps, the right app opened 93 percent of the time. Most of the remaining misses were odd names that the test voice garbled beyond recognition.

## Visual And Ambient Awareness

Jarvis now has a dedicated perception surface instead of a single screenshot-only command.

- `screen` captures the active display by default, grounds the request with active-window metadata, and asks the vision route to describe what is on screen
- `screen monitors` lists connected displays so you can target `display 2`, `primary`, `active`, or `all displays`
- `screen active window`, `screen capture window notepad`, and `screen ocr app code` let you target the current foreground window or a named app window instead of only full displays
- `screen capture <target>` saves a screenshot to `visualCaptureDirectory` without needing the model route
- `screen ocr <target>` asks the vision route for OCR-style text extraction from the captured display
- `screen ui <target>` asks the vision route for structured UI elements and likely interaction targets
- `analyze document <path>` handles text documents, does embedded-text extraction for PDFs, and uses the vision route for OCR-heavy image documents such as receipts, scans, and TIFF pages
- `ambient` reports whether webcam, room-sensor, face-recognition, spatial-audio, and far-field hooks are configured
- `ambient webcam` analyzes the latest configured webcam frame, or an explicit image path, for room awareness
- `ambient presence` estimates presence, posture, distance, and gestures from the latest configured webcam frame
- `ambient face` is hard-gated behind `ambientFaceRecognitionEnabled`, `ambientFaceRecognitionConsentGranted`, and an enrolled `ambientFaceProfilesPath`
- `ambient room` summarizes the latest configured room-sensor snapshot file

Recommended settings if you want to wire room awareness through external producers:

```json
{
  "visualCaptureDirectory": "data/captures",
  "visualCaptureRetentionCount": 24,
  "ambientContextEnabled": true,
  "ambientWebcamFramePath": "data/sensors/webcam/latest.jpg",
  "ambientWebcamFreshnessSeconds": 90,
  "ambientRoomSensorSnapshotPath": "data/sensors/room-state.json",
  "ambientPresenceDetectionEnabled": true,
  "ambientGestureDetectionEnabled": true,
  "ambientFaceRecognitionEnabled": false,
  "ambientFaceRecognitionConsentGranted": false,
  "ambientFaceProfilesPath": "data/sensors/face-profiles.json",
  "spatialAudioCuesEnabled": false,
  "farFieldListeningEnabled": false
}
```

Notes:

- Jarvis does not open the webcam directly yet; it expects another capture process to keep the latest frame on disk
- `ambient face` compares the current webcam frame against enrolled reference images only after consent is explicitly enabled
- room-sensor snapshots can be JSON or plain text, and Jarvis will summarize them or pass them into ambient presence analysis when available
- spatial-audio and far-field settings are surfaced in capability reporting now so the runtime can be wired into a richer audio stack later without changing the assistant contract

## Safety Guardrails

- `shellExecutionEnabled` can be turned on for stronger agent workflows, but Jarvis only auto-runs read-only shell commands
- state-changing shell commands are paused with an explanation and require you to reply `approve`
- reply `deny` or `cancel` to discard a pending major action
- commands that try to change Windows, `System32`, Program Files, ProgramData, or `HKLM` registry locations are blocked outright

Relevant settings:

- `assistantName`
- `assistantPersona`
- `voiceEnabled`
- `voiceProvider`
- `voiceBaseUrl`
- `voiceModel`
- `voiceApiKeyCredentialTarget`
- `voiceApiKeyEnvironmentVariable`
- `voiceName`
- `voiceInstructions`
- `voiceResponseFormat`
- `speechRecognitionEnabled`
- `speechRecognitionProvider`
- `wakeWordEnabled`
- `wakePhrase`
- `recognizerCulture`
- `speechRecognitionBaseUrl`
- `speechRecognitionModel`
- `speechRecognitionLanguage`
- `speechRecognitionDeviceName`
- `speechRecognitionSampleRateHz`
- `speechRecognitionVoiceActivityThreshold`
- `speechRecognitionSilenceDurationMilliseconds`
- `speechRecognitionPreRollMilliseconds`
- `speechRecognitionMinimumUtteranceMilliseconds`
- `speechRecognitionNoiseSuppressionEnabled`
- `speechRecognitionEchoCancellationEnabled`
- `speechRecognitionBargeInEnabled`
- `speechRecognitionBeamformingEnabled`
- `speechRecognitionBeamformingProfile`
- `speechRecognitionHardwareDspProfile`
- `speechRecognitionCalibrationProfiles`
- `speechRecognitionValidationProfiles`
- `speechRecognitionWakeWordAssetPath`
- `speechRecognitionMode`
- `applicationAliases`
- `speechRecognitionCorrections`
- `speechRecognitionRejectedPhrases`
- `speechRecognitionWakeAliases`
- `speechRecognitionWakeDetectionEnabled`
- `speechRecognitionWakeDetectionSensitivity`
- `clapShortcutEnabled`
- `clapShortcutThreshold`
- `keepRunningInTrayOnClose`
- `voiceCommandTimeoutSeconds`
- `speechRecognitionConfidenceThreshold`
- `visualCaptureDirectory`
- `visualCaptureRetentionCount`
- `ambientContextEnabled`
- `ambientWebcamFramePath`
- `ambientWebcamFreshnessSeconds`
- `ambientRoomSensorSnapshotPath`
- `ambientPresenceDetectionEnabled`
- `ambientGestureDetectionEnabled`
- `ambientFaceRecognitionEnabled`
- `ambientFaceRecognitionConsentGranted`
- `ambientFaceProfilesPath`
- `spatialAudioCuesEnabled`
- `farFieldListeningEnabled`

`assistantPersona` defines the assistant's persistent baseline identity. Jarvis keeps that persona stable across turns and sessions, uses temporary tone changes only for the current reply or task, and then returns to the configured baseline.

`assistantPresencePreset`, `assistantResponseStyle`, `assistantUserNickname`, `assistantSmallTalkEnabled`, and `assistantPronunciationHints` now give first-class presence controls in both `jarvis.settings.json` and the desktop app's Presence workbench.

## Current Limits

This is no longer just a thin bootstrap shell, but it is still not full movie behavior. The biggest remaining gaps are:

1. Native in-process Porcupine/OpenWakeWord/ONNX bindings and direct vendor SDK interop; the current build now supports manifest-backed external wake engines and vendor DSP command profiles, but it does not bundle those SDKs itself
2. Real external sync and write actions for email, tasks, messaging, and other productivity systems instead of only local snapshot reasoning
3. Higher-fidelity PDF rasterization, direct webcam capture, and broader desktop-state understanding beyond the current screenshot-, window-, and file-based analysis
4. Event-driven monitoring, richer sensor backends, broader desktop-state understanding, and wider browser coverage beyond the current Chrome-first DOM path
5. Broader multi-device continuity, richer multilingual voice tuning, and more polished operator HUD layers

## Recommended Next Steps

1. Ship tested Porcupine/OpenWakeWord/ONNX wrapper manifests and vendor SDK command profiles for real hardware instead of only the generic manifest schema and command runner.
2. Expand the current automation layer from Chrome-first DOM workflows and UI primitives into broader cross-browser coverage, event-driven monitoring, and deeper app-state control.
3. Add real external integrations for email, tasks, messaging, calendar, and other systems that currently exist only as local planning context.
4. Harden PDF rasterization, OCR workflows, desktop-surface capture, and direct camera/sensor ingestion beyond the current file- and screenshot-based routes.

## Maximum Realism Roadmap

If the goal is to make this feel as close as possible to a movie-style JARVIS, the work is not one feature. It is a stack of voice, reasoning, memory, automation, presentation, safety, and hardware integration layers that all have to work together with low latency.

The list below is intentionally broad. It is the practical roadmap for pushing this project from a desktop assistant into something that feels substantially more alive, proactive, and cinematic.

### 1. Voice Stack Improvements

Much of this section is now in the current build.

Currently implemented:
- Realtime, OpenAI-compatible, and Windows speech-recognition paths with fallback behavior.
- Streaming TTS with fallback, tray-aware voice responses, and clap/wake-driven voice entry points.
- Manifest-backed wake assets and wake engines, per-device calibration and validation, and beamforming/hardware-DSP/vendor-DSP profile hooks.
- Half-duplex and full-duplex conversation modes with noise suppression, echo cancellation, and barge-in support.

Still open:
- Bundled native Porcupine/OpenWakeWord/ONNX and first-party vendor SDK integrations instead of external-wrapper manifests.
- Better turn detection, speaker identification, pronunciation dictionaries, multilingual tuning, and richer voice/prosody controls.
- More polished quiet-mode, volume-leveling, and room-aware voice behavior across a wider range of hardware.

### 2. Language Understanding And Reasoning

Much of this section is now in the current build.

Currently implemented:
- The planner is now a true tool-using agent runtime instead of a thin natural-language bridge.
- Structured tool calling, multi-step planning, reflection, retries, fallback models, and degraded offline behavior are live.
- Intent classification, provider abstraction, model routing, background task handling, and web research with citations are in place.
- The runtime now uses recent turns, recent tool evidence, user state, memory profile, and live computer context when planning.
- Tool-result verification, verification-aware reflection, proactive summaries, and persisted workflow resume are implemented.
- Ambiguous requests can trigger clarification flows, and those clarification sessions now persist across turns.
- Reasoning and execution activity now stream into the desktop UI as classification, planning, tool, reflection, clarification, and verification events.
- Email- and task-aware planning from the local user-state snapshot is implemented.
- Image, screen, and document understanding are implemented through the visual tool surface.
- Short-term reference resolution is now part of the planner, including safe follow-ups such as `open it`, `the second one`, `the other one`, `click that button`, and `type hello into that field`.

Still open:
- Deeper long-horizon reasoning and richer autonomous monitoring loops beyond the current background-task path.
- Broader multi-turn reference resolution for more abstract phrases such as `move that`, `use the one from earlier`, or cross-task references spanning longer sessions.
- Stronger email and task actions beyond planning from the local snapshot.
- More evaluation, testing, and hardening around edge-case planner behavior, visual grounding, and UI-action ambiguity.

### 3. Memory And Personalization

Much of this section is now in the current build.

Currently implemented:
- Short-term conversation state, transcript logging, transcript export/import, and automatic summary compaction.
- Long-term memory for notes, preferences, habits, routines, app/location cues, and episodic turn outcomes.
- Retention policies, off-the-record/private handling, forgetting controls, and a desktop memory workbench for reviewing and editing stored items.
- Structured memories for people, projects, devices, places, relationships, favorites, and reminder memory with time-based recall.
- User-facing memory review, importance tuning, retention tuning, routines/habits views, diagnostics, maintenance previews, maintenance apply flows, and manual memory merge/disambiguation tools.
- Improved importance scoring, richer routine modeling with cadence/time/anchor cues, stronger entity merge behavior, and broader cross-session personalization signals for planner presence guidance.
- Heuristic memory-quality evaluation for duplicates, low-signal notes, stale episodic memory, retention mismatches, false-positive candidates, and long-horizon recall coverage.

### 4. Desktop And App Automation

A large portion of this section is now in the current build.

Currently implemented:
- App discovery and launch, window discovery, focus, close, minimize/maximize/restore, move, resize, snap, and workspace-aware computer context.
- Win32 UI actions for click, type, hotkey, scroll, wait, clipboard workflows, and media controls with approval guardrails.
- Screenshot capture, OCR-style extraction, structured UI detection, and planner grounding from live screen context.
- Browser open/search actions plus deeper Chrome navigation, tab, history/downloads/bookmarks, find-on-page, form-fill, and login workflows.
- DevTools-backed Chrome DOM automation for tab inspection and selection, selector-aware reads, clicks, typing, waits, and explicit verification on real web pages.
- File navigation plus file create/edit/append/move/copy/delete workflows, clipboard history and restore, Jarvis notification history with best-effort toast delivery, and shell execution with approvals.
- Richer app-state introspection, repeatable multi-app macros with optional rollback steps, and app adapters for Spotify, Discord, Slack, VS Code, Office, Chrome, and Explorer.

### 5. External Integrations

Foundations exist here, but the real integrations are still thin.

Currently implemented:
- Reminders are first-class through the memory system and planner context.
- Local user-state snapshots for email and tasks are available so the planner can reason over inbox and task context.
- Spotify-specific routing and deep-link style app control are in place through the desktop adapter layer.

Still open:
- Real email and task sync plus write actions such as drafting, triage, sending, and task mutation.
- Contacts, messaging, phone notification bridging, and note-taking or knowledge-base integrations.
- Smart-home, maps/traffic, finance, home-server, camera, and broader media-service integrations.

### 6. Proactive Behavior

This area is only partially implemented today.

Currently implemented:
- Persisted agent sessions and clarification workflows can resume across turns.
- Reminder memory, conversation summaries, and recent tool evidence give the runtime continuity across sessions.

Still open:
- Scheduled routines, event triggers, reminder delivery flows, summaries that push themselves proactively, and app-watch behaviors.
- Anomaly detection, context-aware suggestions, and broader background monitoring loops.

### 7. Visual Interface And HUD

Substantial groundwork for this section is already in the current build.

Currently implemented:
- A live desktop control surface with response streaming, conversation feed, tray mode, approval card, and planner activity updates.
- Explicit listening, thinking, acting, and speaking state transitions in the UI.
- A right-side voice panel with waveform, mic controls, wake assets, and DSP tuning.
- A desktop memory workbench plus built-in approval handling directly in the UI instead of only raw text prompts.

Still open:
- Overlay or transparent HUD modes, richer operator timelines, screen highlights, second-screen layouts, and more cinematic motion/audio polish.
- A tighter compact voice-only mode and broader presentation options beyond the current operator-console style surface.

### 8. Presence And Personality

Currently implemented:
- A configurable `assistantPersona` plus `assistantPresencePreset` keeps Jarvis's baseline identity stable across turns and sessions.
- First-class presence controls now exist for response style, nickname/address, small-talk posture, and pronunciation hints in both settings and the desktop app's Presence workbench.
- Social turns such as greetings, check-ins, thanks, apologies, compliments, readiness checks, and identity questions now have direct presence-aware replies.
- Short task replies and acknowledgements now apply stronger urgency/frustration modulation without corrupting tool-heavy outputs.
- Streamed spoken acknowledgements and configurable voice provider/model/name/instructions are live, and presence settings now feed both planner prompts and voice instructions.
- Tone continuity is preserved through persisted settings plus learned preference memory.

Still open:
- Broader multi-device continuity and shared account-level presence sync across machines.
- Deeper provider-specific multilingual speech tuning and prosody parity, especially on non-streaming fallback voices.

### 9. Safety, Permissions, And Trust

Substantial guardrails are already in place.

Currently implemented:
- Shell execution is opt-in, protected system paths are hard-blocked, and major shell changes require approval.
- Sensitive memory content is blocked from automatic persistence, and off-the-record/private turns are supported.
- Selected UI actions such as closing apps, hotkeys, clipboard paste, and sensitive typing/text entry are approval-gated.
- API keys can be resolved through environment variables or Windows Credential Manager targets instead of only plaintext settings.

Still open:
- Richer approval tiers, broader previews for risky actions, stronger reversible-action support, and more granular per-tool policy rules.
- More explicit allowlists/denylists, rate limiting, and broader secrets-management or trust-policy hardening.

### 10. Reliability, Performance, And Ops

Some of the foundations are already in place.

Currently implemented:
- Planner and speech stacks have fallback behavior, degraded modes, and startup readiness checks for local planner setups.
- Memory, transcripts, pending approvals, and agent sessions persist locally so work can survive restarts.
- The runtime already exposes a clear status surface for planner availability, speech mode, and desktop capability reporting.

Still open:
- Better telemetry, structured diagnostics, benchmark tooling, simulation/replay, and broader automated test coverage.
- Richer health checks, persistent background job infrastructure, and stronger performance tuning across the full stack.

### 11. Architecture Improvements

A meaningful portion of this section already exists.

Currently implemented:
- The runtime is already split into speech, planning, tools, memory, visual context, desktop automation, user state, and UI layers.
- There is a structured tool surface through `IAssistantTool`, planner tool metadata, and verification-aware tool execution.
- Provider and service interfaces already exist for planner, weather, web research, visual context, desktop automation, transcripts, memory, and user state.
- Persisted transcript and agent-session objects already capture planner/tool context beyond a plain chat log.

Still open:
- Plugin loading, background agent workers, event-bus style state propagation, and more formal schema/permission metadata.
- Richer configuration profiles and a more explicit extension model for installing new capabilities without core edits.

### 12. Screen, Vision, And Spatial Awareness

Much of this section is already underway.

Currently implemented:
- Screenshot capture, monitor-aware targeting, OCR-style extraction, structured UI detection, and screen-grounded planner workflows.
- Document analysis for text, PDF, and image-based files through the visual tool surface.
- Webcam frame-drop and room-sensor hooks for ambient context, plus consent-gated face-recognition and presence-analysis paths.
- Multi-monitor awareness and explicit screen/window targeting.

Still open:
- Direct camera capture, higher-fidelity OCR/UI accuracy, stronger posture-distance-presence detection, and broader sensor ingestion.
- Real spatial audio or directional playback plus richer room and far-field awareness beyond the current config-driven hooks.

### 13. “Movie Feel” Features

Some of this feel is already present in the current build.

Currently implemented:
- Streamed replies, planner activity, and spoken acknowledgements keep long tasks from feeling silent.
- The runtime already uses active app, desktop context, time, reminders, and recent work as grounding for responses.
- Direct commands and conversational planning already coexist instead of forcing a single rigid interaction mode.

Still open:
- Faster partials, stronger proactive behavior, and more polished response cadence across voice, UI, and automation.
- More environment-aware phrasing and tighter timing so the assistant feels consistently intentional rather than just capable.

### 14. Best Order To Build It

From the current baseline, the highest-value order is:

1. Replay and harden the calibrated full-duplex voice stack across more microphones and rooms, then replace generic external wake and vendor DSP wrappers with tested first-party integrations where possible.
2. Deepen planner evaluation, long-horizon workflow handling, and broader multi-turn grounding beyond the current structured tool runtime.
3. Expand app and browser automation from the current UI/tool surface into deeper app-state awareness and repeatable multi-step workflows.
4. Add real external integrations for email, tasks, messaging, calendar, and other systems that currently exist only as local context.
5. Keep refining the HUD, approvals, and proactive routines so the system feels more continuous and operator-friendly.
6. Expand sensors, camera awareness, smart-home integrations, and broader environment monitoring.

### 15. Honest Constraint

The movie effect comes from three things at once:

- low-latency voice conversation
- strong reasoning and tool use
- broad real-world integration

This repository now has the beginnings of that stack, but getting to something that truly feels like film JARVIS will require more than prompt engineering. It needs a stronger runtime, better models, better audio, deeper automation, and eventually hardware-aware context.
