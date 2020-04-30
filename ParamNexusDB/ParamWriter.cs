using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using SoulsFormats;
using static SoulsFormats.PARAMDEF;

namespace ParamNexusDB
{
    public class ParamWriter
    {
        public static void WriteParams(SQLiteConnection con, string outputPath, bool overwriteOutputFiles)
        {
            // Writing a parambnd
            // Need to construct our BND3 files based on what's in our DB.
            // This is a kludge to create a mapping (filename -> (source_path, BND)).
            var bnds = new Dictionary<string, KeyValuePair<string, BND3>>();
            // First thing to do is get our basic BND file setup.
            using (var cmd = new SQLiteCommand(@"SELECT * FROM 'bnd_metadata'", con))
            {
                var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var bnd = new BND3
                    {
                        BigEndian = reader.GetBoolean(reader.GetOrdinal(@"big_endian")),
                        BitBigEndian = reader.GetBoolean(reader.GetOrdinal(@"bit_big_endian")),
                        Compression = (DCX.Type)Enum.Parse(typeof(DCX.Type), reader.GetString(reader.GetOrdinal(@"compression"))),
                        Format = (Binder.Format)reader.GetInt64(reader.GetOrdinal(@"format")),
                        Unk18 = reader.GetInt32(reader.GetOrdinal(@"unk18")),
                        Version = reader.GetString(reader.GetOrdinal(@"version")),
                        Files = new List<BinderFile>()
                    };
                    var filename = reader.GetString(reader.GetOrdinal(@"filename"));
                    bnds.Add(Path.GetFileName(filename), new KeyValuePair<string, BND3>(filename, bnd));
                }
            }

            // Get our list of files. We'll grab the contents afterwards.
            // Note that it's a List because there can be multiple files associated with a given ParamType.
            var files = new Dictionary<string, KeyValuePair<string, List<BinderFile>>>();
            using (var cmd = new SQLiteCommand(@"SELECT * FROM 'bnd_contents'", con))
            {
                var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var source_file = reader.GetString(reader.GetOrdinal(@"source_file"));
                    var file = new BinderFile
                    {
                        ID = reader.GetInt32(reader.GetOrdinal(@"file_id")),
                        Name = reader.GetString(reader.GetOrdinal(@"name")),
                        Flags = (Binder.FileFlags)reader.GetInt64(reader.GetOrdinal(@"flags")),
                        CompressionType = (DCX.Type)System.Enum.Parse(typeof(DCX.Type), reader.GetString(reader.GetOrdinal(@"compression_type")))
                    };

                    var paramType = reader.GetString(reader.GetOrdinal("param_type"));

                    // Add the file to both our list of files in the appropriate BND and also to our dictionary
                    // so that we can continue building it out.
                    bnds[source_file].Value.Files.Add(file);
                    if (files.ContainsKey(Path.GetFileNameWithoutExtension(file.Name)))
                    {
                        var dictValue = files.TryGetValue(Path.GetFileNameWithoutExtension(file.Name), out KeyValuePair<string, List<BinderFile>> value) ? value : 
                            new KeyValuePair<string, List<BinderFile>>(paramType, new List<BinderFile>());
                        dictValue.Value.Add(file);
                    }
                    else
                    {
                        var dictValue = new KeyValuePair<string, List<BinderFile>>(paramType, new List<BinderFile>() { file });
                        files.Add(Path.GetFileNameWithoutExtension(file.Name), dictValue);
                    }
                }
            }

