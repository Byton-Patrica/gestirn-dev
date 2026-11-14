using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace GestirnDevPreview
{
internal static class DarkStyle
{
    public static void Apply(Form form)
    {
        form.BackColor = Theme.Surface;
        form.ForeColor = Theme.Text;
        form.Font = Theme.UiFont;
        form.StartPosition = FormStartPosition.CenterParent;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.ShowInTaskbar = false;
    }

    public static void StyleInput(TextBox box)
    {
        box.BackColor = Theme.SurfaceRaised;
        box.ForeColor = Theme.Text;
        box.BorderStyle = BorderStyle.FixedSingle;
        box.Font = Theme.UiFont;
    }

    public static Label Caption(string text, Point location)
    {
        return new Label { Text = text, Location = location, AutoSize = true, ForeColor = Theme.Muted, BackColor = Theme.Surface };
    }

    public static GlyphButton Button(string text, int width)
    {
        var button = new GlyphButton { Text = text, Width = width, Height = 34, Radius = 10 };
        return button;
    }
}

// Build settings: compiler, language standard and extra flags.
internal sealed class OptionsForm : Form
{
    private readonly TextBox compilerBox = new TextBox();
    private readonly ComboBox standardBox = new ComboBox();
    private readonly TextBox flagsBox = new TextBox();

    public OptionsForm(AppSettings settings)
    {
        DarkStyle.Apply(this);
        Text = "Build options";
        ClientSize = new Size(640, 260);

        Controls.Add(DarkStyle.Caption("C/C++ compiler (g++.exe)", new Point(20, 18)));
        compilerBox.SetBounds(20, 42, 500, 26);
        compilerBox.Text = settings.CompilerPath;
        DarkStyle.StyleInput(compilerBox);
        Controls.Add(compilerBox);

        var browse = DarkStyle.Button("Browse...", 100);
        browse.Location = new Point(528, 40);
        browse.Click += (_, _) =>
        {
            using (var dialog = new OpenFileDialog { Filter = "Compiler|g++.exe;gcc.exe|Programs|*.exe", FileName = compilerBox.Text })
                if (dialog.ShowDialog(this) == DialogResult.OK) compilerBox.Text = dialog.FileName;
        };
        Controls.Add(browse);

        var detect = DarkStyle.Button("Auto-detect", 120);
        detect.Location = new Point(20, 76);
        detect.Click += (_, _) =>
        {
            string found = CompilerLocator.Find("");
            if (found != null) compilerBox.Text = found;
            else MessageBox.Show(this, "No g++ was found. Install Dev-C++ or MSYS2, or browse to g++.exe.", "Build options", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        Controls.Add(detect);

        Controls.Add(DarkStyle.Caption("Language standard", new Point(20, 128)));
        standardBox.DropDownStyle = ComboBoxStyle.DropDownList;
        standardBox.Items.AddRange(new object[] { "c++11", "c++14", "c++17", "c++20" });
        standardBox.SelectedItem = standardBox.Items.Contains(settings.Standard) ? settings.Standard : "c++14";
        standardBox.SetBounds(20, 150, 200, 26);
        standardBox.BackColor = Theme.SurfaceRaised;
        standardBox.ForeColor = Theme.Text;
        Controls.Add(standardBox);

        Controls.Add(DarkStyle.Caption("Extra flags", new Point(240, 128)));
        flagsBox.SetBounds(240, 150, 380, 26);
        flagsBox.Text = settings.ExtraFlags;
        DarkStyle.StyleInput(flagsBox);
        Controls.Add(flagsBox);

        var ok = DarkStyle.Button("OK", 100);
        ok.Location = new Point(420, 208);
        ok.Click += (_, _) => Accept();
        var cancel = DarkStyle.Button("Cancel", 100);
        cancel.Location = new Point(528, 208);
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    public string CompilerPath { get { return compilerBox.Text.Trim(); } }
    public string Standard { get { return (string)standardBox.SelectedItem ?? "c++14"; } }
    public string ExtraFlags { get { return flagsBox.Text.Trim(); } }

    private void Accept()
    {
        string path = CompilerPath;
        if (path.Length > 0 && !File.Exists(path))
        {
            MessageBox.Show(this, "The compiler file does not exist:\n" + path, "Build options", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        DialogResult = DialogResult.OK;
    }
}

// One-line text prompt, used for Find.
internal static class Prompt
{
    public static string Show(IWin32Window owner, string title, string label, string initial)
    {
        using (var form = new Form())
        {
            DarkStyle.Apply(form);
            form.Text = title;
            form.ClientSize = new Size(440, 130);
            var caption = DarkStyle.Caption(label, new Point(18, 18));
            var box = new TextBox();
            box.Bounds = new Rectangle(18, 44, 404, 26);
            box.Text = initial ?? "";
            DarkStyle.StyleInput(box);
            var ok = DarkStyle.Button("OK", 100);
            ok.Location = new Point(204, 84);
            var cancel = DarkStyle.Button("Cancel", 100);
            cancel.Location = new Point(312, 84);
            form.Controls.Add(caption);
            form.Controls.Add(box);
            form.Controls.Add(ok);
            form.Controls.Add(cancel);
            ok.Click += (_, _) => form.DialogResult = DialogResult.OK;
            cancel.Click += (_, _) => form.DialogResult = DialogResult.Cancel;
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            return form.ShowDialog(owner) == DialogResult.OK && box.Text.Length > 0 ? box.Text : null;
        }
    }
}
}
