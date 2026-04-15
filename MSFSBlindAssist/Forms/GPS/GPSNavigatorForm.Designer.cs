namespace MSFSBlindAssist.Forms.GPS;

partial class GPSNavigatorForm
{
    private Label pageGroupLabel = null!;
    private Label navInfoLabel = null!;
    private Label navDetailLabel = null!;
    private Label statusLabel = null!;
    private Label diagLabel = null!;
    private ListBox pageContentList = null!;

    private void InitializeComponent()
    {
        this.Text = "GPS Navigator";
        this.AccessibleName = "GPS Navigator — accessible mirror of the in-sim GPS";
        this.AccessibleDescription =
            "Full mirror of the GNS 530 or G1000 NXi GPS. Browse the current page with the list, " +
            "press GPS buttons from the button panel. Changes on the sim GPS are mirrored here, " +
            "and presses here are sent back to the sim.";
        this.Size = new System.Drawing.Size(820, 700);
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.StartPosition = FormStartPosition.CenterScreen;

        int y = 10;
        int leftWidth = 500;
        int rightX = 520;
        int rightWidth = 280;

        // Status bar (top)
        statusLabel = new Label
        {
            Text = "GPS Navigator ready",
            AccessibleName = "Status",
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(800, 20),
            Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold)
        };
        y += 25;

        // Nav info line
        navInfoLabel = new Label
        {
            Text = "Next: ---  |  --- NM  |  ---°  |  ETE ---",
            AccessibleName = "Current navigation: no data",
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(800, 20)
        };
        y += 22;

        navDetailLabel = new Label
        {
            Text = "DTK: ---  |  XTK: ---  |  GS: ---  |  GPS→NAV: ---",
            AccessibleName = "Navigation details: no data",
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(800, 20)
        };
        y += 30;

