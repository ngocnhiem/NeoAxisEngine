// Copyright (C) NeoAxis Group Ltd. 8 Copthall, Roseau Valley, 00152 Commonwealth of Dominica.
using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using NeoAxis;

namespace NeoAxis.Networking
{
	public static class NetworkCommonSettings
	{
		public static string GeneralManagerDomain { get; set; } = "cloud.neoaxis.com";
		public static string GeneralManagerAddress { get; set; } = "195.200.29.132";
		public static int GeneralManagerHttpsPort = 44318; //internal const int GeneralManagerHttpPort = 44317;
		public static int GeneralManagerExecuteCommandTimeout = 120000;// 30000;

		public static bool NetworkLogging { get; set; }

		//public const int P2PDefaultSeederPort = 56571;
	}
}