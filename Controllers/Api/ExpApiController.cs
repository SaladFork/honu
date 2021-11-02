using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using watchtower.Code.Constants;
using watchtower.Constants;
using watchtower.Models;
using watchtower.Models.Api;
using watchtower.Models.Census;
using watchtower.Models.Db;
using watchtower.Models.Events;
using watchtower.Services.Db;
using watchtower.Services.Repositories;

namespace watchtower.Controllers {

    [ApiController]
    [Route("/api/exp")]
    public class ExpApiController : ControllerBase {

        private readonly ILogger<ExpApiController> _Logger;

        private readonly ICharacterRepository _CharacterRepository;
        private readonly IItemRepository _ItemRepository;
        private readonly IOutfitRepository _OutfitRepository;

        private readonly IExpEventDbStore _ExpDbStore;
        private readonly ISessionDbStore _SessionDb;

        public ExpApiController(ILogger<ExpApiController> logger,
            ICharacterRepository charRepo, IExpEventDbStore killDb,
            IItemRepository itemRepo, IOutfitRepository outfitRepo,
            ISessionDbStore sessionDb) {

            _Logger = logger;

            _CharacterRepository = charRepo ?? throw new ArgumentNullException(nameof(charRepo));
            _ItemRepository = itemRepo ?? throw new ArgumentNullException(nameof(itemRepo));
            _OutfitRepository = outfitRepo ?? throw new ArgumentNullException(nameof(outfitRepo));

            _ExpDbStore = killDb ?? throw new ArgumentNullException(nameof(killDb));
            _SessionDb = sessionDb ?? throw new ArgumentNullException(nameof(sessionDb));
        }

        [HttpGet("session/{sessionID}")]
        public async Task<ActionResult<List<ExpEvent>>> GetBySessionID(long sessionID) {
            Session? session = await _SessionDb.GetByID(sessionID);
            if (session == null) {
                return NotFound($"{nameof(Session)} {sessionID}");
            }

            List<ExpEvent> events = await _ExpDbStore.GetByCharacterID(session.CharacterID, session.Start, session.End ?? DateTime.UtcNow);
            List<ExpandedExpEvent> expanded = new List<ExpandedExpEvent>(events.Count);

            Dictionary<string, PsCharacter?> chars = new Dictionary<string, PsCharacter?>();

            foreach (ExpEvent ev in events) {
                ExpandedExpEvent ex = new ExpandedExpEvent();
                ex.Event = ev;

                if (chars.ContainsKey(ev.SourceID) == false) {
                    chars.Add(ev.SourceID, await _CharacterRepository.GetByID(ev.SourceID));
                }

                ex.Source = chars[ev.SourceID];

                if (ev.OtherID.Length == 19) {
                    if (chars.ContainsKey(ev.OtherID) == false) {
                        chars.Add(ev.OtherID, await _CharacterRepository.GetByID(ev.OtherID));
                    }
                    ex.Other = chars[ev.OtherID];
                }

                expanded.Add(ex);
            }

            return Ok(expanded);
        }

        [HttpGet("character/{charID}/{type}")]
        public async Task<ActionResult<List<CharacterExpSupportEntry>>> CharacterEntries(string charID, string type) {
            PsCharacter? c = await _CharacterRepository.GetByID(charID);
            if (c == null) {
                return NotFound($"{nameof(PsCharacter)} {charID}");
            }

            if (type == "spawns") {
                List<CharacterExpSupportEntry> spawns = await CharacterSpawns(charID);
                return spawns;
            }

            if (type == "vehicleKills") {
                List<CharacterExpSupportEntry> kills = await CharacterVehicleKills(charID);
                return kills;
            }

            List<int> expTypes = new List<int>();

            if (type == "heals") {
                expTypes = new List<int> { Experience.HEAL, Experience.SQUAD_HEAL };
            } else if (type == "revives") {
                expTypes = new List<int> { Experience.REVIVE, Experience.SQUAD_REVIVE };
            } else if (type == "resupplies") {
                expTypes = new List<int> { Experience.RESUPPLY, Experience.SQUAD_RESUPPLY };
            } else if (type == "shield_repair") {
                expTypes = new List<int> { Experience.SHIELD_REPAIR, Experience.SQUAD_SHIELD_REPAIR };
            } else {
                return BadRequest($"Unknown type '{type}'");
            }

            List<CharacterExpSupportEntry> list = await GetByCharacterAndExpIDs(charID, expTypes);

            return Ok(list);
        }

