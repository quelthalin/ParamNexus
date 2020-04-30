using ParamNexusDB;
using System;
using System.Linq;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Collections.Generic;

namespace ParamNexus
{
    class Program
    {
        static int Main(string[] args)
        {
            var rootCommand = new RootCommand();

            var importCmd = new Command("import", "Imports the provided paramfiles and localizations into a SQLite database")
            {
                new Option<string>(
                    "--paramdef-location",
                    "The path to the paramdef file to be loaded. Use the DCX file, e.g. 'paramdef.paramdefbnd.dcx'"
                ),
                new Option<string[]>(
                    "--param-locations",
                    "Comma-separated list of paths containing param files to load. Anything '*.parambnd.dcx' will be loaded."
                ),
                new Option<string>(
                    "--message-location",
                    "Path containing message files to load. Anything '*.msgbnd.dcx' will be loaded."
                ),
                new Option<string>(
                    "--db-location",
                    "The path to the database file to be used. If it doesn't exist, it will be created."
                )
            };

            var exportCmd = new Command("export", "Exports the provided database back to param files")
            {
                new Option<string>(
                    "--output-files-dir",
                    "The directory to output exported DB files to. If not provided, will write to original source locations."
                ),
                new Option<bool>(
                    "--overwrite-output-files",
                    "If true, overwrite file if present. If false, rename the old file as 'filename.<unix_timestamp>.bak' before writing. Default false."
                )
                { Argument = new Argument<bool>(() => false)},
                new Option<string>(
                    "--db-location",
                    "The path to the database file to be used."
                )
            };

            importCmd.Handler = CommandHandler.Create<string, IEnumerable<string>, string, string>(
                (paramdefLocation, paramLocations, messageLocation, dbLocation) =>
            {
                bool valid = true;
                if (String.IsNullOrEmpty(dbLocation))
                {
                    Console.WriteLine("Required option --db-location missing");
                    valid = false;
                }
                if(String.IsNullOrEmpty(paramdefLocation))
                {
                    Console.WriteLine("Required option --paramdef-location missing");
                    valid = false;
                }
                if (!paramLocations?.Any() ?? false)
                {
                    Console.WriteLine("Required option --param-locations missing");
                    valid = false;
                }
                if(String.IsNullOrEmpty(messageLocation))
                {
                    Console.WriteLine("No --message-location is provided. Localizations will not be loaded.");
                    // Will carry forward without this.
                }

                if (valid)
                {
                    var paramLocsList = paramLocations.ToList();
                    ParamDatabase pd = new ParamDatabase(dbLocation);
                    pd.LoadDatabase(paramdefLocation, paramLocsList, messageLocation);
                }
            });

            exportCmd.Handler = CommandHandler.Create<string, bool, string>(
            (outputFilesDir, overwriteOutputFiles, dbLocation) =>
            {
                bool valid = true;
                if (String.IsNullOrEmpty(dbLocation))
                {
                    Console.WriteLine("Required option --db-location missing");
                    valid = false;
                }

                if (valid)
                {
                    ParamDatabase pd = new ParamDatabase(dbLocation);
                    pd.ExportDatabase(outputFilesDir, overwriteOutputFiles);
                }
            });

            rootCommand.AddCommand(importCmd);
            rootCommand.AddCommand(exportCmd);

            return rootCommand.InvokeAsync(args).Result;
        }
    }
}
