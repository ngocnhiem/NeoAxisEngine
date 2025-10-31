// Copyright (C) NeoAxis Group Ltd. 8 Copthall, Roseau Valley, 00152 Commonwealth of Dominica.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace NeoAxis.Networking
{
	public static class CloudClientProcessUtility
	{
		static bool? initialized;
		static long projectID;
		static string appDirectory;

		static string loginForSecureMode;
		static string verificationCodeForSecureMode;

		///////////////////////////////////////////////

		public static string CloudDataDirectory
		{
			get { return Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.LocalApplicationData ), EngineInfo.CloudServiceName ); }
		}

		static bool GetProjectAppAndAppDirectoryFromExecutable()
		{
			try
			{
				if( SystemSettings.CurrentPlatform == SystemSettings.Platform.Windows )
				{
					var appsDirectory = Path.Combine( CloudDataDirectory, "Apps" );
					var location = Assembly.GetExecutingAssembly().Location;

					if( location.Contains( appsDirectory ) )
					{
						var subPath = location.Substring( appsDirectory.Length );
						var split = subPath.Split( Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries );

						if( split.Length > 0 && long.TryParse( split[ 0 ], out var projectID2 ) )
						{
							var appDirectory2 = Path.Combine( appsDirectory, projectID2.ToString() );
							if( location.Contains( appDirectory2 ) )
							{
								projectID = projectID2;
								appDirectory = appDirectory2;
							}
						}
					}
				}
				else
				{
					Log.Fatal( "CloudClientProcessUtility: GetProjectAppAndAppDirectoryFromExecutable: No implementation." );
					return false;
				}

				return true;
			}
			catch
			{
				return false;
			}
		}

		static bool GetProjectAppAndAppDirectoryFromCommandLineParameters()
		{
			try
			{
				//get projectID
				if( !SystemSettings.CommandLineParameters.TryGetValue( "-projectID", out var projectIDString ) )
					return false;
				if( !long.TryParse( projectIDString, out var projectID2 ) )
					return false;

				//get appDirectory
				if( !SystemSettings.CommandLineParameters.TryGetValue( "-appDirectory", out var appDirectory2 ) )
					return false;

				//get loginForSecureMode
				if( !SystemSettings.CommandLineParameters.TryGetValue( "-loginForSecureMode", out var loginForSecureMode2 ) )
					return false;

				//get verificationCodeForSecureMode
				if( !SystemSettings.CommandLineParameters.TryGetValue( "-verificationCodeForSecureMode", out var verificationCodeForSecureMode2 ) )
					return false;

				projectID = projectID2;
				appDirectory = appDirectory2;
				loginForSecureMode = loginForSecureMode2;
				verificationCodeForSecureMode = verificationCodeForSecureMode2;

				return true;
			}
			catch
			{
				return false;
			}
		}

		static void Init()
		{
			if( initialized == null )
			{
				if( GetProjectAppAndAppDirectoryFromCommandLineParameters() )
					initialized = true;
				else if( GetProjectAppAndAppDirectoryFromExecutable() )
					initialized = true;
				else
					initialized = false;
			}
		}

		public static long ProjectID
		{
			get
			{
				Init();
				return projectID;
			}
		}

		public static string AppDirectory
		{
			get
			{
				Init();
				return appDirectory;
			}
		}

		public static string LoginForSecureMode
		{
			get
			{
				Init();
				return loginForSecureMode;
			}
		}

		public static string VerificationCodeForSecureMode
		{
			get
			{
				Init();
				return verificationCodeForSecureMode;
			}
		}

		///////////////////////////////////////////////

		//public static class CommandLineParameters
		//{
		//	internal static long projectID;
		//	internal static string appDirectory;

		//	//internal static string projectDirectory;
		//	//internal static string processSettings = "";
		//	//internal static TextBlock processSettingsTextBlock;

		//	//

		//	public static long ProjectID
		//	{
		//		get
		//		{
		//			InitOld();
		//			return projectID;
		//		}
		//	}

		//	public static string AppDirectory
		//	{
		//		get
		//		{
		//			InitOld();
		//			return appDirectory;
		//		}
		//	}

		//	//public static string ProjectDirectory
		//	//{
		//	//	get
		//	//	{
		//	//		Init();
		//	//		return projectDirectory;
		//	//	}
		//	//}

		//	//public static string ProcessSettings
		//	//{
		//	//	get
		//	//	{
		//	//		Init();
		//	//		return processSettings;
		//	//	}
		//	//}

		//	//public static TextBlock ProcessSettingsTextBlock
		//	//{
		//	//	get
		//	//	{
		//	//		if( processSettingsTextBlock == null )
		//	//		{
		//	//			processSettingsTextBlock = TextBlock.Parse( ProcessSettings, out _ );
		//	//			if( processSettingsTextBlock == null )
		//	//				processSettingsTextBlock = new TextBlock();
		//	//		}
		//	//		return processSettingsTextBlock;
		//	//	}
		//	//}
		//}

		////////////////////////////////////////////////

		//static void InitOld()
		//{
		//	if( !initialized )
		//	{
		//		//get projectID
		//		if( !SystemSettings.CommandLineParameters.TryGetValue( "-projectID", out var projectIDString ) )
		//		{
		//			//Logs.Write( "Common", "Init: '-projectID' is not specified." );
		//			//return false;
		//		}
		//		if( !long.TryParse( projectIDString, out CommandLineParameters.projectID ) )
		//		{
		//			//Logs.Write( "Common", "Init: '-projectID' invalid data." );
		//			//return false;
		//		}

		//		//get appDirectory
		//		if( !SystemSettings.CommandLineParameters.TryGetValue( "-appDirectory", out CommandLineParameters.appDirectory ) )
		//		{
		//			//Logs.Write( "Common", "Init: '-projectDirectory' is not specified." );
		//			//return false;
		//		}

		//		////get projectDirectory
		//		//if( !SystemSettings.CommandLineParameters.TryGetValue( "-projectDirectory", out CommandLineParameters.projectDirectory ) )
		//		//{
		//		//	//Logs.Write( "Common", "Init: '-projectDirectory' is not specified." );
		//		//	//return false;
		//		//}

		//		////get processSettings
		//		//if( SystemSettings.CommandLineParameters.TryGetValue( "-processSettings", out var processSettings ) )
		//		//{
		//		//	try
		//		//	{
		//		//		CommandLineParameters.processSettings = Encoding.UTF8.GetString( Convert.FromBase64String( processSettings ) );
		//		//	}
		//		//	catch { }
		//		//}

		//		initialized = true;
		//	}
		//}
	}
}