using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace CLTimeAssigner
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                ShowUsage();
                return;
            }

            string filePath = null;
            string outputFilePath = null;

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-o" && i + 1 < args.Length)
                {
                    outputFilePath = args[i + 1];
                    i++; // Skip the next argument as it is the output file path
                }
                else
                {
                    filePath = args[i];
                }
            }

            if (string.IsNullOrEmpty(filePath) || Path.GetExtension(filePath).ToLower() != ".json")
            {
                Console.WriteLine("Please drop a .json file.");
                return;
            }

            string jsonString = File.ReadAllText(filePath);
            dynamic root = JsonConvert.DeserializeObject<ExpandoObject>(jsonString, new ExpandoObjectConverter());

            foreach (var node in root.children)
            {
                SheetCalcTime(node);
            }

            var settings = new JsonSerializerSettings
            {
                Formatting = Formatting.Indented,
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = new CamelCaseNamingStrategy()
                }
            };
            string outputJson = JsonConvert.SerializeObject(root, settings);

            if (string.IsNullOrEmpty(outputFilePath))
            {
                string suffix = "assigned";
                outputFilePath = Path.Combine(Path.GetDirectoryName(filePath), Path.GetFileNameWithoutExtension(filePath) + "-" + suffix + ".json");
            }

            File.WriteAllText(outputFilePath, outputJson);

            Console.WriteLine("done.");
        }

        static void ShowUsage()
        {
            string programName = Path.GetFileNameWithoutExtension(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
            Console.WriteLine($"Usage: {programName} <input.json> [-o <output.json>]");
            Console.WriteLine("  <input.json>    The input JSON file.");
            Console.WriteLine("  -o <output.json>  (Optional) The output JSON file.");
        }

        static void SheetCalcTime(dynamic node)
        {
            InitializeParent(node, null);

            void InitializeTotalTimeNode(dynamic n)
            {
                if (IsLeaf(n)) return;
                var time = GetTime(n);
                if (time != null)
                {
                    n.affectNodes = new List<dynamic>();
                    n.exclusionTime = 0;
                }
            }

            void Pre(dynamic n)
            {
                if (IsLeaf(n))
                {
                    string result = null;
                    object resultValue = null; // 初期化
                    if (n.initialValues != null && ((IDictionary<string, object>)n.initialValues).TryGetValue("result", out resultValue))
                    {
                        result = resultValue as string;
                    }

                    if (!string.IsNullOrEmpty(result) && result.StartsWith("-"))
                    {
                        return;
                    }
                }

                double? time = GetTime(n);
                var parent = n.parent;
                while (parent != null)
                {
                    int? totalTime = GetTime(parent);
                    if (totalTime != null)
                    {
                        if (time == null)
                        {
                            if (IsLeaf(n))
                            {
                                parent.affectNodes.Add(n);
                            }
                        }
                        else
                        {
                            parent.exclusionTime += time.Value;
                        }
                        return;
                    }

                    if (IsLeaf(n) && time == null)
                    {
                        int? defaultTime = GetDefaultTime(parent);
                        if (defaultTime != null)
                        {
                            SetEstimatedTime(n, defaultTime.Value);
                            time = defaultTime;
                        }
                    }

                    parent = parent.parent;
                }
            }

            void AffectTime(dynamic n)
            {
                int? time = GetTime(n);
                if (time == null) return;

                if (IsLeaf(n))
                {
                    SetEstimatedTime(n, time.Value);
                    return;
                }

                int adjustedTime = Math.Max(0, time.Value - (int)n.exclusionTime);
                int leafTime = adjustedTime / n.affectNodes.Count;
                int remain = adjustedTime % n.affectNodes.Count;

                foreach (var affectNode in n.affectNodes)
                {
                    int actualLeafTime = leafTime;
                    if (remain-- > 0)
                    {
                        actualLeafTime++;
                    }
                    SetEstimatedTime(affectNode, actualLeafTime);
                }
            }

            ForAllNodesRecurse(node, null, 0, new Action<dynamic>(InitializeTotalTimeNode));
            ForAllNodesRecurse(node, null, 0, new Action<dynamic>(Pre));
            ForAllNodesRecurse(node, null, 0, new Action<dynamic>(AffectTime));

            DeletePropertyForAllNodes(node, "affectNodes");
            DeletePropertyForAllNodes(node, "exclusionTime");
            DeletePropertyForAllNodes(node, "parent");

            DeleteVariablesForAllNodes(node, new List<string> { "default_time", "time" });
        }

        static void InitializeParent(dynamic node, dynamic parent)
        {
            node.parent = parent;
            foreach (var child in node.children)
            {
                InitializeParent(child, node);
            }
        }

        static void DeletePropertyForAllNodes(dynamic node, string name)
        {
            ForAllNodesRecurse(node, null, 0, new Action<dynamic>(n =>
            {
                if (((IDictionary<string, object>)n).ContainsKey(name))
                {
                    ((IDictionary<string, object>)n).Remove(name);
                }
            }));
        }

        static void DeleteVariablesForAllNodes(dynamic node, List<string> names)
        {
            ForAllNodesRecurse(node, null, 0, new Action<dynamic>(n =>
            {
                foreach (var name in names)
                {
                    if (((IDictionary<string, object>)n.variables).ContainsKey(name))
                    {
                        ((IDictionary<string, object>)n.variables).Remove(name);
                    }
                }
            }));
        }

        static void SetEstimatedTime(dynamic node, int time)
        {
            if (node.initialValues == null)
            {
                node.initialValues = new ExpandoObject();
            }
            node.initialValues.estimated_time = time;
        }

        static int? GetNumber(dynamic node, string name)
        {
            if (((IDictionary<string, object>)node.variables).TryGetValue(name, out var value))
            {
                if (value is string stringValue && int.TryParse(stringValue, out var number))
                {
                    return number;
                }
                return value as int?;
            }
            return null;
        }

        static int? GetTime(dynamic node)
        {
            return GetNumber(node, "time");
        }

        static int? GetDefaultTime(dynamic node)
        {
            return GetNumber(node, "default_time");
        }

        static bool IsLeaf(dynamic node)
        {
            return node.children == null || node.children.Count == 0;
        }

        static void ForAllNodesRecurse(dynamic node, dynamic parent, int index, Action<dynamic> preChildren, Action<dynamic> postChildren = null)
        {
            preChildren(node);

            for (int i = 0; i < node.children.Count; i++)
            {
                if (node.children[i] == null) continue;
                ForAllNodesRecurse(node.children[i], node, i, preChildren, postChildren);
            }

            postChildren?.Invoke(node);
        }
    }
}
