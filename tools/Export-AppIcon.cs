using System.IO;

internal static class ExportAppIcon
{
    static void Main(string[] args)
    {
        using (var stream = File.Create(args[0])) KnightUi.WindowIcon.Save(stream);
    }
}
