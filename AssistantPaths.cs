using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32.SafeHandles;

// Application-level file boundary for our native tools. No model-supplied code
// or external process is executed: changing a shell's CWD/TEMP is not a sandbox.
internal sealed class AssistantPaths
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint QueryDosDevice(string name, StringBuilder target, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, StringBuilder path, uint size, uint flags);
    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInfo info);
    [StructLayout(LayoutKind.Sequential)] struct FileInfo
    {
        internal uint Attributes;
        internal System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
        internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    internal readonly string Root;
    readonly bool userTarget;
    internal AssistantPaths(string root) : this(root, false) { }
    // Only explicit final-output/desktop tools opt into C:. Workspace, scripts,
    // temporary files, records and configuration always use the default policy.
    internal static AssistantPaths ForUserTarget(string root) { return new AssistantPaths(root, true); }
    AssistantPaths(string root, bool userTarget)
    {
        this.userTarget = userTarget;
        Root = Absolute(root, userTarget);
        if (Root.Length <= 3) throw new InvalidOperationException("工作目录不能是整个磁盘根目录。");
        CheckChain(Root);
    }
    internal static string Absolute(string value, bool userTarget = false)
    {
        string path = value ?? "";
        // Reject ambiguous/unsafe paths before filesystem lookup. C: is allowed
        // only for user targets, never for internal work/configuration files.
        if (!Regex.IsMatch(path, @"^[A-Za-z]:[\\/]")) throw new InvalidOperationException("必须使用本地绝对路径；内部工作目录须位于非 C 盘，例如 D:\\PetAssistant。");
        if (!userTarget && Char.ToUpperInvariant(path[0]) == 'C') throw new InvalidOperationException("工程、脚本、临时文件、缓存和日志的内部目录不能放在 C 盘；用户指定的操作目标和最终文件可以位于 C 盘。");
        if (path.Substring(2).Contains(":")) throw new InvalidOperationException("不允许备用数据流或额外盘符。");
        CheckParts(path.Substring(3));
        string full = Path.GetFullPath(path.Replace('/', '\\')).TrimEnd('\\');
        if (full.Length == 2) full += "\\";
        var device = new StringBuilder(1024);
        if (QueryDosDevice(full.Substring(0, 2), device, device.Capacity) == 0) throw new IOException("无法验证磁盘映射。", new Win32Exception());
        if (device.ToString().StartsWith(@"\??\", StringComparison.OrdinalIgnoreCase) ||
            !device.ToString().StartsWith(@"\Device\HarddiskVolume", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("不允许 SUBST、网络映射或无法验证的磁盘。");
        return full;
    }
    static void CheckParts(string value)
    {
        foreach (string part in value.Replace('/', '\\').Split('\\'))
        {
            if (part.Length == 0) continue;
            if (part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ") ||
                part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || part.Contains("%") || part.Contains("~") ||
                Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(?:\.|$)", RegexOptions.IgnoreCase))
                throw new InvalidOperationException("路径包含上级跳转、设备名、短路径或不允许的字符。");
        }
    }
    sealed class PathLease : IDisposable
    {
        internal readonly List<SafeFileHandle> Handles = new List<SafeFileHandle>();
        public void Dispose() { for (int i = Handles.Count - 1; i >= 0; i--) Handles[i].Dispose(); }
    }
    static void CheckChain(string full) { using (var lease = Acquire(full)) { } }
    static PathLease Acquire(string full)
    {
        var lease = new PathLease();
        try
        {
        string current = Path.GetPathRoot(full);
        foreach (string part in full.Substring(current.Length).Split('\\'))
        {
            if (part.Length == 0) continue;
            current = Path.Combine(current, part);
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (FileNotFoundException) { break; }
            catch (DirectoryNotFoundException) { break; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("不允许符号链接、目录联接或挂载点：" + current);
            // Do not follow the final reparse point. Hold every ancestor without
            // FILE_SHARE_DELETE until I/O finishes, so a directory cannot be
            // renamed or replaced by a junction between validation and access.
            var handle = CreateFile(current, 0, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            lease.Handles.Add(handle);
            {
                if (handle.IsInvalid) throw new IOException("无法验证实际路径：" + current, new Win32Exception());
                var resolved = new StringBuilder(32768);
                uint length = GetFinalPathNameByHandle(handle, resolved, (uint)resolved.Capacity, 0);
                if (length == 0 || length >= resolved.Capacity) throw new IOException("无法解析实际文件路径。");
                string actual = resolved.ToString();
                if (!actual.StartsWith(@"\\?\", StringComparison.Ordinal) ||
                    !String.Equals(actual.Substring(4).TrimEnd('\\'), current.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("实际路径与声明路径不符，已拒绝访问。");
                FileInfo info;
                if (!GetFileInformationByHandle(handle, out info)) throw new IOException("无法检查文件链接信息。");
                if ((info.Attributes & (uint)FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("不允许访问链接。");
                if ((attributes & FileAttributes.Directory) == 0 && info.Links > 1)
                    throw new InvalidOperationException("不允许通过硬链接访问文件。");
            }
        }
        return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    internal string Resolve(string relative)
    {
        Absolute(Root, userTarget); // Re-check drive mappings on every operation, not only at setup.
        if (String.IsNullOrEmpty(relative) || relative == ".") { CheckChain(Root); return Root; }
        if (Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0) throw new InvalidOperationException("工具只接受工作目录内的相对路径。");
        CheckParts(relative);
        string full = Path.GetFullPath(Path.Combine(Root, relative.Replace('/', '\\')));
        if (!full.StartsWith(Root + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("路径超出允许目录。");
        CheckChain(full); return full;
    }
    internal void CreateDirectory(string relative)
    {
        string path = Resolve(relative);
        using (var lease = Acquire(path)) { Directory.CreateDirectory(path); CheckChain(path); }
    }
    internal string Relative(string path)
    {
        string full = Absolute(path, userTarget);
        if (String.Equals(full, Root, StringComparison.OrdinalIgnoreCase)) return ".";
        if (!full.StartsWith(Root + "\\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("路径不属于当前目录。");
        return full.Substring(Root.Length + 1).Replace('\\', '/');
    }
    internal void WriteNew(string relative, byte[] contents)
    {
        string target = Resolve(relative);
        string parent = Path.GetDirectoryName(target);
        CreateDirectory(Relative(parent));
        using (var lease = Acquire(parent))
        {
        target = Resolve(relative);
        using (var stream = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        { stream.Write(contents, 0, contents.Length); stream.Flush(true); }
        CheckChain(target);
        }
    }
    internal byte[] Read(string relative, int limit)
    {
        string target = Resolve(relative);
        using (var lease = Acquire(target))
        using (var stream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length > limit) throw new InvalidOperationException("文件超出读取大小限制。");
            using (var data = new MemoryStream()) { stream.CopyTo(data); return data.ToArray(); }
        }
    }
    internal string[] List(string relative)
    {
        string directory = Resolve(relative);
        var entries = new List<string>();
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            string item = Relative(path); Resolve(item);
            entries.Add(item + (Directory.Exists(path) ? "/" : ""));
            if (entries.Count >= 200) break;
        }
        return entries.ToArray();
    }
    internal long Size(string relative) { return new System.IO.FileInfo(Resolve(relative)).Length; }
}
