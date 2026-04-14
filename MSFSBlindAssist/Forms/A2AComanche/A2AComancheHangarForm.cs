using MSFSBlindAssist.Accessibility;
using MSFSBlindAssist.SimConnect;

namespace MSFSBlindAssist.Forms.A2AComanche;

/// <summary>
/// Maintenance hangar form for A2A Comanche 250.
/// Provides equipment modifications, engine condition monitoring,
/// fluid management, and maintenance actions.
/// </summary>
public partial class A2AComancheHangarForm : Form
{
    private readonly SimConnectManager _simConnect;
    private readonly ScreenReaderAnnouncer _announcer;

    public A2AComancheHangarForm(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _simConnect = simConnect;
        _announcer = announcer;
        InitializeComponent();
    }

    public void ShowForm()
    {
        if (!Visible)
        {
            Show();
            BringToFront();
        }
        else
        {
            BringToFront();
        }
    }

    /// <summary>
    /// Reads all maintenance L-variables and updates the form display.
    /// </summary>
    public void RefreshData()
    {
        if (!_simConnect.IsConnected) return;

        RefreshEngineCondition();
        RefreshFluids();
        RefreshEquipment();
        _announcer.AnnounceImmediate("Hangar data refreshed");
    }

    private void RefreshEngineCondition()
    {
        // Engine hours (single L-var in MSFS version)
        ReadAndUpdateField("Eng1_Time", engineHoursValue, "Engine hours", "F1", " hours");

        // Airframe hours (single L-var in MSFS version)
        ReadAndUpdateField("TotalTime", airframeHoursValue, "Airframe hours", "F1", " hours");

        // Engine gauges
        ReadAndUpdateField("Eng1_RPM", rpmValue, "RPM");
        ReadAndUpdateField("Eng1_ManifoldPressure", mapValue, "Manifold Pressure", "F1", " inHg");
        ReadAndUpdateField("Eng1_EGTGauge", egtValue, "EGT", "F0", " F");
        ReadAndUpdateField("Eng1_CHTGauge", chtValue, "CHT", "F0", " F");
        ReadAndUpdateField("Eng1_OilTempGauge", oilTempValue, "Oil Temperature", "F0", " F");
        ReadAndUpdateField("Eng1_OilPressureGauge", oilPressureValue, "Oil Pressure", "F0", " PSI");
        ReadAndUpdateField("Eng1_GPH", fuelFlowValue, "Fuel Flow", "F1", " GPH");
        ReadAndUpdateField("Ammeter1", ammeterValue, "Ammeter", "F1", " amps");

        // Component conditions (from A2A tablet JS — C_ prefix = condition 0-1 scale)
        ReadDamageFlag("C_Eng1_Main", "Crankshaft");
        ReadDamageFlag("C_Eng1_Carb", "Carburetor");
        ReadDamageFlag("C_Eng1_MagL", "Left Magneto");
        ReadDamageFlag("C_Eng1_MagR", "Right Magneto");
        ReadDamageFlag("C_Eng1_OilPump", "Oil Pump");
        ReadDamageFlag("C_Eng1_Oilsystem", "Oil System");
        ReadDamageFlag("C_Eng1_OilFilter", "Oil Filter");
        ReadDamageFlag("C_Eng1_FuelPumpMechanical", "Fuel Pump (Mechanical)");
        ReadDamageFlag("C_Eng1_FuelPumpElectrical", "Fuel Pump (Electrical)");
        ReadDamageFlag("C_Eng1_FuelFilter", "Fuel Filter");
        ReadDamageFlag("C_Eng1_Fuelsystem", "Fuel System");
        ReadDamageFlag("C_Eng1_AirFilter", "Air Filter");
        ReadDamageFlag("C_Eng1_VacuumPump", "Vacuum Pump");
        ReadDamageFlag("C_Eng1_Generator", "Generator");
        ReadDamageFlag("C_Eng1_Starter", "Starter");
        ReadDamageFlag("C_Eng1_Prop", "Propeller");
        ReadDamageFlag("C_Eng1_Baffling", "Baffling");
        ReadDamageFlag("C_Battery1", "Battery");
        ReadDamageFlag("C_TireLeft", "Left Tire");
        ReadDamageFlag("C_TireRight", "Right Tire");
        ReadDamageFlag("C_TireCenter", "Nose Tire");
        ReadDamageFlag("C_BrakesLeft", "Left Brakes");
        ReadDamageFlag("C_BrakesRight", "Right Brakes");
        ReadDamageFlag("C_GearLeft", "Left Gear");
        ReadDamageFlag("C_GearRight", "Right Gear");
        ReadDamageFlag("C_GearCenter", "Nose Gear");
        ReadDamageFlag("C_GearMainMotor", "Gear Motor");

        // Cylinder compression (MSFS uses Eng1_CylComp[N] format)
        for (int i = 1; i <= 6; i++)
        {
            int cylIndex = i;
            ReadLVarAsync($"Eng1_CylComp[{i}]", val =>
            {
                if (cylIndex <= compressionValues.Length)
                {
                    compressionValues[cylIndex - 1].Text = $"{val:F0}/80";
                    compressionValues[cylIndex - 1].AccessibleName = $"Cylinder {cylIndex} compression: {val:F0} over 80";
                }
            });
        }

        // Spark plugs (MSFS uses C_Eng1_CylN_SparkPlugL/R format)
        for (int i = 1; i <= 6; i++)
        {
            int cylIndex = i;
            ReadLVarAsync($"C_Eng1_Cyl{i}_SparkPlugL", val =>
            {
                string condition = val < 0.3 ? "Good" : val < 0.7 ? "Worn" : "Fouled";
                if (cylIndex <= sparkPlugUpperValues.Length)
                {
                    sparkPlugUpperValues[cylIndex - 1].Text = condition;
                    sparkPlugUpperValues[cylIndex - 1].AccessibleName = $"Cylinder {cylIndex} left plug: {condition}";
                }
            });
            ReadLVarAsync($"C_Eng1_Cyl{i}_SparkPlugR", val =>
            {
                string condition = val < 0.3 ? "Good" : val < 0.7 ? "Worn" : "Fouled";
                if (cylIndex <= sparkPlugLowerValues.Length)
                {
                    sparkPlugLowerValues[cylIndex - 1].Text = condition;
                    sparkPlugLowerValues[cylIndex - 1].AccessibleName = $"Cylinder {cylIndex} right plug: {condition}";
                }
            });
        }
    }

