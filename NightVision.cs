using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Network;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("NightVision", "Clearshot", "2.4.1")]
    [Description("Allows players to see at night")]
    class NightVision : CovalencePlugin
    {
        private PluginConfig _config;
        private Game.Rust.Libraries.Player _rustPlayer = Interface.Oxide.GetLibrary<Game.Rust.Libraries.Player>("Player");
        private EnvSync _envSync;
        private Dictionary<ulong, NVPlayerData> _playerData = new Dictionary<ulong, NVPlayerData>();
        private Dictionary<ulong, float> _playerTimes = new Dictionary<ulong, float>();
        private DateTime _nvDate;
        private List<ulong> _connected = new List<ulong>();

        private string PERM_ALLOWED = "nightvision.allowed";
        private string PERM_UNLIMITEDNVG = "nightvision.unlimitednvg";
        private string PERM_AUTO = "nightvision.auto";

        private bool API_blockEnvUpdates = false;

        private void SendChatMsg(BasePlayer pl, string msg, string prefix = null) =>
            _rustPlayer.Message(pl, msg, prefix != null ? prefix : lang.GetMessage("ChatPrefix", this, pl.UserIDString), Convert.ToUInt64(_config.chatIconID), Array.Empty<object>());

        private void Init()
        {
            permission.RegisterPermission(PERM_ALLOWED, this);
            permission.RegisterPermission(PERM_UNLIMITEDNVG, this);
            permission.RegisterPermission(PERM_AUTO, this);

            _playerTimes = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, float>>($"{Name}\\playerTimes");
        }

        private void OnServerInitialized()
        {
            _envSync = BaseNetworkable.serverEntities.OfType<EnvSync>().FirstOrDefault();

            timer.Every(5f, () => {
                if (!_envSync.limitNetworking)
                    _envSync.limitNetworking = true;

                List<Connection> subscribers = _envSync.net.group.subscribers;
                if (subscribers != null && subscribers.Count > 0)
                {
                    for (int i = 0; i < subscribers.Count; i++)
                    {
                        Connection connection = subscribers[i];
                        global::BasePlayer basePlayer = connection.player as global::BasePlayer;

                        if (!(basePlayer == null)) {
                            NVPlayerData nvPlayerData = GetNVPlayerData(basePlayer);

                            if (API_blockEnvUpdates && !nvPlayerData.timeLocked) continue;

                            if (connection != null)
                            {
                                var write = Net.sv.StartWrite();
                                connection.validate.entityUpdates = connection.validate.entityUpdates + 1;
                                BaseNetworkable.SaveInfo saveInfo = new global::BaseNetworkable.SaveInfo
                                {
                                    forConnection = connection,
                                    forDisk = false
                                };
                                write.PacketID(Message.Type.Entities);
                                write.UInt32(connection.validate.entityUpdates);
                                using (saveInfo.msg = Facepunch.Pool.Get<ProtoBuf.Entity>())
                                {
                                    _envSync.Save(saveInfo);
                                    if (nvPlayerData.timeLocked)
                                    {
                                        saveInfo.msg.environment.dateTime = _nvDate.AddHours(nvPlayerData.time).ToBinary();
                                        saveInfo.msg.environment.fog = 0;
                                        saveInfo.msg.environment.rain = 0;
                                        saveInfo.msg.environment.clouds = 0;
										saveInfo.msg.environment.wind = 0;
                                    }
                                    if (saveInfo.msg.baseEntity == null)
                                    {
                                        LogError(this + ": ToStream - no BaseEntity!?");
                                    }
                                    if (saveInfo.msg.baseNetworkable == null)
                                    {
                                        LogError(this + ": ToStream - no baseNetworkable!?");
                                    }
                                    saveInfo.msg.ToProto(write);
                                    _envSync.PostSave(saveInfo);
                                    write.Send(new SendInfo(connection));
                                }
                            }
                        }
                    }
                }
            });
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player != null && !_connected.Contains(player.userID))
                _connected.Add(player.userID);
        }

        private void OnPlayerDisconnected(BasePlayer pl, string reason)
        {
            if (pl != null && _playerData.ContainsKey(pl.userID))
                _playerData.Remove(pl.userID);

            if (pl != null && _connected.Contains(pl.userID))
                _connected.Remove(pl.userID);
        }

        private void OnPlayerSleepEnded(BasePlayer pl)
        {
            if (pl == null)
                return;

            if (!_connected.Contains(pl.userID))
                return;

            if (permission.UserHasPermission(pl.UserIDString, PERM_AUTO))
                NightVisionCommand(pl.IPlayer, "nv", new string[] { _playerTimes.ContainsKey(pl.userID) ? _playerTimes[pl.userID].ToString() : "" });

            _connected.Remove(pl.userID);
        }

        private void Unload()
        {
            if (_envSync != null)
                _envSync.limitNetworking = false;
        }

        private void SaveData()
        {
            Interface.Oxide.DataFileSystem.WriteObject($"{Name}\\playerTimes", _playerTimes);
        }

        [Command("nightvision", "nv", "unlimitednvg", "unvg")]
        private void NightVisionCommand(IPlayer player, string command, string[] args)
        {
            if (player == null) return;
            BasePlayer pl = (BasePlayer)player.Object;
            if (pl == null) return;

            if (args.Length != 0 && args[0] == "help")
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(lang.GetMessage("HelpTitle", this, pl.UserIDString));
                sb.AppendLine(lang.GetMessage("Help1", this, pl.UserIDString));

                if (permission.UserHasPermission(pl.UserIDString, PERM_UNLIMITEDNVG))
                    sb.AppendLine(lang.GetMessage("Help2", this, pl.UserIDString));

                SendChatMsg(pl, sb.ToString(), "");
                return;
            }

            NVPlayerData nvpd;
            switch(command)
            {
                case "nightvision":
                case "nv":
                    if (!permission.UserHasPermission(pl.UserIDString, PERM_ALLOWED))
                    {
                        SendChatMsg(pl, lang.GetMessage("NoPerms", this, pl.UserIDString));
                        return;
                    }

                    nvpd = GetNVPlayerData(pl);
                    nvpd.timeLocked = !nvpd.timeLocked;
                    float time;
                    nvpd.time = args.Length > 0 && float.TryParse(args[0], out time) && time >= 0 && time <= 24 ? time : _config.time;

                    if (permission.UserHasPermission(pl.UserIDString, PERM_AUTO))
                    {
                        _playerTimes[pl.userID] = nvpd.time;
                        SaveData();
                    }

                    SendChatMsg(pl, string.Format(lang.GetMessage(nvpd.timeLocked ? "TimeLocked" : "TimeUnlocked", this, pl.UserIDString), nvpd.time));
                    break;
                case "unlimitednvg":
                case "unvg":
                    if (!permission.UserHasPermission(pl.UserIDString, PERM_UNLIMITEDNVG))
                    {
                        SendChatMsg(pl, lang.GetMessage("NoPerms", this, pl.UserIDString));
                        return;
                    }

                    List<Item> unvgInv = pl.inventory.containerWear.itemList.FindAll((Item x) => x.info.name == "hat.nvg.item");
                    if (unvgInv.Count > 0)
                    {
                        foreach(Item i in unvgInv)
                        {
                            if (i.condition == 1 && i.amount == 0)
                            {
                                i.SwitchOnOff(false);
                                i.Remove();
                            }
                        }

                        pl.inventory.containerWear.capacity = 7;
                        SendChatMsg(pl, lang.GetMessage("RemoveUNVG", this, pl.UserIDString));
                    }
                    else
                    {
                        var item = ItemManager.CreateByName("nightvisiongoggles", 1, 0UL);
                        if (item != null)
                        {
                            item.OnVirginSpawn();
                            item.SwitchOnOff(true);
                            item.condition = 1;
                            item.amount = 0;
                            pl.inventory.containerWear.capacity = 8;
                            item.MoveToContainer(pl.inventory.containerWear, 7);
                            SendChatMsg(pl, lang.GetMessage("EquipUNVG", this, pl.UserIDString));
                        }
                    }
                    break;
            }
        }

        private object CanWearItem(PlayerInventory inventory, Item item, int targetSlot)
        {
            if (item == null || inventory == null) return null;
            if (item.info.name == "hat.nvg.item" && item.condition == 1 && item.amount == 0) return null;

            NextTick(() =>
            {
                if (inventory != null && inventory.containerMain != null)
                {
                    foreach (Item i in inventory.containerMain.itemList.FindAll((Item x) => x.info.name == "hat.nvg.item"))
                    {
                        if (i != null && i.condition == 1 && i.amount == 0)
                        {
                            i.SwitchOnOff(false);
                            i.Remove();
                            inventory.containerWear.capacity = 7;
                        }
                    }
                }
                if (inventory != null && inventory.containerBelt != null)
                {
                    foreach (Item i in inventory.containerBelt.itemList.FindAll((Item x) => x.info.name == "hat.nvg.item"))
                    {
                        if (i != null && i.condition == 1 && i.amount == 0)
                        {
                            i.SwitchOnOff(false);
                            i.Remove();
                            inventory.containerWear.capacity = 7;
                        }
                    }
                }
                if (inventory != null && inventory.containerWear != null)
                {
                    foreach (Item i in inventory.containerWear.itemList.FindAll((Item x) => x.info.name == "hat.nvg.item"))
                    {
                        if (i != null && i.condition == 1 && i.amount == 0)
                        {
                            i.SwitchOnOff(false);
                            i.Remove();
                            inventory.containerWear.capacity = 7;
                        }
                    }
                }
            });
            return null;
        }

        private void OnItemDropped(Item item, BaseEntity entity)
        {
            if (item != null && item.info.name == "hat.nvg.item" && item.condition == 1 && item.amount == 0)
            {
                item.Remove();
            }
        }

        private NVPlayerData GetNVPlayerData(BasePlayer pl)
        {
            _playerData[pl.userID] = _playerData.ContainsKey(pl.userID) ? _playerData[pl.userID] : new NVPlayerData();
			_playerData[pl.userID].timeLocked = !(!_playerData[pl.userID].timeLocked || !permission.UserHasPermission(pl.UserIDString, PERM_ALLOWED));
            return _playerData[pl.userID];
        }

        #region Plugin-API

        [HookMethod("LockPlayerTime")]
        void LockPlayerTime_PluginAPI(BasePlayer player, float time)
        {
            var data = GetNVPlayerData(player);
            data.timeLocked = true;
            data.time = time;
        }

        [HookMethod("UnlockPlayerTime")]
        void UnlockPlayerTime_PluginAPI(BasePlayer player)
        {
            var data = GetNVPlayerData(player);
            data.timeLocked = false;
        }

        [HookMethod("IsPlayerTimeLocked")]
        bool IsPlayerTimeLocked_PluginAPI(BasePlayer player)
        {
            var data = GetNVPlayerData(player);
            return data.timeLocked;
        }

        [HookMethod("BlockEnvUpdates")]
        void BlockEnvUpdates_PluginAPI(bool blockEnv)
        {
            API_blockEnvUpdates = blockEnv;
        }

        #endregion

        #region Config
        private DateTime _defaultDate = new DateTime(2024, 1, 25);

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["ChatPrefix"] = "<color=#00ff00>[Night Vision]</color>",
                ["NoPerms"] = "You do not have permission to use this command!",
                ["TimeLocked"] = "Time locked to {0}",
                ["TimeUnlocked"] = "Time unlocked",
                ["HelpTitle"] = "<size=16><color=#00ff00>Night Vision</color> Help</size>\n",
                ["Help1"] = "<color=#00ff00>/nightvision <0-24>(/nv)</color> - Toggle time lock night vision with optional time 0-24",
                ["Help2"] = "<color=#00ff00>/unlimitednvg (/unvg)</color> - Equip/remove unlimited night vision goggles",
                ["EquipUNVG"] = "Equipped unlimited night vision goggles",
                ["RemoveUNVG"] = "Removed unlimited night vision goggles"
            }, this);
        }

        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(GetDefaultConfig(), true);
        }

        private PluginConfig GetDefaultConfig()
        {
            PluginConfig config = new PluginConfig();
            config.date = _defaultDate.ToString("M/d/yyyy");
            config.time = 12;
            return config;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<PluginConfig>();

            if (_config.time < 0 || _config.time > 24)
                _config.time = 12;

            if (!DateTime.TryParse(_config.date, out _nvDate))
            {
                _nvDate = _defaultDate;
                _config.date = _defaultDate.ToString("M/d/yyyy");

                if (_config.time == 0)
                    _config.time = 12;
            }

            Config.WriteObject(_config, true);
        }

        private class PluginConfig
        {
            public string chatIconID = "0";
            public string date;
            public float time;

        }
        #endregion

        private class NVPlayerData
        {
            public bool timeLocked = false;
            public float time = 12f;
        }
    }
}
