using System.Drawing;
using System.Windows.Forms;
using Jarvis.Core;

namespace Jarvis.App;

internal sealed class MemoryWorkbenchForm : Form
{
    private readonly JarvisAssistant _assistant;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly BindingSource _gridBinding = new();

    private List<MemoryNote> _memories = [];
    private bool _isLoadingSelection;

    private DataGridView _grid = null!;
    private Label _statusLabel = null!;
    private Button _refreshButton = null!;
    private Button _saveButton = null!;
    private Button _forgetButton = null!;
    private Label _idLabel = null!;
    private Label _createdLabel = null!;
    private Label _entitiesLabel = null!;
    private ComboBox _kindComboBox = null!;
    private ComboBox _retentionComboBox = null!;
    private ComboBox _privacyComboBox = null!;
    private TextBox _contentBox = null!;
    private TextBox _categoryBox = null!;
    private TextBox _tagsBox = null!;
    private TextBox _reminderTextBox = null!;
    private NumericUpDown _importanceUpDown = null!;
    private DateTimePicker _expiryPicker = null!;
    private DateTimePicker _reminderPicker = null!;
    private CheckBox _userApprovedCheckBox = null!;

    public MemoryWorkbenchForm(JarvisAssistant assistant)
    {
        _assistant = assistant;
        InitializeUi();
        Shown += async (_, _) => await RefreshMemoriesAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cancellationTokenSource.Cancel();
            _cancellationTokenSource.Dispose();
            _gridBinding.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeUi()
    {
        Text = "Memory Workbench";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1040, 700);
        Size = new Size(1180, 780);
        BackColor = Color.FromArgb(11, 18, 28);
        ForeColor = Color.FromArgb(236, 243, 252);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        _refreshButton = CreateButton("Refresh");
        _saveButton = CreateButton("Save");
        _forgetButton = CreateButton("Forget");
        _statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(176, 196, 216),
            Font = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(16, 10, 0, 0),
            Text = "Loading memories..."
        };

        _refreshButton.Click += async (_, _) => await RefreshMemoriesAsync();
        _saveButton.Click += async (_, _) => await SaveSelectedMemoryAsync();
        _forgetButton.Click += async (_, _) => await ForgetSelectedMemoryAsync();

