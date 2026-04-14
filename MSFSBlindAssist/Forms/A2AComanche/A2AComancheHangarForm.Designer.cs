namespace MSFSBlindAssist.Forms.A2AComanche;

partial class A2AComancheHangarForm
{
    private Controls.AccessibleTabControl tabControl = null!;
    private TabPage fuelPayloadTab = null!;
    private TabPage equipmentTab = null!;
    private TabPage engineConditionTab = null!;
    private TabPage fluidsTab = null!;
    private TabPage maintenanceTab = null!;
    private TabPage walkaroundTab = null!;
    private TabPage settingsTab = null!;

    // Fuel/payload tab controls
    private TextBox fuelLeftMainInput = null!;
    private TextBox fuelRightMainInput = null!;
    private TextBox fuelLeftAuxInput = null!;
    private TextBox fuelRightAuxInput = null!;
    private TextBox pilotWeightInput = null!;
    private TextBox copilotWeightInput = null!;
    private TextBox rearLeftWeightInput = null!;
    private TextBox rearRightWeightInput = null!;
    private TextBox baggageWeightInput = null!;

    // Equipment tab controls
    private CheckBox tipTankCheck = null!;
    private CheckBox stabilatorTipsCheck = null!;
    private CheckBox wingGapSealsCheck = null!;
    private CheckBox wingRootFairingsCheck = null!;
    private CheckBox slipperFairingsCheck = null!;
    private CheckBox gearLobeFairingsCheck = null!;
    private CheckBox soundproofingCheck = null!;

    // Engine condition tab controls
    private TextBox engineHoursValue = null!;
    private TextBox airframeHoursValue = null!;
    private TextBox rpmValue = null!;
    private TextBox mapValue = null!;
    private TextBox egtValue = null!;
    private TextBox chtValue = null!;
    private TextBox oilTempValue = null!;
    private TextBox oilPressureValue = null!;
    private TextBox fuelFlowValue = null!;
    private TextBox ammeterValue = null!;
    private ListBox damageList = null!;
    private TextBox[] compressionValues = null!;
    private TextBox[] sparkPlugUpperValues = null!;
    private TextBox[] sparkPlugLowerValues = null!;

    // Fluids tab controls
    private TextBox oilQuantityValue = null!;
    private TextBox oilTempFluidValue = null!;
    private TextBox oilPressureFluidValue = null!;

    // Maintenance tab buttons
    private Button refreshButton = null!;

