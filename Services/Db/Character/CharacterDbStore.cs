﻿using Microsoft.Extensions.Logging;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using watchtower.Code.ExtensionMethods;
using watchtower.Models.Census;
using watchtower.Services.Queues;

namespace watchtower.Services.Db {

    public class CharacterDbStore : IDataReader<PsCharacter> {

        private readonly ILogger<CharacterDbStore> _Logger;
        private readonly IDbHelper _DbHelper;

        public CharacterDbStore(ILogger<CharacterDbStore> logger,
                IDbHelper helper) {

            _Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _DbHelper = helper ?? throw new ArgumentNullException(nameof(helper));
        }

        /// <summary>
        ///     Get a single character by ID
        /// </summary>
        /// <param name="charID">ID of the character to get</param>
        /// <returns>
        ///     The <see cref="PsCharacter"/> with <see cref="PsCharacter.ID"/> of <paramref name="charID"/>
        /// </returns>
        public async Task<PsCharacter?> GetByID(string charID) {
            using NpgsqlConnection conn = _DbHelper.Connection();
            using NpgsqlCommand cmd = await _DbHelper.Command(conn, @"
                SELECT c.*, o.id AS outfit_id, o.tag AS outfit_tag, o.name AS outfit_name
                    FROM wt_character c
                        LEFT JOIN wt_outfit o ON c.outfit_id = o.id
                    WHERE c.id = @ID
            ");

            cmd.AddParameter("@ID", charID);
            await cmd.PrepareAsync();

            PsCharacter? c = await ReadSingle(cmd);
            await conn.CloseAsync();

            return c;
        }

        /// <summary>
        ///     Get characters base on name (case-insensitive)
        /// </summary>
        /// <param name="name">Name of the character to get</param>
        /// <returns>
        ///     A list of <see cref="PsCharacter"/> with <see cref="PsCharacter.Name"/>
        ///     of <paramref name="name"/>, ignoring case
        /// </returns>
        public async Task<List<PsCharacter>> GetByName(string name) {
            using NpgsqlConnection conn = _DbHelper.Connection();
            using NpgsqlCommand cmd = await _DbHelper.Command(conn, @"
                SELECT c.*, o.id AS outfit_id, o.tag AS outfit_tag, o.name AS outfit_name
                    FROM wt_character c
                        LEFT JOIN wt_outfit o ON c.outfit_id = o.id
                    WHERE c.name_lower = @Name
            ");

            cmd.AddParameter("Name", name.ToLower());
            await cmd.PrepareAsync();

            List<PsCharacter> c = await ReadList(cmd);
            await conn.CloseAsync();

            return c;
        }

        /// <summary>
        ///     Get a list of characters from DB by a list of IDs
        /// </summary>
        /// <param name="IDs">List of character IDs to get</param>
        /// <returns>
        ///     A list of <see cref="PsCharacter"/> that have a <see cref="PsCharacter.ID"/>
        ///     as an element of <paramref name="IDs"/>
        /// </returns>
        public async Task<List<PsCharacter>> GetByIDs(List<string> IDs) {
            using NpgsqlConnection conn = _DbHelper.Connection();
            using NpgsqlCommand cmd = await _DbHelper.Command(conn, @"
                SELECT c.*, o.id AS outfit_id, o.tag AS outfit_tag, o.name AS outfit_name
                    FROM wt_character c
                        LEFT JOIN wt_outfit o ON c.outfit_id = o.id
                    WHERE c.id = ANY(@IDs)
            ");

            cmd.AddParameter("IDs", IDs);
            await cmd.PrepareAsync();

            List<PsCharacter> c = await ReadList(cmd);
            await conn.CloseAsync();

            return c;
        }

        /// <summary>
        ///     Update/Insert a <see cref="PsCharacter"/>
        /// </summary>
        /// <remarks>
        ///     A character is updated if the insert fails due to a duplicate key
        /// </remarks>
        /// <param name="character">Parameters used to insert/update</param>
        public async Task Upsert(PsCharacter character) {
            using NpgsqlConnection conn = _DbHelper.Connection();
            using NpgsqlCommand cmd = await _DbHelper.Command(conn, @"
                INSERT INTO wt_character (
                    id, name, world_id, faction_id, outfit_id, battle_rank, prestige, last_updated_on, time_create, time_last_login, time_last_save
                ) VALUES (
                    @ID, @Name, @WorldID, @FactionID, @OutfitID, @BattleRank, @Prestige, @LastUpdatedOn, @DateCreated, @DateLastLogin, @DateLastSave
                ) ON CONFLICT (id) DO
                    UPDATE SET name = @Name,
                        world_id = @WorldID,
                        faction_id = @FactionID,
                        outfit_id = @OutfitID,
                        battle_rank = @BattleRank,
                        prestige = @Prestige,
                        last_updated_on = @LastUpdatedOn,
                        time_create = @DateCreated,
                        time_last_login = @DateLastLogin,
                        time_last_save = @DateLastSave
            ");

            cmd.AddParameter("ID", character.ID);
            cmd.AddParameter("Name", character.Name);
            cmd.AddParameter("WorldID", character.WorldID);
            cmd.AddParameter("FactionID", character.FactionID);
            cmd.AddParameter("OutfitID", character.OutfitID);
            cmd.AddParameter("BattleRank", character.BattleRank);
            cmd.AddParameter("Prestige", character.Prestige);
            cmd.AddParameter("LastUpdatedOn", DateTime.UtcNow);
            cmd.AddParameter("DateCreated", character.DateCreated);
            cmd.AddParameter("DateLastLogin", character.DateLastLogin);
            cmd.AddParameter("DateLastSave", character.DateLastSave);
            await cmd.PrepareAsync();

            await cmd.ExecuteNonQueryAsync();
            await conn.CloseAsync();
        }

        /// <summary>
        ///     Search for characters by name (case-insensitive)
        /// </summary>
        /// <param name="name">Name to search for</param>
        /// <returns>
        ///     A list of all <see cref="PsCharacter"/> where <paramref name="name"/>
        ///     is contained in <see cref="PsCharacter.Name"/>
        /// </returns>
        public async Task<List<PsCharacter>> SearchByName(string name) {
            using NpgsqlConnection conn = _DbHelper.Connection();
            using NpgsqlCommand cmd = await _DbHelper.Command(conn, @"
                SELECT c.*, o.id AS outfit_id, o.tag AS outfit_tag, o.name AS outfit_name
                    FROM wt_character c
                        LEFT JOIN wt_outfit o ON c.outfit_id = o.id
                    WHERE c.name_lower LIKE @Name
            ");

            cmd.AddParameter("Name", $"%{name.ToLower()}%");
            await cmd.PrepareAsync();

            List<PsCharacter> c = await ReadList(cmd);
            await conn.CloseAsync();

            return c;
        }

        /// <summary>
        ///     Internal method
        /// </summary>
        /// <returns></returns>
        public async Task<List<PsCharacter>> GetMissingDates() {
            using NpgsqlConnection conn = _DbHelper.Connection();
            using NpgsqlCommand cmd = await _DbHelper.Command(conn, @"
                SELECT c.*, o.id AS outfit_id, o.tag AS outfit_tag, o.name AS outfit_name
                    FROM wt_character c
                        LEFT JOIN wt_outfit o ON c.outfit_id = o.id
                    WHERE c.time_create IS NULL;
            ");
            await cmd.PrepareAsync();

            List<PsCharacter> c = await ReadList(cmd);
            await conn.CloseAsync();

            return c;
        }

        public override PsCharacter ReadEntry(NpgsqlDataReader reader) {
            PsCharacter c = new PsCharacter();

            // Why keep the reading separate? So if there is an error reading a column,
            // the exception contains the line, which contains the bad column
            c.ID = reader.GetString("id");
            c.Name = reader.GetString("name");
            c.FactionID = reader.GetInt16("faction_id");
            c.WorldID = reader.GetInt16("world_id");
            c.LastUpdated = reader.GetDateTime("last_updated_on");
            c.BattleRank = reader.GetInt16("battle_rank");
            c.Prestige = reader.GetInt32("prestige");

            c.OutfitID = reader.GetNullableString("outfit_id");
            c.OutfitTag = reader.GetNullableString("outfit_tag");
            c.OutfitName = reader.GetNullableString("outfit_name");

            c.DateCreated = reader.GetNullableDateTime("time_create") ?? DateTime.MinValue;
            c.DateLastLogin = reader.GetNullableDateTime("time_last_login") ?? DateTime.MinValue;
            c.DateLastSave = reader.GetNullableDateTime("time_last_save") ?? DateTime.MinValue;

            return c;
        }

    }
}