        // === LEFT SIDE: Current page mirror ===
        pageGroupLabel = new Label
        {
            Text = "No page data",
            AccessibleName = "Current GPS page",
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(leftWidth, 22),
            Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold)
        };

        int listY = y + 25;
        pageContentList = new ListBox
        {
            Location = new System.Drawing.Point(10, listY),
            Size = new System.Drawing.Size(leftWidth, 450),
            AccessibleName = "GPS page content",
            AccessibleDescription = "The current contents of the GPS screen. Arrow keys rotate the GPS knob; Enter is the ENT key.",
            Font = new System.Drawing.Font("Consolas", 10)
        };
        pageContentList.KeyDown += PageContentList_KeyDown;

        // === RIGHT SIDE: Button panel ===
        int btnWidth = 130;
        int btnHeight = 30;
        int btnSpacing = 5;
        int btnY = y;

        // Page group buttons
        var grpLabel = new Label
        {
            Text = "Page Groups",
            Location = new System.Drawing.Point(rightX, btnY),
            AutoSize = true,
            Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold)
        };
        btnY += 22;

        var fplBtn = CreateGpsButton("FPL (F1)", "Flight plan page", rightX, btnY, btnWidth, btnHeight);
        fplBtn.Click += FplButton_Click;
        var procBtn = CreateGpsButton("PROC (F2)", "Procedures page", rightX + btnWidth + btnSpacing, btnY, btnWidth, btnHeight);
        procBtn.Click += ProcButton_Click;
        btnY += btnHeight + btnSpacing;

        var vnavBtn = CreateGpsButton("VNAV", "Vertical navigation page", rightX, btnY, btnWidth, btnHeight);
        vnavBtn.Click += VnavButton_Click;
        var menuBtn = CreateGpsButton("MENU", "Menu / options", rightX + btnWidth + btnSpacing, btnY, btnWidth, btnHeight);
        menuBtn.Click += MenuButton_Click;
        btnY += btnHeight + btnSpacing;

        var msgBtn = CreateGpsButton("MSG", "Messages page", rightX, btnY, btnWidth, btnHeight);
        msgBtn.Click += MsgButton_Click;
        var obsBtn = CreateGpsButton("OBS", "OBS mode toggle", rightX + btnWidth + btnSpacing, btnY, btnWidth, btnHeight);
        obsBtn.Click += ObsButton_Click;
        btnY += btnHeight + btnSpacing + 5;

        // Input controls
        var inputLabel = new Label
        {
            Text = "Input",
            Location = new System.Drawing.Point(rightX, btnY),
            AutoSize = true,
            Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold)
        };
        btnY += 22;

        var dtoBtn = CreateGpsButton("Direct-To", "Direct-To waypoint", rightX, btnY, btnWidth, btnHeight);
        dtoBtn.Click += DirectToButton_Click;
        var entBtn = CreateGpsButton("ENT (Enter)", "Enter / confirm", rightX + btnWidth + btnSpacing, btnY, btnWidth, btnHeight);
        entBtn.Click += EntButton_Click;
        btnY += btnHeight + btnSpacing;

        var clrBtn = CreateGpsButton("CLR (Back)", "Clear / back", rightX, btnY, btnWidth, btnHeight);
        clrBtn.Click += ClrButton_Click;
        var cursorBtn = CreateGpsButton("CURSOR", "Toggle cursor / activate list", rightX + btnWidth + btnSpacing, btnY, btnWidth, btnHeight);
        cursorBtn.Click += CursorButton_Click;
        btnY += btnHeight + btnSpacing + 5;

        // Knobs
        var knobLabel = new Label
        {
            Text = "Knobs",
            Location = new System.Drawing.Point(rightX, btnY),
            AutoSize = true,
            Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold)
        };
        btnY += 22;

        int smallBtnWidth = 62;
        var liInc = CreateGpsButton("L-In +", "Left inner knob clockwise", rightX, btnY, smallBtnWidth, btnHeight);
        liInc.Click += LeftInnerIncButton_Click;
        var liDec = CreateGpsButton("L-In -", "Left inner knob counter-clockwise", rightX + smallBtnWidth + 2, btnY, smallBtnWidth, btnHeight);
        liDec.Click += LeftInnerDecButton_Click;
        var loInc = CreateGpsButton("L-Out +", "Left outer knob clockwise", rightX + 2 * (smallBtnWidth + 2), btnY, smallBtnWidth, btnHeight);
        loInc.Click += LeftOuterIncButton_Click;
        var loDec = CreateGpsButton("L-Out -", "Left outer knob counter-clockwise", rightX + 3 * (smallBtnWidth + 2), btnY, smallBtnWidth, btnHeight);
        loDec.Click += LeftOuterDecButton_Click;
        btnY += btnHeight + btnSpacing;

        var riInc = CreateGpsButton("R-In +", "Right inner knob clockwise (scroll list down)", rightX, btnY, smallBtnWidth, btnHeight);
        riInc.Click += RightInnerIncButton_Click;
        var riDec = CreateGpsButton("R-In -", "Right inner knob counter-clockwise (scroll list up)", rightX + smallBtnWidth + 2, btnY, smallBtnWidth, btnHeight);
        riDec.Click += RightInnerDecButton_Click;
        var roInc = CreateGpsButton("R-Out +", "Right outer knob clockwise (change page group)", rightX + 2 * (smallBtnWidth + 2), btnY, smallBtnWidth, btnHeight);
        roInc.Click += RightOuterIncButton_Click;
        var roDec = CreateGpsButton("R-Out -", "Right outer knob counter-clockwise", rightX + 3 * (smallBtnWidth + 2), btnY, smallBtnWidth, btnHeight);
        roDec.Click += RightOuterDecButton_Click;
        btnY += btnHeight + btnSpacing + 5;

        // Range / map
        var rangeInBtn = CreateGpsButton("Range +", "Zoom in / decrease range", rightX, btnY, btnWidth, btnHeight);
        rangeInBtn.Click += RangeInButton_Click;
        var rangeOutBtn = CreateGpsButton("Range -", "Zoom out / increase range", rightX + btnWidth + btnSpacing, btnY, btnWidth, btnHeight);
        rangeOutBtn.Click += RangeOutButton_Click;
        btnY += btnHeight + btnSpacing + 10;

        // Action buttons
        var actionLabel = new Label
        {
            Text = "Actions",
            Location = new System.Drawing.Point(rightX, btnY),
            AutoSize = true,
            Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold)
        };
        btnY += 22;

        var readBtn = CreateGpsButton("Read Nav", "Announce current navigation", rightX, btnY, rightWidth - 10, btnHeight);
        readBtn.Click += ReadCurrentButton_Click;
        btnY += btnHeight + btnSpacing;

        var drivesBtn = CreateGpsButton("GPS↔NAV Toggle", "Toggle GPS driving NAV 1 for autopilot", rightX, btnY, rightWidth - 10, btnHeight);
        drivesBtn.Click += GpsDrivesNavButton_Click;
        btnY += btnHeight + btnSpacing;

        var actLegBtn = CreateGpsButton("Activate Leg", "Activate the waypoint selected in the list", rightX, btnY, rightWidth - 10, btnHeight);
        actLegBtn.Click += ActivateLegButton_Click;
        btnY += btnHeight + btnSpacing;

        var installBtn = CreateGpsButton("Install/Update Bridge", "Install or update the GPS accessibility mod package", rightX, btnY, rightWidth - 10, btnHeight);
        installBtn.Click += InstallBridgeButton_Click;
        btnY += btnHeight + btnSpacing;

        // Diagnostics at the bottom (full width)
        diagLabel = new Label
        {
            Text = "Bridge status: checking...",
            AccessibleName = "Bridge diagnostics",
            Location = new System.Drawing.Point(10, 630),
            Size = new System.Drawing.Size(800, 22),
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = System.Drawing.ContentAlignment.MiddleLeft
        };

        // Add everything
        this.Controls.AddRange(new Control[] {
            statusLabel, navInfoLabel, navDetailLabel,
            pageGroupLabel, pageContentList,
            grpLabel, fplBtn, procBtn, vnavBtn, menuBtn, msgBtn, obsBtn,
            inputLabel, dtoBtn, entBtn, clrBtn, cursorBtn,
            knobLabel, liInc, liDec, loInc, loDec, riInc, riDec, roInc, roDec,
            rangeInBtn, rangeOutBtn,
            actionLabel, readBtn, drivesBtn, actLegBtn, installBtn,
            diagLabel
        });
    }

    private Button CreateGpsButton(string text, string description, int x, int y, int w, int h)
    {
        return new Button
        {
            Text = text,
            AccessibleName = text,
            AccessibleDescription = description,
            Location = new System.Drawing.Point(x, y),
            Size = new System.Drawing.Size(w, h)
        };
    }
}