    private void InitializeComponent()
    {
        this.Text = "A2A Comanche 250 - Maintenance Hangar";
        this.AccessibleName = "Maintenance Hangar";
        this.AccessibleDescription = "Maintenance hangar for the A2A Comanche 250. Use tabs to navigate between equipment, engine condition, fluids, and maintenance actions.";
        this.Size = new System.Drawing.Size(550, 600);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.StartPosition = FormStartPosition.CenterScreen;

        tabControl = new Controls.AccessibleTabControl();
        tabControl.Dock = DockStyle.Fill;

        // ===== TAB 0: Fuel and Payload =====
        fuelPayloadTab = new TabPage("Fuel and Payload");
        fuelPayloadTab.Padding = new Padding(10);
        fuelPayloadTab.AutoScroll = true;
        int y = 10;

        var fuelHeaderLabel = CreateLabel("Fuel (main tanks: 30 gal each, tip tanks: 15 gal each):", ref y);

        var fillMainBtn = new Button
        {
            Text = "Fill Main Tanks (60 gal)",
            AccessibleName = "Fill main fuel tanks to 60 gallons",
            AccessibleDescription = "Fills left and right main wing tanks to 30 gallons each",
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(240, 30)
        };
        fillMainBtn.Click += FillMainTanks_Click;
        y += 35;

        var fillAllBtn = new Button
        {
            Text = "Fill All Tanks (90 gal)",
            AccessibleName = "Fill all fuel tanks to 90 gallons",
            AccessibleDescription = "Fills main tanks to 30 gallons each and tip tanks to 15 gallons each",
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(240, 30)
        };
        fillAllBtn.Click += FillAllTanks_Click;
        y += 40;

        var customFuelLabel = CreateLabel("Custom Fuel (gallons):", ref y);

        var leftMainLabel = new Label { Text = "Left Main (0-30):", Location = new System.Drawing.Point(10, y), AutoSize = true };
        fuelLeftMainInput = new TextBox
        {
            Text = "30",
            Location = new System.Drawing.Point(200, y),
            Size = new System.Drawing.Size(80, 22),
            AccessibleName = "Left main tank gallons"
        };
        y += 28;

        var rightMainLabel = new Label { Text = "Right Main (0-30):", Location = new System.Drawing.Point(10, y), AutoSize = true };
        fuelRightMainInput = new TextBox
        {
            Text = "30",
            Location = new System.Drawing.Point(200, y),
            Size = new System.Drawing.Size(80, 22),
            AccessibleName = "Right main tank gallons"
        };
        y += 28;

        var leftAuxLabel = new Label { Text = "Left Tip Tank (0-15):", Location = new System.Drawing.Point(10, y), AutoSize = true };
        fuelLeftAuxInput = new TextBox
        {
            Text = "0",
            Location = new System.Drawing.Point(200, y),
            Size = new System.Drawing.Size(80, 22),
            AccessibleName = "Left tip tank gallons"
        };
        y += 28;

        var rightAuxLabel = new Label { Text = "Right Tip Tank (0-15):", Location = new System.Drawing.Point(10, y), AutoSize = true };
        fuelRightAuxInput = new TextBox
        {
            Text = "0",
            Location = new System.Drawing.Point(200, y),
            Size = new System.Drawing.Size(80, 22),
            AccessibleName = "Right tip tank gallons"
        };
        y += 35;

        var setFuelBtn = new Button
        {
            Text = "Set Custom Fuel",
            AccessibleName = "Set custom fuel quantities",
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(200, 30)
        };
        setFuelBtn.Click += SetCustomFuel_Click;
        y += 45;

        var payloadLabel = CreateLabel("Payload (pounds):", ref y);

        var pilotLabel = new Label { Text = "Pilot (100-300):", Location = new System.Drawing.Point(10, y), AutoSize = true };
        pilotWeightInput = new TextBox
        {
            Text = "170",
            Location = new System.Drawing.Point(200, y),
            Size = new System.Drawing.Size(80, 22),
            AccessibleName = "Pilot weight in pounds"
        };
        y += 28;

        var copilotLabel = new Label { Text = "Copilot (0-300):", Location = new System.Drawing.Point(10, y), AutoSize = true };
        copilotWeightInput = new TextBox
        {
            Text = "0",
            Location = new System.Drawing.Point(200, y),
            Size = new System.Drawing.Size(80, 22),
            AccessibleName = "Copilot weight in pounds"
        };
        y += 28;

        var rearLeftLabel = new Label { Text = "Rear Left (0-300):", Location = new System.Drawing.Point(10, y), AutoSize = true };
        rearLeftWeightInput = new TextBox
        {
            Text = "0",
            Location = new System.Drawing.Point(200, y),
            Size = new System.Drawing.Size(80, 22),
            AccessibleName = "Rear left passenger weight in pounds"
        };
        y += 28;

        var rearRightLabel = new Label { Text = "Rear Right (0-300):", Location = new System.Drawing.Point(10, y), AutoSize = true };
        rearRightWeightInput = new TextBox
        {
            Text = "0",
            Location = new System.Drawing.Point(200, y),
            Size = new System.Drawing.Size(80, 22),
            AccessibleName = "Rear right passenger weight in pounds"
        };
        y += 28;

        var baggageLabel = new Label { Text = "Baggage (0-200):", Location = new System.Drawing.Point(10, y), AutoSize = true };
        baggageWeightInput = new TextBox
        {
            Text = "0",
            Location = new System.Drawing.Point(200, y),
            Size = new System.Drawing.Size(80, 22),
            AccessibleName = "Baggage weight in pounds"
        };
        y += 35;

        var setPayloadBtn = new Button
        {
            Text = "Set Payload",
            AccessibleName = "Set passenger and baggage weights",
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(200, 30)
        };
        setPayloadBtn.Click += SetPayload_Click;

        fuelPayloadTab.Controls.AddRange(new Control[] {
            fuelHeaderLabel, fillMainBtn, fillAllBtn,
            customFuelLabel, leftMainLabel, fuelLeftMainInput,
            rightMainLabel, fuelRightMainInput,
            leftAuxLabel, fuelLeftAuxInput,
            rightAuxLabel, fuelRightAuxInput,
            setFuelBtn,
            payloadLabel, pilotLabel, pilotWeightInput,
            copilotLabel, copilotWeightInput,
            rearLeftLabel, rearLeftWeightInput,
            rearRightLabel, rearRightWeightInput,
            baggageLabel, baggageWeightInput,
            setPayloadBtn
        });

        // ===== TAB 1: Equipment =====
        equipmentTab = new TabPage("Equipment");
        equipmentTab.Padding = new Padding(10);
        y = 10;

        tipTankCheck = CreateEquipmentCheck("Tip Tanks", "Install auxiliary fuel tip tanks", ref y);
        tipTankCheck.CheckedChanged += (s, e) => OnEquipmentChanged("TipTank", tipTankCheck.Checked);

        stabilatorTipsCheck = CreateEquipmentCheck("Stabilator Tips", "Install stabilator tip extensions", ref y);
        stabilatorTipsCheck.CheckedChanged += (s, e) => OnEquipmentChanged("StabilizatorTips", stabilatorTipsCheck.Checked);

        wingGapSealsCheck = CreateEquipmentCheck("Wing Gap Seals", "Install wing gap seals for reduced drag", ref y);
        wingGapSealsCheck.CheckedChanged += (s, e) => OnEquipmentChanged("FlapsGapSeal", wingGapSealsCheck.Checked);

        wingRootFairingsCheck = CreateEquipmentCheck("Wing Root Fairings", "Install wing root fairings", ref y);
        wingRootFairingsCheck.CheckedChanged += (s, e) => OnEquipmentChanged("WingFairings", wingRootFairingsCheck.Checked);

        slipperFairingsCheck = CreateEquipmentCheck("Slipper Fairings", "Install slipper fairings", ref y);
        slipperFairingsCheck.CheckedChanged += (s, e) => OnEquipmentChanged("SlipperFairing", slipperFairingsCheck.Checked);

        gearLobeFairingsCheck = CreateEquipmentCheck("Gear Lobe Fairings", "Install gear lobe fairings", ref y);
        gearLobeFairingsCheck.CheckedChanged += (s, e) => OnEquipmentChanged("MainGearLobes", gearLobeFairingsCheck.Checked);

        soundproofingCheck = CreateEquipmentCheck("Soundproofing", "Install cabin soundproofing", ref y);
        soundproofingCheck.CheckedChanged += (s, e) => OnEquipmentChanged("SoundProofing", soundproofingCheck.Checked);

        equipmentTab.Controls.AddRange(new Control[] {
            tipTankCheck, stabilatorTipsCheck, wingGapSealsCheck, wingRootFairingsCheck,
            slipperFairingsCheck, gearLobeFairingsCheck, soundproofingCheck
        });

        // ===== TAB 2: Engine Condition =====
        engineConditionTab = new TabPage("Engine Condition");
        engineConditionTab.Padding = new Padding(10);
        engineConditionTab.AutoScroll = true;
        y = 10;

        var hoursLabel = CreateLabel("Hours:", ref y);
        engineHoursValue = CreateReadOnlyField("Engine hours", ref y);
        airframeHoursValue = CreateReadOnlyField("Airframe hours", ref y);

        y += 10;
        var gaugesLabel = CreateLabel("Engine Gauges:", ref y);
        rpmValue = CreateReadOnlyField("RPM", ref y);
        mapValue = CreateReadOnlyField("Manifold Pressure", ref y);
        egtValue = CreateReadOnlyField("EGT", ref y);
        chtValue = CreateReadOnlyField("CHT", ref y);
        oilTempValue = CreateReadOnlyField("Oil Temperature", ref y);
        oilPressureValue = CreateReadOnlyField("Oil Pressure", ref y);
        fuelFlowValue = CreateReadOnlyField("Fuel Flow", ref y);
        ammeterValue = CreateReadOnlyField("Ammeter", ref y);

        y += 10;
        var damageLabel = CreateLabel("Damage Report:", ref y);
        damageList = new ListBox
        {
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(500, 80),
            AccessibleName = "Damage report"
        };
        y += 85;

        y += 10;
        var compLabel = CreateLabel("Cylinder Compression:", ref y);
        compressionValues = new TextBox[6];
        for (int i = 0; i < 6; i++)
        {
            compressionValues[i] = CreateReadOnlyField($"Cylinder {i + 1} compression", ref y);
        }

        y += 10;
        var plugLabel = CreateLabel("Spark Plugs:", ref y);
        sparkPlugUpperValues = new TextBox[6];
        sparkPlugLowerValues = new TextBox[6];
        for (int i = 0; i < 6; i++)
        {
            sparkPlugUpperValues[i] = CreateReadOnlyField($"Cylinder {i + 1} upper plug", ref y);
            sparkPlugLowerValues[i] = CreateReadOnlyField($"Cylinder {i + 1} lower plug", ref y);
        }

        engineConditionTab.Controls.AddRange(new Control[] { hoursLabel, engineHoursValue, airframeHoursValue });
        engineConditionTab.Controls.AddRange(new Control[] { gaugesLabel, rpmValue, mapValue, egtValue, chtValue, oilTempValue, oilPressureValue, fuelFlowValue, ammeterValue });
        engineConditionTab.Controls.AddRange(new Control[] { damageLabel, damageList });
        engineConditionTab.Controls.Add(compLabel);
        engineConditionTab.Controls.AddRange(compressionValues);
        engineConditionTab.Controls.Add(plugLabel);
        engineConditionTab.Controls.AddRange(sparkPlugUpperValues);
        engineConditionTab.Controls.AddRange(sparkPlugLowerValues);

        // ===== TAB 3: Fluids =====
        fluidsTab = new TabPage("Fluids");
        fluidsTab.Padding = new Padding(10);
        y = 10;

        var oilLabel = CreateLabel("Oil (capacity: 12 quarts):", ref y);
        oilQuantityValue = CreateReadOnlyField("Oil quantity", ref y);
        oilTempFluidValue = CreateReadOnlyField("Oil temperature", ref y);
        oilPressureFluidValue = CreateReadOnlyField("Oil pressure", ref y);

        fluidsTab.Controls.AddRange(new Control[] { oilLabel, oilQuantityValue, oilTempFluidValue, oilPressureFluidValue });

        // ===== TAB 4: Maintenance =====
        maintenanceTab = new TabPage("Maintenance");
        maintenanceTab.Padding = new Padding(10);
        y = 10;

        var oilActionsLabel = CreateLabel("Oil:", ref y);
        var oilChangeBtn = CreateActionButton("Change Oil", "Drain and replace engine oil", ref y);
        oilChangeBtn.Click += OilChangeButton_Click;
        var oilAdditiveBtn = CreateActionButton("Add Oil Additive", "Add CamGuard oil additive", ref y);
        oilAdditiveBtn.Click += OilAdditiveButton_Click;

        y += 10;
        var plugActionsLabel = CreateLabel("Spark Plugs:", ref y);
        var plugsFineWireBtn = CreateActionButton("Install Fine Wire Plugs", "Replace with fine wire spark plugs", ref y);
        plugsFineWireBtn.Click += PlugsFineWireButton_Click;
        var plugsMassiveBtn = CreateActionButton("Install Massive Electrode Plugs", "Replace with massive electrode spark plugs", ref y);
        plugsMassiveBtn.Click += PlugsMassiveButton_Click;
        var plugsCleanBtn = CreateActionButton("Clean Spark Plugs", "Clean all spark plugs", ref y);
        plugsCleanBtn.Click += PlugsCleanButton_Click;

        y += 10;
        var mechActionsLabel = CreateLabel("Mechanical:", ref y);
        var tiresBtn = CreateActionButton("Replace Tires", "Replace all tires", ref y);
        tiresBtn.Click += TiresReplaceButton_Click;
        var brakesBtn = CreateActionButton("Replace Brake Pads", "Replace all brake pads", ref y);
        brakesBtn.Click += BrakesReplaceButton_Click;
        var batteryBtn = CreateActionButton("Replace Battery", "Install new battery", ref y);
        batteryBtn.Click += BatteryReplaceButton_Click;

        var plugsCleanBtn2 = CreateActionButton("Clean Spark Plugs (AccuSim)", "Clean spark plugs via AccuSim overhaul system", ref y);
        plugsCleanBtn2.Click += CleanSparkPlugsButton_Click;

        y += 10;
        var engineActionsLabel = CreateLabel("Engine:", ref y);
        var compTestBtn = CreateActionButton("Run Compression Test", "Run compression test on all cylinders", ref y);
        compTestBtn.Click += CompressionTestButton_Click;
        var overhaulEngineBtn = CreateActionButton("Engine Overhaul", "Overhaul engine (resets engine hours and wear)", ref y);
        overhaulEngineBtn.Click += EngineOverhaulButton_Click;
        var overhaulAirframeBtn = CreateActionButton("Airframe Overhaul", "Overhaul airframe (resets airframe damage)", ref y);
        overhaulAirframeBtn.Click += AirframeOverhaulButton_Click;

        y += 15;
        var repairAllLabel = CreateLabel("Full Repair:", ref y);
        var repairAllBtn = CreateActionButton("REPAIR ALL DAMAGE", "Complete overhaul of entire aircraft. Resets all damage, wear, and engine hours.", ref y);
        repairAllBtn.Click += RepairAllButton_Click;
        repairAllBtn.Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold);
        repairAllBtn.Size = new System.Drawing.Size(350, 35);

