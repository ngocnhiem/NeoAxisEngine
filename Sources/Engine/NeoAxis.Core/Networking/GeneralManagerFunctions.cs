// Copyright (C) NeoAxis Group Ltd. 8 Copthall, Roseau Valley, 00152 Commonwealth of Dominica.
using Internal.LiteDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NeoAxis.Networking
{
	public static class GeneralManagerFunctions
	{
		///////////////////////////////////////////////

		public class SimpleResult
		{
			public string Error;
		}

		///////////////////////////////////////////////

		public static string GetHttpURL()
		{
			var prefix = "https://";
			var address = NetworkCommonSettings.GeneralManagerDomain;
			var port = NetworkCommonSettings.GeneralManagerHttpsPort;
			return $"{prefix}{address}:{port}";
		}

		///////////////////////////////////////////////

		public class AccessGetUserByVerificationCodeResult
		{
			public long UserID;
			public string Username;
			public string Error;
		}

		public static async Task<AccessGetUserByVerificationCodeResult> AccessGetUserByVerificationCodeAsync( long projectID, CloudUserRole userRole, string verificationCode, CancellationToken cancellationToken = default )
		{
			var command = new GeneralManagerExecuteCommand();
			command.FunctionName = "api/v1/access/get_user_by_verification_code";
			command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

			var block = new TextBlock();
			block.SetAttribute( "ProjectID", projectID.ToString() );
			block.SetAttribute( "UserRole", userRole.ToString() );
			block.SetAttribute( "VerificationCode", verificationCode );
			command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

			var requestResult = await command.ExecuteAsync( cancellationToken );
			if( !string.IsNullOrEmpty( requestResult.Error ) )
				return new AccessGetUserByVerificationCodeResult() { Error = requestResult.Error };

			var requestBlock = requestResult.Data;
			long.TryParse( requestBlock.GetAttribute( "UserID" ), out var userID );
			return new AccessGetUserByVerificationCodeResult()
			{
				UserID = userID,
				Username = requestBlock.GetAttribute( "Username" ),
			};
		}

		///////////////////////////////////////////////

		public class AccessRequestServiceResult
		{
			public string ServerAddress;
			public int ServerPort;
			public string VerificationCode;
			public string Error;
		}

		/// <summary>
		/// Requests a service server address and port.
		/// </summary>
		/// <param name="service"></param>
		/// <param name="userRole">Developer, Player</param>
		/// <param name="projectID"></param>
		/// <returns></returns>
		public static async Task<AccessRequestServiceResult> AccessRequestServiceServerAsync( string service, CloudUserRole userRole, long? projectID = null, CancellationToken cancellationToken = default )
		{
			var command = new GeneralManagerExecuteCommand();
			command.FunctionName = "api/v1/access/request_server_access";
			command.RequireUserLogin = true;
			command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

			var block = new TextBlock();
			block.SetAttribute( "Service", service );
			block.SetAttribute( "UserRole", userRole.ToString() );
			if( projectID != null )
				block.SetAttribute( "ProjectID", projectID.Value.ToString() );
			command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

			var requestResult = await command.ExecuteAsync( cancellationToken );
			if( !string.IsNullOrEmpty( requestResult.Error ) )
				return new AccessRequestServiceResult() { Error = requestResult.Error };

			var requestBlock = requestResult.Data;
			return new AccessRequestServiceResult()
			{
				ServerAddress = requestBlock.GetAttribute( "ServerAddress" ),
				ServerPort = int.Parse( requestBlock.GetAttribute( "ServerPort" ) ),
				VerificationCode = requestBlock.GetAttribute( "VerificationCode" ),
			};
		}

		public static async Task<GeneralManagerExecuteCommand.ResultClass> ProjectUpdateConfigurationFromServerAsync( /*long projectID*/ TextBlock block, string serverCheckCode = null, CancellationToken cancellationToken = default )
		{
			var command = new GeneralManagerExecuteCommand();
			command.FunctionName = "api/v1/project/update_configuration_from_server";
			if( !string.IsNullOrEmpty( serverCheckCode ) )
				command.ServerCheckCode = serverCheckCode;
			else
				command.RequireUserLogin = true;
			command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

			command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

			return await command.ExecuteAsync( cancellationToken );
		}

		public class UserTransactionItem
		{
			public long Id;
			public DateTime CreationTime;

			public long SenderID;
			public long RecepientID;

			//AdminChangeBalance: change balance by admin.
			//TopUp: top up balance. NodeId, ServiceComission, NodeComission are used.
			//Withdraw: withdraw balance. NodeId, ServiceComission, NodeComission are used.
			//PaymentServer: оплата сервера (за аренду, за трафик, за хранение). NodeID is used. ProjectId is used (first project of the server). PaymentName: "Basic", "Additional traffic".
			//PaymentStorage: оплата хранилища (за трафик, за хранение).
			//ProjectEntryFee: за вход в проект (купить игру). ProjectID is used.
			//ProjectInAppPayment: in-app payments. purchase when amount > 0, withdraw when amount < 0. ProjectID, PaymentName are used.
			public string Type;

			public string Currency;
			public double Amount;

			public long ProjectID;
			public string ProjectIDs;
			public string ServerAddress;
			public string PaymentName; //"Main server", "Additional server", "Main server traffic", "Additional server traffic" or any name of in-app purchase

			//dynamic data
			public string Status; // "Pending", "Completed" ////, "Failed" delete when failed
			public string Description; //Any description, may be used by the node.
			public bool ProcessedByProject; //in app purchases
			public string ProcessedByProjectDetails; //in app purchases
		}

		public class UserTransactionsGetResult
		{
			public TextBlock Data;
			public List<UserTransactionItem> Transactions;
			public string Error;
		}

		static string ParseUserTransaction( TextBlock block, UserTransactionItem tx )
		{
			if( !long.TryParse( block.GetAttribute( "Id" ), out tx.Id ) )
				return "Can't parse Id.";

			if( long.TryParse( block.GetAttribute( "CreationTime" ), out var creationTimeTicks ) )
				tx.CreationTime = new DateTime( creationTimeTicks );

			if( block.AttributeExists( "SenderID" ) )
				long.TryParse( block.GetAttribute( "SenderID" ), out tx.SenderID );
			if( block.AttributeExists( "RecepientID" ) )
				long.TryParse( block.GetAttribute( "RecepientID" ), out tx.RecepientID );

			tx.Type = block.GetAttribute( "Type" );
			tx.Currency = block.GetAttribute( "Currency" );
			double.TryParse( block.GetAttribute( "Amount" ), out tx.Amount );

			if( block.AttributeExists( "ProjectID" ) )
				long.TryParse( block.GetAttribute( "ProjectID" ), out tx.ProjectID );
			if( block.AttributeExists( "ProjectIDs" ) )
				tx.ProjectIDs = block.GetAttribute( "ProjectIDs" );
			if( block.AttributeExists( "ServerAddress" ) )
				tx.ServerAddress = block.GetAttribute( "ServerAddress" );
			if( block.AttributeExists( "PaymentName" ) )
				tx.PaymentName = block.GetAttribute( "PaymentName" );

			if( block.AttributeExists( "Status" ) )
				tx.Status = block.GetAttribute( "Status" );
			if( block.AttributeExists( "Description" ) )
				tx.Description = block.GetAttribute( "Description" );
			if( block.AttributeExists( "ProcessedByProject" ) )
				bool.TryParse( block.GetAttribute( "ProcessedByProject" ), out tx.ProcessedByProject );
			if( block.AttributeExists( "ProcessedByProjectDetails" ) )
				tx.ProcessedByProjectDetails = block.GetAttribute( "ProcessedByProjectDetails" );

			return null;
		}

		public static async Task<UserTransactionsGetResult> UserTransactionsGetAsync( string type = null, long? projectID = null, string status = "Completed", bool? processedByProject = null, long? transactionID = null, int? getLatest = null, string serverCheckCode = null, CancellationToken cancellationToken = default )
		{
			var command = new GeneralManagerExecuteCommand();
			command.FunctionName = "api/v1/user_transaction/get";
			if( !string.IsNullOrEmpty( serverCheckCode ) )
				command.ServerCheckCode = serverCheckCode;
			else
				command.RequireUserLogin = true;
			command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

			var inputBlock = new TextBlock();
			if( type != null )
				inputBlock.SetAttribute( "Type", type );
			if( projectID != null )
				inputBlock.SetAttribute( "ProjectID", projectID.Value.ToString() );
			if( status != null )
				inputBlock.SetAttribute( "Status", status );
			if( processedByProject != null )
				inputBlock.SetAttribute( "ProcessedByProject", processedByProject.Value.ToString() );
			if( transactionID != null )
				inputBlock.SetAttribute( "TransactionID", transactionID.ToString() );
			if( getLatest != null )
				inputBlock.SetAttribute( "GetLatest", getLatest.Value.ToString() );
			command.ContentData = Encoding.UTF8.GetBytes( inputBlock.DumpToString() );

			var result = await command.ExecuteAsync( cancellationToken );

			if( !string.IsNullOrEmpty( result.Error ) )
				return new UserTransactionsGetResult { Error = result.Error };

			var list = new List<UserTransactionItem>();
			foreach( var block in result.Data.Children )
			{
				if( block.Name == "Item" )
				{
					var tx = new UserTransactionItem();
					var error = ParseUserTransaction( block, tx );
					if( !string.IsNullOrEmpty( error ) )
						return new UserTransactionsGetResult { Error = error };
					list.Add( tx );
				}
			}
			return new UserTransactionsGetResult { Data = result.Data, Transactions = list };
		}

		////public static async Task<UserTransactionsGetResult> UserTransactionsGetProjectInAppPaymentsAsync( long projectID, long? forUserID = null, bool? processedByProject = null, string status = null, string serverCheckCode = null, CancellationToken cancellationToken = default )
		////{
		////	var command = new GeneralManagerExecuteCommand();
		////	command.FunctionName = "api/v1/user_transaction/get_project_in_app_payments";
		////	if( !string.IsNullOrEmpty( serverCheckCode ) )
		////		command.ServerCheckCode = serverCheckCode;
		////	else
		////		command.RequireUserLogin = true;
		////	command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

		////	command.AddParameter( "project", projectID.ToString(), false );
		////	if( forUserID.HasValue )
		////		command.AddParameter( "for_user", forUserID.Value.ToString(), false );
		////	if( processedByProject.HasValue )
		////		command.AddParameter( "processed_by_project", processedByProject.Value.ToString(), false );
		////	if( !string.IsNullOrEmpty( status ) )
		////		command.AddParameter( "status", status, false );

		////	var result = await command.ExecuteAsync( cancellationToken );

		////	if( !string.IsNullOrEmpty( result.Error ) )
		////		return new UserTransactionsGetResult { Error = result.Error };

		////	var list = new List<UserTransactionItem>();
		////	foreach( var block in result.Data.Children )
		////	{
		////		if( block.Name == "Item" )
		////		{
		////			var tx = new UserTransactionItem();
		////			var error = ParseUserTransaction( block, tx );
		////			if( !string.IsNullOrEmpty( error ) )
		////				return new UserTransactionsGetResult { Error = error };
		////			list.Add( tx );
		////		}
		////	}
		////	return new UserTransactionsGetResult { Data = result.Data, Transactions = list };
		////}

		public static async Task<GeneralManagerExecuteCommand.ResultClass> UserTransactionsRequestProjectInAppPurchaseAsync( long projectID, long senderID, string currency, double amount, string description = null, string serverCheckCode = null, CancellationToken cancellationToken = default )
		{
			var command = new GeneralManagerExecuteCommand();
			command.FunctionName = "api/v1/user_transaction/request_project_in_app_purchase";
			if( !string.IsNullOrEmpty( serverCheckCode ) )
				command.ServerCheckCode = serverCheckCode;
			else
				command.RequireUserLogin = true;
			command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

			var inputBlock = new TextBlock();
			inputBlock.SetAttribute( "ProjectID", projectID.ToString() );
			inputBlock.SetAttribute( "SenderID", senderID.ToString() );
			inputBlock.SetAttribute( "Currency", currency );
			inputBlock.SetAttribute( "Amount", amount.ToString() );
			if( description != null )
				inputBlock.SetAttribute( "Description", description );
			command.ContentData = Encoding.UTF8.GetBytes( inputBlock.DumpToString() );

			//command.AddParameter( "project", projectID.ToString(), false );
			//command.AddParameter( "payer", payerID.ToString(), false );
			//command.AddParameter( "currency", currency, false );
			//command.AddParameter( "amount", amount.ToString(), false );
			//if( description != null )
			//	command.AddParameter( "description", description, true );

			return await command.ExecuteAsync( cancellationToken );
		}

		public static async Task<GeneralManagerExecuteCommand.ResultClass> UserTransactionsProjectInAppWithdrawAsync( long projectID, long recepientID, string currency, double amount, string description = null, string serverCheckCode = null, CancellationToken cancellationToken = default )
		{
			var command = new GeneralManagerExecuteCommand();
			command.FunctionName = "api/v1/user_transaction/project_in_app_withdraw";
			if( !string.IsNullOrEmpty( serverCheckCode ) )
				command.ServerCheckCode = serverCheckCode;
			else
				command.RequireUserLogin = true;

			var block = new TextBlock();
			block.SetAttribute( "ProjectID", projectID.ToString() );
			block.SetAttribute( "RecepientID", recepientID.ToString() );
			block.SetAttribute( "Currency", currency );
			block.SetAttribute( "Amount", amount.ToString() );
			if( description != null )
				block.SetAttribute( "Description", description );
			command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

			//command.AddParameter( "project", projectID.ToString(), false );
			//command.AddParameter( "recepient", recepientID.ToString(), false );
			//command.AddParameter( "currency", currency, false );
			//command.AddParameter( "amount", amount.ToString(), false );
			//if( description != null )
			//	command.AddParameter( "description", description, true );

			return await command.ExecuteAsync( cancellationToken );
		}

		//public static async Task<GeneralManagerExecuteCommand.ResultClass> UserTransactionsTransferToAnotherUserAsync( long recepientID, string currency, double amount, string description = null, string serverCheckCode = null, CancellationToken cancellationToken = default )
		//{
		//	var command = new GeneralManagerExecuteCommand();
		//	command.FunctionName = "api/user_transactions_transfer_to_another_user";
		//	if( !string.IsNullOrEmpty( serverCheckCode ) )
		//		command.ServerCheckCode = serverCheckCode;
		//	else
		//		command.RequireUserLogin = true;

		//	command.AddParameter( "recepientID", recepientID.ToString(), false );
		//	command.AddParameter( "currency", currency, false );
		//	command.AddParameter( "amount", amount.ToString(), false );
		//	if( description != null )
		//		command.AddParameter( "description", description, true );

		//	return await command.ExecuteAsync( cancellationToken );
		//}

		public static async Task<GeneralManagerExecuteCommand.ResultClass> UserTransactionsUpdateAsync( /*long projectID,*/ long transactionId, string description = null, bool? processedByProject = null, string processedByProjectDetails = null, string serverCheckCode = null, CancellationToken cancellationToken = default )
		{
			var command = new GeneralManagerExecuteCommand();
			command.FunctionName = "api/v1/user_transaction/update";
			if( !string.IsNullOrEmpty( serverCheckCode ) )
				command.ServerCheckCode = serverCheckCode;
			else
				command.RequireUserLogin = true;
			command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

			var block = new TextBlock();
			//block.SetAttribute( "ProjectID", projectID.ToString() );
			block.SetAttribute( "TransactionID", transactionId.ToString() );
			if( description != null )
				block.SetAttribute( "Description", description );
			if( processedByProject != null )
				block.SetAttribute( "ProcessedByProject", processedByProject.Value.ToString() );
			if( processedByProjectDetails != null )
				block.SetAttribute( "ProcessedByProjectDetails", processedByProjectDetails );
			command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

			//command.AddParameter( "project", projectID.ToString(), false );
			//command.AddParameter( "transaction", transactionId.ToString(), false );
			//if( description != null )
			//	command.AddParameter( "description", description.ToString(), true );
			//if( processedByProject != null )
			//	command.AddParameter( "processed_by_project", processedByProject.Value.ToString(), false );
			//if( processedByProjectDetails != null )
			//	command.AddParameter( "processed_by_project_details", processedByProjectDetails.ToString(), true );

			return await command.ExecuteAsync( cancellationToken );
		}

		///////////////////////////////////////////////

		public class StorageGetInfoResult
		{
			public int Directories;
			public int Files;
			public long Size;
			public string Error;
		}

		public static async Task<StorageGetInfoResult> StorageGetInfoAsync( string storageAccessCode, CancellationToken cancellationToken = default )
		{
			try
			{
				var command = new GeneralManagerExecuteCommand();
				if( !string.IsNullOrEmpty( storageAccessCode ) )
					command.ServerCheckCode = storageAccessCode;
				else
					command.RequireUserLogin = true;
				command.FunctionName = "api/v1/storage/get_info";
				command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

				var block = new TextBlock();
				command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

				var executeResult = await command.ExecuteAsync( cancellationToken );
				if( !string.IsNullOrEmpty( executeResult.Error ) )
					return new StorageGetInfoResult() { Error = executeResult.Error };

				var rootBlock = executeResult.Data;

				var result = new StorageGetInfoResult();
				int.TryParse( rootBlock.GetAttribute( "Directories" ), out result.Directories );
				int.TryParse( rootBlock.GetAttribute( "Files" ), out result.Files );
				long.TryParse( rootBlock.GetAttribute( "Size" ), out result.Size );
				return result;
			}
			catch( Exception e )
			{
				return new StorageGetInfoResult() { Error = e.Message };
			}
		}

		///////////////////////////////////////////////

		public class StorageGetFilesInfoResult
		{
			public Item[] Items;
			public string Error;

			/////////////////////

			public struct Item : IEquatable<Item>
			{
				public bool Exists;
				public long Size;
				public DateTime LastModified;

				//

				public bool Equals( Item other )
				{
					return Exists == other.Exists && Size == other.Size && LastModified == other.LastModified;
				}

				public override bool Equals( object obj )
				{
					return obj is Item item && Equals( item );
				}

				public override int GetHashCode()
				{
					return HashCode.Combine( Exists, Size, LastModified );
				}

				public override string ToString()
				{
					return Exists + " " + Size.ToString();
				}
			}
		}

		public static async Task<StorageGetFilesInfoResult> StorageGetFilesInfoAsync( IList<string> storageFileNames, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			try
			{
				var command = new GeneralManagerExecuteCommand();
				if( !string.IsNullOrEmpty( storageAccessCode ) )
					command.ServerCheckCode = storageAccessCode;
				else
					command.RequireUserLogin = true;
				command.FunctionName = "api/v1/storage/get_files_info";
				command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

				var block = new TextBlock();
				for( int n = 0; n < storageFileNames.Count; n++ )
					block.SetAttribute( $"Name{n}", storageFileNames[ n ].Replace( '\\', '/' ) );
				command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

				var executeResult = await command.ExecuteAsync( cancellationToken );
				if( !string.IsNullOrEmpty( executeResult.Error ) )
					return new StorageGetFilesInfoResult() { Error = executeResult.Error };

				var rootBlock = executeResult.Data;

				var result = new StorageGetFilesInfoResult();
				var resultItems = new List<StorageGetFilesInfoResult.Item>();

				foreach( var childItem in rootBlock.Children )
				{
					if( childItem.Name != "Item" )
						continue;

					var fileItem = new StorageGetFilesInfoResult.Item();
					long.TryParse( childItem.GetAttribute( "Size" ), out fileItem.Size );
					fileItem.Exists = fileItem.Size >= 0;
					if( long.TryParse( childItem.GetAttribute( "LastModified" ), out var lastModifiedTicks ) )
						fileItem.LastModified = new DateTime( lastModifiedTicks );

					//!!!!maybe hash

					resultItems.Add( fileItem );
				}

				if( storageFileNames.Count != resultItems.Count )
					return new StorageGetFilesInfoResult() { Error = "Invalid item count." };

				result.Items = resultItems.ToArray();

				return result;
			}
			catch( Exception e )
			{
				return new StorageGetFilesInfoResult() { Error = e.Message };
			}
		}

		///////////////////////////////////////////////

		public class StorageGetFileInfoResult
		{
			public bool Exists;
			public long Size;
			public DateTime LastModified;
			public string Error;
		}

		public static async Task<StorageGetFileInfoResult> StorageGetFileInfoAsync( string storageFileName, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			try
			{
				var getFilesInfoResult = await StorageGetFilesInfoAsync( new string[] { storageFileName }, storageAccessCode, cancellationToken );
				if( !string.IsNullOrEmpty( getFilesInfoResult.Error ) )
					return new StorageGetFileInfoResult() { Error = getFilesInfoResult.Error };

				var result = new StorageGetFileInfoResult();
				if( getFilesInfoResult.Items.Length == 1 )
				{
					var item = getFilesInfoResult.Items[ 0 ];
					result.Exists = item.Exists;
					result.Size = item.Size;
					result.LastModified = item.LastModified;
				}

				return result;
			}
			catch( Exception e )
			{
				return new StorageGetFileInfoResult() { Error = e.Message };
			}
		}

		///////////////////////////////////////////////

		public class StorageGetDirectoryInfoResult
		{
			public bool Exists;
			public Item[] Items;
			public string Error;

			/////////////////////

			public struct Item : IEquatable<Item>
			{
				public string Name;
				public long Size;
				public DateTime LastModified;
				public bool IsDirectory;

				//

				public bool Equals( Item other )
				{
					return Name == other.Name && Size == other.Size && LastModified == other.LastModified && IsDirectory == other.IsDirectory;
				}

				public override bool Equals( object obj )
				{
					return obj is Item item && Equals( item );
				}

				public override int GetHashCode()
				{
					return HashCode.Combine( Name, Size, LastModified, IsDirectory );
				}

				public override string ToString()
				{
					return Name + " " + Size.ToString();
				}
			}
		}

		public static async Task<StorageGetDirectoryInfoResult> StorageGetDirectoryInfoAsync( string storageFileName, string searchPattern, SearchOption searchOption, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			try
			{
				var command = new GeneralManagerExecuteCommand();
				if( !string.IsNullOrEmpty( storageAccessCode ) )
					command.ServerCheckCode = storageAccessCode;
				else
					command.RequireUserLogin = true;
				command.FunctionName = "api/v1/storage/get_directory_info";
				command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

				var block = new TextBlock();
				block.SetAttribute( "DirectoryName", storageFileName.Replace( '\\', '/' ) );
				if( searchPattern != null )
					block.SetAttribute( "SearchPattern", searchPattern );
				if( searchOption != SearchOption.TopDirectoryOnly )
					block.SetAttribute( "SearchOption", searchOption.ToString() );
				command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

				var executeResult = await command.ExecuteAsync( cancellationToken );
				if( !string.IsNullOrEmpty( executeResult.Error ) )
					return new StorageGetDirectoryInfoResult() { Error = executeResult.Error };

				var rootBlock = executeResult.Data;

				var result = new StorageGetDirectoryInfoResult();
				bool.TryParse( rootBlock.GetAttribute( "Exists" ), out result.Exists );
				var resultItems = new List<StorageGetDirectoryInfoResult.Item>();

				foreach( var childItem in rootBlock.Children )
				{
					if( childItem.Name != "Item" )
						continue;

					var fileItem = new StorageGetDirectoryInfoResult.Item();
					fileItem.Name = childItem.GetAttribute( "Name" );
					long.TryParse( childItem.GetAttribute( "Size" ), out fileItem.Size );
					if( long.TryParse( childItem.GetAttribute( "LastModified" ), out var lastModifiedTicks ) )
						fileItem.LastModified = new DateTime( lastModifiedTicks );
					bool.TryParse( childItem.GetAttribute( "IsDirectory" ), out fileItem.IsDirectory );

					resultItems.Add( fileItem );
				}

				result.Items = resultItems.ToArray();

				return result;
			}
			catch( Exception e )
			{
				return new StorageGetDirectoryInfoResult() { Error = e.Message };
			}
		}

		public static async Task<SimpleResult> StorageCreateDirectoriesAsync( string[] directoryNames, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			try
			{
				var command = new GeneralManagerExecuteCommand();
				if( !string.IsNullOrEmpty( storageAccessCode ) )
					command.ServerCheckCode = storageAccessCode;
				else
					command.RequireUserLogin = true;
				command.FunctionName = "api/v1/storage/create_directories";
				command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

				var block = new TextBlock();
				for( int n = 0; n < directoryNames.Length; n++ )
					block.SetAttribute( $"DirectoryName{n}", directoryNames[ n ].Replace( '\\', '/' ) );
				command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

				var executeResult = await command.ExecuteAsync( cancellationToken );
				if( !string.IsNullOrEmpty( executeResult.Error ) )
					return new SimpleResult() { Error = executeResult.Error };

				return new SimpleResult();
			}
			catch( Exception e )
			{
				return new SimpleResult() { Error = e.Message };
			}
		}

		public static async Task<SimpleResult> StorageCreateDirectoryAsync( string directoryName, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			return await StorageCreateDirectoriesAsync( new string[] { directoryName }, storageAccessCode, cancellationToken );
		}

		///////////////////////////////////////////////

		public struct DeleteObjectsItem
		{
			public string Name;
			public bool IsDirectory;

			public DeleteObjectsItem( string path, bool isDirectory )
			{
				Name = path;
				IsDirectory = isDirectory;
			}
		}

		public static async Task<SimpleResult> StorageDeleteObjectsAsync( IList<DeleteObjectsItem> objects, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			try
			{
				var command = new GeneralManagerExecuteCommand();
				if( !string.IsNullOrEmpty( storageAccessCode ) )
					command.ServerCheckCode = storageAccessCode;
				else
					command.RequireUserLogin = true;
				command.FunctionName = "api/v1/storage/delete_objects";
				command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

				var block = new TextBlock();
				for( int n = 0; n < objects.Count; n++ )
				{
					var obj = objects[ n ];
					block.SetAttribute( $"Name{n}", obj.Name.Replace( '\\', '/' ) );
					if( obj.IsDirectory )
						block.SetAttribute( $"IsDirectory{n}", obj.IsDirectory.ToString() );
				}
				command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

				var executeResult = await command.ExecuteAsync( cancellationToken );
				if( !string.IsNullOrEmpty( executeResult.Error ) )
					return new SimpleResult() { Error = executeResult.Error };

				return new SimpleResult();
			}
			catch( Exception e )
			{
				return new SimpleResult() { Error = e.Message };
			}
		}

		public static async Task<SimpleResult> StorageDeleteDirectoryAsync( string storageDirectory, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			var objects = new DeleteObjectsItem[] { new DeleteObjectsItem( storageDirectory, true ) };
			return await StorageDeleteObjectsAsync( objects, storageAccessCode, cancellationToken );
		}

		public static async Task<SimpleResult> StorageDeleteFilesAsync( string[] storageFileNames, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			var objects = new DeleteObjectsItem[ storageFileNames.Length ];
			for( int n = 0; n < objects.Length; n++ )
				objects[ n ] = new DeleteObjectsItem( storageFileNames[ n ], false );
			return await StorageDeleteObjectsAsync( objects, storageAccessCode, cancellationToken );
		}

		public static async Task<SimpleResult> StorageDeleteFileAsync( string storageFileName, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			var objects = new DeleteObjectsItem[] { new DeleteObjectsItem( storageFileName, false ) };
			return await StorageDeleteObjectsAsync( objects, storageAccessCode, cancellationToken );
		}

		///////////////////////////////////////////////

		public struct CopyObjectsItem
		{
			public string Name;
			public bool IsDirectory;
			public string TargetName;

			public CopyObjectsItem( string path, bool isDirectory, string targetName )
			{
				Name = path;
				IsDirectory = isDirectory;
				TargetName = targetName;
			}
		}

		public static async Task<SimpleResult> StorageCopyObjectsAsync( CopyObjectsItem[] objects, bool move, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			try
			{
				var command = new GeneralManagerExecuteCommand();
				if( !string.IsNullOrEmpty( storageAccessCode ) )
					command.ServerCheckCode = storageAccessCode;
				else
					command.RequireUserLogin = true;
				command.FunctionName = "api/v1/storage/copy_objects";
				command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

				var block = new TextBlock();
				block.SetAttribute( "Move", move.ToString() );
				for( int n = 0; n < objects.Length; n++ )
				{
					var obj = objects[ n ];
					block.SetAttribute( $"Name{n}", obj.Name.Replace( '\\', '/' ) );
					if( obj.IsDirectory )
						block.SetAttribute( $"IsDirectory{n}", obj.IsDirectory.ToString() );
					block.SetAttribute( $"TargetName{n}", obj.TargetName.Replace( '\\', '/' ) );
				}
				command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

				var executeResult = await command.ExecuteAsync( cancellationToken );
				if( !string.IsNullOrEmpty( executeResult.Error ) )
					return new SimpleResult() { Error = executeResult.Error };

				return new SimpleResult();
			}
			catch( Exception e )
			{
				return new SimpleResult() { Error = e.Message };
			}
		}

		///////////////////////////////////////////////

		public class StorageGetContentUrlsResult
		{
			public string[] Urls;
			public string Error;
		}

		public static async Task<StorageGetContentUrlsResult> StorageGetContentUrlsAsync( string[] storageFileNames, bool upload, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			try
			{
				var command = new GeneralManagerExecuteCommand();
				if( !string.IsNullOrEmpty( storageAccessCode ) )
					command.ServerCheckCode = storageAccessCode;
				else
					command.RequireUserLogin = true;
				command.FunctionName = "api/v1/storage/get_content_urls";
				command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

				var block = new TextBlock();
				block.SetAttribute( "Upload", upload.ToString() );
				for( int n = 0; n < storageFileNames.Length; n++ )
					block.SetAttribute( $"Name{n}", storageFileNames[ n ].Replace( '\\', '/' ) );
				command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

				var executeResult = await command.ExecuteAsync( cancellationToken );
				if( !string.IsNullOrEmpty( executeResult.Error ) )
					return new StorageGetContentUrlsResult() { Error = executeResult.Error };

				var urls = new List<string>();
				for( int n = 0; ; n++ )
				{
					if( !executeResult.Data.AttributeExists( $"Url{n}" ) )
						break;
					var code = executeResult.Data.GetAttribute( $"Url{n}" );
					urls.Add( code );
				}

				return new StorageGetContentUrlsResult() { Urls = urls.ToArray() };
			}
			catch( Exception e )
			{
				return new StorageGetContentUrlsResult() { Error = e.Message };
			}
		}

		public class StorageGetContentUrlResult
		{
			public string Url;
			public string Error;
		}

		public static async Task<StorageGetContentUrlResult> StorageGetContentUrlAsync( string storageFileName, bool upload, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			var result = await StorageGetContentUrlsAsync( new string[] { storageFileName }, upload, storageAccessCode, cancellationToken );
			if( !string.IsNullOrEmpty( result.Error ) )
				return new StorageGetContentUrlResult() { Error = result.Error };
			if( result.Urls.Length != 1 )
				return new StorageGetContentUrlResult() { Error = "Invalid url count." };
			return new StorageGetContentUrlResult() { Url = result.Urls[ 0 ] };
		}

		///////////////////////////////////////////////

		public class AdditionalServerGetResult
		{
			public List<AdditionalServerItem> Servers;
			public string Error;

			/////////////////////

			public class AdditionalServerItem
			{
				//common data
				public string Address;
				public string Region;
				public string Configuration;

				//data from the server
				public StatusEnum Status; //public bool Connected;
				public string ServerCheckCode;
				public int CPUUsage;
				public long MemoryInUse;
				public long MemoryCapacity;
				public int GPUUsage;
				public long GPUMemoryInUse;
				public long GPUMemoryCapacity;
				public long DiskInUse;
				public long DiskCapacity;
				public long TrafficOutbound;
				public long TrafficInbound;
				public long TrafficOutboundSpeed;
				public long TrafficInboundSpeed;

				public ProjectItem[] Projects;

				////////////////////

				public enum StatusEnum
				{
					Invalid,
					Creating,
					Connected,
				}

				////////////////////

				public class ProjectItem
				{
					public long ProjectID;
					public string ProcessSummary;
					public int ProcessPort;
				}

				////////////////////

				public int MemoryUsage
				{
					get
					{
						if( MemoryCapacity == 0 )
							return 0;
						return (int)( MemoryInUse * 100 / MemoryCapacity );
					}
				}

				public int GPUMemoryUsage
				{
					get
					{
						if( GPUMemoryCapacity == 0 )
							return 0;
						return (int)( GPUMemoryInUse * 100 / GPUMemoryCapacity );
					}
				}

				public int DiskUsage
				{
					get
					{
						if( DiskCapacity == 0 )
							return 0;
						return (int)( DiskInUse * 100 / DiskCapacity );
					}
				}
			}
		}

		public static async Task<AdditionalServerGetResult> AdditionalServerGetAsync( long projectID, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			try
			{
				var command = new GeneralManagerExecuteCommand();
				if( !string.IsNullOrEmpty( storageAccessCode ) )
					command.ServerCheckCode = storageAccessCode;
				else
					command.RequireUserLogin = true;
				command.FunctionName = "api/v1/additional_server/get";
				command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

				var inputBlock = new TextBlock();
				inputBlock.SetAttribute( "ProjectID", projectID.ToString() );
				command.ContentData = Encoding.UTF8.GetBytes( inputBlock.DumpToString() );

				var executeResult = await command.ExecuteAsync( cancellationToken );
				if( !string.IsNullOrEmpty( executeResult.Error ) )
					return new AdditionalServerGetResult() { Error = executeResult.Error };

				var list = new List<AdditionalServerGetResult.AdditionalServerItem>();
				foreach( var serverBlock in executeResult.Data.Children )
				{
					if( serverBlock.Name == "Item" )
					{
						var server = new AdditionalServerGetResult.AdditionalServerItem();
						server.Address = serverBlock.GetAttribute( "Address" );
						server.Region = serverBlock.GetAttribute( "Region" );
						server.Configuration = serverBlock.GetAttribute( "Configuration" );

						Enum.TryParse( serverBlock.GetAttribute( "Status", "Invalid" ), out server.Status );
						//bool.TryParse( serverBlock.GetAttribute( "Connected", "False" ), out server.Connected );

						server.ServerCheckCode = serverBlock.GetAttribute( "ServerCheckCode", null );
						int.TryParse( serverBlock.GetAttribute( "CPUUsage", "0" ), out server.CPUUsage );
						long.TryParse( serverBlock.GetAttribute( "MemoryInUse", "0" ), out server.MemoryInUse );
						long.TryParse( serverBlock.GetAttribute( "MemoryCapacity", "0" ), out server.MemoryCapacity );
						int.TryParse( serverBlock.GetAttribute( "GPUUsage", "0" ), out server.GPUUsage );
						long.TryParse( serverBlock.GetAttribute( "GPUMemoryInUse", "0" ), out server.GPUMemoryInUse );
						long.TryParse( serverBlock.GetAttribute( "GPUMemoryCapacity", "0" ), out server.GPUMemoryCapacity );
						long.TryParse( serverBlock.GetAttribute( "DiskInUse" ), out server.DiskInUse );
						long.TryParse( serverBlock.GetAttribute( "DiskCapacity" ), out server.DiskCapacity );
						long.TryParse( serverBlock.GetAttribute( "TrafficOutbound", "0" ), out server.TrafficOutbound );
						long.TryParse( serverBlock.GetAttribute( "TrafficInbound", "0" ), out server.TrafficInbound );
						long.TryParse( serverBlock.GetAttribute( "TrafficOutboundSpeed", "0" ), out server.TrafficOutboundSpeed );
						long.TryParse( serverBlock.GetAttribute( "TrafficInboundSpeed", "0" ), out server.TrafficInboundSpeed );

						var projects = new List<AdditionalServerGetResult.AdditionalServerItem.ProjectItem>();
						foreach( var projectBlock in serverBlock.Children )
						{
							if( projectBlock.Name == "Project" )
							{
								var project = new AdditionalServerGetResult.AdditionalServerItem.ProjectItem();
								long.TryParse( projectBlock.GetAttribute( "ProjectID" ), out project.ProjectID );
								project.ProcessSummary = projectBlock.GetAttribute( "ProcessSummary" );
								int.TryParse( projectBlock.GetAttribute( "ProcessPort", "0" ), out project.ProcessPort );
								projects.Add( project );
							}
						}
						server.Projects = projects.ToArray();

						list.Add( server );
					}
				}
				return new AdditionalServerGetResult() { Servers = list };
			}
			catch( Exception e )
			{
				return new AdditionalServerGetResult() { Error = e.Message };
			}
		}

		///////////////////////////////////////////////

		public class AdditionalServerNewResult
		{
			public string ServerAddress;
			public string Error;
		}

		public static async Task<AdditionalServerNewResult> AdditionalServerNewAsync( long projectID, string storageAccessCode, string? region = null, string? configuration = null, CancellationToken cancellationToken = default )
		{
			try
			{
				var command = new GeneralManagerExecuteCommand();
				if( !string.IsNullOrEmpty( storageAccessCode ) )
					command.ServerCheckCode = storageAccessCode;
				else
					command.RequireUserLogin = true;
				command.FunctionName = "api/v1/additional_server/new";
				command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

				var block = new TextBlock();
				block.SetAttribute( "ProjectID", projectID.ToString() );
				if( !string.IsNullOrEmpty( region ) )
					block.SetAttribute( "Region", region );
				if( !string.IsNullOrEmpty( configuration ) )
					block.SetAttribute( "Configuration", configuration );
				command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

				var executeResult = await command.ExecuteAsync( cancellationToken );
				if( !string.IsNullOrEmpty( executeResult.Error ) )
					return new AdditionalServerNewResult() { Error = executeResult.Error };

				var serverAddress = executeResult.Data.GetAttribute( "Address" );
				return new AdditionalServerNewResult() { ServerAddress = serverAddress };
			}
			catch( Exception e )
			{
				return new AdditionalServerNewResult() { Error = e.Message };
			}
		}

		///////////////////////////////////////////////

		public static async Task<SimpleResult> AdditionalServerDeleteAsync( long projectID, string serverAddress, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			try
			{
				var command = new GeneralManagerExecuteCommand();
				if( !string.IsNullOrEmpty( storageAccessCode ) )
					command.ServerCheckCode = storageAccessCode;
				else
					command.RequireUserLogin = true;
				command.FunctionName = "api/v1/additional_server/delete";
				command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

				var block = new TextBlock();
				block.SetAttribute( "ProjectID", projectID.ToString() );
				block.SetAttribute( "Address", serverAddress );
				command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

				var executeResult = await command.ExecuteAsync( cancellationToken );
				if( !string.IsNullOrEmpty( executeResult.Error ) )
					return new SimpleResult() { Error = executeResult.Error };

				return new SimpleResult();
			}
			catch( Exception e )
			{
				return new SimpleResult() { Error = e.Message };
			}
		}

		///////////////////////////////////////////////

		public static async Task<SimpleResult> AdditionalServerDeleteAllAsync( long projectID, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			try
			{
				var command = new GeneralManagerExecuteCommand();
				if( !string.IsNullOrEmpty( storageAccessCode ) )
					command.ServerCheckCode = storageAccessCode;
				else
					command.RequireUserLogin = true;
				command.FunctionName = "api/v1/additional_server/delete_all";
				command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

				var block = new TextBlock();
				block.SetAttribute( "ProjectID", projectID.ToString() );
				command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

				var executeResult = await command.ExecuteAsync( cancellationToken );
				if( !string.IsNullOrEmpty( executeResult.Error ) )
					return new SimpleResult() { Error = executeResult.Error };

				return new SimpleResult();
			}
			catch( Exception e )
			{
				return new SimpleResult() { Error = e.Message };
			}
		}

		///////////////////////////////////////////////

		public static async Task<SimpleResult> AdditionalServerResetAsync( long projectID, string serverAddress, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			try
			{
				var command = new GeneralManagerExecuteCommand();
				if( !string.IsNullOrEmpty( storageAccessCode ) )
					command.ServerCheckCode = storageAccessCode;
				else
					command.RequireUserLogin = true;
				command.FunctionName = "api/v1/additional_server/reset";
				command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

				var block = new TextBlock();
				block.SetAttribute( "ProjectID", projectID.ToString() );
				block.SetAttribute( "Address", serverAddress );
				command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

				var executeResult = await command.ExecuteAsync( cancellationToken );
				if( !string.IsNullOrEmpty( executeResult.Error ) )
					return new SimpleResult() { Error = executeResult.Error };

				return new SimpleResult();
			}
			catch( Exception e )
			{
				return new SimpleResult() { Error = e.Message };
			}
		}

		///////////////////////////////////////////////

		public static async Task<SimpleResult> AdditionalServerResetAllAsync( long projectID, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			try
			{
				var command = new GeneralManagerExecuteCommand();
				if( !string.IsNullOrEmpty( storageAccessCode ) )
					command.ServerCheckCode = storageAccessCode;
				else
					command.RequireUserLogin = true;
				command.FunctionName = "api/v1/additional_server/reset_all";
				command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

				var block = new TextBlock();
				block.SetAttribute( "ProjectID", projectID.ToString() );
				command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

				var executeResult = await command.ExecuteAsync( cancellationToken );
				if( !string.IsNullOrEmpty( executeResult.Error ) )
					return new SimpleResult() { Error = executeResult.Error };

				return new SimpleResult();
			}
			catch( Exception e )
			{
				return new SimpleResult() { Error = e.Message };
			}
		}

		///////////////////////////////////////////////

		public static async Task<SimpleResult> ServerRestartAsync( string address, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			try
			{
				var command = new GeneralManagerExecuteCommand();
				if( !string.IsNullOrEmpty( storageAccessCode ) )
					command.ServerCheckCode = storageAccessCode;
				else
					command.RequireUserLogin = true;
				command.FunctionName = "api/v1/server/restart";
				command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

				var block = new TextBlock();
				block.SetAttribute( "Address", address );
				command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

				var executeResult = await command.ExecuteAsync( cancellationToken );
				if( !string.IsNullOrEmpty( executeResult.Error ) )
					return new SimpleResult() { Error = executeResult.Error };

				return new SimpleResult();
			}
			catch( Exception e )
			{
				return new SimpleResult() { Error = e.Message };
			}
		}

		///////////////////////////////////////////////

		public class ServerUpdateRootPasswordResult
		{
			public string Password;
			public string Error;
		}

		public static async Task<ServerUpdateRootPasswordResult> ServerUpdateRootPasswordAsync( string address, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			try
			{
				var command = new GeneralManagerExecuteCommand();
				if( !string.IsNullOrEmpty( storageAccessCode ) )
					command.ServerCheckCode = storageAccessCode;
				else
					command.RequireUserLogin = true;
				command.FunctionName = "api/v1/server/update_root_password";
				command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;
				command.Timeout = 60000 * 5;

				var block = new TextBlock();
				block.SetAttribute( "Address", address );
				command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

				var executeResult = await command.ExecuteAsync( cancellationToken );
				if( !string.IsNullOrEmpty( executeResult.Error ) )
					return new ServerUpdateRootPasswordResult() { Error = executeResult.Error };

				var password = executeResult.Data.GetAttribute( "Password" );
				return new ServerUpdateRootPasswordResult() { Password = password };
			}
			catch( Exception e )
			{
				return new ServerUpdateRootPasswordResult() { Error = e.Message };
			}
		}

		///////////////////////////////////////////////

		public class ServerGetInfoResult
		{
			public int CPUUsage;
			public long MemoryInUse;
			public long MemoryCapacity;

			public int GPUUsage;
			public long GPUMemoryInUse;
			public long GPUMemoryCapacity;

			public long DiskInUse;
			public long DiskCapacity;

			public long TrafficOutbound;
			public long TrafficInbound;
			public long TrafficOutboundSpeed;
			public long TrafficInboundSpeed;

			//provider info
			public long TrafficOutboundUsed;
			public long TrafficOutboundQuota;
			public long TrafficOutboundBillable;

			public ProjectItem[] Projects;

			public string Error;

			/////////////////////

			public class ProjectItem
			{
				public long ProjectID;
				public string ProcessSummary;
				public int ProcessPort;
			}
		}

		public static async Task<ServerGetInfoResult> ServerGetInfoAsync( string address, string storageAccessCode, CancellationToken cancellationToken = default )
		{
			try
			{
				var command = new GeneralManagerExecuteCommand();
				if( !string.IsNullOrEmpty( storageAccessCode ) )
					command.ServerCheckCode = storageAccessCode;
				else
					command.RequireUserLogin = true;
				command.FunctionName = "api/v1/server/get_info";
				command.RequestMethod = GeneralManagerExecuteCommand.RequestMethodEnum.Post;

				var block = new TextBlock();
				block.SetAttribute( "Address", address );
				command.ContentData = Encoding.UTF8.GetBytes( block.DumpToString() );

				var executeResult = await command.ExecuteAsync( cancellationToken );
				if( !string.IsNullOrEmpty( executeResult.Error ) )
					return new ServerGetInfoResult() { Error = executeResult.Error };

				var result = new ServerGetInfoResult();
				int.TryParse( executeResult.Data.GetAttribute( "CPUUsage", "0" ), out result.CPUUsage );
				long.TryParse( executeResult.Data.GetAttribute( "MemoryInUse", "0" ), out result.MemoryInUse );
				long.TryParse( executeResult.Data.GetAttribute( "MemoryCapacity", "0" ), out result.MemoryCapacity );
				int.TryParse( executeResult.Data.GetAttribute( "GPUUsage", "0" ), out result.GPUUsage );
				long.TryParse( executeResult.Data.GetAttribute( "GPUMemoryInUse", "0" ), out result.GPUMemoryInUse );
				long.TryParse( executeResult.Data.GetAttribute( "GPUMemoryCapacity", "0" ), out result.GPUMemoryCapacity );

				long.TryParse( executeResult.Data.GetAttribute( "DiskInUse", "0" ), out result.DiskInUse );
				long.TryParse( executeResult.Data.GetAttribute( "DiskCapacity", "0" ), out result.DiskCapacity );

				long.TryParse( executeResult.Data.GetAttribute( "TrafficOutbound", "0" ), out result.TrafficOutbound );
				long.TryParse( executeResult.Data.GetAttribute( "TrafficInbound", "0" ), out result.TrafficInbound );
				long.TryParse( executeResult.Data.GetAttribute( "TrafficOutboundSpeed", "0" ), out result.TrafficOutboundSpeed );
				long.TryParse( executeResult.Data.GetAttribute( "TrafficInboundSpeed", "0" ), out result.TrafficInboundSpeed );

				long.TryParse( executeResult.Data.GetAttribute( "TrafficOutboundUsed", "0" ), out result.TrafficOutboundUsed );
				long.TryParse( executeResult.Data.GetAttribute( "TrafficOutboundQuota", "0" ), out result.TrafficOutboundQuota );
				long.TryParse( executeResult.Data.GetAttribute( "TrafficOutboundBillable", "0" ), out result.TrafficOutboundBillable );

				var projects = new List<ServerGetInfoResult.ProjectItem>();
				foreach( var projectBlock in executeResult.Data.Children )
				{
					if( projectBlock.Name == "Project" )
					{
						var project = new ServerGetInfoResult.ProjectItem();
						long.TryParse( projectBlock.GetAttribute( "ProjectID" ), out project.ProjectID );
						project.ProcessSummary = projectBlock.GetAttribute( "ProcessSummary" );
						int.TryParse( projectBlock.GetAttribute( "ProcessPort", "0" ), out project.ProcessPort );
						projects.Add( project );
					}
				}
				result.Projects = projects.ToArray();

				return result;
			}
			catch( Exception e )
			{
				return new ServerGetInfoResult() { Error = e.Message };
			}
		}
	}
}