            // Get all of our PARAMDEFs
            Dictionary<string, PARAMDEF> paramTypeToParamDef = new Dictionary<string, PARAMDEF>();
            using (var cmd = new SQLiteCommand(@"SELECT * FROM 'paramdef_metadata';", con))
            using (var fieldsCmd = new SQLiteCommand(@"SELECT * FROM 'paramdef_fields' WHERE param_type=$param_type;", con))
            {
                var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    PARAMDEF paramdef = new PARAMDEF
                    {
                        BigEndian = reader.GetBoolean(reader.GetOrdinal(@"big_endian")),
                        Compression = (DCX.Type)Enum.Parse(typeof(DCX.Type), reader.GetString(reader.GetOrdinal(@"compression"))),
                        ParamType = reader.GetString(reader.GetOrdinal(@"param_type")),
                        Unicode = reader.GetBoolean(reader.GetOrdinal(@"unicode")),
                        DataVersion = reader.GetInt16(reader.GetOrdinal(@"data_version")),
                        FormatVersion = reader.GetInt16(reader.GetOrdinal(@"format_version"))
                    };
                    paramTypeToParamDef.Add(paramdef.ParamType, paramdef);
                }
            }

            using (var cmd = new SQLiteCommand(@"SELECT * FROM 'paramdef_fields' WHERE param_type=$param_type;", con))
            {
                foreach (KeyValuePair<string, PARAMDEF> keyValue in paramTypeToParamDef)
                {
                    // Get all the fields for our paramdef
                    AddParamToCommand(cmd, @"$param_type", keyValue.Key);
                    var fieldReader = cmd.ExecuteReader();
                    var fields = new List<Field>();
                    while (fieldReader.Read())
                    {
                        var descOrdinal = fieldReader.GetOrdinal(@"description");
                        var field = new Field
                        {
                            ArrayLength = fieldReader.GetInt32(fieldReader.GetOrdinal(@"array_length")),
                            BitSize = fieldReader.GetInt32(fieldReader.GetOrdinal(@"bit_size")),
                            Default = fieldReader.GetFloat(fieldReader.GetOrdinal(@"default")),
                            // Description can be NULL. Need to check. Sigh.
                            Description = fieldReader.IsDBNull(descOrdinal) ? null : fieldReader.GetFieldValue<string>(descOrdinal),
                            DisplayFormat = fieldReader.GetString(fieldReader.GetOrdinal(@"display_format")),
                            DisplayName = fieldReader.GetString(fieldReader.GetOrdinal(@"display_name")),
                            DisplayType = (DefType)System.Enum.Parse(typeof(DefType), fieldReader.GetString(fieldReader.GetOrdinal(@"display_type"))),
                            EditFlags = (EditFlags)fieldReader.GetInt64(fieldReader.GetOrdinal(@"edit_flags")),
                            Increment = fieldReader.GetFloat(fieldReader.GetOrdinal(@"increment")),
                            InternalName = fieldReader.GetString(fieldReader.GetOrdinal(@"internal_name")),
                            InternalType = fieldReader.GetString(fieldReader.GetOrdinal(@"internal_type")),
                            Maximum = fieldReader.GetFloat(fieldReader.GetOrdinal(@"maximum")),
                            Minimum = fieldReader.GetFloat(fieldReader.GetOrdinal(@"minimum")),
                            SortID = fieldReader.GetInt32(fieldReader.GetOrdinal(@"sort_id"))
                        };

                        fields.Add(field);
                    }
                    keyValue.Value.Fields = fields;
                    var exc = new Exception();
                    if (!keyValue.Value.Validate(out exc))
                    {
                        throw exc;
                    }
                    fieldReader.Close();
                }
            }

            // Now we need to grab our contents for each file.
            foreach (KeyValuePair<string, KeyValuePair<string, List<BinderFile>>> entry in files)
            {
                // Want to iterate through each file. Keep in mind multiple tables can have same ParamType, so we can't loop via ParamType.
                // e.g. DeS AtkParam_Npc and AtkParam_Pc
                foreach (BinderFile file in entry.Value.Value)
                {
                    //var tableName = Path.GetFileNameWithoutExtension(file.Name);
                    var tableName = entry.Key;
                    Console.WriteLine("Reading from: " + tableName);
                    using (var cmd = new SQLiteCommand(@"SELECT * FROM '" + tableName + "';", con))
                    using (var metadataCmd = new SQLiteCommand(@"SELECT * FROM param_metadata WHERE param_type = $param_type", con))
                    {
                        var paramDef = paramTypeToParamDef[entry.Value.Key];
                        var paramFile = new PARAM();
                        paramFile.ParamType = entry.Value.Key;

                        if (entry.Value.Key == "AI_STANDARD_INFO_BANK")
                        {
                            Console.WriteLine("Standard AI bank");
                        }

                        AddParamToCommand(metadataCmd, @"$param_type", entry.Value.Key);
                        var metadataReader = metadataCmd.ExecuteReader();
                        while (metadataReader.Read())
                        {
                            paramFile.BigEndian = metadataReader.GetBoolean(metadataReader.GetOrdinal(@"big_endian"));
                            paramFile.Compression = (DCX.Type)Enum.Parse(typeof(DCX.Type), metadataReader.GetString(metadataReader.GetOrdinal(@"compression")));
                            paramFile.Format2D = (PARAM.FormatFlags1)Enum.Parse(typeof(PARAM.FormatFlags1), metadataReader.GetString(metadataReader.GetOrdinal(@"format2d")));
                            paramFile.Format2E = (PARAM.FormatFlags2)Enum.Parse(typeof(PARAM.FormatFlags2), metadataReader.GetString(metadataReader.GetOrdinal(@"format2e")));
                            byte[] buf = new byte[1];
                            metadataReader.GetBytes(metadataReader.GetOrdinal("paramdef_format_version"), 0, buf, 0, 1);
                            paramFile.ParamdefFormatVersion = buf[0];
                            paramFile.Unk06 = metadataReader.GetInt16(metadataReader.GetOrdinal(@"unk06"));
                            paramFile.ParamdefDataVersion = metadataReader.GetInt16(metadataReader.GetOrdinal(@"paramdef_data_version"));
                        }

                        var reader = cmd.ExecuteReader();
                        paramFile.Rows = new List<PARAM.Row>();
                        while (reader.Read())
                        {
                            var id = reader.GetInt32(reader.GetOrdinal(@"id"));
                            if (id == -1)
                            {
                                Console.WriteLine(@"Ignoring id of -1 in " + tableName);
                                continue;
                            }
                            // Description can be NULL
                            var descOrdinal = reader.GetOrdinal(@"description");
                            var description = reader.IsDBNull(descOrdinal) ? null : reader.GetFieldValue<string>(descOrdinal);
                            var row = new PARAM.Row(id, description, paramDef);
                            paramFile.Rows.Add(row);
                            foreach (Field field in paramDef.Fields)
                            {
                                var name = field.InternalName;
                                // Not using InternalType. I don't know the complete set of raw strings across all games.
                                // It would be better to use not be tied to a display field. For some value of "better".
                                var type = field.DisplayType;

                                switch (type)
                                {
                                    // Padding case
                                    case DefType.dummy8:
                                        int length = field.ArrayLength;
                                        if (field.BitSize == -1)
                                        {
                                            paramFile[id][name].Value = Enumerable.Repeat((byte)0, length).ToArray();
                                        }
                                        else
                                        {
                                            paramFile[id][name].Value = 0;
                                        }
                                        break;
                                    // All the integer cases
                                    case DefType.s8:
                                    case DefType.s16:
                                    case DefType.s32:
                                    case DefType.u8:
                                    case DefType.u16:
                                    case DefType.u32:
                                        paramFile[id][name].Value = reader.GetInt32(reader.GetOrdinal(name));
                                        break;
                                    // Float cases
                                    case DefType.f32:
                                        paramFile[id][name].Value = reader.GetFloat(reader.GetOrdinal(name));
                                        break;
                                    // String case
                                    case DefType.fixstr:
                                    case DefType.fixstrW:
                                        paramFile[id][name].Value = reader.GetString(reader.GetOrdinal(name));
                                        break;
                                }
                            }
                        }

                        // Don't apply carefully. We don't have the ability to set the DetectedSize. It only occurs on Read
                        paramFile.ApplyParamdef(paramDef);
                        var exc = new Exception();
                        if (!paramFile.Validate(out exc))
                        {
                            Console.WriteLine("Failed with exception: " + exc);
                        }

                        file.Bytes = paramFile.Write();
                    }
                }
            }

            foreach (KeyValuePair<string, KeyValuePair<string, BND3>> entry in bnds)
            {
                // Default to writing the original file.
                // If output path is defined, put everything there.
                var outputFile = entry.Value.Key;
                if (outputPath != null)
                {
                    outputFile = outputPath + Path.DirectorySeparatorChar + entry.Key;
                    Console.WriteLine("Output current parambnd.dcx: " + outputFile);
                }

                if (!File.Exists(outputFile) || overwriteOutputFiles)
                {
                    entry.Value.Value.Write(outputFile);
                }
                else
                {
                    // Backup the eisting file before writing.
                    // Just append the unix time and ".bak" to avoid managing whole sets of backup nonsense.
                    var unixTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    string backupFile = outputFile + "." + unixTime + ".bak";
                    Console.WriteLine("Collision found. Not overwriting. Moving original file to backup at: " + backupFile);
                    File.Move(outputFile, backupFile);
                    entry.Value.Value.Write(outputFile);
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