        y += 10;
        var inspectBtn = CreateActionButton("Inspect Aircraft", "Mark aircraft as inspected", ref y);
        inspectBtn.Click += InspectButton_Click;

        maintenanceTab.Controls.AddRange(new Control[] {
            oilActionsLabel, oilChangeBtn, oilAdditiveBtn,
            plugActionsLabel, plugsFineWireBtn, plugsMassiveBtn, plugsCleanBtn, plugsCleanBtn2,
            mechActionsLabel, tiresBtn, brakesBtn, batteryBtn,
            engineActionsLabel, compTestBtn, overhaulEngineBtn, overhaulAirframeBtn,
            repairAllLabel, repairAllBtn,
            inspectBtn
        });

        // ===== TAB 5: Walkaround / Ground Ops =====
        walkaroundTab = new TabPage("Walkaround");
        walkaroundTab.Padding = new Padding(10);
        walkaroundTab.AutoScroll = true;
        y = 10;

        var walkaroundLabel = CreateLabel("Walkaround Items:", ref y);

        var cbTieDownLeft = CreateEquipmentCheck("Left Wing Tie Down", "Toggle left wing tie down", ref y);
        cbTieDownLeft.CheckedChanged += (s, e) => _simConnect.SetLVar("vis_WingLeft_TieDown", cbTieDownLeft.Checked ? 1 : 0);

