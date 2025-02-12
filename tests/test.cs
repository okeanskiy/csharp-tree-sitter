using System;
using System.Collections.Generic;
using System.IO;
using GitHub.TreeSitter;
using System.Runtime.InteropServices;

namespace TreeSitterTest
{
    // A class to test the TreeSitter methods
    public class TestTreeSitterCPP
    {
        public static TSLanguage lang = new TSLanguage(tree_sitter_cpp());

        [DllImport("tree-sitter-cpp.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr tree_sitter_cpp();

        public static void PostOrderCursorRecursive(TSCursor cursor, string fileText)
        {
            // If we can go down to the first child, let's iterate that child list
            if (cursor.goto_first_child()) {
                do {
                    using var childCopy = cursor.copy();
                    PostOrderCursorRecursive(childCopy, fileText);
                } while (cursor.goto_next_sibling());
                
                // Once done with children, go back up
                cursor.goto_parent();
            }

            // "Visit" the current node in post-order
            var node = cursor.current_node();
            int so = (int)node.start_offset();
            int eo = (int)node.end_offset();
            var text = fileText.AsSpan(so, eo - so);
            var type = cursor.current_symbol();
            if (type == "function_definition") {
                Console.WriteLine($"Node type is {cursor.current_symbol()}, text is {text}");
            }
        }
        
        public static bool ParseTree(string path, string filetext, TSParser parser)
        {
            parser.set_language(TestTreeSitterCPP.lang);

            using var tree = parser.parse_string(null, filetext);
            if (tree == null) {
                return false;
            }

            using var cursor = new TSCursor(tree.root_node(), lang);

            PostOrderCursorRecursive(cursor, filetext);
            return true;
        }
        
        public static bool TraverseTree(string filename, string filetext)
        {
            using var parser = new TSParser();
            return ParseTree(filename, filetext, parser);
        }

        public static bool PrintTree(List<String> paths)
        {
            bool good = true;
            foreach (var path in paths) {
                var filetext = File.ReadAllText(path);
                if (!TraverseTree(path, filetext)) {
                    good = false;
                }
            }
            return good;
        }
        
        public static void PrintErrorAt(string path, string error, params Object[] args)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            if (Console.CursorLeft != 0) { 
                Console.Error.WriteLine(); 
            }
            
            Console.Error.WriteLine("{0}(): error {1}", path, String.Format(error, args));
            Console.ForegroundColor = Console.ForegroundColor;
        }

        public static List<String> ArgsToPaths(ref int pos, string[] args)
        {
            if (++pos >= args.Length) {
                PrintErrorAt("", "XR0100: No input files to process.");
                return null;
            }

            var files = new List<String>();
            var used = new HashSet<String>();
            var options = new EnumerationOptions();
            options.RecurseSubdirectories = false;
            options.ReturnSpecialDirectories = false;
            options.MatchCasing = MatchCasing.CaseInsensitive;
            options.MatchType = MatchType.Simple;
            options.IgnoreInaccessible = false;

            for (; pos < args.Length; pos++) {
                var arg = args[pos];

                if (arg[0] == '-') {
                    pos--;
                    break;
                }

                if (Directory.Exists(arg)) {
                    PrintErrorAt(arg, "XR0101: Path is a directory, not a file spec.");
                    return null;
                }

                string directory = System.IO.Path.GetDirectoryName(arg);
                string pattern = System.IO.Path.GetFileName(arg);
                if (directory == String.Empty) {
                    directory = ".";
                }
                if (directory == null || String.IsNullOrEmpty(pattern)) {
                    PrintErrorAt(arg, "XR0102: Path is anot a valid file spec.");
                    return null;
                }

                if (!Directory.Exists(directory)) {
                    PrintErrorAt(directory, "XR0103: Couldn't find direvtory.");
                    return null;
                }

                int cnt = 0;
                foreach (var filepath in Directory.EnumerateFiles(directory, pattern, options)) {
                    cnt++;
                    if (!used.Contains(filepath)) {
                        files.Add(filepath);
                        used.Add(filepath);
                    }
                }
            }
            
            return files;
        }
        
        public static void Main(string[] args)
        {
            var files = (List<String>)null;
            int a = 0;
            
            // Check if the args have at least two elements and the first one is "-files"
            if (args.Length < 2 || args[0] != "-files")
            {
                Console.WriteLine("Invalid arguments. Please use -files followed by one or more file paths.");
                return;
            }
            
            if ((files = ArgsToPaths(ref a, args)) != null) {
                PrintTree(files);
            }   
        }
    }
}
