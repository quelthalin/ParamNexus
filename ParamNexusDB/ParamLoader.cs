using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
using SoulsFormats;
using static SoulsFormats.PARAM;
using static SoulsFormats.PARAMDEF;

namespace ParamNexusDB
{
    public class ParamLoader
    {
        private class BndContentsEntry
        {
            public BndContentsEntry(string sourceFile, int fileId, string name, Binder.FileFlags flags, DCX.Type compressionType, string paramType)
            {
                this.SourceFile = sourceFile;
                this.FileId = fileId;
                this.Name = name;
                this.Flags = flags;
                this.CompressionType = compressionType;
                this.ParamType = paramType;
            }

            public string SourceFile { get; private set; }

            public int FileId { get; private set; }

            public string Name { get; private set; }

            public Binder.FileFlags Flags { get; private set; }

            public DCX.Type CompressionType { get; private set; }

            public string ParamType { get; private set; }
        }

        public static void CleanupExistingTables(SQLiteConnection con)
        {
            var cleanupCommand = @"PRAGMA writable_schema = 1;
            delete from sqlite_master where type = 'table';
            PRAGMA writable_schema = 0;
            VACUUM; 
            PRAGMA INTEGRITY_CHECK;";
            using (var cmd = new SQLiteCommand(cleanupCommand, con))
            {
                cmd.ExecuteNonQuery();
            }
        }

        public static void LoadParams(SQLiteConnection con, string paramdefFilepath, IList<string> paramDirs)
        {
            // The metadata tables should be created ahead of time.
            CreateBndMetadataTables(con);
            CreateBndTableOfContentsTable(con);
            CreateParamMetadataTables(con);

            // Reading an original paramdefbnd
            var paramdefs = new Dictionary<string, PARAMDEF>();
            var paramdefbnd = BND3.Read(paramdefFilepath);
            foreach (BinderFile file in paramdefbnd.Files)
            {
                var paramdef = PARAMDEF.Read(file.Bytes);
                paramdefs[paramdef.ParamType] = paramdef;
            }
            ReadParamdefsIntoDatabase(con, paramdefs.Values.ToList());

            // Loading parambnd
            List<string> paramFilepaths = new List<string>();

            foreach (var paramDir in paramDirs)
            {
                // DeS has both a gameparam.parambnd.dcx and a gameparamna.parambnd.dcx.
                // Only grab gameparamna.parambnd.dcx if we have it.
                string filterPattern = "*.parambnd.dcx";
                if (Directory.GetFiles(paramDir, "*gameparamna.parambnd.dcx").Length > 0)
                {
                    Console.WriteLine("Skipping gameparam.parambnd.dcx");
                    filterPattern = "*gameparamna.parambnd.dcx";
                }

                paramFilepaths.AddRange(Directory.GetFiles(paramDir, filterPattern));
            }

            foreach (var paramFilepath in paramFilepaths)
            {
                // Have to construct Table of Contents as we go through, since the info isn't all at BND level, but is needed when reconstructing
                var bndContents = new List<BndContentsEntry>();
                Console.WriteLine("Loading file: " + paramFilepath);
                var parambnd = BND3.Read(paramFilepath);
                foreach (BinderFile file in parambnd.Files)
                {
                    PARAM param = PARAM.Read(file.Bytes);
                    // DSR doesn't seem to like applying carefully, specifically SP_EFFECT_PARAM_ST in Gameparam. At minimum.
                    param.ApplyParamdef(paramdefs[param.ParamType]);

                    var entry = new BndContentsEntry(paramFilepath, file.ID, file.Name, file.Flags, file.CompressionType, param.ParamType);
                    bndContents.Add(entry);
                    ReadParamIntoDatabase(con, Path.GetFileNameWithoutExtension(file.Name), param);
                }

                // Create the metadata tables
                ReadBndMetadataIntoDatabase(con, paramFilepath, parambnd);
                ReadBndTableOfContentsIntoDatabase(con, Path.GetFileName(paramFilepath), bndContents);
            }
        }