        var cbTieDownRight = CreateEquipmentCheck("Right Wing Tie Down", "Toggle right wing tie down", ref y);
        cbTieDownRight.CheckedChanged += (s, e) => _simConnect.SetLVar("vis_WingRight_TieDown", cbTieDownRight.Checked ? 1 : 0);

        var cbTieDownTail = CreateEquipmentCheck("Tail Tie Down", "Toggle tail tie down", ref y);
        cbTieDownTail.CheckedChanged += (s, e) => _simConnect.SetLVar("vis_Tail_TieDown", cbTieDownTail.Checked ? 1 : 0);

        var cbChockLeft = CreateEquipmentCheck("Left Wheel Chock", "Toggle left wheel chock", ref y);
        cbChockLeft.CheckedChanged += (s, e) => _simConnect.SetLVar("vis_WheelChockL", cbChockLeft.Checked ? 1 : 0);

        var cbChockRight = CreateEquipmentCheck("Right Wheel Chock", "Toggle right wheel chock", ref y);
        cbChockRight.CheckedChanged += (s, e) => _simConnect.SetLVar("vis_WheelChockR", cbChockRight.Checked ? 1 : 0);

        var cbPitotCover = CreateEquipmentCheck("Pitot Tube Cover", "Toggle pitot tube cover", ref y);
        cbPitotCover.CheckedChanged += (s, e) => _simConnect.SetLVar("PitotTubeCover", cbPitotCover.Checked ? 1 : 0);

