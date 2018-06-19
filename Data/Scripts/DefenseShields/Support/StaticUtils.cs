﻿using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Interfaces.Terminal;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;

namespace DefenseShields.Support
{
    internal static class UtilsStatic
    {
        public static void GetRealPlayers(Vector3D center, float radius, List<long> realPlayers)
        {
            List<IMyIdentity> realPlayersIdentities = new List<IMyIdentity>();
            MyAPIGateway.Players.GetAllIdentites(realPlayersIdentities, p => !string.IsNullOrEmpty(p?.DisplayName));
            var pruneSphere = new BoundingSphereD(center, radius);
            var pruneList = new List<MyEntity>();
            MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref pruneSphere, pruneList);

            foreach (var ent in pruneList)
            {
                if (ent == null || !(ent is IMyCubeGrid || ent is IMyCharacter)) continue;

                IMyPlayer player = null;

                if (ent is IMyCharacter)
                {
                    player = MyAPIGateway.Players.GetPlayerControllingEntity(ent);
                    if (player == null) continue;
                }
                else
                {
                    var playerTmp = MyAPIGateway.Players.GetPlayerControllingEntity(ent);

                    if (playerTmp?.Character != null) player = playerTmp;
                }

                if (player == null) continue;
                if (realPlayersIdentities.Contains(player.Identity)) realPlayers.Add(player.IdentityId);
            }
        }

        public static Color GetEmissiveColorFromFloat(float percent)
        {
            if (percent > 80) return Color.LightBlue;
            if (percent > 60) return Color.DarkBlue;
            if (percent > 40) return Color.GreenYellow;
            if (percent > 20) return Color.OrangeRed;
            return Color.DarkRed;
        }

        public static Color GetEmissiveColorFromFloatEmitter(float percent)
        {
            if (percent > 80) return Color.White;
            if (percent > 60) return Color.Blue;
            if (percent > 40) return Color.Yellow;
            if (percent > 20) return Color.Orange;
            return Color.Red;
        }

        public static long ThereCanBeOnlyOne(IMyCubeBlock shield)
        {
            if (Session.Enforced.Debug == 1) Log.Line($"ThereCanBeOnlyOne start");
            var shieldBlocks = new List<MyCubeBlock>();
            foreach (var block in ((MyCubeGrid)shield.CubeGrid).GetFatBlocks())
            {
                if (block == null) continue;

                if (block.BlockDefinition.BlockPairName == "DS_Control")
                {
                    if (block.IsWorking) return block.EntityId;
                    shieldBlocks.Add(block);
                }
            }
            Log.Line($"ThereCanBeOnlyOne: No working block found");
            var shieldDistFromCenter = double.MinValue;
            var shieldId = long.MinValue;
            foreach (var s in shieldBlocks)
            {
                if (s == null) continue;

                var dist = Vector3D.DistanceSquared(s.PositionComp.WorldVolume.Center, shield.CubeGrid.WorldVolume.Center);
                if (dist > shieldDistFromCenter)
                {
                    shieldDistFromCenter = dist;
                    shieldId = s.EntityId;
                }
            }
            if (Session.Enforced.Debug == 1) Log.Line($"ThereCanBeOnlyOne complete, found shield: {shieldId}");
            return shieldId;
        }

        public static bool CheckShieldType(IMyFunctionalBlock shield, bool warning, bool takeAction = false)
        {
            var realPlayerIds = new List<long>();
            GetRealPlayers(shield.PositionComp.WorldVolume.Center, 500f, realPlayerIds);
            foreach (var id in realPlayerIds)
            {
                if (!warning && shield.BlockDefinition.SubtypeId == "DefenseShieldsST" && !shield.CubeGrid.Physics.IsStatic)
                {
                    MyVisualScriptLogicProvider.ShowNotification("Station shields only allowed on stations", 5000, "Red", id);
                    warning = true;
                }
                else if (!warning && shield.BlockDefinition.SubtypeId == "DefenseShieldsLS" && shield.CubeGrid.Physics.IsStatic)
                {
                    MyVisualScriptLogicProvider.ShowNotification("Large Ship Shields only allowed on ships, not stations", 5000, "Red", id);
                    warning = true;
                }
                else if (!warning && takeAction)
                {
                }
                else if (!warning)
                {
                    MyVisualScriptLogicProvider.ShowNotification("Control station in standby while active unit is functional", 5000, "Red", id);
                    warning = true;
                }
            }

            if (takeAction && warning)
            {
                warning = false;
                shield.Enabled = false;
            }
            return warning;
        }

