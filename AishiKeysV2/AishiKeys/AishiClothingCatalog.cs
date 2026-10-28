using System.Collections.Generic;

namespace AishiKeys
{
    internal static class AishiClothingCatalog
    {
        internal sealed class Entry
        {
            internal string Family, Variant;
            internal int Rank;
            internal Entry(string family, string variant, int rank)
            {
                Family = family;
                Variant = variant;
                Rank = rank;
            }
        }

        internal static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>
        {
            { "21b55df915be597b99acd51a", new Entry("Chaos Insurgency", "", 0) },
            { "f813f6bc65bf5c5ea612b8d0", new Entry("Chaos Insurgency", "", 1) },
            { "b7a3b7cf159c5177b150252e", new Entry("Chaos Insurgency", "", 2) },
            { "4cc7a49002665c968df6318d", new Entry("Chaos Insurgency", "", 3) },
            { "ce113c13c44f5c79a0f0a931", new Entry("Chaos Insurgency", "", 4) },
            { "91ce6b673c555078a491ecce", new Entry("Chaos Insurgency", "", 5) },
            {
                "69b625e0f5004560bb2d14f0",
                new Entry("Black Division Squad Leader", "", 0)
            },
            {
                "18c4b9a4f8165cda8e77ba93",
                new Entry("Black Division Squad Leader", "", 1)
            },
            {
                "e1640a0606a954d3bb7a2cf2",
                new Entry("Black Division Squad Leader", "", 2)
            },
            {
                "edfeb1973417508e9ce01467",
                new Entry("Black Division Squad Leader", "", 3)
            },
            {
                "69b62408bd67015fb9280147",
                new Entry("Black Division Squad Leader", "", 4)
            },
            {
                "d2d888b48d6e51539c94f3e7",
                new Entry("Black Division Squad Leader", "", 5)
            },
            {
                "e8d13de25c3f5acbbd441735",
                new Entry("Black Division Squad Leader", "", 6)
            },
            {
                "4765c3540f655819af282222",
                new Entry("Black Division Squad Leader", "", 7)
            },
            {
                "7915971e72b651df8da6e957",
                new Entry("Black Division Squad Leader", "", 8)
            },
            {
                "4be3434fbf415498ab43da6b",
                new Entry("Black Division Squad Leader", "", 9)
            },
            {
                "2fa4643d869c5fac985a7ebd",
                new Entry("Black Division Squad Leader", "", 10)
            },
            {
                "9f1efad5a530557abd6ef845",
                new Entry("Black Division Squad Leader", "", 11)
            },
            {
                "1fad331aa3e9507f88f83fc6",
                new Entry("Black Division Squad Leader", "", 12)
            },
            {
                "ddba5186516f5076af36b694",
                new Entry("Black Division Squad Leader", "", 13)
            },
            {
                "b1438721218f5aa6b73bc718",
                new Entry("Black Division Squad Leader", "", 14)
            },
            {
                "13fed14b7d1f520fad6b89e5",
                new Entry("Black Division Squad Leader", "", 15)
            },
            {
                "d36264834c145d70b1ab3ea2",
                new Entry("Black Division Squad Leader", "", 16)
            },
            {
                "4c0d60c4569450ad9ee28c5b",
                new Entry("Black Division Squad Leader", "", 17)
            },
            {
                "f38646e9c9e85a888f4e762b",
                new Entry("Black Division Squad Leader", "", 18)
            },
            {
                "a18ff77409a059ada201c6ab",
                new Entry("Black Division Squad Leader", "", 19)
            },
            {
                "4724b37dab3c5722bdc14e06",
                new Entry("Black Division Squad Leader", "", 20)
            },
            {
                "69b626600d9e0b27bda01ce7",
                new Entry("Black Division Specialist Operative", "", 0)
            },
            {
                "45184e9b81fb5355bf9f7304",
                new Entry("Black Division Specialist Operative", "", 1)
            },
            {
                "c1f5990bef995f109d5ba422",
                new Entry("Black Division Specialist Operative", "", 2)
            },
            {
                "06c28b69c43b59299ca5356f",
                new Entry("Black Division Specialist Operative", "", 3)
            },
            {
                "69b6241abd88cfbaaf3c0e2c",
                new Entry("Black Division Specialist Operative", "", 4)
            },
            {
                "db8ae2e15cfb52a1ba56adaa",
                new Entry("Black Division Specialist Operative", "", 5)
            },
            {
                "06a4a3abcd4a5c8fbef39d53",
                new Entry("Black Division Specialist Operative", "", 6)
            },
            {
                "e5d0d3f709fe584790411f1b",
                new Entry("Black Division Specialist Operative", "", 7)
            },
            {
                "12042dadca2456d78f746db7",
                new Entry("Black Division Specialist Operative", "", 8)
            },
            {
                "b69e5670e71a5321adc62f2b",
                new Entry("Black Division Specialist Operative", "", 9)
            },
            {
                "0fb68046c72859b2b1705170",
                new Entry("Black Division Specialist Operative", "", 10)
            },
            {
                "c7a8a1bd65545604bde0dd81",
                new Entry("Black Division Specialist Operative", "", 11)
            },
            {
                "38375e56f68d59a281cc345f",
                new Entry("Black Division Specialist Operative", "", 12)
            },
            {
                "4c97ee1f964c5b48be7b1a95",
                new Entry("Black Division Specialist Operative", "", 13)
            },
            {
                "e5a9b98c05945879b4d231eb",
                new Entry("Black Division Specialist Operative", "", 14)
            },
            {
                "b23e5e942c2a53159bda43cc",
                new Entry("Black Division Specialist Operative", "", 15)
            },
            {
                "1872ce84f96d5586b5b10b92",
                new Entry("Black Division Specialist Operative", "", 16)
            },
            {
                "8e6481bb7589544d9513b6c3",
                new Entry("Black Division Specialist Operative", "", 17)
            },
            {
                "07f32b9c2d405bf483f2c639",
                new Entry("Black Division Specialist Operative", "", 18)
            },
            {
                "c90e5c91864b5c078f0cfb81",
                new Entry("Black Division Specialist Operative", "", 19)
            },
            {
                "391fb137d2a95b8db46a395e",
                new Entry("Black Division Specialist Operative", "", 20)
            },
            {
                "69b62667aa93b5fd5d9a0eac",
                new Entry("Black Division Scout Operative", "", 0)
            },
            {
                "0d63f6f1c75b50b18fc497b9",
                new Entry("Black Division Scout Operative", "", 1)
            },
            {
                "f0e33d1732ff5a5098e1d7e7",
                new Entry("Black Division Scout Operative", "", 2)
            },
            {
                "f54216b28d715fad8b3ac7c1",
                new Entry("Black Division Scout Operative", "", 3)
            },
            {
                "69b6246b935fbbf49ea1a1a0",
                new Entry("Black Division Scout Operative", "", 4)
            },
            {
                "1481bfa57efa556fa5ee3e43",
                new Entry("Black Division Scout Operative", "", 5)
            },
            {
                "19c81b3139745fbe87d359a6",
                new Entry("Black Division Scout Operative", "", 6)
            },
            {
                "9bc873b68e4a5ec58612f17a",
                new Entry("Black Division Scout Operative", "", 7)
            },
            {
                "ac2aa88218e65ff590fb196a",
                new Entry("Black Division Scout Operative", "", 8)
            },
            {
                "26271a72e29b535fbc7727b0",
                new Entry("Black Division Scout Operative", "", 9)
            },
            {
                "30ca97e4b0365fb39daac1da",
                new Entry("Black Division Scout Operative", "", 10)
            },
            {
                "63c9f5a72edf5c789df64982",
                new Entry("Black Division Scout Operative", "", 11)
            },
            {
                "f9dbdc5c44ac5cc289da121f",
                new Entry("Black Division Scout Operative", "", 12)
            },
            {
                "8b84eb91cbd155d6a4139d9f",
                new Entry("Black Division Scout Operative", "", 13)
            },
            {
                "4081c6da1fd65c129162be3e",
                new Entry("Black Division Scout Operative", "", 14)
            },
            {
                "5537355472fb57b7b0e3dbf7",
                new Entry("Black Division Scout Operative", "", 15)
            },
            {
                "4ca5ec6ce4115d4a8db0c601",
                new Entry("Black Division Scout Operative", "", 16)
            },
            {
                "b9252908e3a75564ab600335",
                new Entry("Black Division Scout Operative", "", 17)
            },
            {
                "1311f8ea1de552d0a04263b1",
                new Entry("Black Division Scout Operative", "", 18)
            },
            {
                "9c892443a287506285e4431b",
                new Entry("Black Division Scout Operative", "", 19)
            },
            {
                "0a1f493b2ebc5487b7504ff3",
                new Entry("Black Division Scout Operative", "", 20)
            },
            {
                "69b6266d56793144284a6184",
                new Entry("Black Division Heavy Operative", "", 0)
            },
            {
                "8c718dbda41f50c69a017b0b",
                new Entry("Black Division Heavy Operative", "", 1)
            },
            {
                "f1b41f9461485410ad1c2311",
                new Entry("Black Division Heavy Operative", "", 2)
            },
            {
                "1f0f219592eb5d9e9bd6c80a",
                new Entry("Black Division Heavy Operative", "", 3)
            },
            {
                "69b624ad3293aecae666223e",
                new Entry("Black Division Heavy Operative", "", 4)
            },
            {
                "9618dbc8823a5cad8dc7826a",
                new Entry("Black Division Heavy Operative", "", 5)
            },
            {
                "1ff762f3c46c5d3f831e4402",
                new Entry("Black Division Heavy Operative", "", 6)
            },
            {
                "3a9f15dd3a5c594a93cfbfbe",
                new Entry("Black Division Heavy Operative", "", 7)
            },
            {
                "b31304560a93580ab278de47",
                new Entry("Black Division Heavy Operative", "", 8)
            },
            {
                "b5fd5abc1ec9583082af4a38",
                new Entry("Black Division Heavy Operative", "", 9)
            },
            {
                "3a3996a3ebe95a98a636c500",
                new Entry("Black Division Heavy Operative", "", 10)
            },
            {
                "f6e94164f35d5e8d94fa810a",
                new Entry("Black Division Heavy Operative", "", 11)
            },
            {
                "fda688834c835731b1a1e5e1",
                new Entry("Black Division Heavy Operative", "", 12)
            },
            {
                "355832d5b3f5554d937e289f",
                new Entry("Black Division Heavy Operative", "", 13)
            },
            {
                "5d560d5321375a32b4b6c586",
                new Entry("Black Division Heavy Operative", "", 14)
            },
            {
                "aba49c25084a5f5c8ec4a1d5",
                new Entry("Black Division Heavy Operative", "", 15)
            },
            {
                "1668250d7cad554da681f632",
                new Entry("Black Division Heavy Operative", "", 16)
            },
            {
                "49249272e64a544582b9cdb2",
                new Entry("Black Division Heavy Operative", "", 17)
            },
            {
                "e3749cd4b4e554a8b5708e77",
                new Entry("Black Division Heavy Operative", "", 18)
            },
            {
                "d5ae152dee2f5a2b9784d7fe",
                new Entry("Black Division Heavy Operative", "", 19)
            },
            {
                "c8922403d8795fe59f739acf",
                new Entry("Black Division Heavy Operative", "", 20)
            },
            {
                "6a8bb3ba915969c9813f8040",
                new Entry("Black Division Nocturnal", "", 0)
            },
            {
                "2e534212626c5d2eb0392bc4",
                new Entry("Black Division Nocturnal", "", 2)
            },
            {
                "6a8bb47bebd1c4435c940058",
                new Entry("Black Division Nocturnal", "", 3)
            },
            {
                "6e0426e7ce1b54c9896b8ec5",
                new Entry("Black Division Nocturnal", "", 4)
            },
            {
                "0f930ae2a7b654f4800d3ec4",
                new Entry("Black Division Nocturnal", "", 5)
            },
            {
                "b07920a34a515f21b8683ef2",
                new Entry("Black Division Nocturnal", "", 6)
            },
            {
                "97b483b4e2cd518ab8b951da",
                new Entry("Black Division Nocturnal", "", 7)
            },
            {
                "b3ee6fe49f39593a9e40a232",
                new Entry("Black Division Nocturnal", "", 8)
            },
            {
                "9af51766208351588cbdbacb",
                new Entry("Black Division Nocturnal", "", 9)
            },
            {
                "62ccbe7d9c6257e599bedac7",
                new Entry("Black Division Nocturnal", "", 10)
            },
            {
                "f2b6dfc5f7a45ae982c16084",
                new Entry("Black Division Nocturnal", "", 11)
            },
            {
                "afaacb2210ee572f8b533b48",
                new Entry("Black Division Nocturnal", "", 12)
            },
            {
                "3999b6640c7e5bf99d41d3b5",
                new Entry("Black Division Nocturnal", "", 13)
            },
            {
                "4d5837ad35b55e06a573b5c1",
                new Entry("Black Division Nocturnal", "", 14)
            },
            {
                "487d7f751ab6556988120ed9",
                new Entry("Black Division Nocturnal", "", 15)
            },
            {
                "6a8bb5835547b8e7ef6a9323",
                new Entry("Black Division Scorpion", "", 0)
            },
            {
                "05b2230022245f99b063421d",
                new Entry("Black Division Scorpion", "", 1)
            },
            {
                "09a561016b9d5d03bd2f34e0",
                new Entry("Black Division Scorpion", "", 2)
            },
            {
                "6a8bb5ae0fef82242540dccf",
                new Entry("Black Division Scorpion", "", 3)
            },
            {
                "2214bd3904765e5ea9bb011d",
                new Entry("Black Division Scorpion", "", 4)
            },
            {
                "d499aff4c4e752fba1f615cc",
                new Entry("Black Division Scorpion", "", 5)
            },
            {
                "d327895d6be7532a8d0644f7",
                new Entry("Black Division Scorpion", "", 6)
            },
            {
                "8adff4e6abf55877a2fac4e2",
                new Entry("Black Division Scorpion", "", 7)
            },
            {
                "d1ace63805095861b8dc6670",
                new Entry("Black Division Scorpion", "", 8)
            },
            {
                "9d081769a18b5c0cbba3e662",
                new Entry("Black Division Scorpion", "", 9)
            },
            {
                "2a0ab4388bd15fa8a3baae1d",
                new Entry("Black Division Scorpion", "", 10)
            },
            {
                "6af1b6e0eaa75273a5de7197",
                new Entry("Black Division Scorpion", "", 11)
            },
            {
                "4e770efea3e25bcbaa0ac72e",
                new Entry("Black Division Scorpion", "", 12)
            },
            {
                "1efb943bcf9b59ce8ef65ea5",
                new Entry("Black Division Scorpion", "", 13)
            },
            {
                "577d9d6587825f719d69850d",
                new Entry("Black Division Scorpion", "", 14)
            },
            {
                "481f3f862d1b507486167edc",
                new Entry("Black Division Scorpion", "", 15)
            },
            {
                "124c04e708d00826d0244e65",
                new Entry("MTF Epsilon-11 Nine-Tailed Fox", "", 0)
            },
            {
                "b5a2a8bf9963567cb6fc18a5",
                new Entry("MTF Epsilon-11 Nine-Tailed Fox", "", 1)
            },
            {
                "b7c765cf52f1f4af94543a76",
                new Entry("MTF Epsilon-11 Nine-Tailed Fox", "", 2)
            },
            {
                "1516b19890445cd9b7dceac4",
                new Entry("MTF Epsilon-11 Nine-Tailed Fox", "", 3)
            },
            {
                "ac06eba13c3f0cb47f99bbbd",
                new Entry("MTF Alpha-1 Red Right Hand", "", 0)
            },
            {
                "ce4d4ba7317d5f3bad64a199",
                new Entry("MTF Alpha-1 Red Right Hand", "", 1)
            },
            {
                "8e00cb5a8fe19fe03d54acab",
                new Entry("MTF Alpha-1 Red Right Hand", "", 2)
            },
            {
                "4ed8d567efec59a5b63a2907",
                new Entry("MTF Alpha-1 Red Right Hand", "", 3)
            },
            {
                "8dfc951bd3e279190a161fcc",
                new Entry("Black Division Red Right Hand", "", 0)
            },
            {
                "57c0e04f799754e18fec818d",
                new Entry("Black Division Red Right Hand", "", 2)
            },
            {
                "5e7222d20edbd732573f0142",
                new Entry("Black Division Red Right Hand", "", 3)
            },
            {
                "2d2bc180e2f251abaf0e4ae8",
                new Entry("Black Division Red Right Hand", "", 4)
            },
            {
                "c0c3d46d700d575bb183136d",
                new Entry("Black Division Red Right Hand", "", 5)
            },
            {
                "e0084404fd2d596aa0147385",
                new Entry("Black Division Red Right Hand", "", 6)
            },
            {
                "f0f16c5bf4ee51b7b1642930",
                new Entry("Black Division Red Right Hand", "", 7)
            },
            {
                "28c64679b0b65b6cafce7831",
                new Entry("Black Division Red Right Hand", "", 8)
            },
            {
                "0f69f3d066c85090bd96d5a9",
                new Entry("Black Division Red Right Hand", "", 9)
            },
            {
                "4188fba859c051e79d0e37a9",
                new Entry("Black Division Red Right Hand", "", 10)
            },
            {
                "971335eb590450cfbd2997f7",
                new Entry("Black Division Red Right Hand", "", 11)
            },
            {
                "5431de7cd653533db44acc15",
                new Entry("Black Division Red Right Hand", "", 12)
            },
            {
                "d9076286a4c05ba9b8b75377",
                new Entry("Black Division Red Right Hand", "", 13)
            },
            {
                "8ea777e7b10b55168dc5a2e6",
                new Entry("Black Division Red Right Hand", "", 14)
            },
            {
                "8c34d2fa2cf95cddb740ac3f",
                new Entry("Black Division Red Right Hand", "", 15)
            },
            {
                "69fac4019f225637638f0819",
                new Entry("Black Division Defector - Squad Leader", "", 0)
            },
            {
                "6a3f1956080aab5ba0e76dc4",
                new Entry("Black Division Defector - Squad Leader", "", 1)
            },
            {
                "6a2f66e0d735b2472d12fff7",
                new Entry("Black Division Defector - Squad Leader", "", 2)
            },
            {
                "69e945fadf2575af720e59a4",
                new Entry("White Tusks", "", 0)
            },
            {
                "69e95ac079841b54ca397fe3",
                new Entry("White Tusks", "", 1)
            },
            {
                "69e961a4bc7177e6288c1d38",
                new Entry("White Tusks", "", 2)
            },
            {
                "615f210d8b6c486b8036c4f8",
                new Entry("White Tusks", "", 3)
            },
            {
                "69e9438291f3244722513a32",
                new Entry("White Tusks", "", 4)
            },
            {
                "69e95545b2043c936df86ff4",
                new Entry("White Tusks", "", 5)
            },
            {
                "69e96185a1551294ebd4994b",
                new Entry("White Tusks", "", 6)
            },
            {
                "6a4c43efd4aa0402ad7437b4",
                new Entry("White Tusks", "", 7)
            },
            {
                "6a4c44d36cc38e9db91bb969",
                new Entry("White Tusks", "", 8)
            },
            {
                "6f3b0e324bd038d1e1d8141e",
                new Entry("White Tusks", "", 9)
            },
            {
                "d5fddab80f4f4a218bd0851f",
                new Entry("White Tusks", "", 10)
            },
            {
                "b6eace2010145abbbd8287d9",
                new Entry("White Tusks", "", 11)
            },
            {
                "69f5317807224670e00610ab",
                new Entry("Goons", "", 0)
            },
            {
                "69f54e1bd2e671111a2aaccb",
                new Entry("Goons", "", 1)
            },
            {
                "6a2df4de88cef00b4dd0f6c4",
                new Entry("Goons", "", 2)
            },
            {
                "6a2df4f86a7f45858f3e59e1",
                new Entry("Goons", "", 3)
            },
            {
                "6a2df504703ffaf5c01f92a7",
                new Entry("Goons", "", 4)
            },
            {
                "69f501aabb8b24b5342e45b1",
                new Entry("Goons", "", 5)
            },
            {
                "69f54e0ba88fe4200941f527",
                new Entry("Goons", "", 6)
            },
            {
                "69f55793b94ee0b50bbead9d",
                new Entry("Goons", "", 7)
            },
            {
                "69f5589cf0ddf2834e092bfb",
                new Entry("Goons", "", 11)
            },
            {
                "6a2df306475990d8dd58d4e3",
                new Entry("Goons", "", 8)
            },
            {
                "6a2df36026b471aedc57ce48",
                new Entry("Goons", "", 9)
            },
            {
                "6a2df40d2f6ccbe0c55eefce",
                new Entry("Goons", "", 10)
            },
            {
                "6a179eb1129e302865319e97",
                new Entry("ISB Team Leaders", "", 0)
            },
            {
                "6a2230f51f0ae72229315852",
                new Entry("ISB Team Leaders", "", 1)
            },
            {
                "6a0d16ca143fcdbbddc7594c",
                new Entry("ISB Team Leaders", "", 2)
            },
            {
                "6a2c8e83792e12df3a52d611",
                new Entry("ISB Team Leaders", "", 3)
            },
            {
                "6a0dc5770d976029af435922",
                new Entry("ISB Team Leaders", "", 4)
            },
            {
                "6a179e3d31ecadf25019162a",
                new Entry("ISB Team Leaders", "", 5)
            },
            {
                "6a179e6c7bf73c4d9d6741d4",
                new Entry("ISB Team Leaders", "", 6)
            },
            {
                "6a2c9204eb3ef08a22f0d72a",
                new Entry("ISB Team Leaders", "", 7)
            },
            {
                "6a2f1ad72aad6bc664b56057",
                new Entry("ISB Team Leaders", "", 8)
            },
            {
                "a083303ee044a3b13ef9f33a",
                new Entry("Unit of Hope", "", 0)
            },
            {
                "c2f6a310e2a0cee9345238f0",
                new Entry("Unit of Hope", "", 1)
            },
            {
                "12da8300a205cbcd8e4c14cf",
                new Entry("Unit of Hope", "", 2)
            },
            {
                "75722f5b39439f36a8f9d7c3",
                new Entry("Unit of Hope", "", 3)
            },
            {
                "d6eb65835e4f5ca6a759eecd",
                new Entry("ISB JUGGERNAUT", "", 0)
            },
            {
                "a4e52b6470065a86a2364192",
                new Entry("ISB JUGGERNAUT", "", 1)
            },
            {
                "b30106da093272ee7f8d42e2",
                new Entry("ISB JUGGERNAUT", "", 2)
            },
            {
                "75a8026625a2a94104295f25",
                new Entry("ISB JUGGERNAUT", "", 3)
            },
            {
                "eaab25722756ba2968e181b4",
                new Entry("ISB Team Leaders", "", 9)
            },
            {
                "e34020084fed486f37ad6aaa",
                new Entry("ISB Team Leaders", "", 10)
            },
            {
                "84ac107d4300998297af5b3d",
                new Entry("ISB Team Leaders", "", 11)
            },
            {
                "f0e69186991e63ea7a986743",
                new Entry("ISB Team Leaders", "", 12)
            },
            {
                "5868a8ca14ff1eaf69403825",
                new Entry("ISB Team Leaders", "", 13)
            },
            {
                "cbabe395e81deb8bc286c6dd",
                new Entry("ISB Team Leaders", "", 14)
            },
        };
    }
}