        var cbControlsLock = CreateEquipmentCheck("Controls Lock", "Toggle controls lock lever", ref y);
        cbControlsLock.CheckedChanged += (s, e) => _simConnect.SetLVar("ControlsLockLever", cbControlsLock.Checked ? 1 : 0);

        y += 10;
        var groundOpsLabel = CreateLabel("Ground Operations:", ref y);

        var btnOpenDoor = CreateActionButton("Open Cabin Door", "Open the main cabin door", ref y);
        btnOpenDoor.Click += (s, e) => { _simConnect.SetLVar("Door1AutoOpen", 1); _announcer.AnnounceImmediate("Door opening"); };

        var btnCloseDoor = CreateActionButton("Close Cabin Door", "Close the main cabin door", ref y);
        btnCloseDoor.Click += (s, e) => { _simConnect.SetLVar("Door1AutoOpen", 2); _announcer.AnnounceImmediate("Door closing"); };

        var btnBaggageDoor = CreateActionButton("Toggle Baggage Door", "Open or close the baggage door", ref y);
        btnBaggageDoor.Click += (s, e) => { _simConnect.SetLVar("WALKAROUND_BaggageDoor_Click", 1); _announcer.AnnounceImmediate("Baggage door toggled"); };

        var cbOnJacks = CreateEquipmentCheck("Put Aircraft on Jacks", "Raise aircraft on maintenance jacks", ref y);
        cbOnJacks.CheckedChanged += (s, e) => _simConnect.SetLVar("OnJacks", cbOnJacks.Checked ? 1 : 0);

