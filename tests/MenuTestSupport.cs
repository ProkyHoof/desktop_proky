using System.Collections.Generic;
using System.Windows.Forms;

internal static class MenuTestSupport
{
    internal static IEnumerable<ToolStripMenuItem> Commands(this ToolStripItemCollection items)
    {
        foreach(ToolStripItem item in items)
        {
            var command=item as ToolStripMenuItem;
            if(command==null)continue;
            yield return command;
            foreach(var child in command.DropDownItems.Commands())yield return child;
        }
    }
}
