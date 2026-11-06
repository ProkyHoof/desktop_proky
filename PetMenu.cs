using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// Keep native menu navigation, placement and accessibility; only replace its paint.
internal sealed class PetContextMenu : ContextMenuStrip
{
    internal enum Glyph { None, Toggle, Choice, Settings, Size, Hair, Keys, Corner, Status, Exit }
    readonly Font menuFont = new Font("Microsoft YaHei UI", 9f);
    readonly float scale;

    internal PetContextMenu()
    {
        using (var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
        Font = menuFont;
    }

    internal void ApplyTheme()
    {
        if (SystemInformation.HighContrast) return;
        Configure(this, new PetMenuRenderer(scale), true);
    }

    int Px(float value) { return (int)Math.Round(value * scale); }
    void Configure(ToolStripDropDownMenu dropDown, ToolStripRenderer renderer, bool root)
    {
        dropDown.Renderer = renderer;
        dropDown.Font = Font;
        dropDown.BackColor = KnightUi.Paper;
        dropDown.ForeColor = KnightUi.Ink;
        dropDown.ShowImageMargin = dropDown.ShowCheckMargin = false;
        // DropDownMenu owns layout padding, so keep the compact bounds explicit.
        int width = Px(root ? 234 : 216);
        int height = Px(8);
        foreach (ToolStripItem item in dropDown.Items)
            width = Math.Max(width, TextRenderer.MeasureText(item.Text, Font).Width + Px(58));
        dropDown.MinimumSize = new Size(width, 0);
        foreach (ToolStripItem item in dropDown.Items)
        {
            item.AutoSize = false;
            item.Margin = Padding.Empty;
            bool status = item.Tag is Glyph && (Glyph)item.Tag == Glyph.Status;
            item.Size = new Size(width - Px(2), Px(item is ToolStripSeparator ? 9 : status ? 24 : 29));
            height += item.Height;
            var parent = item as ToolStripMenuItem;
            if (parent != null && parent.HasDropDownItems)
                Configure((ToolStripDropDownMenu)parent.DropDown, renderer, false);
        }
        dropDown.AutoSize = false;
        dropDown.Size = new Size(width, height);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) menuFont.Dispose();
    }
}

internal sealed class PetMenuRenderer : ToolStripRenderer
{
    static Color Ink { get { return KnightUi.Ink; } }
    static Color Muted { get { return KnightUi.Steel; } }
    static Color Red { get { return KnightUi.Red; } }
    readonly float scale;
    internal PetMenuRenderer(float scale) { this.scale = scale; }
    int Px(float value) { return (int)Math.Round(value * scale); }
    static PetContextMenu.Glyph Role(ToolStripItem item)
    {
        return item.Tag is PetContextMenu.Glyph ? (PetContextMenu.Glyph)item.Tag : PetContextMenu.Glyph.None;
    }
    static bool Highlighted(ToolStripItem item)
    {
        var parent = item as ToolStripMenuItem;
        return item.Enabled && (item.Selected || parent != null && parent.DropDown.Visible);
    }
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        e.Graphics.Clear(KnightUi.Paper);
        using(var shade=new Pen(KnightUi.Silver,Math.Max(1,scale)))
        {
            e.Graphics.DrawLine(shade,2,e.ToolStrip.Height-3,e.ToolStrip.Width-3,e.ToolStrip.Height-3);
            e.Graphics.DrawLine(shade,e.ToolStrip.Width-3,2,e.ToolStrip.Width-3,e.ToolStrip.Height-3);
        }
    }
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        var state=e.Graphics.Save();e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using (var plate=KnightUi.Plate(new RectangleF(.5f,.5f,e.ToolStrip.Width-1,e.ToolStrip.Height-1),Px(4)))
        using (var pen = new Pen(Ink,Math.Max(1,scale)))e.Graphics.DrawPath(pen,plate);
        e.Graphics.Restore(state);
    }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e) { }
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!Highlighted(e.Item)) return;
        var state = e.Graphics.Save();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var r = new Rectangle(Px(4), Px(1), e.Item.Width - Px(8), e.Item.Height - Px(2));
        using(var plate=KnightUi.Plate(r,Px(3)))
        using(var fill=new SolidBrush(e.Item.Pressed?Ink:Color.FromArgb(235,235,235)))
        using(var edge=new Pen(e.Item.Pressed?Ink:KnightUi.Silver))
        {e.Graphics.FillPath(fill,plate);e.Graphics.DrawPath(edge,plate);}
        using (var brush = new SolidBrush(Red))
            e.Graphics.FillRectangle(brush, Px(5), e.Item.Height / 2 - Px(4), Px(2), Px(8));
        e.Graphics.Restore(state);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        bool selected = Highlighted(e.Item);
        var role = Role(e.Item);
        Color color = !e.Item.Enabled ? Muted : e.Item.Pressed ? Color.White : role == PetContextMenu.Glyph.Exit ? Red : Ink;
        var rect = new Rectangle(Px(36), 0, e.Item.Width - Px(56), e.Item.Height);
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, rect, color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
            TextFormatFlags.NoPrefix | (e.ToolStrip.RightToLeft == RightToLeft.Yes ? TextFormatFlags.RightToLeft : 0));
        DrawGlyph(e.Graphics, e.Item, e.Item.Pressed ? Color.White : Red);
    }
    void DrawGlyph(Graphics g, ToolStripItem item, Color color)
    {
        var option=item as ToolStripMenuItem;var role=Role(item);
        var bounds=new RectangleF(Px(11),(item.Height-Px(16))/2f,Px(16),Px(16));
        if(role==PetContextMenu.Glyph.Toggle||role==PetContextMenu.Glyph.Choice)
        {
            if(option!=null&&option.Checked)KnightIcons.Draw(g,KnightIcon.Check,bounds,color);
            else using(var p=new Pen(KnightUi.Silver))g.DrawRectangle(p,bounds.X+3,bounds.Y+3,bounds.Width-6,bounds.Height-6);
            return;
        }
        KnightIcon icon=item.Text.Contains("日历")?KnightIcon.Calendar:item.Text.Contains("对话")?KnightIcon.Chat:
            role==PetContextMenu.Glyph.Settings?KnightIcon.Settings:role==PetContextMenu.Glyph.Size?KnightIcon.Size:
            role==PetContextMenu.Glyph.Hair?KnightIcon.Hair:role==PetContextMenu.Glyph.Keys?KnightIcon.Keyboard:
            role==PetContextMenu.Glyph.Corner?KnightIcon.Pin:role==PetContextMenu.Glyph.Exit?KnightIcon.Power:KnightIcon.None;
        KnightIcons.Draw(g,icon,bounds,item.Pressed?Color.White:!item.Enabled?Muted:Ink);
    }
    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        var state = e.Graphics.Save();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int x = e.Item.Width - Px(17), y = e.Item.Height / 2;
        int direction = e.Direction == ArrowDirection.Left ? -1 : 1;
        using (var pen = new Pen(e.Item.Pressed ? Color.White : Muted, Math.Max(1, scale * 1.2f)) { LineJoin = LineJoin.Round })
            e.Graphics.DrawLines(pen, new[] { new Point(x - direction * Px(2), y - Px(3)), new Point(x + direction * Px(1), y), new Point(x - direction * Px(2), y + Px(3)) });
        e.Graphics.Restore(state);
    }
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y=e.Item.Height/2;
        using (var pen = new Pen(KnightUi.Silver))
            e.Graphics.DrawLine(pen, Px(11), y, e.Item.Width - Px(11), y);
    }
}
