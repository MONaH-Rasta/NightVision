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
        public const string PermAllowed = "nightvision.allowed";
        public const string PermUnlimitedNvg = "nightvision.unlimitednvg";
        public const string PermAuto = "nightvision.auto";

        private PluginConfig _config;
        private readonly Game.Rust.Libraries.Player _rustPlayer = Interface.Oxide.GetLibrary<Game.Rust.Libraries.Player>("Player");
        private EnvSync _envSync;
        private readonly Dictionary<ulong, NvPlayerData> _playerData = new();
        private Dictionary<ulong, float> _playerTimes = new();
        private DateTime _nvDate;
        private readonly List<ulong> _connected = new();

        private bool _apiBlockEnvUpdates;

        private void SendChatMsg(BasePlayer pl, string msg, string prefix = null) =>
            _rustPlayer.Message(pl, msg, prefix ?? lang.GetMessage("ChatPrefix", this, pl.UserIDString), Convert.ToUInt64(_config.ChatIconID), Array.Empty<object>());

        private void Init()
        {
            permission.RegisterPermission(PermAllowed, this);
            permission.RegisterPermission(PermUnlimitedNvg, this);
            permission.RegisterPermission(PermAuto, this);

            _playerTimes = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, float>>($"{Name}\\playerTimes");
        }

        private void OnServerInitialized()
        {
            _envSync = BaseNetworkable.serverEntities.OfType<EnvSync>().FirstOrDefault();

            timer.Every(5f, () =>
            {
                if (!_envSync.limitNetworking)
                    _envSync.limitNetworking = true;

                List<Connection> subscribers = _envSync.net.group.subscribers;
                if (subscribers is { Count: > 0 })
                {
                    for (int i = 0; i < subscribers.Count; i++)
                    {
                        Connection connection = subscribers[i];
                        BasePlayer basePlayer = connection.player as BasePlayer;

                        if (basePlayer == null) continue;

                        NvPlayerData nvPlayerData = GetNvPlayerData(basePlayer);

                        if (_apiBlockEnvUpdates && !nvPlayerData.TimeLocked) continue;

                        NetWrite write = Net.sv.StartWrite();
                        connection.validate.entityUpdates++;
                        BaseNetworkable.SaveInfo saveInfo = new()
                        {
                            forConnection = connection,
                            forDisk = false
                        };
                        write.PacketID(Message.Type.Entities);
                        write.UInt32(connection.validate.entityUpdates);

                        using (saveInfo.msg = Facepunch.Pool.Get<ProtoBuf.Entity>())
                        {
                            _envSync.Save(saveInfo);
                            if (nvPlayerData.TimeLocked)
                            {
                                saveInfo.msg.environment.dateTime = _nvDate.AddHours(nvPlayerData.Time).ToBinary();
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

                            using BufferStream bufferStream = new BufferStream();
                            bufferStream.Initialize();
                            try
                            {
                                saveInfo.msg.ToProto(bufferStream);
                                _envSync.PostSave(saveInfo);

                                ArraySegment<byte> segment = bufferStream.GetBuffer();
                                if (segment.Array == null || segment.Count == 0)
                                {
                                    LogError("BufferStream returned an empty or null segment.");
                                    continue;
                                }

                                byte[] buffer = new byte[segment.Count];
                                Array.Copy(segment.Array, segment.Offset, buffer, 0, segment.Count);

                                write.Write(buffer, 0, buffer.Length);
                                write.Send(new SendInfo(connection));
                            }
                            catch (Exception ex)
                            {
                                LogError($"Failed to serialize with BufferStream: {ex.Message}");
                            }
                        }
                    }
                }
            });

        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player != null && !_connected.Contains(player.userID.Get()))
                _connected.Add(player.userID);
        }

        private void OnPlayerDisconnected(BasePlayer pl, string _)
        {
            if (pl != null)
                _playerData.Remove(pl.userID);

            if (pl != null && _connected.Contains(pl.userID.Get()))
                _connected.Remove(pl.userID.Get());
        }

        private void OnPlayerSleepEnded(BasePlayer pl)
        {
            if (pl == null)
                return;

            if (!_connected.Contains(pl.userID.Get()))
                return;

            if (permission.UserHasPermission(pl.UserIDString, PermAuto))
                NightVisionCommand(pl.IPlayer, "nv", new[] { _playerTimes.TryGetValue(pl.userID, out float time) ? time.ToString() : "" });

            _connected.Remove(pl.userID.Get());
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
                StringBuilder sb = new();
                sb.AppendLine(lang.GetMessage("HelpTitle", this, pl.UserIDString));
                sb.AppendLine(lang.GetMessage("Help1", this, pl.UserIDString));

                if (permission.UserHasPermission(pl.UserIDString, PermUnlimitedNvg))
                    sb.AppendLine(lang.GetMessage("Help2", this, pl.UserIDString));

                SendChatMsg(pl, sb.ToString(), "");
                return;
            }

            switch (command)
            {
                case "nightvision":
                case "nv":
                    if (!permission.UserHasPermission(pl.UserIDString, PermAllowed))
                    {
                        SendChatMsg(pl, lang.GetMessage("NoPerms", this, pl.UserIDString));
                        return;
                    }

                    NvPlayerData nvpd = GetNvPlayerData(pl);
                    nvpd.TimeLocked = !nvpd.TimeLocked;
                    nvpd.Time = args.Length > 0 && float.TryParse(args[0], out float time) && time is >= 0 and <= 24 ? time : _config.Time;

                    if (permission.UserHasPermission(pl.UserIDString, PermAuto))
                    {
                        _playerTimes[pl.userID] = nvpd.Time;
                        SaveData();
                    }

                    SendChatMsg(pl, string.Format(lang.GetMessage(nvpd.TimeLocked ? "TimeLocked" : "TimeUnlocked", this, pl.UserIDString), nvpd.Time));
                    break;
                case "unlimitednvg":
                case "unvg":
                    if (!permission.UserHasPermission(pl.UserIDString, PermUnlimitedNvg))
                    {
                        SendChatMsg(pl, lang.GetMessage("NoPerms", this, pl.UserIDString));
                        return;
                    }

                    List<Item> unvgInv = pl.inventory.containerWear.itemList.FindAll(x => x.info.name == "hat.nvg.item");
                    if (unvgInv.Count > 0)
                    {
                        foreach (Item i in unvgInv)
                        {
                            if (i.condition == 1f && i.amount == 0)
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
                        Item item = ItemManager.CreateByName("nightvisiongoggles");
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
            if (item.info.name == "hat.nvg.item" && item.condition == 1f && item.amount == 0) return null;

            NextTick(() =>
            {
                if (inventory != null && inventory.containerMain != null)
                {
                    foreach (Item i in inventory.containerMain.itemList.FindAll(x => x.info.name == "hat.nvg.item"))
                    {
                        if (i.condition == 1f && i.amount == 0)
                        {
                            i.SwitchOnOff(false);
                            i.Remove();
                            inventory.containerWear.capacity = 7;
                        }
                    }
                }
                if (inventory != null && inventory.containerBelt != null)
                {
                    foreach (Item i in inventory.containerBelt.itemList.FindAll(x => x.info.name == "hat.nvg.item"))
                    {
                        if (i.condition == 1f && i.amount == 0)
                        {
                            i.SwitchOnOff(false);
                            i.Remove();
                            inventory.containerWear.capacity = 7;
                        }
                    }
                }
                if (inventory != null && inventory.containerWear != null)
                {
                    foreach (Item i in inventory.containerWear.itemList.FindAll(x => x.info.name == "hat.nvg.item"))
                    {
                        if (i.condition == 1f && i.amount == 0)
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
            if (item != null && item.info.name == "hat.nvg.item" && item.condition == 1f && item.amount == 0)
            {
                item.Remove();
            }
        }

        private NvPlayerData GetNvPlayerData(BasePlayer pl)
        {
            _playerData[pl.userID] = _playerData.TryGetValue(pl.userID, out NvPlayerData value) ? value : new NvPlayerData();
            _playerData[pl.userID].TimeLocked = !(!_playerData[pl.userID].TimeLocked || !permission.UserHasPermission(pl.UserIDString, PermAllowed));
            return _playerData[pl.userID];
        }

        #region Plugin-API

        [HookMethod("LockPlayerTime")]
        void LockPlayerTime_PluginAPI(BasePlayer player, float time)
        {
            NvPlayerData data = GetNvPlayerData(player);
            data.TimeLocked = true;
            data.Time = time;
        }

        [HookMethod("UnlockPlayerTime")]
        void UnlockPlayerTime_PluginAPI(BasePlayer player)
        {
            NvPlayerData data = GetNvPlayerData(player);
            data.TimeLocked = false;
        }

        [HookMethod("IsPlayerTimeLocked")]
        bool IsPlayerTimeLocked_PluginAPI(BasePlayer player)
        {
            NvPlayerData data = GetNvPlayerData(player);
            return data.TimeLocked;
        }

        [HookMethod("BlockEnvUpdates")]
        void BlockEnvUpdates_PluginAPI(bool blockEnv)
        {
            _apiBlockEnvUpdates = blockEnv;
        }

        #endregion

        #region Config
        private readonly DateTime _defaultDate = new(2024, 1, 25);

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
            PluginConfig config = new()
            {
                Date = _defaultDate.ToString("M/d/yyyy"),
                Time = 12
            };
            return config;
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<PluginConfig>();

            if (_config.Time < 0 || _config.Time > 24)
                _config.Time = 12;

            if (!DateTime.TryParse(_config.Date, out _nvDate))
            {
                _nvDate = _defaultDate;
                _config.Date = _defaultDate.ToString("M/d/yyyy");

                if (_config.Time == 0)
                    _config.Time = 12;
            }

            Config.WriteObject(_config, true);
        }

        private class PluginConfig
        {
            public string ChatIconID = "0";
            public string Date;
            public float Time;

        }
        #endregion

        private class NvPlayerData
        {
            public bool TimeLocked;
            public float Time = 12f;
        }
    }
}
