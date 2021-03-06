using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Messaging;
using System.Text;
using System.Text.RegularExpressions;

namespace Liv.CommandlineArguments
{
	public static class ConsoleOptions
	{
		public static Dictionary<string, string> ReadOptions(string[] args)
		{
			// Fix 
			//var lastArg = args.Last();
			//if (!lastArg.StartsWith("\"") && lastArg.EndsWith("\"")) args[args.Length-1] = lastArg.Remove(lastArg.Length - 1);

			var ret = new Dictionary<string, string>();

			string cmdArgs = "";
			for (int i = 0; i < args.Length; i++)
			{
				if (!Regex.IsMatch(args[i], @"^\s*\-")) cmdArgs += " \"" + args[i] + "\"";
				else cmdArgs += " " + args[i];
			}
			//string cmdArgs = String.Join(" ", _Args); //Environment.GetCommandLineArgs());

			var re =
				new Regex(
					"(?:-{1,2}|\\/)(?<name>\\w+)(?:[=:]?|\\s+)(?<value>[^-\\s\"][^\"]*?|\"[^\"]*\")?(?=\\s+[-\\/]|$)",
					RegexOptions.IgnoreCase);
			var x = re.Matches(cmdArgs);

			foreach (Match match in x)
			{
				string name = match.Groups["name"].ToString().ToLower();
				string value = match.Groups["value"].ToString();
				value = Regex.Replace(value, @"^\s*""|""$", "");

				ret.Add(name, value);
			}

			return ret;
		}