        public static bool ShieldDistanceCheck(IMyCubeBlock shield, int x, double range)
        {
            if (MyAPIGateway.Session.Player.Character == null) return false;

            var pPosition = MyAPIGateway.Session.Player.Character.GetPosition();
            var cPosition = shield.CubeGrid.PositionComp.GetPosition();
            var dist = Vector3D.DistanceSquared(cPosition, pPosition) <= (x + range) * (x + range);
            return dist;
        }

        private static bool ShowControlOreDetectorControls(IMyTerminalBlock block)
        {
            return block.BlockDefinition.SubtypeName.Contains("OreDetector");
        }

        public static void RemoveOreUi()
        {
            var actions = new List<IMyTerminalAction>();
            MyAPIGateway.TerminalControls.GetActions<Sandbox.ModAPI.Ingame.IMyOreDetector>(out actions);
            var actionAntenna = actions.First((x) => x.Id.ToString() == "BroadcastUsingAntennas");
            actionAntenna.Enabled = ShowControlOreDetectorControls;

            var controls = new List<IMyTerminalControl>();
            MyAPIGateway.TerminalControls.GetControls<Sandbox.ModAPI.Ingame.IMyOreDetector>(out controls);
            var antennaControl = controls.First((x) => x.Id.ToString() == "BroadcastUsingAntennas");
            antennaControl.Visible = ShowControlOreDetectorControls;
            var radiusControl = controls.First((x) => x.Id.ToString() == "Range");
            radiusControl.Visible = ShowControlOreDetectorControls;
        }

        public static void GetDefinitons()
        {
            try
            {
                var defintions = MyDefinitionManager.Static.GetAllDefinitions();
                foreach (var def in defintions)
                {
                    if (!(def is MyAmmoMagazineDefinition)) continue;
                    var ammoDef = def as MyAmmoMagazineDefinition;
                    var ammo = MyDefinitionManager.Static.GetAmmoDefinition(ammoDef.AmmoDefinitionId);
                    if (!(ammo is MyMissileAmmoDefinition)) continue;
                    var shot = ammo as MyMissileAmmoDefinition;
                    if (Session.AmmoCollection.ContainsKey(shot.MissileModelName)) continue;
                    Session.AmmoCollection.Add(shot.MissileModelName, new AmmoInfo(shot.IsExplosive, shot.MissileExplosionDamage, shot.MissileExplosionRadius, shot.DesiredSpeed, shot.MissileMass, shot.BackkickForce));
                }
                Log.Line($"Definitions Loaded");
            }
            catch (Exception ex) { Log.Line($"Exception in GetAmmoDefinitions: {ex}"); }
        }

        public static double CreateNormalFit(IMyCubeBlock shield, Vector3D gridHalfExtents)
        {
            var blockPoints = new Vector3D[8];
            var blocks = new List<IMySlimBlock>();

            var subGrids = MyAPIGateway.GridGroups.GetGroup(shield.CubeGrid, GridLinkTypeEnum.Logical);
            foreach (var grid in subGrids) grid.GetBlocks(blocks);

            var sqrt2 = Math.Sqrt(2);
            var sqrt3 = Math.Sqrt(3);
            const double percent = 0.1;
            var last = 0;
            var repeat = 0;
            for (int i = 0; i <= 10; i++)
            {
                var ellipsoidAdjust = MathHelper.Lerp(sqrt2, sqrt3, i * percent);

                var shieldSize = gridHalfExtents * ellipsoidAdjust;
                var mobileMatrix = MatrixD.CreateScale(shieldSize);
                mobileMatrix.Translation = shield.CubeGrid.PositionComp.LocalVolume.Center;
                var matrixInv = MatrixD.Invert(mobileMatrix * shield.CubeGrid.WorldMatrix);

                var c = 0;
                foreach (var block in blocks)
                {
                    BoundingBoxD blockBox;
                    block.GetWorldBoundingBox(out blockBox);

                    blockBox.GetCorners(blockPoints);

                    foreach (var point in blockPoints) if (!CustomCollision.PointInShield(point, matrixInv)) c++;
                }

                if (c == last) repeat++;
                else repeat = 0;

                if (c == 0)
                {
                    return MathHelper.Lerp(sqrt2, sqrt3, i * percent);
                }
                last = c;
                if (i == 10 && repeat > 2) return MathHelper.Lerp(sqrt2, sqrt3, ((10 - repeat) + 1) * percent);
            }
            return sqrt3;
        }

