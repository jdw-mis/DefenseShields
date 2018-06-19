﻿using System;
using DefenseShields.Control;
using DefenseShields.Support;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace DefenseShields
{
    public partial class DefenseShields
    {
        public enum ShieldType
        {
            Station,
            LargeGrid,
            SmallGrid,
        };

        #region Startup Logic
        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            try
            {
                Entity.Components.TryGet(out Sink);
                ResourceInfo = new MyResourceSinkInfo()
                {
                    ResourceTypeId = GId,
                    MaxRequiredInput = 0f,
                    RequiredInputFunc = () => _power
                };
                Sink.RemoveType(ref ResourceInfo.ResourceTypeId);
                Sink.Init(MyStringHash.GetOrCompute("Defense"), ResourceInfo);
                Sink.AddType(ref ResourceInfo);


                base.Init(objectBuilder);
                NeedsUpdate |= MyEntityUpdateEnum.BEFORE_NEXT_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
                NeedsUpdate |= MyEntityUpdateEnum.EACH_100TH_FRAME;

                //Shield.CubeGrid.OnIsStaticChanged += CheckStatic;
                if (!_shields.ContainsKey(Entity.EntityId)) _shields.Add(Entity.EntityId, this);
                MyAPIGateway.Session.OxygenProviderSystem.AddOxygenGenerator(EllipsoidOxyProvider);
                Session.Instance.Components.Add(this);

                StorageSetup();
                ((MyCubeGrid)Shield.CubeGrid).OnHierarchyUpdated += HierarchyChanged;
                if (Session.Enforced.Debug == 1) Log.Line($"pre-Init complete");
            }
            catch (Exception ex) { Log.Line($"Exception in EntityInit: {ex}"); }
        }

        public DefenseShields()
        {
            ShieldComp = new ShieldGridComponent(this);
        }

        private void HierarchyChanged(IMyCubeGrid myCubeGrid = null)
        {
            if (_tick == _hierarchyTick) return;
            if (_hierarchyTick > _tick - 9)
            {
                _hierarchyDelayed = true;
                return;
            }
            _hierarchyTick = _tick;

            var gotGroups = MyAPIGateway.GridGroups.GetGroup(Shield.CubeGrid, GridLinkTypeEnum.Logical);
            ShieldComp.GetSubGrids.Clear();
            for (int i = 0; i < gotGroups.Count; i++) ShieldComp.GetSubGrids.Add(gotGroups[i]);
        }

        private void SetShieldType()
        {
            if (Shield.CubeGrid.Physics.IsStatic) ShieldMode = ShieldType.Station;
            else if (Shield.CubeGrid.GridSizeEnum == MyCubeSize.Large) ShieldMode = ShieldType.LargeGrid;
            else ShieldMode = ShieldType.SmallGrid;

            if (ShieldMode != ShieldType.Station) ShieldComp.GridIsMobile = true;

            switch (ShieldMode)
            {
                case ShieldType.Station:
                    _shieldRatio = Session.Enforced.StationRatio;
                    break;
                case ShieldType.LargeGrid:
                    _shieldRatio = Session.Enforced.LargeShipRatio;
                    break;
                case ShieldType.SmallGrid:
                    _shieldRatio = Session.Enforced.SmallShipRatio;
                    break;
            }
        }

        public override void UpdateAfterSimulation100()
        {
            try
            {
                if (!Session.EnforceInit && (_enforceTick == 0 || _tick - _enforceTick > 60))
                {
                    _enforceTick = _tick;
                    if (Session.IsServer) ServerEnforcementSetup();
                    else ClientEnforcementRequest();
                    return;
                }

                if (AllInited || !Shield.IsFunctional) return;

                if (!HealthCheck()) return;

                if (!Shield.CubeGrid.Components.Has<ShieldGridComponent>()) Shield.CubeGrid.Components.Add(ShieldComp);
                else
                {
                    Shield.CubeGrid.Components.TryGet(out ShieldComp);
                    ShieldComp.BoundingRange = 0f;
                    ShieldComp.WarmedUp = false;
                    ShieldComp.Warming = false;
                    ShieldComp.ShieldActive = false;
                    ShieldComp.ModulationPassword = null;
                    ShieldComp.ShieldIsStarting = false;
                    ShieldComp.DefenseShields = this;
                }

                if (ConnectCheck(true)) return;

                if (Icosphere == null) Icosphere = new Icosphere.Instance(Session.Instance.Icosphere);

                if (!MainInit && Shield.IsFunctional)
                {
                    var enableState = Shield.Enabled;

                    if (enableState)
                    {
                        Shield.Enabled = false; 
                        Shield.Enabled = true;
                    }

                    ((MyCubeBlock)Shield).ChangeOwner(Shield.CubeGrid.BigOwners[0], MyOwnershipShareModeEnum.Faction);

                    SetShieldType();

                    if (ShieldMode == ShieldType.Station)
                    {
                        _shapeAdjusted = false;
                        _shapeLoaded = false;
                    }

                    //InitControls(); // fix get existing controls
                    CreateUi();
                    PowerInit();

                    MainInit = true;
                    if (Session.Enforced.Debug == 1) Log.Line($"MainInit complete");
                }

                if (!PhysicsInit)
                {
                    SpawnEntities();
                    CleanUp(3);

                    if (Session.Enforced.Debug == 1) Log.Line($"PhysicsInit complete");

                    PhysicsInit = true;
                }

                if (AllInited || !PhysicsInit || !MainInit || !Shield.IsFunctional) return;

                if (Session.Enforced.Debug == 1) Log.Line($"ShieldId:{Shield.EntityId.ToString()} - {Shield.BlockDefinition.SubtypeId} is functional - tick:{_tick.ToString()}");

                if (Session.Enforced.Debug == 1) Log.Line($"AnimateInit complete");

                AllInited = true;
            }
            catch (Exception ex) { Log.Line($"Exception in UpdateAfterSimulation100: {ex}"); }
        }

        private bool HealthCheck()
        {
            HardDisable = false || Shield.EntityId != UtilsStatic.ThereCanBeOnlyOne(Shield);
            if (!HardDisable) return true;
            if (Session.Enforced.Debug == 1) Log.Line($"HardDisable is triggered - {Shield.BlockDefinition.SubtypeId}");

            _startupWarning = UtilsStatic.CheckShieldType(Shield, _startupWarning);

            return false;
        }

        private void SpawnEntities()
        {
            var parent = (MyEntity)Shield.CubeGrid;

            _shellPassive = Spawn.EmptyEntity("dShellPassive", $"{Session.Instance.ModPath()}\\Models\\Cubes\\ShieldPassive_LOD0.mwm", parent, true);
            _shellPassive.Render.CastShadows = false;
            _shellPassive.IsPreview = true;
            _shellPassive.Render.Visible = true;
            _shellPassive.Render.RemoveRenderObjects();
            _shellPassive.Render.UpdateRenderObject(true);
            _shellPassive.Render.UpdateRenderObject(false);
            _shellPassive.Save = false;

            _shellActive = Spawn.EmptyEntity("dShellActive", $"{Session.Instance.ModPath()}\\Models\\Cubes\\ShieldActiveH_LOD3.mwm", parent, true);
            _shellActive.Render.CastShadows = false;
            _shellActive.IsPreview = true;
            _shellActive.Render.Visible = true;
            _shellActive.Render.RemoveRenderObjects();
            _shellActive.Render.UpdateRenderObject(true);
            _shellActive.Render.UpdateRenderObject(false);
            _shellActive.Save = false;
            _shellActive.SetEmissiveParts("ShieldEmissiveAlpha", Color.Black, 0.01f);

            _shield = Spawn.EmptyEntity("dShield", null, (MyEntity)Shield, false);
            //_shield = Spawn.SpawnBlock("dShield", $"{Shield.EntityId}", true, false, false, false, true, Shield.OwnerId);
            _shield.Render.CastShadows = false;
            _shield.Render.RemoveRenderObjects();
            _shield.Render.UpdateRenderObject(true);
            _shield.Render.Visible = true;
            _shield.Save = false;
            if (Session.Enforced.Debug == 1) Log.Line($"SpawnEntities complete");
        }

        private void StorageSetup()
        {
            Storage = Shield.Storage;
            DsSet = new DefenseShieldsSettings(Shield);
            DsSet.LoadSettings();
            UpdateSettings(DsSet.Settings);
            if (Session.Enforced.Debug == 1) Log.Line($"StorageSetup complete");
        }

        private void ClientEnforcementRequest()
        {
            if (Session.Enforced.Version > 0)
            {
                if (Session.Enforced.Debug == 1) Log.Line($"Local enforcements found, bypassing request - IsServer? {Session.IsServer}");
            }
            else 
            {
                if (Session.Enforced.Debug == 1) Log.Line($"ClientEnforcementRequest Check finished - Enforcement Request?: {Session.Enforced.Version <= 0}");
                Enforcements.EnforcementRequest(Shield.EntityId);
            }
        }

        private static void ServerEnforcementSetup()
        {
            if (Session.Enforced.Version > 0) Session.EnforceInit = true;
            else Log.Line($"Server has failed to set its own enforcements!! Report this as a bug");

            if (Session.Enforced.Debug == 1) Log.Line($"ServerEnforcementSetup\n{Session.Enforced}");
        }

        private void PowerInit()
        {
            try
            {
                HardDisable = false;
                _power = 0.0000000001f;
                _shieldCurrentPower = _power;
                Sink.Update();

                Shield.AppendingCustomInfo += AppendingCustomInfo;
                Shield.RefreshCustomInfo();
                if (Session.Enforced.Debug == 1) Log.Line($"PowerInit complete");
            }
            catch (Exception ex) { Log.Line($"Exception in AddResourceSourceComponent: {ex}"); }
        }
        #endregion

        #region Create UI

        private void InitControls()
        {
            if (ShieldMode == ShieldType.SmallGrid && !Session.Instance.SmallShieldControlsLoaded)
            {
                Session.Instance.SmallShieldControlsLoaded = true;
                CreateUi();
            }
            else if (ShieldMode != ShieldType.SmallGrid && !Session.Instance.LargeShieldControlsLoaded)
            {
                Session.Instance.LargeShieldControlsLoaded = true;
                CreateUi();
            }
        }

        private void SetW(IMyTerminalBlock b, float v)
        {
            Width = v;
        }

        private void SetH(IMyTerminalBlock b, float v)
        {
            Height = v;
        }

        private void SetD(IMyTerminalBlock b, float v)
        {
            Depth = v;
        }

        private float GetW(IMyTerminalBlock b)
        {
            return Width;
        }

        private float GetH(IMyTerminalBlock b)
        {
            return Height;
        }

        private float GetD(IMyTerminalBlock b)
        {
            return Depth;
        }

        private void CreateUi()
        {
            UtilsStatic.RemoveOreUi();
            _chargeSlider = new RangeSlider<Sandbox.ModAPI.Ingame.IMyOreDetector>(Shield, "ChargeRate", "Shield Charge Rate", 20, 95, 50);

            _extendFit = new RefreshCheckbox<Sandbox.ModAPI.Ingame.IMyOreDetector>(Shield, "ExtendFit", "Extend Shield", false);
            _sphereFit = new RefreshCheckbox<Sandbox.ModAPI.Ingame.IMyOreDetector>(Shield, "SphereFit", "Sphere Fit", false);
            _fortifyShield = new RefreshCheckbox<Sandbox.ModAPI.Ingame.IMyOreDetector>(Shield, "ShieldFortify", "Fortify Shield", false);
            _widthSlider = new RangeSlider<Sandbox.ModAPI.Ingame.IMyOreDetector>(Shield, "WidthSlider", "Shield Size Width", 30, 600, 100);
            _heightSlider = new RangeSlider<Sandbox.ModAPI.Ingame.IMyOreDetector>(Shield, "HeightSlider", "Shield Size Height", 30, 600, 100);
            _depthSlider = new RangeSlider<Sandbox.ModAPI.Ingame.IMyOreDetector>(Shield, "DepthSlider", "Shield Size Depth", 30, 600, 100);

            /*
            if (Shield.CubeGrid.Physics.IsStatic)
            {
                _widthSlider = TerminalHelpers.AddSlider(Shield, "WidthSlider", "Shield Size Height", "Shield Size Height", GetW, SetW);
                _widthSlider.SetLimits(0, ShieldComp.ZRange);
                _widthSlider.Visible = block => true;
                _widthSlider.Enabled = block => true;

                _heightSlider = TerminalHelpers.AddSlider(Shield, "HeightSlider", "Shield Size Height", "Shield Size Height", GetH, SetH);
                _heightSlider.SetLimits(0, ShieldComp.YRange);
                _heightSlider.Visible = block => true;
                _heightSlider.Enabled = block => true;

                _depthSlider = TerminalHelpers.AddSlider(Shield, "DepthSlider", "Shield Size Depth", "Shield Size Depth", GetD, SetD);
                _depthSlider.SetLimits(0, ShieldComp.XRange);
                _depthSlider.Visible = block => true;
                _depthSlider.Enabled = block => true;
            }
            */

            _hidePassiveCheckBox = new RefreshCheckbox<Sandbox.ModAPI.Ingame.IMyOreDetector>(Shield, "HidePassive", "Hide idle shield state", false);
            _hideActiveCheckBox = new RefreshCheckbox<Sandbox.ModAPI.Ingame.IMyOreDetector>(Shield, "HideActive", "Hide active shield state", false);

            if (Session.Enforced.Debug == 1) Log.Line($"CreateUI Complete");
        }
        #endregion
    }
}