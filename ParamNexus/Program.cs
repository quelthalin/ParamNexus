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
        static void Main(string[] args)
        {
            var rootCommand = new RootCommand();

            var importCmd = new Command("import")
            {
                new Option<string>(
                    "--paramdef-location",
                    "The path to the paramdef file to be loaded. Use the DCX file, e.g. 'paramdef.paramdefbnd.dcx'"
                ),
                new Option<string[]>(
                    "--param-locations",
                    "Comma-separated list of paths containing param files to load. Anything '*.parambnd.dcx' will be loaded."
                ) { Argument = new Argument<string[]>() },
                new Option<string>(
                    "--message-location",
                    "Path containing message files to load. Anything '*.msgbnd.dcx' will be loaded."
                ),
                new Option<string>(
                    "--db-location",
                    "The path to the database file to be used. If it doesn't exist, it will be created."
                ) { Argument = new Argument<string>() { Arity = ArgumentArity.ExactlyOne }}
            };

            var exportCmd = new Command("export")
            {
                new Option<string>(
                    "--output-files-dir",
                    "The directory to output exported DB files to. If not provided, will write to original source locations."
                ),
                new Option<bool>(
                    "--overwrite-output-files",
                    description: "If true, overwrite file if present. If false, rename the old file as 'filename.<unix_timestamp>.bak' before writing. Default false."
                )
                {
                    Argument = new Argument<bool>(() => false)
                },
                new Option<string>(
                    "--db-location",
                    "The path to the database file to be used. If it doesn't exist, it will be created."
                ) { Argument = new Argument<string>() { Arity = ArgumentArity.ExactlyOne }}
            };

            rootCommand.AddCommand(importCmd);
            rootCommand.AddCommand(exportCmd);

            importCmd.Handler = CommandHandler.Create<string, IEnumerable<string>, string, string>(
                (paramdefLocation, paramLocations, messageLocation, dbLocation) =>
            {
                Console.WriteLine($"The value for --paramdef-location is: {paramdefLocation}");
                Console.WriteLine($"The value for --param-locations is: " + String.Join(",", paramLocations));
                Console.WriteLine($"The value for --message-location is: {messageLocation}");
                Console.WriteLine($"The value for --db-location is: {dbLocation}");

                var paramLocsList = paramLocations.ToList();
                ParamDatabase pd = new ParamDatabase(dbLocation);
                pd.LoadDatabase(paramdefLocation, paramLocsList, messageLocation);
            });

            exportCmd.Handler = CommandHandler.Create<string, bool, string>(
            (outputFilesDir, overwriteOutputFiles, dbLocation) =>
            {
                Console.WriteLine($"The value for --output-files-dir is: {outputFilesDir}");
                Console.WriteLine($"The value for --overwrite-output-files is: {overwriteOutputFiles}");
                Console.WriteLine($"The value for --db-location is: {dbLocation}");

                ParamDatabase pd = new ParamDatabase(dbLocation);
                pd.ExportDatabase(outputFilesDir, overwriteOutputFiles);
            });

            rootCommand.InvokeAsync(args).Wait();
        }
    }
}
