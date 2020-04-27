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
        private readonly string conStr;

        private class BndContentsEntry
        {
            private readonly string sourceFile;
            private readonly int fileId;
            private readonly string name;
            private readonly Binder.FileFlags flags;
            private readonly DCX.Type compressionType;
            private readonly string paramType;

            public BndContentsEntry(string sourceFile, int fileId, string name, Binder.FileFlags flags, DCX.Type compressionType, string paramType)
            {
                this.sourceFile = sourceFile;
                this.fileId = fileId;
                this.name = name;
                this.flags = flags;
                this.compressionType = compressionType;
                this.paramType = paramType;
            }

            public string SourceFile => sourceFile;

            public int FileId => fileId;

            public string Name => name;

            public Binder.FileFlags Flags => flags;

            public DCX.Type CompressionType => compressionType;

            public string ParamType => paramType;
        }

        public ParamLoader(string conStr)
        {
            // If we don't do this, shift-jis won't work.
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            this.conStr = conStr;
        }

        public void LoadGameParams(string paramdefFilepath, IList<string> paramFilePaths)
        {
            // The param metadata tables should be created ahead of time.
            CreateParamMetadataTables();

            // Reading an original paramdefbnd
            var paramdefs = new Dictionary<string, PARAMDEF>();
            var paramdefbnd = BND3.Read(paramdefFilepath);
            foreach (BinderFile file in paramdefbnd.Files)
            {
                var paramdef = PARAMDEF.Read(file.Bytes);
                paramdefs[paramdef.ParamType] = paramdef;
            }
            ReadParamdefsIntoDatabase(paramdefs.Values.ToList());

            // Loading parambnd
            // Have to construct Table of Contents as we go through, since the info isn't all at BND level, but is needed when reconstructing
            var bndContents = new List<BndContentsEntry>();
            foreach (var paramFilepath in paramFilePaths)
            {
                var parambnd = BND3.Read(paramFilepath);
                //parambnd.Write(@"D:\downloads\DeS_Files\" + Path.GetFileName(paramFilepath) + ".old.dcx");
                //parambnd.Write(@"D:\downloads\DS_Files\" + Path.GetFileName(paramFilepath) + ".old.dcx");


                foreach (BinderFile file in parambnd.Files)
                {
                    PARAM param = PARAM.Read(file.Bytes);
                    param.ApplyParamdef(paramdefs[param.ParamType]);
                    var entry = new BndContentsEntry(paramFilepath, file.ID, file.Name, file.Flags, file.CompressionType, param.ParamType);
                    bndContents.Add(entry);
                    ReadParamIntoDatabase(Path.GetFileNameWithoutExtension(file.Name), param);
                }

                // Create the metadata tables
                ReadBndMetadataIntoDatabase(paramFilepath, parambnd);
                ReadBndTableOfContentsIntoDatabase(Path.GetFileName(paramFilepath), bndContents);
            }
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

        private void ReadBndMetadataIntoDatabase(String filename, BND3 bnd)
        {
            using (var con = GetConnection())
            {
                con.Open();

                // First thing to do is load up the actual bnd file metadata.
                using (var cmd = new SQLiteCommand(@"DROP TABLE IF EXISTS 'bnd_metadata'", con))
                {
                    cmd.ExecuteNonQuery();
                }

                Console.WriteLine(@"Creating table 'bnd_metadata'");
                var createTable = @"CREATE TABLE 'bnd_metadata'
                ('filename' TEXT,
                 'BigEndian' BOOLEAN,
                 'BitBigEndian' BOOLEAN,
                 'Compression' TEXT,
                 'Format' INTEGER,
                 'Unk18' INTEGER,
                 'Version' TEXT
                )";
                using (var cmd = new SQLiteCommand(createTable, con))
                {
                    cmd.ExecuteNonQuery();
                }

                var insert = @"INSERT INTO 'bnd_metadata'
                ('filename', 'BigEndian', 'BitBigEndian', 'Compression', 'Format', 'Unk18', 'Version')
                VALUES ($filename, $BigEndian, $BitBigEndian, $Compression, $Format, $Unk18, $Version)";

                using (var cmd = new SQLiteCommand(insert, con))
                {
                    AddParamToCommand(cmd, @"$filename", filename);
                    AddParamToCommand(cmd, @"$BigEndian", bnd.BigEndian);
                    AddParamToCommand(cmd, @"$BitBigEndian", bnd.BitBigEndian);
                    AddParamToCommand(cmd, @"$Compression", bnd.Compression.ToString());
                    AddParamToCommand(cmd, @"$Format", bnd.Format);
                    AddParamToCommand(cmd, @"$Unk18", bnd.Unk18);
                    AddParamToCommand(cmd, @"$Version", bnd.Version);

                    cmd.ExecuteNonQuery();
                }
            }
        }

        private void CreateParamMetadataTables()
        {
            using (var con = GetConnection())
            {
                con.Open();

                Console.WriteLine("Dropping table: param_metadata");
                using (var cmd = new SQLiteCommand(@"DROP TABLE IF EXISTS 'param_metadata'", con))
                {
                    cmd.ExecuteNonQuery();
                }

                Console.WriteLine("Creating table: param_metadata");
                var createTable = @"CREATE TABLE IF NOT EXISTS 'param_metadata' 
                (
                 'ParamType' TEXT NOT NULL,
                 'BigEndian' BOOLEAN,
                 'Compression' TEXT,
                 'Format2D' BLOB,
                 'Format2E' BLOB,
                 'Format2F' BLOB,
                 'Unk06' INTEGER,
                 'Unk08' INTEGER
                )";
                using (var cmd = new SQLiteCommand(createTable, con))
                {
                    cmd.ExecuteNonQuery();
                }
            }
        }
        private void ReadParamIntoDatabase(string filename, PARAM paramFile)
        {
            Console.WriteLine();

            using (var con = GetConnection())
            {
                con.Open();

                var insert = @"INSERT INTO 'param_metadata'
                ('ParamType', 'BigEndian', 'Compression', 'Format2D', 'Format2E', 'Format2F', 'Unk06', 'Unk08')
                VALUES ($ParamType, $BigEndian, $Compression, $Format2D, $Format2E, $Format2F, $Unk06, $Unk08)";
                using (var cmd = new SQLiteCommand(insert, con))
                {
                    AddParamToCommand(cmd, @"$ParamType", paramFile.AppliedParamdef.ParamType);
                    AddParamToCommand(cmd, @"$BigEndian", paramFile.BigEndian);
                    AddParamToCommand(cmd, @"$Compression", paramFile.Compression.ToString());
                    AddParamToCommand(cmd, @"$Format2D", new byte[] { paramFile.Format2D });
                    AddParamToCommand(cmd, @"$Format2E", new byte[] { paramFile.Format2E });
                    AddParamToCommand(cmd, @"$Format2F", new byte[] { paramFile.Format2F });
                    AddParamToCommand(cmd, @"$Unk06", paramFile.Unk06);
                    AddParamToCommand(cmd, @"$Unk08", paramFile.Unk08);
                    cmd.ExecuteNonQuery();
                }

                Console.WriteLine("Dropping table: " + filename);
                using (var cmd = new SQLiteCommand(@"DROP TABLE IF EXISTS '" + filename + "'", con))
                {
                    cmd.ExecuteNonQuery();
                }

                Console.WriteLine("Creating table: " + filename);
                var sb = new StringBuilder();
                sb.Append(@"CREATE TABLE '");
                sb.Append(filename);
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
                        //case DefType.dummy8: // Text types. Include dummy.
                        //    sb.Append(@"BLOB");
                        //    break;
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
                sb.Append(@"Description TEXT");
                sb.Append(@");");

                using (var cmd = new SQLiteCommand(sb.ToString(), con))
                {
                    cmd.ExecuteNonQuery();
                }

                // Actually insert our data
                // Yes, we could merge some of these loops. But this is small data, so I don't care, this is clearer.
                sb.Clear();
                sb.Append(@"INSERT INTO '");
                sb.Append(filename);
                sb.Append(@"' (id,");
                foreach (Field field in realFields)
                {
                    sb.Append(field.InternalName);
                    sb.Append(@",");
                }
                sb.Append(@"Description");
                sb.Append(@") VALUES($id,");
                foreach (Field field in realFields)
                {
                    sb.Append(@"$");
                    sb.Append(field.InternalName);
                    sb.Append(@",");
                }
                sb.Append(@"$Description");
                sb.Append(@");");
                using (var transaction = con.BeginTransaction())
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
                    descParam.ParameterName = @"$Description";
                    cmd.Parameters.Add(descParam);

                    foreach (Row row in paramFile.Rows)
                    {
                        idParam.Value = row.ID;
                        // At least DeS TALK_PARAM_ST has id -1. However, SoulsFormat uses unsigned for IDs, so it rolls over.
                        if (row.ID == 4294967295)
                        {
                            idParam.Value = -1;
                        }
                        foreach (Field field in realFields)
                        {
                            var param = fieldDict[field.InternalName];
                            param.Value = row[field.InternalName].Value ?? DBNull.Value;
                        }
                        descParam.Value = row.Name;
                        cmd.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
        }

        private void ReadBndTableOfContentsIntoDatabase(string filename, List<BndContentsEntry> bndContents)
        {
            using (var con = GetConnection())
            {
                con.Open();

                // First thing to do is load up the actual bnd file metadata.
                using (var cmd = new SQLiteCommand(@"DROP TABLE IF EXISTS 'bnd_contents'", con))
                {
                    cmd.ExecuteNonQuery();
                }

                Console.WriteLine(@"Creating table 'bnd_contents'");
                var createTable = @"CREATE TABLE 'bnd_contents'
                (
                 'source_file' TEXT NOT NULL,
                 'file_id' INTEGER NOT NULL,
                 'Name' TEXT,
                 'Flags' INTEGER,
                 'CompressionType' TEXT,
                 'ParamType' TEXT
                )";
                using (var cmd = new SQLiteCommand(createTable, con))
                {
                    cmd.ExecuteNonQuery();
                }

                var insert = @"INSERT INTO 'bnd_contents'
                ('source_file', 'file_id', 'Name', 'Flags', 'CompressionType', 'ParamType')
                VALUES ($source_file, $file_id, $Name, $Flags, $CompressionType, $ParamType)";

                using (var transaction = con.BeginTransaction())
                using (var cmd = new SQLiteCommand(insert, con))
                {
                    foreach (BndContentsEntry entry in bndContents)
                    {
                        AddParamToCommand(cmd, @"$source_file", filename);
                        AddParamToCommand(cmd, @"$file_id", entry.FileId);
                        AddParamToCommand(cmd, @"$Name", entry.Name);
                        AddParamToCommand(cmd, @"$Flags", entry.Flags);
                        AddParamToCommand(cmd, @"$CompressionType", entry.CompressionType.ToString());
                        AddParamToCommand(cmd, @"$ParamType", entry.ParamType);

                        cmd.ExecuteNonQuery();
                    }
                    transaction.Commit();
                }
            }
        }

        private void ReadParamdefsIntoDatabase(List<PARAMDEF> paramdefs)
        {
            using (var con = GetConnection())
            {
                con.Open();

                using (var cmd = new SQLiteCommand(@"DROP TABLE IF EXISTS 'paramdef_metadata'", con))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new SQLiteCommand(@"DROP TABLE IF EXISTS 'paramdef_fields'", con))
                {
                    cmd.ExecuteNonQuery();
                }

                // Create the main table and the fields table
                Console.WriteLine(@"Creating table 'paramdef_metadata'");
                var createTable = @"CREATE TABLE 'paramdef_metadata'
                (
                 'ParamType' TEXT NOT NULL,
                 'BigEndian' BOOLEAN,
                 'Compression' TEXT,
                 'Unicode' BOOLEAN,
                 'Unk06' INTEGER,
                 'Version' INTEGER
                )";
                using (var cmd = new SQLiteCommand(createTable, con))
                {
                    cmd.ExecuteNonQuery();
                }
                createTable = @"CREATE TABLE 'paramdef_fields'
                (
                 'ParamType' TEXT NOT NULL,
                 'InternalName' TEXT NOT NULL,
                 'InternalType' TEXT NOT NULL,
                 'ArrayLength' INTEGER DEFAULT 1,
                 'BitSize' INTEGER DEFAULT -1,
                 'Default' REAL,
                 'Description' TEXT,
                 'EditFlags' INTEGER,
                 'Increment' REAL,
                 'Maximum' REAL,
                 'Minimum' REAL,
                 'DisplayFormat' TEXT,
                 'DisplayName' TEXT,
                 'DisplayType' TEXT,
                 'SortID' INTEGER
                )";
                using (var cmd = new SQLiteCommand(createTable, con))
                {
                    cmd.ExecuteNonQuery();
                }

                var insertParamdef = @"INSERT INTO 'paramdef_metadata'
                ('ParamType', 'BigEndian', 'Compression', 'Unicode', 'Unk06', 'Version')
                VALUES ($ParamType, $BigEndian, $Compression, $Unicode, $Unk06, $Version)";

                var insertFields = @"INSERT INTO 'paramdef_fields'
                ('ParamType', 'InternalName', 'InternalType', 'ArrayLength', 'BitSize',
                 'Default', 'Description', 'EditFlags', 'Increment', 'Maximum',
                 'Minimum', 'DisplayFormat', 'DisplayName', 'DisplayType', 'SortID')
                VALUES ($ParamType, $InternalName, $InternalType, $ArrayLength, $BitSize,
                        $Default, $Description, $EditFlags, $Increment, $Maximum,
                        $Minimum, $DisplayFormat, $DisplayName, $DisplayType, $SortID)";

                using (var transaction = con.BeginTransaction())
                using (var cmd = new SQLiteCommand(insertParamdef, con))
                using (var fieldsCmd = new SQLiteCommand(insertFields, con))
                {
                    foreach (PARAMDEF paramdef in paramdefs)
                    {
                        AddParamToCommand(cmd, @"$ParamType", paramdef.ParamType);
                        AddParamToCommand(cmd, @"$BigEndian", paramdef.BigEndian);
                        AddParamToCommand(cmd, @"$Compression", paramdef.Compression.ToString());
                        AddParamToCommand(cmd, @"$Unicode", paramdef.Unicode);
                        AddParamToCommand(cmd, @"$Unk06", paramdef.Unk06);
                        AddParamToCommand(cmd, @"$Version", paramdef.Version);

                        cmd.ExecuteNonQuery();

                        foreach (Field field in paramdef.Fields)
                        {
                            AddParamToCommand(fieldsCmd, @"$ParamType", paramdef.ParamType);
                            AddParamToCommand(fieldsCmd, @"$InternalName", field.InternalName);
                            AddParamToCommand(fieldsCmd, @"$InternalType", field.InternalType);
                            AddParamToCommand(fieldsCmd, @"$ArrayLength", field.ArrayLength);
                            AddParamToCommand(fieldsCmd, @"$BitSize", field.BitSize);
                            AddParamToCommand(fieldsCmd, @"$Default", field.Default);
                            AddParamToCommand(fieldsCmd, @"$Description", field.Description);
                            AddParamToCommand(fieldsCmd, @"$EditFlags", field.EditFlags);
                            AddParamToCommand(fieldsCmd, @"$Increment", field.Increment);
                            AddParamToCommand(fieldsCmd, @"$Maximum", field.Maximum);
                            AddParamToCommand(fieldsCmd, @"$Minimum", field.Minimum);
                            AddParamToCommand(fieldsCmd, @"$DisplayFormat", field.DisplayFormat);
                            AddParamToCommand(fieldsCmd, @"$DisplayName", field.DisplayName);
                            AddParamToCommand(fieldsCmd, @"$DisplayType", field.DisplayType);
                            AddParamToCommand(fieldsCmd, @"$SortID", field.SortID);

                            fieldsCmd.ExecuteNonQuery();
                        }
                    }
                    transaction.Commit();
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