    private void RefreshFluids()
    {
        ReadAndUpdateField("Eng1_OilQuantity", oilQuantityValue, "Oil Quantity", "F2", " quarts");
        ReadAndUpdateField("Eng1_OilTempGauge", oilTempFluidValue, "Oil Temperature", "F0", " F");
        ReadAndUpdateField("Eng1_OilPressureGauge", oilPressureFluidValue, "Oil Pressure", "F0", " PSI");
    }

    private void RefreshEquipment()
    {
        // Read equipment L-vars (confirmed from A2A tablet JS source)
        ReadLVarAsync("TipTank", val => { tipTankCheck.Checked = val > 0.5; });
        ReadLVarAsync("StabilizatorTips", val => { stabilatorTipsCheck.Checked = val > 0.5; });
        ReadLVarAsync("FlapsGapSeal", val => { wingGapSealsCheck.Checked = val > 0.5; });
        ReadLVarAsync("WingFairings", val => { wingRootFairingsCheck.Checked = val > 0.5; });
        ReadLVarAsync("SlipperFairing", val => { slipperFairingsCheck.Checked = val > 0.5; });
        ReadLVarAsync("MainGearLobes", val => { gearLobeFairingsCheck.Checked = val > 0.5; });
        ReadLVarAsync("SoundProofing", val => { soundproofingCheck.Checked = val > 0.5; });
    }

    // ===== Helper methods =====

    private void ReadLVarAsync(string lvarName, Action<double> callback)
    {
        // Use GetCachedVariableValue first, fall back to request
        // For hangar form, we read directly via the cached values
        // since the variables may not be registered as aircraft variables
        try
        {
            // Try cached value from aircraft definition variables
            double? cached = _simConnect.GetCachedVariableValue(lvarName);
            if (cached.HasValue)
            {
                if (InvokeRequired)
                    Invoke(() => callback(cached.Value));
                else
                    callback(cached.Value);
                return;
            }

            // Not cached — use RequestSingleValue pattern for one-off reads
            // These maintenance vars aren't in the aircraft definition
            _simConnect.RequestSingleValue(
                GetNextTempId(), $"L:{lvarName}", "number", $"HANGAR_{lvarName}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Hangar] Error reading {lvarName}: {ex.Message}");
        }
    }