        var cbTow = CreateEquipmentCheck("Attach Tow Bar", "Connect tow bar to nose wheel", ref y);
        cbTow.CheckedChanged += (s, e) => _simConnect.SetLVar("Tow", cbTow.Checked ? 1 : 0);

        walkaroundTab.Controls.AddRange(new Control[] {
            walkaroundLabel, cbTieDownLeft, cbTieDownRight, cbTieDownTail,
            cbChockLeft, cbChockRight, cbPitotCover, cbControlsLock,
            groundOpsLabel, btnOpenDoor, btnCloseDoor, btnBaggageDoor,
            cbOnJacks, cbTow
        });

        // ===== TAB 6: Settings / Options =====
        settingsTab = new TabPage("Settings");
        settingsTab.Padding = new Padding(10);
        settingsTab.AutoScroll = true;
        y = 10;

        var startupLabel = CreateLabel("Startup:", ref y);

        var cbAutoStartBtn = CreateActionButton("Auto Start (Ready to Fly)", "Start aircraft ready to fly", ref y);
        cbAutoStartBtn.Click += (s, e) => { _simConnect.SetLVar("AutoStartTablet", 1); _announcer.AnnounceImmediate("Auto start triggered"); };

        var cbColdDarkBtn = CreateActionButton("Cold and Dark", "Set aircraft to cold and dark state", ref y);
        cbColdDarkBtn.Click += (s, e) => { _simConnect.SetLVar("ColdStartTablet", 1); _announcer.AnnounceImmediate("Cold and dark triggered"); };