        private static void CreateBndMetadataTables(SQLiteConnection con)
        {
            var createTable = @"CREATE TABLE 'bnd_metadata'
                ('filename' TEXT,
                 'big_endian' BOOLEAN,
                 'bit_big_endian' BOOLEAN,
                 'compression' TEXT,
                 'format' INTEGER,
                 'unk18' INTEGER,
                 'version' TEXT
                )";
            using (var cmd = new SQLiteCommand(createTable, con))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private static void ReadBndMetadataIntoDatabase(SQLiteConnection con, String filename, BND3 bnd)
        {
            var insert = @"INSERT INTO 'bnd_metadata'
                ('filename', 'big_endian', 'bit_big_endian', 'compression', 'format', 'unk18', 'version')
                VALUES ($filename, $big_endian, $bit_big_endian, $compression, $format, $unk18, $version)";

            using (var cmd = new SQLiteCommand(insert, con))
            {
                AddParamToCommand(cmd, @"$filename", filename);
                AddParamToCommand(cmd, @"$big_endian", bnd.BigEndian);
                AddParamToCommand(cmd, @"$bit_big_endian", bnd.BitBigEndian);
                AddParamToCommand(cmd, @"$compression", bnd.Compression.ToString());
                AddParamToCommand(cmd, @"$format", bnd.Format);
                AddParamToCommand(cmd, @"$unk18", bnd.Unk18);
                AddParamToCommand(cmd, @"$version", bnd.Version);

                cmd.ExecuteNonQuery();
            }
        }

        private static void CreateParamMetadataTables(SQLiteConnection con)
        {
            var createTable = @"CREATE TABLE 'param_metadata' 
                (
                 'param_type' TEXT NOT NULL PRIMARY KEY,
                 'big_endian' BOOLEAN,
                 'compression' TEXT,
                 'format2d' TEXT,
                 'format2e' TEXT,
                 'paramdef_format_version' BLOB,
                 'unk06' INTEGER,
                 'paramdef_data_version' INTEGER
                )";
            using (var cmd = new SQLiteCommand(createTable, con))
            {
                cmd.ExecuteNonQuery();
            }
        }
        private static void ReadParamIntoDatabase(SQLiteConnection con, string tableName, PARAM paramFile)
        {
            var insert = @"INSERT OR IGNORE INTO 'param_metadata'
                ('param_type', 'big_endian', 'compression', 'format2d', 'format2e', 'paramdef_format_version', 'unk06', 'paramdef_data_version')
                VALUES ($param_type, $big_endian, $compression, $format2d, $format2e, $paramdef_format_version, $unk06, $paramdef_data_version)";
            using (var cmd = new SQLiteCommand(insert, con))
            {
                AddParamToCommand(cmd, @"$param_type", paramFile.AppliedParamdef.ParamType);
                AddParamToCommand(cmd, @"$big_endian", paramFile.BigEndian);
                AddParamToCommand(cmd, @"$compression", paramFile.Compression.ToString());
                AddParamToCommand(cmd, @"$format2d", paramFile.Format2D.ToString());
                AddParamToCommand(cmd, @"$format2e", paramFile.Format2E.ToString());
                AddParamToCommand(cmd, @"$paramdef_format_version", new byte[] { paramFile.ParamdefFormatVersion });
                AddParamToCommand(cmd, @"$unk06", paramFile.Unk06);
                AddParamToCommand(cmd, @"$paramdef_data_version", paramFile.ParamdefDataVersion);
                cmd.ExecuteNonQuery();
            }

            // Need to check if table already exists. For example, DeS EquipParamWeapon exists in both gameparamna
            // and default_drawparam.
            var tableExistsCmd = @"SELECT name FROM sqlite_master WHERE type='table' AND name='" + tableName + "';";
            using (var cmd = new SQLiteCommand(tableExistsCmd, con))
            {
                var result = cmd.ExecuteScalar();
                if (result != null)
                {
                    return;
                }
            }

            var sb = new StringBuilder();
            sb.Append(@"CREATE TABLE '");
            sb.Append(tableName);
            sb.Append("' (");
            sb.Append(@"'id' INTEGER NOT NULL,"); // Shockingly, not always a primary key, e.g. DeS default_AiStandardInfoBank

            // Don't include the padding fields at all.
            var realFields = paramFile.AppliedParamdef.Fields.FindAll(field => field.DisplayType != DefType.dummy8);

            foreach (Field field in realFields)
            {
                // Need to quote field names
                sb.Append(@"'");
                sb.Append(field.InternalName);
                sb.Append(@"' ");
                switch (field.DisplayType)
                {
                    // Ignore dummy8 type
                    case DefType.fixstr:
                    case DefType.fixstrW:
                        sb.Append(@"TEXT");
                        break;
                    case DefType.s16: // All the integer numeric types
                    case DefType.s32:
                    case DefType.u8:
                    case DefType.u16:
                    case DefType.u32:
                        sb.Append(@"INTEGER");
                        break;
                    case DefType.f32: // Floats
                        sb.Append(@"REAL");
                        break;
                }

                sb.Append(@",");
            }
            sb.Append(@"description TEXT");
            sb.Append(@");");

            using (var cmd = new SQLiteCommand(sb.ToString(), con))
            {
                cmd.ExecuteNonQuery();
            }

            // Actually insert our data
            // Yes, we could merge some of these loops. But this is small data, so I don't care, this is clearer.
            sb.Clear();
            sb.Append(@"INSERT INTO '");
            sb.Append(tableName);
            sb.Append(@"' (id,");
            foreach (Field field in realFields)
            {
                sb.Append(field.InternalName);
                sb.Append(@",");
            }
            sb.Append(@"description");
            sb.Append(@") VALUES($id,");
            foreach (Field field in realFields)
            {
                sb.Append(@"$");
                sb.Append(field.InternalName);
                sb.Append(@",");
            }
            sb.Append(@"$description");
            sb.Append(@");");

            using (var cmd = new SQLiteCommand(sb.ToString(), con))
            {
                var fieldDict = new Dictionary<String, SQLiteParameter>();

                var idParam = cmd.CreateParameter();
                idParam.ParameterName = @"$id";
                cmd.Parameters.Add(idParam);

                foreach (Field field in realFields)
                {
                    var param = cmd.CreateParameter();
                    param.ParameterName = @"$" + field.InternalName;
                    cmd.Parameters.Add(param);
                    fieldDict.Add(field.InternalName, param);
                }

                var descParam = cmd.CreateParameter();
                descParam.ParameterName = @"$description";
                cmd.Parameters.Add(descParam);

                foreach (Row row in paramFile.Rows)
                {
                    idParam.Value = row.ID;
                    foreach (Field field in realFields)
                    {
                        var param = fieldDict[field.InternalName];
                        param.Value = row[field.InternalName].Value ?? DBNull.Value;
                    }
                    descParam.Value = row.Name;
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static void CreateBndTableOfContentsTable(SQLiteConnection con)
        {
            var createTable = @"CREATE TABLE 'bnd_contents'
                (
                 'source_file' TEXT NOT NULL,
                 'file_id' INTEGER NOT NULL,
                 'name' TEXT,
                 'flags' INTEGER,
                 'compression_type' TEXT,
                 'param_type' TEXT
                )";
            using (var cmd = new SQLiteCommand(createTable, con))
            {
                cmd.ExecuteNonQuery();
            }
        }

        private static void ReadBndTableOfContentsIntoDatabase(SQLiteConnection con, string filename, List<BndContentsEntry> bndContents)
        {
            var insert = @"INSERT INTO 'bnd_contents'
                ('source_file', 'file_id', 'Name', 'flags', 'compression_type', 'param_type')
                VALUES ($source_file, $file_id, $name, $flags, $compression_type, $param_type)";

            using (var cmd = new SQLiteCommand(insert, con))
            {
                foreach (BndContentsEntry entry in bndContents)
                {
                    AddParamToCommand(cmd, @"$source_file", filename);
                    AddParamToCommand(cmd, @"$file_id", entry.FileId);
                    AddParamToCommand(cmd, @"$name", entry.Name);
                    AddParamToCommand(cmd, @"$flags", entry.Flags);
                    AddParamToCommand(cmd, @"$compression_type", entry.CompressionType.ToString());
                    AddParamToCommand(cmd, @"$param_type", entry.ParamType);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        private static void ReadParamdefsIntoDatabase(SQLiteConnection con, List<PARAMDEF> paramdefs)
        {
            // Create the main table and the fields table
            var createTable = @"CREATE TABLE IF NOT EXISTS 'paramdef_metadata'
                (
                 'param_type' TEXT NOT NULL,
                 'big_endian' BOOLEAN,
                 'compression' TEXT,
                 'unicode' BOOLEAN,
                 'data_version' INTEGER,
                 'format_version' INTEGER
                )";
            using (var cmd = new SQLiteCommand(createTable, con))
            {
                cmd.ExecuteNonQuery();
            }

            createTable = @"CREATE TABLE IF NOT EXISTS 'paramdef_fields'
                (
                 'param_type' TEXT NOT NULL,
                 'internal_name' TEXT NOT NULL,
                 'internal_type' TEXT NOT NULL,
                 'array_length' INTEGER DEFAULT 1,
                 'bit_size' INTEGER DEFAULT -1,
                 'default' REAL,
                 'description' TEXT,
                 'edit_flags' INTEGER,
                 'increment' REAL,
                 'maximum' REAL,
                 'minimum' REAL,
                 'display_format' TEXT,
                 'display_name' TEXT,
                 'display_type' TEXT,
                 'sort_id' INTEGER
                )";
            using (var cmd = new SQLiteCommand(createTable, con))
            {
                cmd.ExecuteNonQuery();
            }

            var insertParamdef = @"INSERT INTO 'paramdef_metadata'
                ('param_type', 'big_endian', 'compression', 'unicode', 'data_version', 'format_version')
                VALUES ($param_type, $big_endian, $compression, $unicode, $data_version, $format_version)";

            var insertFields = @"INSERT INTO 'paramdef_fields'
                ('param_type', 'internal_name', 'internal_type', 'array_length', 'bit_size',
                 'default', 'description', 'edit_flags', 'increment', 'maximum',
                 'minimum', 'display_format', 'display_name', 'display_type', 'sort_id')
                VALUES ($param_type, $internal_name, $internal_type, $array_length, $bit_size,
                        $default, $description, $edit_flags, $increment, $maximum,
                        $minimum, $display_format, $display_name, $display_type, $sort_id)";

            using (var cmd = new SQLiteCommand(insertParamdef, con))
            using (var fieldsCmd = new SQLiteCommand(insertFields, con))
            {
                foreach (PARAMDEF paramdef in paramdefs)
                {
                    AddParamToCommand(cmd, @"$param_type", paramdef.ParamType);
                    AddParamToCommand(cmd, @"$big_endian", paramdef.BigEndian);
                    AddParamToCommand(cmd, @"$compression", paramdef.Compression.ToString());
                    AddParamToCommand(cmd, @"$unicode", paramdef.Unicode);
                    AddParamToCommand(cmd, @"$data_version", paramdef.DataVersion);
                    AddParamToCommand(cmd, @"$format_version", paramdef.FormatVersion);

                    cmd.ExecuteNonQuery();

                    foreach (Field field in paramdef.Fields)
                    {
                        AddParamToCommand(fieldsCmd, @"$param_type", paramdef.ParamType);
                        AddParamToCommand(fieldsCmd, @"$internal_name", field.InternalName);
                        AddParamToCommand(fieldsCmd, @"$internal_type", field.InternalType);
                        AddParamToCommand(fieldsCmd, @"$array_length", field.ArrayLength);
                        AddParamToCommand(fieldsCmd, @"$bit_size", field.BitSize);
                        AddParamToCommand(fieldsCmd, @"$default", field.Default);
                        AddParamToCommand(fieldsCmd, @"$description", field.Description);
                        AddParamToCommand(fieldsCmd, @"$edit_flags", field.EditFlags);
                        AddParamToCommand(fieldsCmd, @"$increment", field.Increment);
                        AddParamToCommand(fieldsCmd, @"$maximum", field.Maximum);
                        AddParamToCommand(fieldsCmd, @"$minimum", field.Minimum);
                        AddParamToCommand(fieldsCmd, @"$display_format", field.DisplayFormat);
                        AddParamToCommand(fieldsCmd, @"$display_name", field.DisplayName);
                        AddParamToCommand(fieldsCmd, @"$display_type", field.DisplayType);
                        AddParamToCommand(fieldsCmd, @"$sort_id", field.SortID);

                        fieldsCmd.ExecuteNonQuery();
                    }
                }
            }
        }

        private static void AddParamToCommand(SQLiteCommand cmd, string parameterName, object value)
        {
            var paramTypeParam = cmd.CreateParameter();
            paramTypeParam.ParameterName = parameterName;
            paramTypeParam.Value = value;
            cmd.Parameters.Add(paramTypeParam);
        }
    }
}
