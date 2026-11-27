using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows;
using Nusa.Services;
using Forms = System.Windows.Forms;

namespace Nusa
{
    public partial class App : Application
    {
        Mutex? _mutex;
        Core? _core;
        NotchWindow? _notch;
        Forms.NotifyIcon? _tray;
        SettingsWindow? _settings;
        Forms.ToolStripMenuItem? _miStartup, _miMascot, _miMinimal, _miGlass, _miSize;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Faqat bitta nusxa ishlashi uchun
            _mutex = new Mutex(true, "Nusa.Notch.SingleInstance", out bool created);
            if (!created) { Shutdown(); return; }

            DispatcherUnhandledException += (_, ex) => { ex.Handled = true; LogError(ex.Exception); };
            AppDomain.CurrentDomain.UnhandledException += (_, ex) => LogError(ex.ExceptionObject as Exception);

            _core = new Core(Dispatcher);
            if (!_core.S.FirstRunDone)
            {
                // Birinchi ishga tushirishda Windows bilan avtomatik ochilishni yoqamiz
                _core.S.FirstRunDone = true;
                AutoStart.Set(true);
                _core.SaveNow();
            }
            _core.S.StartWithWindows = AutoStart.IsEnabled();
            _notch = new NotchWindow();
            _notch.Show();
            _core.Start();

            CreateTray();
            _core.SettingsChanged += SyncTrayChecks;
        }