        [HttpGet("outfit/{outfitID}/{type}/{worldID}/{teamID}")]
        public async Task<ActionResult<List<OutfitExpEntry>>> OutfitEntries(string outfitID, string type, short worldID, short teamID) {
            PsOutfit? outfit = await _OutfitRepository.GetByID(outfitID);
            if (outfitID != "0" && outfit == null) {
                return NotFound($"{nameof(PsOutfit)} {outfitID}");
            }

            List<int> expTypes = new List<int>();

            if (type == "heals") {
                expTypes = new List<int> { Experience.HEAL, Experience.SQUAD_HEAL };
            } else if (type == "revives") {
                expTypes = new List<int> { Experience.REVIVE, Experience.SQUAD_REVIVE };
            } else if (type == "resupplies") {
                expTypes = new List<int> { Experience.RESUPPLY, Experience.SQUAD_RESUPPLY };
            } else if (type == "shield_repair") {
                expTypes = new List<int> { Experience.SHIELD_REPAIR, Experience.SQUAD_SHIELD_REPAIR };
            } else if (type == "spawns") {
                expTypes = new List<int> {
                    Experience.SQUAD_SPAWN, Experience.GALAXY_SPAWN_BONUS,
                    Experience.SUNDERER_SPAWN_BONUS, Experience.GENERIC_NPC_SPAWN,
                    Experience.SQUAD_VEHICLE_SPAWN_BONUS
                };
            } else if (type == "vehicleKills") {
                expTypes = Experience.VehicleKillEvents;
            } else {
                return BadRequest($"Unknown type '{type}'");
            }

            List<OutfitExpEntry> list = await GetByOutfitAndExpIDs(outfitID, expTypes, worldID, teamID);

            return Ok(list);
        }

        private async Task<List<CharacterExpSupportEntry>> CharacterSpawns(string charID) {
            List<ExpEvent> events = await _ExpDbStore.GetRecentByCharacterID(charID, 120);

            CharacterExpSupportEntry sundySpawns = new CharacterExpSupportEntry() { CharacterName = "Sunderers" };
            CharacterExpSupportEntry routerSpawns = new CharacterExpSupportEntry() { CharacterName = "Routers" };
            CharacterExpSupportEntry squadSpawns = new CharacterExpSupportEntry() { CharacterName = "Squad" };
            CharacterExpSupportEntry squadVehicleSpawns = new CharacterExpSupportEntry() { CharacterName = "Squad vehicle" };

            foreach (ExpEvent ev in events) {
                if (ev.ExperienceID == Experience.SUNDERER_SPAWN_BONUS) {
                    ++sundySpawns.Amount;
                } else if (ev.ExperienceID == Experience.GENERIC_NPC_SPAWN) {
                    ++routerSpawns.Amount;
                } else if (ev.ExperienceID == Experience.SQUAD_SPAWN) {
                    ++squadSpawns.Amount;
                } else if (ev.ExperienceID == Experience.SQUAD_VEHICLE_SPAWN_BONUS) {
                    ++squadVehicleSpawns.Amount;
                }
            }

            List<CharacterExpSupportEntry> list = (new List<CharacterExpSupportEntry>() {
                sundySpawns, routerSpawns, squadSpawns, squadVehicleSpawns
            }).OrderByDescending(iter => iter.Amount).ToList();

            return list;
        }

