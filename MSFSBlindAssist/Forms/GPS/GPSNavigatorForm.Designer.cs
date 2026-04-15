namespace MSFSBlindAssist.Forms.GPS;

partial class GPSNavigatorForm
{
    private Label connectionLabel = null!;
    private Label navInfoLabel = null!;
    private Label navDetailLabel = null!;
    private Label statusLabel = null!;
    private ListBox flightPlanList = null!;

    private void InitializeComponent()
    {
        this.Text = "GPS Navigator";
        this.AccessibleName = "GPS Navigator";
        this.AccessibleDescription = "Accessible GPS flight plan and navigation. Browse waypoints, issue Direct-To, and monitor navigation in real time.";
        this.Size = new System.Drawing.Size(600, 650);
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.StartPosition = FormStartPosition.CenterScreen;

        int y = 10;
        int fullWidth = 560;

        // Connection status
        connectionLabel = new Label
        {
            Text = "Bridge: Checking...",
            AccessibleName = "GPS bridge connection status",
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(fullWidth, 20),
            Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold)
        };
        y += 25;

        // Nav info (current waypoint, distance, bearing, ETE)
        navInfoLabel = new Label
        {
            Text = "Next: ---  |  --- NM  |  ---°  |  ETE ---",
            AccessibleName = "Current navigation: no data",
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(fullWidth, 20)
        };
        y += 22;

        // Nav detail (DTK, XTK, ground speed, GPS drives NAV)
        navDetailLabel = new Label
        {
            Text = "DTK: ---  |  XTK: ---  |  GS: ---  |  GPS→NAV: ---",
            AccessibleName = "Navigation details: no data",
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(fullWidth, 20)
        };
        y += 25;

        // Status
        statusLabel = new Label
        {
            Text = "No flight plan loaded",
            AccessibleName = "Flight plan status",
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(fullWidth, 20)
        };
        y += 25;

        // Flight plan list
        var fplLabel = new Label
        {
            Text = "Flight Plan:",
            Location = new System.Drawing.Point(10, y),
            AutoSize = true,
            Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold)
        };
        y += 20;

        flightPlanList = new ListBox
        {
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(fullWidth, 250),
            AccessibleName = "Flight plan waypoints. Use arrow keys to browse.",
            AccessibleDescription = "List of waypoints in the active flight plan. Active leg is marked with arrows.",
            Font = new System.Drawing.Font("Consolas", 10)
        };
        y += 260;

        // Command buttons
        var buttonPanel = new FlowLayoutPanel
        {
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(fullWidth, 130),
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            AutoSize = false
        };

        var btnSize = new System.Drawing.Size(170, 35);

        var readCurrentBtn = new Button { Text = "Read Nav Status", AccessibleName = "Read current navigation status aloud", Size = btnSize };
        readCurrentBtn.Click += ReadCurrentButton_Click;

        var directToBtn = new Button { Text = "Direct-To", AccessibleName = "Open Direct-To on GPS", Size = btnSize };
        directToBtn.Click += DirectToButton_Click;

        var cancelDtoBtn = new Button { Text = "Cancel Direct-To", AccessibleName = "Cancel Direct-To navigation", Size = btnSize };
        cancelDtoBtn.Click += CancelDirectToButton_Click;

        var gpsDrivesBtn = new Button { Text = "GPS→NAV Toggle", AccessibleName = "Toggle GPS drives NAV 1 for autopilot", Size = btnSize };
        gpsDrivesBtn.Click += GpsDrivesNavButton_Click;

        var obsBtn = new Button { Text = "OBS Toggle", AccessibleName = "Toggle OBS hold mode", Size = btnSize };
        obsBtn.Click += ObsToggleButton_Click;

        var activateLegBtn = new Button { Text = "Activate Leg", AccessibleName = "Activate the selected waypoint as the current leg", Size = btnSize };
        activateLegBtn.Click += ActivateLegButton_Click;

        var refreshBtn = new Button { Text = "Refresh", AccessibleName = "Refresh GPS data from sim", Size = btnSize };
        refreshBtn.Click += RefreshButton_Click;

        var installBtn = new Button { Text = "Install Bridge", AccessibleName = "Install or update the GNS 530 bridge mod package", Size = btnSize };
        installBtn.Click += InstallBridgeButton_Click;

        buttonPanel.Controls.AddRange(new Control[] {
            readCurrentBtn, directToBtn, cancelDtoBtn, gpsDrivesBtn,
            obsBtn, activateLegBtn, refreshBtn, installBtn
        });

        this.Controls.AddRange(new Control[] {
            connectionLabel, navInfoLabel, navDetailLabel, statusLabel,
            fplLabel, flightPlanList, buttonPanel
        });
    }
}
