﻿using DSharpPlus.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using watchtower.Code.Constants;
using watchtower.Models.Census;
using watchtower.Models.Db;
using watchtower.Models.Discord;
using watchtower.Models.Queues;
using watchtower.Services.Db;
using watchtower.Services.Queues;
using watchtower.Services.Repositories;

namespace watchtower.Services.Hosted {

    public class SessionEndQueueProcessService : BackgroundService {

        private readonly ILogger<SessionEndQueueProcessService> _Logger;
        private readonly SessionEndQueue _Queue;

        private readonly DiscordMessageQueue _DiscordMessageQueue;
        private readonly SessionRepository _SessionRepository;
        private readonly SessionEndSubscriptionDbStore _SessionSubscriptionDb;
        private readonly CharacterRepository _CharacterRepository;

        public SessionEndQueueProcessService(ILogger<SessionEndQueueProcessService> logger, SessionEndQueue queue,
            SessionRepository sessionRepository, SessionEndSubscriptionDbStore sessionSubscriptionDb,
            CharacterRepository characterRepository, DiscordMessageQueue discordMessageQueue) {

            _Logger = logger;
            _Queue = queue;

            _SessionRepository = sessionRepository;
            _SessionSubscriptionDb = sessionSubscriptionDb;
            _CharacterRepository = characterRepository;
            _DiscordMessageQueue = discordMessageQueue;
        }

        protected async override Task ExecuteAsync(CancellationToken cancel) {
            Stopwatch timer = Stopwatch.StartNew();
            while (cancel.IsCancellationRequested == false) { 
                SessionEndQueueEntry entry = await _Queue.Dequeue(cancel);
                try {
                    timer.Restart();
                    await _SessionRepository.End(entry.CharacterID, entry.Timestamp);

                    if (entry.SessionID == null) {
                        _Queue.AddProcessTime(timer.ElapsedMilliseconds);
                        continue;
                    }

                    List<SessionEndSubscription> subs = await _SessionSubscriptionDb.GetByCharacterID(entry.CharacterID);
                    if (subs.Count == 0) {
                        _Queue.AddProcessTime(timer.ElapsedMilliseconds);
                        continue;
                    }

                    _Logger.LogDebug($"Character ID {entry.CharacterID} has {subs.Count} subscriptions for session end");

                    Session? session = await _SessionRepository.GetByID(entry.SessionID.Value);
                    if (session == null) {
                        _Logger.LogError($"failed to find session ID {entry.SessionID}, expected to exist!");
                        _Queue.AddProcessTime(timer.ElapsedMilliseconds);
                        continue;
                    }

                    /*
                    if ((session.End ?? DateTime.UtcNow) - session.Start <= TimeSpan.FromMinutes(10)) {
                        _Logger.LogDebug($"session {session.ID} is too short, not sending to subscriptions");
                        continue;
                    }
                    */

                    PsCharacter? c = await _CharacterRepository.GetByID(entry.CharacterID, CensusEnvironment.PC, fast: true);

                    HonuDiscordMessage msg = new();
                    DiscordEmbedBuilder builder = new();
                    builder.Title = $"Session end: {c?.GetDisplayName() ?? $"missing {entry.CharacterID}"}";
                    builder.Description = $"https://wt.honu.pw/s/{entry.SessionID}";
                    builder.Url = $"https://wt.honu.pw/s/{entry.SessionID}";
                    builder.Footer = new DiscordEmbedBuilder.EmbedFooter();
                    builder.Footer.Text = $"Session ended at";
                    builder.Timestamp = entry.Timestamp;
                    builder.Color = DiscordColor.Purple;
                    msg.Embeds.Add(builder.Build());

                    foreach (SessionEndSubscription sub in subs) {
                        msg.TargetUserID = sub.DiscordID;
                        _DiscordMessageQueue.Queue(new HonuDiscordMessage(msg)); // clone the object so we can reuse it
                    }
                    _Queue.AddProcessTime(timer.ElapsedMilliseconds);
                } catch (Exception ex) {
                    _Logger.LogError(ex, $"error with session end entry for character {entry.CharacterID} at {entry.Timestamp:u}");
                }
            }

        }

    }
}