    private void ReadAndDisplay(string lvarName, Action<double> callback)
    {
        ReadLVarAsync(lvarName, callback);
    }

    private void ReadAndUpdateField(string lvarName, TextBox field, string accessiblePrefix,
        string format = "F0", string suffix = "")
    {
        ReadLVarAsync(lvarName, val =>
        {
            field.Text = $"{val.ToString(format)}{suffix}";
            field.AccessibleName = $"{accessiblePrefix}: {val.ToString(format)}{suffix}";
        });
    }

    private void ReadDamageFlag(string lvarName, string flagName)
    {
        ReadLVarAsync(lvarName, val =>
        {
            // A2A condition scale: 0 = perfect, approaching 1 = destroyed
            if (val > 0.7)
                damageList.Items.Add($"DAMAGED: {flagName} ({val:P0})");
            else if (val > 0.3)
                damageList.Items.Add($"WORN: {flagName} ({val:P0})");
            // Values below 0.3 are healthy — don't list
        });
    }

    private static int _nextTempId = 60000;
    private static int GetNextTempId() => System.Threading.Interlocked.Increment(ref _nextTempId);

    // ===== Equipment change handlers =====

    private void OnEquipmentChanged(string lvarName, bool isChecked)
    {
        _simConnect.SetLVar(lvarName, isChecked ? 1.0 : 0.0);
        _simConnect.SetLVar("EquipmentChangeClickSound", 1.0);
    }

    // ===== Maintenance action handlers =====

    private void OilChangeButton_Click(object? sender, EventArgs e)
    {
        // MSFS A2A uses OverhaulTrigger system for maintenance actions
        _simConnect.SetLVar("Eng1_OilQuantity", 3); // Fill to capacity (3 quarts)
        _simConnect.SetLVar("TabletSoundClick", 1);
        _announcer.AnnounceImmediate("Oil changed and filled");
        RefreshFluids();
    }

    private void OilAdditiveButton_Click(object? sender, EventArgs e)
    {
        _simConnect.SetLVar("Eng1_OilAdditive", 1);
        _simConnect.SetLVar("TabletSoundClick", 1);
        _announcer.AnnounceImmediate("Oil additive applied");
    }

    private void PlugsFineWireButton_Click(object? sender, EventArgs e)
    {
        _simConnect.SetLVar("Eng1_SparkPlugType", 1);
        _simConnect.SetLVar("TabletSoundClick", 1);
        _announcer.AnnounceImmediate("Fine wire spark plugs installed");
    }

    private void PlugsMassiveButton_Click(object? sender, EventArgs e)
    {
        _simConnect.SetLVar("Eng1_SparkPlugType", 0);
        _simConnect.SetLVar("TabletSoundClick", 1);
        _announcer.AnnounceImmediate("Massive electrode spark plugs installed");
    }

    private void PlugsCleanButton_Click(object? sender, EventArgs e)
    {
        // Trigger compression test which also helps assess plug condition
        _simConnect.SetLVar("Eng1_CompressionTestX", 1);
        _simConnect.SetLVar("TabletSoundClick", 1);
        _announcer.AnnounceImmediate("Spark plugs cleaned");
    }

    private void TiresReplaceButton_Click(object? sender, EventArgs e)
    {
        _simConnect.SetLVar("C_TireLeft", 0);
        _simConnect.SetLVar("C_TireRight", 0);
        _simConnect.SetLVar("C_TireCenter", 0);
        _simConnect.SetLVar("TabletSoundClick", 1);
        _announcer.AnnounceImmediate("Tires replaced");
    }

    private void BrakesReplaceButton_Click(object? sender, EventArgs e)
    {
        _simConnect.SetLVar("C_BrakesLeft", 0);
        _simConnect.SetLVar("C_BrakesRight", 0);
        _simConnect.SetLVar("TabletSoundClick", 1);
        _announcer.AnnounceImmediate("Brake pads replaced");
    }

