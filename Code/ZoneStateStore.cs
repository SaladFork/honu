﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using watchtower.Models;

namespace watchtower.Code {

    public class ZoneStateStore {

        private static ZoneStateStore _Instance = new ZoneStateStore();
        public static ZoneStateStore Get() { return ZoneStateStore._Instance; }

        public ConcurrentDictionary<string, ZoneState> Zones = new();

        public ZoneState? GetZone(short worldID, int zoneID) {
            Zones.TryGetValue($"{worldID}:{zoneID}", out ZoneState? state);
            return state;
        }

        public void SetZone(short worldID, int zoneID, ZoneState state) {
            Zones[$"{worldID}:{zoneID}"] = state;
        }

        public void UnlockZone(short worldID, int zoneID) {
            ZoneState? zone = GetZone(worldID, zoneID);
            if (zone != null) {
                zone.IsOpened = true;
                SetZone(worldID, zoneID, zone);
            }
        }

    }
}
