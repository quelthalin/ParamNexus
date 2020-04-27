using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Text;
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
                        BigEndian = reader.GetBoolean(reader.GetOrdinal(@"BigEndian")),
                        BitBigEndian = reader.GetBoolean(reader.GetOrdinal(@"BitBigEndian")),
                        Compression = (DCX.Type)Enum.Parse(typeof(DCX.Type), reader.GetString(reader.GetOrdinal(@"Compression"))),
                        Format = (Binder.Format)reader.GetInt64(reader.GetOrdinal(@"Format")),
                        Unk18 = reader.GetInt32(reader.GetOrdinal(@"Unk18")),
                        Version = reader.GetString(reader.GetOrdinal(@"Version")),
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
                        Name = reader.GetString(reader.GetOrdinal(@"Name")),
                        Flags = (Binder.FileFlags)reader.GetInt64(reader.GetOrdinal(@"Flags")),
                        CompressionType = (DCX.Type)System.Enum.Parse(typeof(DCX.Type), reader.GetString(reader.GetOrdinal(@"CompressionType")))
                    };

                    var paramType = reader.GetString(reader.GetOrdinal("ParamType"));

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
            using (var fieldsCmd = new SQLiteCommand(@"SELECT * FROM 'paramdef_fields' WHERE ParamType=$ParamType;", con))
            {
                var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    PARAMDEF paramdef = new PARAMDEF
                    {
                        BigEndian = reader.GetBoolean(reader.GetOrdinal(@"BigEndian")),
                        Compression = (DCX.Type)Enum.Parse(typeof(DCX.Type), reader.GetString(reader.GetOrdinal(@"Compression"))),
                        ParamType = reader.GetString(reader.GetOrdinal(@"ParamType")),
                        Unicode = reader.GetBoolean(reader.GetOrdinal(@"Unicode")),
                        Unk06 = reader.GetInt16(reader.GetOrdinal(@"Unk06")),
                        Version = reader.GetInt16(reader.GetOrdinal(@"Version"))
                    };
                    paramTypeToParamDef.Add(paramdef.ParamType, paramdef);
                }
            }

            using (var cmd = new SQLiteCommand(@"SELECT * FROM 'paramdef_fields' WHERE ParamType=$ParamType;", con))
            {
                foreach (KeyValuePair<string, PARAMDEF> keyValue in paramTypeToParamDef)
                {
                    // Get all the fields for our paramdef
                    AddParamToCommand(cmd, @"$ParamType", keyValue.Key);
                    var fieldReader = cmd.ExecuteReader();
                    var fields = new List<Field>();
                    while (fieldReader.Read())
                    {
                        var descOrdinal = fieldReader.GetOrdinal(@"Description");
                        var field = new Field
                        {
                            ArrayLength = fieldReader.GetInt32(fieldReader.GetOrdinal(@"ArrayLength")),
                            BitSize = fieldReader.GetInt32(fieldReader.GetOrdinal(@"BitSize")),
                            Default = fieldReader.GetFloat(fieldReader.GetOrdinal(@"Default")),
                            // Description can be NULL. Need to check. Sigh.
                            Description = fieldReader.IsDBNull(descOrdinal) ? null : fieldReader.GetFieldValue<string>(descOrdinal),
                            DisplayFormat = fieldReader.GetString(fieldReader.GetOrdinal(@"DisplayFormat")),
                            DisplayName = fieldReader.GetString(fieldReader.GetOrdinal(@"DisplayName")),
                            DisplayType = (DefType)System.Enum.Parse(typeof(DefType), fieldReader.GetString(fieldReader.GetOrdinal(@"DisplayType"))),
                            EditFlags = (EditFlags)fieldReader.GetInt64(fieldReader.GetOrdinal(@"EditFlags")),
                            Increment = fieldReader.GetFloat(fieldReader.GetOrdinal(@"Increment")),
                            InternalName = fieldReader.GetString(fieldReader.GetOrdinal(@"InternalName")),
                            InternalType = fieldReader.GetString(fieldReader.GetOrdinal(@"InternalType")),
                            Maximum = fieldReader.GetFloat(fieldReader.GetOrdinal(@"Maximum")),
                            Minimum = fieldReader.GetFloat(fieldReader.GetOrdinal(@"Minimum")),
                            SortID = fieldReader.GetInt32(fieldReader.GetOrdinal(@"SortID"))
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
                    using (var metadataCmd = new SQLiteCommand(@"SELECT * FROM param_metadata WHERE ParamType = $ParamType", con))
                    {

                        var paramDef = paramTypeToParamDef[entry.Value.Key];
                        var paramFile = new PARAM();
                        paramFile.ParamType = entry.Value.Key;

                        AddParamToCommand(metadataCmd, @"$ParamType", entry.Value.Key);
                        var metadataReader = metadataCmd.ExecuteReader();
                        while (metadataReader.Read())
                        {
                            paramFile.BigEndian = metadataReader.GetBoolean(metadataReader.GetOrdinal(@"BigEndian"));
                            paramFile.Compression = (DCX.Type)System.Enum.Parse(typeof(DCX.Type), metadataReader.GetString(metadataReader.GetOrdinal(@"Compression")));
                            byte[] buf = new byte[1]; // These 3 cols are all 1 byte
                            metadataReader.GetBytes(metadataReader.GetOrdinal("Format2D"), 0, buf, 0, 1); // Can't use GetBlob
                            paramFile.Format2D = buf[0];
                            buf = new byte[1];
                            metadataReader.GetBytes(metadataReader.GetOrdinal("Format2E"), 0, buf, 0, 1);
                            paramFile.Format2E = buf[0];
                            buf = new byte[1];
                            metadataReader.GetBytes(metadataReader.GetOrdinal("Format2F"), 0, buf, 0, 1);
                            paramFile.Format2F = buf[0];
                            paramFile.Unk06 = metadataReader.GetInt16(metadataReader.GetOrdinal(@"Unk06"));
                            paramFile.Unk08 = metadataReader.GetInt16(metadataReader.GetOrdinal(@"Unk08"));
                        }

                        var reader = cmd.ExecuteReader();
                        paramFile.Rows = new List<PARAM.Row>();
                        while (reader.Read())
                        {
                            var id = reader.GetInt32(reader.GetOrdinal(@"id"));
                            // TalkParamNA for some reason gets some absurd id values. No idea what's up there.
                            // I think SoulsFormats might be treating it a
                            if (id > 1000000 || id < -1)
                            {
                                id = -1;
                            }
                            // Description can be NULL
                            var descOrdinal = reader.GetOrdinal(@"Description");
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

            Console.WriteLine("Approaching actual write");
            foreach (KeyValuePair<string, KeyValuePair<string, BND3>> entry in bnds)
            {
                Console.WriteLine("made it to actual write");
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