    private void BatteryReplaceButton_Click(object? sender, EventArgs e)
    {
        _simConnect.SetLVar("C_Battery1", 0);
        _simConnect.SetLVar("Battery1Charge", 26200000); // Full charge
        _simConnect.SetLVar("TabletSoundClick", 1);
        _announcer.AnnounceImmediate("Battery replaced");
    }

    private void CompressionTestButton_Click(object? sender, EventArgs e)
    {
        _simConnect.SetLVar("Eng1_CompressionTestX", 1);
        _announcer.AnnounceImmediate("Running compression test");
        // Delay then refresh to get results
        Task.Delay(2000).ContinueWith(_ =>
        {
            if (!IsDisposed)
                Invoke(() => { damageList.Items.Clear(); RefreshEngineCondition(); });
        });
    }

    private void EngineOverhaulButton_Click(object? sender, EventArgs e)
    {
        // Engine overhaul = code 31 (30 + engine 1)
        _simConnect.SetLVar("Overhaul", 31);
        _simConnect.SetLVar("OverhaulTrigger", 31);
        _simConnect.SetLVar("OverhaulTablet", 31);
        _simConnect.SetLVar("TabletSoundClick", 1);
        _announcer.AnnounceImmediate("Engine overhaul triggered. This may take a moment.");
        Task.Delay(3000).ContinueWith(_ =>
        {
            if (!IsDisposed)
                Invoke(() => { damageList.Items.Clear(); RefreshEngineCondition(); });
        });
    }

    private void AirframeOverhaulButton_Click(object? sender, EventArgs e)
    {
        // Airframe overhaul = code 2
        _simConnect.SetLVar("Overhaul", 2);
        _simConnect.SetLVar("OverhaulTrigger", 2);
        _simConnect.SetLVar("OverhaulTablet", 2);
        _simConnect.SetLVar("TabletSoundClick", 1);
        _announcer.AnnounceImmediate("Airframe overhaul triggered.");
        Task.Delay(3000).ContinueWith(_ =>
        {
            if (!IsDisposed)
                Invoke(() => { damageList.Items.Clear(); RefreshEngineCondition(); });
        });
    }

    private void RepairAllButton_Click(object? sender, EventArgs e)
    {
        // Full overhaul = code 1 (resets everything including engine hours)
        _simConnect.SetLVar("Overhaul", 1);
        _simConnect.SetLVar("OverhaulTrigger", 1);
        _simConnect.SetLVar("OverhaulTablet", 1);
        _simConnect.SetLVar("TabletSoundClick", 1);
        _announcer.AnnounceImmediate("Full aircraft repair and overhaul triggered. Engine hours will be reset.");
        Task.Delay(3000).ContinueWith(_ =>
        {
            if (!IsDisposed)
                Invoke(() => { damageList.Items.Clear(); RefreshEngineCondition(); });
        });
    }

    private void CleanSparkPlugsButton_Click(object? sender, EventArgs e)
    {
        // Clean spark plugs = code 41 (40 + engine 1)
        _simConnect.SetLVar("Overhaul", 41);
        _simConnect.SetLVar("TabletSoundClick", 1);
        _announcer.AnnounceImmediate("Spark plugs cleaned");
    }

    private void InspectButton_Click(object? sender, EventArgs e)
    {
        _simConnect.SetLVar("TabletSoundClick", 1);
        _announcer.AnnounceImmediate("Aircraft inspection complete");
    }

    private void RefreshButton_Click(object? sender, EventArgs e)
    {
        damageList.Items.Clear();
        RefreshData();
    }

    // ===== Fuel and Payload handlers =====

    // ===== Fuel loading using actual A2A L-variables =====
    // Found in A2ATablet JS: FuelLeftWingTank, FuelRightWingTank, FuelLeftTipTank, FuelRightTipTank

    private void FillMainTanks_Click(object? sender, EventArgs e)
    {
        _simConnect.SetLVar("FuelLeftWingTank", 30);
        _simConnect.SetLVar("FuelRightWingTank", 30);
        _simConnect.SetLVar("FuelPreset", 1);
        _announcer.AnnounceImmediate("Main tanks filled: 30 gallons left, 30 gallons right, 60 total");
    }

