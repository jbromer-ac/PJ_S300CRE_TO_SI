namespace S300CRE_to_SI.GUI;

internal class TeardownConfirmDialog : Form
{
    public TeardownConfirmDialog(string databaseName)
    {
        Text = "Confirm Teardown";
        Size = new Size(440, 200);
        MinimumSize = Size;
        MaximumSize = Size;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9.5f);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(16),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        layout.Controls.Add(new Label
        {
            Text = $"This will drop all MAP schema objects from [{databaseName}].\n" +
                   "Type the database name below to confirm:",
            AutoSize = true,
            Padding = new Padding(0, 0, 0, 8),
        });

        var input = new TextBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        layout.Controls.Add(input);

        var btnRow = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
        };

        var cancelBtn = new Button
        {
            Text = "Cancel",
            AutoSize = true,
            Margin = new Padding(6, 0, 0, 0),
            DialogResult = DialogResult.Cancel,
        };

        var confirmBtn = new Button
        {
            Text = "Teardown",
            AutoSize = true,
            BackColor = Color.FromArgb(196, 43, 28),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat,
        };
        confirmBtn.FlatAppearance.BorderSize = 0;
        confirmBtn.Click += (_, _) =>
        {
            if (string.Equals(input.Text.Trim(), databaseName, StringComparison.OrdinalIgnoreCase))
                DialogResult = DialogResult.OK;
            else
                MessageBox.Show(
                    "Database name does not match.",
                    "Confirmation Failed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
        };

        btnRow.Controls.AddRange(new Control[] { cancelBtn, confirmBtn });
        layout.Controls.Add(btnRow);

        CancelButton = cancelBtn;
    }
}