        public static double CreateExtendedFit(IMyCubeBlock shield, Vector3D gridHalfExtents)
        {
            var blockPoints = new Vector3D[8];
            var blocks = new List<IMySlimBlock>();

            var subGrids = MyAPIGateway.GridGroups.GetGroup(shield.CubeGrid, GridLinkTypeEnum.Logical);
            foreach (var grid in subGrids) grid.GetBlocks(blocks);

            var sqrt3 = Math.Sqrt(3);
            var sqrt5 = Math.Sqrt(5);
            const double percent = 0.1;
            var last = 0;
            var repeat = 0;
            for (int i = 0; i <= 10; i++)
            {
                var ellipsoidAdjust = MathHelper.Lerp(sqrt3, sqrt5, i * percent);

                var shieldSize = gridHalfExtents * ellipsoidAdjust;
                var mobileMatrix = MatrixD.CreateScale(shieldSize);
                mobileMatrix.Translation = shield.CubeGrid.PositionComp.LocalVolume.Center;
                var matrixInv = MatrixD.Invert(mobileMatrix * shield.CubeGrid.WorldMatrix);

                var c = 0;
                foreach (var block in blocks)
                {
                    BoundingBoxD blockBox;
                    block.GetWorldBoundingBox(out blockBox);

                    blockBox.GetCorners(blockPoints);

                    foreach (var point in blockPoints) if (!CustomCollision.PointInShield(point, matrixInv)) c++;
                }

                if (c == last) repeat++;
                else repeat = 0;

                if (c == 0)
                {
                    return MathHelper.Lerp(sqrt3, sqrt5, i * percent);
                }
                last = c;
                if (i == 10 && repeat > 2) return MathHelper.Lerp(sqrt3, sqrt5, ((10 - repeat) + 1) * percent);
            }
            return sqrt5;
        }

