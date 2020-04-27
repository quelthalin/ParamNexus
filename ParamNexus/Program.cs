using Microsoft.Win32;
using ParamNexusDB;
using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace ParamNexus
{
    class Program
    {
        private static readonly string paramdefFilepath = @"D:\Steam\steamapps\common\DARK SOULS REMASTERED\paramdef\paramdef.paramdefbnd.dcx";
        private static readonly IList<string> paramFilepaths = new List<string> { @"D:\Steam\steamapps\common\DARK SOULS REMASTERED\param\GameParam\GameParam.parambnd.dcx" };


        public static void Main(string[] args)
        {
            //ParamLoader pl = new ParamLoader(@"Data Source=D:\downloads\DeS.db;");
            ParamLoader pl = new ParamLoader(@"Data Source=D:\downloads\DS.db;");
            pl.LoadGameParams(paramdefFilepath, paramFilepaths);
            //var update = @"UPDATE EquipParamWeapon SET attackBaseMagic=200, attackBaseFire=200, attackBaseStamina=0 WHERE id=20110";
            //using (var con = pl.GetConnection())
            //{
            //    con.Open();
            //    using (var cmd = new SQLiteCommand(update, con))
            //    {
            //        cmd.ExecuteNonQuery();
            //    }
            //}
            //ParamWriter pr = new ParamWriter(@"Data Source=D:\downloads\DeS.db;", @"D:\downloads\DeS_output");
            //MessageLoader ml = new MessageLoader(@"Data Source=D:\downloads\DeS.db;");
            ParamWriter pr = new ParamWriter(@"Data Source=D:\downloads\DS.db;", @"D:\downloads\DS_output");
            MessageLoader ml = new MessageLoader(@"Data Source=D:\downloads\DS.db;");
        }
    }
}
