using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AntigravityPersian
{
    public static class AsarHelper
    {
        public static void ExtractAll(string asarPath, string outDir)
        {
            using (var fs = new FileStream(asarPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(fs))
            {
                br.ReadUInt32();
                br.ReadUInt32();
                br.ReadUInt32();
                uint headerSize = br.ReadUInt32();
                byte[] headerBytes = br.ReadBytes((int)headerSize);
                string json = Encoding.UTF8.GetString(headerBytes);

                var jss = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = jss.Deserialize<Dictionary<string, object>>(json);
                long baseOffset = 16 + headerSize;

                ExtractDict((Dictionary<string, object>)root["files"], fs, baseOffset, outDir);
            }
        }

        private static void ExtractDict(Dictionary<string, object> files, FileStream fs, long baseOffset, string currentDir)
        {
            if (!Directory.Exists(currentDir)) Directory.CreateDirectory(currentDir);

            foreach (var kvp in files)
            {
                string name = kvp.Key;
                var info = kvp.Value as Dictionary<string, object>;
                if (info == null) continue;

                string fullPath = Path.Combine(currentDir, name);

                if (info.ContainsKey("files"))
                {
                    ExtractDict((Dictionary<string, object>)info["files"], fs, baseOffset, fullPath);
                }
                else if (info.ContainsKey("size"))
                {
                    if (info.ContainsKey("unpacked") && Convert.ToBoolean(info["unpacked"]))
                    {
                        continue;
                    }

                    long size = Convert.ToInt64(info["size"]);
                    long offset = Convert.ToInt64(info["offset"]);

                    string dir = Path.GetDirectoryName(fullPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    fs.Seek(baseOffset + offset, SeekOrigin.Begin);
                    using (var outFs = new FileStream(fullPath, FileMode.Create, FileAccess.Write))
                    {
                        byte[] buffer = new byte[Math.Min(65536, size)];
                        long remaining = size;
                        while (remaining > 0)
                        {
                            int read = fs.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                            if (read <= 0) break;
                            outFs.Write(buffer, 0, read);
                            remaining -= read;
                        }
                    }
                }
            }
        }

        public static void PackAll(string srcDir, string asarPath)
        {
            var filesList = new List<Tuple<string, Dictionary<string, object>>>();
            var rootFiles = BuildTree(srcDir, filesList);

            long curOffset = 0;
            foreach (var item in filesList)
            {
                item.Item2["offset"] = curOffset.ToString();
                curOffset += Convert.ToInt64(item.Item2["size"]);
            }

            var rootObj = new Dictionary<string, object> { { "files", rootFiles } };
            var jss = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            string json = jss.Serialize(rootObj);
            byte[] headerBytes = Encoding.UTF8.GetBytes(json);
            uint headerSize = (uint)headerBytes.Length;

            using (var outFs = new FileStream(asarPath, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(outFs))
            {
                bw.Write((uint)4);
                bw.Write((uint)(headerSize + 8));
                bw.Write((uint)(headerSize + 4));
                bw.Write((uint)(headerSize));
                bw.Write(headerBytes);

                byte[] buffer = new byte[65536];
                foreach (var item in filesList)
                {
                    long size = Convert.ToInt64(item.Item2["size"]);
                    if (size == 0) continue;

                    using (var inFs = new FileStream(item.Item1, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        int read;
                        while ((read = inFs.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            bw.Write(buffer, 0, read);
                        }
                    }
                }
            }
        }

        private static Dictionary<string, object> BuildTree(string dir, List<Tuple<string, Dictionary<string, object>>> filesList)
        {
            var res = new Dictionary<string, object>();
            var dirInfo = new DirectoryInfo(dir);

            var dirs = new List<DirectoryInfo>(dirInfo.GetDirectories());
            dirs.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            var files = new List<FileInfo>(dirInfo.GetFiles());
            files.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

            foreach (var sub in dirs)
            {
                var subFiles = BuildTree(sub.FullName, filesList);
                res[sub.Name] = new Dictionary<string, object> { { "files", subFiles } };
            }

            foreach (var f in files)
            {
                var fEntry = new Dictionary<string, object>
                {
                    { "size", f.Length },
                    { "offset", "0" }
                };
                res[f.Name] = fEntry;
                filesList.Add(Tuple.Create(f.FullName, fEntry));
            }

            return res;
        }

        public static bool CheckIfEngineInstalled(string asarPath)
        {
            try
            {
                if (!File.Exists(asarPath)) return false;
                using (var fs = new FileStream(asarPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var br = new BinaryReader(fs))
                {
                    br.ReadUInt32(); br.ReadUInt32(); br.ReadUInt32();
                    uint headerSize = br.ReadUInt32();
                    byte[] headerBytes = br.ReadBytes((int)headerSize);
                    string json = Encoding.UTF8.GetString(headerBytes);

                    var jss = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                    var root = jss.Deserialize<Dictionary<string, object>>(json);
                    var files = (Dictionary<string, object>)root["files"];
                    if (!files.ContainsKey("dist")) return false;
                    var dist = (Dictionary<string, object>)((Dictionary<string, object>)files["dist"])["files"];
                    if (!dist.ContainsKey("preload.js")) return false;

                    var preloadInfo = (Dictionary<string, object>)dist["preload.js"];
                    long size = Convert.ToInt64(preloadInfo["size"]);
                    long offset = Convert.ToInt64(preloadInfo["offset"]);
                    long baseOffset = 16 + headerSize;

                    fs.Seek(baseOffset + offset, SeekOrigin.Begin);
                    byte[] pBytes = new byte[size];
                    fs.Read(pBytes, 0, (int)size);
                    string content = Encoding.UTF8.GetString(pBytes);

                    return content.Contains("ANTIGRAVITY-PERSIAN-RTL-ENGINE");
                }
            }
            catch
            {
                return false;
            }
        }
    }

    public class MainForm : Form
    {
        private TextBox txtPath;
        private Button btnBrowse;
        private Button btnInstall;
        private Button btnRestart;
        private Button btnRestore;
        private RichTextBox txtLog;
        private Label lblStatus;
        private ComboBox cboFont;
        private Label lblFontPreview;
        private Button btnInstallFonts;
        private PrivateFontCollection pfc = new PrivateFontCollection();
        private bool isAlreadyInstalled = false;
        private const string EMBEDDED_LOGO_B64 = "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";

        public MainForm()
        {
            InitUI();
            LoadCustomFonts();
            DetectPath();
        }

        private void LoadCustomFonts()
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string fontsDir = Path.Combine(appDir, "Fonts");
                if (Directory.Exists(fontsDir))
                {
                    var files = Directory.GetFiles(fontsDir, "*.ttf", SearchOption.AllDirectories);
                    foreach (var f in files)
                    {
                        try { pfc.AddFontFile(f); } catch { }
                    }
                }
            }
            catch { }
        }

        private void InitUI()
        {
            this.Text = "نصاب هوشمند راست‌چین و فارسی‌ساز آنتی‌گراویتی | Antigravity Persian RTL";
            this.Size = new Size(710, 640);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.BackColor = Color.FromArgb(24, 24, 37);
            this.ForeColor = Color.FromArgb(205, 214, 244);
            this.Font = new Font("Segoe UI", 9.5f);
            this.RightToLeft = RightToLeft.Yes;
            this.RightToLeftLayout = true;

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 75,
                BackColor = Color.FromArgb(30, 30, 46),
                Padding = new Padding(15)
            };

            // Embedded Logo
            try
            {
                byte[] logoBytes = Convert.FromBase64String(EMBEDDED_LOGO_B64);
                using (var ms = new MemoryStream(logoBytes))
                {
                    var picLogo = new PictureBox
                    {
                        Image = Image.FromStream(ms),
                        SizeMode = PictureBoxSizeMode.Zoom,
                        Size = new Size(50, 50),
                        Location = new Point(15, 12)
                    };
                    header.Controls.Add(picLogo);
                }
            }
            catch { }

            try
            {
                this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch { }

            var lblTitle = new Label
            {
                Text = "🚀 نصاب هوشمند محیط فارسی و انتخاب قلم آنتی‌گراویتی",
                Font = new Font("Segoe UI", 12f, FontStyle.Bold),
                ForeColor = Color.FromArgb(137, 180, 250),
                AutoSize = true,
                Location = new Point(75, 12)
            };
            var lblSub = new Label
            {
                Text = "نسخه ۳.۵ پایدار • انتخاب و پیش‌نمایش فونت • سوییچر داخل برنامه • کلید میانبر امن Alt+Shift+R",
                Font = new Font("Segoe UI", 8.5f),
                ForeColor = Color.FromArgb(166, 173, 200),
                AutoSize = true,
                Location = new Point(75, 42)
            };
            header.Controls.Add(lblTitle);
            header.Controls.Add(lblSub);
            this.Controls.Add(header);

            // Path Panel
            var pnlPath = new Panel
            {
                Location = new Point(15, 80),
                Size = new Size(665, 50)
            };
            var lblPath = new Label
            {
                Text = "مسیر فایل اصلی برنامه (app.asar):",
                Location = new Point(0, 3),
                AutoSize = true
            };
            txtPath = new TextBox
            {
                Location = new Point(90, 22),
                Size = new Size(575, 25),
                BackColor = Color.FromArgb(49, 50, 68),
                ForeColor = Color.FromArgb(205, 214, 244),
                BorderStyle = BorderStyle.FixedSingle,
                ReadOnly = true,
                RightToLeft = RightToLeft.No
            };
            btnBrowse = new Button
            {
                Text = "انتخاب...",
                Location = new Point(0, 21),
                Size = new Size(80, 27),
                BackColor = Color.FromArgb(69, 71, 90),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            btnBrowse.Click += (s, e) => BrowsePath();
            pnlPath.Controls.Add(lblPath);
            pnlPath.Controls.Add(txtPath);
            pnlPath.Controls.Add(btnBrowse);
            this.Controls.Add(pnlPath);

            // Font Selection & Live Preview Panel
            var pnlFont = new Panel
            {
                Location = new Point(15, 133),
                Size = new Size(665, 82),
                BackColor = Color.FromArgb(30, 30, 46),
                Padding = new Padding(10)
            };

            var lblFontTitle = new Label
            {
                Text = "🎨 انتخاب فونت پیش‌فرض فارسی:",
                Location = new Point(460, 8),
                Size = new Size(195, 20),
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(137, 180, 250)
            };

            cboFont = new ComboBox
            {
                Location = new Point(230, 6),
                Size = new Size(225, 26),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = Color.FromArgb(49, 50, 68),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            cboFont.Items.Add("وزیرمتن (Vazirmatn) - پیش‌فرض و استاندارد");
            cboFont.Items.Add("استودیو (Studio / SD) - بولد و مدرن");
            cboFont.Items.Add("ری (Ray) - هندسی و خاص");
            cboFont.Items.Add("صمیم (Samim) - کلاسیک و خوانا");
            cboFont.SelectedIndex = 0;
            cboFont.SelectedIndexChanged += (s, e) => UpdateFontPreview();

            btnInstallFonts = new Button
            {
                Text = "📥 نصب فونت‌ها در ویندوز",
                Location = new Point(10, 5),
                Size = new Size(210, 28),
                BackColor = Color.FromArgb(69, 71, 90),
                ForeColor = Color.FromArgb(205, 214, 244),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnInstallFonts.Click += (s, e) => InstallFontsToWindows(true);

            lblFontPreview = new Label
            {
                Text = "پیش‌نمایش قلم: محیط کاملاً فارسی و راست‌چین با سرعت بالا ۱۲۳۴۵۶۷۸۹۰ 🚀 (Antigravity Persian RTL)",
                Location = new Point(10, 40),
                Size = new Size(645, 34),
                BackColor = Color.FromArgb(17, 17, 27),
                ForeColor = Color.FromArgb(166, 227, 161),
                Font = new Font("Segoe UI", 10.5f),
                TextAlign = ContentAlignment.MiddleCenter,
                BorderStyle = BorderStyle.FixedSingle
            };

            pnlFont.Controls.Add(lblFontTitle);
            pnlFont.Controls.Add(cboFont);
            pnlFont.Controls.Add(btnInstallFonts);
            pnlFont.Controls.Add(lblFontPreview);
            this.Controls.Add(pnlFont);

            // Dynamic Status Banner
            lblStatus = new Label
            {
                Location = new Point(15, 220),
                Size = new Size(665, 26),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleLeft,
                BackColor = Color.FromArgb(30, 30, 46),
                Padding = new Padding(8, 0, 8, 0)
            };
            this.Controls.Add(lblStatus);

            // Action Buttons
            btnInstall = new Button
            {
                Text = "✨ نصب و فعال‌سازی فارسی‌ساز (یک کلیک)",
                Location = new Point(385, 252),
                Size = new Size(295, 42),
                BackColor = Color.FromArgb(166, 227, 161),
                ForeColor = Color.FromArgb(17, 17, 27),
                Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnInstall.Click += (s, e) => PerformInstall();
            this.Controls.Add(btnInstall);

            btnRestart = new Button
            {
                Text = "🔄 راه‌اندازی مجدد برنامه",
                Location = new Point(200, 252),
                Size = new Size(175, 42),
                BackColor = Color.FromArgb(137, 180, 250),
                ForeColor = Color.FromArgb(17, 17, 27),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnRestart.Click += (s, e) => RestartAntigravity();
            this.Controls.Add(btnRestart);

            btnRestore = new Button
            {
                Text = "↩️ بازگردانی نسخه اصلی (حذف)",
                Location = new Point(15, 252),
                Size = new Size(175, 42),
                BackColor = Color.FromArgb(243, 139, 168),
                ForeColor = Color.FromArgb(17, 17, 27),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand
            };
            btnRestore.Click += (s, e) => PerformRestore();
            this.Controls.Add(btnRestore);

            txtLog = new RichTextBox
            {
                Location = new Point(15, 302),
                Size = new Size(665, 285),
                BackColor = Color.FromArgb(17, 17, 27),
                ForeColor = Color.FromArgb(166, 227, 161),
                Font = new Font("Consolas", 9.5f),
                BorderStyle = BorderStyle.None,
                ReadOnly = true,
                RightToLeft = RightToLeft.No
            };
            this.Controls.Add(txtLog);
        }

        private void UpdateFontPreview()
        {
            string fontName = GetSelectedFontFamily();
            try
            {
                // Try from private font collection or system
                Font foundFont = null;
                foreach (var fam in pfc.Families)
                {
                    if (fam.Name.IndexOf(fontName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        fontName.IndexOf(fam.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        foundFont = new Font(fam, 11f);
                        break;
                    }
                }
                if (foundFont == null)
                {
                    foundFont = new Font(fontName, 11f);
                }
                lblFontPreview.Font = foundFont;
            }
            catch
            {
                lblFontPreview.Font = new Font("Segoe UI", 11f);
            }
        }

        private string GetSelectedFontKey()
        {
            switch (cboFont.SelectedIndex)
            {
                case 1: return "studio";
                case 2: return "ray";
                case 3: return "samim";
                default: return "vazir";
            }
        }

        private string GetSelectedFontFamily()
        {
            switch (cboFont.SelectedIndex)
            {
                case 1: return "SD-Studio";
                case 2: return "Ray";
                case 3: return "Samim";
                default: return "Vazirmatn";
            }
        }

        private void InstallFontsToWindows(bool showMessage)
        {
            try
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string fontsDir = Path.Combine(appDir, "Fonts");
                if (!Directory.Exists(fontsDir))
                {
                    if (showMessage) MessageBox.Show("پوشه Fonts در کنار برنامه یافت نشد!", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string targetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Fonts");
                if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

                var ttfFiles = Directory.GetFiles(fontsDir, "*.ttf", SearchOption.AllDirectories);
                int installedCount = 0;

                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Fonts", true))
                {
                    foreach (var file in ttfFiles)
                    {
                        string fileName = Path.GetFileName(file);
                        string dest = Path.Combine(targetDir, fileName);
                        try
                        {
                            File.Copy(file, dest, true);
                            if (key != null)
                            {
                                string fontKeyName = Path.GetFileNameWithoutExtension(file) + " (TrueType)";
                                key.SetValue(fontKeyName, dest);
                            }
                            installedCount++;
                        }
                        catch { }
                    }
                }

                Log(string.Format("تعداد {0} فایل فونت با موفقیت در ویندوز ثبت و نصب شد.", installedCount), Color.Cyan);
                if (showMessage)
                {
                    MessageBox.Show(string.Format("تعداد {0} فونت فارسی با موفقیت در ویندوز نصب شدند!", installedCount), "نصب فونت‌ها", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                Log("خطا در نصب فونت‌ها در ویندوز: " + ex.Message, Color.Yellow);
            }
        }

        private void Log(string msg, Color? col = null)
        {
            txtLog.SelectionStart = txtLog.TextLength;
            txtLog.SelectionLength = 0;
            txtLog.SelectionColor = col ?? Color.FromArgb(166, 227, 161);
            txtLog.AppendText(string.Format("[{0:HH:mm:ss}] {1}\r\n", DateTime.Now, msg));
            txtLog.ScrollToCaret();
        }

        private void DetectPath()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string defAsar = Path.Combine(localApp, "Programs", "antigravity", "resources", "app.asar");

            if (File.Exists(defAsar))
            {
                txtPath.Text = defAsar;
                RefreshState();
            }
            else
            {
                lblStatus.Text = "⚠️ فایل app.asar در مسیر پیش‌فرض یافت نشد. لطفاً دکمه 'انتخاب...' را بزنید.";
                lblStatus.ForeColor = Color.Yellow;
                Log("فایل app.asar به صورت خودکار یافت نشد.", Color.Yellow);
            }
        }

        private void RefreshState()
        {
            string asar = txtPath.Text.Trim();
            if (!File.Exists(asar)) return;

            isAlreadyInstalled = AsarHelper.CheckIfEngineInstalled(asar);
            bool hasBackup = File.Exists(asar + ".backup");

            if (isAlreadyInstalled)
            {
                lblStatus.Text = "✅ وضعیت: فارسی‌ساز و راست‌چین در حال حاضر روی Antigravity نصب و فعال است.";
                lblStatus.ForeColor = Color.FromArgb(166, 227, 161);

                btnInstall.Text = "🔄 بروزرسانی / اعمال فونت جدید";
                btnInstall.BackColor = Color.FromArgb(137, 180, 250);

                btnRestore.Text = "↩️ بازگردانی به نسخه اصلی کارخانه";
                btnRestore.BackColor = Color.FromArgb(243, 139, 168);
                btnRestore.Enabled = true;

                Log("وضعیت: فارسی‌ساز روی برنامه نصب است.", Color.Lime);
            }
            else
            {
                lblStatus.Text = "⚪ وضعیت: نسخه اصلی و دست‌نخورده Antigravity فعال است (فارسی‌ساز نصب نیست).";
                lblStatus.ForeColor = Color.FromArgb(249, 226, 175);

                btnInstall.Text = "✨ نصب و فعال‌سازی فارسی‌ساز (یک کلیک)";
                btnInstall.BackColor = Color.FromArgb(166, 227, 161);

                btnRestore.Text = "↩️ بازگردانی نسخه اصلی";
                btnRestore.Enabled = hasBackup;

                Log("برنامه شناسایی شد. فونت مورد نظر را انتخاب و دکمه سبز را بزنید.");
            }
        }

        private void BrowsePath()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "ASAR Archives (*.asar)|*.asar|All Files (*.*)|*.*";
                ofd.Title = "انتخاب فایل app.asar برنامه Antigravity";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    txtPath.Text = ofd.FileName;
                    RefreshState();
                }
            }
        }

        private void KillAntigravity()
        {
            var procs = Process.GetProcessesByName("Antigravity");
            if (procs.Length > 0)
            {
                Log("در حال بستن پروسه‌های فعال Antigravity...");
                foreach (var p in procs)
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }
                Log("پروسه‌های برنامه با موفقیت بسته شدند.");
            }
        }

        private void PerformInstall()
        {
            string asarPath = txtPath.Text.Trim();
            if (!File.Exists(asarPath))
            {
                MessageBox.Show("مسیر فایل app.asar معتبر نیست!", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            try
            {
                btnInstall.Enabled = false;
                KillAntigravity();

                // Install fonts to Windows
                InstallFontsToWindows(false);

                string backupPath = asarPath + ".backup";
                if (!File.Exists(backupPath))
                {
                    Log("در حال ایجاد نسخه پشتیبان دائمی و امن (app.asar.backup)...");
                    File.Copy(asarPath, backupPath, true);
                    Log("بکاپ اولیه با موفقیت ثبت شد.");
                }

                string tempDir = Path.Combine(Path.GetTempPath(), "AG_Persian_" + Guid.NewGuid().ToString("N"));
                Log("در حال استخراج موقت فایل‌های برنامه...");
                AsarHelper.ExtractAll(asarPath, tempDir);

                string preloadPath = Path.Combine(tempDir, "dist", "preload.js");
                if (!File.Exists(preloadPath))
                {
                    throw new Exception("فایل dist/preload.js در آرشیو یافت نشد!");
                }

                string preloadContent = File.ReadAllText(preloadPath, Encoding.UTF8);
                string engineTag = "/* ANTIGRAVITY-PERSIAN-RTL-ENGINE-V3 */";

                if (preloadContent.Contains("ANTIGRAVITY-PERSIAN-RTL-ENGINE"))
                {
                    Log("در حال بروزرسانی موتور فارسی و اعمال فونت " + cboFont.SelectedItem + "...");
                    int idx = preloadContent.IndexOf("/* ANTIGRAVITY-PERSIAN-RTL-ENGINE");
                    if (idx >= 0) preloadContent = preloadContent.Substring(0, idx).TrimEnd();
                }

                string fontKey = GetSelectedFontKey();
                string injectedCode = engineTag + "\r\n" + PersianEngineSource.GetEngineCode(fontKey);
                preloadContent += "\r\n\r\n" + injectedCode;
                File.WriteAllText(preloadPath, preloadContent, Encoding.UTF8);
                Log("موتور کامل فارسی با قابلیت سوییچ فونت داخل برنامه تزریق شد.");

                Log("در حال بسته‌بندی مجدد app.asar...");
                string newAsarPath = asarPath + ".new";
                AsarHelper.PackAll(tempDir, newAsarPath);

                File.Delete(asarPath);
                File.Move(newAsarPath, asarPath);

                try { Directory.Delete(tempDir, true); } catch { }

                Log("--------------------------------------------------", Color.Cyan);
                Log("🎉 عملیات با موفقیت ۱۰۰٪ به پایان رسید!", Color.Lime);
                Log("فونت پیش‌فرض: " + cboFont.SelectedItem, Color.White);
                Log("تغییر فونت بعد از نصب: با دکمه «🎨 فونت» در بالای برنامه", Color.Yellow);
                Log("--------------------------------------------------", Color.Cyan);

                RefreshState();

                var r = MessageBox.Show(
                    "فارسی‌ساز و فونت با موفقیت اعمال شد!\n\n(می‌توانید داخل خود برنامه هم با دکمه 🎨 فونت را تغییر دهید)\n\nآیا می‌خواهید Antigravity اکنون اجرا شود؟",
                    "عملیات موفق",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information
                );

                if (r == DialogResult.Yes)
                {
                    RestartAntigravity();
                }
            }
            catch (Exception ex)
            {
                Log("خطا در هنگام نصب: " + ex.Message, Color.Red);
                MessageBox.Show("خطایی رخ داد:\n" + ex.Message, "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnInstall.Enabled = true;
            }
        }

        private void PerformRestore()
        {
            string asarPath = txtPath.Text.Trim();
            string backupPath = asarPath + ".backup";

            if (!File.Exists(backupPath))
            {
                MessageBox.Show("فایل پشتیبان (app.asar.backup) یافت نشد!", "خطا", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var res = MessageBox.Show(
                "آیا مطمئنید می‌خواهید فارسی‌ساز را حذف کرده و برنامه را دقیقاً به حالت اولیه کارخانه بازگردانید؟",
                "تایید بازگردانی",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );

            if (res != DialogResult.Yes) return;

            try
            {
                KillAntigravity();
                Log("در حال بازگردانی نسخه اصلی از روی فایل پشتیبان...");
                File.Copy(backupPath, asarPath, true);
                Log("نسخه اورجینال برنامه با موفقیت بازیابی شد.", Color.Lime);

                RefreshState();

                MessageBox.Show("برنامه با موفقیت به حالت اولیه کارخانه بازگردانده شد.", "بازگردانی انجام شد", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log("خطا در بازگردانی: " + ex.Message, Color.Red);
            }
        }

        public void PerformInstallHeadless()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string asarPath = Path.Combine(localApp, "Programs", "antigravity", "resources", "app.asar");
            if (!File.Exists(asarPath)) return;

            KillAntigravity();

            string backupPath = asarPath + ".backup";
            if (!File.Exists(backupPath))
            {
                File.Copy(asarPath, backupPath, true);
            }

            string tempDir = Path.Combine(Path.GetTempPath(), "AG_Persian_" + Guid.NewGuid().ToString("N"));
            AsarHelper.ExtractAll(asarPath, tempDir);

            string preloadPath = Path.Combine(tempDir, "dist", "preload.js");
            string preloadContent = File.ReadAllText(preloadPath, Encoding.UTF8);
            string engineTag = "/* ANTIGRAVITY-PERSIAN-RTL-ENGINE-V3 */";

            if (preloadContent.Contains("ANTIGRAVITY-PERSIAN-RTL-ENGINE"))
            {
                int idx = preloadContent.IndexOf("/* ANTIGRAVITY-PERSIAN-RTL-ENGINE");
                if (idx >= 0) preloadContent = preloadContent.Substring(0, idx).TrimEnd();
            }

            string injectedCode = engineTag + "\r\n" + PersianEngineSource.GetEngineCode("vazir");
            preloadContent += "\r\n\r\n" + injectedCode;
            File.WriteAllText(preloadPath, preloadContent, Encoding.UTF8);

            string newAsarPath = asarPath + ".new";
            AsarHelper.PackAll(tempDir, newAsarPath);

            File.Delete(asarPath);
            File.Move(newAsarPath, asarPath);

            try { Directory.Delete(tempDir, true); } catch { }
            Console.WriteLine("Headless installation completed successfully.");
        }

        public void PerformRestoreHeadless()
        {
            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string asarPath = Path.Combine(localApp, "Programs", "antigravity", "resources", "app.asar");
            string backupPath = asarPath + ".backup";
            if (File.Exists(backupPath))
            {
                KillAntigravity();
                File.Copy(backupPath, asarPath, true);
                Console.WriteLine("Headless restore completed successfully.");
            }
        }

        private void RestartAntigravity()
        {
            try
            {
                KillAntigravity();
                string asarPath = txtPath.Text.Trim();
                string progDir = Path.GetDirectoryName(Path.GetDirectoryName(asarPath));
                string exePath = Path.Combine(progDir, "Antigravity.exe");

                if (File.Exists(exePath))
                {
                    Log("در حال اجرای مجدد برنامه Antigravity...");
                    Process.Start(exePath);
                    Log("برنامه با موفقیت اجرا شد!");
                }
                else
                {
                    Log("فایل Antigravity.exe یافت نشد: " + exePath, Color.Yellow);
                }
            }
            catch (Exception ex)
            {
                Log("خطا در اجرای برنامه: " + ex.Message, Color.Red);
            }
        }

        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                string cmd = args[0].ToLower().TrimStart('-', '/');
                if (cmd == "install" || cmd == "silent")
                {
                    var form = new MainForm();
                    form.PerformInstallHeadless();
                    return;
                }
                if (cmd == "restore" || cmd == "uninstall")
                {
                    var form = new MainForm();
                    form.PerformRestoreHeadless();
                    return;
                }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public static class PersianEngineSource
    {
        public static string GetEngineCode(string defaultFontKey = "vazir")
        {
            return string.Format(@"
(function() {{
  if (window.__antigravityPersianRtlLoaded) return;
  window.__antigravityPersianRtlLoaded = true;

  function initPersianRTL() {{
    if (!document.head || !document.body) {{
      if (document.readyState === 'loading') {{
        document.addEventListener('DOMContentLoaded', initPersianRTL);
      }} else {{
        setTimeout(initPersianRTL, 50);
      }}
      return;
    }}

    const fonts = [
      {{ id: 'vazir', name: 'وزیرمتن (Vazirmatn)', family: ""'Vazirmatn', -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif"" }},
      {{ id: 'studio', name: 'استودیو (Studio)', family: ""'SD-Studio', 'Studio', 'Vazirmatn', sans-serif"" }},
      {{ id: 'ray', name: 'ری (Ray)', family: ""'Ray', 'Vazirmatn', sans-serif"" }},
      {{ id: 'samim', name: 'صمیم (Samim)', family: ""'Samim', 'Vazirmatn', sans-serif"" }}
    ];

    let currentFontId = localStorage.getItem('ag_persian_font_id') || '{0}';

    let fontStyle = document.getElementById('ag-font-family-style');
    if (!fontStyle) {{
      fontStyle = document.createElement('style');
      fontStyle.id = 'ag-font-family-style';
      document.head.appendChild(fontStyle);
    }}

    function applyActiveFont(fontId) {{
      const found = fonts.find(f => f.id === fontId) || fonts[0];
      currentFontId = found.id;
      localStorage.setItem('ag_persian_font_id', currentFontId);

      fontStyle.textContent = `
        :root {{
          --ag-active-persian-font: ${{found.family}};
        }}
        body.ag-persian-on,
        body.ag-persian-on button,
        body.ag-persian-on input,
        body.ag-persian-on textarea,
        body.ag-persian-on [contenteditable=""true""],
        body.ag-persian-on .sidebar-persian-title,
        body.ag-persian-on th,
        body.ag-persian-on td {{
          font-family: var(--ag-active-persian-font) !important;
        }}
      `;

      document.querySelectorAll('.ag-font-item').forEach(item => {{
        if (item.getAttribute('data-font-id') === currentFontId) {{
          item.style.background = 'rgba(99, 102, 241, 0.3)';
          item.style.color = '#a5b4fc';
          const check = item.querySelector('.ag-font-check');
          if (check) check.textContent = '✔';
        }} else {{
          item.style.background = 'transparent';
          item.style.color = '#cbd5e1';
          const check = item.querySelector('.ag-font-check');
          if (check) check.textContent = '';
        }}
      }});
    }}

    let baseStyle = document.getElementById('ag-persian-v3-style');
    if (!baseStyle) {{
      baseStyle = document.createElement('style');
      baseStyle.id = 'ag-persian-v3-style';
      document.head.appendChild(baseStyle);
    }}

    baseStyle.textContent = `
      @font-face {{
        font-family: 'Vazirmatn';
        src: local('Vazirmatn Regular'), local('Vazirmatn-Regular'), local('Vazirmatn');
        font-weight: 400;
        font-style: normal;
      }}
      @font-face {{
        font-family: 'Vazirmatn';
        src: local('Vazirmatn Medium'), local('Vazirmatn-Medium');
        font-weight: 500;
        font-style: normal;
      }}
      @font-face {{
        font-family: 'Vazirmatn';
        src: local('Vazirmatn SemiBold'), local('Vazirmatn-SemiBold');
        font-weight: 600;
        font-style: normal;
      }}
      @font-face {{
        font-family: 'Vazirmatn';
        src: local('Vazirmatn Bold'), local('Vazirmatn-Bold');
        font-weight: 700;
        font-style: normal;
      }}
      @font-face {{
        font-family: 'SD-Studio';
        src: local('SD-Studio-Bold'), local('SD-Studio'), local('Studio');
        font-weight: bold;
        font-style: normal;
      }}
      @font-face {{
        font-family: 'Ray';
        src: local('Ray'), local('Ray-Medium'), local('Ray-Bold');
        font-weight: normal;
        font-style: normal;
      }}
      @font-face {{
        font-family: 'Samim';
        src: local('Samim'), local('Samim-Bold');
        font-weight: normal;
        font-style: normal;
      }}

      body.ag-persian-on p,
      body.ag-persian-on blockquote,
      body.ag-persian-on .whitespace-pre-wrap {{
        direction: auto !important;
        unicode-bidi: plaintext !important;
        line-height: 1.85 !important;
      }}

      body.ag-persian-on .group\\/user-input-step .whitespace-pre-wrap {{
        direction: auto !important;
        unicode-bidi: plaintext !important;
        text-align: right !important;
        line-height: 1.85 !important;
      }}

      body.ag-persian-on h1,
      body.ag-persian-on h2,
      body.ag-persian-on h3,
      body.ag-persian-on h4,
      body.ag-persian-on h5,
      body.ag-persian-on h6 {{
        direction: rtl !important;
        text-align: right !important;
        unicode-bidi: isolate !important;
        line-height: 1.6 !important;
      }}

      body.ag-persian-on ol,
      body.ag-persian-on ul {{
        direction: rtl !important;
        unicode-bidi: isolate !important;
        padding-right: 2.2rem !important;
        padding-left: 0.5rem !important;
        margin-right: 0 !important;
        text-align: right !important;
      }}
      body.ag-persian-on li {{
        direction: rtl !important;
        unicode-bidi: plaintext !important;
        text-align: right !important;
        line-height: 1.85 !important;
        margin-bottom: 0.35rem !important;
      }}
      body.ag-persian-on ol > li::marker {{
        direction: rtl !important;
        unicode-bidi: isolate !important;
        font-family: var(--ag-active-persian-font, 'Vazirmatn') !important;
        font-weight: bold !important;
        color: #818cf8 !important;
      }}
      body.ag-persian-on ul > li::marker {{
        color: #818cf8 !important;
      }}

      body.ag-persian-on table {{
        direction: rtl !important;
        unicode-bidi: isolate !important;
        width: 100% !important;
        border-collapse: collapse !important;
        margin: 12px 0 !important;
      }}
      body.ag-persian-on thead,
      body.ag-persian-on tbody,
      body.ag-persian-on tr {{
        direction: rtl !important;
      }}
      body.ag-persian-on th,
      body.ag-persian-on td {{
        direction: rtl !important;
        text-align: right !important;
        unicode-bidi: plaintext !important;
        line-height: 1.8 !important;
        padding: 10px 14px !important;
      }}
      body.ag-persian-on th {{
        font-weight: 700 !important;
        background: rgba(255, 255, 255, 0.04) !important;
      }}

      body.ag-persian-on blockquote {{
        border-right: 3.5px solid #6366f1 !important;
        border-left: none !important;
        padding-right: 1rem !important;
        padding-left: 0.5rem !important;
        direction: auto !important;
        unicode-bidi: plaintext !important;
      }}

      body.ag-persian-on .sidebar-persian-title {{
        direction: rtl !important;
        text-align: right !important;
      }}

      .ag-persian-timestamp {{
        direction: rtl !important;
        unicode-bidi: isolate !important;
        display: inline-block !important;
        font-family: var(--ag-active-persian-font, 'Vazirmatn') !important;
        font-size: 11px !important;
        opacity: 0.75 !important;
        white-space: nowrap !important;
      }}

      pre, code, kbd, samp, .font-mono, [class*=""font-mono""], .token, pre *, code *, .hljs, [class*=""hljs""] {{
        font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, ""Liberation Mono"", monospace !important;
        direction: ltr !important;
        text-align: left !important;
        unicode-bidi: isolate !important;
        letter-spacing: normal !important;
      }}

      body.ag-persian-on code {{
        display: inline-block !important;
        margin: 0 3px !important;
        padding: 0.1em 0.4em !important;
        direction: ltr !important;
        unicode-bidi: isolate !important;
        vertical-align: baseline !important;
      }}

      #ag-rtl-toggle-pill {{
        position: fixed;
        top: 4px;
        left: 310px;
        z-index: 999999;
        height: 22px;
        padding: 0 8px;
        display: inline-flex;
        align-items: center;
        gap: 5px;
        border-radius: 11px;
        cursor: pointer;
        font-size: 11px;
        font-family: var(--ag-active-persian-font, 'Vazirmatn');
        backdrop-filter: blur(8px);
        transition: all 0.2s ease;
        user-select: none;
        -webkit-app-region: no-drag;
        box-shadow: 0 1px 3px rgba(0, 0, 0, 0.3);
      }}
      #ag-rtl-toggle-pill.active {{
        background: rgba(99, 102, 241, 0.25);
        color: #a5b4fc;
        border: 1px solid rgba(99, 102, 241, 0.5);
      }}
      #ag-rtl-toggle-pill.active:hover {{
        background: rgba(99, 102, 241, 0.4);
        border-color: rgba(99, 102, 241, 0.8);
      }}
      #ag-rtl-toggle-pill.inactive {{
        background: rgba(120, 120, 120, 0.15);
        color: #888;
        border: 1px solid rgba(120, 120, 120, 0.3);
      }}

      #ag-rtl-font-btn {{
        position: fixed;
        top: 4px;
        left: 425px;
        z-index: 999999;
        height: 22px;
        padding: 0 8px;
        display: inline-flex;
        align-items: center;
        gap: 4px;
        border-radius: 11px;
        cursor: pointer;
        font-size: 11px;
        font-family: var(--ag-active-persian-font, 'Vazirmatn');
        background: rgba(99, 102, 241, 0.2);
        color: #c7d2fe;
        border: 1px solid rgba(99, 102, 241, 0.4);
        backdrop-filter: blur(8px);
        transition: all 0.2s ease;
        user-select: none;
        -webkit-app-region: no-drag;
      }}
      #ag-rtl-font-btn:hover {{
        background: rgba(99, 102, 241, 0.35);
      }}

      #ag-font-popup {{
        position: fixed;
        top: 32px;
        left: 425px;
        background: #1e1e2e;
        border: 1px solid rgba(99, 102, 241, 0.4);
        border-radius: 8px;
        padding: 6px;
        z-index: 1000000;
        box-shadow: 0 8px 24px rgba(0,0,0,0.5);
        display: none;
        flex-direction: column;
        gap: 3px;
        min-width: 185px;
        direction: rtl;
        font-family: var(--ag-active-persian-font, 'Vazirmatn');
        font-size: 12px;
        backdrop-filter: blur(12px);
      }}

      #ag-rtl-toast {{
        position: fixed;
        top: 36px;
        left: 50%;
        transform: translateX(-50%) translateY(-10px);
        background: #1e1e2e;
        color: #e2e8f0;
        padding: 6px 16px;
        border-radius: 6px;
        font-size: 12px;
        font-family: var(--ag-active-persian-font, 'Vazirmatn');
        box-shadow: 0 4px 16px rgba(0,0,0,0.4);
        border: 1px solid rgba(99, 102, 241, 0.4);
        z-index: 1000000;
        opacity: 0;
        pointer-events: none;
        transition: all 0.2s ease;
      }}
      #ag-rtl-toast.show {{
        opacity: 1;
        transform: translateX(-50%) translateY(0);
      }}
    `;

    let isRtl = localStorage.getItem('ag_persian_rtl_active') !== 'false';

    function updateUIState() {{
      if (isRtl) {{
        document.body.classList.add('ag-persian-on');
      }} else {{
        document.body.classList.remove('ag-persian-on');
      }}
      if (pill) {{
        pill.className = isRtl ? 'active' : 'inactive';
        pill.innerHTML = isRtl
          ? '<span style=""font-size:11px"">🇮🇷</span><span>راست‌چین فعال</span>'
          : '<span style=""font-size:11px;filter:grayscale(1)"">⚪</span><span>راست‌چین خاموش</span>';
      }}
      fastUpdateSidebar();
    }}

    let pill = document.getElementById('ag-rtl-toggle-pill');
    if (!pill) {{
      pill = document.createElement('button');
      pill.id = 'ag-rtl-toggle-pill';
      pill.title = 'تغییر راست‌چین / چپ‌چین (کلید میانبر: Ctrl+Shift+R)';
      pill.onclick = () => {{
        isRtl = !isRtl;
        localStorage.setItem('ag_persian_rtl_active', isRtl ? 'true' : 'false');
        updateUIState();
        showToast(isRtl ? '✨ حالت راست‌چین و فارسی فعال شد' : '⚪ حالت راست‌چین غیرفعال شد');
      }};
      document.body.appendChild(pill);
    }}

    let fontBtn = document.getElementById('ag-rtl-font-btn');
    if (!fontBtn) {{
      fontBtn = document.createElement('button');
      fontBtn.id = 'ag-rtl-font-btn';
      fontBtn.title = 'تغییر قلم و فونت فارسی برنامه';
      fontBtn.innerHTML = '🎨 فونت';
      fontBtn.onclick = (e) => {{
        e.stopPropagation();
        popup.style.display = popup.style.display === 'flex' ? 'none' : 'flex';
      }};
      document.body.appendChild(fontBtn);
    }}

    let popup = document.getElementById('ag-font-popup');
    if (!popup) {{
      popup = document.createElement('div');
      popup.id = 'ag-font-popup';

      const title = document.createElement('div');
      title.style.cssText = 'padding: 4px 8px; font-weight: bold; color: #818cf8; border-bottom: 1px solid rgba(255,255,255,0.1); margin-bottom: 4px; font-size: 11px;';
      title.textContent = '🎨 انتخاب قلم / فونت فارسی:';
      popup.appendChild(title);

      fonts.forEach(f => {{
        const item = document.createElement('div');
        item.className = 'ag-font-item';
        item.setAttribute('data-font-id', f.id);
        item.style.cssText = 'padding: 6px 10px; border-radius: 5px; cursor: pointer; display: flex; justify-content: space-between; align-items: center; transition: all 0.15s ease;';
        item.innerHTML = `<span>${{f.name}}</span><span class=""ag-font-check"" style=""font-weight:bold;color:#a5b4fc""></span>`;
        item.onmouseenter = () => {{ if (f.id !== currentFontId) item.style.background = 'rgba(255,255,255,0.06)'; }};
        item.onmouseleave = () => {{ if (f.id !== currentFontId) item.style.background = 'transparent'; }};
        item.onclick = (e) => {{
          e.stopPropagation();
          applyActiveFont(f.id);
          popup.style.display = 'none';
          showToast('✨ فونت به «' + f.name.split('(')[0].trim() + '» تغییر یافت');
        }};
        popup.appendChild(item);
      }});

      document.body.appendChild(popup);
    }}

    document.addEventListener('click', (e) => {{
      if (popup && !popup.contains(e.target) && e.target.id !== 'ag-rtl-font-btn') {{
        popup.style.display = 'none';
      }}
    }});

    function showToast(msg) {{
      let t = document.getElementById('ag-rtl-toast');
      if (!t) {{
        t = document.createElement('div');
        t.id = 'ag-rtl-toast';
        document.body.appendChild(t);
      }}
      t.textContent = msg;
      t.classList.add('show');
      setTimeout(() => t.classList.remove('show'), 1600);
    }}

    pill.title = 'تغییر راست‌چین / چپ‌چین (کلید میانبر: Alt+Shift+R یا Alt+R)';

    window.addEventListener('keydown', (e) => {{
      const key = (e.key || '').toLowerCase();

      // Prevent accidental Ctrl+Shift+R from triggering browser hard reload / logout
      if (e.ctrlKey && e.shiftKey && key === 'r') {{
        e.preventDefault();
        e.stopPropagation();
        if (pill) pill.click();
        return false;
      }}

      // Safe official shortcuts: Alt+Shift+R, Ctrl+Alt+R, Alt+R
      if ((e.altKey && e.shiftKey && key === 'r') ||
          (e.ctrlKey && e.altKey && key === 'r') ||
          (e.altKey && key === 'r')) {{
        e.preventDefault();
        e.stopPropagation();
        if (pill) pill.click();
        return false;
      }}
    }}, true);

    const RTL_REGEX = /[\u0600-\u06FF\u0750-\u077F\uFB50-\uFDFF\uFE70-\uFEFF]/;
    document.addEventListener('input', (e) => {{
      if (!isRtl) return;
      const target = e.target;
      if (target && (target.isContentEditable || target.tagName === 'TEXTAREA' || target.tagName === 'INPUT')) {{
        const val = (target.value || target.textContent || '').trim();
        if (val.length > 0) {{
          if (RTL_REGEX.test(val[0])) {{
            target.style.direction = 'rtl';
            target.style.textAlign = 'right';
          }} else {{
            target.style.direction = 'ltr';
            target.style.textAlign = 'left';
          }}
        }} else {{
          target.style.direction = '';
          target.style.textAlign = '';
        }}
      }}
    }}, true);

    const DIGITS = ['۰', '۱', '۲', '۳', '۴', '۵', '۶', '۷', '۸', '۹'];
    function toFa(num) {{
      return String(num).replace(/\\d/g, d => DIGITS[d]);
    }}

    function translateTime(txt) {{
      const t = txt.trim();
      let m;
      if ((m = t.match(/^(\\d+)\\s*m$/i))) return '\u200F' + toFa(m[1]) + ' دقیقه پیش\u200F';
      if ((m = t.match(/^(\\d+)\\s*h$/i))) return '\u200F' + toFa(m[1]) + ' ساعت پیش\u200F';
      if ((m = t.match(/^(\\d+)\\s*d$/i))) return '\u200F' + toFa(m[1]) + ' روز پیش\u200F';
      if ((m = t.match(/^(\\d+)\\s*mo$/i))) return '\u200F' + toFa(m[1]) + ' ماه پیش\u200F';
      if ((m = t.match(/^(\\d+)\\s*y$/i))) return '\u200F' + toFa(m[1]) + ' سال پیش\u200F';
      if ((m = t.match(/^(\\d+)\\s*s$/i))) return '\u200F' + toFa(m[1]) + ' ثانیه پیش\u200F';
      if (/^(now|just now)$/i.test(t)) return '\u200Fهمین الان\u200F';
      return null;
    }}

    function fastUpdateSidebar() {{
      if (!isRtl) return;
      const spans = document.querySelectorAll('span');
      for (let i = 0; i < spans.length; i++) {{
        const el = spans[i];
        if (el.children.length > 0) continue;
        const text = el.textContent.trim();

        const tr = translateTime(text);
        if (tr) {{
          el.textContent = tr;
          el.classList.add('ag-persian-timestamp');
          el.style.direction = 'rtl';
          el.style.unicodeBidi = 'isolate';
          continue;
        }}

        if (el.classList.contains('truncate') && RTL_REGEX.test(text) && !text.includes('\\\\') && !text.includes('/')) {{
          el.classList.add('sidebar-persian-title');
          el.style.direction = 'rtl';
          el.style.textAlign = 'right';
        }}
      }}
    }}

    if (window.__agTimer) clearInterval(window.__agTimer);
    window.__agTimer = setInterval(fastUpdateSidebar, 1000);

    document.addEventListener('click', () => setTimeout(fastUpdateSidebar, 80), true);
    window.addEventListener('focus', () => setTimeout(fastUpdateSidebar, 80), true);

    applyActiveFont(currentFontId);
    updateUIState();
    console.log('[Antigravity] Persian RTL v3.4 Loaded with Dynamic Font Switcher.');
  }}

  if (document.readyState === 'loading') {{
    document.addEventListener('DOMContentLoaded', initPersianRTL);
  }} else {{
    initPersianRTL();
  }}
}})();
", defaultFontKey);
        }
    }
}