        var cbPersistence = CreateEquipmentCheck("Cockpit Persistence", "Remember switch positions between flights", ref y);
        cbPersistence.CheckedChanged += (s, e) => _simConnect.SetLVar("CockpitPersistence", cbPersistence.Checked ? 1 : 0);

        y += 10;
        var simLabel = CreateLabel("Simulation:", ref y);

        var cbDamage = CreateEquipmentCheck("Damage Enabled", "Enable engine and airframe damage simulation", ref y);
        cbDamage.CheckedChanged += (s, e) => _simConnect.SetLVar("Damage", cbDamage.Checked ? 1 : 0);

        var cbAccuTurbulence = CreateEquipmentCheck("Accu-Turbulence", "Enable A2A enhanced turbulence", ref y);
        cbAccuTurbulence.CheckedChanged += (s, e) => _simConnect.SetLVar("AccuTurbulence", cbAccuTurbulence.Checked ? 1 : 0);

        var cbCrewVisible = CreateEquipmentCheck("Crew Visible in Cockpit", "Show passenger models in virtual cockpit", ref y);
        cbCrewVisible.CheckedChanged += (s, e) => _simConnect.SetLVar("VC_Crew_Visible", cbCrewVisible.Checked ? 1 : 0);

        var cbHeadphones = CreateEquipmentCheck("Headphones", "Toggle active noise cancelling headphones", ref y);
        cbHeadphones.CheckedChanged += (s, e) => _simConnect.SetLVar("Headphones", cbHeadphones.Checked ? 1 : 0);

        var cb3wayGear = CreateEquipmentCheck("3-Way Gear Switch", "Use 3-position gear switch", ref y);
        cb3wayGear.CheckedChanged += (s, e) => _simConnect.SetLVar("Gear3waySwitch", cb3wayGear.Checked ? 1 : 0);

        var cbRealisticBrake = CreateEquipmentCheck("Realistic Parking Brake", "Require toe brakes before pulling parking brake", ref y);
        cbRealisticBrake.CheckedChanged += (s, e) => _simConnect.SetLVar("RealisticParkingBrake", cbRealisticBrake.Checked ? 1 : 0);

        var cbComSpacing = CreateEquipmentCheck("8.33 kHz COM Radios", "Use 8.33 kHz channel spacing", ref y);
        cbComSpacing.CheckedChanged += (s, e) => _simConnect.SetLVar("ComChannelSpacing", cbComSpacing.Checked ? 1 : 0);

        var cbGyroDrift = CreateEquipmentCheck("Gyro Drift", "Enable realistic gyro drift", ref y);
        cbGyroDrift.CheckedChanged += (s, e) => _simConnect.SetLVar("GyroDriftOn", cbGyroDrift.Checked ? 1 : 0);

        var cbGyroSound = CreateEquipmentCheck("Gyro Sound", "Enable gyro instrument sounds", ref y);
        cbGyroSound.CheckedChanged += (s, e) => _simConnect.SetLVar("GyroSoundOn", cbGyroSound.Checked ? 1 : 0);

        var cbTrimAccel = CreateEquipmentCheck("Trim Acceleration", "Enable trim acceleration when held", ref y);
        cbTrimAccel.CheckedChanged += (s, e) => _simConnect.SetLVar("TrimAcceleration", cbTrimAccel.Checked ? 1 : 0);

