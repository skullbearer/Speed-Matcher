using Sandbox.Game.EntityComponents;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using SpaceEngineers.Game.ModAPI.Ingame;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace IngameScript
{
    partial class Program : MyGridProgram
    {
        /* 
        /// Skullbearer's Modified Whip's Speed Matcher v7 - 3/01/23 /// 

        ///ACKNOWLEDGEMENTS///    
 
            I, Skullbearer, literally just ripped Whiplash's v25 speed matcher script and then code from
            Dude's Turret Radar v1.6.5, an un-uploaded version of Whiplash's Turret Radar script modified
            to use WeaponCore... which is exactly the purpose of this modification, to use WeaponCore.

            I then went on to dramatically modify chunks, rip out others, install new ones, make it my own.
            This is perhaps 60% Whip's original script, 5% Dude's, and float myPortion = 1f - 0.6f - 0.05f;
        _______________________________________________________________________            
        ///DESCRIPTION///    
 
            This code allows you to lock a ship with WeaponCore and match the velocity vector  
            of a grid using your inertial dampeners. The dampening is handled by the code  
            and is completely automatic. This allows the user to fly their ship as if 
            the ship they are matching speed with is stationary. This makes landing 
            on moving carriers much easier! 
    
            The script will only search for control seats and thrusters on the SAME GRID as the program!
        _______________________________________________________________________            
        ///SETUP///    
 
            1.) Load this script into a programmable block 
 
            2.) Optionally add "Speed Match" into the name of any cockpit or control seat you want to use by default. 
                If you recompile while sitting in it, it will select the one you are in.

            3.) Add "Speed Match" into the name of any text panels you want to display target 
                data on. 
        
            4.) (Optional) Add "Ignore" into the name of thrusters that you don't want the script to touch.
 
        _______________________________________________________________________            
        ///BASIC USEAGE///  
 
            1.) Use WC to target the grid you want to speed match.
 
            2.) Optionally run the program with the argument "scan" to scan script lock the target,
                but not match speed.

            3.) Run the program with the argument "match" to speed match.
                NOTE: Your ship will fly normally while the script is on, however, all thrusters
                ARE BEING OVERRIDDEN, they will just respond (via the script) to your cockpit commands
                if and only if the cockpit you are seated in is the one the script is looking at...
                (see setup step 2).
                NOTE2: The speed matching is only applied when dampeners are on! That's right,
                this script makes you relative damp to the target!

            Additional commands: 
              'off' to stop running the script, 
              'clear' to clear the target info and stop running the script
              'self' to display your own info on the screen... you weirdo

            DX PLAYERS!!!!!!!!!!
              Will not orient your ship! You are still steering! Turn to use those Epsteins!

        */

        string textPanelNameTag = "Speed Match";
        string ignoreThrustNameTag = "Ignore";

        //===================================================== 
        //         NO TOUCH BELOW HERE!!!1!!11!!! 
        //===================================================== 

        double currentTime = 141;
        double timeSinceLastScan = 11;
        double autoScanInterval = 10; // seconds

        Vector3D targetVelocityVec = new Vector3D(0, 0, 0);

        bool shouldScan = false;
        bool shouldMatch = false;
        bool matchSelf = false;
        bool hasMatchedSelf = false;
        bool isSetup = false;
        bool successfulScan = false;

        const double runtimeToRealTime = 1.0 / 0.96;
        const double updatesPerSecond = 10;
        const double updateTime = 1 / updatesPerSecond;

        const double refreshInterval = 10;
        double timeSinceRefresh = 141;

        string scanTargetName = "empty";
        string scanTargetType = "empty";
        string scanTargetSpeed = "empty";
        string scanTargetRelation = "empty";

        const string enabled = ">>ENABLED<<";
        const string dampsOff = ">>ENGAGE DAMPENERS<<";
        const string disabled = "<<DISABLED>>";

        // WeaponCore bits...
        WcPbApi wcapi;
        bool wcapiActive = false;

        // Turret bits...
        List<IMyTerminalBlock> turrets = new List<IMyTerminalBlock>();

        // Target bits...
        Dictionary<long, TargetData> targetDataDict = new Dictionary<long, TargetData>();
        Dictionary<MyDetectedEntityInfo, float> wcTargets = new Dictionary<MyDetectedEntityInfo, float>();
        List<MyDetectedEntityInfo> wcObstructions = new List<MyDetectedEntityInfo>();
        static MyDetectedEntityInfo currentTarget;
        struct TargetData
        {
            public MyDetectedEntityInfo Info;
            public long Targeting;
            public bool MyTarget;
            public double Distance;
            public float Threat;
            public Color Color;

            public TargetData(MyDetectedEntityInfo info, long targeting = 0, bool myTarget = false, double distance = 0, float threat = 0, Color color = default(Color))
            {
                Info = info;
                Targeting = targeting;
                MyTarget = myTarget;
                Distance = distance;
                Threat = threat;
                Color = color;
            }
        }

        Program()
        {
            GrabBlocks();

            Runtime.UpdateFrequency = UpdateFrequency.Once;
            Echo("If you can read this\nclick the 'Run' button!");

            // Initialize WeaponCore...
            wcapi = new WcPbApi();
        }

        const double secondsPerTick = 1.0 / 60.0;
        void Main(string arg, UpdateType updateType)
        {
            //------------------------------------------
            //This is a bandaid
            //if ((Runtime.UpdateFrequency & UpdateFrequency.Update1) == 0)
            //    Runtime.UpdateFrequency = UpdateFrequency.Update1;
            //------------------------------------------

            if ((updateType & (UpdateType.Script | UpdateType.Trigger | UpdateType.Terminal)) != 0)
            {
                var argTrim = arg.ToLower().Trim();
                switch (argTrim)
                {
                    case "scan":
                        shouldScan = true;
                        Runtime.UpdateFrequency = UpdateFrequency.Update1;
                        break;

                    case "match":
                        shouldScan = false; // Toggles scanning off, match function grabs the target
                        shouldMatch = true; // Toggles matching to opposite of scanning
                        Runtime.UpdateFrequency = UpdateFrequency.Update1;
                        break;

                    case "off":
                        shouldMatch = false;
                        shouldScan = false;
                        matchSelf = false;
                        // Also shut script off after this tick
                        Runtime.UpdateFrequency = UpdateFrequency.None;
                        break;

                    case "toggle":
                        shouldMatch = !shouldMatch;
                        if (!shouldMatch)
                        {
                            shouldScan = false;
                            matchSelf = false;
                        }
                        Runtime.UpdateFrequency = UpdateFrequency.Update1;
                        break;

                    case "self":
                        matchSelf = true;
                        hasMatchedSelf = false;
                        shouldMatch = true;
                        shouldScan = false;
                        Runtime.UpdateFrequency = UpdateFrequency.Update1;
                        break;

                    case "clear":
                        successfulScan = false;
                        matchSelf = false;
                        shouldMatch = false;
                        shouldScan = false;
                        break;

                    default:
                        IncrementMatchedSpeed(argTrim);
                        Runtime.UpdateFrequency = UpdateFrequency.Update1;
                        break;
                }
            }

            var lastRuntime = runtimeToRealTime * Math.Max(Runtime.TimeSinceLastRun.TotalSeconds, 0);
            currentTime += lastRuntime; //secondsPerTick; 
            timeSinceRefresh += lastRuntime; //secondsPerTick; 
            

            try
            {
                if (!isSetup || refreshInterval <= timeSinceRefresh)
                {
                    isSetup = GrabBlocks();
                    timeSinceRefresh = 0;
                }

                if (!isSetup)
                    return;

                if (currentTime >= updateTime)
                {
                    if (matchSelf && !hasMatchedSelf)
                    {
                        MatchSelf();
                        hasMatchedSelf = true;
                    }

                    if (shouldScan)
                    {
                        ScanForTarget();
                        timeSinceLastScan += lastRuntime; //secondsPerTick; 
                    }
                    SpeedMatcher();
                    BuildOutputText();
                }
                Echo("WMI Speed Matching Script\nOnline...\n");
                Echo($"Next refresh in {Math.Max(refreshInterval - timeSinceRefresh, 0):N0} seconds");
            }
            catch (Exception e)
            {
                Echo("Exception in Main!");
                Me.CustomData += $"> Speed Matcher Exception\n{e.StackTrace}\n";
                isSetup = false;
            }
        }

        List<IMyThrust> allThrust = new List<IMyThrust>();
        List<IMyShipController> allShipControllers = new List<IMyShipController>();
        List<IMyTextPanel> textPanels = new List<IMyTextPanel>();

        bool GrabBlocks()
        {
            GetAllowedGrids(Me, 5000);
            if (!allowedGridsFinished)
                return false;

            // Activate WC, if no WC then use vanilla turrets
            if (!wcapiActive)
            {
                try
                {
                    wcapiActive = wcapi.Activate(Me);
                }
                catch
                {
                    wcapiActive = false;
                }
            }

            if (wcapiActive) Echo("WeaponCore is available!");
            else Echo("WeaponCore not available, using vanilla turrets!");
            
            bool successfulSetup = true;

            // list that holds all ye turrets
            turrets.Clear();



            GridTerminalSystem.GetBlocksOfType(turrets, x =>{
                    if (wcapiActive)
                    {
                        if (wcapi.HasCoreWeapon(x))
                        {
                            turrets.Add(x);
                            return true;
                        }
                    }
                    else
                    {
                        turrets.Add(x as IMyLargeTurretBase);
                        return true;
                    }
                    return false;
                });
            if (turrets.Count == 0)
            {
                Echo("Warning, no turrets were found!");
                successfulSetup = false;
            }

            GridTerminalSystem.GetBlocksOfType(textPanels, x => x.CustomName.ToLower().Contains(textPanelNameTag.ToLower()) && IsAllowedGrid(x));
            if (textPanels.Count == 0)
            {
                Echo($"Warning: No text panels named '{textPanelNameTag}' were found");
            }

            GridTerminalSystem.GetBlocksOfType(allThrust, x => !x.CustomName.ToLower().Contains(ignoreThrustNameTag.ToLower()) && IsAllowedGrid(x));
            if (allThrust.Count == 0)
            {
                Echo("Error: No thrusters on grid or subgrids");
                successfulSetup = false;
            }

            GridTerminalSystem.GetBlocksOfType(allShipControllers, x => IsAllowedGrid(x));
            if (allShipControllers.Count == 0)
            {
                Echo("Error: No ship controllers on grid or subgrids");
                successfulSetup = false;
            }

            return successfulSetup;
        }

        void MatchSelf()
        {
            targetVelocityVec = allShipControllers[0].GetShipVelocities().LinearVelocity;

            scanTargetName = Me.CubeGrid.CustomName;
            scanTargetType = "SELF";
            scanTargetRelation = "SELF";
            scanTargetSpeed = $"{targetVelocityVec.Length():N2}";
        }
        
        MyDetectedEntityInfo storedTargetInfo = new MyDetectedEntityInfo();
        void ScanForTarget(bool scanNow = false)
        {
            successfulScan = false;
            if (timeSinceLastScan > autoScanInterval || scanNow)
            {
                GetAllTargetsWC();
                storedTargetInfo = currentTarget;
                timeSinceLastScan = 0;
                successfulScan = !storedTargetInfo.IsEmpty();
            }

            if (successfulScan)
            {
                scanTargetName = storedTargetInfo.Name;
                scanTargetSpeed = $"{storedTargetInfo.Velocity.Length():N2}";
                scanTargetType = storedTargetInfo.Type.ToString();
                scanTargetRelation = storedTargetInfo.Relationship.ToString();
                shouldScan = false; //successful scan has completed 
            }
        }
        
        void BuildOutputText()
        {
            string status = shouldMatch ? enabled : disabled;
            string scanStatus = shouldScan ? "Searching..." : "Idle";
            string timeToNext = $"{autoScanInterval - timeSinceLastScan}N0";

            string targetStatus;
            if ((successfulScan || matchSelf) && shouldMatch)
                targetStatus = $"///Skull's Speed Matcher///\n Matching: {status}\n\n Scan Status: {scanStatus}\n\n Scan Info\n Name: {scanTargetName}\n Type: {scanTargetType}\n Velocity: {scanTargetSpeed} m/s\n Relation: {scanTargetRelation}";
            else if (Runtime.UpdateFrequency == UpdateFrequency.None)
                targetStatus = $"///Skull's Speed Matcher///\n Matching: {disabled}\n\n Scan Status: {disabled}\n\n Script is OFF\n\n Giant acknowledgement\n >>TO<<\n Whiplash141 and Dude!";
            else
                targetStatus = $"///Skull's Speed Matcher///\n Matching: {disabled}\n\n Scan Status: {scanStatus}\n\n No target found\n\n Time To Next Scan:{timeToNext}";

            WriteToTextPanel(targetStatus);
        }
        bool thrustIsControlled = false;
        void SpeedMatcher()
        {
            if (!shouldMatch && thrustIsControlled)
            {
                ReleaseThrusters(allThrust);
                thrustIsControlled = false;
                return;
            }
            else if (shouldMatch && !thrustIsControlled) thrustIsControlled = true;

            if (reference.Closed || !reference.IsFunctional || !reference.IsUnderControl || !reference.CanControlShip)
            {   // Always try to find a working, piloted cockpit
                FindWorkingController(); // But only once! If none are controlled, then just work with what we got! (Player may be salvaging)
            }
            var thisController = reference;
            var myVelocityVec = thisController.GetShipVelocities().LinearVelocity;
            var inputVec = thisController.MoveIndicator;
            var desiredDirectionVec = Vector3D.TransformNormal(inputVec, thisController.WorldMatrix); //world relative input vector 
            Vector3D relativeVelocity;
            if (shouldMatch)
            {
                shouldScan = true;
                ScanForTarget(true); // Continuously updates currentTarget to match Velocity even if the target changes the velocity
            }
            
            if (successfulScan && shouldMatch)
            {
                relativeVelocity = myVelocityVec - currentTarget.Velocity;
            }
            else
            {
                shouldMatch = false;
                shouldScan = true; // If we requested a match, but none exists, start scanning again
                ReleaseThrusters(allThrust);
                thrustIsControlled = false; // Release the thrusters
                return;
            }
                
            ApplyThrust(allThrust, relativeVelocity, desiredDirectionVec, thisController);
        }
        void ReleaseThrusters(List<IMyThrust> thrusters)
        {
            foreach (IMyThrust t in thrusters)
            {
                SetThrusterOverride(t, 0f);
            }
        }
        
        void ApplyThrust(List<IMyThrust> thrusters, Vector3D travelVec, Vector3D desiredDirectionVec, IMyShipController thisController)
        {
            var mass = thisController.CalculateShipMass().PhysicalMass;
            var gravity = thisController.GetNaturalGravity();

            var desiredThrust = mass * (2 * travelVec + gravity);
            var thrustToApply = desiredThrust;
            if (!Vector3D.IsZero(desiredDirectionVec))
            {
                thrustToApply = VectorRejection(desiredThrust, desiredDirectionVec);
            }

            foreach (IMyThrust thisThrust in thrusters)
            {
                
                if (Vector3D.Dot(thisThrust.WorldMatrix.Backward, desiredDirectionVec) > .7071) //thrusting in desired direction
                {
                    SetThrusterOverride(thisThrust, 1f);
                }
                else if (Vector3D.Dot(thisThrust.WorldMatrix.Forward, thrustToApply) > 0 && thisController.DampenersOverride)
                {   // If it's in the direction to thrust AND dampeners are on
                    var neededThrust = Vector3D.Dot(thrustToApply, thisThrust.WorldMatrix.Forward);
                    var outputProportion = MathHelper.Clamp(neededThrust / thisThrust.MaxEffectiveThrust, 0, 1);
                    thisThrust.ThrustOverridePercentage = (float)outputProportion;
                    thrustToApply -= thisThrust.WorldMatrix.Forward * outputProportion * thisThrust.MaxEffectiveThrust;
                }
                else
                {   // Prevents normal game dampeners from working until the script is turned off
                    SetThrusterOverride(thisThrust, 0.000001f);
                }
            }
        }

        void IncrementMatchedSpeed(string arg)
        {
            if (Vector3D.IsZero(targetVelocityVec, 1e-3))
                return;

            if (!arg.StartsWith("increment", StringComparison.OrdinalIgnoreCase))
                return;

            arg = arg.Replace("increment", "").Trim();
            double speedIncrement = 0;
            if (!double.TryParse(arg, out speedIncrement))
                return;

            var targetTravel = Vector3D.Normalize(targetVelocityVec); //get current direction of target's travel
            targetVelocityVec += targetTravel * speedIncrement;
        }

        Vector3D VectorRejection(Vector3D a, Vector3D b) //reject a on b    
        {
            if (Vector3D.IsZero(b))
                return Vector3D.Zero;

            return a - a.Dot(b) / b.LengthSquared() * b;
        }

        void SetThrusterOverride(IMyThrust thruster, float overrideValue)
        {
            thruster.ThrustOverridePercentage = overrideValue;
        }

        void WriteToTextPanel(string textToWrite, bool append = false)
        {
            foreach (var thisScreen in textPanels)
            {
                thisScreen.WriteText(textToWrite, append);
                thisScreen.SetValue("FontSize", 1.6f);
            }
        }

        /*
        / //// / Whip's GetAllowedGrids method v1 - 3/17/18 / //// /
        Derived from Digi's GetShipGrids() method - https://pastebin.com/MQUHQTg2
        */
        List<IMyMechanicalConnectionBlock> allMechanical = new List<IMyMechanicalConnectionBlock>();
        HashSet<IMyCubeGrid> allowedGrids = new HashSet<IMyCubeGrid>();
        bool allowedGridsFinished = true;
        void GetAllowedGrids(IMyTerminalBlock reference, int instructionLimit = 1000)
        {
            if (allowedGridsFinished)
            {
                allowedGrids.Clear();
                allowedGrids.Add(reference.CubeGrid);
            }

            GridTerminalSystem.GetBlocksOfType(allMechanical, x => x.TopGrid != null);

            bool foundStuff = true;
            while (foundStuff)
            {
                foundStuff = false;

                for (int i = allMechanical.Count - 1; i >= 0; i--)
                {
                    var block = allMechanical[i];
                    if (allowedGrids.Contains(block.CubeGrid))
                    {
                        allowedGrids.Add(block.TopGrid);
                        allMechanical.RemoveAt(i);
                        foundStuff = true;
                    }
                    else if (allowedGrids.Contains(block.TopGrid))
                    {
                        allowedGrids.Add(block.CubeGrid);
                        allMechanical.RemoveAt(i);
                        foundStuff = true;
                    }
                }

                if (Runtime.CurrentInstructionCount >= instructionLimit)
                {
                    Echo("Instruction limit reached\nawaiting next run");
                    allowedGridsFinished = false;
                    return;
                }
            }

            allowedGridsFinished = true;
        }

        bool IsAllowedGrid(IMyTerminalBlock block)
        {
            return allowedGrids.Contains(block.CubeGrid);
        }

        // Turret target finding...
        void AddTargetData(MyDetectedEntityInfo targetInfo, float threat = 0f)
        {
            if (!targetDataDict.ContainsKey(targetInfo.EntityId))
            {
                TargetData targetData = new TargetData(targetInfo);
                targetData.Threat = threat;
                targetDataDict[targetInfo.EntityId] = targetData;
            }
        }

        void FindWorkingController()
        {
            if (allShipControllers.Count == 0) GrabBlocks();
            for (int a = allShipControllers.Count - 1; a > -1; a--)
            {
                if (!allShipControllers[a].Closed && allShipControllers[a].IsFunctional)
                {
                    reference = allShipControllers[a];
                    if (reference.IsUnderControl || reference.CustomName.Contains("Speed Match")) return;
                }
                else allShipControllers.RemoveAtFast(a);
            }
        }
        IMyShipController reference;
        void GetAllTargetsWC()
        {
            targetDataDict.Clear();
            wcTargets.Clear();
            wcapi.GetSortedThreats(Me, wcTargets);
            wcapi.GetObstructions(Me, wcObstructions);

            Dictionary<long, TargetData> temp = new Dictionary<long, TargetData>();

            foreach (var target in wcTargets)
            {
                AddTargetData(target.Key, target.Value);
            }
            foreach (var target in wcObstructions)
            {
                AddTargetData(target, 0);
            }

            var t = wcapi.GetAiFocus(Me.CubeGrid.EntityId, 0);
            if (t.HasValue && t.Value.EntityId != 0L)
                currentTarget = t.Value;

            FindWorkingController();

            if (reference == null)
            {
                throw new Exception("There are no working controllers on the ship!");
            }

            foreach (var kvp in targetDataDict)
            {
                if (kvp.Key == Me.CubeGrid.EntityId)
                    continue;

                var targetData = kvp.Value;

                if (targetData.Info.EntityId != 0)
                    t = wcapi.GetAiFocus(targetData.Info.EntityId, 0);
                if (t.HasValue && t.Value.EntityId != 0)
                    targetData.Targeting = t.Value.EntityId;
                targetData.Distance = Vector3D.Distance(targetData.Info.Position, Me.CubeGrid.GetPosition());

                if (!currentTarget.IsEmpty() && kvp.Key == currentTarget.EntityId)
                {
                    targetData.MyTarget = true;
                }

                temp[targetData.Info.EntityId] = targetData;
            }

            targetDataDict.Clear();
            foreach (var item in temp)
                targetDataDict[item.Key] = item.Value;
        }
        /* Need to implement this for vanilla weapons
        void GetAllTargetsVanilla()
        {
            targetDataDict.Clear();
            vanTargets.Clear();

            for

            Dictionary<long, TargetData> temp = new Dictionary<long, TargetData>();

            foreach (var target in wcTargets)
            {
                AddTargetData(target.Key, target.Value);
            }
            foreach (var target in wcObstructions)
            {
                AddTargetData(target, 0);
            }

            var t = wcapi.GetAiFocus(Me.CubeGrid.EntityId, 0);
            if (t.HasValue && t.Value.EntityId != 0L)
                currentTarget = t.Value;

            for (int a = allShipControllers.Count - 1; a > -1; a--)
            {
                if (!allShipControllers[a].Closed)
                {
                    reference = allShipControllers[a];
                }
                else allShipControllers.RemoveAtFast(a);
            }

            if (reference == null)
            {
                GrabBlocks();
                if (allShipControllers.Count == 0)
                {
                    throw new Exception("There are no working controllers on the ship!");
                    return;
                }
                reference = allShipControllers[0];
            }

            if (reference is IMyShipController)
                reference = (IMyShipController)reference;

            foreach (var kvp in targetDataDict)
            {
                if (kvp.Key == Me.CubeGrid.EntityId)
                    continue;

                var targetData = kvp.Value;

                if (targetData.Info.EntityId != 0)
                    t = wcapi.GetAiFocus(targetData.Info.EntityId, 0);
                if (t.HasValue && t.Value.EntityId != 0)
                    targetData.Targeting = t.Value.EntityId;
                targetData.Distance = Vector3D.Distance(targetData.Info.Position, Me.CubeGrid.GetPosition());

                if (!currentTarget.IsEmpty() && kvp.Key == currentTarget.EntityId)
                {
                    targetData.MyTarget = true;
                }

                temp[targetData.Info.EntityId] = targetData;
            }

            targetDataDict.Clear();
            foreach (var item in temp)
                targetDataDict[item.Key] = item.Value;
        }
        */
        // WeaponCore PB API class
        public class WcPbApi
        {
            private Action<ICollection<MyDefinitionId>> _getCoreWeapons;
            private Action<ICollection<MyDefinitionId>> _getCoreStaticLaunchers;
            private Action<ICollection<MyDefinitionId>> _getCoreTurrets;
            private Action<IMyTerminalBlock, IDictionary<MyDetectedEntityInfo, float>> _getSortedThreats;
            private Action<IMyTerminalBlock, ICollection<MyDetectedEntityInfo>> _getObstructions;
            private Func<long, int, MyDetectedEntityInfo> _getAiFocus;
            private Func<IMyTerminalBlock, long, int, bool> _setAiFocus;
            private Func<IMyTerminalBlock, int, MyDetectedEntityInfo> _getWeaponTarget;
            private Action<IMyTerminalBlock, long, int> _setWeaponTarget;
            private Action<IMyTerminalBlock, bool, int> _fireWeaponOnce;
            private Action<IMyTerminalBlock, bool, bool, int> _toggleWeaponFire;
            private Func<IMyTerminalBlock, int, bool, bool, bool> _isWeaponReadyToFire;
            private Func<IMyTerminalBlock, int, float> _getMaxWeaponRange;
            private Func<IMyTerminalBlock, long, int, bool> _isTargetAligned;
            private Func<IMyTerminalBlock, long, int, MyTuple<bool, VRageMath.Vector3D?>> _isTargetAlignedExtended;
            private Func<IMyTerminalBlock, long, int, bool> _canShootTarget;
            private Func<IMyTerminalBlock, long, int, VRageMath.Vector3D?> _getPredictedTargetPos;
            private Func<long, bool> _hasGridAi;
            private Func<IMyTerminalBlock, bool> _hasCoreWeapon;
            private Func<long, float> _getOptimalDps;
            private Func<IMyTerminalBlock, int, string> _getActiveAmmo;
            private Action<IMyTerminalBlock, int, string> _setActiveAmmo;
            private Func<long, float> _getConstructEffectiveDps;
            private Func<IMyTerminalBlock, long> _getPlayerController;
            private Func<IMyTerminalBlock, long, bool, bool, bool> _isTargetValid;
            private Func<IMyTerminalBlock, int, MyTuple<VRageMath.Vector3D, VRageMath.Vector3D>> _getWeaponScope;
            private Func<IMyTerminalBlock, MyTuple<bool, bool>> _isInRange;

            public bool Activate(IMyTerminalBlock pbBlock)
            {
                var dict = pbBlock.GetProperty("WcPbAPI")?.As<IReadOnlyDictionary<string, Delegate>>().GetValue(pbBlock);
                if (dict == null) throw new Exception("WcPbAPI failed to activate");
                return ApiAssign(dict);
            }

            public bool ApiAssign(IReadOnlyDictionary<string, Delegate> delegates)
            {
                if (delegates == null)
                    return false;

                AssignMethod(delegates, "GetCoreWeapons", ref _getCoreWeapons);
                AssignMethod(delegates, "GetCoreStaticLaunchers", ref _getCoreStaticLaunchers);
                AssignMethod(delegates, "GetCoreTurrets", ref _getCoreTurrets);
                AssignMethod(delegates, "GetSortedThreats", ref _getSortedThreats);
                AssignMethod(delegates, "GetObstructions", ref _getObstructions);
                AssignMethod(delegates, "GetAiFocus", ref _getAiFocus);
                AssignMethod(delegates, "SetAiFocus", ref _setAiFocus);
                AssignMethod(delegates, "GetWeaponTarget", ref _getWeaponTarget);
                AssignMethod(delegates, "SetWeaponTarget", ref _setWeaponTarget);
                AssignMethod(delegates, "FireWeaponOnce", ref _fireWeaponOnce);
                AssignMethod(delegates, "ToggleWeaponFire", ref _toggleWeaponFire);
                AssignMethod(delegates, "IsWeaponReadyToFire", ref _isWeaponReadyToFire);
                AssignMethod(delegates, "GetMaxWeaponRange", ref _getMaxWeaponRange);
                AssignMethod(delegates, "IsTargetAligned", ref _isTargetAligned);
                AssignMethod(delegates, "IsTargetAlignedExtended", ref _isTargetAlignedExtended);
                AssignMethod(delegates, "CanShootTarget", ref _canShootTarget);
                AssignMethod(delegates, "GetPredictedTargetPosition", ref _getPredictedTargetPos);
                AssignMethod(delegates, "HasGridAi", ref _hasGridAi);
                AssignMethod(delegates, "HasCoreWeapon", ref _hasCoreWeapon);
                AssignMethod(delegates, "GetOptimalDps", ref _getOptimalDps);
                AssignMethod(delegates, "GetActiveAmmo", ref _getActiveAmmo);
                AssignMethod(delegates, "SetActiveAmmo", ref _setActiveAmmo);
                AssignMethod(delegates, "GetConstructEffectiveDps", ref _getConstructEffectiveDps);
                AssignMethod(delegates, "GetPlayerController", ref _getPlayerController);
                AssignMethod(delegates, "IsTargetValid", ref _isTargetValid);
                AssignMethod(delegates, "GetWeaponScope", ref _getWeaponScope);
                AssignMethod(delegates, "IsInRange", ref _isInRange);
                return true;
            }

            private void AssignMethod<T>(IReadOnlyDictionary<string, Delegate> delegates, string name, ref T field) where T : class
            {
                if (delegates == null)
                {
                    field = null;
                    return;
                }

                Delegate del;
                if (!delegates.TryGetValue(name, out del))
                    throw new Exception($"{GetType().Name} Couldnt find {name} delegate of type {typeof(T)}");

                field = del as T;
                if (field == null)
                    throw new Exception(
                        $"{GetType().Name} Delegate {name} is not type {typeof(T)} instead its {del.GetType()}");
            }

            public void GetAllCoreWeapons(ICollection<MyDefinitionId> collection) => _getCoreWeapons?.Invoke(collection);

            public void GetAllCoreStaticLaunchers(ICollection<MyDefinitionId> collection) =>
                _getCoreStaticLaunchers?.Invoke(collection);

            public void GetAllCoreTurrets(ICollection<MyDefinitionId> collection) => _getCoreTurrets?.Invoke(collection);

            public void GetSortedThreats(IMyTerminalBlock pBlock, IDictionary<MyDetectedEntityInfo, float> collection) =>
                _getSortedThreats?.Invoke(pBlock, collection);
            public void GetObstructions(IMyTerminalBlock pBlock, ICollection<MyDetectedEntityInfo> collection) =>
                _getObstructions?.Invoke(pBlock, collection);
            public MyDetectedEntityInfo? GetAiFocus(long shooter, int priority = 0) => _getAiFocus?.Invoke(shooter, priority);

            public bool SetAiFocus(IMyTerminalBlock pBlock, long target, int priority = 0) =>
                _setAiFocus?.Invoke(pBlock, target, priority) ?? false;

            public MyDetectedEntityInfo? GetWeaponTarget(IMyTerminalBlock weapon, int weaponId = 0) =>
                _getWeaponTarget?.Invoke(weapon, weaponId);

            public void SetWeaponTarget(IMyTerminalBlock weapon, long target, int weaponId = 0) =>
                _setWeaponTarget?.Invoke(weapon, target, weaponId);

            public void FireWeaponOnce(IMyTerminalBlock weapon, bool allWeapons = true, int weaponId = 0) =>
                _fireWeaponOnce?.Invoke(weapon, allWeapons, weaponId);

            public void ToggleWeaponFire(IMyTerminalBlock weapon, bool on, bool allWeapons, int weaponId = 0) =>
                _toggleWeaponFire?.Invoke(weapon, on, allWeapons, weaponId);

            public bool IsWeaponReadyToFire(IMyTerminalBlock weapon, int weaponId = 0, bool anyWeaponReady = true,
                bool shootReady = false) =>
                _isWeaponReadyToFire?.Invoke(weapon, weaponId, anyWeaponReady, shootReady) ?? false;

            public float GetMaxWeaponRange(IMyTerminalBlock weapon, int weaponId) =>
                _getMaxWeaponRange?.Invoke(weapon, weaponId) ?? 0f;

            public bool IsTargetAligned(IMyTerminalBlock weapon, long targetEnt, int weaponId) =>
                _isTargetAligned?.Invoke(weapon, targetEnt, weaponId) ?? false;

            public MyTuple<bool, VRageMath.Vector3D?> IsTargetAlignedExtended(IMyTerminalBlock weapon, long targetEnt, int weaponId) =>
                _isTargetAlignedExtended?.Invoke(weapon, targetEnt, weaponId) ?? new MyTuple<bool, VRageMath.Vector3D?>();

            public bool CanShootTarget(IMyTerminalBlock weapon, long targetEnt, int weaponId) =>
                _canShootTarget?.Invoke(weapon, targetEnt, weaponId) ?? false;

            public VRageMath.Vector3D? GetPredictedTargetPosition(IMyTerminalBlock weapon, long targetEnt, int weaponId) =>
                _getPredictedTargetPos?.Invoke(weapon, targetEnt, weaponId) ?? null;
            public bool HasGridAi(long entity) => _hasGridAi?.Invoke(entity) ?? false;
            public bool HasCoreWeapon(IMyTerminalBlock weapon) => _hasCoreWeapon?.Invoke(weapon) ?? false;
            public float GetOptimalDps(long entity) => _getOptimalDps?.Invoke(entity) ?? 0f;

            public string GetActiveAmmo(IMyTerminalBlock weapon, int weaponId) =>
                _getActiveAmmo?.Invoke(weapon, weaponId) ?? null;

            public void SetActiveAmmo(IMyTerminalBlock weapon, int weaponId, string ammoType) =>
                _setActiveAmmo?.Invoke(weapon, weaponId, ammoType);
            public float GetConstructEffectiveDps(long entity) => _getConstructEffectiveDps?.Invoke(entity) ?? 0f;

            public long GetPlayerController(IMyTerminalBlock weapon) => _getPlayerController?.Invoke(weapon) ?? -1;
            public bool IsTargetValid(IMyTerminalBlock weapon, long targetId, bool onlyThreats, bool checkRelations) =>
                _isTargetValid?.Invoke(weapon, targetId, onlyThreats, checkRelations) ?? false;

            public MyTuple<VRageMath.Vector3D, VRageMath.Vector3D> GetWeaponScope(IMyTerminalBlock weapon, int weaponId) =>
                _getWeaponScope?.Invoke(weapon, weaponId) ?? new MyTuple<VRageMath.Vector3D, VRageMath.Vector3D>();
            public MyTuple<bool, bool> IsInRange(IMyTerminalBlock block) =>
                _isInRange?.Invoke(block) ?? new MyTuple<bool, bool>();
        }

        /* 
        ///CHANGELOG/// 
        * Removed GetWorldMatrix() method since world matricies were fixed - v6 
        * Removed a bunch of unused angle constants and hard coded them - v6 
        * Cleaned up arguments - v7 
        * Redesigned refresh function to be more efficient - v8 
        * Added in variable config code - v9 
        * Added "clear" command - v9 
        * Touched up output text - v9 
        * Added percentage bar - v10 
        * Added target relation and type to scan info - v10 
        * Fixed dampeners turning off when no successful scan has been completed - v11
        * Fixed some formatting issues - v12
        * Removed unused methods - v13
        * Simplified some math and removed unused methods - v14
        * Added thrust ignore name tag - v15
        * Code now checks for thrusters only on the same grid - 15
        * Code nolonger needs a timer to trigger a loop - v18
        * Added a speed increment method - v19
        * Removed an unnecessary .Length() call - v19
        * Changed dampening method to use the algorithm that keen uses - v20
        * Changed "scan" and "match" commands into toggle functions - v21
        * Added dampener status recognition - v21
        * Fixed issue where codes would trigger multiple times per tick in DS - v22
        * Reduced update frequency - v23
        * Added exception outout - v23
        * Updated update frequency bandaid - v23
        * Added GetAllowedGrids method to allow program to placed on subgrids - v23
        * Changed how "match" command behaves - v23
        * Changed update frequency workaround - v24
        * Fix for keen's stupid negative runtime bug - v25
        * TOTAL RIPOFF AND ADDITION OF WC, -> Skull's - v1
        * Modified so that it will always grab controlled cockpit if available - v2
        * Will properly shut off overrides of thrusters now when not matching - v3
        * Removed the 'on' command as it is redundant - v4
        * 'match' will now initiate a single target scan attempt and default to off if no target - v5
        * 'off' now turns the script off completely - v5
        * When script is passing through manual controls for thrusters, will not override the thrusters opposite of the manual command - v6
        * Updated status screen text - v7
        */
    }
}
