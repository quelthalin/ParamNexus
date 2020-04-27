using System;
using System.Collections.Generic;
using System.Data;
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
        private readonly Dictionary<string, string> DesMsgFileNamesToEnglish = new Dictionary<string, string>()
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

        private readonly string conStr;

        // TODO make these parameterized
        private readonly IList<string> messageFilepath = new List<string> { @"D:\Steam\steamapps\common\DARK SOULS REMASTERED\msg\ENGLISH\item.msgbnd.dcx" };


        public MessageLoader(string conStr)
        {
            // If we don't do this, shift-jis won't work.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            this.conStr = conStr;

            LoadMessages(messageFilepath);
        }

        /// <summary>
        /// Gets a connection to our database. The caller is expected to properly close the connection.
        /// </summary>
        /// <returns></returns>
        public SQLiteConnection GetConnection()
        {
            var con = new SQLiteConnection(conStr);
            return con;
        }

        private void ReadMessagesIntoDatabase(string filename, FMG msgFile)
        {
            var tableName = DesMsgFileNamesToEnglish.TryGetValue(Path.GetFileNameWithoutExtension(filename), out string value) ? value : Path.GetFileNameWithoutExtension(filename);
            //var tableName = DesMsgFileNamesToEnglish[Path.GetFileNameWithoutExtension(filename)];
            Console.WriteLine();

            Console.WriteLine("Dropping table: " + tableName);

            using (var con = GetConnection())
            {
                con.Open();
                using (var cmd = new SQLiteCommand("DROP TABLE IF EXISTS " + tableName, con))
                {
                    cmd.ExecuteNonQuery();
                }

                Console.WriteLine("Creating table: " + tableName);
                var sb = new StringBuilder();
                sb.Append(@"CREATE TABLE '");
                sb.Append(tableName);
                sb.Append("' (");

                sb.Append(@"'id' INTEGER NOT NULL PRIMARY KEY,");
                sb.Append(@"'message' TEXT);");

                using (var cmd = new SQLiteCommand(sb.ToString(), con))
                {
                    cmd.ExecuteNonQuery();
                }

                // Actually insert our data
                // Yes, we could merge some of these loops. But this is small data, so I don't care, this is clearer.
                sb.Clear();
                sb.Append(@"INSERT INTO '");
                sb.Append(tableName);
                sb.Append(@"' (id, message) VALUES($id, $message);");

                using (var transaction = con.BeginTransaction())
                using (var cmd = new SQLiteCommand(sb.ToString(), con))
                {
                    //    var fieldDict = new Dictionary<String, SQLiteParameter>();
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
                    transaction.Commit();
                }
            }
        }

        public void LoadMessages(IList<string> messageFilePaths)
        {
            var messages = new Dictionary<string, PARAM>();
            var msgbnd = BND3.Read(messageFilePaths[0]);
            foreach (BinderFile file in msgbnd.Files)
            {
                string name = System.IO.Path.GetFileNameWithoutExtension(file.Name);

                // Yes, .msgbnd file is FMG
                FMG msg = FMG.Read(file.Bytes);
                ReadMessagesIntoDatabase(file.Name, msg);
            }
        }
    }
}