        y += 10;
        var gpsLabel = CreateLabel("GPS Configuration:", ref y);

        var btnNoGps = CreateActionButton("No GPS", "Remove GPS unit", ref y);
        btnNoGps.Click += (s, e) => { _simConnect.SetLVar("AvionicsConfiguration", 0); _announcer.AnnounceImmediate("GPS removed"); };

        var btnGns430 = CreateActionButton("GNS 430", "Install Garmin GNS 430", ref y);
        btnGns430.Click += (s, e) => { _simConnect.SetLVar("AvionicsConfiguration", 1); _announcer.AnnounceImmediate("GNS 430 installed"); };

        var btnGns530 = CreateActionButton("GNS 530", "Install Garmin GNS 530", ref y);
        btnGns530.Click += (s, e) => { _simConnect.SetLVar("AvionicsConfiguration", 2); _announcer.AnnounceImmediate("GNS 530 installed"); };

        var btnDualGns = CreateActionButton("Dual GNS (430 + 530)", "Install both GNS 430 and GNS 530", ref y);
        btnDualGns.Click += (s, e) => { _simConnect.SetLVar("AvionicsConfiguration", 3); _announcer.AnnounceImmediate("Dual GNS 430 and 530 installed"); };

        settingsTab.Controls.AddRange(new Control[] {
            startupLabel, cbAutoStartBtn, cbColdDarkBtn, cbPersistence,
            simLabel, cbDamage, cbAccuTurbulence, cbCrewVisible, cbHeadphones,
            cb3wayGear, cbRealisticBrake, cbComSpacing, cbGyroDrift, cbGyroSound, cbTrimAccel,
            gpsLabel, btnNoGps, btnGns430, btnGns530, btnDualGns
        });

        // ===== Assemble =====
        tabControl.TabPages.Add(fuelPayloadTab);
        tabControl.TabPages.Add(equipmentTab);
        tabControl.TabPages.Add(engineConditionTab);
        tabControl.TabPages.Add(fluidsTab);
        tabControl.TabPages.Add(maintenanceTab);
        tabControl.TabPages.Add(walkaroundTab);
        tabControl.TabPages.Add(settingsTab);

        // Refresh button at bottom
        refreshButton = new Button
        {
            Text = "Refresh All Data",
            AccessibleName = "Refresh all hangar data",
            Dock = DockStyle.Bottom,
            Height = 35
        };
        refreshButton.Click += RefreshButton_Click;

        this.Controls.Add(tabControl);
        this.Controls.Add(refreshButton);
    }

    // ===== Control creation helpers =====

    private CheckBox CreateEquipmentCheck(string text, string description, ref int y)
    {
        var cb = new CheckBox
        {
            Text = text,
            AccessibleName = text,
            AccessibleDescription = description,
            Location = new System.Drawing.Point(10, y),
            AutoSize = true
        };
        y += 28;
        return cb;
    }

    private Label CreateLabel(string text, ref int y)
    {
        var label = new Label
        {
            Text = text,
            Location = new System.Drawing.Point(10, y),
            AutoSize = true,
            Font = new System.Drawing.Font(System.Drawing.SystemFonts.DefaultFont, System.Drawing.FontStyle.Bold)
        };
        y += 22;
        return label;
    }

    private TextBox CreateReadOnlyField(string accessibleName, ref int y)
    {
        var field = new TextBox
        {
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(500, 22),
            ReadOnly = true,
            AccessibleName = accessibleName,
            Text = "---"
        };
        y += 26;
        return field;
    }

    private Button CreateActionButton(string text, string description, ref int y)
    {
        var btn = new Button
        {
            Text = text,
            AccessibleName = text,
            AccessibleDescription = description,
            Location = new System.Drawing.Point(10, y),
            Size = new System.Drawing.Size(300, 30)
        };
        y += 35;
        return btn;
    }
}
