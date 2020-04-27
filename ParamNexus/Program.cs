using ParamNexusDB;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.IO;
using System.Linq;

namespace ParamNexus
{
    class Program
    {
        private const int ERROR_INVALID_COMMAND_LINE = 0x667;

        /// <param name="paramdefLocation">An option whose argument is parsed as an int</param>
        /// <param name="paramLocations">An option whose argument is parsed as an int</param>
        /// <param name="messageLocations">An option whose argument is parsed as an int</param>
        /// <param name="dbLocation">An option whose argument is parsed as a bool</param>
        /// <param name="outputFilesDir">An option whose argument is parsed as a bool</param>
        /// <param name="overwriteOutputFiles">An option whose argument is parsed as a FileInfo</param>
        /// <param name="overwriteDb">An option whose argument is parsed as a FileInfo</param>
        static void Main(
            string paramdefLocation = null,
            string paramLocations = null,
            string messageLocations = null,
            string dbLocation = null,
            string outputFilesDir = null,
            bool overwriteOutputFiles = false,
            bool overwriteDb = false)
        {
            Console.WriteLine($"The value for --paramdef-location is: {paramdefLocation}");
            Console.WriteLine($"The value for --param-locations is: {paramLocations}");
            Console.WriteLine($"The value for --message-locations is: {messageLocations}");
            Console.WriteLine($"The value for --db-location is: {dbLocation}");
            Console.WriteLine($"The value for --output-files-dir is: {outputFilesDir}");
            Console.WriteLine($"The value for --overwrite-output-files is: {overwriteOutputFiles}");
            Console.WriteLine($"The value for --overwrite-db is: {overwriteDb}");

            ValidateProperPath("--db-location", dbLocation);

            string dataSource = @"Data Source=" + dbLocation + @";";

            if (!File.Exists(dbLocation) || overwriteDb)
            {
                // Only load the data we're told to load.
                if (paramdefLocation != null && paramLocations != null)
                {
                    ValidateProperPath("--paramdef-location", paramdefLocation);
                    var paramLocsList = paramLocations.Split(',').ToList();
                    foreach (string paramLoc in paramLocsList)
                    {
                        ValidateProperPath("--param-locations", paramLoc);
                    }

                    ParamLoader pl = new ParamLoader(dataSource);
                    pl.LoadGameParams(paramdefLocation, paramLocsList);
                }

                if (messageLocations != null)
                {
                    var messageLocsList = paramLocations.Split(',').ToList();
                    foreach (string messageLoc in messageLocsList)
                    {
                        ValidateProperPath("--message-locations", messageLoc);
                    }
                    MessageLoader ml = new MessageLoader(dataSource);
                    ml.LoadMessages(messageLocsList);
                }
            } else
            {
                Console.WriteLine("Using existing database");
            }

            // TODO this would be where we want to allow any follow-on SQL files.
            //var update = @"UPDATE EquipParamWeapon SET attackBaseMagic=200, attackBaseFire=200, attackBaseStamina=0 WHERE id=20110";
            //using (var con = pl.GetConnection())
            //{
            //    con.Open();
            //    using (var cmd = new SQLiteCommand(update, con))
            //    {
            //        cmd.ExecuteNonQuery();
            //    }
            //}

            if (outputFilesDir != null)
            {
                ValidateProperPath("--output-files-dir", outputFilesDir);
                ParamWriter pr = new ParamWriter(dataSource);
                pr.WriteGameParams(outputFilesDir, overwriteOutputFiles);
            }
        }

        private static void ValidateProperPath(string argument, string dbLocation)
        {
            try
            {
                Path.GetFullPath(dbLocation);
            }
            catch (Exception)
            {
                Console.WriteLine(argument + @" is not a proper path");
                Environment.ExitCode = ERROR_INVALID_COMMAND_LINE;
            }
        }
    }
}
