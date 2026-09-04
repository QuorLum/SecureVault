using System.Diagnostics;
using SecureVault.Installer.Services;

namespace SecureVault.Installer.Forms;

public partial class InstallerForm : Form
{
    private int _currentStep = 0; // 0 = Welcome/License, 1 = Destination, 2 = Options, 3 = Installing, 4 = Finished
    private readonly InstallOptions _options = new();

    // Controls
    private Panel _headerPanel = null!;
    private Label _lblHeaderTitle = null!;
    private Label _lblHeaderSubtitle = null!;
    private Panel _bodyPanel = null!;
    private Panel _footerPanel = null!;
    private Button _btnBack = null!;
    private Button _btnNext = null!;
    private Button _btnCancel = null!;

    // Step 0: Welcome / License
    private Panel _pnlStep0 = null!;
    private TextBox _txtLicense = null!;
    private RadioButton _rbAccept = null!;
    private RadioButton _rbDecline = null!;

    // Step 1: Destination
    private Panel _pnlStep1 = null!;
    private TextBox _txtDestination = null!;
    private Button _btnBrowse = null!;
    private Label _lblDiskSpace = null!;

    // Step 2: Options
    private Panel _pnlStep2 = null!;
    private CheckBox _chkDesktop = null!;
    private CheckBox _chkStartMenu = null!;
    private CheckBox _chkFileAssociation = null!;

    // Step 3: Installing
    private Panel _pnlStep3 = null!;
    private ProgressBar _progressBar = null!;
    private Label _lblInstallStatus = null!;

    // Step 4: Finished
    private Panel _pnlStep4 = null!;
    private CheckBox _chkLaunchApp = null!;

    public InstallerForm()
    {
        InitializeComponent();
        ShowStep(0);
    }

    private void InitializeComponent()
    {
        this.Text = "SecureVault Setup";
        this.Size = new Size(640, 480);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = true;
        this.BackColor = Color.FromArgb(15, 23, 42); // #0f172a
        this.ForeColor = Color.FromArgb(248, 250, 252); // #f8fafc
        this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

        // Header
        _headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 70,
            BackColor = Color.FromArgb(30, 41, 59), // #1e293b
            Padding = new Padding(20, 12, 20, 10)
        };

        _lblHeaderTitle = new Label
        {
            Text = "SecureVault Setup",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.FromArgb(248, 250, 252),
            AutoSize = true,
            Location = new Point(20, 12)
        };

        _lblHeaderSubtitle = new Label
        {
            Text = "Military-grade Zero-Knowledge Encrypted Storage",
            Font = new Font("Segoe UI", 9f, FontStyle.Regular),
            ForeColor = Color.FromArgb(148, 163, 184), // #94a3b8
            AutoSize = true,
            Location = new Point(20, 38)
        };

        _headerPanel.Controls.Add(_lblHeaderTitle);
        _headerPanel.Controls.Add(_lblHeaderSubtitle);

