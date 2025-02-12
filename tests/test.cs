using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using GitHub.TreeSitter;

namespace TreeSitterTest
{
    public class TestTreeSitterCPP
    {
        // Tree-sitter C++ language handle
        public static TSLanguage lang = new TSLanguage(tree_sitter_cpp());

        [DllImport("tree-sitter-cpp.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr tree_sitter_cpp();

        /// <summary>
        /// Recursively walks the parse tree. Whenever we find a "function_definition",
        /// we grab the entire function text and store it in the dictionary under its function name.
        /// </summary>
        /// <param name="cursor">The cursor we will walk.</param>
        /// <param name="fileText">Full text of the source file.</param>
        /// <param name="functionMap">Dictionary of function name → entire function text.</param>
        private static void CollectFunctionsRecursive(
            TSCursor cursor,
            string fileText,
            Dictionary<string, string> functionMap)
        {
            // Descend to children first
            if (cursor.goto_first_child())
            {
                do
                {
                    using var childCopy = cursor.copy();
                    CollectFunctionsRecursive(childCopy, fileText, functionMap);
                } while (cursor.goto_next_sibling());

                // Return back up after children
                cursor.goto_parent();
            }

            // Now "visit" this node
            var node = cursor.current_node();
            string nodeType = node.type();

            // We specifically check for function_definition nodes
            if (nodeType == "function_definition")
            {
                // The entire text of this function
                int start = (int)node.start_offset();
                int end   = (int)node.end_offset();
                string functionText = fileText.Substring(start, end - start);

                // Extract the function name from within the function_definition
                // (We'll define a helper for that below.)
                string functionName = ExtractFunctionName(node, fileText);

                // Store in map if not already present
                // (If the same function name appears multiple times, you might want
                //  to handle that differently—this is a simple approach.)
                if (!string.IsNullOrEmpty(functionName) && !functionMap.ContainsKey(functionName))
                {
                    functionMap[functionName] = functionText;
                }
            }
        }

        /// <summary>
        /// Uses a small helper method to find the "identifier" child in a function_definition.
        /// We try to walk the subtree to find function_declarator → identifier, etc.
        /// </summary>
        private static string ExtractFunctionName(TSNode functionDefinitionNode, string fileText)
        {
            var child_count = functionDefinitionNode.child_count();
            for (uint i = 0; i < child_count; i++)
            {
                var childNode = functionDefinitionNode.child(i);
                var childType = childNode.type();

                if (childType == "function_declarator") {
                    return SubstringForNode(childNode, fileText);
                }
            }

            return "";
        }

        private static string SubstringForNode(TSNode node, string text)
        {
            int start = (int)node.start_offset();
            int end   = (int)node.end_offset();
            return text.Substring(start, end - start);
        }

        /// <summary>
        /// Parse one C++ file, returning a dictionary of function-name → function-body.
        /// </summary>
        private static Dictionary<string, string> ParseFunctionsFromFile(string filePath)
        {
            var fileText = File.ReadAllText(filePath);

            using var parser = new TSParser();
            parser.set_language(lang);

            using var tree = parser.parse_string(null, fileText);
            if (tree == null)
            {
                Console.Error.WriteLine($"Could not parse {filePath}");
                return new Dictionary<string, string>();
            }

            var functionMap = new Dictionary<string, string>();

            // Create a cursor from the root node of the parse tree
            using var cursor = new TSCursor(tree.root_node(), lang);

            CollectFunctionsRecursive(cursor, fileText, functionMap);
            return functionMap;
        }

        /// <summary>
        /// Main entry point - Expects:
        ///   -current path\to\current_file.cpp
        ///   -changed path\to\changed_file.cpp
        /// </summary>
        public static void Main(string[] args)
        {
            // Simple argument check
            // Expect: 4 arguments: -current file1 -changed file2
            if (args.Length != 4)
            {
                Console.WriteLine("Usage: MyExe -current <file.cpp> -changed <file.cpp>");
                return;
            }

            string currentPath = null;
            string changedPath = null;

            // Simple parse of CLI
            for (int i = 0; i < args.Length; i += 2)
            {
                string flag = args[i];
                string path = args[i + 1];
                if (flag == "-current")
                {
                    currentPath = path;
                }
                else if (flag == "-changed")
                {
                    changedPath = path;
                }
            }

            // Validate we have both paths
            if (string.IsNullOrEmpty(currentPath) || string.IsNullOrEmpty(changedPath))
            {
                Console.WriteLine("Usage: MyExe -current <file.cpp> -changed <file.cpp>");
                return;
            }

            // Parse both files
            var currentFunctions = ParseFunctionsFromFile(currentPath);
            var changedFunctions = ParseFunctionsFromFile(changedPath);

            // Compare them
            CompareAndPrintDifferences(currentFunctions, changedFunctions);
        }

        /// <summary>
        /// Compares two dictionaries:
        ///  1) If a function is present in both but has different bodies → "Changed:"
        ///  2) If a function is only in changed → "New:"
        ///  3) If a function is only in current → "Removed:"
        /// </summary>
        private static void CompareAndPrintDifferences(
            Dictionary<string, string> currentMap,
            Dictionary<string, string> changedMap)
        {
            // 1) Check all functions in "current" to see if they are changed or removed
            foreach (var kvp in currentMap)
            {
                string funcName = kvp.Key;
                string currentBody = kvp.Value;

                if (changedMap.TryGetValue(funcName, out string changedBody))
                {
                    // Function exists in both. Compare bodies
                    if (currentBody != changedBody)
                    {
                        Console.WriteLine($"\nCHANGED: {funcName}");
                        Console.WriteLine("----- CURRENT -----");
                        Console.WriteLine(currentBody);
                        Console.WriteLine("----- CHANGED -----");
                        Console.WriteLine(changedBody);
                        Console.WriteLine("-------------------\n");
                    }
                }
                else
                {
                    // Only in current
                    Console.WriteLine($"\nREMOVED: {funcName}");
                    Console.WriteLine("----- CURRENT -----");
                    Console.WriteLine(currentBody);
                    Console.WriteLine("-------------------\n");
                }
            }

            // 2) Check for functions that are only in "changed"
            foreach (var kvp in changedMap)
            {
                string funcName = kvp.Key;
                string changedBody = kvp.Value;

                if (!currentMap.ContainsKey(funcName))
                {
                    Console.WriteLine($"\nNEW: {funcName}");
                    Console.WriteLine("----- CHANGED -----");
                    Console.WriteLine(changedBody);
                    Console.WriteLine("-------------------\n");
                }
            }
        }
    }
}
