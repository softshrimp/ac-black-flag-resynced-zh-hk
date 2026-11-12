using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

[assembly: AssemblyTitle("Black Flag Resynced 繁中香港包")]
[assembly: AssemblyCompany("柔軟的蝦")]
[assembly: AssemblyCopyright("Copyright (c) 2026 柔軟的蝦")]
[assembly: AssemblyVersion("2026.11.11.2")]
[assembly: AssemblyFileVersion("2026.11.11.2")]

namespace ACBFLocalPatch
{
    public sealed class RangeCheck { public long offset; public int length; public string sha256; }
    public sealed class FileCheck { public string file; public long length; public RangeCheck[] ranges; }
    public sealed class Compatibility { public string version; public string patch_name; public string patch_sha256; public FileCheck[] checks; }
    public sealed class InstallState
    {
        public int format;
        public string target;
        public string patch_name;
        public string version;
        public bool original_exists;
        public string original_sha256;
        public long original_length;
        public string installed_sha256;
        public string phase;
    }

    internal static class Program
    {
        internal static string DefaultTarget { get { string folder=AppDomain.CurrentDomain.BaseDirectory; return File.Exists(Path.Combine(folder,"ACBlackFlag.exe"))?folder:String.Empty; } }
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 0)
            {
                try
                {
                    // WinForms executables have no console. Write directly to inherited
                    // pipes when invoked through the CLI; do not call SetConsoleOutputCP.
                    Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
                    Console.SetError(new StreamWriter(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true });
                    if (args.Length != 2 || (args[0] != "--install" && args[0] != "--restore"))
                        throw new InvalidOperationException("用法：--install TARGET 或 --restore TARGET");
                    var engine = new Engine(); engine.Progress = Console.WriteLine;
                    Console.WriteLine(engine.Run(args[1], args[0] == "--restore"));
                    return 0;
                }
                catch (Exception ex) { Console.Error.WriteLine("ERROR: " + ex.Message); return 1; }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new InstallerForm());
            return 0;
        }
    }

    internal sealed class InstallerForm : Form
    {
        private readonly TextBox path = new TextBox();
        private readonly TextBox result = new TextBox();
        private readonly Button install = new Button();
        private readonly Button restore = new Button();
        private readonly Button browse = new Button();
        internal InstallerForm()
        {
            Text = "Black Flag Resynced 繁中香港包 — 2026.11.11-rc2";
            ClientSize = new Size(700, 385); MinimumSize = new Size(716, 424);
            StartPosition = FormStartPosition.CenterScreen; Font = new Font("Microsoft JhengHei UI", 10);
            var info = new Label { Left = 18, Top = 16, Width = 660, Height = 100,
                Text = "公開候選版・柔軟的蝦\r\n20,873 條審校記錄；11,800 條修訂。\r\n安裝後請自行選擇繁體中文語言槽。本程式不更改遊戲設定，也不啟動遊戲。\r\n安裝及還原前，請先完全關閉遊戲。需準備及多次核對約 27 GB 封裝；請等候完成。" };
            Controls.Add(info);
            Controls.Add(new Label { Left = 18, Top = 121, Width = 650, Height = 23, Text = "遊戲資料夾" });
            path.SetBounds(18, 148, 555, 28); path.Text = Program.DefaultTarget; path.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            browse.Text = "選擇…"; browse.SetBounds(583, 146, 96, 31); browse.Anchor = AnchorStyles.Right | AnchorStyles.Top;
            browse.Click += delegate { using (var dialog = new FolderBrowserDialog()) { dialog.Description = "選擇遊戲資料夾"; dialog.SelectedPath = path.Text; if (dialog.ShowDialog(this) == DialogResult.OK) path.Text = dialog.SelectedPath; } };
            install.Text = "安裝補丁"; install.SetBounds(18, 190, 130, 36); install.Click += delegate { Execute(false); };
            restore.Text = "還原原檔"; restore.SetBounds(160, 190, 130, 36); restore.Click += delegate { Execute(true); };
            result.SetBounds(18, 240, 661, 127); result.Multiline = true; result.ReadOnly = true; result.ScrollBars = ScrollBars.Vertical;
            result.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
            result.Text = "選擇資料夾後，按「安裝補丁」或「還原原檔」才會執行。";
            Controls.AddRange(new Control[] { path, browse, install, restore, result });
            FormClosing += delegate(object sender,FormClosingEventArgs e) { if(!install.Enabled) { e.Cancel=true;result.AppendText("\r\n請等待安裝或還原完成後再關閉。\r\n"); } };
        }
        private void Execute(bool restoring)
        {
            install.Enabled = restore.Enabled = browse.Enabled = path.Enabled = false; UseWaitCursor = true;
            string chosen=path.Text;
            result.Text="正在核對，請保持遊戲關閉。\r\n";
            var worker=new System.ComponentModel.BackgroundWorker();
            worker.WorkerReportsProgress=true;
            worker.DoWork += delegate(object sender,System.ComponentModel.DoWorkEventArgs e) {
                var engine=new Engine();engine.Progress=message=>worker.ReportProgress(0,message);
                e.Result=engine.Run(chosen,restoring);
            };
            worker.ProgressChanged += delegate(object sender,System.ComponentModel.ProgressChangedEventArgs e) {
                result.AppendText((string)e.UserState+"\r\n");
            };
            worker.RunWorkerCompleted += delegate(object sender,System.ComponentModel.RunWorkerCompletedEventArgs e) {
                result.AppendText(e.Error==null?(string)e.Result:"操作未完成："+e.Error.Message);
                install.Enabled=restore.Enabled=browse.Enabled=path.Enabled=true;UseWaitCursor=false;
            };
            worker.RunWorkerAsync();
        }
    }

    public sealed class ArchivePlan
    {
        public string kind, file, original_sha256, original_row, new_row, payload_resource, payload_sha256, sha256;
        public long original_size, row_offset, append_offset, size;
        public int payload_length, edited_strings;
        public long[] legacy_sizes;
        public string[] legacy_sha256;
    }
    public sealed class NativeManifest
    {
        public string version, method;
        public int edited_strings, records, missing_targets_not_inserted;
        public ArchivePlan[] archives;
        public string[] known_patch02;
        public string[] prior_versions;
    }
    public sealed class NativeState
    {
        public int format = 1;
        public string target, version, phase, message, utc;
    }
    internal sealed class Item
    {
        internal ArchivePlan Plan;
        internal string Target, Backup, CurrentHash, Stage;
        internal bool BackupExists;
    }
    internal sealed class Engine
    {
        internal Action<string> Progress;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 };
        private NativeManifest manifest;
        private readonly Dictionary<string, byte[]> payloads = new Dictionary<string, byte[]>();
        private string target, backupRoot, statePath;
        private readonly List<Item> items = new List<Item>();
        private DateTime nextGameCheck = DateTime.MinValue;
        private void Log(string text) { if (Progress != null) Progress(text); }
        private static bool Same(string a, string b) { return String.Equals(a,b,StringComparison.OrdinalIgnoreCase); }
        private static byte[] Unhex(string text)
        {
            if (text == null || text.Length % 2 != 0) throw new InvalidOperationException("封裝資料格式錯誤。");
            byte[] b = new byte[text.Length / 2];
            for (int i=0;i<b.Length;i++) b[i]=Convert.ToByte(text.Substring(i*2,2),16);
            return b;
        }
        private static string Hex(byte[] b) { return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant(); }
        private static string HashBytes(byte[] b) { using (var h=SHA256.Create()) return Hex(h.ComputeHash(b)); }
        private static string HashAll(Stream s)
        {
            s.Position=0;
            using (var h=SHA256.Create())
            {
                // Keep SHA-256 and every existing verification; reduce tiny stream reads.
                byte[] block=new byte[(int)Math.Min(8L*1024*1024,Math.Max(65536L,s.Length))];int n;
                while((n=s.Read(block,0,block.Length))!=0) h.TransformBlock(block,0,n,null,0);
                h.TransformFinalBlock(new byte[0],0,0);return Hex(h.Hash);
            }
        }
        private static string HashPath(string p) { using (var s=Open(p,FileAccess.Read,FileMode.Open)) return HashAll(s); }
        private static byte[] Resource(string name)
        {
            using (var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if(stream==null) throw new InvalidOperationException("找不到內嵌資料："+name);
                using(var memory=new MemoryStream()) { stream.CopyTo(memory);return memory.ToArray(); }
            }
        }
        private void Load()
        {
            manifest=json.Deserialize<NativeManifest>(Encoding.UTF8.GetString(Resource("manifest.json")));
            if(manifest.method!="native-append-repoint" || manifest.archives==null || manifest.archives.Length!=2)
                throw new InvalidOperationException("封裝清單不符。");
            var seen=new HashSet<string>();
            foreach(var p in manifest.archives)
            {
                if((p.file!="DataPC_boot.forge" && p.file!="DataPC_boot_patch_01.forge") || !seen.Add(p.file))
                    throw new InvalidOperationException("封裝檔案名稱不符。");
                if(p.row_offset<0 || p.row_offset+24>p.original_size || p.append_offset<p.original_size
                    || p.append_offset-p.original_size>65535 || p.size<p.append_offset+p.payload_length
                    || p.size-p.append_offset-p.payload_length>65535 || p.payload_length<1
                    || Unhex(p.original_row).Length!=24 || Unhex(p.new_row).Length!=24)
                    throw new InvalidOperationException("封裝位置不符。");
                if(!Regex.IsMatch(p.original_sha256??"","^[0-9a-f]{64}$") || !Regex.IsMatch(p.sha256??"","^[0-9a-f]{64}$"))
                    throw new InvalidOperationException("封裝雜湊不符。");
                byte[] raw=Resource(p.payload_resource);
                if(raw.Length!=p.payload_length || !Same(HashBytes(raw),p.payload_sha256))
                    throw new InvalidOperationException("內嵌文字資料核對失敗。");
                payloads.Add(p.file,raw);
            }
        }
        private void NoGame()
        {
            foreach(var process in Process.GetProcessesByName("ACBlackFlag"))
            { process.Dispose(); throw new InvalidOperationException("請先完全退出 Black Flag，再執行安裝或還原。"); }
        }
        private void PeriodicGameCheck()
        {
            if(DateTime.UtcNow>=nextGameCheck) { NoGame();nextGameCheck=DateTime.UtcNow.AddSeconds(2); }
        }
        private void State(string phase,string message)
        {
            string temporary=statePath+".new"; NoReparse(temporary);
            if(File.Exists(temporary)) throw new InvalidOperationException("有未完成的記錄暫存檔，已保留供檢查："+temporary);
            byte[] raw=Encoding.UTF8.GetBytes(json.Serialize(new NativeState { target=target,version=manifest.version,phase=phase,message=message,utc=DateTime.UtcNow.ToString("o") }));
            using(var stream=Open(temporary,FileAccess.ReadWrite,FileMode.CreateNew)) { stream.Write(raw,0,raw.Length);stream.Flush(true); }
            if(File.Exists(statePath)) File.Replace(temporary,statePath,null); else File.Move(temporary,statePath);
        }
        private bool Known(ArchivePlan p,string hash,long length)
        {
            if(length==p.original_size && Same(hash,p.original_sha256)) return true;
            if(length==p.size && Same(hash,p.sha256)) return true;
            if(p.legacy_sizes!=null && p.legacy_sha256!=null && p.legacy_sizes.Length==p.legacy_sha256.Length)
                for(int i=0;i<p.legacy_sizes.Length;i++) if(length==p.legacy_sizes[i] && Same(hash,p.legacy_sha256[i])) return true;
            return false;
        }
        private void Discover()
        {
            foreach(var p in manifest.archives)
            {
                NoGame(); Log("核對原檔及現有版本："+p.file);
                string file=Path.Combine(target,p.file); NoReparse(file);
                if(!File.Exists(file)) throw new InvalidOperationException("缺少遊戲檔案："+p.file);
                string hash=HashPath(file); long size=new FileInfo(file).Length;
                if(!Known(p,hash,size)) throw new InvalidOperationException("遊戲檔案版本不符或含其他修改，已停止並保留現況："+p.file);
                string local=Path.Combine(backupRoot,p.file+".original");
                string previous=Path.Combine(Path.Combine(target,".acbf-native-originals-20261107"),p.file+".original");
                string selected=null;
                foreach(string candidate in new string[] { local,previous })
                {
                    NoReparse(Path.GetDirectoryName(candidate)); NoReparse(candidate);
                    if(!File.Exists(candidate)) continue;
                    Log("核對完整原檔備份："+p.file);
                    if(new FileInfo(candidate).Length!=p.original_size || !Same(HashPath(candidate),p.original_sha256))
                        throw new InvalidOperationException("原檔備份不符，已保留供檢查："+candidate);
                    if(selected==null) selected=candidate;
                }
                bool hasBackup=selected!=null;
                if(!hasBackup && !Same(hash,p.original_sha256)) throw new InvalidOperationException("找不到已核實的完整原檔備份，不能處理舊候選版："+p.file);
                items.Add(new Item { Plan=p,Target=file,Backup=selected??local,BackupExists=hasBackup,
                    CurrentHash=hash,Stage=file+".zhHK-"+manifest.version+"-stage" });
            }
        }
        private void CheckLegacy()
        {
            string file=Path.Combine(target,"DataPC_boot_patch_02.forge");NoReparse(file);
            if(!File.Exists(file)) return;
            string hash=HashPath(file);
            if(Array.FindIndex(manifest.known_patch02,h=>Same(h,hash))<0)
                throw new InvalidOperationException("發現其他版本的 patch_02，已保留並停止；請先處理該補丁。");
        }
        private void QuarantineLegacy()
        {
            string file=Path.Combine(target,"DataPC_boot_patch_02.forge");
            if(!File.Exists(file)) return;
            CheckLegacy();NoGame();
            string destination=Path.Combine(backupRoot,"old-patch02-"+Guid.NewGuid().ToString("N")+".disabled");
            File.Move(file,destination); Log("舊版獨立補丁已備份停用。");
        }
        private void CheckSpace(bool restoring)
        {
            long needed=128L*1024*1024, reserve=0;
            foreach(var item in items)
            {
                var p=item.Plan;
                if(!restoring) reserve+=p.original_size;
                if(Same(item.CurrentHash,restoring?p.original_sha256:p.sha256)) continue;
                NoReparse(item.Stage);
                string prior=item.Target+".native-restore-stage";NoReparse(prior);
                if(!File.Exists(item.Stage) && !File.Exists(prior)) needed+=restoring?p.original_size:p.size;
            }
            long available=new DriveInfo(Path.GetPathRoot(target)).AvailableFreeSpace;
#if SANDBOX_TEST
            string simulatedSpace=Environment.GetEnvironmentVariable("ACBF_TEST_AVAILABLE_BYTES");
            if(simulatedSpace!=null) available=Int64.Parse(simulatedSpace);
#endif
            if(available<needed+reserve) throw new InvalidOperationException("磁碟空間不足。暫存及還原預留共需約 "+Math.Ceiling((needed+reserve)/1073741824.0)+" GB。");
        }
        private void CopyOriginal(string source,string destination,ArchivePlan p)
        {
            using(var src=Open(source,FileAccess.Read,FileMode.Open))
            using(var dst=Open(destination,FileAccess.ReadWrite,FileMode.CreateNew))
            using(var digest=SHA256.Create())
            {
                byte[] block=new byte[8*1024*1024];int n;long copied=0;DateTime next=DateTime.MinValue;
                while((n=src.Read(block,0,block.Length))!=0)
                {
                    PeriodicGameCheck(); dst.Write(block,0,n);digest.TransformBlock(block,0,n,null,0);copied+=n;
                    if(DateTime.UtcNow>=next) { Log("準備 "+p.file+"："+(copied*100/p.original_size)+"%");next=DateTime.UtcNow.AddSeconds(5); }
                }
                digest.TransformFinalBlock(new byte[0],0,0);dst.Flush(true);
                if(copied!=p.original_size || !Same(Hex(digest.Hash),p.original_sha256))
                    throw new InvalidOperationException("複製原檔時核對失敗，暫存檔已保留。");
            }
        }
        private void PatchStage(Item item)
        {
            var p=item.Plan;
            using(var s=Open(item.Stage,FileAccess.ReadWrite,FileMode.Open))
            {
                if(s.Length!=p.original_size) throw new InvalidOperationException("暫存原檔大小不符。");
                s.SetLength(p.size);s.Position=p.append_offset;
                byte[] raw=payloads[p.file];s.Write(raw,0,raw.Length);
                s.Position=p.row_offset;byte[] row=Unhex(p.new_row);s.Write(row,0,row.Length);s.Flush(true);
            }
        }
        private void Prepare(Item item,bool restoring)
        {
            var p=item.Plan;string expected=restoring?p.original_sha256:p.sha256;
            if(File.Exists(item.Stage))
            {
                string hash=HashPath(item.Stage);
                if(Same(hash,expected)) return;
                if(!restoring && Same(hash,p.original_sha256)) { PatchStage(item); }
                else throw new InvalidOperationException("已有其他暫存檔，已保留供檢查："+item.Stage);
            }
            else
            {
                string previous=item.Target+".native-restore-stage";NoReparse(previous);
                if(File.Exists(previous))
                {
                    if(new FileInfo(previous).Length!=p.original_size || !Same(HashPath(previous),p.original_sha256))
                        throw new InvalidOperationException("上次還原暫存檔不符，已保留供檢查："+previous);
                    NoGame();File.Move(previous,item.Stage);
                }
                else CopyOriginal(item.BackupExists?item.Backup:item.Target,item.Stage,p);
                if(!restoring) PatchStage(item);
            }
            Log("核對準備完成的檔案："+p.file);
            if(!Same(HashPath(item.Stage),expected)) throw new InvalidOperationException("準備完成的檔案核對失敗，未替換原檔。");
        }
        private void VerifyUnchangedTargets()
        {
            foreach(var item in items)
            {
                NoGame(); Log("確認遊戲原檔未被其他程式修改："+item.Plan.file);
                if(!Same(HashPath(item.Target),item.CurrentHash)) throw new InvalidOperationException("準備期間遊戲檔案被修改，已停止："+item.Plan.file);
            }
        }
        private void Commit(bool restoring)
        {
            foreach(var item in items)
            {
                var p=item.Plan;string expected=restoring?p.original_sha256:p.sha256;
                if(Same(item.CurrentHash,expected)) continue;
                NoGame();Log((restoring?"還原：":"安裝：")+p.file);
                if(!restoring && !item.BackupExists)
                { File.Replace(item.Stage,item.Target,item.Backup);item.BackupExists=true; }
                else File.Replace(item.Stage,item.Target,null);
                item.CurrentHash=expected;
                if(!Same(HashPath(item.Target),expected)) throw new InvalidOperationException("替換後核對失敗："+p.file);
#if SANDBOX_TEST
                if(Environment.GetEnvironmentVariable("ACBF_TEST_FAIL_AFTER_COMMIT")==p.kind)
                    throw new InvalidOperationException("Synthetic commit failure");
#endif
            }
        }
        private void RestoreAll()
        {
            NoGame();
            foreach(var item in items)
            {
                item.CurrentHash=HashPath(item.Target);
                if(!Known(item.Plan,item.CurrentHash,new FileInfo(item.Target).Length))
                    throw new InvalidOperationException("檔案在安裝後被其他程式修改，保留檔案及備份："+item.Plan.file);
                if(Same(item.CurrentHash,item.Plan.original_sha256)) continue;
                if(!item.BackupExists || !Same(HashPath(item.Backup),item.Plan.original_sha256))
                    throw new InvalidOperationException("還原所需的完整備份不符。");
            }
            CheckSpace(true);
            foreach(var item in items) if(!Same(item.CurrentHash,item.Plan.original_sha256)) Prepare(item,true);
            VerifyUnchangedTargets();
            foreach(var item in items)
            {
                if(Same(item.CurrentHash,item.Plan.original_sha256)) continue;
                NoGame(); File.Replace(item.Stage,item.Target,null);
                if(!Same(HashPath(item.Target),item.Plan.original_sha256)) throw new InvalidOperationException("還原後核對失敗。");
                item.CurrentHash=item.Plan.original_sha256;
            }
        }
        internal string Run(string directory,bool restoring)
        {
            Load(); target=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar);
