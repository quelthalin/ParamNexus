using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text;

namespace ParamNexusDB
{
    public class ParamDatabase
    {
        public string DbLocation { get; private set; }

        public ParamDatabase(string dbLocation)
        {
            // If we don't do this, shift-jis won't work.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            DbLocation = dbLocation;
        }

        public void LoadDatabase(string paramdefLocation, List<string> paramDirs, string messageDir)
        {
            using (var con = GetConnection())
            {
                con.Open();

                // Drop all existing tables, so everything can just create and insert.
                ParamLoader.CleanupExistingTables(con);

                using (var transaction = con.BeginTransaction())
                {
                    if (File.Exists(DbLocation))
                    {
                        // Only load the data we're told to load.
                        if (paramdefLocation != null && paramDirs != null)
                        {
                            ParamLoader.LoadParams(con, paramdefLocation, paramDirs);
                        }

                        if (messageDir != null)
                        {
                            MessageLoader.LoadMessages(con, messageDir);
                        }
                    }
                    transaction.Commit();
                }
            }
        }

        public void ExportDatabase(string outputPath, bool overwriteOutputFiles = false)
        {
            using (var con = GetConnection())
            {
                con.Open();

                ParamWriter.WriteParams(con, outputPath, overwriteOutputFiles);
            }
        }

        /// <summary>
        /// Gets a connection to the database. The caller is expected to properly close the connection.
        /// </summary>
        /// <returns></returns>
        public SQLiteConnection GetConnection()
        {
            var conStr = @"Data Source=" + DbLocation + @";Version=3;";
            var con = new SQLiteConnection(conStr);
            return con;
        }
    }
}