		public static T Init<T>(string[] args, bool printHelpIfNeeded = false) where T : BaseOptionsClass, new()
		{
			var arguments = ReadOptions(args);

			var ret = new T();

			var ops = GetOptionsFromType<T>();
			foreach (var op in ops)
			{
				string selectedValue = null;
				foreach (var arg in arguments)
				{
					if (op.ShortName == arg.Key || op.LongName.ToLower() == arg.Key)
					{
						selectedValue = arg.Value;
						break;
					}
				}

				if (op.IsRequired && selectedValue == null && !printHelpIfNeeded)
				{
					throw new ArgumentException(String.Format("Option \"{0}\" (\"{1}\") is required!",
						op.LongName, op.ShortName));
				}

				if (op.IsRequired && selectedValue == "" && op.AssignedProperty.PropertyType != typeof(bool))
				{
					throw new ArgumentException(String.Format("Option \"{0}\" (\"{1}\") is required!",
						op.LongName, op.ShortName));
				}

				var defaultValue = op.DefaultValue ?? op.DefaultValueExtend;

				if (selectedValue == null && defaultValue != null)
				{
					selectedValue = defaultValue;
				}

			    if (op.TrailingSlashFix && selectedValue != null)
			    {
			        if (!selectedValue.EndsWith(@"\")) selectedValue += @"\";
			    }

				try
				{
					if (op.AssignedProperty.PropertyType == typeof (bool) && selectedValue == "")
					{
						op.AssignedProperty.SetValue(ret, Cast(op.AssignedProperty.PropertyType, "true"), null);
					}
					else
					{
						op.AssignedProperty.SetValue(ret, Cast(op.AssignedProperty.PropertyType, selectedValue), null);
					}
				}
				catch (Exception ex)
				{
					if (defaultValue != null)
					{
						Console.WriteLine("* Warning, unable to parse value (\"{2}\") for \"{0}\", using default value \"{1}\"", op.LongName,
							defaultValue, selectedValue);
						Console.WriteLine();
						op.AssignedProperty.SetValue(ret, Cast(op.AssignedProperty.PropertyType, defaultValue), null);
					}
					else
					{
						if (op.IsRequired) throw new ArgumentException("Error: Option \"{0}\" is required and we failed to put value to it", op.LongName);
					}
				}
				op.Value = op.AssignedProperty.GetValue(ret, null)?.ToString();
			}

			ret.Options = ops;

			ret.OriginalParameters = args;

			if (printHelpIfNeeded) PrintHelpIfNeededAndExit<T>(args);

			return ret;
		}

		public static void PrintHelpIfNeededAndExit<T>(string[] args)
		{
			if (args.Length == 0)
			{
				var ops = GetOptionsFromType<T>();
				var hasAtLeastOneRequired = ops.Any(x => x.IsRequired && (x.DefaultValue == null || x.DefaultValueExtend == null));

				if (hasAtLeastOneRequired)
				{
					Console.WriteLine("** Application has at least one required argument, printing help:");

					PrintHelp<T>();
					Environment.Exit(-1);
				}
			}

			var isHasHelpArgument = false;
			foreach (var arg in args)
			{
				isHasHelpArgument = (arg == "/?" || arg == "-?" || arg == "--?" || arg == "--help" || arg == "/help" || arg == "/h");
				if (isHasHelpArgument) break;
			}

			if (isHasHelpArgument)
			{
				PrintHelp<T>();
				Environment.Exit(-1);
			}

		}

		public static object Cast(Type toType, string value)
		{
			return Convert.ChangeType(value, toType);
		}

		public static T Cast<T>(string value)
		{
			return (T)Cast(typeof(T), value); //(T)Convert.ChangeType(value, typeof(T));
		}

		public static string PrintHelp<T>()
		{
			var ops = GetOptionsFromType<T>();

			/*
            Usage: cscs.exe <switch 1> <switch 2> <file> [params] [//x]

            <switch 1>
             /?    - Display help info.
             /e    - Compile script into console application executable.
             /ew   - Compile script into Windows application executable.
            */
			StringBuilder sb = new StringBuilder();
			//sb.Append("Help:" + Environment.NewLine + Environment.NewLine);
			//sb.Append("#########################" + Environment.NewLine + Environment.NewLine);
			sb.Append(Environment.NewLine);

			sb.Append("Usage: " + GetSyntax<T>() + Environment.NewLine);

			sb.Append("Options:" + Environment.NewLine);
			foreach (var op in ops)
			{
                var lineSb = new StringBuilder();

				if (!op.NoShortName) lineSb.Append(" -" + Fill(op.ShortName, 5));
                //else sb.Append("  " + Fill(" ", 5));
                lineSb.Append(" --" + Fill(op.LongName, 25));

				if (op.NoShortName) lineSb.Append("  " + Fill(" ", 5));

			    lineSb.Append(": ");
               
                if (op.DefaultValue != null)
				{
					if (!String.IsNullOrEmpty(op.DefaultValueExtend))
					{
                        lineSb.Append(" (defaultExtended= [YourValue;]" + op.DefaultValueExtend + ") ");
					}
					else
					{
                        lineSb.Append(" (default=" +
						          ((op.Type == typeof (int))
							          ? "(" + op.DefaultValue + ")"
							          : op.DefaultValue) + ") ");
					}
				}

                if (op.IsRequired)
                    lineSb.Append(" *Required");

			    if (op.Description != null)
			    {
			        lineSb.Append(Environment.NewLine).Append(Cropper(op.Description));
			    }

                sb.Append(lineSb).Append(Environment.NewLine);
			}
			sb.Append(Environment.NewLine);
			//sb.Append("#########################" + Environment.NewLine);
			string ret = sb.ToString();
			Console.WriteLine(ret);
			return ret;
		}

	    public static string Cropper(string text, int padding = 8)
	    {
            var ret = new StringBuilder();
	        var tabs = new String('\t', padding/8);

	        while (text.Length > 0)
	        {
	            var l = text.Length;
	            if (l + padding > 80)
	            {
	                l = 80 - padding - 3;
	                l = GetLastSpace(text, l-1)+1;
                }
                ret.Append(tabs).Append("| ").Append(text.Substring(0, l));
	            text = text.Remove(0, l);
	            if (text.Length > 0) ret.AppendLine();
	        }

	        return ret.ToString();
	    }

	    private static int GetLastSpace(string text, int point)
	    {
	        while (text[point] != ' ' && point > 0)
	        {
	            point--;
	        }
	        return point;
	    }

		public static string GetSyntax<T>()
		{
			var ops = GetOptionsFromType<T>();

			/*
            Usage: cscs.exe <switch 1> <switch 2> <file> [params] [//x]

            <switch 1>
             /?    - Display help info.
             /e    - Compile script into console application executable.
             /ew   - Compile script into Windows application executable.
            */
			StringBuilder sb = new StringBuilder();
			sb.Append(Environment.NewLine);
			try
			{
				string filename = Path.GetFileName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);

				sb.Append(filename + " ");
				foreach (var op in ops)
				{
					var x = op.ShortName;
					if (op.AssignedProperty.PropertyType != typeof(bool))
						x += " " + op.AssignedProperty.PropertyType.Name;
					if (op.IsRequired)
					{
						sb.Append("<-" + x + ">");
					}
					else
					{
						sb.Append("[-" + x + "]");
					}

					sb.Append(" ");
				}
				sb.Append(Environment.NewLine);
			}
			catch(Exception ex)
			{
				Console.WriteLine("* Warning: Error while building syntax, ex:{0}", ex.Message);
			}
			
			return sb.ToString();
		}

		public static string PrintArguments(BaseOptionsClass optionsClass, bool writeToConsole = true)
		{
			StringBuilder sb = new StringBuilder();
			sb.Append("| PrintArguments:" + Environment.NewLine);
			foreach (var op in optionsClass.Options)
			{
			    var val = op.AssignedProperty?.GetValue(optionsClass, null) ?? op.Value;
                sb.Append("|\t" + ConsoleOptions.Fill(op.LongName, 25) + " = " + val + Environment.NewLine);
			}
			var ret = sb.ToString();
			if (writeToConsole) Console.WriteLine(ret);
			return ret;
		}

		private static OptionAttribute[] GetOptionsFromType<T>()
		{
			var tmpRet = new List<OptionAttribute>();

			foreach (var p in typeof(T).GetProperties())
			{
				var isOption = false;
				OptionAttribute optionInfo = null;
				foreach (object attr in p.GetCustomAttributes(true))
				{
					optionInfo = attr as OptionAttribute;
					if (optionInfo == null) continue;
					isOption = true;
					break;
				}

				if (!isOption) continue;

				optionInfo.AssignedProperty = p;

				tmpRet.Add(optionInfo);
			}

			var ret = tmpRet.ToArray();
			SetOptionNames(ref ret);

			return ret;
		}

		private static void SetOptionNames(ref OptionAttribute[] ops)
		{
			foreach (var op in ops)
			{
				op.LongName = op.AssignedProperty.Name;

				if (op.ShortName != null) continue;

				// capitals
				string capitals = "";
				foreach (var _char in op.LongName)
				{
					if (char.IsUpper(_char)) capitals += _char;
				}
				op.ShortName = capitals.ToLower();

				bool isCapitalUsed = false;
				foreach (var valueItem2 in ops)
				{
					if (valueItem2 == op) continue;
					if (valueItem2.ShortName == op.ShortName)
					{
						isCapitalUsed = true;
						break;
					}
				}
				if (!isCapitalUsed) continue;

				// letters
				for (int i = 1; i < op.LongName.Length - 1; i++)
				{
					op.ShortName = op.LongName.Substring(0, i);
					bool isUsed = false;
					foreach (var valueItem2 in ops)
					{
						if (valueItem2 == op) continue;
						if (valueItem2.ShortName == op.ShortName)
						{
							isUsed = true;
							break;
						}
					}
					if (!isUsed) break;
				}
			}
		}

		public static string Fill(string text, int maxChars = 30, char fillChar = ' ')
		{
			StringBuilder sb = new StringBuilder(text);

			if (sb.Length >= maxChars) return text;
			for (int i = text.Length - 1; i <= maxChars - sb.Length; i++)
			{
				sb.Append(fillChar);
			}

			return sb.ToString();
		}

		public static void SaveToDisk(BaseOptionsClass optionsClass, string filePath)
		{
			try
			{
				var d = Newtonsoft.Json.JsonConvert.SerializeObject(optionsClass.OriginalParameters);
				File.WriteAllText(filePath, d);
			}
			catch (Exception ex)
			{
				throw new Exception("Failed to save to disk", ex);
			}
		}

		public static T LoadFromDisk<T>(string filePath) where T : BaseOptionsClass, new()
		{
			try
			{
				var savedArgs = Newtonsoft.Json.JsonConvert.DeserializeObject<string[]>(File.ReadAllText(filePath));
				return Init<T>(savedArgs, false);
			}
			catch (Exception ex)
			{
				throw new Exception("Failed to load from disk", ex);
			}
		}
	}
}