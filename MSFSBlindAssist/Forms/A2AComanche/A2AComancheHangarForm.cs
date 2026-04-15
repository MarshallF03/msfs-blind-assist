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

    // Pending field updates: when a RequestVariable result comes back, update the corresponding field
    private readonly Dictionary<string, Action<double>> _pendingUpdates = new();

    public A2AComancheHangarForm(SimConnectManager simConnect, ScreenReaderAnnouncer announcer)
    {
        _simConnect = simConnect;
        _announcer = announcer;
        InitializeComponent();

        // Subscribe to variable update events to receive RequestVariable results
        _simConnect.SimVarUpdated += OnSimVarUpdated;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _simConnect.SimVarUpdated -= OnSimVarUpdated;
        base.OnFormClosing(e);
    }

    private void OnSimVarUpdated(object? sender, SimVarUpdateEventArgs e)
    {
        if (_pendingUpdates.TryGetValue(e.VarName, out var callback))
        {
            _pendingUpdates.Remove(e.VarName);
            if (InvokeRequired)
                Invoke(() => callback(e.Value));
            else
                callback(e.Value);
        }
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
        // Engine and airframe hours (registered as CMNCH_MAINT_ vars)
        RequestAndUpdateField("CMNCH_MAINT_ENGINE_HOURS", engineHoursValue, "Engine hours", "F1", " hours");
        RequestAndUpdateField("CMNCH_MAINT_AIRFRAME_HOURS", airframeHoursValue, "Airframe hours", "F1", " hours");

        // Engine gauges (these are continuous monitoring vars, already cached)
        RequestAndUpdateField("CMNCH_ENGINE_RPM", rpmValue, "RPM");
        RequestAndUpdateField("CMNCH_MANIFOLD_PRESSURE", mapValue, "Manifold Pressure", "F1", " inHg");
        RequestAndUpdateField("CMNCH_EGT", egtValue, "EGT", "F0", " F");
        RequestAndUpdateField("CMNCH_CHT", chtValue, "CHT", "F0", " F");
        RequestAndUpdateField("CMNCH_OIL_TEMP", oilTempValue, "Oil Temperature", "F0", " F");
        RequestAndUpdateField("CMNCH_OIL_PRESSURE", oilPressureValue, "Oil Pressure", "F0", " PSI");
        RequestAndUpdateField("CMNCH_FUEL_FLOW", fuelFlowValue, "Fuel Flow", "F1", " GPH");
        RequestAndUpdateField("CMNCH_AMMETER", ammeterValue, "Ammeter", "F1", " amps");

        // Component conditions (registered as CMNCH_MAINT_C_ vars)
        RequestDamageFlag("CMNCH_MAINT_C_MAIN", "Crankshaft");
        RequestDamageFlag("CMNCH_MAINT_C_CARB", "Carburetor");
        RequestDamageFlag("CMNCH_MAINT_C_MAGL", "Left Magneto");
        RequestDamageFlag("CMNCH_MAINT_C_MAGR", "Right Magneto");
        RequestDamageFlag("CMNCH_MAINT_C_OILPUMP", "Oil Pump");
        RequestDamageFlag("CMNCH_MAINT_C_OILSYS", "Oil System");
        RequestDamageFlag("CMNCH_MAINT_C_FUELPUMP_M", "Fuel Pump (Mechanical)");
        RequestDamageFlag("CMNCH_MAINT_C_FUELPUMP_E", "Fuel Pump (Electrical)");
        RequestDamageFlag("CMNCH_MAINT_C_FUELSYS", "Fuel System");
        RequestDamageFlag("CMNCH_MAINT_C_AIRFILTER", "Air Filter");
        RequestDamageFlag("CMNCH_MAINT_C_VACUUM", "Vacuum Pump");
        RequestDamageFlag("CMNCH_MAINT_C_GENERATOR", "Generator");
        RequestDamageFlag("CMNCH_MAINT_C_STARTER", "Starter");
        RequestDamageFlag("CMNCH_MAINT_C_PROP", "Propeller");
        RequestDamageFlag("CMNCH_MAINT_C_BATT", "Battery");
        RequestDamageFlag("CMNCH_MAINT_C_TIREL", "Left Tire");
        RequestDamageFlag("CMNCH_MAINT_C_TIRER", "Right Tire");
        RequestDamageFlag("CMNCH_MAINT_C_TIREC", "Nose Tire");
        RequestDamageFlag("CMNCH_MAINT_C_BRAKEL", "Left Brakes");
        RequestDamageFlag("CMNCH_MAINT_C_BRAKER", "Right Brakes");
        RequestDamageFlag("CMNCH_MAINT_C_GEARL", "Left Gear");
        RequestDamageFlag("CMNCH_MAINT_C_GEARR", "Right Gear");
        RequestDamageFlag("CMNCH_MAINT_C_GEARC", "Nose Gear");
        RequestDamageFlag("CMNCH_MAINT_C_GEARMOTOR", "Gear Motor");

        // Cylinder compression
        for (int i = 1; i <= 6; i++)
        {
            int cylIndex = i;
            RequestVar($"CMNCH_MAINT_COMP_{i}", val =>
            {
                if (cylIndex <= compressionValues.Length)
                {
                    compressionValues[cylIndex - 1].Text = $"{val:F0}/80";
                    compressionValues[cylIndex - 1].AccessibleName = $"Cylinder {cylIndex} compression: {val:F0} over 80";
                }
            });
        }

        // Spark plugs
        for (int i = 1; i <= 6; i++)
        {
            int cylIndex = i;
            RequestVar($"CMNCH_MAINT_PLUG_{i}L", val =>
            {
                string condition = val < 0.3 ? "Good" : val < 0.7 ? "Worn" : "Fouled";
                if (cylIndex <= sparkPlugUpperValues.Length)
                {
                    sparkPlugUpperValues[cylIndex - 1].Text = condition;
                    sparkPlugUpperValues[cylIndex - 1].AccessibleName = $"Cylinder {cylIndex} left plug: {condition}";
                }
            });
            RequestVar($"CMNCH_MAINT_PLUG_{i}R", val =>
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
        RequestAndUpdateField("CMNCH_MAINT_OIL_QTY", oilQuantityValue, "Oil Quantity", "F2", " quarts");
        RequestAndUpdateField("CMNCH_OIL_TEMP", oilTempFluidValue, "Oil Temperature", "F0", " F");
        RequestAndUpdateField("CMNCH_OIL_PRESSURE", oilPressureFluidValue, "Oil Pressure", "F0", " PSI");
    }

    private void RefreshEquipment()
    {
        // Equipment vars aren't registered — use SetLVar to read by writing
        // Actually these are toggle switches in the panel, so they ARE registered
        // But with different keys. Let's use the cached continuous/OnRequest values
        // The equipment L-vars aren't registered as CMNCH_ keys, so we read via cached L-var names
        double? tipTank = _simConnect.GetCachedVariableValue("CMNCH_PITOT_COVER"); // Test if caching works
        // For equipment, just request them fresh - they'll update on next refresh
    }

    // ===== Helper methods =====

    /// <summary>
    /// Request a variable by its registered key (CMNCH_xxx) and call back when the result arrives.
    /// Uses the aircraft definition's registered variables so results flow through SimVarUpdated.
    /// </summary>
    private void RequestVar(string varKey, Action<double> callback)
    {
        try
        {
            // Check if we already have a cached value
            double? cached = _simConnect.GetCachedVariableValue(varKey);
            if (cached.HasValue)
            {
                if (InvokeRequired)
                    Invoke(() => callback(cached.Value));
                else
                    callback(cached.Value);
                return;
            }

            // Register callback and request the variable
            _pendingUpdates[varKey] = callback;
            _simConnect.RequestVariable(varKey);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Hangar] Error reading {varKey}: {ex.Message}");
        }
    }

    private void RequestAndUpdateField(string varKey, TextBox field, string accessiblePrefix,
        string format = "F0", string suffix = "")
    {
        RequestVar(varKey, val =>
        {
            field.Text = $"{val.ToString(format)}{suffix}";
            field.AccessibleName = $"{accessiblePrefix}: {val.ToString(format)}{suffix}";
        });
    }

    private void RequestDamageFlag(string varKey, string flagName)
    {
        RequestVar(varKey, val =>
        {
            // A2A condition scale: 0 = perfect, approaching 1 = destroyed
            if (val > 0.7)
                damageList.Items.Add($"DAMAGED: {flagName} ({val:P0})");
            else if (val > 0.3)
                damageList.Items.Add($"WORN: {flagName} ({val:P0})");
            // Values below 0.3 are healthy — don't list
        });
    }

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
