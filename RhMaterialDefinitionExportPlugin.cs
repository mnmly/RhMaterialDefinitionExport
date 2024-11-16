using System;
using Rhino;
using Rhino.FileIO;
using Rhino.PlugIns;
using System.IO;
using System.Xml;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MNML
{
    ///<summary>
    /// <para>Every RhinoCommon .rhp assembly must have one and only one PlugIn-derived
    /// class. DO NOT create instances of this class yourself. It is the
    /// responsibility of Rhino to create an instance of this class.</para>
    /// <para>To complete plug-in information, please also see all PlugInDescription
    /// attributes in AssemblyInfo.cs (you might need to click "Project" ->
    /// "Show All Files" to see it in the "Solution Explorer" window).</para>
    ///</summary>
    public class RhMaterialDefinitionExportPlugin : FileExportPlugIn
    {
        public RhMaterialDefinitionExportPlugin()
        {
            Instance = this;
        }

        ///<summary>Gets the only instance of the RhMaterialDefinitionExportPlugin plug-in.</summary>
        public static RhMaterialDefinitionExportPlugin Instance { get; private set; }

        /// <summary>Defines file extensions that this export plug-in is designed to write.</summary>
        /// <param name="options">Options that specify how to write files.</param>
        /// <returns>A list of file types that can be exported.</returns>
        protected override FileTypeList AddFileTypes(FileWriteOptions options)
        {
            var result = new FileTypeList();
            result.AddFileType("Material Definition (json)", "json");
            return result;
        }


        // You can override methods here to change the plug-in behavior on
        // loading and shut down, add options pages to the Rhino _Option command
        // and maintain plug-in wide options in a document.
        /// <summary>
		/// Is called when a user requests to export a ".udatasmith" file.
		/// It is actually up to this method to write the file itself.
		/// </summary>
		/// <param name="Filename">The complete path to the new file.</param>
		/// <param name="Index">The index of the file type as it had been specified by the AddFileTypes method.</param>
		/// <param name="RhinoDocument">The document to be written.</param>
		/// <param name="Options">Options that specify how to write file.</param>
		/// <returns>A value that defines success or a specific failure.</returns>
		protected override WriteFileResult WriteFile(string Filename, int Index, RhinoDoc RhinoDocument, FileWriteOptions Options)
        {
            try
            {
                var materialsArray = new JsonArray();

                foreach (var m in RhinoDocument.RenderMaterials)
                {
                    var value = JsonNode.Parse(XmlToJsonConverter.Convert(m.Xml));
                    var materialObj = new JsonObject
                    {
                        ["name"] = m.Name,
                        ["value"] = value
                    };

                    if (value?["parameters"]?["plugin-content"] != null)
                    {
                        string pluginContentRaw = value["parameters"]["plugin-content"].GetValue<string>();
                        if (pluginContentRaw.Length > 6)
                        {
                            try
                            {
                                // Remove the first 7 characters and decode base64
                                string base64Content = pluginContentRaw.Substring(7);
                                byte[] decodedBytes = Convert.FromBase64String(base64Content);
                                string decodedJson = Encoding.UTF8.GetString(decodedBytes);

                                // Parse the decoded JSON and add it to the material object
                                var pluginContentNode = JsonNode.Parse(decodedJson);
                                materialObj["plugin-content"] = pluginContentNode;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error processing material {m.Name}: {ex.Message}");
                            }
                        }
                    }
                    materialsArray.Add(materialObj);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string serialisedMaterialDefinitions = JsonSerializer.Serialize(materialsArray, options);

                using (StreamWriter writer = new StreamWriter(Filename))
                {
                    writer.Write(serialisedMaterialDefinitions);
                }
                RhinoApp.WriteLine($"File successfully saved to: {Filename}");
                return WriteFileResult.Success;
            }
            catch (Exception ex)
            {
                // Handle any errors
                RhinoApp.WriteLine($"Error saving file: {ex.Message}");
                return WriteFileResult.Failure;
            }
        }
    }

    public class XmlToJsonConverter
    {
        public static string Convert(string xml)
        {
            // Load XML
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(xml);
            // Convert to JSON
            var jsonNode = ConvertXmlNodeToJsonNode(xmlDoc.DocumentElement);
            // Serialize to JSON string with formatting
            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(jsonNode, options);
        }

        private static JsonNode ConvertXmlNodeToJsonNode(XmlNode node)
        {
            // Handle different node types
            switch (node.NodeType)
            {
                case XmlNodeType.Element:
                    var obj = new JsonObject();

                    // Handle attributes
                    if (node.Attributes != null)
                    {
                        foreach (XmlAttribute attr in node.Attributes)
                        {
                            obj[$"@{attr.Name}"] = JsonValue.Create(attr.Value);
                        }
                    }

                    // Handle child nodes
                    if (node.HasChildNodes)
                    {
                        var childElements = node.ChildNodes.Cast<XmlNode>()
                            .Where(n => n.NodeType == XmlNodeType.Element)
                            .ToList();

                        if (childElements.Count > 0)
                        {
                            // Group similar elements
                            var groups = childElements.GroupBy(n => n.Name);
                            foreach (var group in groups)
                            {
                                if (group.Count() > 1)
                                {
                                    // Create array for multiple elements with same name
                                    var array = new JsonArray();
                                    foreach (var element in group)
                                    {
                                        array.Add(ConvertXmlNodeToJsonNode(element));
                                    }
                                    obj[group.Key] = array;
                                }
                                else
                                {
                                    obj[group.Key] = ConvertXmlNodeToJsonNode(group.First());
                                }
                            }
                        }
                        else if (node.FirstChild?.NodeType == XmlNodeType.Text)
                        {
                            // Handle text content
                            return JsonValue.Create(node.FirstChild.Value);
                        }
                    }
                    return obj;

                case XmlNodeType.Text:
                    return JsonValue.Create(node.Value);

                default:
                    return null;
            }
        }
    }
}