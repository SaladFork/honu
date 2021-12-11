﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using watchtower.Models.Census;

namespace watchtower.Models.Queues {

    /// <summary>
    ///     Represents info about performing a character update
    /// </summary>
    public class CharacterUpdateQueueEntry {

        /// <summary>
        ///     ID of the character to perform the update on
        /// </summary>
        public string CharacterID { get; set; } = "";

        /// <summary>
        ///     Character from census. Useful if Honu already got the character (say as part of a logout process),
        ///     and it saves a Census call
        /// </summary>
        public PsCharacter? CensusCharacter { get; set; }

    }
}
