using System.Drawing;
using System.Windows.Forms;
using Jarvis.Core;

namespace Jarvis.App;

internal sealed class PresenceWorkbenchForm : Form
{
    private readonly JarvisOptions _initialOptions;

    private ComboBox _presencePresetComboBox = null!;
    private ComboBox _responseStyleComboBox = null!;
    private TextBox _nicknameBox = null!;
    private CheckBox _smallTalkCheckBox = null!;
    private TextBox _pronunciationBox = null!;
    private TextBox _personaBox = null!;
    private RichTextBox _previewBox = null!;

    public PresenceWorkbenchForm(JarvisOptions options)
    {
        _initialOptions = options;
        SelectedOptions = options;
        InitializeUi();
        RefreshPreview();
    }

    public JarvisOptions SelectedOptions { get; private set; }

    private void InitializeUi()
    {
        Text = "Presence Workbench";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(860, 620);
        Size = new Size(980, 700);
        BackColor = Color.FromArgb(11, 18, 28);
        ForeColor = Color.FromArgb(236, 243, 252);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(14)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54f));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56f));

        root.Controls.Add(BuildEditorPanel(), 0, 0);
        root.Controls.Add(BuildPreviewPanel(), 1, 0);
        root.Controls.Add(BuildButtonBar(), 0, 1);
        root.SetColumnSpan(root.GetControlFromPosition(0, 1)!, 2);

        Controls.Add(root);
    }

    private Control BuildEditorPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(14, 22, 34),
            Padding = new Padding(16)
        };

        var title = CreateHeaderLabel("Presence, Style, And Voice");
        title.Dock = DockStyle.Top;

        var intro = CreateBodyLabel("Set the persistent presence preset, reply style, nickname, and pronunciation hints that should survive restarts and shape live replies, acknowledgements, and voice output.");
        intro.Dock = DockStyle.Top;
        intro.Height = 66;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 6,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _presencePresetComboBox = CreateComboBox(["baseline", "operator", "warm", "cinematic"]);
        SelectComboValue(_presencePresetComboBox, AssistantPersonaConfiguration.ResolvePresencePreset(_initialOptions), "baseline");

        _responseStyleComboBox = CreateComboBox(["adaptive", "concise", "normal", "detailed", "cinematic"]);
        SelectComboValue(_responseStyleComboBox, AssistantPersonaConfiguration.ResolveResponseStyle(_initialOptions), "adaptive");

        _nicknameBox = CreateTextBox();
        _nicknameBox.Text = _initialOptions.AssistantUserNickname ?? string.Empty;

        _smallTalkCheckBox = new CheckBox
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(236, 243, 252),
            Text = "Allow brief social turns",
            Checked = _initialOptions.AssistantSmallTalkEnabled,
            Margin = new Padding(0, 8, 0, 0)
        };

        _pronunciationBox = CreateTextBox(multiline: true);
        _pronunciationBox.Text = string.Join(Environment.NewLine, AssistantPersonaConfiguration.ResolvePronunciationHints(_initialOptions));

        _personaBox = CreateTextBox(multiline: true);
        _personaBox.Text = _initialOptions.AssistantPersona ?? string.Empty;

        AddRow(layout, 0, "Preset", _presencePresetComboBox);
        AddRow(layout, 1, "Style", _responseStyleComboBox);
        AddRow(layout, 2, "Nickname", _nicknameBox);
        AddRow(layout, 3, "Small talk", _smallTalkCheckBox);
        AddRow(layout, 4, "Pronunciation", _pronunciationBox);
        AddRow(layout, 5, "Persona", _personaBox);

        _presencePresetComboBox.SelectedIndexChanged += (_, _) => RefreshPreview();
        _responseStyleComboBox.SelectedIndexChanged += (_, _) => RefreshPreview();
        _nicknameBox.TextChanged += (_, _) => RefreshPreview();
        _smallTalkCheckBox.CheckedChanged += (_, _) => RefreshPreview();
        _pronunciationBox.TextChanged += (_, _) => RefreshPreview();
        _personaBox.TextChanged += (_, _) => RefreshPreview();

        panel.Controls.Add(layout);
        panel.Controls.Add(intro);
        panel.Controls.Add(title);
        title.BringToFront();
        intro.BringToFront();
        return panel;
    }

    private Control BuildPreviewPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(14, 22, 34),
            Padding = new Padding(16)
        };

        var title = CreateHeaderLabel("Live Preview");
        title.Dock = DockStyle.Top;

        _previewBox = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(10, 18, 30),
            ForeColor = Color.FromArgb(236, 243, 252),
            Font = new Font("Consolas", 10.1f, FontStyle.Regular, GraphicsUnit.Point)
        };

        panel.Controls.Add(_previewBox);
        panel.Controls.Add(title);
        title.BringToFront();
        return panel;
    }

    private Control BuildButtonBar()
    {
        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };

        var saveButton = CreateButton("Apply", accent: true);
        var cancelButton = CreateButton("Cancel");
        var resetButton = CreateButton("Reset");

        saveButton.Click += (_, _) =>
        {
            SelectedOptions = BuildOptions();
            DialogResult = DialogResult.OK;
            Close();
        };
        cancelButton.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
        resetButton.Click += (_, _) =>
        {
            _presencePresetComboBox.SelectedItem = "baseline";
            _responseStyleComboBox.SelectedItem = "adaptive";
            _nicknameBox.Clear();
            _smallTalkCheckBox.Checked = true;
            _pronunciationBox.Clear();
            _personaBox.Clear();
            RefreshPreview();
        };

        bar.Controls.Add(saveButton);
        bar.Controls.Add(cancelButton);
        bar.Controls.Add(resetButton);
        return bar;
    }

    private void RefreshPreview()
    {
        var options = BuildOptions();
        var preferredAddress = options.AssistantUserNickname?.Trim() ?? string.Empty;
        var greeting = AssistantPresenceStyle.TryBuildSocialReply("hello", options, preferredAddress, out var socialReply)
            ? socialReply
            : "Greeting route unavailable.";
        var identityReply = AssistantPresenceStyle.TryBuildSocialReply("who are you", options, preferredAddress, out var whoReply)
            ? whoReply
            : "Identity route unavailable.";
        var acknowledgement = AssistantPresenceStyle.TryBuildAcknowledgement("this is urgent and broken", options, out var ack)
            ? ack
            : "On it.";
        var taskReply = AssistantPresenceStyle.ApplyResponsePresence(
            "this is urgent and broken",
            "I reopened the app and restored focus to the workspace.",
            options,
            AgentIntentKind.DirectTool,
            false,
            preferredAddress);

        _previewBox.Text = string.Join(
            Environment.NewLine,
            $"Preset: {AssistantPersonaConfiguration.ResolvePresencePreset(options)}",
            $"Style: {AssistantPersonaConfiguration.ResolveResponseStyle(options)}",
            $"Nickname: {(string.IsNullOrWhiteSpace(preferredAddress) ? "none" : preferredAddress)}",
            $"Small talk: {(options.AssistantSmallTalkEnabled ? "briefly enabled" : "minimal by default")}",
            $"Pronunciation hints: {AssistantPersonaConfiguration.ResolvePronunciationHints(options).Count}",
            string.Empty,
            "Presence guidance:",
            AssistantPersonaConfiguration.BuildPresenceGuidance(options),
            string.Empty,
            "Preview greeting:",
            greeting,
            string.Empty,
            "Preview identity reply:",
            identityReply,
            string.Empty,
            "Preview acknowledgement:",
            acknowledgement,
            string.Empty,
            "Preview task reply:",
            taskReply);
    }

    private JarvisOptions BuildOptions()
    {
        var selectedPreset = _presencePresetComboBox.SelectedItem as string ?? "baseline";
        var selectedStyle = _responseStyleComboBox.SelectedItem as string ?? "adaptive";
        var pronunciationHints = _pronunciationBox.Text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return _initialOptions with
        {
            AssistantPresencePreset = selectedPreset.Trim(),
            AssistantResponseStyle = selectedStyle.Trim(),
            AssistantUserNickname = _nicknameBox.Text.Trim(),
            AssistantSmallTalkEnabled = _smallTalkCheckBox.Checked,
            AssistantPronunciationHints = pronunciationHints,
            AssistantPersona = _personaBox.Text.Trim()
        };
    }

    private static void AddRow(TableLayoutPanel layout, int rowIndex, string labelText, Control control)
    {
        var label = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(186, 204, 222),
            Font = new Font("Bahnschrift SemiBold", 10.5f, FontStyle.Bold, GraphicsUnit.Point),
            Margin = new Padding(0, 10, 0, 0),
            Text = labelText
        };

        control.Dock = DockStyle.Fill;
        layout.Controls.Add(label, 0, rowIndex);
        layout.Controls.Add(control, 1, rowIndex);
    }

    private static TextBox CreateTextBox(bool multiline = false)
    {
        return new TextBox
        {
            Multiline = multiline,
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(10, 18, 30),
            ForeColor = Color.FromArgb(236, 243, 252),
            Font = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Point),
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None
        };
    }

    private static ComboBox CreateComboBox(IReadOnlyList<string> items)
    {
        var comboBox = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(10, 18, 30),
            ForeColor = Color.FromArgb(236, 243, 252),
            Font = new Font("Bahnschrift", 10f, FontStyle.Regular, GraphicsUnit.Point)
        };

        comboBox.Items.AddRange(items.Cast<object>().ToArray());
        return comboBox;
    }

    private static void SelectComboValue(ComboBox comboBox, string value, string fallback)
    {
        var target = comboBox.Items
            .Cast<object>()
            .Select(item => item?.ToString() ?? string.Empty)
            .FirstOrDefault(item => string.Equals(item, value, StringComparison.OrdinalIgnoreCase));

        comboBox.SelectedItem = target ?? fallback;
    }

    private static Button CreateButton(string text, bool accent = false)
    {
        var button = new Button
        {
            AutoSize = true,
            Height = 34,
            Margin = new Padding(8, 8, 0, 0),
            Padding = new Padding(16, 0, 16, 0),
            FlatStyle = FlatStyle.Flat,
            Text = text,
            Cursor = Cursors.Hand,
            BackColor = accent ? Color.FromArgb(106, 220, 197) : Color.FromArgb(16, 34, 52),
            ForeColor = accent ? Color.FromArgb(8, 15, 24) : Color.FromArgb(226, 236, 248),
            Font = new Font("Bahnschrift SemiBold", 10.5f, FontStyle.Bold, GraphicsUnit.Point)
        };

        button.FlatAppearance.BorderSize = accent ? 0 : 1;
        button.FlatAppearance.BorderColor = Color.FromArgb(34, 88, 118);
        return button;
    }

    private static Label CreateHeaderLabel(string text)
    {
        return new Label
        {
            Height = 28,
            ForeColor = Color.FromArgb(214, 229, 242),
            Font = new Font("Bahnschrift SemiBold", 12f, FontStyle.Bold, GraphicsUnit.Point),
            Text = text
        };
    }

    private static Label CreateBodyLabel(string text)
    {
        return new Label
        {
            ForeColor = Color.FromArgb(166, 188, 210),
            Font = new Font("Consolas", 9.8f, FontStyle.Regular, GraphicsUnit.Point),
            Text = text
        };
    }
}