        private async Task<List<CharacterExpSupportEntry>> CharacterVehicleKills(string charID) {
            List<ExpEvent> events = await _ExpDbStore.GetRecentByCharacterID(charID, 120);

            CharacterExpSupportEntry flashKills = new CharacterExpSupportEntry() { CharacterName = "Flashes" };
            CharacterExpSupportEntry galaxyKills = new CharacterExpSupportEntry() { CharacterName = "Galaxies" };
            CharacterExpSupportEntry libKills = new CharacterExpSupportEntry() { CharacterName = "Liberators" };
            CharacterExpSupportEntry lightningKills = new CharacterExpSupportEntry() { CharacterName = "Lightnings" };
            CharacterExpSupportEntry magriderKills = new CharacterExpSupportEntry() { CharacterName = "Magriders" };
            CharacterExpSupportEntry mosquitoKills = new CharacterExpSupportEntry() { CharacterName = "Mosquitos" };
            CharacterExpSupportEntry prowlerKills = new CharacterExpSupportEntry() { CharacterName = "Prowlers" };
            CharacterExpSupportEntry reaverKills = new CharacterExpSupportEntry() { CharacterName = "Reavers" };
            CharacterExpSupportEntry scytheKills = new CharacterExpSupportEntry() { CharacterName = "Scythes" };
            CharacterExpSupportEntry vanguardKills = new CharacterExpSupportEntry() { CharacterName = "Vanguards" };
            CharacterExpSupportEntry harasserKills = new CharacterExpSupportEntry() { CharacterName = "Harassers" };
            CharacterExpSupportEntry valkKills = new CharacterExpSupportEntry() { CharacterName = "Valkyries" };
            CharacterExpSupportEntry antKills = new CharacterExpSupportEntry() { CharacterName = "ANTs" };
            CharacterExpSupportEntry colossusKills = new CharacterExpSupportEntry() { CharacterName = "Colossuses" };
            CharacterExpSupportEntry javelinKills = new CharacterExpSupportEntry() { CharacterName = "Javelins" };
            CharacterExpSupportEntry chimeraKills = new CharacterExpSupportEntry() { CharacterName = "Chimeras" };
            CharacterExpSupportEntry dervishKills = new CharacterExpSupportEntry() { CharacterName = "Dervishes" };

            foreach (ExpEvent ev in events) {
                if (ev.ExperienceID == Experience.VKILL_FLASH) {
                    ++flashKills.Amount;
                } else if (ev.ExperienceID == Experience.VKILL_GALAXY) {
                    ++galaxyKills.Amount;
                } else if (ev.ExperienceID == Experience.VKILL_LIBERATOR) {
                    ++libKills.Amount;
                } else if (ev.ExperienceID == Experience.VKILL_LIGHTNING) {
                    ++lightningKills.Amount;
                } else if (ev.ExperienceID == Experience.VKILL_MAGRIDER) {
                    ++magriderKills.Amount;
                } else if (ev.ExperienceID == Experience.VKILL_MOSQUITO) {
                    ++mosquitoKills.Amount;
                } else if (ev.ExperienceID == Experience.VKILL_PROWLER) {
                    ++prowlerKills.Amount;
                } else if (ev.ExperienceID == Experience.VKILL_REAVER) {
                    ++reaverKills.Amount;
                } else if (ev.ExperienceID == Experience.VKILL_SCYTHE) {
                    ++scytheKills.Amount;
                } else if (ev.ExperienceID == Experience.VKILL_VANGUARD) {
                    ++vanguardKills.Amount;
                } else if (ev.ExperienceID == Experience.VKILL_HARASSER) {
                    ++harasserKills.Amount;
                } else if (ev.ExperienceID == Experience.VKILL_VALKYRIE) {
                    ++valkKills.Amount;
                } else if (ev.ExperienceID == Experience.VKILL_ANT) {
                    ++antKills.Amount;
                } else if (ev.ExperienceID == Experience.VKILL_COLOSSUS) {
                    ++colossusKills.Amount;
                } else if (ev.ExperienceID == Experience.VKILL_JAVELIN) {
                    ++javelinKills.Amount;
                } else if (ev.ExperienceID == Experience.VKILL_CHIMERA) {
                    ++chimeraKills.Amount;
                } else if (ev.ExperienceID == Experience.VKILL_DERVISH) {
                    ++dervishKills.Amount;
                }
            }

            List<CharacterExpSupportEntry> list = (new List<CharacterExpSupportEntry>() {
                    flashKills, galaxyKills, libKills,
                    lightningKills, magriderKills, mosquitoKills,
                    prowlerKills, reaverKills, scytheKills,
                    vanguardKills, harasserKills, valkKills,
                    antKills, colossusKills, javelinKills,
                    dervishKills, chimeraKills
            }).Where(iter => iter.Amount > 0)
                .OrderByDescending(iter => iter.Amount).ToList();

            return list;
        }

        private async Task<List<CharacterExpSupportEntry>> GetByCharacterAndExpIDs(string charID, List<int> events) {
            List<ExpEvent> exps = await _ExpDbStore.GetRecentByCharacterID(charID, 120);

            Dictionary<string, CharacterExpSupportEntry> entries = new Dictionary<string, CharacterExpSupportEntry>();

            foreach (ExpEvent ev in exps) {
                if (events.Contains(ev.ExperienceID) == false) {
                    continue;
                }

                if (entries.TryGetValue(ev.OtherID, out CharacterExpSupportEntry? entry) == false) {
                    PsCharacter? character = await _CharacterRepository.GetByID(ev.OtherID);

                    entry = new CharacterExpSupportEntry() {
                        CharacterID = ev.OtherID,
                        CharacterName = character?.GetDisplayName() ?? $"Missing {ev.OtherID}"
                    };
                }

                ++entry.Amount;

                entries[ev.OtherID] = entry;
            }

            List<CharacterExpSupportEntry> list = entries.Values
                .OrderByDescending(iter => iter.Amount)
                .ToList();

            return list;
        }

        private async Task<List<OutfitExpEntry>> GetByOutfitAndExpIDs(string outfitID, List<int> events, short worldID, short teamID) {
            List<ExpEvent> exp = await _ExpDbStore.GetByOutfitID(outfitID, worldID, teamID, 120);

            Dictionary<string, OutfitExpEntry> entries = new Dictionary<string, OutfitExpEntry>();

            foreach (ExpEvent ev in exp) {
                if (events.Contains(ev.ExperienceID) == false) {
                    continue;
                }

                if (entries.TryGetValue(ev.SourceID, out OutfitExpEntry? entry) == false) {
                    PsCharacter? character = await _CharacterRepository.GetByID(ev.SourceID);
                    entry = new OutfitExpEntry() {
                        CharacterID = ev.SourceID,
                        CharacterName = character?.Name ?? $"Missing {ev.SourceID}"
                    };
                }

                ++entry.Amount;

                entries[ev.SourceID] = entry;
            }

            List<OutfitExpEntry> list = entries.Values
                .OrderByDescending(iter => iter.Amount)
                .ToList();

            return list;
        }

    }
}