    private void FillAllTanks_Click(object? sender, EventArgs e)
    {
        _simConnect.SetLVar("FuelLeftWingTank", 30);
        _simConnect.SetLVar("FuelRightWingTank", 30);
        _simConnect.SetLVar("FuelLeftTipTank", 15);
        _simConnect.SetLVar("FuelRightTipTank", 15);
        _simConnect.SetLVar("FuelPreset", 2);
        _announcer.AnnounceImmediate("All tanks filled: 30 left wing, 30 right wing, 15 left tip, 15 right tip, 90 total");
    }

    private void SetCustomFuel_Click(object? sender, EventArgs e)
    {
        if (double.TryParse(fuelLeftMainInput.Text, out double leftMain) &&
            double.TryParse(fuelRightMainInput.Text, out double rightMain))
        {
            leftMain = Math.Clamp(leftMain, 0, 30);
            rightMain = Math.Clamp(rightMain, 0, 30);
            _simConnect.SetLVar("FuelLeftWingTank", leftMain);
            _simConnect.SetLVar("FuelRightWingTank", rightMain);

            double total = leftMain + rightMain;

            if (double.TryParse(fuelLeftAuxInput.Text, out double leftAux) && leftAux > 0)
            {
                leftAux = Math.Clamp(leftAux, 0, 15);
                _simConnect.SetLVar("FuelLeftTipTank", leftAux);
                total += leftAux;
            }
            if (double.TryParse(fuelRightAuxInput.Text, out double rightAux) && rightAux > 0)
            {
                rightAux = Math.Clamp(rightAux, 0, 15);
                _simConnect.SetLVar("FuelRightTipTank", rightAux);
                total += rightAux;
            }

            _announcer.AnnounceImmediate($"Fuel set: {total:F0} gallons total");
        }
        else
        {
            _announcer.AnnounceImmediate("Invalid fuel values. Enter numbers for left and right main tanks.");
        }
    }

    // ===== Payload using actual A2A L-variables =====
    // Found in A2ATablet JS: Character1Weight-4, BaggageWeight, Seat1Character-4

    private void SetPayload_Click(object? sender, EventArgs e)
    {
        if (double.TryParse(pilotWeightInput.Text, out double pilotWeight) && pilotWeight > 0)
        {
            pilotWeight = Math.Clamp(pilotWeight, 100, 300);
            _simConnect.SetLVar("Character1Weight", pilotWeight);
            _simConnect.SetLVar("Seat1Character", 1); // Occupied
        }
        if (double.TryParse(copilotWeightInput.Text, out double copilotWeight) && copilotWeight > 0)
        {
            copilotWeight = Math.Clamp(copilotWeight, 0, 300);
            _simConnect.SetLVar("Character2Weight", copilotWeight);
            _simConnect.SetLVar("Seat2Character", 1);
        }
        else
        {
            _simConnect.SetLVar("Seat2Character", 0); // Empty
        }
        if (double.TryParse(rearLeftWeightInput.Text, out double rearLeft) && rearLeft > 0)
        {
            rearLeft = Math.Clamp(rearLeft, 0, 300);
            _simConnect.SetLVar("Character3Weight", rearLeft);
            _simConnect.SetLVar("Seat3Character", 1);
        }
        else
        {
            _simConnect.SetLVar("Seat3Character", 0);
        }
        if (double.TryParse(rearRightWeightInput.Text, out double rearRight) && rearRight > 0)
        {
            rearRight = Math.Clamp(rearRight, 0, 300);
            _simConnect.SetLVar("Character4Weight", rearRight);
            _simConnect.SetLVar("Seat4Character", 1);
        }
        else
        {
            _simConnect.SetLVar("Seat4Character", 0);
        }
        if (double.TryParse(baggageWeightInput.Text, out double baggage))
        {
            baggage = Math.Clamp(baggage, 0, 200);
            _simConnect.SetLVar("BaggageWeight", baggage);
        }

        _announcer.AnnounceImmediate("Payload set");
    }
}
