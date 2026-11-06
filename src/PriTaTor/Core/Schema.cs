// What each "*key" in the settings files means. Keys and meanings come from the old PriTaTor source
// (Monster.vb, Item.vb, NPChar.vb), plus the keys found in real server files. Keys not listed here
// still show up and can be edited: they just have no English label.

using System;
using System.Collections.Generic;
using System.Linq;

namespace PriTaTor.Core
{
    public sealed class SchemaField
    {
        public string Key, Group, Label, Help;
    }

    public sealed class Schema
    {
        public string Name;
        public List<SchemaField> Fields = new List<SchemaField>();
        Dictionary<string, SchemaField> byKey;
        Dictionary<string, int> order;

        public SchemaField Lookup(string key)
        {
            if (byKey == null) Index();
            if (byKey.TryGetValue(key, out SchemaField f)) return f;
            if (this == Zhoon)
            {
                // the letter is the language, and it varies between servers
                string k = key.ToUpperInvariant();
                if (k.EndsWith("_NAME", StringComparison.Ordinal)) byKey.TryGetValue("*E_NAME", out f);
                else if (k.EndsWith("_CHAT", StringComparison.Ordinal)) byKey.TryGetValue("*E_CHAT", out f);
            }
            return f;
        }

        public int OrderOf(string key)
        {
            if (byKey == null) Index();
            if (order.TryGetValue(key, out int i)) return i;
            SchemaField f = Lookup(key);
            return f != null && order.TryGetValue(f.Key, out i) ? i : int.MaxValue;
        }

        void Index()
        {
            byKey = new Dictionary<string, SchemaField>(StringComparer.OrdinalIgnoreCase);
            order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < Fields.Count; i++)
            {
                if (!byKey.ContainsKey(Fields[i].Key)) byKey[Fields[i].Key] = Fields[i];
                if (!order.ContainsKey(Fields[i].Key)) order[Fields[i].Key] = i;
            }
        }

        static Schema Make(string name, string table)
        {
            var s = new Schema { Name = name };
            string group = "";
            foreach (string raw in table.Split('\n'))
            {
                string l = raw.Trim();
                if (l.Length == 0) continue;
                if (l[0] == '#') { group = l.Substring(1).Trim(); continue; }
                string[] p = l.Split('|');
                s.Fields.Add(new SchemaField { Key = p[0].Trim(), Group = group, Label = p[1].Trim(), Help = p.Length > 2 ? p[2].Trim() : "" });
            }
            return s;
        }

