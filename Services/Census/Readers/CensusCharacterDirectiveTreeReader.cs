using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using watchtower.Code.ExtensionMethods;
using watchtower.Models.Census;

namespace watchtower.Services.Census.Readers {

    public class CensusCharacterDirectiveTreeReader : ICensusReader<CharacterDirectiveTree> {

        public override CharacterDirectiveTree? ReadEntry(JToken token) {
            CharacterDirectiveTree dir = new CharacterDirectiveTree();

            dir.CharacterID = token.GetRequiredString("character_id");
            dir.TreeID = token.GetInt32("directive_tree_id", 0);
            dir.CurrentTier = token.GetInt32("current_directive_tier_id", 0);
            dir.CurrentLevel = token.GetInt32("current_level", 0);
            dir.CompletionDate = token.GetInt32("completion_time", 0) == 0 ? null : token.CensusTimestamp("completion_time");

            return dir;
        }

    }
}
