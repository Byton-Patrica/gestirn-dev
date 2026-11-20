using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GestirnDev
{
// Shows what the AI changed: its explanation and the corrected file with the changed lines highlighted.
internal sealed class AiFixDialog : DarkDialog
{
    public bool Applied { get; private set; }

    public AiFixDialog(AiFix.Result result, string original, string host)
    {
        Text = L.T("AI fix (experimental)", "AI 纠错（实验性）");
        ClientSize = Ui.D(900, 700);
        int pad = Ui.D(20);

        AddLabel(L.T("What the AI found", "AI 的分析"), pad, Ui.D(16), Ui.D(400), Theme.UiSemi, Theme.Orange);
        var explain = new RichTextBox
        {
            Bounds = new Rectangle(pad, Ui.D(42), ClientSize.Width - pad * 2, Ui.D(110)), ReadOnly = true, BorderStyle = BorderStyle.None,
            BackColor = Theme.Raised, ForeColor = Theme.Text, Font = Theme.Ui10, Text = string.IsNullOrWhiteSpace(result.Explanation) ? L.T("(no explanation)", "（没有说明）") : result.Explanation,
        };
        Native.DarkScrollbars(explain);
        Controls.Add(explain);

        bool hasCode = !string.IsNullOrEmpty(result.Code);
        AddLabel(hasCode ? L.T("Corrected file (changed lines highlighted)", "修改后的文件（改动的行已高亮）") : L.T("The AI sent no code", "AI 没有给出代码"),
            pad, Ui.D(166), Ui.D(600), Theme.UiSemi, Theme.Orange);
        var code = new RichTextBox
        {
            Bounds = new Rectangle(pad, Ui.D(192), ClientSize.Width - pad * 2, Ui.D(400)), ReadOnly = true, BorderStyle = BorderStyle.None, WordWrap = false,
            BackColor = Theme.EditorBg, ForeColor = Theme.SynPlain, Font = Theme.CodeSmall, ScrollBars = RichTextBoxScrollBars.Both,
        };
        Native.DarkScrollbars(code);
        Controls.Add(code);
        if (hasCode) Fill(code, original, result.Code);

        AddLabel(L.T("AI can be wrong. Read the change before you apply it. Data was sent to ", "AI 也会出错，应用前请先看一遍改动。数据已发送到 ") + host,
            pad, Ui.D(602), ClientSize.Width - pad * 2, Theme.UiSmall, Theme.Muted);

        var apply = AddButton(L.T("Apply fix", "应用修复"), ButtonKind.Primary, ClientSize.Width - Ui.D(372), ClientSize.Height - Ui.D(54), Ui.D(120));
        var copy = AddButton(L.T("Copy code", "复制代码"), ButtonKind.Secondary, ClientSize.Width - Ui.D(244), ClientSize.Height - Ui.D(54), Ui.D(110));
        var close = AddButton(L.T("Close", "关闭"), ButtonKind.Secondary, ClientSize.Width - Ui.D(126), ClientSize.Height - Ui.D(54), Ui.D(106));
        apply.Enabled = copy.Enabled = hasCode;
        apply.Click += (_, _) => { Applied = true; DialogResult = DialogResult.OK; };
        copy.Click += (_, _) => { try { Clipboard.SetText(result.Code); } catch (System.Runtime.InteropServices.ExternalException) { } };
        close.Click += (_, _) => DialogResult = DialogResult.Cancel;
    }

    private static void Fill(RichTextBox box, string original, string fixedCode)
    {
        var known = new HashSet<string>((original ?? "").Replace("\r", "").Split('\n').Select(l => l.Trim()));
        box.Clear();
        foreach (var line in fixedCode.Replace("\r", "").Split('\n'))
        {
            bool changed = line.Trim().Length > 0 && !known.Contains(line.Trim());
            box.SelectionStart = box.TextLength;
            box.SelectionBackColor = changed ? Theme.IsLight ? Color.FromArgb(214, 232, 170) : Color.FromArgb(52, 72, 24) : box.BackColor;
            box.SelectionColor = box.ForeColor;
            box.AppendText(line + "\n");
        }
        box.SelectionStart = 0;
        box.ScrollToCaret();
    }
}
}
