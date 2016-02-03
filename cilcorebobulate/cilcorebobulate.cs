﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using Mono.Cecil;

class MainClass
{
	static bool Verbose = false;

	static int Usage ()
	{
		Log ("cilcorebobulate [-c dir] [-v] from to");
		return 1;
	}

	static void Log (string text, params object[] list)
	{
		if (Verbose)
			Console.WriteLine (text, list);
	}

	public static int Main (string[] args)
	{
		string coreclr_dir = ".";
		string input_assembly_name = null;
		string output_assembly_name = null;
		
		for (int i = 0; i < args.Length; i++) {
			switch (args [i]) {
			case "--coreclr":
			case "-c":
				coreclr_dir = args [++i];
				break;
			case "-v":
				Verbose = true;
				break;
			case "--":
				i++;
				goto default;
			default:	
				input_assembly_name = args [i++];
				output_assembly_name = args [i];
				if (i != args.Length - 1)
					return Usage ();
				break;
			}
		}

		
		Log ("Converting {0} => {1}", input_assembly_name, output_assembly_name);

		if (input_assembly_name == null || output_assembly_name == null)
			return Usage ();

		// Step 1.
		// Process all CoreCLR assemblies, build a type=>assembly mapping.

		
		Log ("Scanning the CoreCLR libraries at {0}...", coreclr_dir);

		var coreclr_dlls = Directory.GetFiles (coreclr_dir, "System.*.dll");

		var type2as = new Dictionary<string, string> ();
		var netcore_assemblies = new Dictionary<string, AssemblyNameReference> ();

		foreach (var netcore_dll_path in coreclr_dlls) {
//			if (coreclr_dll.IndexOf (".Private.") != -1)
//				continue;
					
			var netcore_dll_name = Path.GetFileName (netcore_dll_path);
			netcore_dll_name = netcore_dll_name.Substring (0, netcore_dll_name.Length - 4);

			
			Log ("  A {0}", netcore_dll_name);

			var assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly (netcore_dll_path);
			netcore_assemblies [netcore_dll_name] = new Mono.Cecil.AssemblyNameReference (netcore_dll_name, null);

			foreach (var module in assembly.Modules) {
				
				Log ("    M {0}", module.Name);
				
				foreach (var type in module.ExportedTypes.Select(et => et.FullName).Concat (module.Types.Select(t => t.FullName))) {
					
					
					Log ("      T {0}", type);
					
					if (type2as.ContainsKey (type)) {
						Log ("      Warning: Type {0} already exists in another assembly {1}", type, type2as [type]);
						// Seems like a fairy common thing for whatever reason. Intentional or not?
						// threw new Exception ();
					}
					type2as [type] = netcore_dll_name;
				}
			}
		}

		// Step 2.
		// Scan the input assembly, replace all references to the appropriate .NET Core libs.

		Log ("\n\nA {0}", input_assembly_name);

		var input_assembly = Mono.Cecil.AssemblyDefinition.ReadAssembly (input_assembly_name);

		Log ("Processing the modules...");

		foreach (var module in input_assembly.Modules) {
			{
				Log ("  M {0}", module.Name);
				Log ("  Replacing assembly references in types...");
			}

			foreach (var type in module.GetTypeReferences ()) {
				
				if (Verbose) Console.Write ("    T {0} S {1} => ", type.FullName, type.Scope.Name);

				if (type.Scope.MetadataScopeType != MetadataScopeType.AssemblyNameReference) {
					Log ("[unexpected scope, skipping]");
					continue;
				}
						
				if (type.FullName.IndexOf ("System.") == -1) {
					Log ("[not in mscorlib, skipping]");
					continue;
				}

				// Replace the reference & update scope.
				try {
					var netcore_assembly = netcore_assemblies [type2as [type.FullName]];

					if (!module.AssemblyReferences.Contains (netcore_assembly))
						module.AssemblyReferences.Add (netcore_assembly);
					type.Scope = netcore_assembly;

					Log ("A {0}", type2as [type.FullName] + ".dll");
				}
				catch (KeyNotFoundException) {
					Console.Error.WriteLine ("Error: Type {0} not found in any assembly provided.", type.FullName);
					Console.Error.WriteLine ("       Perhaps you forgot to get the correct NuGet package or it's not supported by .NET Core?");
					Console.Error.WriteLine ("       See https://dotnet.github.io/api/{0}.html for more information on the assembly.", type.FullName);
					return 3;
				}
					

			}

			// Remove default mscorlib
			try {
				module.AssemblyReferences.Remove (module.AssemblyReferences.SingleOrDefault (a => a.Name == "mscorlib"));
			} catch (System.InvalidOperationException) {
				// Not present
			}
				
		}

		input_assembly.Write (output_assembly_name);

		Log ("Success.");

		return 0;
	}

}