        public static int BlockCount(IMyCubeBlock shield)
        {
            var subGrids = MyAPIGateway.GridGroups.GetGroup(shield.CubeGrid, GridLinkTypeEnum.Logical);
            var blockCnt = 0;
            foreach (var grid in subGrids)
            {
                blockCnt += ((MyCubeGrid) grid).BlocksCount;
            }
            return blockCnt;
        }	
        public static void PrepConfigFile()
        {
            const int baseScaler = 30;
            const float nerf = 0f;
            const float efficiency = 100f;
            const int stationRatio = 1;
            const int largeShipRate = 2;
            const int smallShipRatio = 1;
            const int disableVoxel = 0;
            const int disableGridDmg = 0;
            const int debug = 0;
            const bool altRecharge = false;
            const int version = 57;

            var dsCfgExists = MyAPIGateway.Utilities.FileExistsInGlobalStorage("DefenseShields.cfg");
            if (dsCfgExists)
            {
                var unPackCfg = MyAPIGateway.Utilities.ReadFileInGlobalStorage("DefenseShields.cfg");
                var unPackedData = MyAPIGateway.Utilities.SerializeFromXML<DefenseShieldsEnforcement>(unPackCfg.ReadToEnd());

                if (Session.Enforced.Debug == 1) Log.Line($"unPackedData is: {unPackedData}\nServEnforced are: {Session.Enforced}");

                if (unPackedData.Version == version) return;
                Log.Line($"outdated config file regenerating, file version: {unPackedData.Version} - current version: {version}");
                Session.Enforced.BaseScaler = !unPackedData.BaseScaler.Equals(-1) ? unPackedData.BaseScaler : baseScaler;
                Session.Enforced.Nerf = !unPackedData.Nerf.Equals(-1f) ? unPackedData.Nerf : nerf;
                Session.Enforced.Efficiency = !unPackedData.Efficiency.Equals(-1f) ? unPackedData.Efficiency : efficiency;
                Session.Enforced.StationRatio = !unPackedData.StationRatio.Equals(-1) ? unPackedData.StationRatio : stationRatio;
                Session.Enforced.LargeShipRatio = !unPackedData.LargeShipRatio.Equals(-1) ? unPackedData.LargeShipRatio : largeShipRate;
                Session.Enforced.SmallShipRatio = !unPackedData.SmallShipRatio.Equals(-1) ? unPackedData.SmallShipRatio : smallShipRatio;
                Session.Enforced.DisableVoxelSupport = !unPackedData.DisableVoxelSupport.Equals(-1) ? unPackedData.DisableVoxelSupport : disableVoxel;
                Session.Enforced.DisableGridDamageSupport = !unPackedData.DisableGridDamageSupport.Equals(-1) ? unPackedData.DisableGridDamageSupport : disableGridDmg;
                Session.Enforced.Debug = !unPackedData.Debug.Equals(-1) ? unPackedData.Debug : debug;
                Session.Enforced.AltRecharge = unPackedData.AltRecharge;
                Session.Enforced.Version = version;

                unPackedData = null;
                unPackCfg.Close();
                unPackCfg.Dispose();
                MyAPIGateway.Utilities.DeleteFileInGlobalStorage("DefenseShields.cfg");
                var newCfg = MyAPIGateway.Utilities.WriteFileInGlobalStorage("DefenseShields.cfg");
                var newData = MyAPIGateway.Utilities.SerializeToXML(Session.Enforced);
                newCfg.Write(newData);
                newCfg.Flush();
                newCfg.Close();

                if (Session.Enforced.Debug == 1)
                    Log.Line(
                        $"wrote modified config file - file exists: {MyAPIGateway.Utilities.FileExistsInGlobalStorage("DefenseShields.cfg")}");
            }
            else
            {
                Session.Enforced.BaseScaler = baseScaler;
                Session.Enforced.Nerf = nerf;
                Session.Enforced.Efficiency = efficiency;
                Session.Enforced.StationRatio = stationRatio;
                Session.Enforced.LargeShipRatio = largeShipRate;
                Session.Enforced.SmallShipRatio = smallShipRatio;
                Session.Enforced.DisableVoxelSupport = disableVoxel;
                Session.Enforced.DisableGridDamageSupport = disableGridDmg;
                Session.Enforced.Debug = debug;
                Session.Enforced.AltRecharge = altRecharge;
                Session.Enforced.Version = version;

                var cfg = MyAPIGateway.Utilities.WriteFileInGlobalStorage("DefenseShields.cfg");
                var data = MyAPIGateway.Utilities.SerializeToXML(Session.Enforced);
                cfg.Write(data);
                cfg.Flush();
                cfg.Close();

                if (Session.Enforced.Debug == 1)
                    Log.Line($"wrote new config file - file exists: {MyAPIGateway.Utilities.FileExistsInGlobalStorage("DefenseShields.cfg")}");
            }
        }

        public static void ReadConfigFile()
        {
            var dsCfgExists = MyAPIGateway.Utilities.FileExistsInGlobalStorage("DefenseShields.cfg");

            if (Session.Enforced.Debug == 1) Log.Line($"Reading config, file exists? {dsCfgExists}");

            if (!dsCfgExists) return;

            var cfg = MyAPIGateway.Utilities.ReadFileInGlobalStorage("DefenseShields.cfg");
            var data = MyAPIGateway.Utilities.SerializeFromXML<DefenseShieldsEnforcement>(cfg.ReadToEnd());
            Session.Enforced = data;

            if (Session.Enforced.Debug == 1) Log.Line($"Writing settings to mod:\n{data}");
        }
    }
}