        // Footer
        _footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 55,
            BackColor = Color.FromArgb(30, 41, 59),
            Padding = new Padding(15)
        };

        _btnCancel = CreateStyledButton("Cancel", 520, 12);
        _btnCancel.Click += (s, e) => this.Close();

        _btnNext = CreateStyledButton("Next >", 420, 12, isPrimary: true);
        _btnNext.Click += OnNextClicked;

        _btnBack = CreateStyledButton("< Back", 320, 12);
        _btnBack.Click += (s, e) => ShowStep(_currentStep - 1);

        _footerPanel.Controls.Add(_btnBack);
        _footerPanel.Controls.Add(_btnNext);
        _footerPanel.Controls.Add(_btnCancel);

        // Body
        _bodyPanel = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(25, 20, 25, 15)
        };

        CreateStep0Controls();
        CreateStep1Controls();
        CreateStep2Controls();
        CreateStep3Controls();
        CreateStep4Controls();

        this.Controls.Add(_bodyPanel);
        this.Controls.Add(_headerPanel);
        this.Controls.Add(_footerPanel);
    }

    private Button CreateStyledButton(string text, int x, int y, bool isPrimary = false)
    {
        var btn = new Button
        {
            Text = text,
            Size = new Size(90, 32),
            Location = new Point(x, y),
            FlatStyle = FlatStyle.Flat,
            Cursor = Cursors.Hand,
            Font = new Font("Segoe UI", 9f, FontStyle.Bold)
        };

        btn.FlatAppearance.BorderSize = 1;

        if (isPrimary)
        {
            btn.BackColor = Color.FromArgb(99, 102, 241); // #6366f1 Indigo
            btn.ForeColor = Color.White;
            btn.FlatAppearance.BorderColor = Color.FromArgb(129, 140, 248);
        }
        else
        {
            btn.BackColor = Color.FromArgb(51, 65, 85); // #334155
            btn.ForeColor = Color.FromArgb(241, 245, 249);
            btn.FlatAppearance.BorderColor = Color.FromArgb(71, 85, 105);
        }

        return btn;
    }

    private void CreateStep0Controls()
    {
        _pnlStep0 = new Panel { Dock = DockStyle.Fill, Visible = false };

        var lblIntro = new Label
        {
            Text = "Please review the license terms before installing SecureVault.",
            AutoSize = true,
            Location = new Point(0, 0),
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
            ForeColor = Color.FromArgb(226, 232, 240)
        };

        _txtLicense = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Size = new Size(570, 180),
            Location = new Point(0, 25),
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.FromArgb(226, 232, 240),
            BorderStyle = BorderStyle.FixedSingle,
            Text = @"MIT License

Copyright (c) 2026 SecureVault Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the ""Software""), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED ""AS IS"", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT."
        };

        _rbAccept = new RadioButton
        {
            Text = "I accept the agreement",
            Location = new Point(0, 220),
            AutoSize = true,
            Checked = true,
            Cursor = Cursors.Hand,
            ForeColor = Color.FromArgb(248, 250, 252)
        };
        _rbAccept.CheckedChanged += (s, e) => _btnNext.Enabled = _rbAccept.Checked;

        _rbDecline = new RadioButton
        {
            Text = "I do not accept the agreement",
            Location = new Point(0, 245),
            AutoSize = true,
            Cursor = Cursors.Hand,
            ForeColor = Color.FromArgb(148, 163, 184)
        };

        _pnlStep0.Controls.Add(lblIntro);
        _pnlStep0.Controls.Add(_txtLicense);
        _pnlStep0.Controls.Add(_rbAccept);
        _pnlStep0.Controls.Add(_rbDecline);

        _bodyPanel.Controls.Add(_pnlStep0);
    }

    private void CreateStep1Controls()
    {
        _pnlStep1 = new Panel { Dock = DockStyle.Fill, Visible = false };

        var lbl = new Label
        {
            Text = "Choose Destination Location\nSetup will install SecureVault into the following folder:",
            Location = new Point(0, 10),
            Size = new Size(570, 40),
            ForeColor = Color.FromArgb(226, 232, 240)
        };

        _txtDestination = new TextBox
        {
            Text = InstallerEngine.DefaultInstallDirectory,
            Location = new Point(0, 65),
            Size = new Size(465, 30),
            BackColor = Color.FromArgb(30, 41, 59),
            ForeColor = Color.FromArgb(248, 250, 252),
            BorderStyle = BorderStyle.FixedSingle
        };
        _txtDestination.TextChanged += (s, e) => UpdateDiskSpaceInfo();

        _btnBrowse = CreateStyledButton("Browse...", 480, 63);
        _btnBrowse.Click += (s, e) =>
        {
            using var dlg = new FolderBrowserDialog();
            dlg.Description = "Select Destination Directory for SecureVault";
            dlg.InitialDirectory = _txtDestination.Text;
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                _txtDestination.Text = dlg.SelectedPath;
            }
        };

        _lblDiskSpace = new Label
        {
            Location = new Point(0, 120),
            Size = new Size(570, 50),
            ForeColor = Color.FromArgb(148, 163, 184)
        };
        UpdateDiskSpaceInfo();

        _pnlStep1.Controls.Add(lbl);
        _pnlStep1.Controls.Add(_txtDestination);
        _pnlStep1.Controls.Add(_btnBrowse);
        _pnlStep1.Controls.Add(_lblDiskSpace);

        _bodyPanel.Controls.Add(_pnlStep1);
    }

    private void UpdateDiskSpaceInfo()
    {
        try
        {
            string root = Path.GetPathRoot(_txtDestination.Text) ?? "C:\\";
            var drive = new DriveInfo(root);
            long freeMb = drive.AvailableFreeSpace / (1024 * 1024);
            _lblDiskSpace.Text = $"Space required: 450 MB\nSpace available on drive {drive.Name}: {freeMb:N0} MB";
        }
        catch
        {
            _lblDiskSpace.Text = "Space required: 450 MB";
        }
    }

    private void CreateStep2Controls()
    {
        _pnlStep2 = new Panel { Dock = DockStyle.Fill, Visible = false };

        var lbl = new Label
        {
            Text = "Select Additional Tasks\nWhich additional tasks should be performed during installation?",
            Location = new Point(0, 10),
            Size = new Size(570, 40),
            ForeColor = Color.FromArgb(226, 232, 240)
        };

        _chkDesktop = new CheckBox
        {
            Text = "Create a Desktop shortcut (Home Screen)",
            Location = new Point(10, 65),
            Size = new Size(500, 30),
            Checked = true,
            Cursor = Cursors.Hand,
            ForeColor = Color.FromArgb(248, 250, 252),
            Font = new Font("Segoe UI", 10f, FontStyle.Regular)
        };

        _chkStartMenu = new CheckBox
        {
            Text = "Create a Start Menu shortcut",
            Location = new Point(10, 105),
            Size = new Size(500, 30),
            Checked = true,
            Cursor = Cursors.Hand,
            ForeColor = Color.FromArgb(248, 250, 252),
            Font = new Font("Segoe UI", 10f, FontStyle.Regular)
        };

        _chkFileAssociation = new CheckBox
        {
            Text = "Associate SecureVault with .vault container files",
            Location = new Point(10, 145),
            Size = new Size(500, 30),
            Checked = true,
            Cursor = Cursors.Hand,
            ForeColor = Color.FromArgb(248, 250, 252),
            Font = new Font("Segoe UI", 10f, FontStyle.Regular)
        };

        _pnlStep2.Controls.Add(lbl);
        _pnlStep2.Controls.Add(_chkDesktop);
        _pnlStep2.Controls.Add(_chkStartMenu);
        _pnlStep2.Controls.Add(_chkFileAssociation);

        _bodyPanel.Controls.Add(_pnlStep2);
    }

    private void CreateStep3Controls()
    {
        _pnlStep3 = new Panel { Dock = DockStyle.Fill, Visible = false };

        var lbl = new Label
        {
            Text = "Installing SecureVault...\nPlease wait while Setup installs SecureVault on your computer.",
            Location = new Point(0, 15),
            Size = new Size(570, 40),
            ForeColor = Color.FromArgb(226, 232, 240)
        };

        _progressBar = new ProgressBar
        {
            Location = new Point(0, 75),
            Size = new Size(570, 24),
            Style = ProgressBarStyle.Continuous,
            Value = 0
        };

        _lblInstallStatus = new Label
        {
            Text = "Extracting files...",
            Location = new Point(0, 110),
            Size = new Size(570, 30),
            ForeColor = Color.FromArgb(148, 163, 184)
        };

        _pnlStep3.Controls.Add(lbl);
        _pnlStep3.Controls.Add(_progressBar);
        _pnlStep3.Controls.Add(_lblInstallStatus);

        _bodyPanel.Controls.Add(_pnlStep3);
    }

    private void CreateStep4Controls()
    {
        _pnlStep4 = new Panel { Dock = DockStyle.Fill, Visible = false };

        var lblSuccessTitle = new Label
        {
            Text = "Completing the SecureVault Setup Wizard",
            Font = new Font("Segoe UI", 13f, FontStyle.Bold),
            ForeColor = Color.FromArgb(52, 211, 153), // #34d399 Emerald
            Location = new Point(0, 20),
            AutoSize = true
        };

        var lblDesc = new Label
        {
            Text = "SecureVault has been successfully installed on your computer.\n\nYou can launch it anytime from your Desktop or Start Menu.",
            Location = new Point(0, 60),
            Size = new Size(570, 50),
            ForeColor = Color.FromArgb(226, 232, 240)
        };

        _chkLaunchApp = new CheckBox
        {
            Text = "Launch SecureVault now",
            Location = new Point(5, 130),
            Size = new Size(500, 30),
            Checked = true,
            Cursor = Cursors.Hand,
            ForeColor = Color.FromArgb(248, 250, 252),
            Font = new Font("Segoe UI", 10f, FontStyle.Regular)
        };

        _pnlStep4.Controls.Add(lblSuccessTitle);
        _pnlStep4.Controls.Add(lblDesc);
        _pnlStep4.Controls.Add(_chkLaunchApp);

        _bodyPanel.Controls.Add(_pnlStep4);
    }

    private void ShowStep(int step)
    {
        _currentStep = step;

        _pnlStep0.Visible = (step == 0);
        _pnlStep1.Visible = (step == 1);
        _pnlStep2.Visible = (step == 2);
        _pnlStep3.Visible = (step == 3);
        _pnlStep4.Visible = (step == 4);

        _btnBack.Visible = (step > 0 && step < 3);
        _btnCancel.Visible = (step < 3);

        switch (step)
        {
            case 0:
                _lblHeaderTitle.Text = "License Agreement";
                _lblHeaderSubtitle.Text = "Please review the license terms before proceeding.";
                _btnNext.Text = "Next >";
                _btnNext.Enabled = _rbAccept.Checked;
                break;
            case 1:
                _lblHeaderTitle.Text = "Select Destination Directory";
                _lblHeaderSubtitle.Text = "Where should SecureVault be installed?";
                _btnNext.Text = "Next >";
                _btnNext.Enabled = true;
                break;
            case 2:
                _lblHeaderTitle.Text = "Select Additional Tasks";
                _lblHeaderSubtitle.Text = "Choose shortcut and file association options.";
                _btnNext.Text = "Install";
                _btnNext.Enabled = true;
                break;
            case 3:
                _lblHeaderTitle.Text = "Installing";
                _lblHeaderSubtitle.Text = "Please wait while files are being copied.";
                _btnNext.Enabled = false;
                break;
            case 4:
                _lblHeaderTitle.Text = "Installation Finished";
                _lblHeaderSubtitle.Text = "Setup has finished installing SecureVault.";
                _btnNext.Text = "Finish";
                _btnNext.Enabled = true;
                break;
        }
    }

    private async void OnNextClicked(object? sender, EventArgs e)
    {
        if (_currentStep == 0)
        {
            ShowStep(1);
        }
        else if (_currentStep == 1)
        {
            _options.DestinationDirectory = _txtDestination.Text.Trim();
            ShowStep(2);
        }
        else if (_currentStep == 2)
        {
            _options.CreateDesktopShortcut = _chkDesktop.Checked;
            _options.CreateStartMenuShortcut = _chkStartMenu.Checked;
            _options.RegisterFileAssociation = _chkFileAssociation.Checked;

            ShowStep(3);
            await RunInstallationAsync();
        }
        else if (_currentStep == 4)
        {
            if (_chkLaunchApp.Checked)
            {
                string exePath = Path.Combine(_options.DestinationDirectory, "SecureVault.exe");
                if (File.Exists(exePath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = exePath,
                            WorkingDirectory = _options.DestinationDirectory,
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
            }

            this.Close();
        }
    }

    private async Task RunInstallationAsync()
    {
        var progress = new Progress<(int Percent, string Status)>(p =>
        {
            _progressBar.Value = Math.Clamp(p.Percent, 0, 100);
            _lblInstallStatus.Text = p.Status;
        });

        try
        {
            await InstallerEngine.InstallAsync(_options, progress);
            ShowStep(4);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"Installation failed:\n\n{ex.Message}",
                "Setup Error",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);

            ShowStep(2); // Go back to options
        }
    }
}
