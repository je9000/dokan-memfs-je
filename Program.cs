using System;
using System.IO;
using System.Collections;
using System.Runtime.InteropServices;
using Dokan;

namespace DokaMemFSje
{
    abstract class MemFsEntry
    {
        protected FileInformation fileinfo;

        public MemFsEntry(string name)
        {
            fileinfo = new FileInformation();
            fileinfo.CreationTime = fileinfo.LastAccessTime = fileinfo.LastWriteTime = DateTime.Now;
            fileinfo.Attributes = 0; // Child must fill this in!
            fileinfo.FileName = name;
            fileinfo.Length = 0;
        }

        public FileInformation GetFileInfo()
        {
            FileInformation r = new FileInformation();
            r.Attributes = fileinfo.Attributes;
            r.CreationTime = fileinfo.CreationTime;
            r.LastAccessTime = fileinfo.LastAccessTime;
            r.LastWriteTime = fileinfo.LastWriteTime;
            r.FileName = fileinfo.FileName;
            r.Length = fileinfo.Length;
            return r;
        }

        public bool IsValidName(string name)
        {
            if (name.Equals("")) { return false; }
            if (name.Equals(".")) { return false; }
            if (name.Equals("..")) { return false; }
            if (name.Contains("/")) { return false; }
            if (name.Contains("\\")) { return false; }
            return true;
        }

        public bool SetName(string newname)
        {
            if (!this.IsValidName(newname)) { return false; }
            fileinfo.FileName = newname;
            return true;
        }

        public abstract bool SetFileAttributes(FileAttributes attr);

        public void SetFileTime(DateTime ctime, DateTime atime, DateTime mtime)
        {
            fileinfo.LastWriteTime = mtime;
            fileinfo.LastAccessTime = atime;
            fileinfo.CreationTime = ctime;
        }
    }

    class MemFSDirectory : MemFsEntry
    {
        Hashtable contents;
        public MemFSDirectory(string name) : base(name)
        {
            contents = new Hashtable();
            fileinfo.Attributes = FileAttributes.Directory;
        }

        public bool AddFile(string name)
        {
            if (!this.IsValidName(name)) { return false; }

            if (contents.ContainsKey(name)) { return false; }
            contents.Add(name, new MemFSFile(name));
            return true;
        }

        public bool AddDirectory(string name) {
            if (!this.IsValidName(name)) { return false; }

            if (contents.ContainsKey(name)) { return false; }
            contents.Add(name, new MemFSDirectory(name));
            return true;
        }

        public bool AddEntry(string name, MemFsEntry entry)
        {
            if (!this.IsValidName(name)) { return false; }

            if (contents.ContainsKey(name)) { return false; }
            contents.Add(name, entry);
            return true;
        }

        public bool RemoveEntry(string name)
        {
            if (!this.IsValidName(name)) { return false; }

            if (!contents.ContainsKey(name)) { return false; }
            contents.Remove(name);
            return true;
        }

        public MemFsEntry GetEntry(string name)
        {
            if (contents.ContainsKey(name))
            {
                return (MemFsEntry) contents[name];
            }
            return null;
        }

        public MemFsEntry[] GetEntries()
        {
            MemFsEntry[] results = new MemFsEntry[contents.Count];
            int x = 0;

            foreach (object o in contents.Values)
            {
                results[x++] = (MemFsEntry)o;
            }
            return results;
        }

        public override bool SetFileAttributes(FileAttributes attr)
        {
            if ((attr & FileAttributes.Compressed) == FileAttributes.Compressed) { return false; }
            if ((attr & FileAttributes.Encrypted) == FileAttributes.Encrypted) { return false; }
            if ((attr & FileAttributes.Normal) == FileAttributes.Normal ) { return false; }
            if ((attr & FileAttributes.ReadOnly) == FileAttributes.ReadOnly ) { return false; }
            if ((attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint ) { return false; }
            if ((attr & FileAttributes.Hidden) == FileAttributes.Hidden ) { return false; }
            if ((attr & FileAttributes.Directory) != FileAttributes.Directory ) { return false; }

            fileinfo.Attributes = attr;
            return true;
        }
    }

