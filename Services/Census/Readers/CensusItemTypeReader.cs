﻿using Newtonsoft.Json.Linq;
using watchtower.Code.ExtensionMethods;
using watchtower.Models.Census;

namespace watchtower.Services.Census.Readers {

    public class CensusItemTypeReader : ICensusReader<ItemType> {

        public override ItemType? ReadEntry(JToken token) {
            ItemType type = new ItemType();

            type.ID = token.GetRequiredInt32("item_type_id");
            type.Name = token.GetString("name", "");
            type.Code = token.GetString("code", "");

            return type;
        }

    }
}
