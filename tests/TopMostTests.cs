using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class TopMostTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    const uint PositionFlags = 0x0001 | 0x0002 | 0x0010 | 0x0200;
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr window, int index);
    sealed class CoverWindow : Form
    {
        protected override bool ShowWithoutActivation { get { return true; } }
    }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static bool Above(IntPtr first, IntPtr second)
    {
        for (IntPtr window = GetWindow(second, 3); window != IntPtr.Zero; window = GetWindow(window, 3))
            if (window == first) return true;
        return false;
    }
    static void Refresh(KeyboardPetWindow pet)
    {
        typeof(KeyboardPetWindow).GetField("nextTopMostRefresh", Private).SetValue(pet, 0.0);
        typeof(KeyboardPetWindow).GetMethod("Tick", Private).Invoke(pet, new object[] { null, EventArgs.Empty });
    }
    internal static void Run(string root)
    {
        using (var pet = new KeyboardPetWindow(root, true))
        using (var cover = new CoverWindow())
        {
            ((Timer)typeof(KeyboardPetWindow).GetField("timer", Private).GetValue(pet)).Stop();
            pet.Show();
            cover.FormBorderStyle = FormBorderStyle.None;
            cover.ShowInTaskbar = false; cover.TopMost = true;
            cover.StartPosition = FormStartPosition.Manual; cover.Bounds = new Rectangle(-30000, -30000, 1, 1);
            cover.Show();
            Check(SetWindowPos(cover.Handle, new IntPtr(-1), 0, 0, 0, 0, PositionFlags), "Could not raise competing topmost window");
            Check(Above(cover.Handle, pet.Handle), "Competing topmost window must reproduce the overlap");
            IntPtr foreground = GetForegroundWindow(); Rectangle bounds = pet.Bounds;
            Refresh(pet);
            Check(Above(pet.Handle, cover.Handle), "Timer must restore pet above competing topmost window");
            Check(GetForegroundWindow() == foreground && pet.Bounds == bounds, "Restoring topmost must preserve focus, position and size");

            // WinForms can still report TopMost=true after native Z order is lost.
            SetWindowPos(pet.Handle, new IntPtr(-2), 0, 0, 0, 0, PositionFlags);
            Check(pet.TopMost && (GetWindowLong(pet.Handle, -20) & 8) == 0, "Must reproduce managed/native topmost mismatch");
            Refresh(pet);
            Check((GetWindowLong(pet.Handle, -20) & 8) != 0 && Above(pet.Handle, cover.Handle), "Native topmost flag must recover");

            pet.Enabled = false;
            SetWindowPos(cover.Handle, new IntPtr(-1), 0, 0, 0, 0, PositionFlags);
            Refresh(pet);
            Check(Above(cover.Handle, pet.Handle), "Disabled pet must leave modal settings above it");
            pet.Enabled = true;
            var menu = (ContextMenuStrip)typeof(KeyboardPetWindow).GetField("menu", Private).GetValue(pet);
            menu.Show(pet, new Point(10, 10));
            SetWindowPos(cover.Handle, new IntPtr(-1), 0, 0, 0, 0, PositionFlags);
            Refresh(pet);
            Check(Above(cover.Handle, pet.Handle), "Open context menu must suspend Z-order refresh");
            menu.Close();
            Refresh(pet);
            Check(Above(pet.Handle, cover.Handle), "Topmost refresh must resume after menu closes");

            ToolStripMenuItem top = null;
            foreach (ToolStripItem item in menu.Items.Commands())
                if (item.Text == "保持置顶") top = (ToolStripMenuItem)item;
            Check(top != null, "Topmost menu item must exist");
            top.Checked = false;
            Refresh(pet);
            Check(!pet.TopMost && (GetWindowLong(pet.Handle, -20) & 8) == 0, "User opt-out must remain effective on timer ticks");
            top.Checked = true;
            Check(pet.TopMost && Above(pet.Handle, cover.Handle), "Enabling topmost must restore Z order immediately");
        }
        Console.WriteLine("PASS: native topmost recovery, competing windows, focus/bounds preservation, menu/modal suspension and user toggle.");
    }
}