        toolbar.Controls.Add(_refreshButton);
        toolbar.Controls.Add(_saveButton);
        toolbar.Controls.Add(_forgetButton);
        toolbar.Controls.Add(_statusLabel);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            SplitterDistance = 560,
            BackColor = Color.FromArgb(11, 18, 28)
        };

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = Color.FromArgb(13, 21, 33),
            BorderStyle = BorderStyle.None,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AutoGenerateColumns = false,
            ReadOnly = true,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            RowHeadersVisible = false,
            DataSource = _gridBinding,
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(13, 21, 33),
                ForeColor = Color.FromArgb(236, 243, 252),
                SelectionBackColor = Color.FromArgb(28, 52, 78),
                SelectionForeColor = Color.FromArgb(248, 251, 255)
            },
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(18, 30, 46),
                ForeColor = Color.FromArgb(224, 234, 246),
                SelectionBackColor = Color.FromArgb(18, 30, 46),
                SelectionForeColor = Color.FromArgb(224, 234, 246)
            },
            EnableHeadersVisualStyles = false
        };
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MemoryRow.Id), HeaderText = "Id", Width = 74 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MemoryRow.Kind), HeaderText = "Kind", Width = 84 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MemoryRow.Category), HeaderText = "Category", Width = 92 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MemoryRow.Due), HeaderText = "Due", Width = 120 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MemoryRow.Retention), HeaderText = "Retention", Width = 96 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = nameof(MemoryRow.Content), HeaderText = "Content", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.SelectionChanged += (_, _) => OnGridSelectionChanged();

        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(BuildEditorPanel());

        root.Controls.Add(toolbar, 0, 0);
        root.Controls.Add(split, 0, 1);
        Controls.Add(root);
    }

    private Control BuildEditorPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(14, 22, 34),
            Padding = new Padding(14)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 11
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 124));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        for (var index = 0; index < 11; index++)
        {
            layout.RowStyles.Add(new RowStyle(index == 2 ? SizeType.Percent : SizeType.Absolute, index == 2 ? 100 : 44));
        }

        _idLabel = CreateValueLabel();
        _createdLabel = CreateValueLabel();
        _entitiesLabel = CreateValueLabel();
        _contentBox = CreateTextBox(multiline: true);
        _categoryBox = CreateTextBox();
        _tagsBox = CreateTextBox();
        _reminderTextBox = CreateTextBox();
        _kindComboBox = CreateComboBox(["note", "preference", "entity", "relationship", "episodic", "summary", "reminder"]);
        _retentionComboBox = CreateComboBox(["standard", "short", "episodic", "rolling", "long", "forever", "until-complete"]);
        _privacyComboBox = CreateComboBox(["persistent", "private"]);
        _importanceUpDown = new NumericUpDown
        {
            Dock = DockStyle.Left,
            Width = 140,
            DecimalPlaces = 0,
            Minimum = 5,
            Maximum = 100,
            Increment = 5,
            BackColor = Color.FromArgb(10, 18, 30),
            ForeColor = Color.FromArgb(236, 243, 252)
        };
        _expiryPicker = CreateDateTimePicker();
        _reminderPicker = CreateDateTimePicker();
        _userApprovedCheckBox = new CheckBox
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(236, 243, 252),
            Text = "User-approved retention"
        };

        AddEditorRow(layout, 0, "Memory Id", _idLabel);
        AddEditorRow(layout, 1, "Created", _createdLabel);
        AddEditorRow(layout, 2, "Content", _contentBox);
        AddEditorRow(layout, 3, "Kind", _kindComboBox);
        AddEditorRow(layout, 4, "Category", _categoryBox);
        AddEditorRow(layout, 5, "Tags", _tagsBox);
        AddEditorRow(layout, 6, "Importance", _importanceUpDown);
        AddEditorRow(layout, 7, "Retention", _retentionComboBox);
        AddEditorRow(layout, 8, "Expires", _expiryPicker);
        AddEditorRow(layout, 9, "Reminder", BuildReminderPanel());
        AddEditorRow(layout, 10, "Entities", _entitiesLabel);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildReminderPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

        panel.Controls.Add(_reminderTextBox, 0, 0);
        panel.Controls.Add(_reminderPicker, 0, 1);
        panel.Controls.Add(_privacyComboBox, 0, 2);
        panel.Controls.Add(_userApprovedCheckBox, 0, 3);
        return panel;
    }

    private async Task RefreshMemoriesAsync()
    {
        SetBusy(true);

        try
        {
            _memories = (await _assistant.GetMemoriesAsync(_cancellationTokenSource.Token)).ToList();
            _gridBinding.DataSource = _memories.Select(note => new MemoryRow(
                ShortId(note.Id),
                note.Id,
                note.Kind,
                note.Category,
                string.IsNullOrWhiteSpace(note.ReminderText) ? note.Content : note.ReminderText,
                note.ReminderAtUtc?.ToLocalTime().ToString("MM-dd h:mm tt") ?? string.Empty,
                note.RetentionPolicy)).ToList();
            _statusLabel.Text = $"Loaded {_memories.Count} active memory item(s).";

            if (_grid.Rows.Count > 0)
            {
                _grid.Rows[0].Selected = true;
                OnGridSelectionChanged();
            }
            else
            {
                PopulateEditor(null);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "Memory refresh failed.";
            MessageBox.Show(this, exception.Message, "Memory Refresh Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void OnGridSelectionChanged()
    {
        if (_isLoadingSelection || _grid.CurrentRow?.DataBoundItem is not MemoryRow row)
        {
            return;
        }

        var note = _memories.FirstOrDefault(candidate => string.Equals(candidate.Id, row.FullId, StringComparison.OrdinalIgnoreCase));
        PopulateEditor(note);
    }

    private void PopulateEditor(MemoryNote? note)
    {
        _isLoadingSelection = true;

        try
        {
            if (note is null)
            {
                _idLabel.Text = "No selection";
                _createdLabel.Text = string.Empty;
                _entitiesLabel.Text = string.Empty;
                _contentBox.Text = string.Empty;
                _categoryBox.Text = string.Empty;
                _tagsBox.Text = string.Empty;
                _reminderTextBox.Text = string.Empty;
                _kindComboBox.SelectedIndex = 0;
                _retentionComboBox.SelectedIndex = 0;
                _privacyComboBox.SelectedIndex = 0;
                _importanceUpDown.Value = 35;
                _expiryPicker.Checked = false;
                _reminderPicker.Checked = false;
                _userApprovedCheckBox.Checked = false;
                return;
            }

            _idLabel.Text = note.Id;
            _createdLabel.Text = note.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd h:mm tt");
            _entitiesLabel.Text = note.Entities is { Length: > 0 }
                ? string.Join(" | ", note.Entities.Select(entity => entity.ToInlineText()))
                : "None";
            _contentBox.Text = note.Content;
            _categoryBox.Text = note.Category;
            _tagsBox.Text = string.Join(", ", note.Tags ?? Array.Empty<string>());
            _reminderTextBox.Text = note.ReminderText;
            SelectComboValue(_kindComboBox, note.Kind, "note");
            SelectComboValue(_retentionComboBox, note.RetentionPolicy, "standard");
            SelectComboValue(_privacyComboBox, note.Privacy, "persistent");
            _importanceUpDown.Value = Math.Clamp((decimal)Math.Round(note.ImportanceScore * 100d), 5, 100);
            _expiryPicker.Checked = note.ExpiresAtUtc is not null;
            _expiryPicker.Value = (note.ExpiresAtUtc ?? DateTimeOffset.Now).ToLocalTime().DateTime;
            _reminderPicker.Checked = note.ReminderAtUtc is not null;
            _reminderPicker.Value = (note.ReminderAtUtc ?? DateTimeOffset.Now).ToLocalTime().DateTime;
            _userApprovedCheckBox.Checked = note.UserApprovedRetention;
        }
        finally
        {
            _isLoadingSelection = false;
        }
    }

    private async Task SaveSelectedMemoryAsync()
    {
        if (string.IsNullOrWhiteSpace(_idLabel.Text) || _idLabel.Text == "No selection")
        {
            return;
        }

        SetBusy(true);

        try
        {
            var request = new MemoryUpdateRequest(
                _idLabel.Text,
                _contentBox.Text.Trim(),
                _kindComboBox.Text,
                _categoryBox.Text.Trim(),
                _privacyComboBox.Text,
                ParseTags(_tagsBox.Text),
                (double)_importanceUpDown.Value / 100d,
                _retentionComboBox.Text,
                _expiryPicker.Checked ? new DateTimeOffset(_expiryPicker.Value) : null,
                _userApprovedCheckBox.Checked,
                _reminderPicker.Checked ? new DateTimeOffset(_reminderPicker.Value) : null,
                _reminderTextBox.Text.Trim(),
                _reminderPicker.Checked ? "pending" : string.Empty);

            var updated = await _assistant.UpdateMemoryAsync(request, _cancellationTokenSource.Token);

            if (updated is null)
            {
                _statusLabel.Text = "Selected memory no longer exists.";
                return;
            }

            _statusLabel.Text = $"Saved {ShortId(updated.Id)}.";
            await RefreshMemoriesAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Memory Save Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task ForgetSelectedMemoryAsync()
    {
        if (string.IsNullOrWhiteSpace(_idLabel.Text) || _idLabel.Text == "No selection")
        {
            return;
        }

        var confirmed = MessageBox.Show(
            this,
            "Forget the selected memory from active recall?",
            "Forget Memory",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);

        if (confirmed != DialogResult.Yes)
        {
            return;
        }

        SetBusy(true);

        try
        {
            var forgot = await _assistant.ForgetMemoryAsync(_idLabel.Text, "ui request", _cancellationTokenSource.Token);
            _statusLabel.Text = forgot ? $"Forgot {_idLabel.Text[..8]}." : "Forget request failed.";
            await RefreshMemoriesAsync();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            MessageBox.Show(this, exception.Message, "Memory Forget Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool isBusy)
    {
        _refreshButton.Enabled = !isBusy;
        _saveButton.Enabled = !isBusy;
        _forgetButton.Enabled = !isBusy;
        Cursor = isBusy ? Cursors.WaitCursor : Cursors.Default;
    }

    private static void AddEditorRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        var header = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(184, 204, 224),
            Font = new Font("Bahnschrift SemiBold", 10f, FontStyle.Bold, GraphicsUnit.Point),
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft
        };

        control.Dock = DockStyle.Fill;
        layout.Controls.Add(header, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private static Button CreateButton(string text)
    {
        return new Button
        {
            AutoSize = true,
            Height = 32,
            Margin = new Padding(0, 6, 8, 0),
            Padding = new Padding(14, 0, 14, 0),
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(20, 34, 50),
            ForeColor = Color.FromArgb(236, 243, 252)
        };
    }

    private static Label CreateValueLabel()
    {
        return new Label
        {
            AutoEllipsis = true,
            ForeColor = Color.FromArgb(236, 243, 252),
            Font = new Font("Consolas", 10f, FontStyle.Regular, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static TextBox CreateTextBox(bool multiline = false)
    {
        return new TextBox
        {
            Multiline = multiline,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Color.FromArgb(10, 18, 30),
            ForeColor = Color.FromArgb(236, 243, 252)
        };
    }

    private static ComboBox CreateComboBox(IEnumerable<string> values)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(10, 18, 30),
            ForeColor = Color.FromArgb(236, 243, 252)
        };

        foreach (var value in values)
        {
            combo.Items.Add(value);
        }

        combo.SelectedIndex = 0;
        return combo;
    }

    private static DateTimePicker CreateDateTimePicker()
    {
        return new DateTimePicker
        {
            Format = DateTimePickerFormat.Custom,
            CustomFormat = "yyyy-MM-dd h:mm tt",
            ShowCheckBox = true,
            CalendarMonthBackground = Color.FromArgb(10, 18, 30)
        };
    }

    private static string[] ParseTags(string value)
    {
        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void SelectComboValue(ComboBox comboBox, string? value, string fallback)
    {
        var target = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var index = comboBox.Items.Cast<object>()
            .Select((item, idx) => new { Value = item.ToString() ?? string.Empty, Index = idx })
            .FirstOrDefault(item => string.Equals(item.Value, target, StringComparison.OrdinalIgnoreCase))
            ?.Index ?? 0;
        comboBox.SelectedIndex = index;
    }

    private static string ShortId(string id)
    {
        return string.IsNullOrWhiteSpace(id) ? "n/a" : id.Length <= 8 ? id : id[..8];
    }

    private sealed record MemoryRow(
        string Id,
        string FullId,
        string Kind,
        string Category,
        string Content,
        string Due,
        string Retention);
}
