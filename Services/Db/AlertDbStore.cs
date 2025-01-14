﻿using Microsoft.Extensions.Logging;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using watchtower.Code.ExtensionMethods;
using watchtower.Models.Alert;
using watchtower.Models.Census;

namespace watchtower.Services.Db {

    /// <summary>
    ///     Service to interact with the alerts table
    /// </summary>
    public class AlertDbStore {

        private readonly ILogger<AlertDbStore> _Logger;
        private readonly IDbHelper _DbHelper;

        private readonly IDataReader<PsAlert> _Reader;
        private readonly IDataReader<AlertPlayer> _ParticipantReader;

        public AlertDbStore(ILogger<AlertDbStore> logger,
            IDbHelper dbHelper, IDataReader<PsAlert> reader,
            IDataReader<AlertPlayer> participantReader) {

            _Logger = logger;
            _DbHelper = dbHelper;
            _Reader = reader;
            _ParticipantReader = participantReader;
        }

        /// <summary>
        ///     Get all alerts
        /// </summary>
        /// <returns>A list of all alerts</returns>
        public async Task<List<PsAlert>> GetAll() {
            using NpgsqlConnection conn = _DbHelper.Connection();
            using NpgsqlCommand cmd = await _DbHelper.Command(conn, @"
                SELECT *
                    FROM alerts;
            ");

            List<PsAlert> alert = await _Reader.ReadList(cmd);
            await conn.CloseAsync();

            return alert;
        }

        /// <summary>
        ///     Get a single <see cref="PsAlert"/> by ID
        /// </summary>
        /// <param name="ID">ID of the alert</param>
        /// <returns>The <see cref="PsAlert"/> with ID of <paramref name="ID"/>, or null if it doesn't exist</returns>
        public async Task<PsAlert?> GetByID(long ID) {
            using NpgsqlConnection conn = _DbHelper.Connection();
            using NpgsqlCommand cmd = await _DbHelper.Command(conn, @"
                SELECT *
                    FROM alerts
                    WHERE id = @ID;
            ");

            cmd.AddParameter("ID", ID);

            PsAlert? alert = await _Reader.ReadSingle(cmd);
            await conn.CloseAsync();

            return alert;
        }

        /// <summary>
        ///     Get an alert by it's instance ID, which is unique per world
        /// </summary>
        /// <param name="instanceID">Instance ID</param>
        /// <param name="worldID">World ID</param>
        /// <returns>
        ///     The <see cref="PsAlert"/> with <see cref="PsAlert.InstanceID"/> of <paramref name="instanceID"/>,
        ///     and <see cref="PsAlert.WorldID"/> of <paramref name="instanceID"/>, 
        ///     or <c>null</c> if it does not exist
        /// </returns>
        public async Task<PsAlert?> GetByInstanceID(int instanceID, short worldID) {
            using NpgsqlConnection conn = _DbHelper.Connection();
            using NpgsqlCommand cmd = await _DbHelper.Command(conn, @"
                SELECT *
                    FROM alerts
                    WHERE world_id = @WorldID
                        AND instance_id = @InstanceID;
            ");

            cmd.AddParameter("InstanceID", instanceID);
            cmd.AddParameter("WorldID", worldID);

            PsAlert? alert = await _Reader.ReadSingle(cmd);
            await conn.CloseAsync();

            return alert;
        }

        /// <summary>
        ///     Get the alerts that haven't been finished
        /// </summary>
        /// <returns></returns>
        public async Task<List<PsAlert>> LoadUnfinished() {
            using NpgsqlConnection conn = _DbHelper.Connection();
            using NpgsqlCommand cmd = await _DbHelper.Command(conn, @"
                SELECT *
                    FROM alerts
                    --WHERE NOW() AT TIME ZONE 'utc' > (timestamp + (duration || ' seconds')::INTERVAL);
                    WHERE NOW() AT TIME ZONE 'utc' < ((timestamp + (duration || ' seconds')::INTERVAL) AT TIME ZONE 'utc');
            ");

            List<PsAlert> alerts = await _Reader.ReadList(cmd);
            await conn.CloseAsync();

            return alerts;
        }

        /// <summary>
        ///     Get the participants of an alert, and how many seconds they were online for
        /// </summary>
        /// <remarks>
        ///     A participant is anyone who got a kill, death or exp event in the same zone, world,
        ///     and during the duration of the alert. This means the seconds_online may be inaccurate,
        ///     as the session data does not include any zone information.
        ///     
        ///     For example, if someone starts a session, gets 30 kills in zone A, then warps to zone B
        ///     -- where an alert is happening -- and gets 1 kill, the session will have started in zone A,
        ///     but only got one kill in zone B, so their precious KPM is lower than it actually was in the zone
        /// </remarks>
        /// <param name="alert">Alert to get the participants of</param>
        /// <returns>
        ///     A list of all participants, or an empty list if no participants
        /// </returns>
        public async Task<List<AlertPlayer>> GetParticipants(PsAlert alert) {
            using NpgsqlConnection conn = _DbHelper.Connection();
            using NpgsqlCommand cmd = await _DbHelper.Command(conn, @"
                WITH kill_dataset AS (
                    SELECT *
                        FROM wt_kills
                        WHERE (zone_id = @ZoneID OR @ZoneID = 0)
                            AND world_id = @WorldID
                            AND timestamp BETWEEN @AlertStart AND @AlertEnd
                ), exp_dataset AS (
                    SELECT *
                        FROM wt_exp
                        WHERE (zone_id = @ZoneID OR @ZoneID = 0)
                            AND world_id = @WorldID
                            AND timestamp BETWEEN @AlertStart AND @AlertEnd
                ), characters AS (
                    SELECT source_character_id AS character_id
                        FROM exp_dataset
                        GROUP BY source_character_id
                    UNION SELECT attacker_character_id AS character_id
                        FROM kill_dataset
                        GROUP BY attacker_character_id
                    UNION SELECT killed_character_id AS character_id
                        FROM kill_dataset
                        GROUP BY killed_character_id
                )
                SELECT
                    character_id
                FROM 
                    characters c;
            ");
            cmd.CommandTimeout = 300;

            cmd.AddParameter("AlertStart", alert.Timestamp);
            cmd.AddParameter("AlertEnd", alert.Timestamp + TimeSpan.FromSeconds(alert.Duration));
            cmd.AddParameter("ZoneID", alert.ZoneID);
            cmd.AddParameter("WorldID", alert.WorldID);

            List<AlertPlayer> parts = await _ParticipantReader.ReadList(cmd);
            await conn.CloseAsync();

            return parts;
        }

        /// <summary>
        ///     Get the alert participants by alert ID, instead of passing the alert object.
        ///     See remarks of <see cref="GetParticipants(PsAlert)"/> for what a participant is
        /// </summary>
        /// <param name="alertID">ID of the alert</param>
        /// <returns>
        ///     The participants of an alert (character ID + seconds online),
        ///     or an empty list if the alert does not exist
        /// </returns>
        public async Task<List<AlertPlayer>> GetParticipants(int alertID) {
            PsAlert? alert = await GetByID(alertID);

            if (alert == null) {
                return new List<AlertPlayer>();
            }

            return await GetParticipants(alert);
        }

        /// <summary>
        ///     Insert a new alert using the parameter passed in from <paramref name="alert"/>
        /// </summary>
        /// <param name="alert">Parameters used to insert</param>
        /// <returns>ID of the alert that was just inserted</returns>
        public async Task<long> Insert(PsAlert alert) {
            using NpgsqlConnection conn = _DbHelper.Connection();
            using NpgsqlCommand cmd = await _DbHelper.Command(conn, @"
                INSERT INTO alerts (
                    timestamp, duration, zone_id, world_id, alert_id, victor_faction_id, warpgate_vs, warpgate_nc, warpgate_tr, zone_facility_count, count_vs, count_nc, count_tr, instance_id, name
                ) VALUES (
                    @Timestamp, @Duration, @ZoneID, @WorldID, @AlertID, null, @WarpgateVS, @WarpgateNC, @WarpgateTR , @ZoneFacilityCount, null, null, null, @InstanceID, @Name
                ) RETURNING id;
            ");

            cmd.AddParameter("Timestamp", alert.Timestamp);
            cmd.AddParameter("Duration", alert.Duration);
            cmd.AddParameter("ZoneID", alert.ZoneID);
            cmd.AddParameter("WorldID", alert.WorldID);
            cmd.AddParameter("AlertID", alert.AlertID);
            cmd.AddParameter("WarpgateVS", alert.WarpgateVS);
            cmd.AddParameter("WarpgateNC", alert.WarpgateNC);
            cmd.AddParameter("WarpgateTR", alert.WarpgateTR);
            cmd.AddParameter("ZoneFacilityCount", alert.ZoneFacilityCount);
            cmd.AddParameter("InstanceID", alert.InstanceID);
            cmd.AddParameter("Name", alert.Name);

            long ID = await cmd.ExecuteInt64(CancellationToken.None);

            return ID;
        }

        /// <summary>
        ///     Update an alert, only the following parameters are updated:
        ///     <ul>
        ///         <li><see cref="PsAlert.VictorFactionID"/></li>
        ///         <li><see cref="PsAlert.CountVS"/></li>
        ///         <li><see cref="PsAlert.CountNC"/></li>
        ///         <li><see cref="PsAlert.CountTR"/></li>
        ///         <li><see cref="PsAlert.Participants"/></li>
        ///         <li><see cref="PsAlert.Name"/></li>
        ///         <li><see cref="PsAlert.Duration"/></li>
        ///     </ul>
        /// </summary>
        /// <param name="ID">ID of the alert to update</param>
        /// <param name="parameters">Parameters used to update</param>
        public async Task UpdateByID(long ID, PsAlert parameters) {
            using NpgsqlConnection conn = _DbHelper.Connection();
            using NpgsqlCommand cmd = await _DbHelper.Command(conn, @"
                UPDATE alerts
                    SET victor_faction_id = @VictorFactionID,
                        count_vs = @CountVS,
                        count_nc = @CountNC,
                        count_tr = @CountTR,
                        participants = @Players,
                        name = @Name,
                        duration = @Duration
                    WHERE id = @ID;
            ");

            cmd.AddParameter("ID", ID);
            cmd.AddParameter("VictorFactionID", parameters.VictorFactionID);
            cmd.AddParameter("CountVS", parameters.CountVS);
            cmd.AddParameter("CountNC", parameters.CountNC);
            cmd.AddParameter("CountTR", parameters.CountTR);
            cmd.AddParameter("Players", parameters.Participants);
            cmd.AddParameter("Name", parameters.Name);
            cmd.AddParameter("Duration", parameters.Duration);

            await cmd.ExecuteNonQueryAsync();
            await conn.CloseAsync();
        }

    }
}