#if SANDBOX_TEST
            string boundary=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sandbox"))+Path.DirectorySeparatorChar;
            if(!target.StartsWith(boundary,StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("測試版只接受指定的 D 槽 sandbox。");
#endif
            if(!Directory.Exists(target) || !File.Exists(Path.Combine(target,"ACBlackFlag.exe"))) throw new InvalidOperationException("請選擇含 ACBlackFlag.exe 的遊戲資料夾。");
            NoGame();
            using(var handles=new HandleSet())
            {
                LockDirectoryChain(target,handles);
                backupRoot=Path.Combine(target,".acbf-zhhk-native");NoReparse(backupRoot);
                Directory.CreateDirectory(backupRoot);LockDirectory(backupRoot,handles);
                statePath=Path.Combine(backupRoot,"state.json");NoReparse(statePath);
                if(File.Exists(statePath))
                {
                    var old=json.Deserialize<NativeState>(File.ReadAllText(statePath,Encoding.UTF8));
                    if(old.format!=1 || !Same(old.target,target) || (old.version!=manifest.version && (manifest.prior_versions==null || Array.IndexOf(manifest.prior_versions,old.version)<0)))
                        throw new InvalidOperationException("安裝記錄不符，已保留供檢查。");
                }
                using(var operation=Open(Path.Combine(backupRoot,"operation.lock"),FileAccess.ReadWrite,FileMode.OpenOrCreate))
                {
                    CheckLegacy();Discover();CheckSpace(restoring);
                    if(restoring)
                    {
                        State("restoring","Preparing both complete original archives.");
                        RestoreAll();QuarantineLegacy();State("restored","Both full original archives verified; failed patch02 stays disabled.");
                        return "原遊戲檔案已完整還原並核對。\r\n完整原檔備份已保留，舊版獨立補丁維持停用。";
                    }
                    bool committing=false;
                    try
                    {
                        State("preparing","Preparing and verifying both native archives before replacement.");
                        foreach(var item in items) if(!Same(item.CurrentHash,item.Plan.sha256)) Prepare(item,false);
                        VerifyUnchangedTargets();NoGame();
                        State("committing","Full original backups will be retained before each atomic replacement.");
                        QuarantineLegacy();committing=true;Commit(false);
                        State("installed","Both native archives verified against the new package; full original backups retained.");
                        return "新版繁中香港包已安裝，兩個封裝均核對完成。\r\n11,800 條修訂已納入；93 條缺失目標建議不插入資源。\r\n請自行啟動遊戲，確認選單及字幕。完整原檔備份已保留。";
                    }
                    catch(Exception error)
                    {
                        if(committing)
                        {
                            try { State("rolling_back",error.Message);RestoreAll();State("failed_restored",error.Message); }
                            catch(Exception restoreError)
                            { State("restore_attention_required",error.Message+" | "+restoreError.Message);throw new InvalidOperationException("安裝未完成；還原暫停。完整備份已保留。\r\n"+restoreError.Message,error); }
                            throw new InvalidOperationException("安裝未完成，原檔已完整還原。\r\n"+error.Message,error);
                        }
                        State("failed_before_commit",error.Message);throw;
                    }
                }
            }
        }
        private const uint Reparse = 0x400, DirectoryAttribute = 0x10;
        private static void NoReparse(string path)
        {
            try { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("拒絕符號連結、junction 或其他 reparse point：" + path); }
            catch (FileNotFoundException) { } catch (DirectoryNotFoundException) { }
        }
        private static void LockDirectoryChain(string directory, HandleSet handles)
        {
            var directories = new Stack<string>(); string current = directory;
            while (!String.IsNullOrEmpty(current)) { directories.Push(current); var parent = Directory.GetParent(current); current = parent == null ? null : parent.FullName; }
            while (directories.Count != 0) LockDirectory(directories.Pop(), handles);
        }
        private static void LockDirectory(string path, HandleSet handles)
        {
            NoReparse(path);
            SafeFileHandle handle = Native.CreateFile(path, 0, 3, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
            if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new IOException("無法鎖定資料夾：" + path + " (Win32 " + error + ")"); }
            Native.ByHandle info;
            if (!Native.GetFileInformationByHandle(handle, out info) || (info.Attributes & Reparse) != 0 || (info.Attributes & DirectoryAttribute) == 0)
            { handle.Dispose(); throw new InvalidOperationException("資料夾是 reparse point 或無法驗證：" + path); }
            handles.Add(handle);
        }
        private static FileStream Open(string path, FileAccess access, FileMode mode)
        {
            NoReparse(path);
            uint desired = access == FileAccess.Read ? 0x80000000 : 0xC0000000;
            uint disposition = mode == FileMode.CreateNew ? 1U : mode == FileMode.OpenOrCreate ? 4U : 3U;
            SafeFileHandle handle = Native.CreateFile(path, desired, 0, IntPtr.Zero, disposition, 0x00200000, IntPtr.Zero);
            if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new IOException("無法排他開啟檔案，可能正被使用、缺少或權限不足：" + path + " (Win32 " + error + ")"); }
            Native.ByHandle info;
            if (!Native.GetFileInformationByHandle(handle, out info) || ((info.Attributes & (Reparse | DirectoryAttribute)) != 0 || (access != FileAccess.Read && info.Links != 1)))
            { handle.Dispose(); throw new InvalidOperationException("檔案是 reparse point、資料夾或無法驗證：" + path); }
            return new FileStream(handle, access);
        }
    }
    internal sealed class HandleSet : IDisposable
    {
        private readonly List<IDisposable> handles = new List<IDisposable>();
        internal void Add(IDisposable handle) { handles.Add(handle); }
        public void Dispose() { for (int i = handles.Count - 1; i >= 0; --i) handles[i].Dispose(); }
    }
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct ByHandle
        {
            internal uint Attributes; internal System.Runtime.InteropServices.ComTypes.FILETIME Creation, Access, Write;
            internal uint Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetFileInformationByHandle(SafeFileHandle handle, out ByHandle info);
    }
}