    class MemFSFile : MemFsEntry
    {

        IntPtr mydata;
        public uint refcount;
        public bool deleteonclose;
        public MemFSFile(string name) : base(name)
        {
            fileinfo.Attributes = FileAttributes.Normal;
            mydata = Marshal.AllocHGlobal(0);
            refcount = 0;
            deleteonclose = false;
        }

        ~MemFSFile()
        {
            Marshal.FreeHGlobal(mydata);
        }

        public bool Resize(long newsize)
        {
            if (newsize < 0) { return false; }
            if (newsize == fileinfo.Length) { return true; }
            try
            {
                mydata = Marshal.ReAllocHGlobal(mydata, (IntPtr)newsize);
                fileinfo.Length = newsize;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool Read(Byte[] buffer, ref uint readBytes, long offset)
        {
            if ((buffer.Length > Int32.MaxValue) || (offset > fileinfo.Length))
            {
                readBytes = 0;
                return false;
            }
            if ( ( buffer.Length == 0 ) || ( offset == fileinfo.Length ) ) {
                readBytes = 0;
                return true;
            }

            readBytes = (uint) buffer.Length; // Cast is safe because whe check for overflow above.
            if (readBytes > (fileinfo.Length - offset))
            {
                readBytes = (uint) ( fileinfo.Length - offset ); // Safe because we check above against an overflow.
            }
            try
            {
                Marshal.Copy((IntPtr)(mydata.ToInt64() + offset), buffer, 0, (int)readBytes);
            }
            catch
            {
                readBytes = 0;
                return false;
            }
            return true;
        }

        public bool Write(Byte[] buffer, ref uint writtenBytes, long offset)
        {
            try
            {
                if (buffer.LongLength > Int32.MaxValue) {
                    writtenBytes = 0;
                    return false;
                }
                if (buffer.Length + offset > fileinfo.Length)
                {
                    mydata = Marshal.ReAllocHGlobal(mydata, (IntPtr) (buffer.Length + offset));
                    fileinfo.Length = buffer.Length + offset;
                }
                Marshal.Copy(buffer, 0, (IntPtr)( mydata.ToInt64() + offset ), buffer.Length);
                writtenBytes = (uint) buffer.Length;
            }
            catch
            {
                writtenBytes = 0;
                return false;
            }
            return true;
        }

        public override bool SetFileAttributes(FileAttributes attr)
        {
            // "touch" from cygwin seems to want to set the file attributes to
            // 0. As far as I can tell, 0 and Normal are equivelent, so
            // lets act like they requested Normal.
            if ((attr & FileAttributes.Normal) != FileAttributes.Normal) { attr |= FileAttributes.Normal; }

            if ((attr & FileAttributes.Compressed) == FileAttributes.Compressed) { return false; }
            if ((attr & FileAttributes.Encrypted) == FileAttributes.Encrypted) { return false; }
            if ((attr & FileAttributes.ReadOnly) == FileAttributes.ReadOnly) { return false; }
            if ((attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint) { return false; }
            if ((attr & FileAttributes.Hidden) == FileAttributes.Hidden) { return false; }
            if ((attr & FileAttributes.Directory) == FileAttributes.Directory) { return false; }

            fileinfo.Attributes = attr;
            return true;
        }
    }

    class MemFSje : DokanOperations
    {
        private MemFSDirectory root;
        public MemFSje()
        {
            root = new MemFSDirectory("");
        }

        [DllImport("kernel32.dll")]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

        [StructLayout(LayoutKind.Sequential)] //, Pack = 4)]
        private struct MemoryStatusEx
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        private MemFSDirectory GetDirectoryForPath(string path)
        {
            string[] elements = path.Split( new Char[] { '/', '\\' } );

            MemFSDirectory cwd = root;
            MemFsEntry e;
            for (int x = 1; x < elements.Length - 1; x++)
            {
                e = cwd.GetEntry(elements[x]);
                if ( (e == null) || ( !( e is MemFSDirectory ) ) )
                {
                    return null;
                }
                cwd = (MemFSDirectory)e;
            }

            return cwd;
        }

        private MemFsEntry GetEntryForPath(string path)
        {
            // Seems like a hack, but it's a consequence of the way we store
            // directories. When opening the root, the driver asks for "\\" (
            // note that is one backslash escaped). If we break this up into
            // components we get a directory of "" and a file of "". Opening a
            // directory of "" works, because GetDirectoryForPath returns the
            // root when there are no backslashes to split on. But inside the
            // root there is no file named "". I don't see a better way of
            // handling this, unless we create a "virtual" entry named "" in
            // each directory that points to self? That seems worse, since
            // we don't handle "." or ".." references either.
            if (path.Equals("\\")) { return root; }
            MemFSDirectory dir = GetDirectoryForPath(path);
            if (dir == null) { return null; }
            return dir.GetEntry(GetFilenameFromPath(path));
        }

        private string GetFilenameFromPath(string path)
        {
            string[] elements = path.Split(new Char[] { '/', '\\' });
            return elements[elements.Length - 1];
        }

        // Public methods follow...

        public int CreateFile(String filename, FileAccess access, FileShare share,
            FileMode mode, FileOptions options, DokanFileInfo info)
        {
            MemFsEntry entry = GetEntryForPath(filename);

            // Not a lot to do for directories.
            if (entry != null && entry is MemFSDirectory)
            {
                info.IsDirectory = true;
                return 0;
            }

            // File exists and caller requests we overwrite.
            if ( ( entry != null ) && ( mode == FileMode.Create ) )
            {
                GetDirectoryForPath(filename).RemoveEntry(GetFilenameFromPath(filename));
                entry = null;
            }

            // File doesn't exist, do we create?
            if (entry == null)
            {
                if (mode == FileMode.Create || mode == FileMode.CreateNew || mode == FileMode.OpenOrCreate)
                {
                    if (GetDirectoryForPath(filename).AddFile(GetFilenameFromPath(filename)))
                    {
                        entry = GetEntryForPath(filename);
                        ((MemFSFile)entry).refcount = 1;
                        if ((options & FileOptions.DeleteOnClose) == FileOptions.DeleteOnClose)
                        {
                            ((MemFSFile)entry).deleteonclose = true;
                        }

                        return 0;
                    }
                    else { return -1; }
                }
                else { return -DokanNet.ERROR_FILE_NOT_FOUND; }
            }

            // File exists, do we open?
            if (mode == FileMode.CreateNew) { return -DokanNet.ERROR_ALREADY_EXISTS; }

            // Okay, open it.

            if (!(entry is MemFSFile))
            {
                // We don't support anything other than files or directories
                // (which are opened above and created in another method).
                return -1;
            }

            if ((options & FileOptions.DeleteOnClose) == FileOptions.DeleteOnClose)
            {
                // If they want delete-on-close, it must be opened that way
                // already.
                if ((((MemFSFile)entry).refcount > 0)
                    && (!((MemFSFile)entry).deleteonclose)
                )
                {
                    return -1;
                }
            }

            if (mode == FileMode.Truncate)
            {
                ((MemFSFile)entry).Resize(0);
            }

            ((MemFSFile)entry).refcount++;
            return 0;

        }

        public int OpenDirectory(String filename, DokanFileInfo info)
        {
            object o = GetEntryForPath(filename);
            if (o is MemFSDirectory) { return 0; }
            return -1;
        }

        public int CreateDirectory(String filename, DokanFileInfo info)
        {
            MemFsEntry entry = GetDirectoryForPath(filename);
            if (entry == null) { return -DokanNet.ERROR_FILE_NOT_FOUND; }
            if (GetEntryForPath(filename) != null) { return -DokanNet.ERROR_ALREADY_EXISTS; }
            if (((MemFSDirectory)entry).AddDirectory(GetFilenameFromPath(filename)))
            {
                return 0;
            }
            return -1;
        }

        public int Cleanup(String filename, DokanFileInfo info)
        {
            return 0;
        }

        public int CloseFile(String filename, DokanFileInfo info)
        {
            MemFsEntry entry = GetEntryForPath(filename);
            if (entry is MemFSFile)
            {
                ((MemFSFile)entry).refcount--;
                if ((((MemFSFile)entry).deleteonclose) && (((MemFSFile)entry).refcount == 0))
                {
                    this.DeleteFile(filename, info);
                }
            }
            return 0;
        }

        public int ReadFile(String filename, Byte[] buffer, ref uint readBytes,
            long offset, DokanFileInfo info)
        {
            MemFsEntry entry = GetEntryForPath(filename);
            if (!(entry is MemFSFile)) { return -DokanNet.ERROR_FILE_NOT_FOUND; }

            if (((MemFSFile)entry).Read(buffer, ref readBytes, offset))
            {
                return 0;
            }
            return -1;
        }

        public int WriteFile(String filename, Byte[] buffer,
            ref uint writtenBytes, long offset, DokanFileInfo info)
        {
            MemFsEntry entry = GetEntryForPath(filename);
            if (!(entry is MemFSFile)) { return -DokanNet.ERROR_FILE_NOT_FOUND; }

            if (((MemFSFile)entry).Write(buffer, ref writtenBytes, offset))
            {
                return 0;
            }
            return -1;
        }

        public int FlushFileBuffers(String filename, DokanFileInfo info)
        {
            return 0;
        }

        public int GetFileInformation(String filename, FileInformation fileinfo, DokanFileInfo info)
        {
            MemFsEntry entry = GetEntryForPath(filename);
            if (entry == null) { return -DokanNet.ERROR_FILE_NOT_FOUND; }

            FileInformation f = entry.GetFileInfo();

            fileinfo.Attributes = f.Attributes;
            fileinfo.CreationTime = f.CreationTime;
            fileinfo.LastAccessTime = f.LastAccessTime;
            fileinfo.LastWriteTime = f.LastWriteTime;
            fileinfo.Length = f.Length;
            fileinfo.FileName = f.FileName;

            return 0;
        }

        public int FindFiles(String filename, ArrayList files, DokanFileInfo info)
        {
            MemFsEntry entry = GetEntryForPath(filename);
            if (!(entry is MemFSDirectory)) { return -DokanNet.ERROR_FILE_NOT_FOUND; }

            FileInformation dots = entry.GetFileInfo();
            dots.FileName = ".";
            files.Add(dots);

            if (entry == root)
            {
                dots = entry.GetFileInfo();
                dots.FileName = "..";
                files.Add(dots);
            }
            else
            {
                dots = GetDirectoryForPath(filename).GetFileInfo();
                dots.FileName = "..";
                files.Add(dots);
            }
            
            foreach (MemFsEntry e in ((MemFSDirectory) entry).GetEntries() )
            {
                files.Add(e.GetFileInfo());
            }

            return 0;
        }

        public int SetFileAttributes(String filename, FileAttributes attr, DokanFileInfo info)
        {
            MemFsEntry entry = GetEntryForPath(filename);
            if (entry == null) { return -DokanNet.ERROR_FILE_NOT_FOUND; }
            if (entry.SetFileAttributes(attr))
            {
                return 0;
            }
            return -1;
        }

        public int SetFileTime(String filename, DateTime ctime,
                DateTime atime, DateTime mtime, DokanFileInfo info)
        {
            // Disabled - Throws errors about improper times for files?
            //MemFsEntry entry = GetEntryForPath(filename);
            //if (entry == null) { return -DokanNet.ERROR_FILE_NOT_FOUND; }
            //entry.SetFileTime(ctime, atime, mtime);
            return 0;
        }

        public int DeleteFile(String filename, DokanFileInfo info)
        {
            MemFsEntry entry = GetEntryForPath(filename);
            if (entry == null) { return -DokanNet.ERROR_FILE_NOT_FOUND; }
            if (!(entry is MemFSFile)) { return -1; }
            entry = GetDirectoryForPath(filename);
            if (((MemFSDirectory)entry).RemoveEntry(GetFilenameFromPath(filename)))
            {
                return 0;
            }
            return -1;
        }

        public int DeleteDirectory(String filename, DokanFileInfo info)
        {
            MemFsEntry entry = GetEntryForPath(filename);
            if (entry == null) { return -DokanNet.ERROR_FILE_NOT_FOUND; }
            if (!(entry is MemFSDirectory)) { return -1; }
            entry = GetDirectoryForPath(filename);
            if (((MemFSDirectory)entry).RemoveEntry(GetFilenameFromPath(filename)))
            {
                return 0;
            }
            return -1;
        }

        public int MoveFile(String filename, String newname, bool replace, DokanFileInfo info)
        {
            MemFsEntry entry = GetEntryForPath(filename);
            if (entry == null) { return -DokanNet.ERROR_FILE_NOT_FOUND; }

            MemFsEntry destentry = GetEntryForPath(newname);
            string newfilename = GetFilenameFromPath(newname);

            if ( destentry != null ) {
                if ( replace ) {
                    if (!GetDirectoryForPath(newname).RemoveEntry(newfilename))
                    {
                        return -DokanNet.ERROR_ALREADY_EXISTS;
                    }
                } else {
                    return -DokanNet.ERROR_ALREADY_EXISTS;
                }
            }

            if (!GetDirectoryForPath(newname).AddEntry(newfilename, entry)) { return -1; }
            if (!GetDirectoryForPath(filename).RemoveEntry(GetFilenameFromPath(filename))) { return -1; }
            entry.SetName(newfilename);

            return 0;
        }

        public int SetEndOfFile(String filename, long length, DokanFileInfo info)
        {
            MemFsEntry entry = GetEntryForPath(filename);
            if (entry == null) { return -DokanNet.ERROR_FILE_NOT_FOUND; }
            if (entry is MemFSFile)
            {
                if (((MemFSFile)entry).Resize(length))
                {
                    return 0;
                }
                // else fall through...
            }
            return -1;
        }

        public int SetAllocationSize(String filename, long length, DokanFileInfo info)
        {
            return this.SetEndOfFile(filename, length, info);
        }

        public int LockFile(String filename, long offset, long length, DokanFileInfo info)
        {
            return 0;
        }

        public int UnlockFile(String filename, long offset, long length, DokanFileInfo info)
        {
            return 0;
        }

        public int GetDiskFreeSpace(ref ulong freeBytesAvailable, ref ulong totalBytes,
            ref ulong totalFreeBytes, DokanFileInfo info)
        {
            MemoryStatusEx ms = new MemoryStatusEx();
            ms.dwLength = (uint)Marshal.SizeOf(ms);
            GlobalMemoryStatusEx(ref ms);
            totalBytes = ms.ullTotalPageFile;
            freeBytesAvailable = ms.ullAvailPageFile;
            totalFreeBytes = ms.ullAvailPageFile;

            return 0;
        }

        public int Unmount(DokanFileInfo info)
        {
            return 0;
        }

        static void Main(string[] args)
        {
            DokanOptions opt = new DokanOptions();
            opt.DebugMode = false;
            opt.DriveLetter = 'm';
            // Leave to default
            opt.ThreadCount = 1;
            opt.VolumeLabel = "MEMFS";
            DokanNet.DokanMain(opt, new MemFSje());
        }
    }
}
