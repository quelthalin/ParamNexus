# ParamNexus
ParamNexus provides a SQL interface to [SoulsFormats](https://github.com/JKAnderson/SoulsFormats) for Demon's Souls. This enables users to create a database, make your changes via SQL, and write them back to the original files. It also loads up the localized strings, so that you can query using the actual English strings where they exist, rather than dealing with the original Japanese strings found with the data.

## Why SQL?
If you know C# already, the original SoulsFormats is a well-understood and common way to interact with files. However, games later than Demon's Souls typically have preexisting tools that are used in modders workflows. Using SQL has some benefits:

* The database used, SQLite, is very language agnostic. There's appropriate and tested bindings for common languages. This lets you write in whatever language you're comfortable with, but with the upfront cost of loading the database.
* SQL already has plenty of pre-built UIs for interaction and data exploration.
* Easy data discovery. Even if you aren't planning on pushing updates via ParamNexus, using SQL to dig through data is often going to be easier than interacting with spreadsheets or raw files, while enabling very powerful queries.

The output of ParamNexus is a database file, you can load it up into an outside program, e.g. DBeaver as seen here:
![Querying for NPCs associated with item lots](/img/dbeaver_img.png?raw=true)

Right now ParamNexus loads primarily Demon's Souls files, but Dark Souls Remaster gameparam (but not drawparam) currently load successfully. This might be useful for data discovery, but I'm not familiar enough to validate.

## How to setup the project
* Assumes .NET Framework 4.8
* Clone the project
* https://github.com/JKAnderson/SoulsFormats is required.
  * This is referenced by ParamNexus as a local project, because there is no up to date release, to the best of my knowledge. Clone SoulsFormats in the same directory ParamNexus is cloned to.
  * `99bd4d5de4eaebb1b849aecb2fd3e6d2d395c556` is a known working git commit.
* At this point, you should be able to build the solution.

## Loading a database
If you run ParamNexus.exe with no args, you'll get some usage info back:
```
$ ParamNexus
Required command was not provided.

Usage:
  ParamNexus [options] [command]

Options:
  --version    Display version information

Commands:
  import
  export
```

We'll want to run the following command to load everything in. Make sure to replace `<path_to_paramdef>`, `<path_to_param>`, `<path_to_msg>`, and `<output_dir>` appropriately.  `--message-location` is optional, but omitting it means localizations won't be loaded. Don't include a backslash at the end of paths. Feel free to choose a different DB name.
```
import --paramdef-location "<path_to_paramdef>\paramdef.paramdefbnd.dcx" --param-locations "<path_to_param>/gameparam" --param-locations "<path_to_param>\drawparam" --message-location "<path_to_msg>\na_english" --db-location "<output_dir>\DeS.db"
```

If everything goes successfully, you'll have an output database file.  Query it using your preferred SQL interface.  There's a few types of tables worth mentioning
* Tables that directly correspond to a param, e.g. EquipParamWeapon and Magic. These are field for field the same as the source files.
* Tables that correspond to localization strings. These are tables like item_name and armor_description. They all are `id,message`, where the ID corresponds to the appropriate table's row id.
* Tables containing metadata necessary to glue everything back together. These are tables like `bnd_contents` and `paramdef_metadata`.

## Interacting with our database.
Run a few queries. For example to find a particular weapon, e.g. a Broadsword+10, you can do the following:
```
SELECT * FROM EquipParamWeapon epw
INNER JOIN weapon_name 
WHERE weapon_name.id  = epw.id AND message LIKE "Broadsword+10";
```

To update that same weapon, because we know the id from the previous query, we can run something like
```
UPDATE EquipParamWeapon
SET attackBaseFire = 200,
	attackBaseMagic = 200,
	attackBaseStamina = 0
WHERE id = 20110;
```

You could also run more broad queries. E.g. to get all the item lot ids associated with a enemy/NPC drops and their localized name
```
SELECT ilp.id AS ilp_id, np.id AS np_id, nn.message FROM ItemLotParam ilp
LEFT OUTER JOIN NpcParam np
ON np.itemLotId_1 == ilp.id OR 
   np.itemLotId_2 == ilp.id OR 
   np.itemLotId_3 == ilp.id OR 
   np.itemLotId_4 == ilp.id OR 
   np.itemLotId_5 == ilp.id OR 
   np.itemLotId_6 = ilp.id
LEFT OUTER JOIN npc_name nn
ON np.nameId = nn.id
WHERE nn.id IS NOT NULL AND nn.message IS NOT NULL;
```

This results in
```
50	5	Graverobber Blige
90	9	First Musketeer
100	10	Second Musketeer
110	11	Third Musketeer
120	12	Saint Urbain
121	12	Saint Urbain
122	12	Saint Urbain
123	12	Saint Urbain
...
```

A more complex query could also provide the translated names of the items in the lots, or even update a large number of rows at the same time.

Anything in the params we update will be reflected in our repacked files, with two caveats:
* draw params are relatively untested, due to my unfamiliarity.
* Localizations are currently not written back to source.

## Exporting a database
Before exporting a database, I recommend creating manual backups of the appropriate `*.dcx` files in both `gameparam` and `drawparam`, given the early stage in development of ParamNexus.

Here's where we turn to the `export` subcommand of ParamNexus.

If we want to output to the directory we originally loaded our files from:
```
export --db-location "<db_dir>\DeS.db" --overwrite-output-files false
```

This command will write out new param files, including any updates made. They will replace the originals. If `--overwrite-output-files` is `false` (which it is by default), we will rename the originals to `<original_filename>.<unix_timestamp>.bak`. If `true`, the originals will be lost and replaced by ParamNexus output. It is strongly recommended to not overwrite the original files.

If we want to place all our files into a separate, isolated directory, we can do so. Note that the original structure will not be preserved (e.g. `drawparam` vs `gameparam`).
```
export --db-location "<db_dir>\DeS.db" --overwrite-output-files false --output-files-dir "<output_files_dir>"
```

### High level todos:
* ~Kill all the hardcoding~. There's direct use of BND3 and so on, but most of the hardcoding is cleaned up.
* ~Make it easier to interface with~. Added a CLI interface. 
* ~Load the drawparams and such, too.~
* Plenty of refactoring, I'm sure
* potentially expand past DS1
* Clean up C# sins. I don't know what they are yet, but I'm sure I committed them.
* ~Backup files if they'll be overwritten.~ Files will be written in the form `<original-name>.<unix-timestamp.bak`. Optionally can force overwrite.
* Make an dead simple way for users to load data, not just CLI. Suggestions were .bat or drag-and-drop (and maybe just load supported files?)
* ~Clean up the CLI help for subcommands.~
* In the case of duplicate IDs, use the first instead of the last