        static void LogError(Exception? ex)
        {
            if (ex == null) return;
            try
            {
                System.IO.Directory.CreateDirectory(Storage.Folder);
                System.IO.File.AppendAllText(System.IO.Path.Combine(Storage.Folder, "error.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {ex}\n\n");
            }
            catch { }
        }

        void CreateTray()
        {
            var menu = new Forms.ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false, ShowCheckMargin = true };
            menu.Font = new Font("Segoe UI", 9.5f);

            var title = new Forms.ToolStripLabel("Nusa") { Font = new Font("Segoe UI Semibold", 10f), Image = LogoBitmap(20), ImageAlign = ContentAlignment.MiddleLeft };
            menu.Items.Add(title);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Notchni ochish", null, (_, _) => _notch?.Expand(null));
            menu.Items.Add("Sozlamalar…", null, (_, _) => OpenSettings());
            menu.Items.Add("Notch ko'rinishi…", null, (_, _) => OpenSettings("notch"));
            menu.Items.Add(new Forms.ToolStripSeparator());

            // Notch o'lchami — tez tanlash
            _miSize = new Forms.ToolStripMenuItem("Notch o'lchami");
            foreach (var pct in new[] { 80, 90, 100, 115, 130, 150 })
            {
                var it = new Forms.ToolStripMenuItem(pct + "%") { Tag = pct / 100.0, ForeColor = Color.White };
                it.Click += (_, _) => { _core!.S.NotchScale = (double)it.Tag!; _core.NotifySettingsChanged(); };
                _miSize.DropDownItems.Add(it);
            }
            _miSize.DropDown.BackColor = Color.FromArgb(32, 32, 35);
            ((Forms.ToolStripDropDownMenu)_miSize.DropDown).ShowImageMargin = false;
            ((Forms.ToolStripDropDownMenu)_miSize.DropDown).ShowCheckMargin = true;
            menu.Items.Add(_miSize);
            _miGlass = new Forms.ToolStripMenuItem("Liquid Glass (shisha)") { CheckOnClick = true };
            _miGlass.Click += (_, _) => { _core!.S.LiquidGlass = _miGlass.Checked; _core.NotifySettingsChanged(); };
            menu.Items.Add(_miGlass);

            _miStartup = new Forms.ToolStripMenuItem("Windows bilan ishga tushish") { CheckOnClick = true };
            _miStartup.Click += (_, _) => { _core!.S.StartWithWindows = _miStartup.Checked; AutoStart.Set(_miStartup.Checked); _core.NotifySettingsChanged(); };
            _miMascot = new Forms.ToolStripMenuItem("Jonzotcha (maskot)") { CheckOnClick = true };
            _miMascot.Click += (_, _) => { _core!.S.Mascot = _miMascot.Checked; _core.NotifySettingsChanged(); };
            _miMinimal = new Forms.ToolStripMenuItem("Minimal notch (ingichka chiziq)") { CheckOnClick = true };
            _miMinimal.Click += (_, _) => { _core!.S.MinimalNotch = _miMinimal.Checked; _core.NotifySettingsChanged(); };
            menu.Items.Add(_miStartup);
            menu.Items.Add(_miMascot);
            menu.Items.Add(_miMinimal);
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("Nusa haqida", null, (_, _) => OpenSettings("about"));
            menu.Items.Add("Chiqish", null, (_, _) => ExitApp());

            foreach (Forms.ToolStripItem it in menu.Items) it.ForeColor = Color.White;

            _tray = new Forms.NotifyIcon
            {
                Text = "Nusa — jonli notch",
                Icon = LoadIcon(),
                ContextMenuStrip = menu,
                Visible = true,
            };
            _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) _notch?.Expand(null); };
            _tray.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) OpenSettings(); };
            SyncTrayChecks();
        }

        static Icon LoadIcon()
        {
            try
            {
                var res = GetResourceStream(new Uri("pack://application:,,,/Assets/nusa.ico"));
                // Ekran masshtabiga mos o'lcham (100% — 16 px, 150% — 24 px ...)
                if (res != null) return new Icon(res.Stream, Forms.SystemInformation.SmallIconSize);
            }
            catch { }
            // Zaxira: kodda chizilgan kichik notch
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = new GraphicsPath();
                path.AddArc(2, 8, 16, 16, 90, 180);
                path.AddArc(14, 8, 16, 16, 270, 180);
                path.CloseFigure();
                g.FillPath(Brushes.Black, path);
                g.DrawPath(new Pen(Color.FromArgb(160, 160, 170), 2), path);
                g.FillEllipse(Brushes.White, 10, 13, 4, 6);
                g.FillEllipse(Brushes.White, 18, 13, 4, 6);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        /// <summary>Menyu sarlavhasi uchun logo (Assets/logo.png).</summary>
        static Bitmap? LogoBitmap(int size)
        {
            try
            {
                var res = GetResourceStream(new Uri("pack://application:,,,/Assets/logo.png"));
                if (res == null) return null;
                using var src = new Bitmap(res.Stream);
                var bmp = new Bitmap(size, size);
                using var g = Graphics.FromImage(bmp);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(src, 0, 0, size, size);
                return bmp;
            }
            catch { return null; }
        }

        void SyncTrayChecks()
        {
            if (_miGlass != null && _core != null) _miGlass.Checked = _core.S.LiquidGlass;
            if (_miSize != null && _core != null)
                foreach (Forms.ToolStripMenuItem it in _miSize.DropDownItems)
                    it.Checked = Math.Abs((double)it.Tag! - _core.S.NotchScale) < 0.01;
            if (_core == null || _miStartup == null) return;
            _miStartup.Checked = _core.S.StartWithWindows;
            _miMascot!.Checked = _core.S.Mascot;
            _miMinimal!.Checked = _core.S.MinimalNotch;
        }

        public void OpenSettings(string? section = null)
        {
            if (_settings == null || !_settings.IsLoaded)
            {
                _settings = new SettingsWindow();
                _settings.Closed += (_, _) => _settings = null;
                _settings.Show();
            }
            _settings.Activate();
            if (section != null) _settings.ScrollTo(section);
        }

        public void ExitApp()
        {
            _core?.SaveNow();
            _core?.Volume.Dispose();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            _notch?.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _core?.SaveNow();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            try { _mutex?.ReleaseMutex(); } catch { }
            base.OnExit(e);
        }

        /// <summary>Tray menyusi uchun qorong'i ko'rinish.</summary>
        sealed class DarkRenderer : Forms.ToolStripProfessionalRenderer
        {
            public DarkRenderer() : base(new DarkColors()) { RoundedEdges = true; }

            protected override void OnRenderItemCheck(Forms.ToolStripItemImageRenderEventArgs e)
            {
                var r = new Rectangle(e.ImageRectangle.Left + 2, e.ImageRectangle.Top + 2, e.ImageRectangle.Width - 4, e.ImageRectangle.Height - 4);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var b = new SolidBrush(Color.FromArgb(10, 132, 255));
                e.Graphics.FillEllipse(b, r);
                using var p = new Pen(Color.White, 1.8f);
                e.Graphics.DrawLines(p, new[]
                {
                    new PointF(r.Left + r.Width * 0.28f, r.Top + r.Height * 0.52f),
                    new PointF(r.Left + r.Width * 0.45f, r.Top + r.Height * 0.70f),
                    new PointF(r.Left + r.Width * 0.74f, r.Top + r.Height * 0.34f),
                });
            }
        }

        sealed class DarkColors : Forms.ProfessionalColorTable
        {
            static readonly Color Bg = Color.FromArgb(32, 32, 35);
            static readonly Color Hover = Color.FromArgb(52, 52, 58);
            public override Color ToolStripDropDownBackground => Bg;
            public override Color ImageMarginGradientBegin => Bg;
            public override Color ImageMarginGradientMiddle => Bg;
            public override Color ImageMarginGradientEnd => Bg;
            public override Color MenuBorder => Color.FromArgb(60, 60, 66);
            public override Color MenuItemBorder => Hover;
            public override Color MenuItemSelected => Hover;
            public override Color SeparatorDark => Color.FromArgb(60, 60, 66);
            public override Color SeparatorLight => Bg;
            public override Color CheckBackground => Bg;
            public override Color CheckSelectedBackground => Hover;
            public override Color CheckPressedBackground => Hover;
        }
    }
}
