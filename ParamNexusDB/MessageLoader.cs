using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text;
using SoulsFormats;

namespace ParamNexusDB
{
    public class MessageLoader
    {
        // All the msg filenames are in Japanese. Give a vaguely useful English translation for querying.
        // TODO this should be configurable by game.
        private static readonly Dictionary<string, string> DesMsgFileNamesToEnglish = new Dictionary<string, string>()
        {
            { "NPC名", "npc_name" },  // 18
            { "アイテムうんちく", "item_lore" },  // 24
            { "アイテム名", "item_name" },  // 10
            { "アイテム説明", "item_description" },  // 20
            { "アクセサリうんちく", "accessory_lore" },  // 27
            { "アクセサリ名", "accessory_name" },  // 13
            { "アクセサリ説明", "accessory_description" },  // 23
            { "イベントテキスト", "event_text" },  // 30
            { "インゲームメニュー", "in_game_menu" },  // 70
            { "キーガイド", "key_guide" },  // 79
            { "ダイアログ", "dialog" },  // 78
            { "テキスト表示用タグ一覧", "display_tags" },  // 90
            { "ムービー字幕", "movie_subtitles" },  // 3
            { "メニューその他", "menu_and_others" },  // 77
            { "メニュー共通テキスト", "menu_common" },  // 76
            { "一行ヘルプ", "help" },  // 80
            { "会話", "conversation" },  // 1
            { "地名", "place_name" },  // 19
            { "武器うんちく", "weapon_lore" },  // 25
            { "武器名", "weapon_name" },  // 11
            { "武器説明", "weapon_description" },  // 21
            { "特徴うんちく", "feature" },  // 17
            { "特徴名", "feature_name" },  // 15
            { "特徴説明", "feature_description" },  // 16
            { "血文字", "blood_message" },  // 2
            { "防具うんちく", "armor_lore" },  // 26
            { "防具名", "armor_name" },  // 12
            { "防具説明", "armor_description" },  // 22
            { "項目ヘルプ", "field_help" },  // 81
            { "魔法うんちく", "magic_lore" },  // 29
            { "魔法名", "magic_name" },  // 14
            { "魔法説明", "magic_description" }  // 28
        };

        private static void ReadMessagesIntoDatabase(SQLiteConnection con, string name, FMG msgFile)
        {
            var tableName = DesMsgFileNamesToEnglish.TryGetValue(name, out string value) ? value : name;
            Console.WriteLine("Tablename is " + tableName);

            // Create the table to write into
            // Oddly enough, DS1 seems to have multiple copies of the same data in the same file.
            // Not sure if that's a source issue, or bug.
            var sb = new StringBuilder();
            sb.Append(@"CREATE TABLE IF NOT EXISTS'");
            sb.Append(tableName);
            sb.Append("' (");

            sb.Append(@"'id' INTEGER NOT NULL PRIMARY KEY,");
            sb.Append(@"'message' TEXT);");

            using (var cmd = new SQLiteCommand(sb.ToString(), con))
            {
                cmd.ExecuteNonQuery();
            }

            // Actually insert our data
            sb.Clear();
            sb.Append(@"INSERT OR IGNORE INTO '");
            sb.Append(tableName);
            sb.Append(@"' (id, message) VALUES($id, $message);");

            using (var cmd = new SQLiteCommand(sb.ToString(), con))
            {
                var idParam = cmd.CreateParameter();
                idParam.ParameterName = @"$id";
                cmd.Parameters.Add(idParam);

                var msgParam = cmd.CreateParameter();
                msgParam.IsNullable = true;
                msgParam.ParameterName = @"$message";
                cmd.Parameters.Add(msgParam);

                foreach (FMG.Entry entry in msgFile.Entries)
                {
                    idParam.Value = entry.ID;
                    msgParam.Value = entry.Text;

                    cmd.ExecuteNonQuery();
                }
            }
        }

        public static void LoadMessages(SQLiteConnection con, string messageDir)
        {

            List<string> messageFilepaths = new List<string>();
            messageFilepaths.AddRange(Directory.GetFiles(messageDir, "*.msgbnd.dcx"));

            foreach (string messageFilepath in messageFilepaths)
            {
                var msgbnd = BND3.Read(messageFilepath);
                foreach (BinderFile file in msgbnd.Files)
                {
                    string name = Path.GetFileNameWithoutExtension(file.Name);

                    // Yes, .msgbnd file is FMG
                    FMG msg = FMG.Read(file.Bytes);
                    ReadMessagesIntoDatabase(con, name, msg);
                }
            }
        }
    }
}