        public static readonly Schema Monster = Make("Monster", @"
# Identity
*이름|Name (Korean)|Name used by .spm spawn lists. Quoted text.
*Name|Name (English)|Quoted text. Display name only.
*속성|Type|적 = enemy
*모양파일|Model file|Quoted path to the model .INI
*예비모델|Spare model|Alternate model used for events
*모델크기|Model scale|e.g. 0.7
*크기|Shadow size|소형 small, 중형 medium, 중대형 medium-large, 대형 large, 없음 none
*화면보정|Camera offset|Two integers
*효과음|Sound code|Sound set name, no quotes
*소리|Sound code (alt)|Sound set name, no quotes
*두목|Boss|Marks the monster as a boss
*보스|Boss (alt)|Marks the monster as a boss
*계급|Rank|Integer
*구별코드|Class code|Integer
*몬스터종족|Species|e.g. 언데드 undead, 뮤턴트 mutant, 메카닉 mechanic, 디먼 demon
*언데드|Undead|유 / 있음 = undead
*레벨|Level|Integer
*경험치|Experience|Integer
# Behaviour
*활동시간|Active hours|제한없음 any time, 낮 day, 밤 night
*조직|Group size|Min and max number spawned together
*지능|Intelligence|1 to 10
*품성|Nature|Good, Neutral or Evil
*시야|Sight range|1M = 27 units
*이동타입|Move type|Integer
*이동범위|Move range|Integer
*이동력|Move power|Decimal
*이동속도|Move speed|Integer
*넉백금지|No knockback|Monster can't be knocked back
# Combat
*생명력|Life|HP
*라이프|Life (alt)|HP
*공격력|Attack damage|Min and max
*흡수율|Absorption|Percent of damage absorbed
*블럭율|Block chance|Percent
*방어력|Defence|Integer
*공격속도|Attack speed|5 to 9
*명중력|Attack rating|Integer
*특수공격률|Special attack rate|Percent
*공격범위|Attack range|Integer
*스턴율|Stun chance|Percent
*스턴률|Stun chance (alt)|Percent
# Skills
*기술공격력|Skill damage|Min and max
*기술공격률|Skill rate|Percent
*기술공격거리|Skill distance|Integer
*기술공격범위|Skill area|Integer
*저주기술|Curse skill|Integer
*매직|Magic|Integer
# Resistances
*생체|Organic|Integer
*번개|Lightning|Integer
*얼음|Ice|Integer
*불|Fire|Integer
*독|Poison|Integer
*물|Water|Integer
*바람|Wind|Integer
*지동력|Earth|Integer
# Drops
*아이템카운터|Max item drops|Integer
*아이템|Item drop|First number is the chance, then item codes. 돈 min max = gold, 없음 = nothing. Can repeat.
*추가아이템|Bonus item drop|Chance, then item code
*아이템모두|Drops visible to all|Everyone can see the drops
*골드|Extra gold|Integer
*물약보유수|Potions carried|Integer
*물약보유률|Potion chance|Percent
*이벤트아이템|Event drop|Item code
*이벤트코드|Event code|Integer
*이벤트정보|Event info|Integer
*대화|Dialogue|Quoted text. Can repeat.
# Link
*연결파일|Linked name file|Quoted path to the .zhoon holding the English name
");

        public static readonly Schema Item = Make("Item", @"
# Identity
*이름|Name (Korean)|Quoted text
*Name|Name (English)|Quoted text
*NAME|Name (English, alt)|Quoted text
*코드|Item code|Quoted code, e.g. WA108
*유니크|Unique|Integer
*유니크색상|Unique colour|Red, green, blue, alpha, blink
*이펙트설정|Display effect|Integer
*세트아이템|Set item|Integer
*최대수량|Max stack|Integer
*발생제한|Spawn day limit|Integer
# General
*내구력|Durability|Min and max
*무게|Weight|Integer
*가격|Price|Integer
# Elements
*생체|Organic|Min and max
*대자연|Earth|Min and max
*불|Fire|Min and max
*냉기|Ice|Min and max
*번개|Lightning|Min and max
*독|Poison|Min and max
*물|Water|Min and max
*바람|Wind|Min and max
# Attack
*공격력|Damage|Four values: min low, min high, max low, max high
*사정거리|Range|Integer
*공격속도|Attack speed|Integer
*명중력|Attack rating|Min and max
*크리티컬|Critical|Integer
# Defence
*흡수력|Absorption|Min and max (decimal)
*방어력|Defence|Min and max
*블럭율|Block|Min and max (decimal)
# Movement
*이동속도|Move speed|Min and max (decimal)
# Storage
*보유공간|Potion space|Integer
# Abilities
*생명력재생|Life regen|Min and max (decimal)
*라이프재생|Life regen (alt)|Min and max (decimal)
*기력재생|Mana regen|Min and max (decimal)
*마나재생|Mana regen (alt)|Min and max (decimal)
*근력재생|Stamina regen|Min and max (decimal)
*스테미나재생|Stamina regen (alt)|Min and max (decimal)
*생명력추가|Add life|Min and max
*라이프추가|Add life (alt)|Min and max
*기력추가|Add mana|Min and max
*마나추가|Add mana (alt)|Min and max
*근력추가|Add stamina|Min and max
*스테미나추가|Add stamina (alt)|Min and max
*마법숙련도|Magic mastery|Decimal
*마법기술숙련도|Magic skill mastery|Decimal
*기술숙련도|Skill mastery|Decimal
# Requirements
*레벨|Level|Integer
*힘|Strength|Integer
*정신력|Spirit|Integer
*재능|Talent|Integer
*민첩성|Agility|Integer
*건강|Health|Integer
# Recovery
*생명력상승|Life recovery|Min and max
*라이프상승|Life recovery (alt)|Min and max
*기력상승|Mana recovery|Min and max
*마나상승|Mana recovery (alt)|Min and max
*근력상승|Stamina recovery|Min and max
*스테미너상승|Stamina recovery (alt)|Min and max
# Class specialisation
**특화랜덤|Random spec class|Class names
**특화|Primary spec class|Class name
**공격력|Spec: damage per level|Two integers
**공격속도|Spec: attack speed|Integer
**명중력|Spec: attack rating per level|Two integers
**크리티컬|Spec: critical|Integer
**사정거리|Spec: range|Integer
**흡수력|Spec: absorption|Min and max (decimal)
**방어력|Spec: defence|Min and max
**블럭율|Spec: block|Decimal
**이동속도|Spec: move speed|Min and max (decimal)
**생명력재생|Spec: life regen|Decimal
**라이프재생|Spec: life regen (alt)|Decimal
**기력재생|Spec: mana regen|Min and max (decimal)
**마나재생|Spec: mana regen (alt)|Min and max (decimal)
**근력재생|Spec: stamina regen|Decimal
**스테미나재생|Spec: stamina regen (alt)|Decimal
**마법숙련도|Spec: magic mastery|Min and max (decimal)
**기력추가|Spec: add mana|Integer
**마나추가|Spec: add mana (alt)|Integer
**생명력추가|Spec: add life|Integer
**라이프추가|Spec: add life (alt)|Integer
# Link
*연결파일|Linked name file|Quoted path to the .zhoon holding the English name
");

        public static readonly Schema Npc = Make("NPC", @"
# Identity
*속성|Type|NPC, or 적 = hostile
*모양파일|Model file|Quoted path to the model .INI
*이름|Name (Korean)|Quoted text
*Name|Name (English)|Quoted text
*레벨|Level|Integer
*계급|Rank|Integer
*모델크기|Model scale|Decimal
*크기|Shadow size|소형, 중형, 중대형, 대형, 없음
*효과음|Sound code|Sound set name
*대화|Dialogue|Quoted text. Can repeat.
# Shop
*무기판매|Sells weapons|Item codes, 없음 = nothing. Can repeat.
*방어구판매|Sells armour|Item codes, 없음 = nothing. Can repeat.
*잡화판매|Sells general goods|Item codes, 없음 = nothing. Can repeat.
# Services
*스킬수련|Skill master|Switch
*직업전환|Class change|Switch or integer
*아이템보관|Warehouse|Switch
*아이템조합|Item mixing|Switch
*아이템제련|Smelting|Switch
*아이템제작|Manufacturing|Switch
*아이템연금|Alchemy|Switch
*아이템에이징|Aging|Switch
*믹스처리셋|Mixture reset|Switch
*클랜기능|Clan functions|Switch
*텔레포트|Teleport|Integer, default 1
*블레스캐슬|Bless Castle|Integer, default 1
*벨라트라|Bellatra|Integer
*크리스탈강화|Crystal upgrade|Integer
*각인|Engraving|Integer
# Events
*이벤트매표소|Event ticket booth|Integer, default 1
*이벤트코드|Event code|Integer
*이벤트정보|Event info|Integer
*경품추첨|Prize draw|Switch
*경품배달|Prize delivery|Switch
*꽝이지롱|Wow event|Switch
*모금함|Collection box|Switch
*기부함|Donation box|Switch
*윙퀘스트|Wing quest|Integer, default 1
*퀘스트이벤트|Quest event|Integer, default 2
*퀘스트코드|Quest code|Quest code, then parameter
*별포인트적립|Star points|Integer, default 20
*설문조사|Survey|Integer, default 1
*동영상제목|Video title|Quoted text
*동영상경로|Video path|Quoted text
*출현간격|Appearance interval|Find word, exit number
# Link
*연결파일|Linked name file|Quoted path to the .zhoon holding the English name
");

        public static readonly Schema Zhoon = Make("Zhoon", @"
# Text
*E_NAME|Name|Quoted text. The letter before _NAME is the language.
*E_CHAT|Dialogue|Quoted text. Can repeat.
");
    }
}
